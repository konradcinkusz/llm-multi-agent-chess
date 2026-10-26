using Chess.Analysis;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Engine;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tests.Games;

/// <summary>Hands out the player that <paramref name="create"/> builds for each side, and counts the players made.</summary>
internal sealed class ScriptedPlayerFactory(Func<PlayerDescriptor, Side, IChessPlayer> create) : IChessPlayerFactory
{
    private int _created;

    public int Created => _created;

    public Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct)
    {
        Interlocked.Increment(ref _created);
        return Task.FromResult(create(descriptor, side));
    }
}

/// <summary>A player whose moves are whatever <paramref name="choose"/> returns.</summary>
internal sealed class ScriptedPlayer(PlayerDescriptor descriptor, Func<MoveRequest, CancellationToken, Task<PlayerMove>> choose)
    : IChessPlayer
{
    public PlayerDescriptor Descriptor => descriptor;

    public Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct) => choose(request, ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Counts the moves in flight across every game that uses it, and remembers the most there were at once.</summary>
internal sealed class ConcurrencyMeter
{
    private readonly Lock _gate = new();
    private int _current;
    private int _peak;

    public int Peak
    {
        get
        {
            lock (_gate)
            {
                return _peak;
            }
        }
    }

    public void Enter()
    {
        lock (_gate)
        {
            _current++;
            _peak = Math.Max(_peak, _current);
        }
    }

    public void Exit()
    {
        lock (_gate)
        {
            _current--;
        }
    }
}

/// <summary>Delays the wrapped player's move, and reports it to the meter while the move is in flight.</summary>
internal sealed class MeteredPlayer(IChessPlayer inner, ConcurrencyMeter meter, TimeSpan delay) : IChessPlayer
{
    public PlayerDescriptor Descriptor => inner.Descriptor;

    public async Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct)
    {
        meter.Enter();
        try
        {
            await Task.Delay(delay, ct);
            return await inner.ChooseMoveAsync(request, ct);
        }
        finally
        {
            meter.Exit();
        }
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

/// <summary>An analyzer with a fixed answer; it records each position it was asked about.</summary>
internal sealed class StubAnalyzer(bool available = false, int? centipawns = null) : IMoveQualityAnalyzer
{
    public List<string> EvaluatedFens { get; } = [];

    public bool IsAvailable => available;

    public Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MoveQuality>>([]);

    public Task<int?> EvaluateAsync(string fen, CancellationToken ct)
    {
        EvaluatedFens.Add(fen);
        return Task.FromResult(centipawns);
    }
}

/// <summary>
/// A game runner over the InMemory provider, with its own database and event stream. Start it, seed games, and
/// play them to the end; disposing it stops the worker.
/// </summary>
internal sealed class GameHarness : IAsyncDisposable
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public static readonly DateTimeOffset CreatedAt = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public GameHarness(
        IChessPlayerFactory players,
        IMoveQualityAnalyzer? analyzer = null,
        int maxPlies = 200,
        int maxConcurrentGames = 2,
        Action<IGameEvent>? onPublish = null)
    {
        // The same registration the app uses, over a database nobody else sees.
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _services = services.BuildServiceProvider();
        Factory = _services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        Events = new InMemoryGameEventStream();
        Readiness = new DatabaseReadiness();
        Runner = new GameRunner(
            Factory,
            players,
            analyzer ?? new StubAnalyzer(),
            onPublish is null ? Events : new ObservedEventStream(Events, onPublish),
            Options.Create(new GameRunnerOptions { MaxConcurrentGames = maxConcurrentGames, MaxPlies = maxPlies }),
            TimeProvider.System,
            Readiness,
            NullLogger<GameRunner>.Instance);
    }

    private readonly ServiceProvider _services;

    public IDbContextFactory<ArenaDbContext> Factory { get; }

    public InMemoryGameEventStream Events { get; }

    public DatabaseReadiness Readiness { get; }

    public GameRunner Runner { get; }

    /// <summary>The players every test uses unless it needs scripted ones: the real factory, with the fake gateway.</summary>
    public static ChessPlayerFactory RealPlayers(FakeLlmGateway? gateway = null) =>
        new(gateway ?? new FakeLlmGateway(seed: 3), Options.Create(new PlayerOptions()));

    public Task StartAsync() => Runner.StartAsync(CancellationToken.None);

    /// <summary>Completes the schema step the app runs before the runner requeues games. Until then the runner waits.</summary>
    public void FinishMigration() => Readiness.MarkReady(0);

    public async Task<Guid> SeedGameAsync(
        PlayerDescriptor white, PlayerDescriptor black, string? startFen = null, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        var fen = startFen ?? StartFen;
        await using var db = await Factory.CreateDbContextAsync();
        db.Games.Add(new GameEntity
        {
            Id = id,
            WhiteKind = white.Kind,
            WhiteModel = white.ModelId,
            BlackKind = black.Kind,
            BlackModel = black.ModelId,
            Rated = true,
            Result = "*",
            StartFen = fen,
            CurrentFen = fen,
            CreatedAt = createdAt ?? CreatedAt,
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>Makes a seeded game look finished, as a game that ended before a restart does.</summary>
    public async Task MarkFinishedAsync(Guid gameId, string result, string termination)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var game = await db.Games.SingleAsync(g => g.Id == gameId);
        game.Result = result;
        game.Termination = termination;
        game.StartedAt = game.CreatedAt.AddMinutes(1);
        game.FinishedAt = game.CreatedAt.AddMinutes(2);
        await db.SaveChangesAsync();
    }

    /// <summary>Subscribes to the game's events now, so none are missed, and reads them as they come.</summary>
    public IAsyncEnumerable<IGameEvent> Watch(Guid gameId, CancellationToken ct) => Events.SubscribeAsync(gameId, ct);

    /// <summary>Subscribes, queues the game, and returns every event up to and including its <see cref="GameFinished"/>.</summary>
    public async Task<List<IGameEvent>> PlayToEndAsync(Guid gameId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stream = Watch(gameId, timeout.Token);
        await Runner.EnqueueAsync(gameId, timeout.Token);

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

    public async Task<GameEntity> LoadGameAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.SingleAsync(g => g.Id == gameId);
    }

    public async Task<List<MoveEntity>> LoadMovesAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Moves.Where(m => m.GameId == gameId).OrderBy(m => m.Ply).ToListAsync();
    }

    public async Task<List<MoveAttemptEntity>> LoadAttemptsAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.MoveAttempts.Where(a => a.GameId == gameId).OrderBy(a => a.Ply).ThenBy(a => a.Number).ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Runner.StopAsync(stop.Token);
        await _services.DisposeAsync();
    }
}

/// <summary>
/// Calls an observer synchronously inside <see cref="IGameEventStream.Publish"/>, on the runner's own thread, before the
/// event reaches the subscribers. A test uses it to act at the exact moment an event is published.
/// </summary>
internal sealed class ObservedEventStream(IGameEventStream inner, Action<IGameEvent> observer) : IGameEventStream
{
    public void Publish(IGameEvent e)
    {
        observer(e);
        inner.Publish(e);
    }

    public IAsyncEnumerable<IGameEvent> SubscribeAsync(Guid gameId, CancellationToken ct) => inner.SubscribeAsync(gameId, ct);
}
