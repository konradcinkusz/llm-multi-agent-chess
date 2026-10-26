using Chess.Web.Api;
using Chess.Web.Components.Pages.Results;
using Chess.Web.Components.Pages.Shared;
using Xunit;

namespace Chess.Web.Tests.Results;

public class PromptStyleComparisonTests
{
    private static ModelRating Rating(string model, double rating, double low, double high, bool provisional = false) =>
        new(model, rating, low, high, Games: 30, Score: 0.5, Legality: 0.9, MoveQuality: 0.95, Quality: 0.5, ArenaScore: 50, Provisional: provisional);

    private static PairwiseCoefficient Pair(string a, string b, double rqc, double low, double high) =>
        new(a, b, rqc, 0.5, rqc, low, high, 0, 5, rqc);

    private static ModelResultsDto Results(string model, double? costPerGame) =>
        new(model, 30, 0, 0, 0, 0, 400, 0.98, 1500, null, null, null, costPerGame);

    private static readonly Dictionary<string, ModelResultsDto> NoBreakdown = new(StringComparer.Ordinal);

    [Fact]
    public void StylesAreListedClassicFirstThenTheKnownOnesThenTheRestByName()
    {
        var styles = PromptStyles.Present(["gpt~zeta", "gpt", "gpt~think", "gpt~alpha", "gpt@high~board", "gpt~think"]);

        Assert.Equal(new[] { "classic", "board", "think", "alpha", "zeta" }, styles);
    }

    [Fact]
    public void AFilterKeepsThePlayersOfTheChosenStyleAndAllKeepsEveryone()
    {
        Assert.True(PromptStyles.Includes("gpt@high~think", "think"));
        Assert.False(PromptStyles.Includes("gpt@high", "think"));
        Assert.True(PromptStyles.Includes("gpt@high", "classic"));
        Assert.True(PromptStyles.Includes("gpt@high~think", null));
    }

    [Fact]
    public void WithOnlyClassicPlayersThereIsNoComparison()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140), Rating("claude", 90, 70, 110)],
            [],
            NoBreakdown);

        Assert.Empty(groups);
    }

    [Fact]
    public void AModelEffortPlayedUnderTwoStylesGetsOneRowPerStyleClassicFirst()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high~think", 130, 110, 150), Rating("gpt@high", 120, 100, 140), Rating("claude", 90, 70, 110)],
            [Pair("gpt@high~think", "gpt@high", 0.6, 0.52, 0.68)],
            NoBreakdown);

        var group = Assert.Single(groups);
        Assert.Equal("gpt@high", group.Configuration);
        Assert.Equal(["classic", "think"], group.Rows.Select(row => row.Style));
        Assert.Equal(["gpt@high", "gpt@high~think"], group.Rows.Select(row => row.Player));
        Assert.Equal("Baseline", group.Rows[0].Against);
    }

    [Fact]
    public void ASignificantLeadOverTheBaselineIsCalledSignificantWithItsInterval()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140), Rating("gpt@high~think", 130, 110, 150)],
            [Pair("gpt@high~think", "gpt@high", 0.61, 0.55, 0.70)],
            NoBreakdown);

        Assert.Equal("Significantly ahead of classic: RQC 0.61, 95 % interval 0.55 to 0.70.", groups[0].Rows[1].Against);
    }

    [Fact]
    public void AnIntervalThatContainsOneHalfIsNoSignificantDifference()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140), Rating("gpt@high~think", 125, 105, 145)],
            [Pair("gpt@high~think", "gpt@high", 0.52, 0.45, 0.60)],
            NoBreakdown);

        Assert.Equal("No significant difference from classic: RQC 0.52, 95 % interval 0.45 to 0.60.", groups[0].Rows[1].Against);
    }

    [Fact]
    public void ALeadThatOnlyProvisionalModelsHoldIsSaidToBeTooFewGames()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140, provisional: true), Rating("gpt@high~think", 130, 110, 150, provisional: true)],
            [Pair("gpt@high~think", "gpt@high", 0.61, 0.55, 0.70)],
            NoBreakdown);

        Assert.Equal(
            $"No significant difference from classic: RQC 0.61, 95 % interval 0.55 to 0.70. {Significance.TooFewGames}",
            groups[0].Rows[1].Against);
    }

    [Fact]
    public void ARowWithNoPairwiseCellSaysSo()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140), Rating("gpt@high~think", 130, 110, 150)],
            [],
            NoBreakdown);

        Assert.Equal("No head-to-head games yet.", groups[0].Rows[1].Against);
    }

    [Fact]
    public void TheCostPerGameComesFromTheBreakdownOfTheFullPlayerId()
    {
        var breakdown = new Dictionary<string, ModelResultsDto>(StringComparer.Ordinal)
        {
            ["gpt@high"] = Results("gpt@high", 0.02),
            ["gpt@high~think"] = Results("gpt@high~think", null),
        };

        var groups = PromptStyleComparison.Build(
            [Rating("gpt@high", 120, 100, 140), Rating("gpt@high~think", 130, 110, 150)],
            [Pair("gpt@high~think", "gpt@high", 0.6, 0.52, 0.68)],
            breakdown);

        Assert.Equal(0.02, groups[0].Rows[0].CostPerGame);
        Assert.Null(groups[0].Rows[1].CostPerGame);
    }

    [Fact]
    public void WithoutAClassicPlayerTheFirstStyleIsTheBaseline()
    {
        var groups = PromptStyleComparison.Build(
            [Rating("gpt~think", 130, 110, 150), Rating("gpt~board", 110, 90, 130)],
            [Pair("gpt~think", "gpt~board", 0.6, 0.52, 0.68)],
            NoBreakdown);

        Assert.Equal(["board", "think"], groups[0].Rows.Select(row => row.Style));
        Assert.Equal("Baseline", groups[0].Rows[0].Against);
        Assert.Equal("Significantly ahead of board: RQC 0.60, 95 % interval 0.52 to 0.68.", groups[0].Rows[1].Against);
    }
}
