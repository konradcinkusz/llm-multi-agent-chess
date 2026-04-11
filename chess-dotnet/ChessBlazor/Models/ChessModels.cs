namespace ChessBlazor.Models;

public class GameState
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "Ongoing";
    public string CurrentFen { get; set; } = "";
    public string CurrentTurn { get; set; } = "White";
    public int MoveCount { get; set; }
    public bool IsGameOver { get; set; }
    public bool IsThinking { get; set; }
    public AgentConfig White { get; set; } = new();
    public AgentConfig Black { get; set; } = new();
    public List<MoveHistoryItem> History { get; set; } = new();
    public AnalysisData? Analysis { get; set; }
    public string? LastError { get; set; }
}

public class AgentConfig
{
    public string AgentType { get; set; } = "llm";
    public string Model { get; set; } = "gpt-4o-mini";
    public string Personality { get; set; } = "standard";
}

public class MoveHistoryItem
{
    public int MoveNumber { get; set; }
    public string Color { get; set; } = "";
    public string San { get; set; } = "";
    public float? Evaluation { get; set; }
    public bool IsCheck { get; set; }
    public bool IsCheckmate { get; set; }
}

public class AnalysisData
{
    public int MaterialWhite { get; set; }
    public int MaterialBlack { get; set; }
    public int MaterialAdvantage { get; set; }
    public int ControlWhite { get; set; }
    public int ControlBlack { get; set; }
    public int ControlAdvantage { get; set; }
    public float KingSafetyWhite { get; set; }
    public float KingSafetyBlack { get; set; }
    public int DevelopmentWhite { get; set; }
    public int DevelopmentBlack { get; set; }
    public float Evaluation { get; set; }
}

public class MoveResultMessage
{
    public string San { get; set; } = "";
    public string Fen { get; set; } = "";
    public int MoveNumber { get; set; }
    public string Color { get; set; } = "";
    public string StatusText { get; set; } = "";
    public bool IsGameOver { get; set; }
    public bool IsCheck { get; set; }
    public bool IsCheckmate { get; set; }
    public float Evaluation { get; set; }
    public int MaterialAdvantage { get; set; }
    public AnalysisData Analysis { get; set; } = new();
}

public class GameResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public string CurrentFen { get; set; } = "";
    public int MoveCount { get; set; }
    public string CurrentTurn { get; set; } = "White";
    public bool IsGameOver { get; set; }
    public bool IsThinking { get; set; }
    public AgentConfig White { get; set; } = new();
    public AgentConfig Black { get; set; } = new();
    public List<MoveHistoryItem> History { get; set; } = new();
    public AnalysisData? Analysis { get; set; }
}

// ── Admin Models ──────────────────────────────────────────────────────────────

public class AdminStats
{
    public int TotalGames { get; set; }
    public int TotalPrompts { get; set; }
    public int TotalMoves { get; set; }
    public int ActiveGames { get; set; }
    public int CompletedGames { get; set; }
    public List<string> PlayerNames { get; set; } = new();
}

public class AdminGameSummary
{
    public Guid Id { get; set; }
    public string? PlayerName { get; set; }
    public string Status { get; set; } = "";
    public int MoveCount { get; set; }
    public string WhiteAgentType { get; set; } = "";
    public string WhiteModel { get; set; } = "";
    public string BlackAgentType { get; set; } = "";
    public string BlackModel { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AdminMoveItem
{
    public int Ply { get; set; }
    public int MoveNumber { get; set; }
    public string Color { get; set; } = "";
    public string San { get; set; } = "";
    public float Evaluation { get; set; }
    public int MaterialAdvantage { get; set; }
    public bool IsCheck { get; set; }
    public bool IsCheckmate { get; set; }
    public DateTime PlayedAt { get; set; }
}

public class AdminGameDetail
{
    public Guid Id { get; set; }
    public string? PlayerName { get; set; }
    public string Status { get; set; } = "";
    public string CurrentFen { get; set; } = "";
    public int MoveCount { get; set; }
    public string WhiteAgentType { get; set; } = "";
    public string WhiteModel { get; set; } = "";
    public string WhitePersonality { get; set; } = "";
    public string BlackAgentType { get; set; } = "";
    public string BlackModel { get; set; } = "";
    public string BlackPersonality { get; set; } = "";
    public List<AdminMoveItem> Moves { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AdminPromptLogItem
{
    public int Id { get; set; }
    public Guid GameId { get; set; }
    public string? PlayerName { get; set; }
    public int Ply { get; set; }
    public string Color { get; set; } = "";
    public string Model { get; set; } = "";
    public string Personality { get; set; } = "";
    public string PromptText { get; set; } = "";
    public string? ResponseText { get; set; }
    public bool Success { get; set; }
    public DateTime CreatedAt { get; set; }
}
