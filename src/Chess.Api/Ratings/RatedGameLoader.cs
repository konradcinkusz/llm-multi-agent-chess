using Chess.Api.Persistence;
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

        var rated = db.Games.AsNoTracking().Where(g => g.Rated && g.Result != Unfinished);
        if (tournamentId is { } id)
        {
            rated = rated.Where(g => g.TournamentId == id);
        }

        var games = await rated
            // Order before projecting: Npgsql cannot translate an OrderBy over a constructed record.
            .OrderBy(g => g.Id)
            .Select(g => new GameRow(g.Id, g.Result, g.WhiteKind, g.WhiteModel, g.BlackKind, g.BlackModel))
            .ToListAsync(ct);

        var moves = await db.Moves.AsNoTracking()
            .Where(m => rated.Any(g => g.Id == m.GameId))
            .Select(m => new MoveRow(m.GameId, m.Color, m.Attempts, m.CentipawnLoss))
            .ToListAsync(ct);

        var sides = games.ToDictionary(
            g => g.Id,
            g => new Sides(ModelOf(g.WhiteKind, g.WhiteModel), ModelOf(g.BlackKind, g.BlackModel)));

        var outcomes = games
            .Select(g => new GameOutcome(sides[g.Id].White, sides[g.Id].Black, ScoreOf(g.Result)))
            .ToList();

        var stats = moves
            .GroupBy(m => IsWhite(m.Color) ? sides[m.GameId].White : sides[m.GameId].Black, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => StatsOf(group.Key, group.ToList()))
            .ToList();

        return new RatedGames(outcomes, stats);
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
    private static PlayerMoveStats StatsOf(string model, IReadOnlyList<MoveRow> moves)
    {
        var losses = moves.Select(m => m.CentipawnLoss).ToList();
        double? acpl = losses.All(loss => loss is not null) ? losses.Average() : null;
        return new PlayerMoveStats(model, moves.Count, moves.Count(m => m.Attempts == 1), acpl);
    }

    private sealed record GameRow(Guid Id, string Result, string WhiteKind, string WhiteModel, string BlackKind, string BlackModel);

    private sealed record Sides(string White, string Black);

    private sealed record MoveRow(Guid GameId, string Color, int Attempts, int? CentipawnLoss);
}
