namespace Chess.Api.Broadcast;

public static class BroadcastExtensions
{
    /// <summary>
    /// Registers broadcast mode (ADR-011). The planner is a pure static function and needs no
    /// registration; the runner that plays the slots is added by <see cref="BroadcastRunnerExtensions.AddBroadcastRunner"/>.
    /// </summary>
    public static IHostApplicationBuilder AddBroadcast(this IHostApplicationBuilder builder) =>
        builder.AddBroadcastRunner();
}
