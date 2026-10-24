namespace Chess.Api.Results;

/// <summary>Bound from the <c>RatingSnapshots</c> configuration section.</summary>
public sealed class RatingSnapshotOptions
{
    /// <summary>How often the writer looks for finished rated games that have no snapshot yet.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
}
