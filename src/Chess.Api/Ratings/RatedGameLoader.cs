using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;
using Chess.Rating;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Ratings;

/// <summary>What <see cref="RatingCalculator"/> reads from the database: the rated games and the moves played in them.</summary>
public sealed record RatedGames(IReadOnlyList<GameOutcome> Outcomes, IReadOnlyList<PlayerMoveStats> MoveStats);

/// <summary>
/// Reads the rating inputs from the games and moves tables (RATING.md §1, §4). A game is rated when it was
/// created with <c>rated</c> and has a result; interrupted and unfinished games keep <c>*</c> and are left out.
/// Public so the results breakdown (T2.9) reads the same games as the leaderboard.
/// </summary>
public static class RatedGameLoader
{
    public const string RandomModel = "random";

    private const string RandomKind = "random";
    private const string Unfinished = "*";

    /// <summary>Loads the rated games, optionally only those of one tournament. Outcomes are ordered by game id.</summary>
    public static async Task<RatedGames> LoadAsync(IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        var games = await GameRows(db, tournamentId).ToListAsync(ct);
        var outcomes = games
            .Select(g => new GameOutcome(ModelOf(g.WhiteKind, g.WhiteModel), ModelOf(g.BlackKind, g.BlackModel), ScoreOf(g.Result)))
            .ToList();

        // The three numbers per model are summed in the database: the rows of every move never leave it.
        var groups = await MoveGroups(db, tournamentId).ToListAsync(ct);
        var stats = groups
            .GroupBy(g => IsWhite(g.Color) ? ModelOf(g.WhiteKind, g.WhiteModel) : ModelOf(g.BlackKind, g.BlackModel), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => StatsOf(group.Key, group.ToList()))
            .ToList();

        return new RatedGames(outcomes, stats);
    }

    /// <summary>
    /// What the rating is a function of, read in a few small aggregate queries: the number of rated games, the newest finish,
    /// and the count, annotation count and total centipawn loss of their moves. It changes whenever a game is rated
    /// or a move is annotated, so a result memoised under it is never stale (see <see cref="RatingMemo"/>).
    /// </summary>
    public static async Task<RatedDataVersion> VersionAsync(IDbContextFactory<ArenaDbContext> factory, Guid? tournamentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var games = await GamesVersion(db, tournamentId).FirstOrDefaultAsync(ct);
        var moves = await MovesVersion(db, tournamentId).FirstOrDefaultAsync(ct);
        return new RatedDataVersion(
            games?.Games ?? 0, games?.LastFinishedAt, moves?.Moves ?? 0, moves?.Annotated ?? 0, moves?.CentipawnLoss ?? 0);
    }

    // Each query has its own method so a test can translate it for PostgreSQL (ToQueryString) without a server:
    // the InMemory provider evaluates queries Npgsql cannot translate. Order before projecting (lesson #78).
    internal static IQueryable<GameRow> GameRows(ArenaDbContext db, Guid? tournamentId) =>
        Rated(db, tournamentId)
            .OrderBy(g => g.Id)
            .Select(g => new GameRow(g.Id, g.Result, g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel));

    internal static IQueryable<MoveGroup> MoveGroups(ArenaDbContext db, Guid? tournamentId) =>
        db.Moves.AsNoTracking()
            .Join(Rated(db, tournamentId), m => m.GameId, g => g.Id,
                (m, g) => new { m.Color, m.Attempts, m.CentipawnLoss, g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel })
            .GroupBy(x => new { x.Color, x.WhiteKind, x.WhiteModel, x.BlackKind, x.BlackModel })
            .Select(group => new MoveGroup(
                group.Key.Color, group.Key.WhiteKind, group.Key.WhiteModel, group.Key.BlackKind, group.Key.BlackModel,
                group.Count(),
                group.Count(x => x.Attempts == 1),
                group.Count(x => x.CentipawnLoss != null),
                group.Sum(x => (long?)x.CentipawnLoss) ?? 0));

    internal static IQueryable<GamesVersionRow> GamesVersion(ArenaDbContext db, Guid? tournamentId) =>
        Rated(db, tournamentId)
            .GroupBy(g => 1)
            .Select(group => new GamesVersionRow(group.Count(), group.Max(g => g.FinishedAt)));

    internal static IQueryable<MovesVersionRow> MovesVersion(ArenaDbContext db, Guid? tournamentId) =>
        db.Moves.AsNoTracking()
            .Join(Rated(db, tournamentId), m => m.GameId, g => g.Id, (m, g) => m)
            .GroupBy(m => 1)
            .Select(group => new MovesVersionRow(
                group.Count(), group.Count(m => m.CentipawnLoss != null), group.Sum(m => (long?)m.CentipawnLoss) ?? 0));

    private static IQueryable<GameEntity> Rated(ArenaDbContext db, Guid? tournamentId)
    {
        var rated = db.Games.AsNoTracking().Where(g => g.Rated && g.Result != Unfinished);
        return tournamentId is { } id ? rated.Where(g => g.TournamentId == id) : rated;
    }

    // The model a side is rated as. A random side is always "random", whatever id the row stores.
    private static string ModelOf(string kind, string modelId) => kind == RandomKind ? RandomModel : modelId;

    // s = score of White: 1, ½, 0 (RATING.md §2). Any other result on a rated game is a bug upstream, not a draw.
    private static double ScoreOf(string result) => result switch
    {
        "1-0" => 1.0,
        "0-1" => 0.0,
        "1/2-1/2" => 0.5,
        _ => throw new InvalidOperationException($"Rated game has unexpected result '{result}'."),
    };

    private static bool IsWhite(string color) => color switch
    {
        "white" => true,
        "black" => false,
        _ => throw new InvalidOperationException($"Move has unexpected colour '{color}'."),
    };

    // L = attempts == 1 over moves. ACPL is the mean cp loss only when every move has one; a single unanalysed move
    // makes it unknown rather than a biased average (RATING.md §4).
    private static PlayerMoveStats StatsOf(string model, IReadOnlyList<MoveGroup> groups)
    {
        var moves = groups.Sum(g => g.Moves);
        var annotated = groups.Sum(g => g.Annotated);
        var loss = groups.Sum(g => g.CentipawnLoss);
        double? acpl = annotated == moves ? (double)loss / moves : null;
        return new PlayerMoveStats(model, moves, groups.Sum(g => g.FirstAttempt), acpl);
    }

    internal sealed record GameRow(Guid Id, string Result, string WhiteKind, string WhiteModel, string BlackKind, string BlackModel);

    /// <summary>The moves of one side of one pairing, summed by the database.</summary>
    internal sealed record MoveGroup(
        string Color, string WhiteKind, string WhiteModel, string BlackKind, string BlackModel,
        int Moves, int FirstAttempt, int Annotated, long CentipawnLoss);

    internal sealed record GamesVersionRow(int Games, DateTimeOffset? LastFinishedAt);

    internal sealed record MovesVersionRow(int Moves, int Annotated, long CentipawnLoss);
}

/// <summary>The cheap fingerprint of the rating inputs (see <see cref="RatedGameLoader.VersionAsync"/>).</summary>
public sealed record RatedDataVersion(int Games, DateTimeOffset? LastFinishedAt, int Moves, int AnnotatedMoves, long CentipawnLoss);
