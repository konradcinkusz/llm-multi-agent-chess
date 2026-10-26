using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;

namespace Chess.Web.Api;

/// <summary>Typed client for Chess.Api: one method per row of 03 §5. The SSE stream is <see cref="GameEventsClient"/>.</summary>
public sealed class ArenaApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<ModelDto>> GetModelsAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<ModelDto>>("/api/models", ct);

    /// <summary>The prompt styles, <c>classic</c> first (03 §14.3). The forms offer classic alone when this fails.</summary>
    public Task<IReadOnlyList<PromptStyleDto>> GetPromptStylesAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<PromptStyleDto>>("/api/prompt-styles", ct);

    /// <summary>The rate and the token averages the forms estimate with before anything is saved (03 §10).</summary>
    public Task<CostInputsDto> GetCostInputsAsync(CancellationToken ct = default) =>
        GetJsonAsync<CostInputsDto>("/api/cost/inputs", ct);

    public Task<StatusDto> GetStatusAsync(CancellationToken ct = default) =>
        GetJsonAsync<StatusDto>("/api/status", ct);

    public async Task<GameSummaryDto> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default) =>
        await ReadJsonAsync<GameSummaryDto>(await http.PostAsJsonAsync("/api/games", request, JsonOptions, ct), ct);

    public Task<PageDto<GameSummaryDto>> GetGamesAsync(int page, int pageSize, string? model, string? status,
        CancellationToken ct = default) =>
        GetJsonAsync<PageDto<GameSummaryDto>>(
            WithQuery("/api/games", ("page", page), ("pageSize", pageSize), ("model", model), ("status", status)), ct);

    public Task<GameDetailDto> GetGameAsync(Guid id, CancellationToken ct = default) =>
        GetJsonAsync<GameDetailDto>($"/api/games/{id}", ct);

    public async Task<string> GetGamePgnAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/api/games/{id}/pgn", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public Task AbortGameAsync(Guid id, CancellationToken ct = default) =>
        EnsureSuccessAsync(http.DeleteAsync($"/api/games/{id}", ct), ct);

    public async Task<TournamentCreatedDto> CreateTournamentAsync(CreateTournamentRequest request, CancellationToken ct = default) =>
        await ReadJsonAsync<TournamentCreatedDto>(await http.PostAsJsonAsync("/api/tournaments", request, JsonOptions, ct), ct);

    public Task<IReadOnlyList<TournamentDto>> GetTournamentsAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<TournamentDto>>("/api/tournaments", ct);

    public Task<TournamentDto> GetTournamentAsync(Guid id, CancellationToken ct = default) =>
        GetJsonAsync<TournamentDto>($"/api/tournaments/{id}", ct);

    public Task DeleteTournamentAsync(Guid id, CancellationToken ct = default) =>
        EnsureSuccessAsync(http.DeleteAsync($"/api/tournaments/{id}", ct), ct);

    public Task<LeaderboardDto> GetLeaderboardAsync(CancellationToken ct = default) =>
        GetJsonAsync<LeaderboardDto>("/api/ratings", ct);

    public Task<PairwiseCoefficient> GetPairwiseAsync(string a, string b, CancellationToken ct = default) =>
        GetJsonAsync<PairwiseCoefficient>(WithQuery("/api/ratings/pairwise", ("a", a), ("b", b)), ct);

    public Task<MatrixDto> GetRatingMatrixAsync(CancellationToken ct = default) =>
        GetJsonAsync<MatrixDto>("/api/ratings/matrix", ct);

    public Task<IReadOnlyList<RatingHistoryPointDto>> GetRatingHistoryAsync(string model, CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<RatingHistoryPointDto>>(WithQuery("/api/ratings/history", ("model", model)), ct);

    public Task<IReadOnlyList<ModelResultsDto>> GetResultsBreakdownAsync(Guid? tournamentId, CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<ModelResultsDto>>(
            WithQuery("/api/results/breakdown", ("tournamentId", tournamentId)), ct);

    /// <summary>03 §10: what the game's models used and what it cost (404 for an unknown game).</summary>
    public Task<GameUsageDto> GetGameUsageAsync(Guid id, CancellationToken ct = default) =>
        GetJsonAsync<GameUsageDto>($"/api/games/{id}/usage", ct);

    public Task<PageDto<AttemptDto>> GetAttemptsAsync(Guid? gameId, string? model, int page, CancellationToken ct = default) =>
        GetJsonAsync<PageDto<AttemptDto>>(
            WithQuery("/api/attempts", ("gameId", gameId), ("model", model), ("page", page)), ct);

    /// <summary>03 §12.2: the candidate's gauntlet. The estimate in the response is the API's, for the record of the run.</summary>
    public async Task<BenchmarkCreatedDto> CreateBenchmarkAsync(CreateBenchmarkRequest request, CancellationToken ct = default) =>
        await ReadJsonAsync<BenchmarkCreatedDto>(await http.PostAsJsonAsync("/api/benchmarks", request, JsonOptions, ct), ct);

    public Task<IReadOnlyList<BenchmarkDto>> GetBenchmarksAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<BenchmarkDto>>("/api/benchmarks", ct);

    public Task<BenchmarkDto> GetBenchmarkAsync(Guid id, CancellationToken ct = default) =>
        GetJsonAsync<BenchmarkDto>($"/api/benchmarks/{id}", ct);

    /// <summary>The report so far: a running benchmark reports the games it has decided (03 §12.2).</summary>
    public Task<BenchmarkReportDto> GetBenchmarkReportAsync(Guid id, CancellationToken ct = default) =>
        GetJsonAsync<BenchmarkReportDto>($"/api/benchmarks/{id}/report", ct);

    /// <summary>The same report as Markdown, the text a download link carries (<c>?format=md</c>).</summary>
    public async Task<string> GetBenchmarkReportMarkdownAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/api/benchmarks/{id}/report?format=md", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    public Task CancelBenchmarkAsync(Guid id, CancellationToken ct = default) =>
        EnsureSuccessAsync(http.PostAsync($"/api/benchmarks/{id}/cancel", null, ct), ct);

    /// <summary>03 §15.4: the menu, its styles, the tiers and the command help. Pages treat a failure as "requests are off".</summary>
    public Task<RequestMenuDto> GetRequestMenuAsync(CancellationToken ct = default) =>
        GetJsonAsync<RequestMenuDto>("/api/requests/menu", ct);

    /// <summary>The price of one matchup. Codes may carry a <c>~style</c>, as in a Super Chat command. PriceUsd null: not offered.</summary>
    public Task<QuoteDto> GetRequestQuoteAsync(string white, string black, CancellationToken ct = default) =>
        GetJsonAsync<QuoteDto>(WithQuery("/api/requests/quote", ("white", white), ("black", black)), ct);

    /// <summary>The running request, the queue and the recent requests. A 404 means viewer-paid games are not on.</summary>
    public Task<RequestQueueDto> GetRequestQueueAsync(CancellationToken ct = default) =>
        GetJsonAsync<RequestQueueDto>("/api/requests/queue", ct);

    /// <summary>
    /// Runs a chat message through the intake as YouTube would. Only in Mock mode: any other mode answers 403, which
    /// surfaces as an <see cref="HttpRequestException"/> whose <see cref="HttpRequestException.StatusCode"/> is 403.
    /// </summary>
    public Task SimulateChatAsync(SimulateChatRequest request, CancellationToken ct = default) =>
        EnsureSuccessAsync(http.PostAsJsonAsync("/api/requests/simulate", request, JsonOptions, ct), ct);

    public Task CancelRequestAsync(Guid id, CancellationToken ct = default) =>
        EnsureSuccessAsync(http.PostAsync($"/api/requests/{id:D}/cancel", null, ct), ct);

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct) =>
        await ReadJsonAsync<T>(await http.GetAsync(url, ct), ct);

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new InvalidOperationException($"Empty body from {response.RequestMessage?.RequestUri}.");
        }
    }

    private static async Task EnsureSuccessAsync(Task<HttpResponseMessage> send, CancellationToken ct)
    {
        using var response = await send;
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Appends the non-null parameters to <paramref name="path"/>, percent-encoded.</summary>
    private static string WithQuery(string path, params (string Name, object? Value)[] parameters)
    {
        var pairs = parameters
            .Where(p => p.Value is not null)
            .Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(Convert.ToString(p.Value, CultureInfo.InvariantCulture)!)}");
        var query = string.Join('&', pairs);
        return query.Length == 0 ? path : $"{path}?{query}";
    }
}
