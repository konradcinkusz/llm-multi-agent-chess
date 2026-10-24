namespace Chess.Api.Players;

public static class PlayersExtensions
{
    /// <summary>Registers <see cref="IChessPlayerFactory"/> and the <c>Arena</c> move limits it reads.</summary>
    public static IHostApplicationBuilder AddPlayers(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<PlayerOptions>().BindConfiguration("Arena");
        builder.Services.AddSingleton<IChessPlayerFactory, ChessPlayerFactory>();
        return builder;
    }
}
