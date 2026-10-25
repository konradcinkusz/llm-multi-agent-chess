using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chess.Api.Broadcast;
using Chess.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chess.Api.Tests.Broadcast;

/// <summary>The <c>/api/broadcast</c> plan, schedule and slot routes (03 §9.2), on the InMemory provider.</summary>
public class EndpointsTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly PlayerSpec FakeA = new("llm", "fake-a");
    private static readonly PlayerSpec FakeB = new("llm", "fake-b");

    // 80 plies at 3 s + 4 s per move plus a 20 s gap: each seeded slot below is 10 minutes, which is what the tests need.
    private static readonly TimeSpan SlotLength = TimeSpan.FromMinutes(10);

    private static PlanRequest Plan(params string[] models) =>
        new("Test day", models, Start, DurationHours: 24, new PacingProfile(4000, 20), Seed: 42,
            AssumedPlies: 80, AssumedMoveLatencyMs: 3000);

    // ---- POST /api/broadcast/plan ----

    [Fact]
    public async Task Plan_saves_a_draft_and_returns_201_with_its_slots()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a", "fake-b", "random"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        var id = detail.Schedule.Id;
        Assert.EndsWith($"/api/broadcast/schedules/{id}", response.Headers.Location?.ToString());
        Assert.Equal(ScheduleStatus.Draft, detail.Schedule.Status);
        Assert.Equal(detail.Slots.Count, detail.Schedule.SlotsTotal);
        Assert.True(detail.Slots.Count > 0);
        Assert.Equal(0, detail.Schedule.SlotsFinished);
        Assert.All(detail.Slots, slot => Assert.Equal(SlotState.Pending, slot.State));

        var stored = await ReadAsync<ScheduleDetailDto>(await h.Client.GetAsync($"/api/broadcast/schedules/{id}"));
        Assert.Equal(detail.Slots.Select(s => (s.Index, s.White, s.Black, s.PlannedStart)),
            stored.Slots.Select(s => (s.Index, s.White, s.Black, s.PlannedStart)));
    }

    [Fact]
    public async Task Plan_estimate_is_the_planner_estimate_per_model_and_null_when_the_multiplier_is_unknown()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a", "fake-b", "random"));

        var detail = await ReadAsync<ScheduleDetailDto>(response);
        var estimate = detail.Schedule.EstimatedPremiumRequests.ToDictionary(e => e.ModelId, e => e.EstimatedPremiumRequests);
        var gamesOfA = detail.Slots.Count(s => s.White.ModelId == "fake-a" || s.Black.ModelId == "fake-a");
        // slots × (80 plies / 2) × multiplier 3
        Assert.Equal(gamesOfA * 40 * 3.0, estimate["fake-a"]);
        Assert.Null(estimate["fake-b"]);
        Assert.Equal(0.0, estimate["random"]);
    }

    [Fact]
    public async Task Plan_with_one_model_is_a_validation_problem() =>
        await AssertPlanRejectedAsync(Plan("fake-a"));

    [Fact]
    public async Task Plan_with_one_model_named_twice_is_a_validation_problem() =>
        await AssertPlanRejectedAsync(Plan("fake-a", "fake-a"));

    private static async Task AssertPlanRejectedAsync(PlanRequest request)
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "request"));
    }

    [Fact]
    public async Task Plan_accepts_a_variant_whose_effort_the_model_offers()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a@high", "fake-b"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Contains("fake-a@high", detail.Schedule.Models);
        Assert.Contains(detail.Slots, slot => slot.White.ModelId == "fake-a@high" || slot.Black.ModelId == "fake-a@high");
    }

    [Fact]
    public async Task Plan_with_an_effort_the_model_does_not_offer_is_worded_as_game_creation_words_it()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a@ultra", "fake-b"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("fake-a does not offer reasoning effort 'ultra'. It offers: low, medium, high.",
            await FirstErrorAsync(response, "models"));
    }

    [Theory]
    [InlineData("fake-a@")]
    [InlineData("fake-a@high@low")]
    [InlineData("@high")]
    [InlineData("fake-nope@high")]
    [InlineData("random@high")]
    public async Task Plan_with_a_malformed_or_unknown_variant_is_a_validation_problem(string model)
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan(model, "fake-b"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "models"));
    }

    [Fact]
    public async Task Plan_with_a_model_the_gateway_does_not_offer_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a", "fake-nope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "models"));
    }

    [Fact]
    public async Task Plan_with_a_disabled_model_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a", "fake-off"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "models"));
    }

    [Fact]
    public async Task Plan_longer_than_a_week_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan",
            Plan("fake-a", "fake-b") with { DurationHours = BroadcastEndpoints.MaxDurationHours + 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "durationHours"));
    }

    [Fact]
    public async Task Plan_whose_window_cannot_hold_one_game_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan",
            Plan("fake-a", "fake-b") with { DurationHours = 1, AssumedPlies = 100_000 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "request"));
    }

    [Fact]
    public async Task Plan_without_a_body_is_a_bad_request_problem()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PostAsync("/api/broadcast/plan", new StringContent("", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid plan", await TitleAsync(response));
    }

    [Fact]
    public async Task Plan_without_pacing_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var body = new
        {
            name = "No pacing",
            models = new[] { "fake-a", "fake-b" },
            startsAt = Start,
            durationHours = 24,
            pacing = (object?)null,
            seed = 1,
            assumedPlies = 80,
            assumedMoveLatencyMs = 3000,
        };

        var response = await h.Client.PostAsJsonAsync("/api/broadcast/plan", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "request"));
    }

    // ---- GET /api/broadcast/schedules ----

    [Fact]
    public async Task List_contains_a_saved_plan()
    {
        using var h = new BroadcastHarness();
        var planned = await ReadAsync<ScheduleDetailDto>(
            await h.Client.PostAsJsonAsync("/api/broadcast/plan", Plan("fake-a", "fake-b")));

        var listed = await ReadAsync<List<ScheduleDto>>(await h.Client.GetAsync("/api/broadcast/schedules"));

        var schedule = Assert.Single(listed, s => s.Id == planned.Schedule.Id);
        Assert.Equal(ScheduleStatus.Draft, schedule.Status);
        Assert.Equal(planned.Slots.Count, schedule.SlotsTotal);
    }

    [Fact]
    public async Task Get_returns_the_schedule_with_its_slots_in_index_order()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Scheduled, SlotState.Finished, SlotState.Finished, SlotState.Pending);

        var detail = await ReadAsync<ScheduleDetailDto>(await h.Client.GetAsync($"/api/broadcast/schedules/{id}"));

        Assert.Equal(id, detail.Schedule.Id);
        Assert.Equal(ScheduleStatus.Scheduled, detail.Schedule.Status);
        Assert.Equal(3, detail.Schedule.SlotsTotal);
        Assert.Equal(2, detail.Schedule.SlotsFinished);
        Assert.Equal(new PacingProfile(4000, 20), detail.Schedule.Pacing);
        Assert.Equal(new[] { 0, 1, 2 }, detail.Slots.Select(s => s.Index));
    }

    [Fact]
    public async Task Get_estimate_is_recomputed_from_the_stored_slots()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending, SlotState.Pending, SlotState.Pending);

        var detail = await ReadAsync<ScheduleDetailDto>(await h.Client.GetAsync($"/api/broadcast/schedules/{id}"));

        var estimate = detail.Schedule.EstimatedPremiumRequests.ToDictionary(e => e.ModelId, e => e.EstimatedPremiumRequests);
        // Three slots, each with fake-a: 3 × (80 / 2) × 3
        Assert.Equal(3 * 40 * 3.0, estimate["fake-a"]);
        Assert.Null(estimate["fake-b"]);
    }

    [Fact]
    public async Task Get_unknown_schedule_is_not_found()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.GetAsync($"/api/broadcast/schedules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Schedule not found", await TitleAsync(response));
    }

    // ---- PUT /api/broadcast/schedules/{id}/slots ----

    [Fact]
    public async Task Replacing_the_slots_of_a_draft_renumbers_them_in_the_order_given()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending, SlotState.Pending, SlotState.Pending);
        var edits = new[]
        {
            Edit(FakeB, FakeA, 0),
            Edit(FakeA, FakeB, 1),
        };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Equal(2, detail.Schedule.SlotsTotal);
        Assert.Equal(new[] { 0, 1 }, detail.Slots.Select(s => s.Index));
        Assert.Equal(FakeB, detail.Slots[0].White);
        Assert.Equal(FakeA, detail.Slots[1].White);

        using var db = h.App.NewContext();
        Assert.Equal(2, await db.BroadcastSlots.CountAsync(s => s.ScheduleId == id));
    }

    [Fact]
    public async Task Replacing_the_slots_of_a_scheduled_schedule_is_allowed()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Scheduled, SlotState.Pending);

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", new[] { Edit(FakeA, FakeB, 0) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(ScheduleStatus.Running)]
    [InlineData(ScheduleStatus.Paused)]
    [InlineData(ScheduleStatus.Finished)]
    [InlineData(ScheduleStatus.Cancelled)]
    public async Task Replacing_the_slots_of_a_started_schedule_is_a_conflict(string status)
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, status, SlotState.Pending);

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", new[] { Edit(FakeA, FakeB, 0) });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Schedule status does not allow this", await TitleAsync(response));
    }

    [Fact]
    public async Task Replacing_the_slots_of_an_unknown_schedule_is_not_found()
    {
        using var h = new BroadcastHarness();

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{Guid.NewGuid()}/slots",
            new[] { Edit(FakeA, FakeB, 0) });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_slots_with_none_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", Array.Empty<SlotEdit>());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_the_slots_with_an_overlap_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);
        var edits = new[]
        {
            Edit(FakeA, FakeB, 0),
            Edit(FakeB, FakeA, 0, minutes: 5),
        };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_the_slots_with_a_slot_outside_the_window_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);
        var edits = new[] { Edit(FakeA, FakeB, 0, startHours: 24) };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_the_slots_with_a_model_the_schedule_does_not_have_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);
        var edits = new[] { Edit(FakeA, new PlayerSpec("llm", "fake-off"), 0) };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_the_slots_with_a_player_that_plays_itself_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);
        var edits = new[] { Edit(FakeA, FakeA, 0) };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    [Fact]
    public async Task Replacing_the_slots_with_a_random_kind_that_names_an_llm_is_a_validation_problem()
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, ScheduleStatus.Draft, SlotState.Pending);
        var edits = new[] { Edit(new PlayerSpec("random", "fake-a"), FakeB, 0) };

        var response = await h.Client.PutAsJsonAsync($"/api/broadcast/schedules/{id}/slots", edits);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await HasErrorKeyAsync(response, "slots"));
    }

    // ---- POST /api/broadcast/schedules/{id}/{start|pause|resume|stop} ----

    [Theory]
    [InlineData("start", ScheduleStatus.Draft, ScheduleStatus.Running)]
    [InlineData("start", ScheduleStatus.Scheduled, ScheduleStatus.Running)]
    [InlineData("pause", ScheduleStatus.Running, ScheduleStatus.Paused)]
    [InlineData("resume", ScheduleStatus.Paused, ScheduleStatus.Running)]
    [InlineData("stop", ScheduleStatus.Scheduled, ScheduleStatus.Cancelled)]
    [InlineData("stop", ScheduleStatus.Running, ScheduleStatus.Cancelled)]
    [InlineData("stop", ScheduleStatus.Paused, ScheduleStatus.Cancelled)]
    public async Task An_accepted_command_is_handed_to_the_controller_and_answers_with_the_new_state(
        string command, string from, string to)
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, from, SlotState.Pending);

        var response = await h.Client.PostAsync($"/api/broadcast/schedules/{id}/{command}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await ReadAsync<ScheduleDetailDto>(response);
        Assert.Equal(to, detail.Schedule.Status);
        Assert.Equal(id, detail.Schedule.Id);
        Assert.Equal(1, detail.Schedule.SlotsTotal);
        Assert.Contains((command, id), h.App.Controller.Calls);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("stop")]
    public async Task A_command_on_an_unknown_schedule_is_not_found(string command)
    {
        using var h = new BroadcastHarness();
        h.App.Controller.Result = BroadcastCommandResult.NotFound;

        var response = await h.Client.PostAsync($"/api/broadcast/schedules/{Guid.NewGuid()}/{command}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Schedule not found", await TitleAsync(response));
    }

    [Theory]
    [InlineData("start", ScheduleStatus.Finished)]
    [InlineData("pause", ScheduleStatus.Draft)]
    [InlineData("resume", ScheduleStatus.Running)]
    [InlineData("stop", ScheduleStatus.Finished)]
    public async Task A_command_the_controller_refuses_is_a_conflict_with_the_current_status(string command, string status)
    {
        using var h = new BroadcastHarness();
        var id = await SeedAsync(h, status, SlotState.Pending);
        h.App.Controller.Result = BroadcastCommandResult.Conflict;

        var response = await h.Client.PostAsync($"/api/broadcast/schedules/{id}/{command}", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Schedule status does not allow this", await TitleAsync(response));
        Assert.Contains($"The schedule is {status}.", await ProblemDetailAsync(response));
    }

    // ---- helpers ----

    private static SlotEdit Edit(PlayerSpec white, PlayerSpec black, int index, int minutes = 0, int startHours = 0)
    {
        var start = Start.AddHours(startHours).AddMinutes(index * (SlotLength.TotalMinutes + 10) + minutes);
        return new SlotEdit(white, black, start, start + SlotLength);
    }

    /// <summary>Stores a schedule in the given status with one slot per state, alternating colours, back to back.</summary>
    private static async Task<Guid> SeedAsync(BroadcastHarness h, string status, params string[] slotStates)
    {
        var id = Guid.NewGuid();
        using var db = h.App.NewContext();
        db.BroadcastSchedules.Add(new BroadcastScheduleEntity
        {
            Id = id,
            Name = "Seeded",
            Status = status,
            StartsAt = Start,
            EndsAt = Start.AddHours(24),
            MoveDelayMs = 4000,
            GameGapSeconds = 20,
            Seed = 7,
            Models = ["fake-a", "fake-b"],
            CreatedAt = Start.AddDays(-1),
        });
        for (var index = 0; index < slotStates.Length; index++)
        {
            var start = Start.AddMinutes(index * (SlotLength.TotalMinutes + 10));
            var white = index % 2 == 0 ? FakeA : FakeB;
            var black = index % 2 == 0 ? FakeB : FakeA;
            db.BroadcastSlots.Add(new BroadcastSlotEntity
            {
                Id = Guid.NewGuid(),
                ScheduleId = id,
                Index = index,
                WhiteKind = white.Kind,
                WhiteModel = white.ModelId,
                BlackKind = black.Kind,
                BlackModel = black.ModelId,
                PlannedStart = start,
                PlannedEnd = start + SlotLength,
                State = slotStates[index],
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

    private static async Task<string?> TitleAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("title", out var title) ? title.GetString() : null;
    }

    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
    }
}
