namespace Chess.Web.Components.Shared;

/// <summary>Reads the piece-placement field of a FEN string for drawing. The other FEN fields are ignored.</summary>
public static class FenBoard
{
    private const string PieceLetters = "KQRBNPkqrbnp";
    private const string PieceSymbols = "♔♕♖♗♘♙♚♛♜♝♞♟";

    /// <summary>Maps each occupied square (e.g. "e1") to its FEN piece letter.</summary>
    /// <exception cref="FormatException">The placement field does not describe an 8×8 board.</exception>
    public static IReadOnlyDictionary<string, char> Parse(string fen)
    {
        var ranks = fen.Split(' ', 2)[0].Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException($"A FEN placement has 8 ranks: '{fen}'.");
        }

        var pieces = new Dictionary<string, char>();
        for (var row = 0; row < 8; row++)
        {
            var file = 0;
            foreach (var c in ranks[row])
            {
                if (c is >= '1' and <= '8')
                {
                    file += c - '0';
                    continue;
                }

                if (!PieceLetters.Contains(c) || file > 7)
                {
                    throw new FormatException($"Unexpected '{c}' in FEN placement '{fen}'.");
                }

                pieces[Square(file, 7 - row)] = c;
                file++;
            }

            if (file != 8)
            {
                throw new FormatException($"Rank {8 - row} of '{fen}' does not span 8 files.");
            }
        }

        return pieces;
    }

    /// <summary>The Unicode chess glyph for a FEN piece letter (white upper case, black lower case).</summary>
    public static string Symbol(char piece) => PieceSymbols[PieceLetters.IndexOf(piece)].ToString();

    /// <summary>File 0–7 is a–h; rank 0–7 is 1–8.</summary>
    public static string Square(int file, int rank) => $"{(char)('a' + file)}{rank + 1}";
}
