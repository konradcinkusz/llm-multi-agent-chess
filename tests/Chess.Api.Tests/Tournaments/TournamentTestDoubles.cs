using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Tests.Catalog;
using Chess.Api.Tournaments;
using Chess.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Chess.Api.Tests.Tournaments;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// Stands in for the game runner: it remembers which games it was given and which it has not yet finished. The test ends
/// a game with <see cref="TournamentHarness.FinishAsync"/> and then with <see cref="Finish"/>, as the real runner would.
/// </summary>
internal sealed class RecordingGameRunner : IGameRunner
{
    private readonly HashSet<Guid> _held = [];

    /// <summary>Every enqueue, in order, including any repeated ones.</summary>
    public List<Guid> Enqueued { get; } = [];

    /// <summary>The games aborted through <see cref="TryAbort"/>.</summary>
    public List<Guid> Aborted { get; } = [];

    /// <summary>The games the runner holds: enqueued, not aborted and not yet finished.</summary>
    public IReadOnlyCollection<Guid> Held => _held;

    public ValueTask EnqueueAsync(Guid gameId, CancellationToken ct)
    {
        Enqueued.Add(gameId);
        _held.Add(gameId);
        return ValueTask.CompletedTask;
    }

    public bool TryAbort(Guid gameId)
    {
        if (!_held.Remove(gameId))
        {
            return false;
        }

        Aborted.Add(gameId);
        return true;
    }

    public void Finish(Guid gameId) => _held.Remove(gameId);
}

/// <summary>A tournament service over the InMemory provider, with a recording runner and a catalog of the given models.</summary>
internal sealed class TournamentHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public TournamentHarness(
        IReadOnlyList<LlmModelInfo>? models = null,
        LlmGatewayState state = LlmGatewayState.Ready)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<ArenaDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        _services = services.BuildServiceProvider();

        Factory = _services.GetRequiredService<IDbContextFactory<ArenaDbContext>>();
        Events = new InMemoryGameEventStream();
        Runner = new RecordingGameRunner();
        Clock = new ManualClock();
        var gateway = new StubGateway(new LlmGatewayStatus(state, "test"), models);
        Catalog = new ModelCatalog(gateway, new MemoryCache(new MemoryCacheOptions()), Options.Create(new CatalogOptions()));
        Costs = new CostInputs(Factory, Options.Create(new CatalogOptions()));
        Tournaments = new TournamentService(Factory, Runner, Events, Catalog, Costs, Clock);
    }

    public IDbContextFactory<ArenaDbContext> Factory { get; }

    public InMemoryGameEventStream Events { get; }

    public RecordingGameRunner Runner { get; }

    public ManualClock Clock { get; }

    public ModelCatalog Catalog { get; }

    public CostInputs Costs { get; }

    public TournamentService Tournaments { get; }

    public Task<TournamentCreatedDto> CreateAsync(string[] models, int gamesPerPair = 2, int maxConcurrency = 2) =>
        Tournaments.CreateAsync(new CreateTournamentRequest("Test", models, gamesPerPair, maxConcurrency), CancellationToken.None);

    public async Task<List<GameEntity>> GamesOfAsync(Guid tournamentId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.Where(g => g.TournamentId == tournamentId).OrderBy(g => g.Id).ToListAsync();
    }

    public async Task<GameEntity> LoadGameAsync(Guid gameId)
    {
        await using var db = await Factory.CreateDbContextAsync();
        return await db.Games.SingleAsync(g => g.Id == gameId);
    }

    /// <summary>Records a game's end in the database, as the runner would, and tells the runner it is done.</summary>
    public async Task FinishAsync(Guid gameId, string result = "1-0", string termination = "checkmate")
    {
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var game = await db.Games.SingleAsync(g => g.Id == gameId);
            game.Result = result;
            game.Termination = termination;
            game.FinishedAt = Clock.GetUtcNow();
            await db.SaveChangesAsync();
        }

        Runner.Finish(gameId);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
