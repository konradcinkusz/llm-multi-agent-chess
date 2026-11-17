using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>
/// <c>POST /api/broadcast/schedules</c> (create from picked games, optionally started in the same call),
/// <c>DELETE /api/broadcast/schedules/{id}</c>, and the window, offset and cap rules the plan and slot routes share.
/// </summary>
public class EndpointsCreateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");
    private static readonly PlayerSpec Random = new("random", "random");

    private static CreateScheduleRequest Create(bool start, params ScheduleGame[] games) =>
        new("Picked day", new PacingProfile(4000, 20), games, start, AssumedPlies: 80, AssumedMoveLatencyMs: 3000);

    // ---- POST /api/broadcast/schedules ----

    [Fact]
    public async Task Create_without_start_saves_a_draft_with_the_games_in_the_order_given()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeB, FakeA), new ScheduleGame(FakeA, Random), new ScheduleGame(Random, FakeB)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Equal(ScheduleStatus.Draft, detail.Schedule.Status);
        Assert.Equal(3, detail.Schedule.SlotsTotal);
        Assert.Equal([(FakeB, FakeA), (FakeA, Random), (Random, FakeB)], detail.Slots.Select(s => (s.White, s.Black)));
        Assert.Equal([0, 1, 2], detail.Slots.Select(s => s.Index));
        Assert.Empty(h.App.Controller.Calls);
        Assert.EndsWith($"/api/broadcast/schedules/{detail.Schedule.Id}", response.Headers.Location?.ToString());

        var stored = await ReadAsync<ScheduleDetailDto>(await h.Client.GetAsync($"/api/broadcast/schedules/{detail.Schedule.Id}"));
        Assert.Equal(detail.Slots.Select(s => (s.White, s.Black, s.PlannedStart)), stored.Slots.Select(s => (s.White, s.Black, s.PlannedStart)));
    }

    [Fact]
    public async Task Create_lays_the_games_end_to_end_from_the_start_of_the_current_second()
    {
        using var h = new BroadcastHarness();

        var detail = await ReadAsync<ScheduleDetailDto>(await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeA, FakeB), new ScheduleGame(FakeB, FakeA))));

        // 80 plies × (3 s + 4 s) = 560 s a game, then the 20 s gap.
        Assert.Equal(0, detail.Slots[0].PlannedStart.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(TimeSpan.FromSeconds(560), detail.Slots[0].PlannedEnd - detail.Slots[0].PlannedStart);
        Assert.Equal(TimeSpan.FromSeconds(20), detail.Slots[1].PlannedStart - detail.Slots[0].PlannedEnd);
        Assert.Equal(detail.Slots[0].PlannedStart, detail.Schedule.StartsAt);
        Assert.True(detail.Schedule.EndsAt >= detail.Slots[1].PlannedEnd);
        Assert.Equal(TimeSpan.Zero, detail.Schedule.StartsAt.Offset);
    }

    [Fact]
    public async Task Create_with_start_saves_and_starts_in_one_call()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(true, new ScheduleGame(FakeA, Random)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Equal(ScheduleStatus.Running, detail.Schedule.Status);
        Assert.Contains(("start", detail.Schedule.Id), h.App.Controller.Calls);
        // A random player is stored with kind random and the random model id.
        Assert.Equal(Random, detail.Slots[0].Black);
    }

    [Fact]
    public async Task Create_whose_start_the_controller_refuses_leaves_no_draft_behind()
    {
        using var h = new BroadcastHarness();
        h.App.Controller.Result = BroadcastCommandResult.Conflict;

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(true, new ScheduleGame(FakeA, FakeB)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var db = h.App.NewContext();
        Assert.Empty(await db.BroadcastSchedules.ToListAsync());
        Assert.Empty(await db.BroadcastSlots.ToListAsync());
    }

    [Fact]
    public async Task Create_with_start_while_another_broadcast_plays_is_refused_without_saving_or_calling_the_controller()
    {
        using var h = new BroadcastHarness();
        var running = await SeedAsync(h, ScheduleStatus.Running);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(true, new ScheduleGame(FakeA, FakeB)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(h.App.Controller.Calls);
        using var db = h.App.NewContext();
        Assert.Equal(running, Assert.Single(await db.BroadcastSchedules.ToListAsync()).Id);
    }

    [Fact]
    public async Task Create_without_start_is_allowed_while_another_broadcast_plays()
    {
        using var h = new BroadcastHarness();
        await SeedAsync(h, ScheduleStatus.Running);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(false, new ScheduleGame(FakeA, FakeB)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_a_random_kind_that_names_an_llm_is_a_validation_problem_and_saves_nothing()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(true, new ScheduleGame(new PlayerSpec("random", "fake-a"), FakeB)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("random player's model must be 'random'", await FirstErrorAsync(response, "slots"));
        using var db = h.App.NewContext();
        Assert.Empty(await db.BroadcastSchedules.ToListAsync());
    }

    [Fact]
    public async Task Create_with_the_random_model_under_the_llm_kind_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        // The bug the planner page had: the random player sent with kind llm.
        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeA, new PlayerSpec("llm", "random"))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Create_with_a_variant_the_model_offers_is_accepted()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(new PlayerSpec("llm", "fake-a@high"), FakeB)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("fake-nope")]
    [InlineData("fake-off")]
    [InlineData("fake-a@ultra")]
    public async Task Create_with_a_model_the_gateway_does_not_offer_is_a_validation_problem(string model)
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeA, new PlayerSpec("llm", model))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "models"));
    }

    [Fact]
    public async Task Create_with_a_player_that_plays_itself_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(false, new ScheduleGame(FakeA, FakeA)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Create_with_no_games_a_blank_name_or_no_pacing_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var none = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(false));
        var nameless = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(false, new ScheduleGame(FakeA, FakeB)) with { Name = "  " });
        var unpaced = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", new
        {
            name = "No pacing",
            games = new[] { new { white = FakeA, black = FakeB } },
            start = false,
        });

        Assert.True(await HasErrorKeyAsync(none, "games"));
        Assert.True(await HasErrorKeyAsync(nameless, "name"));
        Assert.True(await HasErrorKeyAsync(unpaced, "pacing"));
        Assert.All([none, nameless, unpaced], response => Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode));
    }

    [Fact]
    public async Task Create_without_a_body_is_a_bad_request_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsync("/api/broadcast/schedules", new StringContent("", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_more_games_than_the_cap_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var games = Enumerable.Range(0, BroadcastPlanner.MaxSlots + 1).Select(_ => new ScheduleGame(FakeA, FakeB)).ToArray();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", Create(false, games));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "games"));
    }

    [Fact]
    public async Task Create_whose_games_would_run_past_a_week_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var request = Create(false, new ScheduleGame(FakeA, FakeB), new ScheduleGame(FakeB, FakeA)) with { Pacing = new PacingProfile(4000, 200 * 3600) };

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("168", await FirstErrorAsync(response, "games"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Create_with_a_non_positive_assumed_pace_is_a_validation_problem(double plies)
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeA, FakeB)) with { AssumedPlies = plies });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "games"));
    }

    [Fact]
    public async Task Create_with_games_that_fill_whole_hours_exactly_is_accepted_and_the_window_ends_with_the_last_game()
    {
        using var h = new BroadcastHarness();
        // 80 plies × (40 s + 5 s) = exactly one hour a game, no gap: the span is exactly two whole hours.
        var request = new CreateScheduleRequest("Whole hours", new PacingProfile(5000, 0),
            [new ScheduleGame(FakeA, FakeB), new ScheduleGame(FakeB, FakeA)], Start: false, StartsAt: Start, AssumedPlies: 80, AssumedMoveLatencyMs: 40_000);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/schedules", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Equal(Start.AddHours(2), detail.Schedule.EndsAt);
        Assert.Equal(detail.Schedule.EndsAt, detail.Slots[^1].PlannedEnd);
    }

    [Fact]
    public async Task Create_stores_a_non_utc_start_as_utc()
    {
        using var h = new BroadcastHarness();
        var start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.FromHours(2));

        var detail = await ReadAsync<ScheduleDetailDto>(await h.Client.PostAsJsonAsync("/api/broadcast/schedules",
            Create(false, new ScheduleGame(FakeA, FakeB)) with { StartsAt = start }));

        Assert.Equal(start, detail.Schedule.StartsAt);
        using var db = h.App.NewContext();
        var stored = await db.BroadcastSchedules.SingleAsync();
        Assert.Equal(TimeSpan.Zero, stored.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, stored.EndsAt.Offset);
        Assert.All(await db.BroadcastSlots.ToListAsync(), slot =>
        {
            Assert.Equal(TimeSpan.Zero, slot.PlannedStart.Offset);
            Assert.Equal(TimeSpan.Zero, slot.PlannedEnd.Offset);
        });
    }

    // ---- DELETE /api/broadcast/schedules/{id} ----

    [Fact]
    public async Task Delete_removes_a_draft_and_its_slots()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, slots: 2);
        var other = await SeedAsync(h, ScheduleStatus.Draft, slots: 1);

        var response = await h.Client.DeleteAsync($"/api/broadcast/schedules/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var db = h.App.NewContext();
        Assert.Equal(other, Assert.Single(await db.BroadcastSchedules.ToListAsync()).Id);
        Assert.Equal(1, await db.BroadcastSlots.CountAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.GetAsync($"/api/broadcast/schedules/{id}")).StatusCode);
    }

    [Theory]
    [InlineData(ScheduleStatus.Scheduled)]
    [InlineData(ScheduleStatus.Running)]
    [InlineData(ScheduleStatus.Paused)]
    [InlineData(ScheduleStatus.Finished)]
    [InlineData(ScheduleStatus.Cancelled)]
    public async Task Delete_of_a_schedule_that_is_not_a_draft_is_a_conflict_and_removes_nothing(string status)
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, status);

        var response = await h.Client.DeleteAsync($"/api/broadcast/schedules/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains($"The schedule is {status}.", await ProblemDetailAsync(response));
        using var db = h.App.NewContext();
        Assert.Single(await db.BroadcastSchedules.ToListAsync());
    }

    [Fact]
    public async Task Delete_of_an_unknown_schedule_is_not_found()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.DeleteAsync($"/api/broadcast/schedules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- window tolerance, offsets and caps on the existing routes ----

    [Fact]
    public async Task A_slot_that_ends_sub_microsecond_after_the_stored_window_end_is_inside_the_window()
    {
        using var h = new BroadcastHarness();
        // The stored end lost its last digits (timestamptz keeps microseconds), the client's slot kept all seven.
        var id = await SeedAsync(h, ScheduleStatus.Draft, slots: 1, endsAt: Start.AddHours(1));
        var edit = new SlotEdit(FakeA, FakeB, Start, Start.AddHours(1).AddTicks(7));

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", new[] { edit });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_slot_that_overruns_the_window_by_more_than_a_millisecond_is_still_refused()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, slots: 1, endsAt: Start.AddHours(1));
        var edit = new SlotEdit(FakeA, FakeB, Start, Start.AddHours(1).AddMilliseconds(2));

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", new[] { edit });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_slots_with_non_utc_times_stores_them_as_utc()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, slots: 1);
        var offset = TimeSpan.FromHours(-5);
        var edit = new SlotEdit(FakeA, FakeB, Start.ToOffset(offset), Start.AddMinutes(10).ToOffset(offset));

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", new[] { edit });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var db = h.App.NewContext();
        var slot = await db.BroadcastSlots.SingleAsync();
        Assert.Equal(TimeSpan.Zero, slot.PlannedStart.Offset);
        Assert.Equal(TimeSpan.Zero, slot.PlannedEnd.Offset);
    }

    [Fact]
    public async Task Plan_stores_a_non_utc_start_as_utc()
    {
        using var h = new BroadcastHarness();
        var request = new PlanRequest("Offset", ["fake-a", "fake-b"], Start.ToOffset(TimeSpan.FromHours(2)), 24,
            new PacingProfile(4000, 20), 1, 80, 3000);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var db = h.App.NewContext();
        var stored = await db.BroadcastSchedules.SingleAsync();
        Assert.Equal(TimeSpan.Zero, stored.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, stored.EndsAt.Offset);
        Assert.All(await db.BroadcastSlots.ToListAsync(), slot => Assert.Equal(TimeSpan.Zero, slot.PlannedStart.Offset));
    }

    [Fact]
    public async Task Plan_that_would_hold_a_billion_slots_is_a_validation_problem_not_an_out_of_memory()
    {
        using var h = new BroadcastHarness();
        var request = new PlanRequest("Tiny", ["fake-a", "fake-b"], Start, BroadcastEndpoints.MaxDurationHours,
            new PacingProfile(0, 0), 1, AssumedPlies: 0.5, AssumedMoveLatencyMs: 1);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "request"));
    }

    [Theory]
    [InlineData(0, 3000)]
    [InlineData(-1, 3000)]
    [InlineData(80, -1)]
    [InlineData(1e300, 3000)]
    [InlineData(80, 1e300)]
    public async Task Plan_with_a_non_positive_or_absurd_assumption_is_a_validation_problem(double plies, double latency)
    {
        using var h = new BroadcastHarness();
        var request = new PlanRequest("Odd", ["fake-a", "fake-b"], Start, 24, new PacingProfile(4000, 20), 1, plies, latency);

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "request"));
    }

    // ---- helpers ----

    private static async Task<Guid> SeedAsync(BroadcastHarness h, string status, int slots = 1, DateTimeOffset? endsAt = null)
    {
        var id = Guid.NewGuid();
        using var db = h.App.NewContext();
        db.BroadcastSchedules.Add(new BroadcastScheduleEntity
        {
            Id = id,
            Name = "Seeded",
            Status = status,
            StartsAt = Start,
            EndsAt = endsAt ?? Start.AddHours(24),
            MoveDelayMs = 4000,
            GameGapSeconds = 20,
            Seed = 7,
            Models = ["fake-a", "fake-b"],
            CreatedAt = Start.AddDays(-1),
        });
        for (var index = 0; index < slots; index++)
        {
            db.BroadcastSlots.Add(new BroadcastSlotEntity
            {
                Id = Guid.NewGuid(),
                ScheduleId = id,
                Index = index,
                WhiteKind = FakeA.Kind,
                WhiteModel = FakeA.ModelId,
                BlackKind = FakeB.Kind,
                BlackModel = FakeB.ModelId,
                PlannedStart = Start.AddMinutes(index * 10),
                PlannedEnd = Start.AddMinutes(index * 10 + 9),
                State = SlotState.Pending,
            });
        }

        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<bool> HasErrorKeyAsync(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errors", out var errors) && errors.TryGetProperty(key, out _);
    }

    private static async Task<string?> FirstErrorAsync(HttpResponseMessage response, string key)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("errors").GetProperty(key)[0].GetString();
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }
}
