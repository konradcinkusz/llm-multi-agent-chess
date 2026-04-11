using ChessApi.Agents;
using ChessApi.Chess;

namespace ChessApi.Sessions;

public class ChessGameSession
{
    private readonly ChessBoard _board;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<(int MoveNumber, string Color, string San)> _history = new();

    public Guid Id { get; }
    public IChessAgent WhiteAgent { get; }
    public IChessAgent BlackAgent { get; }

    public bool IsThinking { get; private set; }
    public bool IsGameOver => _board.IsGameOver();
    public string CurrentTurn => _board.SideToMove == Side.White ? "White" : "Black";
    public int MoveCount => _history.Count;

    public ChessGameSession(Guid id, IChessAgent white, IChessAgent black)
    {
        Id = id;
        WhiteAgent = white;
        BlackAgent = black;
        _board = ChessBoard.StartingPosition();
    }

    public IReadOnlyList<(int MoveNumber, string Color, string San)> History => _history;

    public string GetFen() => _board.ToFen();
    public string GetStatusText() => _board.GetStatusText();
    public string GetBoardSvg() => _board.ToSvg();
    public Chess.GameStatus GetStatus() => _board.GetStatus();

    public PositionAnalysis GetAnalysis()
    {
        var (wm, bm) = _board.MaterialCounts();
        var (wc, bc) = _board.SquareControl();
        var (wk, bk) = _board.KingSafety();
        var (wd, bd) = _board.Development();
        float eval = _board.SimpleEval();
        return new PositionAnalysis(wm, bm, wc, bc, wk, bk, wd, bd, eval);
    }

    /// <summary>
    /// Executes the next move. Calls the appropriate agent and updates board state.
    /// The SemaphoreSlim prevents concurrent moves during auto-play.
    /// </summary>
    public async Task<MoveResult?> MakeMoveAsync(
        Func<int, string, Task>? onRetry = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsGameOver) return null;
            IsThinking = true;

            var agent = _board.SideToMove == Side.White ? WhiteAgent : BlackAgent;
            var (success, san) = await agent.GetMoveAsync(_board, _history, onRetry, ct);

            if (!success || san == null) return null;

            bool applied = _board.ApplyMoveSan(san);
            if (!applied) return null;

            int ply = _history.Count + 1;
            int moveNumber = (ply + 1) / 2;
            string color = ply % 2 == 1 ? "White" : "Black";
            _history.Add((moveNumber, color, san));

            var analysis = GetAnalysis();
            var status = _board.GetStatus();

            return new MoveResult(
                San: san,
                Fen: _board.ToFen(),
                MoveNumber: moveNumber,
                Color: color,
                Ply: ply,
                StatusText: _board.GetStatusText(),
                IsGameOver: _board.IsGameOver(),
                IsCheck: status is Chess.GameStatus.Check or Chess.GameStatus.Checkmate,
                IsCheckmate: status == Chess.GameStatus.Checkmate,
                Evaluation: analysis.Evaluation,
                MaterialAdvantage: analysis.MaterialWhite - analysis.MaterialBlack,
                Analysis: analysis
            );
        }
        finally
        {
            IsThinking = false;
            _lock.Release();
        }
    }
}

public record PositionAnalysis(
    int MaterialWhite, int MaterialBlack,
    int ControlWhite, int ControlBlack,
    float KingSafetyWhite, float KingSafetyBlack,
    int DevelopmentWhite, int DevelopmentBlack,
    float Evaluation);

public record MoveResult(
    string San, string Fen,
    int MoveNumber, string Color, int Ply,
    string StatusText, bool IsGameOver,
    bool IsCheck, bool IsCheckmate,
    float Evaluation, int MaterialAdvantage,
    PositionAnalysis Analysis);
