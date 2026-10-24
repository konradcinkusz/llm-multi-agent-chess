namespace Chess.Api.Persistence.Entities;

/// <summary>One row of <c>games</c> (03 §4). Players are flattened into kind/model columns.</summary>
public sealed class GameEntity
{
    public Guid Id { get; set; }
    public Guid? TournamentId { get; set; }
    public string WhiteKind { get; set; } = "";
    public string WhiteModel { get; set; } = "";
    public string BlackKind { get; set; } = "";
    public string BlackModel { get; set; } = "";
    public bool Rated { get; set; }
    public string Result { get; set; } = "*";
    public string? Termination { get; set; }
    public string StartFen { get; set; } = "";
    public string CurrentFen { get; set; } = "";
    public int PlyCount { get; set; }
    public string? Pgn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
