namespace Chess.Api.Quality;

/// <summary>Bound from <c>MoveQuality</c>. The interval is how often finished games are checked for unscored moves (03 §7, T2.8).</summary>
public sealed class MoveQualityOptions
{
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);
}
