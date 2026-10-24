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
