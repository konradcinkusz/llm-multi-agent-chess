namespace Chess.Web.Site;

/// <summary>What the public site shows beyond the results. Bound from <c>Arena:Site</c> (03 §11).</summary>
public sealed class SiteOptions
{
    /// <summary>The "Watch live" link (<c>Arena:Site:YouTubeUrl</c>). Not rendered when empty or not an http(s) address.</summary>
    public string? YouTubeUrl { get; set; }

    /// <summary>Support links, a label and a URL each (<c>Arena:Site:SupportLinks</c>). Only http(s) links are rendered.</summary>
    public List<SupportLink> SupportLinks { get; set; } = [];

    /// <summary>Only the newest this many games keep their engine log (<c>Arena:Site:FullLogGames</c>, used by the game pages).</summary>
    public int FullLogGames { get; set; } = 300;
}

/// <summary>One support link. A class rather than a record, so the configuration binder can fill it.</summary>
public sealed class SupportLink
{
    public string Label { get; set; } = "";

    public string Url { get; set; } = "";
}
