namespace Chess.Api.Persistence.Entities;

/// <summary>
/// One row of <c>rating_snapshots</c>: history only. It is written after each finished rated game
/// and never read back into a rating calculation (03 §4).
/// </summary>
public sealed class RatingSnapshotEntity
{
    public long Id { get; set; }
    public DateTimeOffset TakenAt { get; set; }
    public Guid AfterGameId { get; set; }
    public int RatedGames { get; set; }
    public string Model { get; set; } = "";
    public double Rating { get; set; }
    public double CiLow { get; set; }
    public double CiHigh { get; set; }
    public double ArenaScore { get; set; }
    public int Games { get; set; }
    public bool Provisional { get; set; }
    public string FormulaVersion { get; set; } = "";
}
