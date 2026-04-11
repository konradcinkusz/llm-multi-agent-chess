using ChessApi.Chess;

namespace ChessApi.Agents;

public class RandomChessAgent(string name) : IChessAgent
{
    public string Name { get; } = name;
    public string AgentType => "random";
    public string Model => "random";
    public string Personality => "random";

    public Task<(bool Success, string? San)> GetMoveAsync(
        ChessBoard board,
        IReadOnlyList<(int MoveNumber, string Color, string San)> history,
        Func<int, string, Task>? onRetry = null,
        CancellationToken ct = default)
    {
        var moves = board.GetLegalMoves();
        if (moves.Count == 0) return Task.FromResult<(bool, string?)>((false, null));
        var move = moves[Random.Shared.Next(moves.Count)];
        return Task.FromResult<(bool, string?)>((true, move.San));
    }
}
