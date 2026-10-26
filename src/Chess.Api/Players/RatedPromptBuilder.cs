using System.Globalization;
using Chess.Engine;

namespace Chess.Api.Players;

/// <summary>
/// The one prompt template of rated games (RATING.md §1). Pure: the same request always gives the same text.
/// The <c>Legal moves:</c> line is its own line, because <see cref="Chess.Llm.FakeLlmGateway"/> finds the moves there.
/// </summary>
public static class RatedPromptBuilder
{
    public const string SystemPrompt =
        "You are playing a game of chess against another language model. " +
        "Each reply is a single move in standard algebraic notation (SAN) and nothing else.";

    private const int RecentMoveCount = 10;

    public static string Build(MoveRequest request)
    {
        var (side, _) = ParseFen(request.Fen);
        return string.Join('\n',
            $"You are playing a game of chess. It is {SideName(side)} to move.",
            $"Position (FEN): {request.Fen}",
            $"Last moves: {FormatRecent(request.Fen, request.HistorySan)}",
            $"Legal moves: {string.Join(", ", request.LegalSan)}",
            "Answer with one move in SAN only.");
    }

    /// <summary>The follow-up sent after an illegal reply, or after no reply at all (<paramref name="reply"/> null).</summary>
    public static string Corrective(string? reply, IReadOnlyList<string> legalSan)
    {
        var rejected = reply is null ? "No move was received." : $"'{reply}' is not a legal move.";
        return string.Join('\n',
            rejected,
            $"Legal moves: {string.Join(", ", legalSan)}",
            "Answer with one move in SAN only.");
    }

    private static string FormatRecent(string fen, IReadOnlyList<string> history) =>
        FormatMoves(fen, history, RecentMoveCount);

    /// <summary>
    /// The last <paramref name="count"/> plies in PGN style, with move numbers. The move numbers are derived from the FEN by walking
    /// back from the current position, so the text is right for any start position, not only the standard one.
    /// </summary>
    internal static string FormatMoves(string fen, IReadOnlyList<string> history, int count)
    {
        var (side, fullMove) = ParseFen(fen);
        var recent = new List<(int Number, Side Mover, string San)>();

        // The last move was made by the side that is not to move. Before a black move the full-move
        // number is one lower than in the position after it; before a white move it is the same.
        for (var i = history.Count - 1; i >= 0 && recent.Count < count; i--)
        {
            var mover = Opposite(side);
            var number = mover == Side.Black ? fullMove - 1 : fullMove;
            recent.Add((number, mover, history[i]));
            side = mover;
            fullMove = number;
        }

        if (recent.Count == 0)
        {
            return "none";
        }

        recent.Reverse();
        var words = new List<string>(recent.Count + 2);
        for (var j = 0; j < recent.Count; j++)
        {
            var (number, mover, san) = recent[j];
            if (mover == Side.White)
            {
                words.Add($"{number}. {san}");
            }
            else if (j == 0)
            {
                words.Add($"{number}... {san}");
            }
            else
            {
                words.Add(san);
            }
        }

        return string.Join(' ', words);
    }

    private static (Side Side, int FullMove) ParseFen(string fen)
    {
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var side = fields[1] == "w" ? Side.White : Side.Black;
        return (side, int.Parse(fields[5], CultureInfo.InvariantCulture));
    }

    private static Side Opposite(Side side) => side == Side.White ? Side.Black : Side.White;

    private static string SideName(Side side) => side == Side.White ? "White" : "Black";
}
