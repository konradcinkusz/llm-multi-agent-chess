extern alias ChessWeb;

using Chess.Web.Site.Publishing;
using Chess.Web.Tests.Site;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

/// <summary>The real exporter and the git publisher, through the app's own registrations, into a local bare repository.</summary>
public class SitePublishEndToEndTests : IDisposable
{
    private readonly SiteTestGit _git = new();

    public void Dispose() => _git.Dispose();

    [Fact]
    public async Task A_real_export_publishes_one_commit_to_the_bare_remote()
    {
        _git.CreateBareRemote();
        await using var app = new SiteExportApp(false,
            ("Arena:Site:Remote", _git.Remote),
            ("Arena:Site:WorkDir", _git.WorkDir),
            ("Arena:Site:ExportDirectory", Path.Combine(_git.Root, "export")));
        var coordinator = app.Services.GetRequiredService<SiteExportCoordinator>();

        var result = await coordinator.PublishAsync(default);

        Assert.True(result.Published, result.Detail);
        Assert.Equal("1", SiteTestGit.Run(_git.Remote, "rev-list", "--count", "gh-pages"));
        var files = SiteTestGit.Run(_git.Remote, "ls-tree", "-r", "--name-only", "gh-pages");
        Assert.Contains("index.html", files);
        Assert.Contains(".nojekyll", files);
        Assert.Contains("<html", SiteTestGit.Run(_git.Remote, "show", "gh-pages:index.html"));
        Assert.Equal(result.Commit, SiteTestGit.Run(_git.Remote, "rev-parse", "gh-pages"));
    }
}
