using Chess.Web.Site;
using Chess.Web.Site.Publishing;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

public class SiteExportCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
    private readonly SiteTestGit _git = new();

    public void Dispose() => _git.Dispose();

    [Fact]
    public async Task An_export_with_no_pages_is_refused_and_the_publisher_is_never_called()
    {
        var exporter = new SiteFakeExporter { Behaviour = _ => new SiteExportResult(0, 0, 0, Start) };
        var publisher = new SiteFakePublisher();
        var coordinator = Build(exporter, publisher, Start);

        var result = await coordinator.PublishAsync(default);

        Assert.False(result.Published);
        Assert.Contains("no pages", result.Detail);
        Assert.Empty(publisher.Published);
        Assert.Same(result, coordinator.LastPublish);
    }

    [Fact]
    public async Task A_publish_moves_the_published_through_mark_to_the_export_time()
    {
        var exportedAt = Start.AddMinutes(12);
        var exporter = new SiteFakeExporter { TakenAt = exportedAt };
        var coordinator = Build(exporter, new SiteFakePublisher(), Start);

        var result = await coordinator.PublishAsync(default);

        Assert.True(result.Published);
        Assert.Equal(exportedAt, coordinator.PublishedThrough);
        Assert.NotNull(coordinator.LastExport);
        Assert.Equal(3, coordinator.LastExport!.Pages);
    }

    [Fact]
    public async Task A_failed_publish_does_not_move_the_published_through_mark()
    {
        var coordinator = Build(new SiteFakeExporter { TakenAt = Start.AddMinutes(12) }, new SiteFakePublisher(published: false), Start);

        await coordinator.PublishAsync(default);

        Assert.Equal(Start, coordinator.PublishedThrough);
    }

    [Fact]
    public async Task An_export_empties_the_export_folder_first()
    {
        var folder = Path.Combine(_git.Root, "export");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "stale.html"), "old");
        var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder);

        await coordinator.ExportAsync(default);

        Assert.False(File.Exists(Path.Combine(folder, "stale.html")));
        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
    }

    [Fact]
    public async Task An_exporter_that_throws_is_reported_by_a_publish_and_nothing_is_published()
    {
        var exporter = new SiteFakeExporter { Behaviour = _ => throw new IOException("disk full") };
        var publisher = new SiteFakePublisher();
        var coordinator = Build(exporter, publisher, Start);

        var result = await coordinator.PublishAsync(default);

        Assert.False(result.Published);
        Assert.Contains("disk full", result.Detail);
        Assert.Empty(publisher.Published);
        Assert.False(coordinator.Busy);
    }

    private SiteExportCoordinator Build(SiteFakeExporter exporter, SiteFakePublisher publisher, DateTimeOffset now, string? folder = null)
    {
        var clock = new SiteFakeClock(now);
        exporter.TakenAt = exporter.TakenAt == default ? now : exporter.TakenAt;
        publisher.At = now;
        var options = SiteTestOptions.Create(o =>
        {
            o.ExportDirectory = folder ?? Path.Combine(_git.Root, "export");
            o.Remote = _git.Remote;
        });
        return new SiteExportCoordinator(exporter, publisher, options, new SiteFakeHostEnvironment(_git.Root), clock);
    }
}
