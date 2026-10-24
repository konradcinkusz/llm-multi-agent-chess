namespace Chess.Api.Games;

public static class GameRunnerExtensions
{
    /// <summary>
    /// Registers <see cref="IGameRunner"/>, its background worker, the in-memory event stream and the <c>Arena</c>
    /// game limits. Needs the persistence, players and analysis registrations, which are added first.
    /// </summary>
    public static IHostApplicationBuilder AddGameRunner(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<GameRunnerOptions>().BindConfiguration("Arena");
        builder.Services.AddSingleton<IGameEventStream, InMemoryGameEventStream>();
        builder.Services.AddSingleton<GameRunner>();
        builder.Services.AddSingleton<IGameRunner>(sp => sp.GetRequiredService<GameRunner>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<GameRunner>());
        return builder;
    }
}
