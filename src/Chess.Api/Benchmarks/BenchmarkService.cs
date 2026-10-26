using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Api.Ratings;
using Chess.Api.Tournaments;
using Chess.Api.Usage;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Benchmarks;

/// <summary>
/// Creates, reads, cancels and reports benchmarks (03 §12.2). A benchmark is a tournament of kind <c>benchmark</c> with its
/// candidate, so its games run on <see cref="TournamentService"/> and count against its concurrency cap. Its games are
/// ordinary rated games: they enter the global rating, and the report reads that rating for the candidate.
/// </summary>
public sealed class BenchmarkService(
    IDbContextFactory<ArenaDbContext> factory,
    TournamentService tournaments,
    ModelCatalog catalog,
    CostInputs costs,
    TimeProvider clock)
{
    private const string RandomKind = "random";

    public async Task<BenchmarkCreatedDto> CreateAsync(CreateBenchmarkRequest request, IReadOnlyList<string> opponents, CancellationToken ct)
    {
        var models = new[] { request.Candidate }.Concat(opponents).ToList();
        var pairings = GauntletSchedule.Pairings(request.Candidate, opponents, request.GamesPerOpponent);
        var run = tournaments.NewTournament(request.Name, models, request.GamesPerOpponent, request.MaxConcurrency,
            TournamentKind.Benchmark, request.Candidate);
        await tournaments.StartAsync(run, pairings, ct);

        var estimate = await tournaments.EstimateAsync(models, pairings, ct);
        return new BenchmarkCreatedDto(ToDto(run, (pairings.Count, 0)), estimate);
    }

    public async Task<IReadOnlyList<BenchmarkDto>> ListAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var runs = await db.Tournaments.AsNoTracking()
            .Where(t => t.Kind == TournamentKind.Benchmark)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
        var progress = await TournamentService.ProgressAsync(db, runs.Select(t => t.Id).ToList(), ct);
        return runs.Select(t => ToDto(t, progress.GetValueOrDefault(t.Id))).ToArray();
    }

    /// <returns>The benchmark, or null when no benchmark has that id.</returns>
    public async Task<BenchmarkDto?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var run = await db.Tournaments.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id && t.Kind == TournamentKind.Benchmark, ct);
        if (run is null)
        {
            return null;
        }

        var progress = await TournamentService.ProgressAsync(db, [id], ct);
        return ToDto(run, progress.GetValueOrDefault(id));
    }

    /// <summary>Cancels a running benchmark: its queued and running games are aborted (as for a tournament). Null for an unknown id.</summary>
    public async Task<BenchmarkDto?> CancelAsync(Guid id, CancellationToken ct)
    {
        if (await GetAsync(id, ct) is null)
        {
            return null;
        }

        await tournaments.CancelAsync(id, ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// The report of a benchmark, as far as its games have run (a running benchmark reports what is decided so far).
    /// Null for an unknown id.
    /// </summary>
    public async Task<BenchmarkReportDto?> ReportAsync(Guid id, CancellationToken ct)
    {
        var benchmark = await GetAsync(id, ct);
        if (benchmark is null)
        {
            return null;
        }

        var listing = await catalog.GetModelsAsync(ct);
        var games = await LoadGamesAsync(benchmark, listing, ct);
        var rated = await RatedGameLoader.LoadAsync(factory, tournamentId: null, ct);
        var rating = RatingCalculator.Compute(rated.Outcomes, rated.MoveStats, new RatingOptions());
        return BenchmarkReportBuilder.Build(benchmark, games, rating, rated.Outcomes,
            BenchmarkFingerprint.Create(clock.GetUtcNow(), benchmark.Candidate));
    }

    private async Task<List<BenchmarkGame>> LoadGamesAsync(BenchmarkDto benchmark, ModelListing listing, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Games.AsNoTracking()
            .Where(g => g.TournamentId == benchmark.Id)
            .OrderBy(g => g.Id)
            .Select(g => new GameRow(g.Id, g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel, g.Result, g.Termination))
            .ToListAsync(ct);

        var gameIds = rows.Select(r => r.Id).ToList();
        var moves = (await db.Moves.AsNoTracking()
            .Where(m => gameIds.Contains(m.GameId))
            .Select(m => new MoveRow(m.GameId, m.Color, m.Attempts))
            .ToListAsync(ct))
            .ToLookup(m => m.GameId);

        // An attempt has no colour of its own; plies are numbered from 1 and White moves on the odd ones (RatedGameLoader).
        var attempts = (await db.MoveAttempts.AsNoTracking()
            .Where(a => gameIds.Contains(a.GameId))
            .Select(a => new AttemptRow(a.GameId, a.Ply, a.InputTokens, a.OutputTokens, a.ReasoningTokens,
                a.CachedInputTokens, a.AiCredits))
            .ToListAsync(ct))
            .ToLookup(a => (a.GameId, IsWhitePly: a.Ply % 2 == 1));

        var candidate = benchmark.Candidate;
        var rate = costs.UsdPerAiCredit;
        return rows.Select(row =>
        {
            var candidateIsWhite = row.WhiteModel == candidate;
            var candidateColor = candidateIsWhite ? "white" : "black";
            var opponent = candidateIsWhite ? row.BlackModel : row.WhiteModel;
            var whiteUsage = SideUsage(row, "white", attempts, listing, rate);
            var blackUsage = SideUsage(row, "black", attempts, listing, rate);
            var candidateMoves = moves[row.Id].Where(m => m.Color == candidateColor).ToList();

            return new BenchmarkGame(opponent, candidateIsWhite, row.Result, row.Termination,
                CandidateMoves: candidateMoves.Count,
                CandidateFirstAttemptLegal: candidateMoves.Count(m => m.Attempts == 1),
                Candidate: candidateIsWhite ? whiteUsage : blackUsage,
                OpponentUsage: candidateIsWhite ? blackUsage : whiteUsage);
        }).ToList();
    }

    private static SideUsageDto SideUsage(GameRow row, string color, ILookup<(Guid GameId, bool IsWhitePly), AttemptRow> attempts,
        ModelListing listing, double usdPerAiCredit)
    {
        var isWhite = color == "white";
        var kind = isWhite ? row.WhiteKind : row.BlackKind;
        var model = isWhite ? row.WhiteModel : row.BlackModel;
        var replies = attempts[(row.Id, isWhite)]
            .Select(a => new GameCostCalculator.ReplyUsage(a.InputTokens, a.OutputTokens, a.ReasoningTokens,
                a.CachedInputTokens, a.AiCredits))
            .ToList();
        var prices = listing.Models.FirstOrDefault(m => m.Id == ModelVariant.BaseOf(model))?.Pricing;
        return GameCostCalculator.ForSide(color, model, isLlm: kind != RandomKind, replies, prices, usdPerAiCredit);
    }

    private static BenchmarkDto ToDto(TournamentEntity run, (int Total, int Finished) progress) =>
        new(
            run.Id,
            run.Name,
            run.Candidate ?? "",
            run.Models.Where(m => m != run.Candidate).ToList(),
            run.GamesPerPair,
            run.Status,
            progress.Total,
            progress.Finished,
            run.CreatedAt,
            run.FinishedAt);

    private sealed record GameRow(Guid Id, string WhiteKind, string WhiteModel, string BlackKind, string BlackModel,
        string Result, string? Termination);

    private sealed record MoveRow(Guid GameId, string Color, int Attempts);

    private sealed record AttemptRow(Guid GameId, int Ply, int? InputTokens, int? OutputTokens, int? ReasoningTokens,
        int? CachedInputTokens, double? AiCredits);
}
