extern alias ChessWeb;

using Chess.Web.Site;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chess.Web.Tests.Site;

/// <summary>The configured links of 03 §11: "Watch live" and the support links, rendered only when configured and only when safe.</summary>
public class SiteOptionsTests(ConfiguredSiteFixture site) : IClassFixture<ConfiguredSiteFixture>
{
    [Fact]
    public void Watch_live_is_on_every_page_when_configured()
    {
        foreach (var (path, html) in site.Pages)
        {
            Assert.True(html.Contains("href=\"https://www.youtube.com/@arena/live\"", StringComparison.Ordinal) && html.Contains("Watch live", StringComparison.Ordinal),
                $"{path} has no Watch live link.");
        }
    }

    [Fact]
    public void Support_links_are_on_every_page_and_an_unsafe_one_is_not_rendered()
    {
        foreach (var (path, html) in site.Pages)
        {
            Assert.True(html.Contains("href=\"https://ko-fi.com/arena\"", StringComparison.Ordinal), $"{path} has no Ko-fi link.");
            Assert.DoesNotContain("javascript", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Unsafe", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Options_bind_from_arena_site_and_drop_an_unsafe_support_link()
    {
        var options = new SiteOptions
        {
            SupportLinks =
            [
                new SupportLink { Label = "Ko-fi", Url = "https://ko-fi.com/arena" },
                new SupportLink { Label = "Unsafe", Url = "javascript:alert(1)" },
                new SupportLink { Label = "Relative", Url = "/donate" },
                new SupportLink { Label = " ", Url = "https://example.org" },
            ],
        };

        var support = SiteLinks.Support(options);

        Assert.Equal(["Ko-fi"], support.Select(link => link.Label));
    }

    [Fact]
    public void AddSiteExport_binds_arena_site_and_keeps_the_documented_defaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Arena:Site:YouTubeUrl"] = "https://www.youtube.com/@arena/live",
                ["Arena:Site:FullLogGames"] = "50",
            })
            .Build();
        var services = new ServiceCollection().AddLogging().AddSiteExport(configuration);

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<SiteOptions>>().Value;

        Assert.Equal("https://www.youtube.com/@arena/live", options.YouTubeUrl);
        Assert.Equal(50, options.FullLogGames);
        Assert.Empty(options.SupportLinks);
    }
}

/// <summary>An export whose configuration names a "Watch live" address and two support links, one of them unsafe.</summary>
public sealed class ConfiguredSiteFixture : ExportedSite
{
    public override Task InitializeAsync() => ExportWith(false,
        ("Arena:Site:YouTubeUrl", "https://www.youtube.com/@arena/live"),
        ("Arena:Site:SupportLinks:0:Label", "Ko-fi"),
        ("Arena:Site:SupportLinks:0:Url", "https://ko-fi.com/arena"),
        ("Arena:Site:SupportLinks:1:Label", "Unsafe"),
        ("Arena:Site:SupportLinks:1:Url", "javascript:alert(1)"));
}
