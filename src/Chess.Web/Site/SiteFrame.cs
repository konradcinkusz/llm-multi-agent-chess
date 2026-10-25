namespace Chess.Web.Site;

/// <summary>
/// What every page of the export is told: where the page sits, the snapshot it shows and the external links it may show.
/// Every link on a page is relative to the page's own folder through <see cref="Link"/>, so the export works from
/// <c>file://</c> and under any sub-path (e.g. <c>/llm-multi-agent-chess/</c>).
/// </summary>
/// <param name="Depth">Folders between the export root and the page: 0 for <c>index.html</c>, 1 for <c>games/index.html</c>.</param>
/// <param name="TakenAt">The snapshot time, UTC.</param>
/// <param name="RatedGames">Rated games in the snapshot.</param>
/// <param name="WatchLiveUrl">The configured "Watch live" address, or null when none is configured.</param>
/// <param name="SupportLinks">The configured support links; empty when none is configured.</param>
public sealed record SiteFrame(int Depth, DateTimeOffset TakenAt, int RatedGames, string? WatchLiveUrl,
    IReadOnlyList<SupportLink> SupportLinks)
{
    /// <summary>"../" once per folder between the page and the export root.</summary>
    public string Root => string.Concat(Enumerable.Repeat("../", Depth));

    /// <summary>A relative link to <paramref name="sitePath"/>, which is written from the export root, e.g. <c>games/index.html</c>.</summary>
    public string Link(string sitePath) => Root + sitePath;
}
