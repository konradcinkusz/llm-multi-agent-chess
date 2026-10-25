using Chess.Web.Api;
using Chess.Web.Components.Pages.Games;

namespace Chess.Web.Site;

/// <summary>One row of the public games list: the game and what it cost, when its usage was read.</summary>
public sealed record SiteGameRow(GameSummaryDto Game, GameUsageDto? Usage);

/// <summary>The games the public site lists: finished games only, newest first, in pages of <see cref="GameArchive.PageSize"/>.</summary>
public static class SiteGames
{
    /// <summary>Finished games (aborted ones included, as the archive shows them), newest first, ties broken by id.</summary>
    public static IReadOnlyList<GameSummaryDto> Finished(IEnumerable<GameSummaryDto> games) =>
        games.Where(g => g.State == "finished")
            .OrderByDescending(g => g.CreatedAt)
            .ThenBy(g => g.Id)
            .ToList();

    /// <summary>The 1-based page of <paramref name="games"/>; a page past the end is empty.</summary>
    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> games, int page) =>
        games.Skip((page - 1) * GameArchive.PageSize).Take(GameArchive.PageSize).ToList();
}
