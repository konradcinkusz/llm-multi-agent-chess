using System.Net.Http.Json;
using System.Text.Json;
using Chess.Web.Api;

namespace Chess.Web.Components.Pages.Broadcast;

/// <summary>
/// The two broadcast reads the OBS page makes: <c>GET /api/broadcast/now</c> and the schedule list. Only the
/// fields the page shows are copied from 03 §9.2; the rest of the JSON is ignored.
/// </summary>
public sealed class LiveBroadcastClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<LiveNowNext> GetNowAsync(CancellationToken ct = default) =>
        GetJsonAsync<LiveNowNext>("/api/broadcast/now", ct);

    public Task<IReadOnlyList<LiveScheduleSummary>> GetSchedulesAsync(CancellationToken ct = default) =>
        GetJsonAsync<IReadOnlyList<LiveScheduleSummary>>("/api/broadcast/schedules", ct);

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new InvalidOperationException($"Empty body from {url}.");
    }
}

/// <summary>What the broadcast is doing now (03 §9.2 NowNextDto). Status is idle, running or paused.</summary>
public sealed record LiveNowNext(string Status, LiveSlot? Current, IReadOnlyList<LiveSlot> Next, string? Notice,
    DateTimeOffset? EndsAt);

/// <summary>One game of the schedule (03 §9.2 SlotDto). State is pending, running, finished or skipped.</summary>
public sealed record LiveSlot(Guid Id, int Index, PlayerSpec White, PlayerSpec Black, DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd, string State, Guid? GameId);

/// <summary>
/// The part of a schedule (03 §9.2 ScheduleDto) the header needs: how many games it has, and the pace its replays and
/// games use. Pacing is null when the API does not send it, and the page then uses its own defaults.
/// </summary>
public sealed record LiveScheduleSummary(Guid Id, string Status, int SlotsTotal, int SlotsFinished, PacingProfile? Pacing = null);
