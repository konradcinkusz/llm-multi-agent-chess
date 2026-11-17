using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Xunit;

namespace Chess.Api.Tests.Postgres;

/// <summary>
/// The broadcast planner and its schedule routes on PostgreSQL. A <c>timestamptz</c> column keeps microseconds, so the 100 ns digit
/// of a .NET value is lost on the way in; the slot validation compares what a client sends back with the stored window, and the
/// window edge is where a lost digit would show.
/// </summary>
public class PostgresBroadcastTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static PlanRequest Plan(DateTimeOffset startsAt, int hours, int moveDelayMs = 4500, int gapSeconds = 0,
        double assumedMoveLatencyMs = 3000) =>
        new("Postgres day", ["fake-a", "fake-b", "random"], startsAt, hours, new PacingProfile(moveDelayMs, gapSeconds),
            Seed: 7, AssumedPlies: 80, AssumedMoveLatencyMs: assumedMoveLatencyMs);

    private static SlotEdit EditOf(SlotDto slot) => new(slot.White, slot.Black, slot.PlannedStart, slot.PlannedEnd);

    [PostgresFact(Timeout = 120_000)]
    public async Task A_whole_hour_window_filled_to_the_edge_round_trips_through_the_slot_validation()
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        // 80 plies at 3 s + 4.5 s a move, no gap: a slot is exactly 10 minutes, so the last one ends at the window's end.
        var startsAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
        var detail = await PlanAsync(client, Plan(startsAt, hours: 2));
        Assert.Equal(12, detail.Slots.Count);
        Assert.Equal(startsAt.AddHours(2), detail.Schedule.EndsAt);
        Assert.Equal(detail.Schedule.EndsAt, detail.Slots[^1].PlannedEnd);

        // What the planner page does: read the stored schedule, send its slots back.
        var stored = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{detail.Schedule.Id}");
        Assert.Equal(startsAt.AddHours(2), stored.Schedule.EndsAt);
        Assert.Equal(stored.Schedule.EndsAt, stored.Slots[^1].PlannedEnd);
        Assert.All(stored.Slots, slot => Assert.True(slot.PlannedEnd <= stored.Schedule.EndsAt));

        var response = await client.PutAsJsonAsync($"/api/broadcast/schedules/{detail.Schedule.Id}/slots",
            stored.Slots.Select(EditOf).ToList());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"PUT slots answered {(int)response.StatusCode}: {body}");

        var replaced = JsonSerializer.Deserialize<ScheduleDetailDto>(body, Json)!;
        Assert.Equal(stored.Slots.Select(s => (s.Index, s.White, s.Black, s.PlannedStart, s.PlannedEnd)),
            replaced.Slots.Select(s => (s.Index, s.White, s.Black, s.PlannedStart, s.PlannedEnd)));

        var reread = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{detail.Schedule.Id}");
        Assert.Equal(stored.Slots.Select(s => (s.Index, s.PlannedStart, s.PlannedEnd)),
            reread.Slots.Select(s => (s.Index, s.PlannedStart, s.PlannedEnd)));
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task The_slots_a_plan_answers_with_are_accepted_when_its_start_carries_a_sub_microsecond_digit()
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        // The last slot ends exactly at the window's end (10-minute slots, no gap). The plan response is built from the
        // in-memory values, which keep the 100 ns digit that timestamptz drops; the PUT is checked against the stored window.
        var startsAt = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero).AddTicks(7);
        var detail = await PlanAsync(client, Plan(startsAt, hours: 1));
        var stored = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{detail.Schedule.Id}");

        // What the plan answered is what is stored: no value the client holds differs from the database by a digit.
        Assert.Equal(stored.Schedule.StartsAt, detail.Schedule.StartsAt);
        Assert.Equal(stored.Schedule.EndsAt, detail.Schedule.EndsAt);
        Assert.Equal(stored.Slots.Select(s => (s.PlannedStart, s.PlannedEnd)), detail.Slots.Select(s => (s.PlannedStart, s.PlannedEnd)));

        var response = await client.PutAsJsonAsync($"/api/broadcast/schedules/{detail.Schedule.Id}/slots",
            detail.Slots.Select(EditOf).ToList());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"PUT slots answered {(int)response.StatusCode}: {body}");
    }

    [PostgresTheory(Timeout = 120_000)]
    [InlineData(1, 0)]
    [InlineData(24, 0)]
    [InlineData(3, 7)]
    public async Task A_window_whose_edge_carries_a_sub_microsecond_digit_still_accepts_the_slots_it_planned(int hours, int extraTicks)
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        // A start with a 100 ns digit and a fractional move latency: the in-memory values carry more precision than PostgreSQL keeps.
        var startsAt = new DateTimeOffset(2026, 11, 21, 6, 0, 0, TimeSpan.Zero).AddTicks(extraTicks);
        var detail = await PlanAsync(client, Plan(startsAt, hours, moveDelayMs: 4500, gapSeconds: 0, assumedMoveLatencyMs: 3000.3333));

        var stored = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{detail.Schedule.Id}");
        Assert.NotEmpty(stored.Slots);
        Assert.All(stored.Slots, slot => Assert.True(slot.PlannedEnd <= stored.Schedule.EndsAt,
            $"slot {slot.Index} ends {slot.PlannedEnd:O} after the schedule's end {stored.Schedule.EndsAt:O}"));

        var response = await client.PutAsJsonAsync($"/api/broadcast/schedules/{detail.Schedule.Id}/slots",
            stored.Slots.Select(EditOf).ToList());
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"PUT slots answered {(int)response.StatusCode}: {body}");

        // A shorter list is a valid edit too: drop the last slot.
        if (stored.Slots.Count > 1)
        {
            var shorter = await client.PutAsJsonAsync($"/api/broadcast/schedules/{detail.Schedule.Id}/slots",
                stored.Slots.Take(stored.Slots.Count - 1).Select(EditOf).ToList());
            Assert.Equal(HttpStatusCode.OK, shorter.StatusCode);
            var after = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{detail.Schedule.Id}");
            Assert.Equal(stored.Slots.Count - 1, after.Slots.Count);
        }
    }

    [PostgresFact(Timeout = 120_000)]
    public async Task Schedules_list_and_read_and_now_answers_idle()
    {
        using var app = new PostgresApp();
        using var client = app.CreateClient();
        await app.WaitUntilReadyAsync();

        var first = await PlanAsync(client, Plan(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero), hours: 1));
        var second = await PlanAsync(client, Plan(new DateTimeOffset(2026, 11, 23, 0, 0, 0, TimeSpan.Zero), hours: 1));

        var list = await GetAsync<List<ScheduleDto>>(client, "/api/broadcast/schedules");
        Assert.Equal([second.Schedule.Id, first.Schedule.Id], list.Select(s => s.Id));
        Assert.All(list, s => Assert.Equal(ScheduleStatus.Draft, s.Status));
        Assert.All(list, s => Assert.Equal(3, s.Models.Count));

        var one = await GetAsync<ScheduleDetailDto>(client, $"/api/broadcast/schedules/{first.Schedule.Id}");
        Assert.Equal(first.Slots.Count, one.Schedule.SlotsTotal);
        Assert.Equal(first.Slots.Select(s => s.Id), one.Slots.Select(s => s.Id));
        Assert.Equal(Enumerable.Range(0, one.Slots.Count), one.Slots.Select(s => s.Index));

        var missing = await client.GetAsync($"/api/broadcast/schedules/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var now = await GetAsync<NowNextDto>(client, "/api/broadcast/now");
        Assert.Equal("idle", now.Status);
    }

    // ---- helpers ----

    private static async Task<ScheduleDetailDto> PlanAsync(HttpClient client, PlanRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/broadcast/plan", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"plan answered {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<ScheduleDetailDto>(body, Json)!;
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"GET {path} answered {(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }
}
