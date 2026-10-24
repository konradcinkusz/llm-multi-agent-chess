using ChessApi.Agents;
using Chess.Engine;

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
    public GameStatus GetStatus() => _board.GetStatus();

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
    /// <param name="onPromptLogged">Called with (ply, color, model, personality, promptText, responseText, success)
    /// for each LLM prompt attempt made during this move.</param>
    public async Task<MoveResult?> MakeMoveAsync(
        Func<int, string, Task>? onRetry = null,
        Func<int, string, string, string, string, string?, bool, Task>? onPromptLogged = null,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (IsGameOver) return null;
            IsThinking = true;

            int ply = _history.Count + 1;
            string color = ply % 2 == 1 ? "White" : "Black";
            var agent = _board.SideToMove == Side.White ? WhiteAgent : BlackAgent;

            Func<string, string?, bool, Task>? promptCallback = null;
            if (onPromptLogged != null)
            {
                promptCallback = (prompt, response, success) =>
                    onPromptLogged(ply, color, agent.Model, agent.Personality, prompt, response, success);
            }

            var (success, san) = await agent.GetMoveAsync(_board, _history, onRetry, promptCallback, ct);

            if (!success || san == null) return null;

            bool applied = _board.ApplyMoveSan(san);
            if (!applied) return null;

            int moveNumber = (ply + 1) / 2;
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
                IsCheck: status is GameStatus.Check or GameStatus.Checkmate,
                IsCheckmate: status == GameStatus.Checkmate,
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
