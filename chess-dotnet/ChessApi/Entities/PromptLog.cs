namespace ChessApi.Entities;

public class PromptLog
{
    public int Id { get; set; }
    public Guid GameId { get; set; }
    public Game Game { get; set; } = null!;
    public int Ply { get; set; }
    public string Color { get; set; } = "";
    public string Model { get; set; } = "";
    public string Personality { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string? ResponseText { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedAt { get; set; }
}
