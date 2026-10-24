namespace Chess.Api.Persistence.Entities;

/// <summary>One row of <c>moves</c>; the key is (game, ply), which makes ply unique per game.</summary>
public sealed class MoveEntity
{
    public Guid GameId { get; set; }
    public int Ply { get; set; }
    public string Color { get; set; } = "";
    public string San { get; set; } = "";
    public string FenAfter { get; set; } = "";
    public int Attempts { get; set; }
    public long LatencyMs { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public int? CentipawnLoss { get; set; }
    public string? BestSan { get; set; }
    public DateTimeOffset PlayedAt { get; set; }
}
