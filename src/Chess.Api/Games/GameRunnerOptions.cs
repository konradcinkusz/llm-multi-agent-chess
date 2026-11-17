namespace Chess.Api.Games;

/// <summary>Configuration section <c>Arena</c>, the keys that govern how games are played (03 §7).</summary>
public sealed class GameRunnerOptions
{
    /// <summary>Games played at the same time. Every LLM move is a Copilot request, so this caps the cost.</summary>
    public int MaxConcurrentGames { get; set; } = 2;

    /// <summary>Plies after which a game is adjudicated (RATING.md §2).</summary>
    public int MaxPlies { get; set; } = 200;

    /// <summary>
    /// How long tournament and benchmark dispatch waits after a game ended in a technical failure (the gateway errored, see
    /// <see cref="TechnicalFailureCooldown"/>). It doubles with each failure in a row, up to <see cref="MaxTechnicalCooldown"/>.
    /// </summary>
    public TimeSpan TechnicalCooldown { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>The longest pause <see cref="TechnicalCooldown"/> grows to.</summary>
    public TimeSpan MaxTechnicalCooldown { get; set; } = TimeSpan.FromMinutes(2);
}
