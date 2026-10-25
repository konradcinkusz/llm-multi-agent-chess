namespace Chess.Web.Site;

/// <summary>The external links the public site may show. Anything that is not an absolute http(s) address is dropped.</summary>
public static class SiteLinks
{
    /// <summary>The address when it is an absolute http or https URL; otherwise null, so a <c>javascript:</c> or relative value never reaches a page.</summary>
    public static string? Safe(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;

    /// <summary>The support links that are configured and safe, in configuration order.</summary>
    public static IReadOnlyList<SupportLink> Support(SiteOptions options) =>
        options.SupportLinks
            .Where(link => !string.IsNullOrWhiteSpace(link.Label) && Safe(link.Url) is not null)
            .Select(link => new SupportLink { Label = link.Label.Trim(), Url = Safe(link.Url)! })
            .ToList();
}
