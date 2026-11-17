using Chess.Api.Catalog;
using Chess.Api.Contracts;
using Chess.Api.Persistence;
using Chess.Api.Tournaments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Chess.Api.Broadcast;

/// <summary>
/// One slot as the planner page sends it to <c>PUT /api/broadcast/schedules/{id}/slots</c>. The array order is the
/// play order; the slot index is assigned from it.
/// </summary>
public sealed record SlotEdit(PlayerSpec White, PlayerSpec Black, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd);

/// <summary>
/// The <c>/api/broadcast</c> routes (03 §9.2): plan a day (saved as a draft), create a schedule from picked games in one
/// call (additive), list and read schedules, delete a draft, replace the slots of a draft or scheduled schedule, and
/// <c>/now</c> for the broadcast page and OBS. Nothing here plays a game; the control routes hand the schedule to the
/// broadcast runner.
/// </summary>
public static class BroadcastEndpoints
{
    /// <summary>
    /// The plies the premium estimate assumes when a stored schedule is read. The plan request carries
    /// <c>AssumedPlies</c>, but 03 §9.3 does not store it, so reads use the 03 §9.1 assumption.
    /// </summary>
    public const double StoredAssumedPlies = 80;

    /// <summary>The longest window a plan may cover. A week of slots is already far beyond one broadcast day.</summary>
    public const int MaxDurationHours = 168;

    /// <summary>The longest schedule name, as the planner page limits it.</summary>
    public const int MaxNameLength = 120;

    /// <summary>
    /// How far a slot may overrun the schedule window and still count as inside it. A stored time keeps microseconds
    /// (PostgreSQL <c>timestamptz</c>) while a client's has 100 ns, so a window that ends exactly on a slot's end can read back
    /// a few hundred nanoseconds short.
    /// </summary>
    private static readonly TimeSpan WindowTolerance = TimeSpan.FromMilliseconds(1);

    private const string LlmKind = "llm";
    private const string RandomKind = "random";

    public static IEndpointRouteBuilder MapBroadcastEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var broadcast = endpoints.MapGroup("/broadcast");
        broadcast.MapPost("/plan", PlanAsync);
        broadcast.MapPost("/schedules", CreateAsync);
        broadcast.MapGet("/schedules", ListAsync);
        broadcast.MapGet("/schedules/{id:guid}", GetAsync);
        broadcast.MapDelete("/schedules/{id:guid}", DeleteAsync);
        broadcast.MapPut("/schedules/{id:guid}/slots", ReplaceSlotsAsync);
        broadcast.MapGet("/now", NowAsync);
        broadcast.MapPost("/schedules/{id:guid}/start", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CostInputs costs, CancellationToken ct) =>
            CommandAsync(id, controller.StartAsync, factory, catalog, costs, ct));
        broadcast.MapPost("/schedules/{id:guid}/pause", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CostInputs costs, CancellationToken ct) =>
            CommandAsync(id, controller.PauseAsync, factory, catalog, costs, ct));
        broadcast.MapPost("/schedules/{id:guid}/resume", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CostInputs costs, CancellationToken ct) =>
            CommandAsync(id, controller.ResumeAsync, factory, catalog, costs, ct));
        broadcast.MapPost("/schedules/{id:guid}/stop", (Guid id, IBroadcastController controller,
            IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CostInputs costs, CancellationToken ct) =>
            CommandAsync(id, controller.StopAsync, factory, catalog, costs, ct));
        return endpoints;
    }

    private static async Task<IResult> PlanAsync(
        PlanRequest? request,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CostInputs costs,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid plan", "A plan request is required.");
        }

        if (request.Models is null || request.Pacing is null)
        {
            return ValidationProblemFor("request", "Models and pacing are required.");
        }

        if (request.DurationHours > MaxDurationHours)
        {
            return ValidationProblemFor("durationHours",
                $"DurationHours must be at most {MaxDurationHours}.");
        }

        // Npgsql refuses a timestamptz with an offset, so the window is stored in UTC whatever offset was sent.
        request = request with { StartsAt = request.StartsAt.ToUniversalTime() };

        var listing = await catalog.GetModelsAsync(ct);
        var inputs = await costs.ForAsync(listing, ct);

        BroadcastPlan plan;
        try
        {
            plan = BroadcastPlanner.Plan(request, inputs, costs.UsdPerAiCredit);
        }
        catch (InvalidPlanException ex)
        {
            return ValidationProblemFor("request", ex.Message);
        }

        // With the gateway down only the random player can be checked; a draft may still name LLMs.
        if (listing.Warning is null)
        {
            var models = ModelErrors(request.Models, listing);
            if (models is not null)
            {
                return TypedResults.ValidationProblem(models);
            }
        }

        var distinctModels = request.Models.Distinct(StringComparer.Ordinal).ToList();
        var schedule = new BroadcastScheduleEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Status = ScheduleStatus.Draft,
            StartsAt = request.StartsAt,
            EndsAt = request.StartsAt.AddHours(request.DurationHours),
            MoveDelayMs = request.Pacing.MoveDelayMs,
            GameGapSeconds = request.Pacing.GameGapSeconds,
            Seed = request.Seed,
            Models = distinctModels,
            CreatedAt = clock.GetUtcNow(),
        };
        var slots = plan.Slots.Select(slot => ToSlotEntity(schedule.Id, slot)).ToList();

        await using var db = await factory.CreateDbContextAsync(ct);
        db.BroadcastSchedules.Add(schedule);
        db.BroadcastSlots.AddRange(slots);
        await db.SaveChangesAsync(ct);

        var detail = new ScheduleDetailDto(
            ToScheduleDto(schedule, slots.Count, slots.Count(s => s.State == SlotState.Finished),
                plan.EstimatedPremiumRequests),
            slots.OrderBy(s => s.Index).Select(ToSlotDto).ToList());
        return TypedResults.Created($"/api/broadcast/schedules/{schedule.Id}", detail);
    }

    /// <summary>
    /// Creates a schedule from the games a person picked, in one call: validates them like <c>PUT .../slots</c>, saves the
    /// schedule and its slots together, and starts it when <see cref="CreateScheduleRequest.Start"/> is set. A refusal leaves
    /// nothing behind: a start the controller refuses removes the schedule it just saved.
    /// </summary>
    private static async Task<IResult> CreateAsync(
        CreateScheduleRequest? request,
        IBroadcastController controller,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CostInputs costs,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "Invalid schedule", "A schedule request is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ValidationProblemFor("name", "Name is required.");
        }

        if (request.Name.Trim().Length > MaxNameLength)
        {
            return ValidationProblemFor("name", $"Name must be at most {MaxNameLength} characters.");
        }

        if (request.Pacing is null)
        {
            return ValidationProblemFor("pacing", "Pacing is required.");
        }

        if (request.Games is null || request.Games.Count == 0)
        {
            return ValidationProblemFor("games", "At least one game is required.");
        }

        if (request.Games.Count > BroadcastPlanner.MaxSlots)
        {
            return ValidationProblemFor("games", $"A schedule holds at most {BroadcastPlanner.MaxSlots} games.");
        }

        if (request.Games.Any(game => game?.White is null || game.Black is null
                || string.IsNullOrWhiteSpace(game.White.ModelId) || string.IsNullOrWhiteSpace(game.Black.ModelId)))
        {
            return ValidationProblemFor("games", "Every game needs a White and a Black player, each with a model id.");
        }

        var startsAt = (request.StartsAt ?? WholeSeconds(clock.GetUtcNow())).ToUniversalTime();
        if (startsAt.Year is < 2000 or > 2200)
        {
            return ValidationProblemFor("startsAt", "StartsAt must be between the years 2000 and 2200.");
        }

        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> windows;
        try
        {
            windows = BroadcastPlanner.Layout(request.Games.Count, startsAt, request.AssumedPlies,
                request.AssumedMoveLatencyMs, request.Pacing);
        }
        catch (InvalidPlanException ex)
        {
            return ValidationProblemFor("games", ex.Message);
        }

        var edits = request.Games
            .Select((game, index) => new SlotEdit(game.White, game.Black, windows[index].Start, windows[index].End))
            .ToList();
        var models = edits.SelectMany(edit => new[] { edit.White.ModelId, edit.Black.ModelId })
            .Distinct(StringComparer.Ordinal).ToList();

        var listing = await catalog.GetModelsAsync(ct);
        if (listing.Warning is null && ModelErrors(models, listing) is { } modelErrors)
        {
            return TypedResults.ValidationProblem(modelErrors);
        }

        var spanTicks = (windows[^1].End - startsAt).Ticks;
        var hours = (int)((spanTicks + TimeSpan.TicksPerHour - 1) / TimeSpan.TicksPerHour);
        var schedule = new BroadcastScheduleEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Status = ScheduleStatus.Draft,
            StartsAt = startsAt,
            EndsAt = startsAt.AddHours(Math.Max(1, hours)),
            MoveDelayMs = request.Pacing.MoveDelayMs,
            GameGapSeconds = request.Pacing.GameGapSeconds,
            Seed = request.Seed,
            Models = models,
            CreatedAt = clock.GetUtcNow(),
        };
        if (SlotErrors(edits, schedule) is { } slotErrors)
        {
            return TypedResults.ValidationProblem(slotErrors);
        }

        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            if (request.Start && await ChannelBusyAsync(db, ct))
            {
                return ChannelBusy();
            }

            db.BroadcastSchedules.Add(schedule);
            db.BroadcastSlots.AddRange(edits.Select((edit, index) => ToSlotEntity(schedule.Id, index, edit)));
            await db.SaveChangesAsync(ct);
        }

        if (request.Start)
        {
            BroadcastCommandResult started;
            try
            {
                started = await controller.StartAsync(schedule.Id, ct);
            }
            catch
            {
                await RemoveDraftAsync(factory, schedule.Id);
                throw;
            }

            if (started != BroadcastCommandResult.Accepted)
            {
                // Lost a race with another start: leave no draft behind.
                await RemoveDraftAsync(factory, schedule.Id);
                return ChannelBusy();
            }
        }

        var inputs = await costs.ForAsync(listing, ct);
        await using var read = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(read, schedule.Id, ct);
        if (loaded is null)
        {
            return NotFound(schedule.Id);
        }

        var (stored, slots) = loaded.Value;
        var detail = new ScheduleDetailDto(ToScheduleDto(stored, slots, inputs, costs.UsdPerAiCredit),
            slots.Select(ToSlotDto).ToList());
        return TypedResults.Created($"/api/broadcast/schedules/{schedule.Id}", detail);
    }

    /// <summary>Deletes a draft and its slots. Only a draft: a schedule that has started is history, and 409 says so.</summary>
    private static async Task<IResult> DeleteAsync(Guid id, IDbContextFactory<ArenaDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return NotFound(id);
        }

        if (schedule.Status != ScheduleStatus.Draft)
        {
            return Conflict(schedule.Status, "Only a draft can be deleted.");
        }

        db.BroadcastSlots.RemoveRange(await db.BroadcastSlots.Where(s => s.ScheduleId == id).ToListAsync(ct));
        db.BroadcastSchedules.Remove(schedule);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<bool> ChannelBusyAsync(ArenaDbContext db, CancellationToken ct) =>
        db.BroadcastSchedules.AnyAsync(s => s.Status == ScheduleStatus.Running || s.Status == ScheduleStatus.Paused, ct);

    private static ProblemHttpResult ChannelBusy() => Problem(StatusCodes.Status409Conflict, "Another broadcast is playing",
        "Stop or finish the broadcast that is running or paused before starting this one.");

    /// <summary>Removes a schedule this request created and could not start. Not cancellable: the cleanup must finish.</summary>
    private static async Task RemoveDraftAsync(IDbContextFactory<ArenaDbContext> factory, Guid id)
    {
        await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == id && s.Status == ScheduleStatus.Draft);
        if (schedule is null)
        {
            return;
        }

        db.BroadcastSlots.RemoveRange(await db.BroadcastSlots.Where(s => s.ScheduleId == id).ToListAsync());
        db.BroadcastSchedules.Remove(schedule);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static DateTimeOffset WholeSeconds(DateTimeOffset at) =>
        new(at.Ticks - at.Ticks % TimeSpan.TicksPerSecond, at.Offset);

    private static async Task<IResult> ListAsync(
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CostInputs costs,
        CancellationToken ct)
    {
        var inputs = await costs.ForAsync(await catalog.GetModelsAsync(ct), ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var schedules = await db.BroadcastSchedules.AsNoTracking().OrderByDescending(s => s.CreatedAt).ToListAsync(ct);
        var slots = await db.BroadcastSlots.AsNoTracking().ToListAsync(ct);

        var dtos = schedules.Select(schedule =>
        {
            var own = slots.Where(s => s.ScheduleId == schedule.Id).ToList();
            return ToScheduleDto(schedule, own, inputs, costs.UsdPerAiCredit);
        }).ToList();
        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CostInputs costs,
        CancellationToken ct)
    {
        var inputs = await costs.ForAsync(await catalog.GetModelsAsync(ct), ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (loaded is null)
        {
            return NotFound(id);
        }

        var (schedule, slots) = loaded.Value;
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, slots, inputs, costs.UsdPerAiCredit),
            slots.Select(ToSlotDto).ToList()));
    }

    private static async Task<IResult> ReplaceSlotsAsync(
        Guid id,
        IReadOnlyList<SlotEdit>? edits,
        IDbContextFactory<ArenaDbContext> factory,
        ModelCatalog catalog,
        CostInputs costs,
        CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (loaded is null)
        {
            return NotFound(id);
        }

        var (schedule, existing) = loaded.Value;
        if (schedule.Status is not (ScheduleStatus.Draft or ScheduleStatus.Scheduled))
        {
            return Conflict(schedule.Status, "Slots can only be edited while the schedule is a draft or scheduled.");
        }

        if (edits is null || edits.Count == 0)
        {
            return ValidationProblemFor("slots", "At least one slot is required.");
        }

        var errors = SlotErrors(edits, schedule);
        if (errors is not null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var replaced = edits.Select((edit, index) => ToSlotEntity(schedule.Id, index, edit)).ToList();

        // Two saves: the (schedule, index) key is unique, so the old rows must be gone before the new ones are inserted.
        db.BroadcastSlots.RemoveRange(existing);
        await db.SaveChangesAsync(ct);
        db.BroadcastSlots.AddRange(replaced);
        await db.SaveChangesAsync(ct);

        var inputs = await costs.ForAsync(await catalog.GetModelsAsync(ct), ct);
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, replaced, inputs, costs.UsdPerAiCredit),
            replaced.Select(ToSlotDto).ToList()));
    }

    /// <summary>
    /// The controller owns what the broadcast page shows (its status, the game in play, the next slots and the
    /// technical-break notice), so this route passes its answer through.
    /// </summary>
    private static async Task<IResult> NowAsync(IBroadcastController controller, CancellationToken ct) =>
        TypedResults.Ok(await controller.NowAsync(ct));

    /// <summary>
    /// Runs a schedule command on the controller and answers with the schedule as stored: 200 when accepted, 404 when
    /// the schedule does not exist, 409 when its status does not allow the command.
    /// </summary>
    private static async Task<IResult> CommandAsync(Guid id,
        Func<Guid, CancellationToken, Task<BroadcastCommandResult>> command,
        IDbContextFactory<ArenaDbContext> factory, ModelCatalog catalog, CostInputs costs, CancellationToken ct)
    {
        var result = await command(id, ct);

        await using var db = await factory.CreateDbContextAsync(ct);
        var loaded = await LoadAsync(db, id, ct);
        if (result == BroadcastCommandResult.NotFound || loaded is null)
        {
            return NotFound(id);
        }

        if (result == BroadcastCommandResult.Conflict)
        {
            return Conflict(loaded.Value.Schedule.Status, "That command does not apply to a schedule in this state.");
        }

        var inputs = await costs.ForAsync(await catalog.GetModelsAsync(ct), ct);
        var (schedule, slots) = loaded.Value;
        return TypedResults.Ok(new ScheduleDetailDto(ToScheduleDto(schedule, slots, inputs, costs.UsdPerAiCredit),
            slots.Select(ToSlotDto).ToList()));
    }

    private static async Task<(BroadcastScheduleEntity Schedule, List<BroadcastSlotEntity> Slots)?> LoadAsync(
        ArenaDbContext db, Guid id, CancellationToken ct)
    {
        var schedule = await db.BroadcastSchedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null)
        {
            return null;
        }

        var slots = await db.BroadcastSlots.Where(s => s.ScheduleId == id).OrderBy(s => s.Index).ToListAsync(ct);
        return (schedule, slots);
    }

    /// <summary>
    /// Checks the model names of a plan against the gateway's list, as game creation does (03 §5). A model may be a variant,
    /// <c>model@effort</c>: its base model must be listed, and the effort must be one it offers (<see cref="ModelVariant"/>).
    /// </summary>
    private static Dictionary<string, string[]>? ModelErrors(IEnumerable<string> models, ModelListing listing)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var model in models.Distinct(StringComparer.Ordinal))
        {
            if (model == ModelCatalog.RandomModelId)
            {
                continue;
            }

            var baseModel = ModelVariant.BaseOf(model);
            var listed = listing.Models.FirstOrDefault(m => m.Id == baseModel);
            if (listed is null || baseModel == ModelCatalog.RandomModelId)
            {
                errors["models"] = [$"The model '{model}' is not one the gateway offers."];
                return errors;
            }

            if (ModelVariant.Problem(model, listing) is { } effortProblem)
            {
                errors["models"] = [effortProblem];
                return errors;
            }

            if (!listed.Enabled)
            {
                errors["models"] = [$"The model '{model}' is not enabled."];
                return errors;
            }
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>Checks that the edited slots are playable: known players, a proper window, in play order, no overlap.</summary>
    private static Dictionary<string, string[]>? SlotErrors(IReadOnlyList<SlotEdit> edits, BroadcastScheduleEntity schedule)
    {
        var errors = new List<string>();
        DateTimeOffset? previousEnd = null;

        for (var index = 0; index < edits.Count; index++)
        {
            var edit = edits[index];
            var label = $"Slot {index + 1}";
            if (edit.White is null || edit.Black is null)
            {
                errors.Add($"{label}: both players are required.");
                continue;
            }

            foreach (var (side, player) in new[] { ("White", edit.White), ("Black", edit.Black) })
            {
                if (player.Kind is not (LlmKind or RandomKind))
                {
                    errors.Add($"{label}: the {side} player's kind must be '{LlmKind}' or '{RandomKind}'.");
                }
                else if ((player.Kind == RandomKind) != (player.ModelId == ModelCatalog.RandomModelId))
                {
                    errors.Add($"{label}: the {side} random player's model must be '{ModelCatalog.RandomModelId}'.");
                }
                else if (!schedule.Models.Contains(player.ModelId, StringComparer.Ordinal))
                {
                    errors.Add($"{label}: the {side} model '{player.ModelId}' is not one of the schedule's models.");
                }
            }

            if (edit.White == edit.Black)
            {
                errors.Add($"{label}: a player cannot play itself.");
            }

            if (edit.PlannedEnd <= edit.PlannedStart)
            {
                errors.Add($"{label}: the slot must end after it starts.");
            }

            if (edit.PlannedStart < schedule.StartsAt - WindowTolerance || edit.PlannedEnd > schedule.EndsAt + WindowTolerance)
            {
                errors.Add($"{label}: the slot must lie inside the schedule window.");
            }

            if (previousEnd is { } end && edit.PlannedStart < end)
            {
                errors.Add($"{label}: the slot starts before the previous one ends; slots play one at a time in order.");
            }

            previousEnd = edit.PlannedEnd;
        }

        return errors.Count == 0 ? null : new Dictionary<string, string[]> { ["slots"] = errors.ToArray() };
    }

    /// <summary>A slot as stored: times in UTC, because Npgsql refuses a <c>timestamptz</c> with an offset.</summary>
    private static BroadcastSlotEntity ToSlotEntity(Guid scheduleId, int index, SlotEdit edit) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = scheduleId,
        Index = index,
        WhiteKind = edit.White.Kind,
        WhiteModel = edit.White.ModelId,
        BlackKind = edit.Black.Kind,
        BlackModel = edit.Black.ModelId,
        PlannedStart = edit.PlannedStart.ToUniversalTime(),
        PlannedEnd = edit.PlannedEnd.ToUniversalTime(),
        State = SlotState.Pending,
    };

    private static BroadcastSlotEntity ToSlotEntity(Guid scheduleId, PlannedSlot slot) => new()
    {
        Id = Guid.NewGuid(),
        ScheduleId = scheduleId,
        Index = slot.Index,
        WhiteKind = slot.White.Kind,
        WhiteModel = slot.White.ModelId,
        BlackKind = slot.Black.Kind,
        BlackModel = slot.Black.ModelId,
        PlannedStart = slot.PlannedStart,
        PlannedEnd = slot.PlannedEnd,
        State = SlotState.Pending,
    };

    private static SlotDto ToSlotDto(BroadcastSlotEntity slot) => new(slot.Id, slot.Index,
        new PlayerSpec(slot.WhiteKind, slot.WhiteModel), new PlayerSpec(slot.BlackKind, slot.BlackModel),
        slot.PlannedStart, slot.PlannedEnd, slot.State, slot.GameId);

    /// <summary>The schedule as the API shows it. Plans pass the estimate the planner computed for the request.</summary>
    private static ScheduleDto ToScheduleDto(BroadcastScheduleEntity schedule, int slotsTotal, int slotsFinished,
        IReadOnlyList<ModelPremiumEstimateDto> estimate) =>
        new(schedule.Id, schedule.Name, schedule.Status, schedule.StartsAt, schedule.EndsAt,
            new PacingProfile(schedule.MoveDelayMs, schedule.GameGapSeconds), schedule.Seed, schedule.Models,
            slotsTotal, slotsFinished, estimate);

    /// <summary>The schedule as stored: the estimate is recomputed from its slots (see <see cref="StoredAssumedPlies"/>).</summary>
    private static ScheduleDto ToScheduleDto(BroadcastScheduleEntity schedule, IReadOnlyList<BroadcastSlotEntity> slots,
        IReadOnlyDictionary<string, ModelCostInputs> inputs, double usdPerAiCredit) =>
        ToScheduleDto(schedule, slots.Count, slots.Count(s => s.State == SlotState.Finished),
            schedule.Models.Select(model => BroadcastPlanner.EstimateFor(model,
                slots.Count(slot => slot.WhiteModel == model || slot.BlackModel == model), StoredAssumedPlies, inputs, usdPerAiCredit))
                .ToList());

    private static ValidationProblem ValidationProblemFor(string key, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [key] = [message] });

    private static ProblemHttpResult Problem(int status, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail);

    private static ProblemHttpResult NotFound(Guid id) => Problem(StatusCodes.Status404NotFound,
        "Schedule not found", $"No broadcast schedule has the id {id}.");

    private static ProblemHttpResult Conflict(string status, string detail) => Problem(StatusCodes.Status409Conflict,
        "Schedule status does not allow this", $"The schedule is {status}. {detail}");
}
