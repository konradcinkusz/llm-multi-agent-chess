using Chess.Engine;

namespace Chess.Api.Games;

/// <summary>How a game ended: its PGN result and its termination word (03 §3.3).</summary>
public sealed record GameEnd(string Result, string Termination);

/// <summary>
/// The rules that end a game (RATING.md §2): the engine's status, a forfeit, an abort and adjudication at
/// <c>MaxPlies</c>. Pure: no I/O and no clock.
/// </summary>
public static class GameTermination
{
    /// <summary>Centipawns for one side that decide an adjudicated game when Stockfish is available.</summary>
    public const int AdjudicationCentipawns = 300;

    /// <summary>Material points (pawn 1, minor 3, rook 5, queen 9) that decide an adjudicated game without Stockfish.</summary>
    public const int AdjudicationMaterialPoints = 5;

    private const string Draw = "1/2-1/2";

    /// <summary>The termination of a game that reached its budget (03 §15.2). Adjudicated like <c>MaxPlies</c>.</summary>
    public const string BudgetReached = "budget";

    public const string Adjudicated = "adjudicated";

    public static readonly GameEnd Aborted = new("*", "aborted");

    /// <summary>The end the engine reports for this position, or null when the game goes on.</summary>
    public static GameEnd? FromStatus(ChessBoard board) => board.GetStatus() switch
    {
        // The side to move is mated: the other side wins.
        GameStatus.Checkmate => new(board.SideToMove == Side.White ? "0-1" : "1-0", "checkmate"),
        GameStatus.Stalemate => new(Draw, "stalemate"),
        GameStatus.DrawInsufficientMaterial => new(Draw, "insufficient-material"),
        GameStatus.DrawFiftyMoves => new(Draw, "fifty-move"),
        GameStatus.DrawRepetition => new(Draw, "threefold"),
        _ => null,
    };

    /// <summary>The side that could not produce a legal move in time loses; the game is not replayed.</summary>
    public static GameEnd Forfeit(Side side) => side == Side.White
        ? new("0-1", "forfeit-white")
        : new("1-0", "forfeit-black");

    /// <summary>
    /// The verdict at <c>MaxPlies</c> (or at a budget, with its own <paramref name="termination"/>). <paramref name="whiteCentipawns"/>
    /// is Stockfish's evaluation from White's point of view, or null when there is none, in which case the material difference decides.
    /// </summary>
    public static GameEnd Adjudicate(ChessBoard board, int? whiteCentipawns, string termination = Adjudicated)
    {
        var lead = whiteCentipawns is { } centipawns
            ? Lead(centipawns, AdjudicationCentipawns)
            : Lead(MaterialDifference(board), AdjudicationMaterialPoints);

        return new(lead > 0 ? "1-0" : lead < 0 ? "0-1" : Draw, termination);
    }

    private static int Lead(int difference, int threshold) =>
        difference >= threshold ? 1 : difference <= -threshold ? -1 : 0;

    private static int MaterialDifference(ChessBoard board)
    {
        var (white, black) = board.MaterialCounts();
        return white - black;
    }
}
