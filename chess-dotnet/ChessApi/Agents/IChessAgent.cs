using ChessApi.Chess;

namespace ChessApi.Agents;

public interface IChessAgent
{
    string Name { get; }       // "White" | "Black"
    string AgentType { get; }  // "llm" | "random"
    string Model { get; }
    string Personality { get; }

    Task<(bool Success, string? San)> GetMoveAsync(
        ChessBoard board,
        IReadOnlyList<(int MoveNumber, string Color, string San)> history,
        Func<int, string, Task>? onRetry = null,
        CancellationToken ct = default);
}
