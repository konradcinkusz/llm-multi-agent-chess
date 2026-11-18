using Chess.Web.Api;
using Chess.Web.Components.Pages.Games;

namespace Chess.Web.Site;

/// <summary>One row of the public games list: the game and what it cost, when its usage was read.</summary>
public sealed record SiteGameRow(GameSummaryDto Game, GameUsageDto? Usage);

/// <summary>
/// The games the public site lists: finished games that have a result, newest first, in pages of <see cref="GameArchive.PageSize"/>.
/// A game that ended undecided (result <c>*</c>: a cancelled tournament's queued game, a game cut off by a restart) has no
/// PGN and nothing to rank, so it is not on the site, and neither are its attempts (03 §11).
/// </summary>
public static class SiteGames
{
    /// <summary>The result of a game that nobody won or drew: it was aborted.</summary>
    public const string Undecided = "*";

    /// <summary>True when the game finished with a result (a win or a draw), so it belongs on the site.</summary>
    public static bool IsPublic(GameSummaryDto game) => game.State == "finished" && game.Result != Undecided;

    /// <summary>Finished games that have a result (aborted ones left out), newest first, ties broken by id.</summary>
    public static IReadOnlyList<GameSummaryDto> Finished(IEnumerable<GameSummaryDto> games) =>
        games.Where(IsPublic)
            .OrderByDescending(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .ToList();

    /// <summary>The 1-based page of <paramref name="games"/>; a page past the end is empty.</summary>
    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> games, int page) =>
        games.Skip((page - 1) * GameArchive.PageSize).Take(GameArchive.PageSize).ToList();
}
