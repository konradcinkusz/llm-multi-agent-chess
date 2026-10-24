namespace Chess.Api.Broadcast;

// T5.2 owns this file and registers the BroadcastRunner hosted service and IBroadcastController.
public static class BroadcastRunnerExtensions
{
    public static IHostApplicationBuilder AddBroadcastRunner(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IBroadcastController, BroadcastRunnerController>();
        return builder;
    }
}
