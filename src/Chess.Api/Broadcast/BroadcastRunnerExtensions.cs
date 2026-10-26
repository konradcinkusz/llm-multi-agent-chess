namespace Chess.Api.Broadcast;

public static class BroadcastRunnerExtensions
{
    /// <summary>
    /// Registers the broadcast runner (its background worker), the controller that T5.3 calls, the in-process signals
    /// they share, and the paid requests it plays (03 §15). Needs the persistence, players, game runner and request
    /// registrations, which are added first.
    /// </summary>
    public static IHostApplicationBuilder AddBroadcastRunner(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BroadcastRequests>();
        builder.Services.AddSingleton<BroadcastRunnerSignals>();
        builder.Services.AddSingleton<IBroadcastController, BroadcastRunnerController>();
        builder.Services.AddHostedService<BroadcastRunner>();
        return builder;
    }
}
