using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using ApiDto = Chess.Api.Contracts;
using ApiBenchmarks = Chess.Api.Benchmarks;
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
        AssertRoundTrip<ApiTournaments.ModelPremiumEstimateDto, WebDto.ModelPremiumEstimateDto>(
            new ApiTournaments.ModelPremiumEstimateDto("gpt-5", 960, 24.5, 0.245, "history", 12));
        AssertRoundTrip<ApiTournaments.ModelPremiumEstimateDto, WebDto.ModelPremiumEstimateDto>(
            new ApiTournaments.ModelPremiumEstimateDto("claude", null, 26.4, 0.264, "assumed"));
        // 03 §10 (G5): GET /api/cost/inputs.
        AssertRoundTrip<ApiTournaments.CostInputsDto, WebDto.CostInputsDto>(
            new ApiTournaments.CostInputsDto(0.01, 700, 300,
                [new ApiTournaments.ModelTokenAverageDto("gpt-5", 12, 1000.5, 200.25), new ApiTournaments.ModelTokenAverageDto("claude", 0, null, null)]));
        AssertRoundTrip<ApiTournaments.CostInputsDto, WebDto.CostInputsDto>(
            new ApiTournaments.CostInputsDto(0.02, 700, 300, []));
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
            new ApiDto.ModelResultsDto("gpt-5", 10, 6, 2, 2, 1, 410, 0.97, 1500.5, 800.0, 45.5, 22.1, 0.0105, 0.1));
        AssertRoundTrip<ApiDto.ModelResultsDto, WebDto.ModelResultsDto>(
            new ApiDto.ModelResultsDto("random", 10, 2, 0, 8, 0, 300, 0.2, 0, null, null, null, null, 0));
        AssertRoundTrip<ApiDto.AttemptDto, WebDto.AttemptDto>(
            new ApiDto.AttemptDto(Id, 3, 1, "gpt-5", "Legal moves: ...", "Nf3", "Nf3", true, 1200, null, At));
    }

    [Fact]
    public void EveryUsageAndPricingContractRoundTrips()
    {
        AssertRoundTrip<ApiDto.ModelDto, WebDto.ModelDto>(
            new ApiDto.ModelDto("claude-sonnet", "Claude Sonnet", null, true, new ApiDto.ModelPricingDto(3.0, 15.0, 0.3)));
        AssertRoundTrip<ApiDto.ModelDto, WebDto.ModelDto>(
            new ApiDto.ModelDto("mystery", "Mystery", null, true, new ApiDto.ModelPricingDto(null, null, null)));
        AssertRoundTrip<ApiDto.PromptStyleDto, WebDto.PromptStyleDto>(
            new ApiDto.PromptStyleDto("think", "Think, then move", "An analysis, a plan and one move.", true, "sha256:0123456789ab"));
        AssertRoundTrip<ApiDto.RequestMenuDto, WebDto.RequestMenuDto>(new ApiDto.RequestMenuDto(
            [new ApiDto.MenuItemDto("c1", "claude-haiku@low", "Claude Haiku", 0.04), new ApiDto.MenuItemDto("d3", "llama-70b", "Llama 70B", 0.006)],
            ["classic", "board", "think"], [1, 2, 5, 10, 20, 50], 0.3, "!g <white> vs <black> [style]"));
        AssertRoundTrip<ApiDto.QuoteDto, WebDto.QuoteDto>(new ApiDto.QuoteDto("c1", "d3~think", 0.07, 1));
        AssertRoundTrip<ApiDto.QuoteDto, WebDto.QuoteDto>(new ApiDto.QuoteDto("o9~think", "o9~think", 40, null));
        var request = new ApiDto.GameRequestDto(Id, "youtube", "Viewer", "c1", "d3", 1, "queued", null, null, At, 2);
        AssertRoundTrip<ApiDto.GameRequestDto, WebDto.GameRequestDto>(request);
        AssertRoundTrip<ApiDto.RequestQueueDto, WebDto.RequestQueueDto>(new ApiDto.RequestQueueDto(
            request with { Status = "playing", GameId = Id, QueuePosition = null }, [request], [], 12));
        AssertRoundTrip<ApiDto.SimulateChatRequest, WebDto.SimulateChatRequest>(
            new ApiDto.SimulateChatRequest("UC123", "Viewer", 5, "USD", "!g c1 vs d3 think"));
        AssertRoundTrip<ApiDto.AttemptDto, WebDto.AttemptDto>(
            new ApiDto.AttemptDto(Id, 14, 1, "claude-sonnet", "Legal moves: ...", "The main line is 7...h6. h6", null, false,
                41000, "Unparseable: the reply is not one move.", At, "Keres Attack: h6 stops g5.", 1200, 310, 240, 0.0057,
                "unparseable"));
        var white = new ApiDto.SideUsageDto("white", "claude-sonnet", 41, 52000, 9000, 6100, 12000, 0.31, 0.0031, "reported");
        var black = new ApiDto.SideUsageDto("black", "random", 0, 0, 0, 0, 0, null, null, "free");
        AssertRoundTrip<ApiDto.SideUsageDto, WebDto.SideUsageDto>(white);
        AssertRoundTrip<ApiDto.GameUsageDto, WebDto.GameUsageDto>(new ApiDto.GameUsageDto(Id, white, black, 0.31, 0.0031, 0.01));
    }

    [Fact]
    public void EveryBenchmarkContractRoundTrips()
    {
        var run = new ApiBenchmarks.BenchmarkDto(Id, "Medium pick", "claude@medium", ["fake-b", "random"], 30, "running", 60, 12, At, null);
        var opponent = new ApiBenchmarks.BenchmarkOpponentDto("random", 30, 22, 4, 4, 0, 0.7, 190.85, 40.2, null, 0.82, 0.71, 0.91, 0.98, 0.0021);
        var unrated = new ApiBenchmarks.BenchmarkOpponentDto("fake-b", 0, 0, 0, 0, 0, 0, null, null, null, null, null, null, 0, null);
        var fingerprint = new ApiBenchmarks.BenchmarkFingerprintDto("1.0.0+abc1234", "1", "sha256:0123456789ab", At);

        AssertRoundTrip<ApiBenchmarks.CreateBenchmarkRequest, WebDto.CreateBenchmarkRequest>(
            new ApiBenchmarks.CreateBenchmarkRequest("Medium pick", "claude@medium", null));
        AssertRoundTrip<ApiBenchmarks.CreateBenchmarkRequest, WebDto.CreateBenchmarkRequest>(
            new ApiBenchmarks.CreateBenchmarkRequest("Quick", "fake-a@high", ["fake-b"], 4, 1));
        AssertRoundTrip<ApiBenchmarks.BenchmarkDto, WebDto.BenchmarkDto>(run);
        AssertRoundTrip<ApiBenchmarks.BenchmarkCreatedDto, WebDto.BenchmarkCreatedDto>(
            new ApiBenchmarks.BenchmarkCreatedDto(run, [new ApiTournaments.ModelPremiumEstimateDto("random", null, 0, 0, "free")]));
        AssertRoundTrip<ApiBenchmarks.BenchmarkOpponentDto, WebDto.BenchmarkOpponentDto>(opponent);
        AssertRoundTrip<ApiBenchmarks.BenchmarkOpponentDto, WebDto.BenchmarkOpponentDto>(unrated);
        AssertRoundTrip<ApiBenchmarks.BenchmarkSummaryDto, WebDto.BenchmarkSummaryDto>(
            new ApiBenchmarks.BenchmarkSummaryDto(60, 0.6, 1512, 1430, 1601, 0.71, false, 91000, 0.2, null));
        AssertRoundTrip<ApiBenchmarks.BenchmarkFingerprintDto, WebDto.BenchmarkFingerprintDto>(fingerprint);
        AssertRoundTrip<ApiBenchmarks.BenchmarkReportDto, WebDto.BenchmarkReportDto>(
            new ApiBenchmarks.BenchmarkReportDto(run, [opponent, unrated],
                new ApiBenchmarks.BenchmarkSummaryDto(30, 0.7, null, null, null, null, true, 0, null, null), fingerprint));
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
