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
