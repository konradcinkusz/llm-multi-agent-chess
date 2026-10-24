using System.Reflection;
using Bunit;
using Chess.Web.Components.Layout;
using Chess.Web.Components.Pages.Results;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Chess.Web.Tests.Components;

public class NavigationTests
{
    // Every route of 03 §6 and the planner page of 03 §9.4, in the order they are listed there.
    private static readonly string[] Section6Routes =
    [
        "/results", "/", "/head-to-head", "/tournaments", "/tournaments/{Id:guid}",
        "/games", "/games/{Id:guid}", "/admin/attempts", "/broadcast/plan",
    ];

    // "/broadcast" is the chrome-less OBS page (03 §9.4): a route, but deliberately NOT in the navigation.
    private static readonly string[] ObsRoutes = ["/broadcast"];

    // The navigation shows the top-level routes, Results first; the detail routes are reached from their lists.
    private static readonly string[] NavHrefs =
        ["/results", "/", "/head-to-head", "/tournaments", "/broadcast/plan", "/games", "/admin/attempts"];

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
}
