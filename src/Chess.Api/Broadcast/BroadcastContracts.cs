using Chess.Api.Contracts;

namespace Chess.Api.Broadcast;

// Frozen by T5.1 (03 §9.2). Changing one of these is a supervisor decision.

public sealed record PacingProfile(int MoveDelayMs, int GameGapSeconds);
public sealed record PlanRequest(string Name, IReadOnlyList<string> Models, DateTimeOffset StartsAt, int DurationHours,
    PacingProfile Pacing, int Seed, double AssumedPlies, double AssumedMoveLatencyMs);
public sealed record SlotDto(Guid Id, int Index, PlayerSpec White, PlayerSpec Black, DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd, string State, Guid? GameId);              // State: pending|running|finished|skipped
public sealed record ScheduleDto(Guid Id, string Name, string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    PacingProfile Pacing, int Seed, IReadOnlyList<string> Models, int SlotsTotal, int SlotsFinished,
    IReadOnlyList<ModelPremiumEstimateDto> EstimatedPremiumRequests);
public sealed record ScheduleDetailDto(ScheduleDto Schedule, IReadOnlyList<SlotDto> Slots);
public sealed record NowNextDto(string Status, SlotDto? Current, IReadOnlyList<SlotDto> Next, string? Notice,
    DateTimeOffset? EndsAt);                                              // Notice: technical-break text, null when fine

// Additive (the records above are untouched): one call that validates, saves and optionally starts a schedule from the games
// a person picked, for POST /api/broadcast/schedules. The server lays the games end to end, so the client sends no times.

/// <summary>One game of a created schedule: who plays White and who plays Black.</summary>
public sealed record ScheduleGame(PlayerSpec White, PlayerSpec Black);

/// <summary>
/// The body of <c>POST /api/broadcast/schedules</c>. <see cref="Games"/> is the play order. The window starts at
/// <see cref="StartsAt"/> (now, to the whole second, when null) and spans the games; the assumptions size each game as in
/// <see cref="PlanRequest"/>. <see cref="Start"/> true starts the schedule in the same call.
/// </summary>
public sealed record CreateScheduleRequest(string Name, PacingProfile Pacing, IReadOnlyList<ScheduleGame> Games, bool Start,
    DateTimeOffset? StartsAt = null, double AssumedPlies = 80, double AssumedMoveLatencyMs = 40_000, int Seed = 1);
