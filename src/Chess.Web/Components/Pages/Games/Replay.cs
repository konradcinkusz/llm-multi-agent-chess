using Chess.Web.Api;
using Chess.Web.Components.Shared;

namespace Chess.Web.Components.Pages.Games;

/// <summary>
/// Positions of a replay. Ply 0 is the start position; ply n is the position after the n-th move, which is
/// <see cref="MoveDto.FenAfter"/> of that move (03 §5.1).
/// </summary>
public static class Replay
{
    /// <summary>Keeps a ply inside the game: 0 is the start position and <paramref name="moveCount"/> the last one.</summary>
    public static int Clamp(int ply, int moveCount) => Math.Clamp(ply, 0, moveCount);

    public static string FenAt(GameDetailDto game, int ply) =>
        ply == 0 ? game.StartFen : game.Moves[ply - 1].FenAfter;

    /// <summary>The squares of the move that reached <paramref name="ply"/>; null at the start position.</summary>
    public static BoardMove? LastMove(GameDetailDto game, int ply) =>
        ply == 0 ? null : MoveSquares.Between(FenAt(game, ply - 1), FenAt(game, ply), game.Moves[ply - 1].Color);
}

/// <summary>
/// Works out a move's from and to squares by comparing the positions before and after it, so the move notation
/// never has to be parsed.
/// </summary>
public static class MoveSquares
{
    /// <param name="mover">The side that moved: "white" or "black" (03 §5.1 MoveDto.Color).</param>
    public static BoardMove? Between(string before, string after, string mover)
    {
        var previous = FenBoard.Parse(before);
        var current = FenBoard.Parse(after);

        // A move takes the mover's piece off one square and puts a mover's piece on another. Castling does both
        // twice, for the king and the rook; the king's pair is the one the notation names.
        var vacated = previous
            .Where(square => IsMover(square.Value, mover) && current.GetValueOrDefault(square.Key) != square.Value)
            .Select(square => square.Key)
            .ToList();
        var arrived = current
            .Where(square => IsMover(square.Value, mover) && previous.GetValueOrDefault(square.Key) != square.Value)
            .Select(square => square.Key)
            .ToList();

        if (vacated.Count == 1 && arrived.Count == 1)
        {
            return new BoardMove(vacated[0], arrived[0]);
        }

        if (vacated.Count == 2 && arrived.Count == 2)
        {
            var from = vacated.SingleOrDefault(square => char.ToUpperInvariant(previous[square]) == 'K');
            var to = arrived.SingleOrDefault(square => char.ToUpperInvariant(current[square]) == 'K');
            return from is null || to is null ? null : new BoardMove(from, to);
        }

        return null;
    }

    private static bool IsMover(char piece, string mover) =>
        mover == "white" ? char.IsUpper(piece) : char.IsLower(piece);
}
