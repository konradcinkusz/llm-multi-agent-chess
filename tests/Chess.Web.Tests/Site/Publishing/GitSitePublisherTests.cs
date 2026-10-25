using Chess.Web.Site.Publishing;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

/// <summary>03 §11 / ADR-013 against a local bare repository as the remote: never GitHub, never the network.</summary>
public class GitSitePublisherTests : IDisposable
{
    private readonly SiteTestGit _git = new();

    public void Dispose() => _git.Dispose();

    [Fact]
    public async Task First_publish_creates_the_branch_with_one_commit_and_the_export()
    {
        _git.CreateBareRemote();
        var site = SiteTestGit.WriteSite(Path.Combine(_git.Root, "export"));

        var result = await Publisher(_git.Remote).PublishAsync(site, default);

        Assert.True(result.Published, result.Detail);
        Assert.Equal("1", SiteTestGit.Run(_git.Remote, "rev-list", "--count", "gh-pages"));
        Assert.Equal(result.Commit, SiteTestGit.Run(_git.Remote, "rev-parse", "gh-pages"));
        Assert.Contains("<h1>Results</h1>", SiteTestGit.Run(_git.Remote, "show", "gh-pages:index.html"));
        Assert.Contains("about/index.html", SiteTestGit.Run(_git.Remote, "ls-tree", "-r", "--name-only", "gh-pages"));
        Assert.Contains(".nojekyll", SiteTestGit.Run(_git.Remote, "ls-tree", "--name-only", "gh-pages"));
    }

    [Fact]
    public async Task Second_publish_still_leaves_one_commit_and_replaces_the_content()
    {
        _git.CreateBareRemote();
        var publisher = Publisher(_git.Remote);
        await publisher.PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "first"), "Old"), default);
        var second = SiteTestGit.WriteSite(Path.Combine(_git.Root, "second"), "New");
        File.Delete(Path.Combine(second, "about", "index.html"));

        var result = await publisher.PublishAsync(second, default);

        Assert.True(result.Published, result.Detail);
        Assert.Equal("1", SiteTestGit.Run(_git.Remote, "rev-list", "--count", "gh-pages"));
        Assert.Contains("<h1>New</h1>", SiteTestGit.Run(_git.Remote, "show", "gh-pages:index.html"));
        Assert.DoesNotContain("about/index.html", SiteTestGit.Run(_git.Remote, "ls-tree", "-r", "--name-only", "gh-pages"));
    }

    [Fact]
    public async Task The_owners_working_tree_and_main_are_untouched()
    {
        _git.CreateBareRemote();
        _git.CreateSourceRepository();
        var mainBefore = SiteTestGit.Run(_git.Source, "rev-parse", "main");
        var site = SiteTestGit.WriteSite(Path.Combine(_git.Root, "export"));

        var result = await Publisher(remote: null, sourceDirectory: _git.Source).PublishAsync(site, default);

        Assert.True(result.Published, result.Detail);
        Assert.Equal(mainBefore, SiteTestGit.Run(_git.Source, "rev-parse", "main"));
        Assert.Equal(string.Empty, SiteTestGit.Run(_git.Source, "status", "--porcelain"));
        Assert.Equal(string.Empty, SiteTestGit.Run(_git.Source, "branch", "--list", "gh-pages"));
        Assert.Equal("owner's work", File.ReadAllText(Path.Combine(_git.Source, "README.md")).Trim());
        Assert.Contains("<h1>", SiteTestGit.Run(_git.Remote, "show", "gh-pages:index.html"));
    }

    [Fact]
    public async Task A_remote_is_taken_from_the_origin_of_the_repository_when_none_is_configured()
    {
        _git.CreateBareRemote();
        _git.CreateSourceRepository();

        var result = await Publisher(remote: null, sourceDirectory: _git.Source)
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.True(result.Published, result.Detail);
        Assert.Equal("1", SiteTestGit.Run(_git.Remote, "rev-list", "--count", "gh-pages"));
    }

    [Fact]
    public async Task An_export_without_index_html_is_refused_and_nothing_is_pushed()
    {
        _git.CreateBareRemote();
        var empty = Directory.CreateDirectory(Path.Combine(_git.Root, "empty")).FullName;

        var result = await Publisher(_git.Remote).PublishAsync(empty, default);

        Assert.False(result.Published);
        Assert.Contains("no index.html", result.Detail);
        Assert.Equal(string.Empty, SiteTestGit.Run(_git.Remote, "branch", "--list", "gh-pages"));
    }

    [Fact]
    public async Task No_origin_and_no_configured_remote_is_reported_not_thrown()
    {
        var plain = Directory.CreateDirectory(Path.Combine(_git.Root, "plain")).FullName;

        var result = await Publisher(remote: null, sourceDirectory: plain)
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.Contains("No remote", result.Detail);
    }

    [Fact]
    public async Task A_refused_push_is_reported_with_the_git_message()
    {
        var result = await Publisher(Path.Combine(_git.Root, "missing.git"))
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.Contains("was refused", result.Detail);
        Assert.Contains("missing.git", result.Detail);
    }

    [Fact]
    public async Task A_missing_git_executable_is_reported_not_thrown()
    {
        var result = await Publisher(_git.Remote, git: "arena-no-such-git")
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.Contains("not installed", result.Detail);
    }

    [Fact]
    public async Task A_remote_with_credentials_never_shows_them_in_the_detail()
    {
        var result = await Publisher("https://user:secret-token@127.0.0.1:9/none.git")
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.DoesNotContain("secret-token", result.Detail);
    }

    [Fact]
    public async Task The_work_folder_must_not_be_the_repository_the_app_runs_from()
    {
        _git.CreateBareRemote();
        _git.CreateSourceRepository();
        var source = _git.Source;

        var result = await Publisher(_git.Remote, workDir: source, sourceDirectory: source)
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.Contains("folder of its own", result.Detail);
        Assert.True(File.Exists(Path.Combine(source, "README.md")));
    }

    [Fact]
    public async Task A_folder_with_content_that_the_publisher_did_not_create_is_never_deleted()
    {
        _git.CreateBareRemote();
        Directory.CreateDirectory(_git.WorkDir);
        var keep = Path.Combine(_git.WorkDir, "keep.txt");
        File.WriteAllText(keep, "not ours");

        var result = await Publisher(_git.Remote, workDir: _git.WorkDir)
            .PublishAsync(SiteTestGit.WriteSite(Path.Combine(_git.Root, "export")), default);

        Assert.False(result.Published);
        Assert.Contains("not a publisher folder", result.Detail);
        Assert.Equal("not ours", File.ReadAllText(keep));
    }

    private GitSitePublisher Publisher(string? remote, string? workDir = null, string? sourceDirectory = null, string git = "git") =>
        new(Options.Create(new SitePublishingOptions
            {
                Remote = remote,
                WorkDir = workDir ?? _git.WorkDir,
                GitExecutable = git,
            }),
            TimeProvider.System,
            sourceDirectory ?? Directory.CreateDirectory(Path.Combine(_git.Root, "app")).FullName);
}
