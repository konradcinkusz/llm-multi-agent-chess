using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>
/// The owner's decision: GitHub Pages is deployed from the publish-site workflow (Settings, Pages, Source = GitHub Actions). The
/// texts that tell the owner what to set must say the same, and the workflow must give the Pages permissions to its deploy job only.
/// </summary>
public class PagesDeploymentTests
{
    private static readonly string[] Texts =
    [
        "README.md",
        "CONTRIBUTING.md",
        "docs/RESULTS-ARCHIVE.md",
        "docs/architecture/05-DECISIONS.md",
        "docs/architecture/03-TARGET-ARCHITECTURE.md",
        "src/Chess.Web/Components/Pages/Admin/SitePage.razor",
        ".github/workflows/publish-site.yml",
        ".github/workflows/deploy-pages.yml",
    ];

    [Fact]
    public void No_text_tells_the_owner_to_deploy_pages_from_a_branch()
    {
        foreach (var text in Texts)
        {
            Assert.DoesNotContain("Deploy from a branch", Read(text), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_texts_that_set_up_pages_name_github_actions_as_the_source()
    {
        foreach (var text in new[] { "README.md", "docs/RESULTS-ARCHIVE.md", "src/Chess.Web/Components/Pages/Admin/SitePage.razor", ".github/workflows/publish-site.yml" })
        {
            Assert.Contains("GitHub Actions", Read(text), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_the_deploy_job_of_the_workflow_may_write_pages()
    {
        var workflow = Read(".github/workflows/publish-site.yml");
        var deploy = workflow.IndexOf("\n  deploy:", StringComparison.Ordinal);
        var build = workflow.IndexOf("\n  build:", StringComparison.Ordinal);

        Assert.True(build > 0 && deploy > build);
        Assert.Contains("\npermissions: {}", workflow, StringComparison.Ordinal);
        var beforeDeploy = workflow[..deploy];
        var fromDeploy = workflow[deploy..];
        Assert.DoesNotContain("pages: write", beforeDeploy, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", beforeDeploy, StringComparison.Ordinal);
        Assert.Contains("pages: write", fromDeploy, StringComparison.Ordinal);
        Assert.Contains("id-token: write", fromDeploy, StringComparison.Ordinal);
        Assert.Contains("contents: read", beforeDeploy, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_job_has_a_timeout_and_the_dotnet_setup_is_the_one_ci_uses()
    {
        var workflow = Read(".github/workflows/publish-site.yml");

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(workflow, "timeout-minutes:").Count);
        Assert.Contains("actions/setup-dotnet@v6", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v6", Read(".github/workflows/ci.yml"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_workflow_runs_for_changes_to_the_site_code_and_not_only_the_archive()
    {
        var workflow = Read(".github/workflows/publish-site.yml");

        foreach (var path in new[] { "results/**", "src/Chess.Web/Site/**", "src/Chess.Web/Components/Site/**", "src/Chess.Api/Results/**", "src/Chess.Web/wwwroot/**" })
        {
            Assert.Contains($"'{path}'", workflow, StringComparison.Ordinal);
        }
    }

    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "global.json")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar)));
    }
}
