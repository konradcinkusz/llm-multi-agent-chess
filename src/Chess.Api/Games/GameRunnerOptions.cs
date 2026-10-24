namespace Chess.Api.Games;

/// <summary>Configuration section <c>Arena</c>, the keys that govern how games are played (03 §7).</summary>
public sealed class GameRunnerOptions
{
    /// <summary>Games played at the same time. Every LLM move is a Copilot request, so this caps the cost.</summary>
    public int MaxConcurrentGames { get; set; } = 2;

    /// <summary>Plies after which a game is adjudicated (RATING.md §2).</summary>
    public int MaxPlies { get; set; } = 200;
}
