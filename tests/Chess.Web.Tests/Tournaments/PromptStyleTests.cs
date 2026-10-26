using System.Net;
using System.Text.Json;
using Bunit;
using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;
using Chess.Web.Tests.Support;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

public class PromptStyleTests
{
    [Fact]
    public void TwoModelsInTwoStylesMakeFourParticipantsAndFourEstimateRows()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/prompt-styles"] = Serve.Ok(PromptStyleFixtures.Json),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
            ["POST /api/tournaments"] = Serve.Created("""{"tournament":{"id":"33333333-3333-3333-3333-333333333333","name":"Styles","models":[],"gamesPerPair":2,"status":"running","gamesTotal":12,"gamesFinished":0,"createdAt":"2026-10-24T10:00:00Z","finishedAt":null},"estimatedPremiumRequests":[]}"""),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));
        cut.Find("#tournament-name").Input("Styles");
        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);
        cut.Find("#style-think").Change(true);

        // Two models in two styles: four players, six pairs, two games a pair.
        cut.WaitForAssertion(() => Assert.Contains("12 games in 6 pairs.", cut.Markup));
        Assert.Equal(4, cut.FindAll(".tournament-estimate tbody tr").Count);
        Assert.Contains("2 players", cut.Find("fieldset.tournament-styles").TextContent);
        Assert.Equal("240", cut.FindAll(".tournament-estimate tbody tr")[1].Children[2].TextContent);
        Assert.Equal("GPT · Think, then move", cut.FindAll(".tournament-estimate tbody tr")[1].Children[0].TextContent);

        cut.Find("#start-tournament").Click();

        cut.WaitForAssertion(() => Assert.Contains("Started", cut.Markup));
        var post = Assert.Single(handler.Requests, request => request.Method == "POST");
        using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal(["gpt", "gpt~think", "claude", "claude~think"],
            body.RootElement.GetProperty("models").EnumerateArray().Select(model => model.GetString()!).ToArray());
    }

    [Fact]
    public void WithoutStylesTheFormOffersClassicAloneAndSaysSo()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>
        {
            ["GET /api/models"] = Serve.Ok(Fixtures.Models),
            ["GET /api/tournaments"] = Serve.Ok(Fixtures.Tournaments()),
        });
        using var ctx = new BunitContext();
        Wire.Api(ctx, handler);
        var cut = ctx.Render<TournamentsPage>();
        cut.WaitForAssertion(() => Assert.Contains("No tournaments yet", cut.Markup));

        var chips = cut.FindAll("fieldset.tournament-styles .chip");
        Assert.Single(chips);
        Assert.Contains("Classic", chips[0].TextContent);
        Assert.Contains("Prompt styles could not be loaded, so only classic is offered.", cut.Find("[data-testid='styles-notice']").TextContent);

        cut.Find("#model-gpt").Change(true);
        cut.Find("#model-claude").Change(true);
        cut.WaitForAssertion(() => Assert.Contains("Known total: 200", cut.Markup));
    }

    [Fact]
    public void ClassicAloneKeepsThePlainIdsAndTheStylesPlayersCount()
    {
        Assert.Equal(["gpt", "claude"], PromptStyleChoices.Participants(["gpt", "claude"], ["classic"]));
        Assert.Equal(["gpt", "gpt~think", "claude", "claude~think"],
            PromptStyleChoices.Participants(["gpt", "claude"], ["classic", "think"]));
        Assert.Equal(["random", "gpt", "gpt~think"], PromptStyleChoices.Participants(["random", "gpt"], ["classic", "think"]));
    }

    [Fact]
    public void AStyledPlayerIsPricedAndNamedByItsModel()
    {
        var models = JsonSerializer.Deserialize<List<ModelDto>>(Fixtures.Models, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var player = PromptStyleChoices.Find(models, "gpt~think")!;
        Assert.Equal("gpt~think", player.Id);
        Assert.Equal(3.0, player.Pricing!.InputUsdPerMillionTokens);
        Assert.Equal("GPT · Think", PromptStyleChoices.Label("gpt~think", models, [new("think", "Think", "", true, "")]));
        Assert.Equal("GPT", PromptStyleChoices.Label("gpt", models, []));
        Assert.Null(PromptStyleChoices.Find(models, "nope~think"));
    }

    [Fact]
    public async Task AFailedStyleListFallsBackToClassicWithAReason()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply>());
        var api = new ArenaApiClient(Canned.Client(handler));

        var loaded = await PromptStyleChoices.LoadAsync(api, CancellationToken.None);

        Assert.Equal(["classic"], loaded.Styles.Select(style => style.Id).ToArray());
        Assert.Equal(PromptStyleChoices.Unavailable, loaded.Notice);
    }

    [Fact]
    public async Task AnEmptyStyleListIsTreatedAsUnavailable()
    {
        var handler = Serve.Routes(new Dictionary<string, Reply> { ["GET /api/prompt-styles"] = Serve.Ok("[]") });

        var loaded = await PromptStyleChoices.LoadAsync(new ArenaApiClient(Canned.Client(handler)), CancellationToken.None);

        Assert.Equal(["classic"], loaded.Styles.Select(style => style.Id).ToArray());
        Assert.NotNull(loaded.Notice);
    }
}
