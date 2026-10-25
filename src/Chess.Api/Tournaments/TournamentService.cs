using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Games;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Engine;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Tournaments;

/// <summary>
/// Creates, reads and cancels round robins (03 §5), and hands their games to <see cref="IGameRunner"/> so that no
/// tournament has more than its <c>MaxConcurrency</c> games in the runner at once. The games are created up front;
/// <see cref="DispatchAsync"/> starts them as room frees up. It is a singleton because it remembers which games it has
/// handed over, and it is called by <see cref="TournamentScheduler"/>.
/// </summary>
public sealed class TournamentService(
    IDbContextFactory<ArenaDbContext> factory,
    IGameRunner runner,
    IGameEventStream events,
    ModelCatalog catalog,
    CostInputs costs,
    TimeProvider clock)
{
    /// <summary>
    /// Premium requests a model is expected to make in one game: its moves on one side (40, 03 §9.1), the same convention
    /// as the credit estimate and the broadcast planner.
    /// </summary>
    public const int EstimatedRequestsPerGame = 40;

    // Serialises dispatch and cancel, so a game is never handed to the runner after its tournament was cancelled.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // The games of each tournament that were handed to the runner and have not finished, as this process sees them.
    private readonly Dictionary<Guid, HashSet<Guid>> _queued = [];

    public async Task<TournamentCreatedDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct)
    {
        var models = request.Models.ToList();
        var pairings = RoundRobin.Pairings(models, request.GamesPerPair);
        var tournament = NewTournament(request.Name, models, request.GamesPerPair, request.MaxConcurrency,
            TournamentKind.RoundRobin, candidate: null);
        await StartAsync(tournament, pairings, ct);

        var estimate = await EstimateAsync(models, pairings, ct);
        return new TournamentCreatedDto(ToDto(tournament, (pairings.Count, 0)), estimate);
    }

    /// <summary>
    /// A new run of the given kind, not yet saved: <see cref="StartAsync"/> saves it with its games. <paramref name="candidate"/>
    /// is set only for a benchmark (03 §12.2).
    /// </summary>
    public TournamentEntity NewTournament(string name, IReadOnlyList<string> models, int gamesPerPair, int maxConcurrency,
        string kind, string? candidate) => new()
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Models = [.. models],
            GamesPerPair = gamesPerPair,
            MaxConcurrency = maxConcurrency,
            Status = TournamentStatus.Running,
            CreatedAt = clock.GetUtcNow(),
            Kind = kind,
            Candidate = candidate,
        };

    /// <summary>
    /// Saves a new run with one rated game per pairing, then hands as many of them to the runner as its concurrency allows.
    /// The games of a run are created up front; <see cref="DispatchAsync"/> starts the rest as room frees up.
    /// </summary>
    public async Task StartAsync(TournamentEntity tournament, IReadOnlyList<Pairing> pairings, CancellationToken ct)
    {
        var startFen = ChessBoard.StartingPosition().ToFen();
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            db.Tournaments.Add(tournament);
            foreach (var pairing in pairings)
            {
                db.Games.Add(new GameEntity
                {
                    Id = Guid.NewGuid(),
                    TournamentId = tournament.Id,
                    WhiteKind = KindOf(pairing.White),
                    WhiteModel = pairing.White,
                    BlackKind = KindOf(pairing.Black),
                    BlackModel = pairing.Black,
                    Rated = true,
                    Result = "*",
                    StartFen = startFen,
                    CurrentFen = startFen,
                    CreatedAt = tournament.CreatedAt,
                });
            }

            await db.SaveChangesAsync(ct);
            await DispatchLockedAsync(db, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<TournamentDto>> ListAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        // Benchmarks have their own list (03 §12.2); a round robin is what this one shows.
        var tournaments = await db.Tournaments.AsNoTracking()
            .Where(t => t.Kind == TournamentKind.RoundRobin)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        var progress = await ProgressAsync(db, tournaments.Select(t => t.Id).ToList(), ct);
        return tournaments.Select(t => ToDto(t, progress.GetValueOrDefault(t.Id))).ToArray();
    }

    public async Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var tournament = await db.Tournaments.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tournament is null)
        {
            return null;
        }

        var progress = await ProgressAsync(db, [id], ct);
        return ToDto(tournament, progress.GetValueOrDefault(id));
    }

    /// <summary>
    /// Cancels a running tournament: its queued and running games are aborted, and it is marked cancelled. A tournament
    /// that already ended keeps its status. Returns null for an unknown tournament.
    /// </summary>
    public async Task<TournamentDto?> CancelAsync(Guid id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var tournament = await db.Tournaments.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (tournament is null)
            {
                return null;
            }

            if (tournament.Status == TournamentStatus.Running)
            {
                tournament.Status = TournamentStatus.Cancelled;
                tournament.FinishedAt = clock.GetUtcNow();
                _queued.Remove(id);
                await AbortUnfinishedAsync(db, id, ct);
                await db.SaveChangesAsync(ct);
            }

            var progress = await ProgressAsync(db, [id], ct);
            return ToDto(tournament, progress.GetValueOrDefault(id));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Starts the queued games of each running tournament as far as its <c>MaxConcurrency</c> allows, and finishes the
    /// tournaments whose games have all ended. Called on a timer; a game is handed to the runner only once.
    /// </summary>
    public async Task DispatchAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await DispatchLockedAsync(db, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DispatchLockedAsync(ArenaDbContext db, CancellationToken ct)
    {
        var running = await db.Tournaments.Where(t => t.Status == TournamentStatus.Running).ToListAsync(ct);
        foreach (var tournament in running)
        {
            var unfinished = await db.Games
                .Where(g => g.TournamentId == tournament.Id && g.FinishedAt == null)
                .OrderBy(g => g.CreatedAt)
                .Select(g => g.Id)
                .ToListAsync(ct);

            if (unfinished.Count == 0)
            {
                tournament.Status = TournamentStatus.Finished;
                tournament.FinishedAt = clock.GetUtcNow();
                _queued.Remove(tournament.Id);
                continue;
            }

            var queued = QueuedFor(tournament.Id);
            queued.IntersectWith(unfinished);
            var room = tournament.MaxConcurrency - queued.Count;
            var next = unfinished.Where(gameId => !queued.Contains(gameId)).Take(Math.Max(0, room)).ToList();
            foreach (var gameId in next)
            {
                await runner.EnqueueAsync(gameId, ct);
                queued.Add(gameId);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// A game the runner holds is aborted there, and the runner records it as aborted. A game that never reached the runner
    /// is closed here. A game that finished in the meantime keeps its result.
    /// </summary>
    private async Task AbortUnfinishedAsync(ArenaDbContext db, Guid tournamentId, CancellationToken ct)
    {
        var unfinished = await db.Games
            .Where(g => g.TournamentId == tournamentId && g.FinishedAt == null)
            .Select(g => g.Id)
            .ToListAsync(ct);
        var notInRunner = unfinished.Where(gameId => !runner.TryAbort(gameId)).ToList();

        var now = clock.GetUtcNow();
        var closed = new List<Guid>();
        foreach (var gameId in notInRunner)
        {
            var game = await db.Games.FirstOrDefaultAsync(g => g.Id == gameId && g.FinishedAt == null, ct);
            if (game is null)
            {
                continue;
            }

            game.Result = GameTermination.Aborted.Result;
            game.Termination = GameTermination.Aborted.Termination;
            game.FinishedAt = now;
            closed.Add(gameId);
        }

        await db.SaveChangesAsync(ct);
        foreach (var gameId in closed)
        {
            events.Publish(new GameFinished(gameId, now, GameTermination.Aborted.Result, GameTermination.Aborted.Termination));
        }
    }

    private HashSet<Guid> QueuedFor(Guid tournamentId)
    {
        if (!_queued.TryGetValue(tournamentId, out var queued))
        {
            queued = [];
            _queued[tournamentId] = queued;
        }

        return queued;
    }

    /// <summary>
    /// What each model is expected to cost in these pairings: its games × the moves a side makes in a game. A variant
    /// (<c>model@effort</c>) is priced as its model, because the premium multiplier and the token prices belong to the model.
    /// </summary>
    public async Task<IReadOnlyList<ModelPremiumEstimateDto>> EstimateAsync(IReadOnlyList<string> models,
        IReadOnlyList<Pairing> pairings, CancellationToken ct)
    {
        var listing = await catalog.GetModelsAsync(ct);
        var inputs = await costs.ForAsync(listing, ct);
        return models
            .Select(id =>
            {
                var model = ModelVariant.BaseOf(id);
                var gamesOfModel = pairings.Count(p => p.White == id || p.Black == id);
                var cost = CostEstimator.Estimate(model, gamesOfModel * CostEstimator.MovesPerSidePerGame,
                    inputs.GetValueOrDefault(model), costs.UsdPerAiCredit);
                return new ModelPremiumEstimateDto(id, Estimate(listing, model, gamesOfModel),
                    cost.AiCredits, cost.Usd, cost.Basis, cost.SampleMoves);
            })
            .ToArray();
    }

    private static double? Estimate(ModelListing listing, string modelId, int gamesPerModel)
    {
        var multiplier = listing.Models.FirstOrDefault(m => m.Id == modelId)?.PremiumMultiplier;
        return multiplier is { } value ? gamesPerModel * EstimatedRequestsPerGame * value : null;
    }

    /// <summary>The games and finished games of each run, by run id; a run with no games is absent.</summary>
    internal static async Task<Dictionary<Guid, (int Total, int Finished)>> ProgressAsync(
        ArenaDbContext db, List<Guid> tournamentIds, CancellationToken ct)
    {
        var rows = await db.Games
            .Where(g => g.TournamentId.HasValue && tournamentIds.Contains(g.TournamentId.Value))
            .GroupBy(g => g.TournamentId!.Value)
            .Select(group => new
            {
                Id = group.Key,
                Total = group.Count(),
                Finished = group.Count(g => g.FinishedAt != null),
            })
            .ToListAsync(ct);
        return rows.ToDictionary(row => row.Id, row => (row.Total, row.Finished));
    }

    private static TournamentDto ToDto(TournamentEntity tournament, (int Total, int Finished) progress) =>
        new(
            tournament.Id,
            tournament.Name,
            tournament.Models,
            tournament.GamesPerPair,
            tournament.Status,
            progress.Total,
            progress.Finished,
            tournament.CreatedAt,
            tournament.FinishedAt);

    /// <summary>A model id that the gateway does not list is still a player; <c>random</c> is the synthetic one.</summary>
    private static string KindOf(string modelId) => modelId == ModelCatalog.RandomModelId ? "random" : "llm";
}
