using Chess.Api.Contracts;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// The model that never answers: every move it is asked for comes back with no reply, as after a gateway failure. The
/// players of the other models are the real ones.
/// </summary>
internal sealed class NoReplyModel : IChessPlayer
{
    public NoReplyModel(PlayerDescriptor descriptor) => Descriptor = descriptor;

    public PlayerDescriptor Descriptor { get; }

    public Task<PlayerMove> ChooseMoveAsync(MoveRequest request, CancellationToken ct) =>
        Task.FromResult(new PlayerMove(null, [new MoveAttempt(1, "prompt", null, null, false, TimeSpan.Zero, null, null, "Timeout")], Forfeit: true));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Model <c>broken</c> never answers; every other player is the real one.</summary>
internal sealed class BrokenOrRealPlayers(IChessPlayerFactory real) : IChessPlayerFactory
{
    public const string BrokenModel = "broken";

    public Task<IChessPlayer> CreateAsync(PlayerDescriptor descriptor, Guid gameId, Side side, CancellationToken ct) =>
        descriptor.ModelId == BrokenModel
            ? Task.FromResult<IChessPlayer>(new NoReplyModel(descriptor))
            : real.CreateAsync(descriptor, gameId, side, ct);
}

/// <summary>
/// The broadcast runner over the InMemory provider, the real game runner and random players, with a clock the test drives.
/// The schedule and its slots are seeded straight into the database; the controller and the runner are the real ones.
/// </summary>
internal sealed class RunnerHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);

    public static readonly PlayerSpec RandomPlayer = new(ModelCatalog.RandomModelId, ModelCatalog.RandomModelId);

    private readonly ServiceProvider _services;

    public RunnerHarness()
    {
        Clock = new RunnerClock(Start);
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _services = services.BuildServiceProvider();
        Factory = _services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();

        Readiness = new DatabaseReadiness();
        Readiness.MarkReady(0);
        Signals = new BroadcastRunnerSignals();
        Players = new BrokenOrRealPlayers(GameHarness.RealPlayers());
        Games = new GameRunner(
            Factory,
            Players,
            new StubAnalyzer(),
            new InMemoryGameEventStream(),
            Options.Create(new GameRunnerOptions()),
            Clock,
            Readiness,
            NullLogger<GameRunner>.Instance);
        Controller = new BroadcastRunnerController(Factory, Signals, Clock);
        Runner = new BroadcastRunner(Factory, Games, Players, Signals, Clock, Readiness, NullLogger<BroadcastRunner>.Instance);
    }

    public RunnerClock Clock { get; }

    public IDbContextFactory<ArenaDbContext> Factory { get; }

    public DatabaseReadiness Readiness { get; }

    public BroadcastRunnerSignals Signals { get; }

    public IChessPlayerFactory Players { get; }

    public GameRunner Games { get; }

    public BroadcastRunnerController Controller { get; }

    public BroadcastRunner Runner { get; }

    public Task StartRunnerAsync() => Runner.StartAsync(CancellationToken.None);

    /// <summary>Seeds a schedule with one slot per pair, all planned at <see cref="Start"/> unless a start is given.</summary>
    public async Task<Guid> SeedScheduleAsync(
        string status, int moveDelayMs, int gameGapSeconds, params (PlayerSpec White, PlayerSpec Black)[] pairs)
    {
        var id = Guid.NewGuid();
        await using var db = await Factory.CreateDbContextAsync();
        db.BroadcastSchedules.Add(new BroadcastScheduleEntity
        {
            Id = id,
            Name = "Test day",
            Status = status,
            StartsAt = Start,
            EndsAt = Start.AddHours(24),
            MoveDelayMs = moveDelayMs,
            GameGapSeconds = gameGapSeconds,
            Seed = 1,
            Models = pairs.SelectMany(p => new[] { p.White.ModelId, p.Black.ModelId }).Distinct().ToList(),
            CreatedAt = Start.AddDays(-1),
            StartedAt = status == ScheduleStatus.Running ? Start.AddDays(-1) : null,
        });

        for (var i = 0; i < pairs.Length; i++)
        {
            db.BroadcastSlots.Add(new BroadcastSlotEntity
            {
                Id = Guid.NewGuid(),
                ScheduleId = id,
                Index = i,
                WhiteKind = pairs[i].White.Kind,
                WhiteModel = pairs[i].White.ModelId,
                BlackKind = pairs[i].Black.Kind,
                BlackModel = pairs[i].Black.ModelId,
                PlannedStart = Start,
                PlannedEnd = Start.AddHours(1),
                State = SlotState.Pending,
            });
        }

        await db.SaveChangesAsync();
        return id;
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

    /// <summary>The games of the slots, in the order they were created.</summary>
    public async Task<List<GameEntity>> GamesAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.OrderBy(g => g.CreatedAt).ToListAsync();
    }

    public async Task<GameEntity> GameAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.SingleAsync(g => g.Id == gameId);
    }

    public async Task<int> PlyCountAsync(Guid gameId) => (await GameAsync(gameId)).PlyCount;

    public async ValueTask DisposeAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Runner.StopAsync(stop.Token);
        await _services.DisposeAsync();
    }
}

internal sealed record ScheduleStatusView(string Status, DateTimeOffset? FinishedAt);
