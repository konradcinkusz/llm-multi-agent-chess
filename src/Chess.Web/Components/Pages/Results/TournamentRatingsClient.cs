using System.Net.Http.Json;
using System.Text.Json;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Results;

/// <summary>
/// The rating calls that take a <c>tournamentId</c> filter (03 §5). <see cref="ArenaApiClient"/> does not expose the
/// filter on these three methods, so this adapter sends the requests itself. It is built on the HttpClient of the
/// typed <see cref="ArenaApiClient"/> registration, so base address, service discovery and resilience are the same.
/// </summary>
public sealed class TournamentRatingsClient(HttpClient http)
{
    /// <summary>The name <c>AddHttpClient&lt;ArenaApiClient&gt;</c> registers under; a test checks that it resolves.</summary>
    public const string ClientName = nameof(ArenaApiClient);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Leaderboard of the games of one tournament, or of all games when <paramref name="tournamentId"/> is null.</summary>
    public Task<LeaderboardDto> GetLeaderboardAsync(Guid? tournamentId, CancellationToken ct = default) =>
        GetLeaderboardAsync(tournamentId, RankingOrder.Score, ct);

    /// <summary>
    /// The same leaderboard in <paramref name="order"/> (<see cref="RankingOrder"/>). The score order is the API default, so
    /// its request is the plain one.
    /// </summary>
    public Task<LeaderboardDto> GetLeaderboardAsync(Guid? tournamentId, string order, CancellationToken ct = default)
    {
        var url = WithTournament("/api/ratings", tournamentId);
        return GetJsonAsync<LeaderboardDto>(order == RankingOrder.Score ? url : Append(url, $"order={Uri.EscapeDataString(order)}"), ct);
    }

    public Task<PairwiseCoefficient> GetPairwiseAsync(string a, string b, Guid? tournamentId, CancellationToken ct = default) =>
        GetJsonAsync<PairwiseCoefficient>(
            WithTournament($"/api/ratings/pairwise?a={Uri.EscapeDataString(a)}&b={Uri.EscapeDataString(b)}", tournamentId), ct);

    public Task<MatrixDto> GetRatingMatrixAsync(Guid? tournamentId, CancellationToken ct = default) =>
        GetJsonAsync<MatrixDto>(WithTournament("/api/ratings/matrix", tournamentId), ct);

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException($"Empty body from {url}.");
    }

    private static string WithTournament(string url, Guid? tournamentId) =>
        tournamentId is not { } id ? url : Append(url, $"tournamentId={id:D}");

    private static string Append(string url, string parameter) => $"{url}{(url.Contains('?') ? '&' : '?')}{parameter}";
}
