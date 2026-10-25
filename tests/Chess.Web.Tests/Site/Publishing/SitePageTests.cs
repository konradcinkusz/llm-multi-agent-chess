using Bunit;
using Chess.Web.Components.Pages.Admin;
using Chess.Web.Site.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Site.Publishing;

public class SitePageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 14, 3, 0, TimeSpan.Zero);

    [Fact]
    public void Shows_the_public_address_and_that_nothing_was_published_yet()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, new SiteFakeExporter(), new SiteFakePublisher());

        Assert.Equal("https://owner.github.io/repo/", cut.Find("#site-url").GetAttribute("href"));
        Assert.Contains("Never published", cut.Markup);
        Assert.Contains("Not exported", cut.Markup);
        Assert.Contains("Off.", cut.Markup);
    }

    [Fact]
    public void Publish_shows_the_outcome_and_its_detail_and_the_export_counts()
    {
        using var ctx = new BunitContext();
        var publisher = new SiteFakePublisher { At = Now };
        var cut = Render(ctx, new SiteFakeExporter { TakenAt = Now }, publisher);

        Click(cut, "Publish");

        cut.WaitForAssertion(() => Assert.Equal("Published 3 files", cut.Find("#site-publish-detail").TextContent));
        Assert.Contains("Published at 2026-10-25 14:03 UTC", cut.Markup);
        Assert.Equal("3", cut.Find("#site-pages").TextContent);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public void A_refused_publish_is_shown_as_not_published()
    {
        using var ctx = new BunitContext();
        var cut = Render(ctx, new SiteFakeExporter { TakenAt = Now }, new SiteFakePublisher(published: false) { At = Now });

        Click(cut, "Publish");

        cut.WaitForAssertion(() => Assert.Contains("Not published", cut.Markup));
        Assert.Equal("Refused by the fake", cut.Find("#site-publish-detail").TextContent);
    }

    [Fact]
    public void Export_alone_shows_the_export_without_publishing()
    {
        using var ctx = new BunitContext();
        var publisher = new SiteFakePublisher();
        var cut = Render(ctx, new SiteFakeExporter { TakenAt = Now }, publisher);

        Click(cut, "Export");

        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("#site-pages").TextContent));
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public void A_failing_export_is_shown_as_an_error_and_does_not_throw()
    {
        using var ctx = new BunitContext();
        var exporter = new SiteFakeExporter { Behaviour = _ => throw new IOException("disk full") };
        var cut = Render(ctx, exporter, new SiteFakePublisher());

        Click(cut, "Export");

        cut.WaitForAssertion(() => Assert.Contains("disk full", cut.Find(".error-msg").TextContent));
    }

    [Fact]
    public void A_failing_export_during_a_publish_is_shown_as_not_published()
    {
        using var ctx = new BunitContext();
        var exporter = new SiteFakeExporter { Behaviour = _ => throw new IOException("disk full") };
        var cut = Render(ctx, exporter, new SiteFakePublisher());

        Click(cut, "Publish");

        cut.WaitForAssertion(() => Assert.Contains("Not published", cut.Markup));
        Assert.Equal("The export failed: disk full", cut.Find("#site-publish-detail").TextContent);
    }

    private static IRenderedComponent<SitePage> Render(BunitContext ctx, SiteFakeExporter exporter, SiteFakePublisher publisher)
    {
        var options = SiteTestOptions.Create(o =>
        {
            o.Remote = "https://github.com/owner/repo.git";
            o.ExportDirectory = Path.Combine(Path.GetTempPath(), "arena-site-page-test");
        });
        var coordinator = new SiteExportCoordinator(publisher, options,
            new SiteFakeHostEnvironment(Path.GetTempPath()), new SiteFakeClock(Now), exporter);
        ctx.Services.AddSingleton(options);
        ctx.Services.AddSingleton(coordinator);
        return ctx.Render<SitePage>();
    }

    private static void Click(IRenderedComponent<SitePage> cut, string label) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == label).Click();
}
