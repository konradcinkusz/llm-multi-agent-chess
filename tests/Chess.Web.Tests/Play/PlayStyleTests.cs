using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Play;

public class PlayStyleTests
{
    [Fact]
    public async Task AStyledSideIsSentWithItsStyleAndAClassicSideWithThePlainId()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        cut.WaitForAssertion(() => Assert.Contains("Think, then move", cut.Find("[data-testid='white-style']").TextContent));

        cut.Find("[data-testid='white-model']").Change("gpt");
        cut.Find("[data-testid='black-model']").Change("claude");
        cut.Find("[data-testid='white-style']").Change("think");
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=start-game]").HasAttribute("disabled")));
        cut.Find("[data-testid=start-game]").Click();

        var body = await PostedGame(server);
        Assert.Equal("gpt~think", body.GetProperty("white").GetProperty("modelId").GetString());
        Assert.Equal("claude", body.GetProperty("black").GetProperty("modelId").GetString());
        server.EndStream();
    }

    [Fact]
    public void TheStyleSelectsOfferEveryStyleWithItsDescriptionAsTheHint()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='white-style'] option")));

        var options = cut.FindAll("[data-testid='white-style'] option").Select(option => option.TextContent).ToArray();
        Assert.Equal(["Classic", "Board", "Think, then move"], options);
        Assert.Contains("Today's rated prompt.", cut.Find("#white-style").Closest("label")!.TextContent);

        cut.Find("[data-testid='white-style']").Change("think");
        Assert.Contains("An analysis, a plan and one move.", cut.Find("#white-style").Closest("label")!.TextContent);
    }

    [Fact]
    public void WithoutTheStyleListOnlyClassicIsOfferedAndTheNoticeSaysSo()
    {
        var server = new PlayServer { PromptStyles = null };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='white-style'] option")));
        Assert.Equal("Classic", cut.Find("[data-testid='white-style'] option").TextContent);
        Assert.Contains("Prompt styles could not be loaded, so only classic is offered.", cut.Find("[data-testid='styles-notice']").TextContent);
    }

    [Fact]
    public void ARandomSideHasNoStyleSelect()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='white-style']")));

        cut.Find("[data-testid='white-model']").Change("random");

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='white-style']")));
        Assert.NotEmpty(cut.FindAll("[data-testid='black-style']"));
    }

    private static void Wire(BunitContext ctx, PlayServer server)
    {
        var client = Canned.Client(server.Handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
        ctx.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
    }

    private static async Task<JsonElement> PostedGame(PlayServer server)
    {
        string? body = null;
        for (var attempt = 0; attempt < 100 && body is null; attempt++)
        {
            body = server.Handler.Requests.FirstOrDefault(request => request.Method == "POST" && request.Target == "/api/games")?.Body;
            await Task.Delay(10);
        }

        return JsonDocument.Parse(body ?? throw new InvalidOperationException("No game was posted.")).RootElement;
    }
}
