using Chess.Api.Contracts;
using Chess.Api.Usage;
using Chess.Llm;
using Xunit;
using Reply = Chess.Api.Usage.GameCostCalculator.ReplyUsage;

namespace Chess.Api.Tests.Usage;

/// <summary>03 §10.2: the cost basis of each side and the USD rate, for every case the summary shows.</summary>
public class GameCostCalculatorTests
{
    private const double Rate = 0.01;

    /// <summary>1.0 credit per million input tokens, 4.0 per million output, 0.1 per million cached input.</summary>
    private static readonly ModelPricingDto Prices = ModelPricing.ToDto(new LlmTokenPrices(1_000_000, 1.0, 4.0, 0.1), Rate)!;

    private static Reply Tokens(int input, int output, int? cached = null, double? reported = null) =>
        new(input, output, null, cached, reported);

    [Fact]
    public void TheRandomSideIsFreeAndHasNoRequests()
    {
        var side = GameCostCalculator.ForSide("black", "random", isLlm: false, [], null, Rate);

        Assert.Equal(GameCostCalculator.Free, side.CostBasis);
        Assert.Equal(0, side.Requests);
        Assert.Equal(0, side.AiCredits);
        Assert.Equal(0, side.EstimatedUsd);
    }

    [Fact]
    public void AnLlmSideWithNoRepliesIsUnknown()
    {
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [], Prices, Rate);

        Assert.Equal(GameCostCalculator.Unknown, side.CostBasis);
        Assert.Null(side.AiCredits);
        Assert.Null(side.EstimatedUsd);
        Assert.Equal(0, side.Requests);
    }

    [Fact]
    public void WithoutPricesOrAReportedCostTheTokensAreSummedAndTheCostIsUnknown()
    {
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [Tokens(100, 20), Tokens(200, 30)], prices: null, Rate);

        Assert.Equal(GameCostCalculator.Unknown, side.CostBasis);
        Assert.Equal(2, side.Requests);
        Assert.Equal(300, side.InputTokens);
        Assert.Equal(50, side.OutputTokens);
        Assert.Null(side.AiCredits);
        Assert.Null(side.EstimatedUsd);
    }

    [Fact]
    public void ACostEstimatedFromThePricesIsInCreditsAndInDollars()
    {
        // 1000 input at 1.0 per million and 500 output at 4.0 per million is 0.003 credits, which is 0.00003 USD.
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [Tokens(1000, 500)], Prices, Rate);

        Assert.Equal(GameCostCalculator.Estimated, side.CostBasis);
        Assert.Equal(0.003, side.AiCredits!.Value, 12);
        Assert.Equal(0.00003, side.EstimatedUsd!.Value, 12);
    }

    [Fact]
    public void CachedInputIsBilledAtItsOwnPrice()
    {
        // 600 uncached input at 1.0, 400 cached at 0.1, 500 output at 4.0: (600 + 40 + 2000) millionths of a credit.
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [Tokens(1000, 500, cached: 400)], Prices, Rate);

        Assert.Equal(0.00264, side.AiCredits!.Value, 12);
        Assert.Equal(400, side.CachedInputTokens);
    }

    [Fact]
    public void ReasoningTokensAreCountedButAddNoCostOfTheirOwn()
    {
        var withReasoning = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [new Reply(1000, 500, ReasoningTokens: 300, CachedInputTokens: null, ReportedCredits: null)], Prices, Rate);
        var without = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [Tokens(1000, 500)], Prices, Rate);

        Assert.Equal(300, withReasoning.ReasoningTokens);
        Assert.Equal(without.AiCredits, withReasoning.AiCredits);
    }

    [Fact]
    public void AReportedCostIsUsedAndTheBasisSaysSo()
    {
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [Tokens(1000, 500, reported: 0.5), Tokens(10, 10, reported: 0.25)], prices: null, Rate);

        Assert.Equal(GameCostCalculator.Reported, side.CostBasis);
        Assert.Equal(0.75, side.AiCredits!.Value, 12);
        Assert.Equal(0.0075, side.EstimatedUsd!.Value, 12);
    }

    [Fact]
    public void AReportedReplyMixedWithAnEstimatedOneIsEstimated()
    {
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [Tokens(1000, 500, reported: 0.5), Tokens(1000, 500)], Prices, Rate);

        Assert.Equal(GameCostCalculator.Estimated, side.CostBasis);
        Assert.Equal(0.503, side.AiCredits!.Value, 12);
    }

    [Fact]
    public void OneUnknownReplyMakesTheBasisPartialAndTheSumCoversTheRest()
    {
        // A reply with no usage at all, such as a timeout, cannot be priced.
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [Tokens(1000, 500), new Reply(null, null, null, null, null)], Prices, Rate);

        Assert.Equal(GameCostCalculator.Partial, side.CostBasis);
        Assert.Equal(2, side.Requests);
        Assert.Equal(0.003, side.AiCredits!.Value, 12);
    }

    [Fact]
    public void TheUsdRateScalesTheDollarFigureOnly()
    {
        var side = GameCostCalculator.ForSide("white", "fake-a", isLlm: true,
            [Tokens(1000, 500, reported: 0.5)], prices: null, usdPerAiCredit: 0.02);

        Assert.Equal(0.5, side.AiCredits!.Value, 12);
        Assert.Equal(0.01, side.EstimatedUsd!.Value, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    public void ARateThatIsNotPositiveIsRefused(double rate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [], Prices, rate));
    }

    [Fact]
    public void TheGameTotalSumsTheSidesThatHaveACost()
    {
        var white = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [Tokens(1000, 500)], Prices, Rate);
        var black = GameCostCalculator.ForSide("black", "random", isLlm: false, [], null, Rate);

        var game = GameCostCalculator.ForGame(Guid.Empty, white, black, Rate);

        Assert.Equal(0.003, game.TotalAiCredits!.Value, 12);
        Assert.Equal(0.00003, game.TotalEstimatedUsd!.Value, 12);
        Assert.Equal(Rate, game.UsdPerAiCredit);
    }

    [Fact]
    public void TheGameTotalIsNullWhenNeitherSideHasACost()
    {
        var white = GameCostCalculator.ForSide("white", "fake-a", isLlm: true, [Tokens(100, 10)], prices: null, Rate);
        var black = GameCostCalculator.ForSide("black", "fake-b", isLlm: true, [], prices: null, Rate);

        var game = GameCostCalculator.ForGame(Guid.Empty, white, black, Rate);

        Assert.Null(game.TotalAiCredits);
        Assert.Null(game.TotalEstimatedUsd);
    }

    [Fact]
    public void ModelPricesAreShownPerMillionTokensInDollars()
    {
        // 1 credit per million input tokens is 0.01 USD per million at 0.01 USD a credit.
        var prices = ModelPricing.ToDto(new LlmTokenPrices(1_000_000, 1.0, 4.0, null), Rate);

        Assert.NotNull(prices);
        Assert.Equal(0.01, prices.InputUsdPerMillionTokens!.Value, 12);
        Assert.Equal(0.04, prices.OutputUsdPerMillionTokens!.Value, 12);
        Assert.Null(prices.CachedInputUsdPerMillionTokens);
    }

    [Fact]
    public void AModelWithoutPricesOrWithABatchSizeOfZeroShowsNone()
    {
        Assert.Null(ModelPricing.ToDto(null, Rate));
        Assert.Null(ModelPricing.ToDto(new LlmTokenPrices(0, 1.0, 4.0, null), Rate));
    }
}
