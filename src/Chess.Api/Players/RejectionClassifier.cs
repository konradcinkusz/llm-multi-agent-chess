using System.Text.RegularExpressions;
using Chess.Engine;

namespace Chess.Api.Players;

/// <summary>
/// Decides why a reply was not a legal move (03 §10.3). The player makes the decision, so every stored attempt and every
/// reason text uses the same rule. Only the reply and the position are read here; a timeout or a gateway failure has
/// no reply and is classified by the player that caught it.
/// </summary>
public static class RejectionClassifier
{
    public const string Unparseable = "unparseable";
    public const string Illegal = "illegal";
    public const string Ambiguous = "ambiguous";
    public const string Timeout = "timeout";
    public const string Error = "error";

    private const int QuotedLength = 80;

    // A reply that is one move as written: SAN (piece letter, disambiguation, capture, square, promotion), UCI or castling.
    private static readonly Regex MoveShaped = new(
        @"^(?:[KQRBN]?[a-h]?[1-8]?x?[a-h][1-8](?:=?[QRBN])?|[a-h][1-8][a-h][1-8][qrbn]?|O-O(?:-O)?|0-0(?:-0)?)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex LeadingMoveNumber = new(@"^\d+\s*\.+\s*", RegexOptions.CultureInvariant);
    private static readonly char[] Wrapping = ['"', '\'', '`', ' ', '\t', '\r', '\n'];
    private static readonly char[] Trailing = ['.', ',', ';', ':', '!', '?', '+', '#', '"', '\'', '`', ' ', '\t', '\r', '\n'];

    /// <summary>The kind and the reason text for a reply that <see cref="MoveNotation.TryResolve"/> did not accept.</summary>
    /// <param name="board">The position the reply was given for.</param>
    /// <param name="reply">The reply as the model wrote it.</param>
    public static Rejection Classify(ChessBoard board, string reply)
    {
        var candidate = Candidate(reply);
        if (candidate.Length == 0 || !MoveShaped.IsMatch(candidate))
        {
            return new Rejection(Unparseable,
                $"unparseable: \"{Quote(reply)}\" is not one move. Reply with exactly one legal move, such as \"Nf3\".");
        }

        // The same move written without its disambiguation and capture mark: "Rc7" when two rooks reach c7.
        var key = Simplify(candidate);
        var matches = board.GetLegalMoves()
            .Select(m => m.San)
            .Where(san => Simplify(san.TrimEnd('+', '#')) == key)
            .ToList();
        if (matches.Count > 1)
        {
            return new Rejection(Ambiguous,
                $"ambiguous: \"{candidate}\" matches {matches.Count} legal moves ({string.Join(", ", matches)}). "
                + "Name the one you mean with its file or rank, such as \"" + matches[0] + "\".");
        }

        return new Rejection(Illegal, $"illegal: \"{candidate}\" is not a legal move in this position.");
    }

    /// <summary>The reply with the wrapping, the move number and the trailing punctuation removed, as <see cref="MoveNotation"/> reads it.</summary>
    public static string Candidate(string reply)
    {
        var text = LeadingMoveNumber.Replace(reply.Trim(Wrapping), string.Empty).TrimEnd(Trailing);
        return text.StartsWith("0-0", StringComparison.Ordinal) ? text.Replace('0', 'O') : text;
    }

    /// <summary>
    /// Drops the check mark, the capture mark and, for a piece, the disambiguation: <c>Rac7</c>, <c>Rfc7</c> and <c>Rxc7+</c>
    /// all become <c>Rc7</c>. Pawn moves and castling keep their text, so <c>exd5</c> stays distinct from <c>e5</c>.
    /// </summary>
    private static string Simplify(string san)
    {
        var text = san.TrimEnd('+', '#').Replace("x", string.Empty, StringComparison.Ordinal);
        return text.Length >= 3 && "KQRBN".Contains(text[0])
            ? $"{text[0]}{text[^2..]}"
            : text;
    }

    private static string Quote(string reply)
    {
        var text = reply.Trim();
        return text.Length <= QuotedLength ? text : string.Concat(text.AsSpan(0, QuotedLength), "...");
    }

    /// <summary>A rejected reply: its kind (03 §10.3) and the reason text that names the kind and the candidate.</summary>
    public sealed record Rejection(string Kind, string Reason);
}
