using Chess.Api.Contracts;
using Chess.Api.Requests;
using Chess.Api.Tournaments;
using Xunit;

namespace Chess.Api.Tests.Requests;

public class RequestPricingTests
{
    private const double Rate = 0.01;
    private static readonly RequestOptions Options = new();
    private static readonly ModelPricingDto Premium = new(3, 15, null);
    private static readonly ModelPricingDto Cheap = new(0.01, 0.04, null);
    private static readonly StyleTokens Classic = new() { InputTokens = 700, OutputTokens = 300 };
    private static readonly StyleTokens Think = new() { InputTokens = 2000, OutputTokens = 900 };

    [Fact]
    public void Two_cheap_models_are_the_smallest_tier()
    {
        var white = RequestPricing.SideEstimateUsd("cheap-a", Cheap, null, Classic, Rate);
        var black = RequestPricing.SideEstimateUsd("cheap-b", Cheap, null, Classic, Rate);

        Assert.Equal(1.0, RequestPricing.PriceUsd(white, black, Options));
    }

    [Fact]
    public void A_premium_think_matchup_takes_a_higher_tier_than_the_same_classic_one()
    {
        var classicPrice = RequestPricing.PriceUsd(
            RequestPricing.SideEstimateUsd("big", Premium, null, Classic, Rate),
            RequestPricing.SideEstimateUsd("big", Premium, null, Classic, Rate), Options);
        var thinkPrice = RequestPricing.PriceUsd(
            RequestPricing.SideEstimateUsd("big", Premium, null, Think, Rate),
            RequestPricing.SideEstimateUsd("big", Premium, null, Think, Rate), Options);

        Assert.Equal(2.0, classicPrice);
        Assert.Equal(5.0, thinkPrice);
    }

    [Fact]
    public void The_side_estimate_is_the_tournament_formula_over_the_style_assumption()
    {
        // 40 moves × (2 000 × $3 + 900 × $15) / 1 000 000 = $0.78 for one think side with no stored moves.
        var usd = RequestPricing.SideEstimateUsd("big", Premium, null, Think, Rate);

        Assert.Equal(0.78, usd!.Value, 9);
    }

    [Fact]
    public void A_stored_history_replaces_the_style_assumption()
    {
        var history = new ModelTokenAverage(Moves: 10, AverageInputTokens: 100, AverageOutputTokens: 50);

        // 40 moves × (100 × $3 + 50 × $15) / 1 000 000 = $0.042, whatever the style assumes.
        Assert.Equal(0.042, RequestPricing.SideEstimateUsd("big", Premium, history, Think, Rate)!.Value, 9);
    }

    [Fact]
    public void Above_the_largest_tier_the_price_is_null()
    {
        // Each side is about $0.78; forty times that needs about $134 once the safety factor and fee share are in, above $50.
        var side = RequestPricing.SideEstimateUsd("big", Premium, null, Think, Rate);

        Assert.Null(RequestPricing.PriceUsd(side * 40, side * 40, Options));
        Assert.Equal(50.0, RequestPricing.PriceUsd(side * 10, side * 10, Options));
    }

    [Fact]
    public void A_model_that_lists_no_prices_cannot_be_quoted()
    {
        var unpriced = RequestPricing.SideEstimateUsd("unlisted", null, null, Classic, Rate);

        Assert.Null(unpriced);
        Assert.Null(RequestPricing.PriceUsd(unpriced, 0.1, Options));
    }

    [Fact]
    public void Money_round_trips_through_micro_units()
    {
        Assert.Equal(1_500_000, Money.FromUnits(1.5));
        Assert.Equal(1.5, Money.ToUnits(1_500_000));
        Assert.Equal(0, Money.FromUnits(0.0000001));
    }

    [Fact]
    public void A_super_chat_is_converted_by_its_currency_rate_and_stays_gross()
    {
        var rates = Options.UsdPerUnit;

        Assert.Equal(Money.FromUnits(11.0), RequestPricing.Deposit(Money.FromUnits(10), "EUR", rates).UsdMicros);
        Assert.Equal(Money.FromUnits(5), RequestPricing.Deposit(Money.FromUnits(5), "usd", rates).UsdMicros);
    }

    [Fact]
    public void An_unknown_currency_is_recorded_at_zero_with_its_reason()
    {
        var value = RequestPricing.Deposit(Money.FromUnits(5), "XYZ", Options.UsdPerUnit);

        Assert.Equal(0, value.UsdMicros);
        Assert.Equal("currency not configured", value.Reason);
    }

    [Theory]
    [InlineData(null, "USD", "invalid amount")]
    [InlineData(0L, "USD", "invalid amount")]
    [InlineData(-5L, "USD", "invalid amount")]
    [InlineData(5_000_000L, null, "currency missing")]
    public void An_invalid_amount_or_currency_is_recorded_at_zero(long? micros, string? currency, string reason)
    {
        var value = RequestPricing.Deposit(micros, currency, Options.UsdPerUnit);

        Assert.Equal(0, value.UsdMicros);
        Assert.Equal(reason, value.Reason);
    }
}
