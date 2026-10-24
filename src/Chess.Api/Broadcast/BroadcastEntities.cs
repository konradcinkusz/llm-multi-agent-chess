namespace Chess.Api.Broadcast;

/// <summary>One row of <c>broadcast_schedules</c> (03 §9.3). <see cref="Models"/> is a jsonb string array.</summary>
public sealed class BroadcastScheduleEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = ScheduleStatus.Draft;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public int MoveDelayMs { get; set; }
    public int GameGapSeconds { get; set; }
    public int Seed { get; set; }
    public List<string> Models { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

/// <summary>One row of <c>broadcast_slots</c> (03 §9.3): one planned game of a schedule.</summary>
public sealed class BroadcastSlotEntity
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public int Index { get; set; }
    public string WhiteKind { get; set; } = "";
    public string WhiteModel { get; set; } = "";
    public string BlackKind { get; set; } = "";
    public string BlackModel { get; set; } = "";
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public string State { get; set; } = SlotState.Pending;
    public Guid? GameId { get; set; }
    public int Attempts { get; set; }
    public string? Note { get; set; }
}
