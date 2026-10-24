using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Tournaments;

/// <summary>The games of one tournament, for the detail page.</summary>
public static class TournamentGames
{
    /// <summary>The largest page <c>GET /api/games</c> returns.</summary>
    public const int PageSize = 200;

    /// <summary>
    /// Every game of the tournament, oldest first. <c>GET /api/games</c> has no tournament filter, so the archive is read
    /// page by page and kept to the games whose <c>TournamentId</c> matches.
    /// </summary>
    public static async Task<IReadOnlyList<GameSummaryDto>> LoadAsync(ArenaApiClient api, Guid tournamentId, CancellationToken ct)
    {
        var games = new List<GameSummaryDto>();
        for (var page = 1; ; page++)
        {
            var result = await api.GetGamesAsync(page, PageSize, null, null, ct);
            games.AddRange(result.Items.Where(game => game.TournamentId == tournamentId));
            if (result.Items.Count == 0 || page * PageSize >= result.Total)
            {
                break;
            }
        }

        return games.OrderBy(game => game.CreatedAt).ToArray();
    }
}
