using Chess.Api.Contracts;
using Chess.Api.Games;
using Chess.Api.Persistence.Entities;
using Chess.Api.Tournaments;
using Chess.Llm;
using Xunit;

namespace Chess.Api.Tests.Tournaments;

public class TournamentServiceTests
{
    private static readonly string[] ThreeModels = ["fake-a", "fake-b", "random"];

    [Fact]
    public async Task ThreeModelsWithTwoGamesEachCreateSixRatedGamesThatAreColourBalanced()
    {
        await using var harness = new TournamentHarness();

        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2);

        var games = await harness.GamesOfAsync(created.Tournament.Id);
        Assert.Equal(6, games.Count);
        Assert.All(games, g =>
        {
            Assert.Equal(created.Tournament.Id, g.TournamentId);
            Assert.True(g.Rated);
            Assert.Equal("*", g.Result);
            Assert.Null(g.FinishedAt);
        });

        // Each unordered pair meets twice, once with each colour.
        foreach (var (first, second) in new[] { ("fake-a", "fake-b"), ("fake-a", "random"), ("fake-b", "random") })
        {
            var meetings = games.Where(g => Same(g, first, second)).ToList();
            Assert.Equal(2, meetings.Count);
            Assert.Equal(1, meetings.Count(g => g.WhiteModel == first));
        }

        // Each model has as many games with white as with black.
        foreach (var model in ThreeModels)
        {
            Assert.Equal(games.Count(g => g.WhiteModel == model), games.Count(g => g.BlackModel == model));
        }

        // The random player is a random player, every other model is an LLM.
        Assert.All(games.Where(g => g.WhiteModel == "random"), g => Assert.Equal("random", g.WhiteKind));
        Assert.All(games.Where(g => g.WhiteModel != "random"), g => Assert.Equal("llm", g.WhiteKind));
        Assert.All(games.Where(g => g.BlackModel == "random"), g => Assert.Equal("random", g.BlackKind));
    }

    [Fact]
    public async Task CreatingATournamentStartsOnlyAsManyGamesAsItsMaxConcurrency()
    {
        await using var harness = new TournamentHarness();

        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);

        Assert.Equal(2, harness.Runner.Enqueued.Count);
        Assert.Equal(6, created.Tournament.GamesTotal);
        Assert.Equal(0, created.Tournament.GamesFinished);
        Assert.Equal(TournamentStatus.Running, created.Tournament.Status);
    }

    [Fact]
    public async Task ANewGameStartsOnlyWhenARunningOneEnds()
    {
        await using var harness = new TournamentHarness();
        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);
        var first = harness.Runner.Enqueued[0];

        await harness.FinishAsync(first);
        await harness.Tournaments.DispatchAsync(CancellationToken.None);

        Assert.Equal(3, harness.Runner.Enqueued.Count);
        Assert.Equal(2, harness.Runner.Held.Count);
        var progress = await harness.Tournaments.GetAsync(created.Tournament.Id, CancellationToken.None);
        Assert.Equal(1, progress!.GamesFinished);
        Assert.Equal(6, progress.GamesTotal);
    }

    [Fact]
    public async Task NoGameIsHandedToTheRunnerTwice()
    {
        await using var harness = new TournamentHarness();
        await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);

        await harness.Tournaments.DispatchAsync(CancellationToken.None);
        await harness.Tournaments.DispatchAsync(CancellationToken.None);

        Assert.Equal(2, harness.Runner.Enqueued.Count);
        Assert.Equal(2, harness.Runner.Enqueued.Distinct().Count());
    }

    [Fact]
    public async Task ATournamentPlaysEveryGameAndFinishesWhenTheLastOneEnds()
    {
        await using var harness = new TournamentHarness();
        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);

        // Each round the runner finishes what it holds, and the service starts what fits in its place.
        while (true)
        {
            await harness.Tournaments.DispatchAsync(CancellationToken.None);
            var held = harness.Runner.Held.ToList();
            if (held.Count == 0)
            {
                break;
            }

            Assert.True(held.Count <= 2);
            foreach (var gameId in held)
            {
                await harness.FinishAsync(gameId);
            }
        }

        Assert.Equal(6, harness.Runner.Enqueued.Distinct().Count());
        var finished = await harness.Tournaments.GetAsync(created.Tournament.Id, CancellationToken.None);
        Assert.Equal(TournamentStatus.Finished, finished!.Status);
        Assert.Equal(6, finished.GamesFinished);
        Assert.NotNull(finished.FinishedAt);
    }

    [Fact]
    public async Task CancellingAbortsTheGamesTheRunnerHoldsAndClosesTheRestHere()
    {
        await using var harness = new TournamentHarness();
        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);
        var held = harness.Runner.Held.ToList();
        var notStarted = (await harness.GamesOfAsync(created.Tournament.Id))
            .Select(g => g.Id)
            .Except(held)
            .ToList();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var closedEvents = notStarted.Select(id => harness.Events.SubscribeAsync(id, cancellation.Token).GetAsyncEnumerator()).ToList();

        var cancelled = await harness.Tournaments.CancelAsync(created.Tournament.Id, CancellationToken.None);

        Assert.Equal(TournamentStatus.Cancelled, cancelled!.Status);
        Assert.NotNull(cancelled.FinishedAt);
        Assert.Equal(held.OrderBy(id => id), harness.Runner.Aborted.OrderBy(id => id));

        // The runner ends the games it held; they stay open until it does.
        foreach (var gameId in held)
        {
            Assert.Null((await harness.LoadGameAsync(gameId)).FinishedAt);
        }

        // The games that never reached the runner end here, as aborted, and their subscribers hear it.
        foreach (var gameId in notStarted)
        {
            var game = await harness.LoadGameAsync(gameId);
            Assert.Equal("*", game.Result);
            Assert.Equal("aborted", game.Termination);
            Assert.NotNull(game.FinishedAt);
        }

        foreach (var subscriber in closedEvents)
        {
            Assert.True(await subscriber.MoveNextAsync());
            var finished = Assert.IsType<GameFinished>(subscriber.Current);
            Assert.Equal("aborted", finished.Termination);
            await subscriber.DisposeAsync();
        }

        // A cancelled tournament starts nothing more.
        await harness.Tournaments.DispatchAsync(CancellationToken.None);
        Assert.Equal(2, harness.Runner.Enqueued.Count);
        var after = await harness.Tournaments.GetAsync(created.Tournament.Id, CancellationToken.None);
        Assert.Equal(TournamentStatus.Cancelled, after!.Status);
    }

    [Fact]
    public async Task CancellingTwiceChangesNothingTheSecondTime()
    {
        await using var harness = new TournamentHarness();
        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);

        var first = await harness.Tournaments.CancelAsync(created.Tournament.Id, CancellationToken.None);
        var second = await harness.Tournaments.CancelAsync(created.Tournament.Id, CancellationToken.None);

        Assert.Equal(TournamentStatus.Cancelled, second!.Status);
        Assert.Equal(first!.FinishedAt, second.FinishedAt);
        Assert.Equal(2, harness.Runner.Aborted.Count);
    }

    [Fact]
    public async Task CancellingKeepsTheResultOfAGameThatAlreadyFinished()
    {
        await using var harness = new TournamentHarness();
        var created = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);
        var finishedGame = harness.Runner.Held.First();
        await harness.FinishAsync(finishedGame, "0-1", "checkmate");

        await harness.Tournaments.CancelAsync(created.Tournament.Id, CancellationToken.None);

        var game = await harness.LoadGameAsync(finishedGame);
        Assert.Equal("0-1", game.Result);
        Assert.Equal("checkmate", game.Termination);
    }

    [Fact]
    public async Task CancellingAnUnknownTournamentFindsNothing()
    {
        await using var harness = new TournamentHarness();

        Assert.Null(await harness.Tournaments.CancelAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await harness.Tournaments.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EstimateIsEightyRequestsAPerGameTimesEachModelsMultiplier()
    {
        var models = new[]
        {
            new LlmModelInfo("fake-a", "Fake A", 1.0, true),
            new LlmModelInfo("fake-b", "Fake B", 0.5, true),
        };
        await using var harness = new TournamentHarness(models);

        // Three models, two games a pair: each model plays two opponents twice, so four games.
        var created = await harness.CreateAsync(["fake-a", "fake-b", "fake-c"], gamesPerPair: 2);

        Assert.Equal(
            [
                new ModelPremiumEstimateDto("fake-a", 4 * 80 * 1.0),
                new ModelPremiumEstimateDto("fake-b", 4 * 80 * 0.5),
                new ModelPremiumEstimateDto("fake-c", null),
            ],
            created.EstimatedPremiumRequests);
    }

    [Fact]
    public async Task EstimateIsUnknownWhileTheGatewayIsNotReadyExceptForTheFreeRandomPlayer()
    {
        await using var harness = new TournamentHarness(state: LlmGatewayState.NotAuthenticated);

        var created = await harness.CreateAsync(["fake-a", "random"], gamesPerPair: 2);

        Assert.Equal(
            [new ModelPremiumEstimateDto("fake-a", null), new ModelPremiumEstimateDto("random", 0)],
            created.EstimatedPremiumRequests);
    }

    [Fact]
    public async Task ListShowsNewestFirstWithProgressForEach()
    {
        await using var harness = new TournamentHarness();
        var older = await harness.CreateAsync(["fake-a", "fake-b"], gamesPerPair: 2, maxConcurrency: 1);
        harness.Clock.Now = harness.Clock.Now.AddMinutes(1);
        var newer = await harness.CreateAsync(ThreeModels, gamesPerPair: 2, maxConcurrency: 2);
        var newerGames = (await harness.GamesOfAsync(newer.Tournament.Id)).Select(g => g.Id).ToHashSet();
        await harness.FinishAsync(harness.Runner.Held.First(newerGames.Contains));

        var listed = await harness.Tournaments.ListAsync(CancellationToken.None);

        Assert.Equal([newer.Tournament.Id, older.Tournament.Id], listed.Select(t => t.Id));
        Assert.Equal(6, listed[0].GamesTotal);
        Assert.Equal(1, listed[0].GamesFinished);
        Assert.Equal(2, listed[1].GamesTotal);
        Assert.Equal(0, listed[1].GamesFinished);
    }

    private static bool Same(GameEntity game, string first, string second) =>
        (game.WhiteModel == first && game.BlackModel == second) || (game.WhiteModel == second && game.BlackModel == first);
}
