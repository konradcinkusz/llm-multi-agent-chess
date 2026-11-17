using Bunit;
using Chess.Web.Api;
using Chess.Web.Components;
using Chess.Web.Components.Layout;
using Chess.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Components;

/// <summary>The shell's own error states: the router's not-found page and the error boundary around the page body.</summary>
public class ErrorStatesTests
{
    private const string Status =
        """{"copilot":"Ready","copilotDetail":"signed in","database":"healthy","stockfish":"healthy"}""";

    [Fact]
    public void An_unknown_address_in_the_circuit_shows_the_not_found_page_in_the_layout()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(new FakeHandler(_ => Canned.Json(Status)))));
        ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/no-such-page");

        var cut = ctx.Render<Routes>();

        cut.WaitForAssertion(() => Assert.Contains("Page not found", cut.Find("h1").TextContent));
        Assert.NotNull(cut.Find(".app-shell"));
        Assert.Equal("/results", cut.Find("[data-testid='not-found-results']").GetAttribute("href"));
    }

    [Fact]
    public void A_page_that_throws_shows_a_reload_state_and_the_layout_stays()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(new FakeHandler(_ => Canned.Json(Status)))));

        var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, ThrowerBody));

        var error = cut.WaitForElement("[data-testid='page-error']");
        Assert.Contains("Something went wrong on this page", error.TextContent);
        Assert.NotNull(cut.Find("[data-testid='page-error-reload']"));
        Assert.NotNull(cut.Find("nav[aria-label='Main']"));
        Assert.Empty(cut.FindAll("[data-testid='thrower']"));
    }

    [Fact]
    public void The_next_page_starts_clean_after_a_page_threw()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(new FakeHandler(_ => Canned.Json(Status)))));
        var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, ThrowerBody));
        cut.WaitForElement("[data-testid='page-error']");

        cut.Render(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p data-testid=\"fine\">All good</p>"))));

        Assert.Empty(cut.FindAll("[data-testid='page-error']"));
        Assert.Equal("All good", cut.Find("[data-testid='fine']").TextContent);
    }

    [Fact]
    public void The_layout_carries_the_framework_error_bar()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(new FakeHandler(_ => Canned.Json(Status)))));

        var cut = ctx.Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p>x</p>"))));

        var bar = cut.Find("#blazor-error-ui");
        Assert.NotNull(bar.QuerySelector("a.reload"));
        Assert.NotNull(bar.QuerySelector(".dismiss"));
    }

    [Fact]
    public void The_broadcast_layout_gets_no_app_chrome()
    {
        using var ctx = new BunitContext();

        var cut = ctx.Render<BroadcastLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p>stage</p>"))));

        Assert.Empty(cut.FindAll("#blazor-error-ui"));
        Assert.Empty(cut.FindAll("nav"));
        Assert.Empty(cut.FindAll(".status-strip"));
    }

    private static readonly RenderFragment ThrowerBody = b =>
    {
        b.OpenComponent<Thrower>(0);
        b.CloseComponent();
    };

    private sealed class Thrower : ComponentBase
    {
        // As a loader does: the call to the API fails after the first render, not while the page is being created.
        protected override async Task OnInitializedAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("the page failed");
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddMarkupContent(0, "<p data-testid=\"thrower\">never shown</p>");
    }
}
