namespace Chess.Api.Broadcast;

/// <summary>Schedule status values (03 §9.1), stored as lower-case strings like tournament status.</summary>
public static class ScheduleStatus
{
    public const string Draft = "draft";

    /// <summary>Never set by the app today (a started day goes straight to Running); kept because persisted values and the contract name it.</summary>
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Finished = "finished";
    public const string Cancelled = "cancelled";
}

/// <summary>Slot state values (03 §9.2).</summary>
public static class SlotState
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Finished = "finished";
    public const string Skipped = "skipped";
}
