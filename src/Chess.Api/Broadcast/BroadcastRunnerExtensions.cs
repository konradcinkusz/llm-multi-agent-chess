namespace Chess.Api.Broadcast;

public static class BroadcastRunnerExtensions
{
    /// <summary>
    /// Registers the broadcast runner (its background worker), the controller that T5.3 calls, and the in-process signals
    /// they share. Needs the persistence, players and game runner registrations, which are added first.
    /// </summary>
    public static IHostApplicationBuilder AddBroadcastRunner(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BroadcastRunnerSignals>();
        builder.Services.AddSingleton<IBroadcastController, BroadcastRunnerController>();
        builder.Services.AddHostedService<BroadcastRunner>();
        return builder;
    }
}
