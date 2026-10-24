namespace ChessApi.Entities;

public class Game
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    // "Ongoing" | "Checkmate! White wins!" | "Checkmate! Black wins!" | etc.
    public string Status { get; set; } = "Ongoing";

    public string WhiteAgentType { get; set; } = "llm";
    public string WhiteModel { get; set; } = "gpt-4o-mini";
    public string WhitePersonality { get; set; } = "standard";

    public string BlackAgentType { get; set; } = "llm";
    public string BlackModel { get; set; } = "gpt-4o-mini";
    public string BlackPersonality { get; set; } = "standard";

    public string CurrentFen { get; set; } =
        "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    public int MoveCount { get; set; }

    public string? PlayerName { get; set; }

    public ICollection<MoveRecord> Moves { get; set; } = new List<MoveRecord>();
    public ICollection<PromptLog> PromptLogs { get; set; } = new List<PromptLog>();
}
