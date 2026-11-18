using Chess.Web.Api;

namespace Chess.Web.Site.Publishing;

/// <summary>
/// How many games have finished, for auto-publish. A count rather than a time: the games of one run (a tournament, a
/// benchmark) share one creation time and the games list is ordered by it, so the newest games by that order are not the ones
/// that finished last, and a later finish inside an older run would never be seen. The total only grows when a game finishes
/// (and shrinks when one is deleted, which changes the site as well).
/// </summary>
public interface ISiteActivity
{
    Task<int> FinishedGamesAsync(CancellationToken ct);
}

public sealed class ArenaSiteActivity(ArenaApiClient api) : ISiteActivity
{
    public async Task<int> FinishedGamesAsync(CancellationToken ct) =>
        (await api.GetGamesAsync(1, 1, null, "finished", ct)).Total;
}
