using Chess.Engine;

namespace Chess.Analysis;

/// <summary>
/// Move quality from a Stockfish binary over UCI, per docs/RATING.md §4: fixed depth, centipawn loss
/// capped at 1000, mate scores ±1000. Owns one engine process.
/// <para>
/// A binary that cannot start, does not answer the UCI handshake or hangs in a search makes the calls below throw
/// (<see cref="TimeoutException"/> for a hang) and <see cref="IsAvailable"/> false for a while; callers handle the exception
/// and carry on without Stockfish (see <see cref="UciTimeouts"/>).
/// </para>
/// </summary>
public sealed class StockfishAnalyzer : IMoveQualityAnalyzer, IAsyncDisposable
{
    private const int TerminalMateScore = -CentipawnMath.MateScore;

    private readonly IUciEngine _engine;
    private readonly int _depth;

    public StockfishAnalyzer(string binaryPath, int depth, UciTimeouts? timeouts = null)
        : this(new ProcessUciEngine(binaryPath, timeouts), depth)
    {
    }

    internal StockfishAnalyzer(IUciEngine engine, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 1);
        _engine = engine;
        _depth = depth;
    }

    /// <summary>
    /// True until the engine fails (it cannot start, does not answer the handshake, exits or hangs in a search); false for
    /// <see cref="UciTimeouts.RetryAfter"/> after that, then the next call tries a fresh process. It does not start the process:
    /// use <see cref="ProbeAsync"/> to find out whether the binary works.
    /// </summary>
    public bool IsAvailable => _engine.IsAvailable;

    /// <summary>Starts the process if needed and completes the UCI handshake. Throws when the binary does not work.</summary>
    public Task ProbeAsync(CancellationToken ct) => _engine.EnsureReadyAsync(ct);

    public async Task<IReadOnlyList<MoveQuality>> AnalyzeAsync(string startFen, IReadOnlyList<string> sanMoves, CancellationToken ct)
    {
        if (sanMoves.Count == 0)
        {
            return [];
        }

        var board = new ChessBoard();
        board.LoadFen(startFen);
        var uciMoves = new List<string>(sanMoves.Count);

        // scores[k] and bestSans[k] describe the position before move k+1; scores[^1] is the position after the last move.
        var scores = new List<int>(sanMoves.Count + 1);
        var bestSans = new List<string>(sanMoves.Count);
        for (var k = 0; k < sanMoves.Count; k++)
        {
            var search = await SearchAsync(board, startFen, uciMoves, ct);
            scores.Add(search.Score);
            var best = search.BestMove ?? throw new InvalidOperationException($"Stockfish gave no best move before ply {k + 1}.");
            bestSans.Add(SanOf(board, best));

            if (board.FindBySan(sanMoves[k]) is not { } played)
            {
                throw new ArgumentException($"Illegal move '{sanMoves[k]}' at ply {k + 1}.", nameof(sanMoves));
            }

            uciMoves.Add(MoveNotation.ToUci(played));
            board.ApplyMoveSan(sanMoves[k]);
        }

        scores.Add((await SearchAsync(board, startFen, uciMoves, ct)).Score);

        var quality = new List<MoveQuality>(sanMoves.Count);
        for (var k = 0; k < sanMoves.Count; k++)
        {
            // Best value before the move versus the played move's value after it, from the mover's side.
            // After the move the opponent is to move, so its score is negated.
            quality.Add(new MoveQuality(k + 1, CentipawnMath.Loss(scores[k], -scores[k + 1]), bestSans[k]));
        }

        return quality;
    }

    /// <summary>Centipawns from White's point of view (mate = ±1000); null when unavailable.</summary>
    public async Task<int?> EvaluateAsync(string fen, CancellationToken ct)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        var search = await SearchAsync(board, fen, [], ct);
        return board.SideToMove == Side.White ? search.Score : -search.Score;
    }

    public ValueTask DisposeAsync() => _engine.DisposeAsync();

    /// <summary>
    /// A position the rules decide is not sent to the engine: a mate scores -1000 for the side to move, and every drawn
    /// position (stalemate, threefold repetition, fifty moves, insufficient material) scores 0. Only a position that is
    /// still in play is searched.
    /// </summary>
    private async Task<UciSearch> SearchAsync(ChessBoard board, string startFen, IReadOnlyList<string> uciMoves, CancellationToken ct)
        => board.GetStatus() switch
        {
            GameStatus.Checkmate => new UciSearch(TerminalMateScore, null),
            GameStatus.Stalemate or GameStatus.DrawRepetition or GameStatus.DrawFiftyMoves or GameStatus.DrawInsufficientMaterial
                => new UciSearch(0, null),
            _ => await _engine.SearchAsync(startFen, uciMoves, _depth, ct)
        };

    private static string SanOf(ChessBoard board, string uci)
    {
        foreach (var move in board.GetLegalMoves())
        {
            if (MoveNotation.ToUci(move) == uci)
            {
                return move.San;
            }
        }

        throw new InvalidOperationException($"Stockfish suggested '{uci}', which is not legal in this position.");
    }
}
