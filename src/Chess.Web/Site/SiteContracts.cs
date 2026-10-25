namespace Chess.Web.Site;

// 03 §11 (ADR-013): the public results site is a static export, published to the gh-pages branch.

/// <summary>Renders every public page of the arena into <paramref name="outputDirectory"/> as static files.</summary>
public interface ISiteExporter
{
    Task<SiteExportResult> ExportAsync(string outputDirectory, CancellationToken ct);
}

public sealed record SiteExportResult(int Pages, int Games, long Bytes, DateTimeOffset TakenAt);

/// <summary>Publishes an exported site. Reports failures in the result; never throws for an expected failure.</summary>
public interface ISitePublisher
{
    Task<SitePublishResult> PublishAsync(string siteDirectory, CancellationToken ct);
}

public sealed record SitePublishResult(bool Published, string? Commit, string Detail, DateTimeOffset At);
