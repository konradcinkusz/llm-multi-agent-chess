using Chess.Analysis;
using Chess.Api.Broadcast;
using Chess.Api.Catalog;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Players;
using Chess.Api.Tests.Games;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// Induced gateway failures for the soak test. Between <see cref="OutageFrom"/> and <see cref="OutageUntil"/> the gateway is
/// down: every model move is refused, as a Copilot outage does, so the slots that start in it back off and are skipped.
/// Outside the outage <see cref="FlakyModel"/> fails in <see cref="FlakyShare"/> of its games, which the runner retries.
/// The random player never fails; the other models are the real players over the fake gateway.
/// </summary>
internal sealed class InducedFailurePlayers(IChessPlayerFactory real, Random random, TimeProvider clock,
    DateTimeOffset outageFrom, DateTimeOffset outageUntil) : IChessPlayerFactory
{
    public const string FlakyModel = "fake-b";
    public const double FlakyShare = 0.3;

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, bool> _flakyGames = [];

    public Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var outage = now >= outageFrom && now < outageUntil && descriptor.ModelId != ModelCatalog.RandomModelId;
        var flaky = descriptor.ModelId == FlakyModel && FailsThisGame(gameId);
        return outage || flaky
            ? Task.FromResult<IChessPlayer>(new NoReplyModel(descriptor))
            : real.CreateAsync(descriptor, gameId, side, ct);
    }

    /// <summary>Decided once per game, so that both sides of a game agree on whether it fails.</summary>
    private bool FailsThisGame(Guid gameId)
    {
        lock (_gate)
        {
            if (!_flakyGames.TryGetValue(gameId, out var fails))
            {
                fails = random.NextDouble() < FlakyShare;
                _flakyGames[gameId] = fails;
            }

            return fails;
        }
    }
}

/// <summary>Counts the errors a service logs, so that a test can assert that none escaped the runner's own handling.</summary>
internal sealed class ErrorCountingLogger<T> : ILogger<T>
{
    private int _errors;

    public int Errors => Volatile.Read(ref _errors);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Error)
        {
            Interlocked.Increment(ref _errors);
        }
    }
}

/// <summary>
/// A whole broadcast day on the InMemory provider, the real planner, the real game runner and the real broadcast runner,
/// driven by <see cref="RunnerClock"/>. <see cref="RestartAsync"/> plays a process restart: the runner stops with a game
/// in flight, the start-up steps of the app run again (the interrupted-game sweep, then a new runner that recovers the
/// slots), and the day goes on from the same database.
/// </summary>
internal sealed class SoakHarness : IAsyncDisposable
{
    /// <summary>The seed of the induced failures and of the planner's order: the same day every run.</summary>
    public const int Seed = 7;

    /// <summary>The gateway outage of the day: two simulated hours, from noon.</summary>
    public static readonly DateTimeOffset OutageFrom = RunnerHarness.Start.AddHours(12);
    public static readonly DateTimeOffset OutageUntil = RunnerHarness.Start.AddHours(14);

    /// <summary>The models of the day: the random player, three fake models, one of which fails in a share of its games.</summary>
    public static readonly string[] Models = [ModelCatalog.RandomModelId, "fake-a", InducedFailurePlayers.FlakyModel, "fake-c"];

    private readonly ServiceProvider _services;
    private readonly IChessPlayerFactory _players;

    private BroadcastRunnerSignals _signals = null!;
    private GameRunner _games = null!;
    private BroadcastRunner _runner = null!;

    public SoakHarness()
    {
        Clock = new RunnerClock(RunnerHarness.Start);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _services = services.BuildServiceProvider();
        Factory = _services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();

        Readiness = new DatabaseReadiness();
        Readiness.MarkReady(0);
        _players = new InducedFailurePlayers(GameHarness.RealPlayers(), new Random(Seed), Clock, OutageFrom, OutageUntil);
        Boot();
    }

    public RunnerClock Clock { get; }

    public IDbContextFactory<ArenaDbContext> Factory { get; }

    public DatabaseReadiness Readiness { get; }

    public ErrorCountingLogger<BroadcastRunner> RunnerLog { get; } = new();

    public BroadcastRunnerController Controller { get; private set; } = null!;

    /// <summary>The runner of the current process. After a restart this is the new one.</summary>
    public BroadcastRunner Runner => _runner;

    /// <summary>Plans the day with the real planner and saves it as a scheduled schedule, the state a start is pressed from.</summary>
    public async Task<Guid> SeedDayAsync(PlanRequest request)
    {
        var plan = BroadcastPlanner.Plan(request, new Dictionary<string, double?>());
        var id = Guid.NewGuid();
        await using var db = await Factory.CreateDbContextAsync();
        db.BroadcastSchedules.Add(new BroadcastScheduleEntity
        {
            Id = id,
            Name = request.Name,
            Status = ScheduleStatus.Scheduled,
            StartsAt = request.StartsAt,
            EndsAt = request.StartsAt.AddHours(request.DurationHours),
            MoveDelayMs = request.Pacing.MoveDelayMs,
            GameGapSeconds = request.Pacing.GameGapSeconds,
            Seed = request.Seed,
            Models = request.Models.Distinct().ToList(),
            CreatedAt = RunnerHarness.Start.AddDays(-1),
        });
        db.BroadcastSlots.AddRange(plan.Slots.Select(slot => new BroadcastSlotEntity
        {
            Id = Guid.NewGuid(),
            ScheduleId = id,
            Index = slot.Index,
            WhiteKind = slot.White.Kind,
            WhiteModel = slot.White.ModelId,
            BlackKind = slot.Black.Kind,
            BlackModel = slot.Black.ModelId,
            PlannedStart = slot.PlannedStart,
            PlannedEnd = slot.PlannedEnd,
            State = SlotState.Pending,
        }));
        await db.SaveChangesAsync();
        return id;
    }

    public Task StartRunnerAsync() => Runner.StartAsync(CancellationToken.None);

    /// <summary>
    /// A process restart in the middle of the day: the runner stops, the games it left unfinished are closed as
    /// interrupted (what <c>MigrationHostedService</c> does at start-up), and a new runner starts over the same database.
    /// </summary>
    public async Task RestartAsync()
    {
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await _runner.StopAsync(stop.Token);
        }

        await using (var db = await Factory.CreateDbContextAsync())
        {
            await InterruptedGameSweep.SweepAsync(db, Clock.GetUtcNow(), CancellationToken.None);
        }

        Boot();
        await StartRunnerAsync();
    }

    public async Task<ScheduleStatusView> ScheduleAsync(Guid scheduleId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        var schedule = await db.BroadcastSchedules.SingleAsync(s => s.Id == scheduleId);
        return new ScheduleStatusView(schedule.Status, schedule.FinishedAt);
    }

    public async Task<List<BroadcastSlotEntity>> SlotsAsync(Guid scheduleId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.BroadcastSlots.Where(s => s.ScheduleId == scheduleId).OrderBy(s => s.Index).ToListAsync();
    }

    /// <summary>Every game that was started, in the order they started.</summary>
    public async Task<List<GameEntity>> StartedGamesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.Where(g => g.StartedAt != null).OrderBy(g => g.StartedAt).ToListAsync();
    }

    public async Task<GameEntity> GameAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.SingleAsync(g => g.Id == gameId);
    }

    public async Task<int> PlyCountAsync(Guid gameId) => (await GameAsync(gameId)).PlyCount;

    /// <summary>True once <paramref name="settled"/> slots have ended and a slot is in play with at least <paramref name="plies"/> plies.</summary>
    public async Task<bool> MidGameAfterAsync(Guid scheduleId, int settled, int plies)
    {
        var slots = await SlotsAsync(scheduleId);
        var running = slots.SingleOrDefault(s => s.State == SlotState.Running && s.GameId is not null);
        return running is not null
            && slots.Count(s => s.State is SlotState.Finished or SlotState.Skipped) >= settled
            && await PlyCountAsync(running.GameId!.Value) >= plies;
    }

    public async ValueTask DisposeAsync()
    {
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await _runner.StopAsync(stop.Token);
        }

        await _services.DisposeAsync();
    }

    /// <summary>A fresh process over the same database: its own signals, game runner, controller and broadcast runner.</summary>
    private void Boot()
    {
        _signals = new BroadcastRunnerSignals();
        _games = new GameRunner(
            Factory,
            _players,
            new StubAnalyzer(),
            new InMemoryGameEventStream(),
            Options.Create(new GameRunnerOptions()),
            Clock,
            Readiness,
            NullLogger<GameRunner>.Instance);
        Controller = new BroadcastRunnerController(Factory, _signals, Clock);
        _runner = new BroadcastRunner(Factory, _games, _players, _signals, Clock, Readiness, RunnerLog);
    }
}
