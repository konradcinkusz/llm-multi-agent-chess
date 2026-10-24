using Chess.Engine;
using Xunit;

namespace Chess.Engine.Tests;

// Perft: count leaf nodes of the legal-move tree to a fixed depth.
// Reference numbers: Chess Programming Wiki, "Perft Results".
public class PerftTests
{
    private const string Initial = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string Kiwipete = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
    private const string Position3 = "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1";
    private const string Position4 = "r2q1rk1/pP1p2pp/Q4n2/bbp1p3/Np6/1B3NBn/pPPP1PPP/R3K2R b KQ - 0 1";
    private const string Position5 = "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8";
    private const string Position6 = "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10";

    [Theory]
    [InlineData(Initial, 1, 20)]
    [InlineData(Initial, 2, 400)]
    [InlineData(Initial, 3, 8902)]
    [InlineData(Initial, 4, 197281)]
    [InlineData(Kiwipete, 1, 48)]
    [InlineData(Kiwipete, 2, 2039)]
    [InlineData(Kiwipete, 3, 97862)]
    [InlineData(Position3, 1, 14)]
    [InlineData(Position3, 2, 191)]
    [InlineData(Position3, 3, 2812)]
    [InlineData(Position3, 4, 43238)]
    [InlineData(Position4, 1, 6)]
    [InlineData(Position4, 2, 264)]
    [InlineData(Position4, 3, 9467)]
    [InlineData(Position5, 1, 44)]
    [InlineData(Position5, 2, 1486)]
    [InlineData(Position5, 3, 62379)]
    [InlineData(Position6, 1, 46)]
    [InlineData(Position6, 2, 2079)]
    [InlineData(Position6, 3, 89890)]
    public void PerftMatchesPublishedCount(string fen, int depth, long expected)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);

        Assert.Equal(expected, Perft(board, depth));
    }

    // Each child is rebuilt from its FEN: ChessBoard exposes no undo, and FEN round-trip is
    // itself under test in FenTests.
    private static long Perft(ChessBoard board, int depth)
    {
        var moves = board.GetLegalMoves();
        if (depth == 1) return moves.Count;

        long nodes = 0;
        foreach (var move in moves)
        {
            var child = new ChessBoard();
            child.LoadFen(board.ToFen());
            child.ApplyMoveSan(move.San);
            nodes += Perft(child, depth - 1);
        }
        return nodes;
    }
}
