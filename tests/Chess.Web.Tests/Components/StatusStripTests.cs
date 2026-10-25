using System.Net;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Shared;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Components;

public class StatusStripTests
{
    [Fact]
    public void ShowsTheStatusReportedByApiStatus()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"Ready","copilotDetail":"signed in","database":"ok","stockfish":"absent"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains("Copilot: Ready (signed in)", cut.Find(".status-strip").TextContent));
        Assert.Contains("Database: ok", cut.Find(".status-strip").TextContent);
        Assert.Contains("Stockfish: absent", cut.Find(".status-strip").TextContent);
        Assert.Equal("/api/status", Assert.Single(handler.Requests).Target);
    }

    [Theory]
    [InlineData("Ready", "dot-success")]
    [InlineData("Fake", "dot-accent")]
    [InlineData("NotAuthenticated", "dot-warning")]
    [InlineData("Unavailable", "dot-danger")]
    public void CopilotStateSetsTheDotColourAndKeepsTheDetail(string state, string dot)
    {
        var handler = new FakeHandler(_ => Canned.Json($$"""{"copilot":"{{state}}","copilotDetail":"signed in","database":"healthy","stockfish":"healthy"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains(dot, cut.Find("[data-service='copilot'] .dot").ClassList));
        Assert.Contains($"Copilot: {state} (signed in)", cut.Find(".status-strip").TextContent);
    }

    [Fact]
    public void TheCopilotDetailIsAlsoTheTooltipOfItsItem()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"NotAuthenticated","copilotDetail":"run the login","database":"healthy","stockfish":"healthy"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Equal("run the login", cut.Find("[data-service='copilot']").GetAttribute("title")));
    }

    [Theory]
    [InlineData("healthy", "dot-success")]
    [InlineData("degraded", "dot-warning")]
    [InlineData("unhealthy", "dot-danger")]
    [InlineData("absent", "dot-neutral")]
    public void DatabaseAndStockfishHealthSetTheirDotColour(string health, string dot)
    {
        var handler = new FakeHandler(_ => Canned.Json($$"""{"copilot":"Ready","copilotDetail":"signed in","database":"{{health}}","stockfish":"{{health}}"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains(dot, cut.Find("[data-service='database'] .dot").ClassList));
        Assert.Contains(dot, cut.Find("[data-service='stockfish'] .dot").ClassList);
    }

    [Theory]
    [InlineData("available", "dot-success")]
    [InlineData("absent", "dot-neutral")]
    public void StockfishAvailabilitySetsItsDot(string availability, string dot)
    {
        var handler = new FakeHandler(_ => Canned.Json($$"""{"copilot":"Ready","copilotDetail":"signed in","database":"healthy","stockfish":"{{availability}}"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains(dot, cut.Find("[data-service='stockfish'] .dot").ClassList));
        Assert.Contains($"Stockfish: {availability}", cut.Find(".status-strip").TextContent);
    }

    [Fact]
    public void AbsentStockfishSaysThatMoveQualityIsDisabled()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"Ready","copilotDetail":"signed in","database":"healthy","stockfish":"absent"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Equal("move quality disabled", cut.Find("[data-service='stockfish']").GetAttribute("title")));
    }

    [Fact]
    public void AvailableStockfishHasNoTooltip()
    {
        var handler = new FakeHandler(_ => Canned.Json("""{"copilot":"Ready","copilotDetail":"signed in","database":"healthy","stockfish":"available"}"""));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains("Stockfish: available", cut.Find(".status-strip").TextContent));
        Assert.False(cut.Find("[data-service='stockfish']").HasAttribute("title"));
    }

    [Fact]
    public void SaysSoWhenTheApiCannotBeReached()
    {
        var handler = new FakeHandler(_ => Canned.Json("{}", HttpStatusCode.BadGateway));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains("API unavailable", cut.Find(".status-strip").TextContent));
        Assert.DoesNotContain("Copilot:", cut.Find(".status-strip").TextContent);
    }

    [Fact]
    public void AnyFailureIsShownInsteadOfBreakingThePage()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("no service endpoint"));
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(new ArenaApiClient(Canned.Client(handler)));

        var cut = ctx.Render<StatusStrip>();

        cut.WaitForAssertion(() => Assert.Contains("API unavailable: no service endpoint", cut.Find(".status-strip").TextContent));
    }
}
