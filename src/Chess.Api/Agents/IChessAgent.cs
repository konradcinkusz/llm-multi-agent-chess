using Chess.Engine;

namespace ChessApi.Agents;

public interface IChessAgent
{
    string Name { get; }       // "White" | "Black"
    string AgentType { get; }  // "llm" | "random"
    string Model { get; }
    string Personality { get; }

    /// <param name="onPrompt">Called after each LLM attempt with (promptText, responseText, success).
    /// Not invoked for random agents.</param>
    Task<(bool Success, string? San)> GetMoveAsync(
        ChessBoard board,
        IReadOnlyList<(int MoveNumber, string Color, string San)> history,
        Func<int, string, Task>? onRetry = null,
        Func<string, string?, bool, Task>? onPrompt = null,
        CancellationToken ct = default);
}
