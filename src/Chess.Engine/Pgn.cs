using System.Text;

namespace Chess.Engine;

// PGN export: Seven Tag Roster, FEN/SetUp for non-standard starts, movetext wrapped at 80 columns.
// Pure: no I/O, no clock. Moves are replayed on a board, so only legal SAN can be written.
public static class Pgn
{
    private const int MaxLineLength = 80;
    private const string ResultTag = "Result";

    private static readonly string[] Results = { "1-0", "0-1", "1/2-1/2", "*" };

    private static readonly (string Name, string Placeholder)[] Roster =
    {
        ("Event", "?"), ("Site", "?"), ("Date", "????.??.??"),
        ("Round", "?"), ("White", "?"), ("Black", "?"),
    };

    private static readonly HashSet<string> ManagedTags = new(StringComparer.Ordinal)
    {
        "Event", "Site", "Date", "Round", "White", "Black", ResultTag, "SetUp", "FEN",
    };

    public static string Write(
        IReadOnlyDictionary<string, string> tags, string? startFen, IEnumerable<string> sanMoves, string result)
    {
        if (Array.IndexOf(Results, result) < 0)
            throw new ArgumentException($"'{result}' is not a PGN result.", nameof(result));

        var board = string.IsNullOrWhiteSpace(startFen) ? ChessBoard.StartingPosition() : LoadBoard(startFen);
        var startSetup = board.ToFen();
        var isStandardStart = Setup(startSetup) == Setup(ChessBoard.StartingPosition().ToFen());
        var movetext = Movetext(board, sanMoves, result);

        var sb = new StringBuilder();
        foreach (var (name, placeholder) in Roster)
            AppendTag(sb, name, Tag(tags, name, placeholder));
        AppendTag(sb, ResultTag, result);
        if (!isStandardStart)
        {
            AppendTag(sb, "SetUp", "1");
            AppendTag(sb, "FEN", startSetup);
        }
        foreach (var (name, value) in tags.Where(t => !ManagedTags.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal))
            AppendTag(sb, name, value);

        sb.Append('\n');
        foreach (var line in Wrap(movetext))
            sb.Append(line).Append('\n');
        return sb.ToString();
    }

    private static ChessBoard LoadBoard(string fen)
    {
        var board = new ChessBoard();
        board.LoadFen(fen);
        return board;
    }

    // Placement, side, castling and en passant: the fields that decide whether this is the standard start.
    private static string Setup(string fen) => string.Join(' ', fen.Split(' ').Take(4));

    private static string Tag(IReadOnlyDictionary<string, string> tags, string name, string placeholder)
        => tags.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : placeholder;

    private static void AppendTag(StringBuilder sb, string name, string value)
    {
        if (name.Length == 0 || name.Any(c => char.IsWhiteSpace(c) || c is '"' or '[' or ']'))
            throw new ArgumentException($"'{name}' is not a valid PGN tag name.", "tags");

        var escaped = value.ReplaceLineEndings(" ").Replace("\\", "\\\\").Replace("\"", "\\\"");
        sb.Append('[').Append(name).Append(" \"").Append(escaped).Append("\"]\n");
    }

    // One token per move, with its move number attached, so wrapping never separates a number from its move.
    private static List<string> Movetext(ChessBoard board, IEnumerable<string> sanMoves, string result)
    {
        var tokens = new List<string>();
        foreach (var san in sanMoves)
        {
            var number = board.FullMoveNumber;
            var prefix = board.SideToMove == Side.White ? $"{number}. "
                : tokens.Count == 0 ? $"{number}... "
                : string.Empty;

            if (!board.GetLegalMoves().Any(m => m.San == san))
                throw new ArgumentException($"'{san}' is not a legal move in this position.", nameof(sanMoves));
            board.ApplyMoveSan(san);
            tokens.Add(prefix + san);
        }
        tokens.Add(result);
        return tokens;
    }

    private static IEnumerable<string> Wrap(IEnumerable<string> tokens)
    {
        var line = new StringBuilder();
        foreach (var token in tokens)
        {
            if (line.Length > 0 && line.Length + 1 + token.Length > MaxLineLength)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0)
                line.Append(' ');
            line.Append(token);
        }
        if (line.Length > 0)
            yield return line.ToString();
    }
}
