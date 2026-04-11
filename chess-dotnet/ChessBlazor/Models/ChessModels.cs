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
