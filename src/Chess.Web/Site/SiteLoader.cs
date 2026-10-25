using Chess.Web.Api;
using Chess.Web.Components.Charts;
using Chess.Web.Components.Pages.Results;

namespace Chess.Web.Site;

/// <summary>
/// Reads everything the export shows from the API, once. Every call goes through <see cref="ArenaApiClient"/> or the
/// same adapter the Results page uses, so the site shows what the app shows.
/// </summary>
internal sealed class SiteLoader(ArenaApiClient api, TournamentRatingsClient ratings)
{
    /// <summary>The largest page <c>GET /api/games</c> returns (03 §5).</summary>
    private const int MaxGamesPage = 200;

    public async Task<SiteSnapshot> LoadAsync(DateTimeOffset takenAt, CancellationToken ct)
    {
        var board = await api.GetLeaderboardAsync(ct);
        var models = board.Models.Select(m => m.Model).ToList();
        var matrix = await api.GetRatingMatrixAsync(ct);
        var breakdown = await api.GetResultsBreakdownAsync(null, ct);

        // The history is read only for the models that get a palette colour, as on the Results page.
        var history = new Dictionary<string, IReadOnlyList<RatingHistoryPointDto>>(StringComparer.Ordinal);
        foreach (var model in ChartPalette.AssignSlots(models).Keys)
        {
            history[model] = await api.GetRatingHistoryAsync(model, ct);
        }

        var tournaments = await api.GetTournamentsAsync(ct);
        var tournamentBoards = new Dictionary<Guid, LeaderboardDto>();
        foreach (var tournament in tournaments)
        {
            tournamentBoards[tournament.Id] = await ratings.GetLeaderboardAsync(tournament.Id, ct);
        }

        var games = SiteGames.Finished(await ReadAllGamesAsync(ct));

        // Usage is per game (03 §10), so the cost of every finished game is read one request at a time.
        var usage = new Dictionary<Guid, GameUsageDto>();
        foreach (var game in games)
        {
            usage[game.Id] = await api.GetGameUsageAsync(game.Id, ct);
        }

        return new SiteSnapshot(takenAt, board, matrix, breakdown, history, tournaments, tournamentBoards, games, usage);
    }

    /// <summary>Every game of the archive: <c>GET /api/games</c> has no tournament filter and no way to ask for finished only.</summary>
    private async Task<List<GameSummaryDto>> ReadAllGamesAsync(CancellationToken ct)
    {
        var games = new List<GameSummaryDto>();
        for (var page = 1; ; page++)
        {
            var result = await api.GetGamesAsync(page, MaxGamesPage, null, null, ct);
            games.AddRange(result.Items);
            if (result.Items.Count == 0 || page * MaxGamesPage >= result.Total)
            {
                return games;
            }
        }
    }
}
