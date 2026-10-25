using Chess.Web.Api;
using Chess.Web.Components.Pages.Tournaments;
using ApiContracts = Chess.Api.Contracts;
using ApiTournaments = Chess.Api.Tournaments;
using Xunit;

namespace Chess.Web.Tests.Tournaments;

/// <summary>The forms' cost preview must give the API's figures, so what is shown before saving is what gets stored.</summary>
public class CostPreviewParityTests
{
    private const double Rate = 0.01;

    [Fact]
    public void AModelWithStoredMovesIsEstimatedFromItsAverageTokens()
    {
        var pricing = new ApiContracts.ModelPricingDto(3.0, 15.0, 0.3);
        var api = ApiTournaments.CostEstimator.Estimate("gpt", 40,
            new ApiTournaments.ModelCostInputs(1, pricing, new ApiTournaments.ModelTokenAverage(12, 1000, 200)), Rate);

        var web = CostPreview.For(Model("gpt", new ModelPricingDto(3.0, 15.0, 0.3)),
            Inputs(new ModelTokenAverageDto("gpt", 12, 1000, 200)), 40);

        AssertSame(api, web);
        Assert.Equal("history", web.Basis);
        Assert.Equal(12, web.SampleMoves);
    }

    [Fact]
    public void AModelWithoutStoredMovesIsEstimatedFromTheAssumedTokens()
    {
        var pricing = new ApiContracts.ModelPricingDto(3.0, 15.0, 0.3);
        var api = ApiTournaments.CostEstimator.Estimate("gpt", 80,
            new ApiTournaments.ModelCostInputs(null, pricing, null), Rate);

        var web = CostPreview.For(Model("gpt", new ModelPricingDto(3.0, 15.0, 0.3)), Inputs(), 80);

        AssertSame(api, web);
        Assert.Equal("assumed", web.Basis);
        Assert.Null(web.SampleMoves);
    }

    [Fact]
    public void AModelWithoutPricesIsUnknownAndTheRandomPlayerIsFree()
    {
        var api = ApiTournaments.CostEstimator.Estimate("claude", 40, new ApiTournaments.ModelCostInputs(1.5, null, null), Rate);
        var web = CostPreview.For(Model("claude", null), Inputs(), 40);
        AssertSame(api, web);
        Assert.Equal("unknown", web.Basis);

        var free = CostPreview.For(Model("random", null), Inputs(), 40);
        Assert.Equal("free", free.Basis);
        Assert.Equal(0, free.AiCredits);
    }

    [Fact]
    public void PremiumRequestsPerGameAreTheSameInTheAPIAndTheForms()
    {
        Assert.Equal(ApiTournaments.TournamentService.EstimatedRequestsPerGame, PremiumEstimate.RequestsPerGame);
        Assert.Equal(ApiTournaments.CostEstimator.MovesPerSidePerGame, CostPreview.MovesPerSidePerGame);
    }

    private static void AssertSame(ApiTournaments.ModelCostEstimate api, CostPreview.Estimate web)
    {
        Assert.Equal(api.AiCredits, web.AiCredits);
        Assert.Equal(api.Usd, web.Usd);
        Assert.Equal(api.Basis, web.Basis);
        Assert.Equal(api.SampleMoves, web.SampleMoves);
    }

    private static ModelDto Model(string id, ModelPricingDto? pricing) => new(id, id, null, true, pricing);

    private static CostInputsDto Inputs(params ModelTokenAverageDto[] models) =>
        new(Rate, 700, 300, models);
}
