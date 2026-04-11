using ChessApi.Chess;
using ChessApi.Config;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace ChessApi.Agents;

public class LlmChessAgent : IChessAgent
{
    private readonly ChatClient _chatClient;
    private readonly AppConfig _config;

    public string Name { get; }
    public string AgentType => "llm";
    public string Model { get; }
    public string Personality { get; }

    public LlmChessAgent(string name, string model, string personality,
        ChatClient chatClient, IOptions<AppConfig> config)
    {
        Name = name;
        Model = model;
        Personality = personality;
        _chatClient = chatClient;
        _config = config.Value;
    }

    public async Task<(bool Success, string? San)> GetMoveAsync(
        ChessBoard board,
        IReadOnlyList<(int MoveNumber, string Color, string San)> history,
        Func<int, string, Task>? onRetry = null,
        Func<string, string?, bool, Task>? onPrompt = null,
        CancellationToken ct = default)
    {
        var legalMoves = board.GetLegalMoves().Select(m => m.San).ToList();
        if (legalMoves.Count == 0) return (false, null);

        string prompt = BuildPrompt(board, history, legalMoves);

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var completion = await _chatClient.CompleteChatAsync(
                    [new SystemChatMessage(prompt)],
                    new ChatCompletionOptions
                    {
                        Temperature = _config.Temperature,
                        MaxOutputTokenCount = 15
                    },
                    ct);

                string san = completion.Value.Content[0].Text.Trim();
                bool valid = legalMoves.Contains(san);

                if (onPrompt != null)
                    await onPrompt(prompt, san, valid);

                if (valid) return (true, san);

                if (onRetry != null)
                    await onRetry(attempt, $"'{san}' is not a legal move");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                if (onPrompt != null)
                    await onPrompt(prompt, null, false);

                if (attempt == _config.MaxRetries) break;
                if (onRetry != null) await onRetry(attempt, ex.Message);
            }
        }

        // Fallback to first legal move (mirrors Python behaviour)
        return (true, legalMoves[0]);
    }

    private string BuildPrompt(
        ChessBoard board,
        IReadOnlyList<(int MoveNumber, string Color, string San)> history,
        List<string> legalMoves)
    {
        string style = Personality switch
        {
            "aggressive" => "most attacking and aggressive",
            "defensive" => "safest and most defensive",
            _ => "best positional"
        };

        string sideLabel = board.SideToMove == Side.White ? "White" : "Black";
        var (wMat, bMat) = board.MaterialCounts();
        int advantage = wMat - bMat;
        string matText = advantage == 0 ? "Material is equal."
            : advantage > 0 ? $"White is up {advantage} points of material."
            : $"Black is up {-advantage} points of material.";

        var recentMoves = history.TakeLast(_config.MoveHistoryLimit)
            .Select(h => $"Move {h.MoveNumber}: {h.Color} played {h.San}");

        string movesStr = string.Join(", ", legalMoves);
        string historyStr = history.Count > 0
            ? $"\nRecent moves:\n{string.Join("\n", recentMoves)}" : "";

        return $"""
            You are a chess engine playing as {sideLabel}.
            Current position (FEN): {board.ToFen()}
            {matText}{historyStr}

            Legal moves: {movesStr}

            Choose the {style} move from the list of legal moves above.
            Respond with ONLY the move in Standard Algebraic Notation (SAN), exactly as it appears in the legal moves list.
            Do not include any explanation or punctuation.
            """;
    }
}
