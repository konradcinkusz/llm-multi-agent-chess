using ChessApi.Sessions;

namespace ChessApi.DTOs;

// ── Requests ─────────────────────────────────────────────────────────────────

public record CreateGameRequest(
    string WhiteAgentType,
    string WhiteModel,
    string WhitePersonality,
    string BlackAgentType,
    string BlackModel,
    string BlackPersonality,
    string? PlayerName = null);

// ── Responses ─────────────────────────────────────────────────────────────────

public record AgentInfo(string AgentType, string Model, string Personality);

public record MoveHistoryItem(int MoveNumber, string Color, string San,
    float? Evaluation = null, bool IsCheck = false, bool IsCheckmate = false);

public record AnalysisDto(
    int MaterialWhite, int MaterialBlack, int MaterialAdvantage,
    int ControlWhite, int ControlBlack, int ControlAdvantage,
    float KingSafetyWhite, float KingSafetyBlack,
    int DevelopmentWhite, int DevelopmentBlack,
    float Evaluation);

public record GameResponse(
    Guid Id,
    string Status,
    string CurrentFen,
    int MoveCount,
    string CurrentTurn,
    bool IsGameOver,
    bool IsThinking,
    AgentInfo White,
    AgentInfo Black,
    List<MoveHistoryItem> History,
    AnalysisDto? Analysis,
    DateTime CreatedAt);

public record GameSummary(
    Guid Id, string Status, int MoveCount,
    string WhiteAgentType, string BlackAgentType,
    DateTime CreatedAt);

public record MoveResultDto(
    string San, string Fen,
    int MoveNumber, string Color,
    string StatusText, bool IsGameOver,
    bool IsCheck, bool IsCheckmate,
    float Evaluation, int MaterialAdvantage,
    AnalysisDto Analysis);

// ── Admin DTOs ────────────────────────────────────────────────────────────────

public record AdminGameSummary(
    Guid Id,
    string? PlayerName,
    string Status,
    int MoveCount,
    string WhiteAgentType,
    string WhiteModel,
    string BlackAgentType,
    string BlackModel,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record AdminMoveItem(
    int Ply,
    int MoveNumber,
    string Color,
    string San,
    float Evaluation,
    int MaterialAdvantage,
    bool IsCheck,
    bool IsCheckmate,
    DateTime PlayedAt);

public record AdminGameDetail(
    Guid Id,
    string? PlayerName,
    string Status,
    string CurrentFen,
    int MoveCount,
    string WhiteAgentType,
    string WhiteModel,
    string WhitePersonality,
    string BlackAgentType,
    string BlackModel,
    string BlackPersonality,
    List<AdminMoveItem> Moves,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record AdminPromptLogItem(
    int Id,
    Guid GameId,
    string? PlayerName,
    int Ply,
    string Color,
    string Model,
    string Personality,
    string PromptText,
    string? ResponseText,
    bool Success,
    DateTime CreatedAt);

public record AdminStats(
    int TotalGames,
    int TotalPrompts,
    int TotalMoves,
    int ActiveGames,
    int CompletedGames,
    List<string> PlayerNames);

// ── Mappings ──────────────────────────────────────────────────────────────────

public static class DtoMapper
{
    public static AnalysisDto ToDto(PositionAnalysis a) => new(
        a.MaterialWhite, a.MaterialBlack, a.MaterialWhite - a.MaterialBlack,
        a.ControlWhite, a.ControlBlack, a.ControlWhite - a.ControlBlack,
        a.KingSafetyWhite, a.KingSafetyBlack,
        a.DevelopmentWhite, a.DevelopmentBlack,
        a.Evaluation);

    public static MoveResultDto ToDto(MoveResult r) => new(
        r.San, r.Fen, r.MoveNumber, r.Color,
        r.StatusText, r.IsGameOver, r.IsCheck, r.IsCheckmate,
        r.Evaluation, r.MaterialAdvantage,
        ToDto(r.Analysis));
}
