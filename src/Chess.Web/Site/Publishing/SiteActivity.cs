using Chess.Web.Api;

namespace Chess.Web.Site.Publishing;

/// <summary>When the newest game finished, for auto-publish. Null when no game has finished.</summary>
public interface ISiteActivity
{
    Task<DateTimeOffset?> LatestFinishedAsync(CancellationToken ct);
}

public sealed class ArenaSiteActivity(ArenaApiClient api) : ISiteActivity
{
    public async Task<DateTimeOffset?> LatestFinishedAsync(CancellationToken ct)
    {
        var page = await api.GetGamesAsync(1, 20, null, "finished", ct);
        return page.Items.Select(game => game.FinishedAt).Max();
    }
}
