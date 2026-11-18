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
/// <param name="PublicUrl">The address the site is served at (<c>Arena:Site:PublicUrl</c>), an absolute http(s) address, or null.
/// Only with it can a page name its canonical address.</param>
public sealed record SiteFrame(int Depth, DateTimeOffset TakenAt, int RatedGames, string? WatchLiveUrl,
    IReadOnlyList<SupportLink> SupportLinks, string? PublicUrl = null)
{
    /// <summary>
    /// The absolute address of the page at <paramref name="sitePath"/> (written from the export root, e.g. <c>games/index.html</c>),
    /// or null when no public address is configured. A folder page is named by its folder, as a browser reaches it.
    /// </summary>
    public string? Canonical(string sitePath)
    {
        if (PublicUrl is null)
        {
            return null;
        }

        var path = sitePath == "index.html" ? "" : sitePath.EndsWith("/index.html", StringComparison.Ordinal) ? sitePath[..^"index.html".Length] : sitePath;
        return PublicUrl.TrimEnd('/') + "/" + path;
    }

    /// <summary>"../" once per folder between the page and the export root.</summary>
    public string Root => string.Concat(Enumerable.Repeat("../", Depth));

    /// <summary>A relative link to <paramref name="sitePath"/>, which is written from the export root, e.g. <c>games/index.html</c>.</summary>
    public string Link(string sitePath) => Root + sitePath;
}
