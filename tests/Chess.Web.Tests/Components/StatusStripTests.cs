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
