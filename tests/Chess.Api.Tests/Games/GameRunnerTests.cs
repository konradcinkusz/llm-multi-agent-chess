using Chess.Analysis;
using Chess.Api.Games;
using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Games;

public class GameRunnerTests
{
    private static readonly PlayerDescriptor RandomPlayer = new("random", "random");
    private static readonly string[] Terminations =
        ["checkmate", "stalemate", "insufficient-material", "fifty-move", "threefold", "adjudicated"];

    [Fact]
    public async Task RandomGamePlaysToATerminalStateAndItsRowsMatchItsEvents()
    {
        await using var harness = new GameHarness(GameHarness.RealPlayers());
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(Assert.Single(events, e => e is GameFinished));
        Assert.Same(finished, events[^1]);
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal(finished.Result, game.Result);
        Assert.Equal(finished.Termination, game.Termination);
        Assert.Contains(game.Termination, Terminations);
        Assert.NotNull(game.StartedAt);
        Assert.NotNull(game.FinishedAt);

        var moves = await harness.LoadMovesAsync(gameId);
        var played = events.OfType<MovePlayed>().ToList();
        Assert.NotEmpty(moves);
        Assert.Equal(moves.Count, played.Count);
        Assert.Equal(moves.Count, game.PlyCount);
        Assert.Equal(moves[^1].FenAfter, game.CurrentFen);
        for (var i = 0; i < moves.Count; i++)
        {
            Assert.Equal(moves[i].Ply, played[i].Ply);
            Assert.Equal(moves[i].San, played[i].San);
            Assert.Equal(moves[i].FenAfter, played[i].Fen);
            Assert.Equal(i % 2 == 0 ? "white" : "black", moves[i].Color);
        }

        // Each side started one move per ply, and the random player never needs a second attempt.
        Assert.Equal(moves.Count, events.OfType<MoveStarted>().Count());
        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.Equal(moves.Count, attempts.Count);
        Assert.All(attempts, a => Assert.True(a.Legal));

        Assert.Contains($"[Result \"{game.Result}\"]", game.Pgn);
        Assert.EndsWith(game.Result + "\n", game.Pgn);
    }

    [Fact]
    public async Task AnLlmThatGivesNoLegalMoveForfeitsAndTheForfeitIsRecorded()
    {
        var gateway = new FakeLlmGateway(seed: 7);
        gateway.Script("fake-a", "Qxf7", "nonsense", "e9");
        await using var harness = new GameHarness(GameHarness.RealPlayers(gateway));
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(new("llm", "fake-a"), RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal("0-1", finished.Result);
        Assert.Equal("forfeit-white", finished.Termination);
        Assert.Equal(3, events.OfType<AttemptRejected>().Count());
        Assert.Empty(events.OfType<MovePlayed>());

        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal("0-1", game.Result);
        Assert.Equal("forfeit-white", game.Termination);
        Assert.Equal(0, game.PlyCount);
        Assert.Empty(await harness.LoadMovesAsync(gameId));
        var attempts = await harness.LoadAttemptsAsync(gameId);
        Assert.Equal([1, 2, 3], attempts.Select(a => a.Number));
        Assert.All(attempts, a => Assert.False(a.Legal));
        Assert.Equal("Qxf7", attempts[0].RawReply);
        Assert.Contains("[Result \"0-1\"]", game.Pgn);
    }

    [Fact]
    public async Task AForfeitInTheMiddleOfAGameKeepsTheMovesBeforeIt()
    {
        var players = new ScriptedPlayerFactory((descriptor, side) => side == Side.White
            ? new RandomChessPlayer(descriptor, 5)
            : new ScriptedPlayer(descriptor, (_, _) => Task.FromResult(new PlayerMove(null, [], Forfeit: true))));
        await using var harness = new GameHarness(players);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal(new GameEnd("1-0", "forfeit-black"), new GameEnd(finished.Result, finished.Termination));
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal(1, game.PlyCount);
        Assert.Equal(["white"], (await harness.LoadMovesAsync(gameId)).Select(m => m.Color));
        Assert.Equal("1-0", game.Result);
        Assert.Equal("forfeit-black", game.Termination);
    }

    [Fact]
    public async Task AbortStopsAGameInProgressAndRecordsItAsAborted()
    {
        var players = new ScriptedPlayerFactory((descriptor, side) => side == Side.White
            ? new ScriptedPlayer(descriptor, WaitForever)
            : new RandomChessPlayer(descriptor, 1));
        await using var harness = new GameHarness(players);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = harness.Watch(gameId, timeout.Token);
        await harness.Runner.EnqueueAsync(gameId, timeout.Token);

        var seen = new List<IGameEvent>();
        var aborted = false;
        await foreach (var e in stream)
        {
            seen.Add(e);
            if (e is MoveStarted && !aborted)
            {
                aborted = harness.Runner.TryAbort(gameId);
            }

            if (e is GameFinished)
            {
                break;
            }
        }

        Assert.True(aborted);
        Assert.False(harness.Runner.TryAbort(gameId));
        var finished = Assert.IsType<GameFinished>(seen[^1]);
        Assert.Equal("*", finished.Result);
        Assert.Equal("aborted", finished.Termination);
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal("aborted", game.Termination);
        Assert.Equal("*", game.Result);
        Assert.NotNull(game.FinishedAt);
        Assert.Empty(await harness.LoadMovesAsync(gameId));
    }

    /// <summary>
    /// GameFinished is published before the runner removes the game's slot. An abort at that moment used to report success
    /// for a game that had already ended; the CI run of #107 caught it in AbortStopsAGameInProgressAndRecordsItAsAborted.
    /// Aborting from inside Publish hits that window every time.
    /// </summary>
    [Fact]
    public async Task AnAbortAtTheMomentTheGameFinishesReportsThatTheGameIsNoLongerRunning()
    {
        bool? abortedAtFinish = null;
        GameHarness? harness = null;
        await using var h = harness = new GameHarness(GameHarness.RealPlayers(), maxPlies: 4, onPublish: e =>
        {
            if (e is GameFinished finished)
            {
                abortedAtFinish = harness!.Runner.TryAbort(finished.GameId);
            }
        });
        await h.StartAsync();
        var gameId = await h.SeedGameAsync(RandomPlayer, RandomPlayer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stream = h.Watch(gameId, timeout.Token);
        await h.Runner.EnqueueAsync(gameId, timeout.Token);

        await foreach (var e in stream)
        {
            if (e is GameFinished)
            {
                break;
            }
        }

        Assert.False(abortedAtFinish);
        Assert.NotEqual("aborted", (await h.LoadGameAsync(gameId)).Termination);
    }

    [Fact]
    public async Task AGameAbortedWhileQueuedIsRecordedWithoutBeingPlayed()
    {
        var players = new ScriptedPlayerFactory((descriptor, side) => side == Side.White
            ? new ScriptedPlayer(descriptor, WaitForever)
            : new RandomChessPlayer(descriptor, 1));
        await using var harness = new GameHarness(players, maxConcurrentGames: 1);
        await harness.StartAsync();
        var running = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);
        var queued = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var runningEvents = harness.Watch(running, timeout.Token);
        await harness.Runner.EnqueueAsync(running, timeout.Token);
        await foreach (var e in runningEvents)
        {
            if (e is MoveStarted)
            {
                break;
            }
        }

        // The single worker is busy with the first game, so the second one only waits in the queue.
        var queuedEvents = harness.Watch(queued, timeout.Token);
        await harness.Runner.EnqueueAsync(queued, timeout.Token);
        Assert.True(harness.Runner.TryAbort(queued));
        Assert.True(harness.Runner.TryAbort(running));

        var seen = new List<IGameEvent>();
        await foreach (var e in queuedEvents)
        {
            seen.Add(e);
            if (e is GameFinished)
            {
                break;
            }
        }

        var finished = Assert.IsType<GameFinished>(Assert.Single(seen));
        Assert.Equal("aborted", finished.Termination);
        Assert.Equal(2, players.Created);
        var game = await harness.LoadGameAsync(queued);
        Assert.Equal("aborted", game.Termination);
        Assert.Null(game.StartedAt);
        Assert.Empty(await harness.LoadMovesAsync(queued));
    }

    [Fact]
    public async Task NoMoreThanMaxConcurrentGamesAreInPlayAtOnce()
    {
        var meter = new ConcurrencyMeter();
        var players = new ScriptedPlayerFactory((descriptor, _) =>
            new MeteredPlayer(new RandomChessPlayer(descriptor, Random.Shared.Next()), meter, TimeSpan.FromMilliseconds(10)));
        await using var harness = new GameHarness(players, maxPlies: 8, maxConcurrentGames: 2);
        await harness.StartAsync();
        var games = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            games.Add(await harness.SeedGameAsync(RandomPlayer, RandomPlayer));
        }

        var results = await Task.WhenAll(games.Select(harness.PlayToEndAsync));

        Assert.All(results, events => Assert.IsType<GameFinished>(events[^1]));
        Assert.InRange(meter.Peak, 1, 2);
    }

    [Fact]
    public async Task AGameThatReachesMaxPliesIsAdjudicatedByTheStockfishVerdict()
    {
        var analyzer = new StubAnalyzer(available: true, centipawns: 350);
        var players = new ScriptedPlayerFactory((descriptor, side) =>
            new RandomChessPlayer(descriptor, side == Side.White ? 11 : 12));
        await using var harness = new GameHarness(players, analyzer, maxPlies: 4);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        var finished = Assert.IsType<GameFinished>(events[^1]);
        Assert.Equal(new GameEnd("1-0", "adjudicated"), new GameEnd(finished.Result, finished.Termination));
        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal(4, game.PlyCount);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("adjudicated", game.Termination);
        var moves = await harness.LoadMovesAsync(gameId);
        Assert.Equal([moves[^1].FenAfter], analyzer.EvaluatedFens);
    }

    [Fact]
    public async Task WithoutStockfishTheMaterialDecidesAtMaxPlies()
    {
        // White has a rook and a queen; black only a king. Two plies cannot end the game or change that.
        const string rookAndQueenUp = "4k3/8/8/8/8/8/8/R3K2Q w - - 0 1";
        await using var harness = new GameHarness(GameHarness.RealPlayers(), maxPlies: 2);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, rookAndQueenUp);

        await harness.PlayToEndAsync(gameId);

        var game = await harness.LoadGameAsync(gameId);
        Assert.Equal(2, game.PlyCount);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("adjudicated", game.Termination);
    }

    [Fact]
    public async Task AGameThatFailsIsRecordedAsAbortedAndTheWorkerGoesOnWithTheNextGame()
    {
        var players = new ScriptedPlayerFactory((descriptor, _) => descriptor.ModelId == "broken"
            ? new ScriptedPlayer(descriptor, (_, _) => throw new InvalidOperationException("boom"))
            : new RandomChessPlayer(descriptor, 1));
        await using var harness = new GameHarness(players, maxConcurrentGames: 1);
        await harness.StartAsync();
        var broken = await harness.SeedGameAsync(new("llm", "broken"), RandomPlayer);
        var healthy = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        var brokenEvents = await harness.PlayToEndAsync(broken);
        var healthyEvents = await harness.PlayToEndAsync(healthy);

        Assert.Equal("aborted", Assert.IsType<GameFinished>(brokenEvents[^1]).Termination);
        var brokenGame = await harness.LoadGameAsync(broken);
        Assert.Equal("*", brokenGame.Result);
        Assert.Equal("aborted", brokenGame.Termination);
        Assert.NotNull(brokenGame.FinishedAt);

        Assert.IsType<GameFinished>(healthyEvents[^1]);
        var healthyGame = await harness.LoadGameAsync(healthy);
        Assert.NotEqual("aborted", healthyGame.Termination);
        Assert.NotEmpty(await harness.LoadMovesAsync(healthy));
    }

    [Fact]
    public async Task AnUnknownGameIsSkippedAndTheWorkerGoesOn()
    {
        await using var harness = new GameHarness(GameHarness.RealPlayers(), maxConcurrentGames: 1);
        await harness.StartAsync();
        await harness.Runner.EnqueueAsync(Guid.NewGuid(), CancellationToken.None);
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);

        var events = await harness.PlayToEndAsync(gameId);

        Assert.IsType<GameFinished>(events[^1]);
    }

    [Fact]
    public async Task EnqueueingAGameTwiceDoesNotPlayItTwice()
    {
        var players = new ScriptedPlayerFactory((descriptor, _) => new RandomChessPlayer(descriptor, 1));
        await using var harness = new GameHarness(players);
        await harness.StartAsync();
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stream = harness.Watch(gameId, timeout.Token);

        await harness.Runner.EnqueueAsync(gameId, timeout.Token);
        await harness.Runner.EnqueueAsync(gameId, timeout.Token);

        var finishes = 0;
        await foreach (var e in stream)
        {
            if (e is GameFinished)
            {
                finishes++;
                break;
            }
        }

        Assert.Equal(1, finishes);
        Assert.Equal(2, players.Created);
    }

    [Fact]
    public async Task QueuedGamesLeftByARestartArePlayedOldestFirstAndAFinishedGameIsUntouched()
    {
        var players = new ScriptedPlayerFactory((descriptor, _) => new RandomChessPlayer(descriptor, Random.Shared.Next()));
        await using var harness = new GameHarness(players, maxPlies: 20, maxConcurrentGames: 1);
        // The newer game is saved first, so only its creation time puts the older one ahead of it.
        var newer = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: GameHarness.CreatedAt.AddMinutes(1));
        var older = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: GameHarness.CreatedAt);
        var finished = await harness.SeedGameAsync(RandomPlayer, RandomPlayer, createdAt: GameHarness.CreatedAt.AddMinutes(2));
        await harness.MarkFinishedAsync(finished, "0-1", "checkmate");
        var finishedBefore = await harness.LoadGameAsync(finished);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var olderEvents = harness.Watch(older, timeout.Token);
        var newerEvents = harness.Watch(newer, timeout.Token);

        await harness.StartAsync();
        harness.FinishMigration();

        Assert.IsType<GameFinished>((await ReadUntilFinishedAsync(olderEvents))[^1]);
        Assert.IsType<GameFinished>((await ReadUntilFinishedAsync(newerEvents))[^1]);
        var olderGame = await harness.LoadGameAsync(older);
        var newerGame = await harness.LoadGameAsync(newer);
        Assert.NotNull(olderGame.FinishedAt);
        Assert.NotNull(newerGame.FinishedAt);
        Assert.Contains(olderGame.Termination, Terminations);
        Assert.Contains(newerGame.Termination, Terminations);
        Assert.True(olderGame.StartedAt < newerGame.StartedAt);

        // Only the two queued games were played: four players, one per side each, and no moves for the finished game.
        Assert.Equal(4, players.Created);
        var finishedAfter = await harness.LoadGameAsync(finished);
        Assert.Equal(finishedBefore.FinishedAt, finishedAfter.FinishedAt);
        Assert.Equal(finishedBefore.StartedAt, finishedAfter.StartedAt);
        Assert.Equal("0-1", finishedAfter.Result);
        Assert.Equal("checkmate", finishedAfter.Termination);
        Assert.Empty(await harness.LoadMovesAsync(finished));
    }

    [Fact]
    public async Task AGameEnqueuedByHandAndAlsoRequeuedOnStartupIsPlayedOnce()
    {
        var players = new ScriptedPlayerFactory((descriptor, _) => new RandomChessPlayer(descriptor, 1));
        await using var harness = new GameHarness(players, maxPlies: 20);
        var gameId = await harness.SeedGameAsync(RandomPlayer, RandomPlayer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stream = harness.Watch(gameId, timeout.Token);

        await harness.StartAsync();
        await harness.Runner.EnqueueAsync(gameId, timeout.Token);
        harness.FinishMigration();

        var events = await ReadUntilFinishedAsync(stream);
        Assert.IsType<GameFinished>(events[^1]);
        Assert.Single(events, e => e is GameFinished);
        Assert.Equal(2, players.Created);
    }

    private static async Task<List<IGameEvent>> ReadUntilFinishedAsync(IAsyncEnumerable<IGameEvent> stream)
    {
        var seen = new List<IGameEvent>();
        await foreach (var e in stream)
        {
            seen.Add(e);
            if (e is GameFinished)
            {
                break;
            }
        }

        return seen;
    }

    private static async Task<PlayerMove> WaitForever(MoveRequest request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new PlayerMove(null, [], Forfeit: true);
    }
}
