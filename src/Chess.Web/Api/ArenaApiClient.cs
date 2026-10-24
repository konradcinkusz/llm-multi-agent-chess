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

    public Task<PageDto<AttemptDto>> GetAttemptsAsync(Guid? gameId, string? model, int page, CancellationToken ct = default) =>
        GetJsonAsync<PageDto<AttemptDto>>(
            WithQuery("/api/attempts", ("gameId", gameId), ("model", model), ("page", page)), ct);

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
