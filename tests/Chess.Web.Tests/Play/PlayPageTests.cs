using System.Net;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Play;
using Chess.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chess.Web.Tests.Play;

public class PlayPageTests
{
    [Fact]
    public void OffersEveryEnabledModelWithItsMultiplierAndNoBannerWhenCopilotIsReady()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = ctx.Render<PlayPage>();

        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid='white-model'] option").Count));
        var options = cut.FindAll("[data-testid='white-model'] option");
        Assert.Equal(new[] { "GPT · ×1", "Claude · ×1.5", "Random" }, options.Select(option => option.TextContent).ToArray());
        Assert.All(options, option => Assert.False(option.HasAttribute("disabled")));
        Assert.Empty(cut.FindAll("[data-testid=gateway-banner]"));
        Assert.True(cut.Find("[data-testid=start-game]").ClassList.Contains("btn-primary"));
        Assert.NotNull(cut.Find("[data-testid=white-model]").Closest(".card"));
        Assert.NotNull(cut.Find("[data-testid=board]").Closest(".card"));
    }

    [Fact]
    public void WithoutACopilotLoginOnlyRandomCanBePickedAndTheBannerSaysHowToLogIn()
    {
        var server = new PlayServer { Status = PlayFixtures.NotReady };
        using var ctx = new BunitContext();
        Wire(ctx, server);

        var cut = ctx.Render<PlayPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=gateway-banner]")));
        var banner = cut.Find("[data-testid=gateway-banner]").TextContent;
        Assert.Contains("GitHub Copilot is not ready: NotAuthenticated (not logged in)", banner);
        Assert.Contains("Run `copilot` in a terminal, log in", banner);
        Assert.True(cut.Find("[data-testid='black-model'] option[value='gpt']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='black-model'] option[value='claude']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='black-model'] option[value='random']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task WithoutACopilotLoginTheDefaultGameIsRandomAgainstRandom()
    {
        var server = new PlayServer { Status = PlayFixtures.NotReady };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);

        var body = await PostedGame(server);

        Assert.Equal("random", body.GetProperty("white").GetProperty("kind").GetString());
        Assert.Equal("random", body.GetProperty("white").GetProperty("modelId").GetString());
        Assert.Equal("random", body.GetProperty("black").GetProperty("kind").GetString());
        server.EndStream();
    }

    [Fact]
    public async Task StartingSendsTheChosenPlayersAndRatedFlagAndLocksTheSetup()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut, rated: false);

        var body = await PostedGame(server);

        Assert.Equal("llm", body.GetProperty("white").GetProperty("kind").GetString());
        Assert.Equal("gpt", body.GetProperty("white").GetProperty("modelId").GetString());
        Assert.Equal("claude", body.GetProperty("black").GetProperty("modelId").GetString());
        Assert.False(body.GetProperty("rated").GetBoolean());
        cut.WaitForAssertion(() => Assert.True(cut.Find("[data-testid=start-game]").HasAttribute("disabled")));
        Assert.True(cut.Find("[data-testid=white-model]").HasAttribute("disabled"));
        Assert.Single(cut.FindAll("[data-testid=abort-game]"));
        server.EndStream();
    }

    [Fact]
    public void AStartThatFailsSaysSoAndLeavesThePageUsable()
    {
        var server = new PlayServer { CreateStatus = HttpStatusCode.ServiceUnavailable };
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=play-error]")));
        Assert.Contains("Could not start the game", cut.Find("[data-testid=play-error]").TextContent);
        Assert.False(cut.Find("[data-testid=start-game]").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[data-testid=abort-game]"));
    }

    [Fact]
    public async Task EventsUpdateTheBoardMoveListAndThinkingIndicatorAndReplayedPliesAreIgnored()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        await server.SendAsync("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt"));
        cut.WaitForAssertion(() => Assert.Contains("GPT is thinking about White's move (ply 1)",
            cut.Find("[data-testid=thinking]").TextContent));

        await server.SendAsync("MovePlayed", PlayFixtures.MovePlayed(1, "White", "e4", PlayFixtures.AfterE4));
        cut.WaitForAssertion(() => Assert.Contains("e4", cut.Find("[data-testid=move-list]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=thinking]"));
        Assert.Single(cut.FindAll("[data-testid=board] [data-piece='P'][x='450'][y='450']"));
        Assert.Empty(cut.FindAll("[data-testid=board] [data-piece='P'][x='450'][y='650']"));

        // A replayed MovePlayed and a MoveStarted for a ply already played are duplicates (03 §5.1).
        await server.SendAsync("MovePlayed", PlayFixtures.MovePlayed(1, "White", "e4", PlayFixtures.AfterE4));
        await server.SendAsync("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt"));
        await server.SendAsync("MoveStarted", PlayFixtures.MoveStarted(2, "Black", "claude"));
        cut.WaitForAssertion(() => Assert.Contains("Claude is thinking about Black's move (ply 2)",
            cut.Find("[data-testid=thinking]").TextContent));
        Assert.Single(cut.FindAll("[data-testid=move-list] li"));
        Assert.Equal("1", cut.Find("[data-testid=move-list] li").GetAttribute("data-ply"));
        server.EndStream();
    }

    [Fact]
    public async Task AGapInThePliesReloadsTheGameInsteadOfGuessingTheMove()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));
        // The first load (queued, no moves) has happened once the page asked for the game.
        cut.WaitForAssertion(() => Assert.Single(server.Handler.Requests,
            request => request.Method == "GET" && request.Target == PlayServer.GameTarget));

        server.GameDetail = PlayFixtures.Detail("running", "*", null,
            ("White", "e4", PlayFixtures.AfterE4), ("Black", "e5", PlayFixtures.AfterE5));

        // Ply 1 never arrived, so ply 2 is a gap: the page loads the game again rather than showing a guessed move.
        await server.SendAsync("MovePlayed", PlayFixtures.MovePlayed(2, "Black", "e5", PlayFixtures.AfterE5));

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=move-list] li").Count));
        Assert.Equal(2, server.Handler.Requests.Count(request => request.Method == "GET" && request.Target == PlayServer.GameTarget));
        server.EndStream();
    }

    [Fact]
    public async Task AnIllegalReplyIsShownInlineWithItsReasonWhileTheModelIsStillThinking()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        await server.SendAsync("MoveStarted", PlayFixtures.MoveStarted(1, "White", "gpt"));
        await server.SendAsync("AttemptRejected", PlayFixtures.AttemptRejected(1, 1, "Ke9", "Ke9 is not a legal move"));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=attempt-notices] li")));
        var notice = cut.Find("[data-testid=attempt-notices] li").TextContent;
        Assert.Contains("White's attempt 1 at ply 1 was not a legal move: Ke9 is not a legal move", notice);
        Assert.Contains("Ke9", cut.Find("[data-testid=attempt-notices] code").TextContent);
        Assert.Contains("GPT is thinking", cut.Find("[data-testid=thinking]").TextContent);
        server.EndStream();
    }

    [Fact]
    public async Task GameFinishedShowsTheResultEndsTheGameAndOffersThePgn()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        await server.SendAsync("GameFinished", PlayFixtures.GameFinished("1-0", "checkmate"));

        cut.WaitForAssertion(() => Assert.Equal("1-0 · checkmate", cut.Find("[data-testid=game-result]").TextContent));
        Assert.True(cut.Find("[data-testid=game-result]").ClassList.Contains("badge"));
        Assert.True(cut.Find("[data-testid=game-result]").ClassList.Contains("badge-success"));
        Assert.Empty(cut.FindAll("[data-testid=forfeit-notice]"));
        Assert.Empty(cut.FindAll("[data-testid=abort-game]"));
        Assert.False(cut.Find("[data-testid=start-game]").HasAttribute("disabled"));

        cut.Find("[data-testid=pgn-toggle]").Click();
        cut.WaitForAssertion(() => Assert.Contains("1. e4 e5 1-0", cut.Find("[data-testid=pgn]").TextContent));
    }

    [Fact]
    public async Task AForfeitIsExplainedAsTheSideThatRanOutOfLegalReplies()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.True(server.StreamOpen));

        await server.SendAsync("GameFinished", PlayFixtures.GameFinished("0-1", "forfeit-white"));

        cut.WaitForAssertion(() => Assert.Equal("0-1 · White forfeited", cut.Find("[data-testid=game-result]").TextContent));
        Assert.Contains("White forfeited: no legal move within the attempt limit. No fallback move is played.",
            cut.Find("[data-testid=forfeit-notice]").TextContent);
    }

    [Fact]
    public void AbortSendsDeleteForTheRunningGame()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=abort-game]")));

        cut.Find("[data-testid=abort-game]").Click();

        cut.WaitForAssertion(() => Assert.Contains(server.Handler.Requests,
            request => request.Method == "DELETE" && request.Target == PlayServer.GameTarget));
    }

    [Fact]
    public async Task AGameIsRatedUnlessTheToggleIsTurnedOff()
    {
        var server = new PlayServer();
        using var ctx = new BunitContext();
        Wire(ctx, server);
        var cut = ctx.Render<PlayPage>();
        StartWhenReady(cut);

        var body = await PostedGame(server);

        Assert.True(body.GetProperty("rated").GetBoolean());
        server.EndStream();
    }

    private static void Wire(BunitContext ctx, PlayServer server)
    {
        var client = Canned.Client(server.Handler);
        ctx.Services.AddSingleton(new ArenaApiClient(client));
        ctx.Services.AddSingleton(new GameEventsClient(client));
    }

    /// <summary>Waits until the models have loaded and the Start button can be pressed, then starts the game.</summary>
    private static void StartWhenReady(IRenderedComponent<PlayPage> cut, bool rated = true)
    {
        cut.WaitForAssertion(() => Assert.False(cut.Find("[data-testid=start-game]").HasAttribute("disabled")));
        if (!rated)
        {
            cut.Find("[data-testid=rated-toggle]").Change(false);
        }

        cut.Find("[data-testid=start-game]").Click();
    }

    /// <summary>The JSON body of the one POST /api/games the page sent.</summary>
    private static async Task<JsonElement> PostedGame(PlayServer server)
    {
        string? body = null;
        for (var attempt = 0; attempt < 100 && body is null; attempt++)
        {
            body = server.Handler.Requests.FirstOrDefault(request => request.Method == "POST" && request.Target == "/api/games")?.Body;
            await Task.Delay(10);
        }

        Assert.NotNull(body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
