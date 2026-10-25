using System.Reflection;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Layout;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Components;

public class NavigationTests
{
    // Every route of 03 §6 and the planner page of 03 §9.4, in the order they are listed there.
    private static readonly string[] Section6Routes =
    [
        "/results", "/", "/head-to-head", "/tournaments", "/tournaments/{Id:guid}", "/benchmark", "/benchmark/{Id:guid}",
        "/games", "/games/{Id:guid}", "/admin/attempts", "/broadcast/plan",
        // Not in 03 §6: the public-site admin page (ADR-013, Site/Publishing), listed here so the route set stays exact.
        "/admin/site",
    ];

    // "/broadcast" is the chrome-less OBS page (03 §9.4): a route, but deliberately NOT in the navigation.
    private static readonly string[] ObsRoutes = ["/broadcast"];

    // The navigation shows the top-level routes, Results first; the detail routes are reached from their lists.
    private static readonly string[] NavHrefs =
        ["/results", "/", "/head-to-head", "/tournaments", "/benchmark", "/broadcast/plan", "/games", "/admin/attempts", "/admin/site"];

    [Fact]
    public void EveryRouteOfSection6IsServedByAPlaceholderPage()
    {
        var routes = typeof(ResultsPage).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Chess.Web.Components.Pages", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => r.Template)
            .Except(ObsRoutes)
            .Order()
            .ToArray();

        Assert.Equal(Section6Routes.Order().ToArray(), routes);
    }

    [Fact]
    public void ResultsIsTheFirstNavigationItem()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NavMenu>();

        var links = cut.FindAll("a");
        Assert.Equal("Results", links[0].TextContent);
        Assert.Equal("/results", links[0].GetAttribute("href"));
    }

    [Fact]
    public void TheNavigationLinksEveryTopLevelRouteInOrder()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<NavMenu>();

        var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")!).ToArray();
        Assert.Equal(NavHrefs, hrefs);
    }

    [Fact]
    public void TheShellWrapsHeaderStatusStripAndMainInThatOrder()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"Ready","copilotDetail":"signed in","database":"healthy","stockfish":"healthy"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p>Page body</p>"))));

        var shell = cut.Find(".app-shell");
        Assert.Equal(new[] { "HEADER", "DIV", "MAIN" }, shell.Children.Select(c => c.TagName).ToArray());
        Assert.Equal("♞", cut.Find(".brand-mark").TextContent);
        Assert.Contains("LLM Chess Arena", cut.Find(".brand").TextContent);
        Assert.Contains("Page body", cut.Find("main.app-main").TextContent);
    }
}
