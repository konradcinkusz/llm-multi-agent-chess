namespace Chess.Api.Players;

/// <summary>Configuration section <c>Arena</c>, the keys that govern how a player makes one move.</summary>
public sealed class PlayerOptions
{
    /// <summary>Illegal or failed replies allowed for one move before the side forfeits.</summary>
    public int MaxAttemptsPerMove { get; set; } = 3;

    /// <summary>How long one reply from an LLM may take.</summary>
    public int MoveTimeoutSeconds { get; set; } = 60;
}
