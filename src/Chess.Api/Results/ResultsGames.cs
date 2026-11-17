using System.Linq.Expressions;
using Chess.Api.Persistence;
using Chess.Api.Persistence.Entities;

namespace Chess.Api.Results;

/// <summary>A finished game as the results code reads it.</summary>
internal sealed record ScoredGame(
    Guid Id, string WhiteKind, string WhiteModel, string BlackKind, string BlackModel, string Result, string? Termination);

/// <summary>One row of <c>moves</c> with the fields the results code reads.</summary>
internal sealed record MoveRow(
    Guid GameId, string FenAfter, int Attempts, long LatencyMs, int? InputTokens, int? OutputTokens, int? CentipawnLoss);

/// <summary>
/// The moves of one side of one pairing over a set of games, summed by the database. A sum is over the moves that have the
/// value; its count says how many those are. No column of <c>moves</c> but the sums leaves the database.
/// </summary>
internal sealed record MoveTotal(
    string Color, string WhiteModel, string BlackModel,
    int Moves, int FirstAttemptLegal, long LatencyMsSum,
    int InputTokensCount, long InputTokensSum, int OutputTokensCount, long OutputTokensSum,
    int CentipawnLossCount, long CentipawnLossSum);

/// <summary>One row of <c>move_attempts</c> with the columns the cost of a game needs. Ply 1 is White's first reply.</summary>
internal sealed record ReplyRow(
    Guid GameId, int Ply, int? InputTokens, int? OutputTokens, int? ReasoningTokens, int? CachedInputTokens, double? AiCredits);

internal static class ResultsGames
{
    public const string LlmKind = "llm";
    public const string ForfeitWhite = "forfeit-white";
    public const string ForfeitBlack = "forfeit-black";

    public static readonly Expression<Func<GameEntity, ScoredGame>> Project = g => new ScoredGame(
        g.Id, g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel, g.Result, g.Termination);

    /// <summary>
    /// Finished games with a decided result. A result of <c>*</c> (aborted, interrupted, still running) is
    /// not a game for rating or results (RATING.md §1).
    /// </summary>
    public static IQueryable<GameEntity> Decided(ArenaDbContext db) => db.Games.Where(g =>
        g.FinishedAt != null && (g.Result == "1-0" || g.Result == "0-1" || g.Result == "1/2-1/2"));

    public static IQueryable<GameEntity> RatedDecided(ArenaDbContext db) => Decided(db).Where(g => g.Rated);

    public static IQueryable<MoveRow> MovesOf(ArenaDbContext db, IQueryable<GameEntity> games) => games.Join(
        db.Moves,
        g => g.Id,
        m => m.GameId,
        (g, m) => new MoveRow(m.GameId, m.FenAfter, m.Attempts, m.LatencyMs, m.InputTokens, m.OutputTokens, m.CentipawnLoss));

    /// <summary>
    /// The move statistics of <paramref name="games"/>, grouped by the colour that moved and the pairing, so a model's
    /// moves are the groups where it was that colour. A join, then a group by plain columns, then aggregates: the
    /// shape PostgreSQL translates (a test translates it with Npgsql).
    /// </summary>
    internal static IQueryable<MoveTotal> MoveTotalsOf(ArenaDbContext db, IQueryable<GameEntity> games) => games
        .Join(
            db.Moves,
            g => g.Id,
            m => m.GameId,
            (g, m) => new { m.Color, g.WhiteModel, g.BlackModel, m.Attempts, m.LatencyMs, m.InputTokens, m.OutputTokens, m.CentipawnLoss })
        .GroupBy(x => new { x.Color, x.WhiteModel, x.BlackModel })
        .Select(group => new MoveTotal(
            group.Key.Color, group.Key.WhiteModel, group.Key.BlackModel,
            group.Count(),
            group.Count(x => x.Attempts == 1),
            group.Sum(x => x.LatencyMs),
            group.Count(x => x.InputTokens != null),
            group.Sum(x => (long?)x.InputTokens) ?? 0,
            group.Count(x => x.OutputTokens != null),
            group.Sum(x => (long?)x.OutputTokens) ?? 0,
            group.Count(x => x.CentipawnLoss != null),
            group.Sum(x => (long?)x.CentipawnLoss) ?? 0));

    public static IQueryable<ReplyRow> RepliesOf(ArenaDbContext db, IQueryable<GameEntity> games) => games.Join(
        db.MoveAttempts,
        g => g.Id,
        a => a.GameId,
        (g, a) => new ReplyRow(a.GameId, a.Ply, a.InputTokens, a.OutputTokens, a.ReasoningTokens, a.CachedInputTokens, a.AiCredits));

    /// <summary>White's score: 1, ½ or 0.</summary>
    public static double WhiteScore(string result) => result switch
    {
        "1-0" => 1,
        "0-1" => 0,
        "1/2-1/2" => 0.5,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Not a decided result."),
    };

    /// <summary>
    /// The side that made a move, read from <c>fen_after</c>: the side to move there is the opponent.
    /// </summary>
    public static bool WhiteMoved(string fenAfter) => fenAfter.Split(' ')[1] == "b";

    /// <summary>The kind and model of the side that made a move.</summary>
    public static (string Kind, string Model) Mover(ScoredGame game, string fenAfter) => WhiteMoved(fenAfter)
        ? (game.WhiteKind, game.WhiteModel)
        : (game.BlackKind, game.BlackModel);
}
