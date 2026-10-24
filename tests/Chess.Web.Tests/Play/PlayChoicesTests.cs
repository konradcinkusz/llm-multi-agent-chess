using Chess.Web.Api;
using Chess.Web.Components.Pages.Play;
using Xunit;

namespace Chess.Web.Tests.Play;

public class PlayChoicesTests
{
    private static readonly ModelDto Gpt = new("gpt", "GPT", 1, true);
    private static readonly ModelDto Claude = new("claude", "Claude", 1.5, true);
    private static readonly ModelDto Random = new("random", "Random", null, true);
    private static readonly ModelDto Retired = new("retired", "Retired", 1, false);

    [Theory]
    [InlineData("Ready", true)]
    [InlineData("Fake", true)]
    [InlineData("NotAuthenticated", false)]
    [InlineData("Unavailable", false)]
    public void TheGatewayIsReadyOnlyWhenCopilotIsSignedInOrFaked(string copilot, bool ready)
    {
        Assert.Equal(ready, PlayChoices.GatewayReady(new StatusDto(copilot, "detail", "ok", "absent")));
    }

    [Fact]
    public void WithoutAGatewayNoLlmIsSelectableAndRetiredModelsAreNotOffered()
    {
        var choices = PlayChoices.Choices([Gpt, Claude, Random, Retired], gatewayReady: false);

        Assert.Equal(["gpt", "claude", "random"], choices.Select(choice => choice.Model.Id));
        Assert.Equal([false, false, true], choices.Select(choice => choice.Selectable));
    }

    [Fact]
    public void WithAGatewayEveryEnabledModelIsSelectable()
    {
        var choices = PlayChoices.Choices([Gpt, Claude, Random, Retired], gatewayReady: true);

        Assert.All(choices, choice => Assert.True(choice.Selectable));
    }

    [Fact]
    public void TheDefaultsAreTheFirstTwoLlmsOrRandomWhenThereAreNotTwo()
    {
        var ready = PlayChoices.Choices([Gpt, Claude, Random], gatewayReady: true);
        var notReady = PlayChoices.Choices([Gpt, Claude, Random], gatewayReady: false);
        var oneLlm = PlayChoices.Choices([Gpt, Random], gatewayReady: true);

        Assert.Equal("gpt", PlayChoices.Default(ready, 0));
        Assert.Equal("claude", PlayChoices.Default(ready, 1));
        Assert.Equal("random", PlayChoices.Default(notReady, 0));
        Assert.Equal("random", PlayChoices.Default(notReady, 1));
        Assert.Equal("random", PlayChoices.Default(oneLlm, 1));
    }

    [Fact]
    public void RandomIsSentAsRandomAndAnyOtherModelAsAnLlm()
    {
        Assert.Equal(new PlayerSpec("random", "random"), PlayChoices.Player("random"));
        Assert.Equal(new PlayerSpec("llm", "gpt"), PlayChoices.Player("gpt"));
    }

    [Fact]
    public void ALabelShowsTheMultiplierAndRandomHasNone()
    {
        Assert.Equal("Claude · ×1.5", PlayChoices.Label(Claude));
        Assert.Equal("Random", PlayChoices.Label(Random));
        Assert.Equal("GPT · multiplier unknown", PlayChoices.Label(new ModelDto("gpt", "GPT", null, true)));
    }

    [Fact]
    public void AnOutcomeNamesItsTerminationAndAForfeitIsExplained()
    {
        Assert.Equal("1-0 · checkmate", PlayChoices.Outcome(new GameOutcome("1-0", "checkmate")));
        Assert.Equal("1/2-1/2 · fifty-move rule", PlayChoices.Outcome(new GameOutcome("1/2-1/2", "fifty-move")));
        Assert.Equal("*", PlayChoices.Outcome(new GameOutcome("*", null)));
        Assert.Null(PlayChoices.ForfeitNotice("checkmate"));
        Assert.StartsWith("Black forfeited:", PlayChoices.ForfeitNotice("forfeit-black"));
    }
}
