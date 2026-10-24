using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using ApiDto = Chess.Api.Contracts;
using ApiTournaments = Chess.Api.Tournaments;
using WebDto = Chess.Web.Api;

namespace Chess.Web.Tests.Api;

/// <summary>
/// Keeps the web copies of the 03 §5.1 DTOs in step with the API: whatever the API serialises must
/// deserialise into the web record and serialise back to the same JSON.
/// </summary>
public class ContractRoundTripTests
{
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset At = new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-8c45a0a0f0f1");

    [Fact]
    public void EveryGameAndMoveContractRoundTrips()
    {
        var white = new ApiDto.PlayerSpec("llm", "gpt-5");
        var black = new ApiDto.PlayerSpec("random", "random");
        var summary = new ApiDto.GameSummaryDto(Id, null, white, black, true, "finished", "1-0", "checkmate", 41, At, At.AddMinutes(9));

        AssertRoundTrip<ApiDto.PlayerSpec, WebDto.PlayerSpec>(white);
        AssertRoundTrip<ApiDto.CreateGameRequest, WebDto.CreateGameRequest>(new ApiDto.CreateGameRequest(white, black, false));
        AssertRoundTrip<ApiDto.GameSummaryDto, WebDto.GameSummaryDto>(summary);
        AssertRoundTrip<ApiDto.MoveDto, WebDto.MoveDto>(new ApiDto.MoveDto(1, "white", "e4", "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", 2, 1830, 120, 14, 25, "e4"));
        AssertRoundTrip<ApiDto.MoveDto, WebDto.MoveDto>(new ApiDto.MoveDto(2, "black", "e5", "fen", 1, 900, null, null, null, null));
        AssertRoundTrip<ApiDto.GameDetailDto, WebDto.GameDetailDto>(new ApiDto.GameDetailDto(summary, "start", "current", []));
        AssertRoundTrip<ApiDto.PageDto<ApiDto.GameSummaryDto>, WebDto.PageDto<WebDto.GameSummaryDto>>(new ApiDto.PageDto<ApiDto.GameSummaryDto>([summary], 1, 20, 1));
    }

    [Fact]
    public void EveryTournamentAndModelContractRoundTrips()
    {
        AssertRoundTrip<ApiDto.CreateTournamentRequest, WebDto.CreateTournamentRequest>(
            new ApiDto.CreateTournamentRequest("Spring", ["a", "b"], 2, 2));
        AssertRoundTrip<ApiDto.TournamentDto, WebDto.TournamentDto>(
            new ApiDto.TournamentDto(Id, "Spring", ["a", "b"], 2, "running", 4, 1, At, null));
        AssertRoundTrip<ApiTournaments.ModelPremiumEstimateDto, WebDto.ModelPremiumEstimateDto>(
            new ApiTournaments.ModelPremiumEstimateDto("gpt-5", 960));
        AssertRoundTrip<ApiTournaments.ModelPremiumEstimateDto, WebDto.ModelPremiumEstimateDto>(
            new ApiTournaments.ModelPremiumEstimateDto("random", null));
        AssertRoundTrip<ApiTournaments.TournamentCreatedDto, WebDto.TournamentCreatedDto>(
            new ApiTournaments.TournamentCreatedDto(
                new ApiDto.TournamentDto(Id, "Spring", ["a", "b"], 2, "running", 2, 0, At, null),
                [new ApiTournaments.ModelPremiumEstimateDto("a", 160), new ApiTournaments.ModelPremiumEstimateDto("b", null)]));
        AssertRoundTrip<ApiDto.ModelDto, WebDto.ModelDto>(new ApiDto.ModelDto("gpt-5", "GPT-5", 1.0, true));
        AssertRoundTrip<ApiDto.ModelDto, WebDto.ModelDto>(new ApiDto.ModelDto("random", "Random", null, false));
        AssertRoundTrip<ApiDto.StatusDto, WebDto.StatusDto>(new ApiDto.StatusDto("Ready", "signed in", "ok", "absent"));
    }

    [Fact]
    public void EveryRatingContractRoundTrips()
    {
        var rating = new Chess.Rating.ModelRating("gpt-5", 1520.5, 1490.1, 1551.0, 30, 0.62, 0.97, 0.88, 0.74, 0.66, false);
        var pair = new Chess.Rating.PairwiseCoefficient("a", "b", 0.6, 0.5, 0.56, 0.48, 0.64, 35.2, 4, 0.75);

        AssertRoundTrip<Chess.Rating.ModelRating, WebDto.ModelRating>(rating);
        AssertRoundTrip<Chess.Rating.PairwiseCoefficient, WebDto.PairwiseCoefficient>(pair);
        AssertRoundTrip<ApiDto.LeaderboardDto, WebDto.LeaderboardDto>(new ApiDto.LeaderboardDto([rating], 0.54, 120, true));
        AssertRoundTrip<ApiDto.MatrixDto, WebDto.MatrixDto>(new ApiDto.MatrixDto(["a", "b"], [pair]));
        AssertRoundTrip<ApiDto.RatingHistoryPointDto, WebDto.RatingHistoryPointDto>(
            new ApiDto.RatingHistoryPointDto(At, 12, "gpt-5", 1500, 1460, 1540, 0.6, true));
    }

    [Fact]
    public void EveryResultsAndAttemptContractRoundTrips()
    {
        AssertRoundTrip<ApiDto.ModelResultsDto, WebDto.ModelResultsDto>(
            new ApiDto.ModelResultsDto("gpt-5", 10, 6, 2, 2, 1, 410, 0.97, 1500.5, 800.0, 45.5, 22.1));
        AssertRoundTrip<ApiDto.ModelResultsDto, WebDto.ModelResultsDto>(
            new ApiDto.ModelResultsDto("random", 10, 2, 0, 8, 0, 300, 0.2, 0, null, null, null));
        AssertRoundTrip<ApiDto.AttemptDto, WebDto.AttemptDto>(
            new ApiDto.AttemptDto(Id, 3, 1, "gpt-5", "Legal moves: ...", "Nf3", "Nf3", true, 1200, null, At));
    }

    [Fact]
    public void TheApiSerialisesCamelCaseNames()
    {
        var json = JsonSerializer.Serialize(new ApiDto.MoveDto(1, "white", "e4", "fen", 1, 5, null, null, null, null), ApiJson);

        Assert.Contains("\"fenAfter\":\"fen\"", json);
        Assert.Contains("\"latencyMs\":5", json);
    }

    private static void AssertRoundTrip<TApi, TWeb>(TApi apiValue)
    {
        var apiJson = JsonSerializer.Serialize(apiValue, ApiJson);

        var webValue = JsonSerializer.Deserialize<TWeb>(apiJson, ApiJson);
        Assert.NotNull(webValue);

        var webJson = JsonSerializer.Serialize(webValue, ApiJson);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(apiJson), JsonNode.Parse(webJson)),
            $"{typeof(TWeb).Name} lost data.\napi: {apiJson}\nweb: {webJson}");
    }
}
