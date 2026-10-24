using System.Net.Http.Json;
using System.Text.Json;

namespace Chess.Web.Api;

/// <summary>
/// Typed client for the <c>/api/broadcast</c> routes the planner page uses (03 §9.2). A refused request throws
/// <see cref="BroadcastApiException"/> carrying the API's problem text, so the page can show why.
/// </summary>
public sealed class BroadcastApiClient(HttpClient http)
{
    /// <summary>The name <c>AddHttpClient&lt;ArenaApiClient&gt;</c> registers under, so base address, service discovery and resilience are shared.</summary>
    public const string ClientName = nameof(ArenaApiClient);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Plans a day and saves it as a draft. Nothing runs.</summary>
    public async Task<ScheduleDetailDto> PlanAsync(PlanRequest request, CancellationToken ct = default) =>
        await ReadAsync<ScheduleDetailDto>(await http.PostAsJsonAsync("/api/broadcast/plan", request, JsonOptions, ct), ct);

    public async Task<IReadOnlyList<ScheduleDto>> GetSchedulesAsync(CancellationToken ct = default) =>
        await ReadAsync<IReadOnlyList<ScheduleDto>>(await http.GetAsync("/api/broadcast/schedules", ct), ct);

    public async Task<ScheduleDetailDto> GetScheduleAsync(Guid id, CancellationToken ct = default) =>
        await ReadAsync<ScheduleDetailDto>(await http.GetAsync($"/api/broadcast/schedules/{id:D}", ct), ct);

    /// <summary>Replaces the slots of a draft or scheduled schedule, in play order.</summary>
    public async Task<ScheduleDetailDto> ReplaceSlotsAsync(Guid id, IReadOnlyList<SlotEdit> slots, CancellationToken ct = default) =>
        await ReadAsync<ScheduleDetailDto>(
            await http.PutAsJsonAsync($"/api/broadcast/schedules/{id:D}/slots", slots, JsonOptions, ct), ct);

    public Task<ScheduleDetailDto> StartAsync(Guid id, CancellationToken ct = default) => CommandAsync(id, "start", ct);

    public Task<ScheduleDetailDto> PauseAsync(Guid id, CancellationToken ct = default) => CommandAsync(id, "pause", ct);

    public Task<ScheduleDetailDto> ResumeAsync(Guid id, CancellationToken ct = default) => CommandAsync(id, "resume", ct);

    public Task<ScheduleDetailDto> StopAsync(Guid id, CancellationToken ct = default) => CommandAsync(id, "stop", ct);

    private async Task<ScheduleDetailDto> CommandAsync(Guid id, string command, CancellationToken ct) =>
        await ReadAsync<ScheduleDetailDto>(await http.PostAsync($"/api/broadcast/schedules/{id:D}/{command}", null, ct), ct);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new BroadcastApiException((int)response.StatusCode, await ProblemTextAsync(response, ct));
            }

            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new InvalidOperationException($"Empty body from {response.RequestMessage?.RequestUri}.");
        }
    }

    /// <summary>The validation messages when the API sent them, else its detail or title, else the status code.</summary>
    private static async Task<string> ProblemTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            // Read as text: the API answers with application/problem+json, which ReadFromJsonAsync does not accept.
            var problem = JsonSerializer.Deserialize<ProblemBody>(await response.Content.ReadAsStringAsync(ct), JsonOptions);
            var messages = problem?.Errors?.Values.SelectMany(values => values).ToList();
            if (messages is { Count: > 0 })
            {
                return string.Join(" ", messages);
            }

            var text = problem?.Detail ?? problem?.Title;
            if (text is not null)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not a problem document: fall through to the status code.
        }

        return $"HTTP {(int)response.StatusCode}";
    }

    private sealed record ProblemBody(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

/// <summary>A broadcast API call the API refused. <see cref="StatusCode"/> is the HTTP status (409 when the state does not allow it).</summary>
public sealed class BroadcastApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
