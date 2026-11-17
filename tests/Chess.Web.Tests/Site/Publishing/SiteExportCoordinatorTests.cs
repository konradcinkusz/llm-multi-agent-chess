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
    public async Task An_export_empties_the_export_folder_it_made_first()
    {
        var folder = Path.Combine(_git.Root, "export");
        var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder);
        await coordinator.ExportAsync(default);
        File.WriteAllText(Path.Combine(folder, "stale.html"), "old");

        await coordinator.ExportAsync(default);

        Assert.False(File.Exists(Path.Combine(folder, "stale.html")));
        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
        Assert.True(File.Exists(Path.Combine(folder, SiteFolders.ExportMarker)));
    }

    [Fact]
    public async Task A_folder_with_content_the_export_did_not_write_is_refused_and_left_as_it_was()
    {
        var folder = Path.Combine(_git.Root, "export");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
        var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExportAsync(default));
        var published = await coordinator.PublishAsync(default);

        Assert.Contains("did not write", failure.Message);
        Assert.False(published.Published);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(folder, "notes.txt")));
        Assert.False(File.Exists(Path.Combine(folder, "index.html")));
    }

    [Fact]
    public async Task An_empty_export_folder_is_used()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_git.Root, "export")).FullName;
        var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder);

        await coordinator.ExportAsync(default);

        Assert.True(File.Exists(Path.Combine(folder, "index.html")));
    }

    [Fact]
    public async Task The_export_folder_is_never_the_repository_a_folder_in_it_or_a_folder_above_it()
    {
        _git.CreateBareRemote();
        _git.CreateSourceRepository();
        var inside = Path.Combine(_git.Source, "site-export");
        var work = Path.Combine(_git.Root, "work");

        foreach (var folder in new[] { _git.Source, inside, _git.Root })
        {
            var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder, contentRoot: Path.Combine(_git.Source, "src"), workDir: work);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExportAsync(default));

            Assert.Contains("folder of its own", failure.Message);
        }

        Assert.Equal("owner's work", File.ReadAllText(Path.Combine(_git.Source, "README.md")).Trim());
        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public async Task The_export_folder_and_the_work_folder_are_never_nested()
    {
        var folder = Path.Combine(_git.Root, "export");

        foreach (var work in new[] { folder, Path.Combine(folder, "work"), _git.Root })
        {
            var coordinator = Build(new SiteFakeExporter(), new SiteFakePublisher(), Start, folder, workDir: work);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExportAsync(default));

            Assert.Contains("two separate folders", failure.Message);
        }
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

    private SiteExportCoordinator Build(SiteFakeExporter exporter, SiteFakePublisher publisher, DateTimeOffset now, string? folder = null,
        string? contentRoot = null, string? workDir = null)
    {
        var clock = new SiteFakeClock(now);
        exporter.TakenAt = exporter.TakenAt == default ? now : exporter.TakenAt;
        publisher.At = now;
        var options = SiteTestOptions.Create(o =>
        {
            o.ExportDirectory = folder ?? Path.Combine(_git.Root, "export");
            o.WorkDir = workDir ?? _git.WorkDir;
            o.Remote = _git.Remote;
        });
        // The app runs from a folder of its own, as it does from src/Chess.Web, so the export folder is not inside it.
        var app = Directory.CreateDirectory(contentRoot ?? Path.Combine(_git.Root, "app")).FullName;
        return new SiteExportCoordinator(publisher, options, new SiteFakeHostEnvironment(app), clock, exporter);
    }
}
