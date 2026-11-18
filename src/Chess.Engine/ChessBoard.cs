// Chess engine: FEN, SAN, legal move generation, game status detection.
// Squares: index = rank*8 + file  (a1=0, h1=7, a2=8, ..., h8=63)

namespace Chess.Engine;

public enum PieceType : byte { None = 0, Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6 }
public enum Side { White = 0, Black = 1 }
public enum GameStatus { Ongoing, Check, Checkmate, Stalemate, DrawInsufficientMaterial, DrawFiftyMoves, DrawRepetition }

public readonly record struct Piece(PieceType Type, Side Color)
{
    public static readonly Piece Empty = new(PieceType.None, Side.White);
    public bool IsEmpty => Type == PieceType.None;

    public int Value => Type switch
    {
        PieceType.Pawn => 1, PieceType.Knight => 3, PieceType.Bishop => 3,
        PieceType.Rook => 5, PieceType.Queen => 9, _ => 0
    };

    public char FenChar => (Type, Color) switch
    {
        (PieceType.Pawn, Side.White) => 'P', (PieceType.Knight, Side.White) => 'N',
        (PieceType.Bishop, Side.White) => 'B', (PieceType.Rook, Side.White) => 'R',
        (PieceType.Queen, Side.White) => 'Q', (PieceType.King, Side.White) => 'K',
        (PieceType.Pawn, Side.Black) => 'p', (PieceType.Knight, Side.Black) => 'n',
        (PieceType.Bishop, Side.Black) => 'b', (PieceType.Rook, Side.Black) => 'r',
        (PieceType.Queen, Side.Black) => 'q', (PieceType.King, Side.Black) => 'k',
        _ => '.'
    };

    public static Piece FromFenChar(char c) => c switch
    {
        'P' => new(PieceType.Pawn, Side.White), 'N' => new(PieceType.Knight, Side.White),
        'B' => new(PieceType.Bishop, Side.White), 'R' => new(PieceType.Rook, Side.White),
        'Q' => new(PieceType.Queen, Side.White), 'K' => new(PieceType.King, Side.White),
        'p' => new(PieceType.Pawn, Side.Black), 'n' => new(PieceType.Knight, Side.Black),
        'b' => new(PieceType.Bishop, Side.Black), 'r' => new(PieceType.Rook, Side.Black),
        'q' => new(PieceType.Queen, Side.Black), 'k' => new(PieceType.King, Side.Black),
        _ => Empty
    };
}

public record struct ChessMove(int From, int To, PieceType Promotion = PieceType.None)
{
    public string San { get; internal set; } = "";
}

public class ChessBoard
{
    private readonly Piece[] _squares = new Piece[64];
    private Side _sideToMove = Side.White;
    private bool _wKingside = true, _wQueenside = true;
    private bool _bKingside = true, _bQueenside = true;
    private int _epSquare = -1;        // en passant target square, -1 if none
    private int _halfMoveClock = 0;
    private int _fullMoveNumber = 1;
    private readonly List<string> _positionHistory = new();

    public Side SideToMove => _sideToMove;
    public int HalfMoveClock => _halfMoveClock;
    public int FullMoveNumber => _fullMoveNumber;
    public Piece this[int square] => _squares[square];

    // ── Square helpers ────────────────────────────────────────────────────────
    public static int Rank(int sq) => sq / 8;
    public static int File(int sq) => sq % 8;
    public static int Square(int file, int rank) => rank * 8 + file;
    public static string ToAlgebraic(int sq) => $"{(char)('a' + File(sq))}{Rank(sq) + 1}";
    public static int FromAlgebraic(string s) => (s[1] - '1') * 8 + (s[0] - 'a');

    // ── Construction ─────────────────────────────────────────────────────────
    public static ChessBoard StartingPosition()
    {
        var b = new ChessBoard();
        b.LoadFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        return b;
    }

    /// <summary>
    /// Sets the position from a FEN string. The text must be a position that can occur in a game: six fields (the two
    /// clocks may be left out, as in an EPD-style record, and default to 0 and 1), eight ranks of eight squares, known
    /// piece letters, exactly one king per side, no pawn on the first or last rank, the side not to move not in check,
    /// a side of <c>w</c> or <c>b</c>, castling of <c>-</c> or letters from <c>KQkq</c>, an en passant square that is
    /// empty and on the third rank (Black to move) or sixth rank (White to move), a clock of 0 or more and a move
    /// number of 1 or more. Anything else throws a <see cref="FormatException"/> that names the first problem, and the
    /// board is left as it was. Use <see cref="TryLoadFen"/> to test a text without an exception.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid FEN position.</exception>
    public void LoadFen(string fen)
    {
        if (!TryLoadFen(fen, out var error))
            throw new FormatException(error);
    }

    /// <summary>Like <see cref="LoadFen"/>, but returns false with the reason in <paramref name="error"/> instead of throwing.</summary>
    public bool TryLoadFen(string? fen, out string? error)
    {
        error = ParseFen(fen, out var parsed);
        if (error is not null) return false;

        Array.Copy(parsed!.Squares, _squares, 64);
        _sideToMove = parsed.SideToMove;
        _wKingside = parsed.WKingside; _wQueenside = parsed.WQueenside;
        _bKingside = parsed.BKingside; _bQueenside = parsed.BQueenside;
        _epSquare = parsed.EpSquare;
        _halfMoveClock = parsed.HalfMoveClock;
        _fullMoveNumber = parsed.FullMoveNumber;

        _positionHistory.Clear();
        _positionHistory.Add(PositionKey());
        return true;
    }

    private sealed record ParsedFen(
        Piece[] Squares, Side SideToMove, bool WKingside, bool WQueenside, bool BKingside, bool BQueenside,
        int EpSquare, int HalfMoveClock, int FullMoveNumber);

    // Returns the first problem as text, or null with the position in `parsed`.
    private static string? ParseFen(string? fen, out ParsedFen? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(fen)) return "FEN is empty.";
        var parts = fen.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4 || parts.Length > 6)
            return $"FEN needs 4 to 6 space-separated fields (placement, side, castling, en passant, clocks), found {parts.Length}.";

        var squares = new Piece[64];
        Array.Fill(squares, Piece.Empty);
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8) return $"FEN placement needs 8 ranks separated by '/', found {ranks.Length}.";

        int whiteKings = 0, blackKings = 0;
        for (int i = 0; i < 8; i++)
        {
            int rank = 7 - i, file = 0;
            bool lastWasDigit = false;
            foreach (char c in ranks[i])
            {
                if (c is >= '1' and <= '8')
                {
                    if (lastWasDigit) return $"FEN rank {rank + 1} has two digits in a row.";
                    file += c - '0';
                    lastWasDigit = true;
                    if (file > 8) return $"FEN rank {rank + 1} has more than 8 squares.";
                    continue;
                }
                lastWasDigit = false;
                var piece = Piece.FromFenChar(c);
                if (piece.IsEmpty) return $"FEN rank {rank + 1} has an unknown character '{c}'.";
                if (file >= 8) return $"FEN rank {rank + 1} has more than 8 squares.";
                if (piece.Type == PieceType.Pawn && (rank == 0 || rank == 7)) return "FEN has a pawn on the first or last rank.";
                if (piece.Type == PieceType.King) { if (piece.Color == Side.White) whiteKings++; else blackKings++; }
                squares[rank * 8 + file++] = piece;
            }
            if (file != 8) return $"FEN rank {rank + 1} has {file} squares, not 8.";
        }
        if (whiteKings != 1 || blackKings != 1)
            return $"FEN needs exactly one king per side, found {whiteKings} white and {blackKings} black.";

        if (parts[1] is not ("w" or "b")) return $"FEN side to move must be 'w' or 'b', found '{parts[1]}'.";
        var side = parts[1] == "w" ? Side.White : Side.Black;

        string cr = parts[2];
        bool wk = false, wq = false, bk = false, bq = false;
        if (cr != "-")
        {
            foreach (char c in cr)
            {
                bool repeated;
                switch (c)
                {
                    case 'K': repeated = wk; wk = true; break;
                    case 'Q': repeated = wq; wq = true; break;
                    case 'k': repeated = bk; bk = true; break;
                    case 'q': repeated = bq; bq = true; break;
                    default: return $"FEN castling field '{cr}' may only hold K, Q, k, q or be '-'.";
                }
                if (repeated) return $"FEN castling field '{cr}' repeats '{c}'.";
            }
        }

        int ep = -1;
        if (parts[3] != "-")
        {
            var e = parts[3];
            if (e.Length != 2 || e[0] is < 'a' or > 'h' || e[1] is not ('3' or '6'))
                return $"FEN en passant field '{e}' must be '-' or a square on the 3rd or 6th rank.";
            ep = FromAlgebraic(e);
            if (Rank(ep) != (side == Side.White ? 5 : 2))
                return $"FEN en passant square '{e}' is on the wrong rank for {(side == Side.White ? "White" : "Black")} to move.";
            if (!squares[ep].IsEmpty) return $"FEN en passant square '{e}' is not empty.";
        }

        int half = 0, full = 1;
        if (parts.Length > 4 && (!int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out half)))
            return $"FEN halfmove clock '{parts[4]}' must be a number of 0 or more.";
        if (parts.Length > 5 && (!int.TryParse(parts[5], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out full) || full < 1))
            return $"FEN move number '{parts[5]}' must be a number of 1 or more.";

        var candidate = new ParsedFen(squares, side, wk, wq, bk, bq, ep, half, full);

        // The side that just moved cannot have left its king attacked: the position could not have arisen.
        var probe = new ChessBoard();
        Array.Copy(squares, probe._squares, 64);
        if (probe.IsInCheck(Opponent(side)))
            return $"FEN has the {(side == Side.White ? "black" : "white")} king in check with {(side == Side.White ? "White" : "Black")} to move.";

        parsed = candidate;
        return null;
    }

    public string ToFen()
    {
        var sb = new System.Text.StringBuilder();
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                var p = _squares[rank * 8 + file];
                if (p.IsEmpty) { empty++; }
                else { if (empty > 0) { sb.Append(empty); empty = 0; } sb.Append(p.FenChar); }
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0) sb.Append('/');
        }
        sb.Append(' ').Append(_sideToMove == Side.White ? 'w' : 'b');
        string cr = (_wKingside ? "K" : "") + (_wQueenside ? "Q" : "") + (_bKingside ? "k" : "") + (_bQueenside ? "q" : "");
        sb.Append(' ').Append(cr.Length > 0 ? cr : "-");
        sb.Append(' ').Append(_epSquare >= 0 ? ToAlgebraic(_epSquare) : "-");
        sb.Append(' ').Append(_halfMoveClock).Append(' ').Append(_fullMoveNumber);
        return sb.ToString();
    }

    // ── Legal moves ──────────────────────────────────────────────────────────
    public List<ChessMove> GetLegalMoves()
    {
        var pseudo = GeneratePseudo(_sideToMove);
        var legal = new List<ChessMove>(pseudo.Count);
        foreach (var m in pseudo)
        {
            var nb = Clone(); nb.ApplyInternal(m);
            if (!nb.IsInCheck(_sideToMove)) legal.Add(m);
        }
        // Add SAN using legal list for correct disambiguation
        return legal.Select(m => m with { San = BuildSan(m, legal) }).ToList();
    }

    public ChessMove? FindBySan(string san)
    {
        // FirstOrDefault would return a default (0→0) struct, not null, when nothing matches.
        foreach (var m in GetLegalMoves())
            if (m.San == san) return m;
        return null;
    }

    // ── Apply move ───────────────────────────────────────────────────────────
    public bool ApplyMoveSan(string san)
    {
        var move = FindBySan(san);
        if (move == null) return false;
        ApplyInternal(move.Value);
        _positionHistory.Add(PositionKey());
        return true;
    }

    private void ApplyInternal(ChessMove move)
    {
        var piece = _squares[move.From];
        var captured = _squares[move.To];
        int newEp = -1;

        if (piece.Type == PieceType.King)
        {
            int delta = move.To - move.From;
            if (delta == 2) // kingside castle
            {
                _squares[move.From + 1] = _squares[move.From + 3];
                _squares[move.From + 3] = Piece.Empty;
            }
            else if (delta == -2) // queenside castle
            {
                _squares[move.From - 1] = _squares[move.From - 4];
                _squares[move.From - 4] = Piece.Empty;
            }
            if (piece.Color == Side.White) { _wKingside = false; _wQueenside = false; }
            else { _bKingside = false; _bQueenside = false; }
        }

        if (piece.Type == PieceType.Rook)
        {
            if (move.From == 0) _wQueenside = false;
            else if (move.From == 7) _wKingside = false;
            else if (move.From == 56) _bQueenside = false;
            else if (move.From == 63) _bKingside = false;
        }
        // Rook captured on starting square loses castling rights
        if (!captured.IsEmpty && captured.Type == PieceType.Rook)
        {
            if (move.To == 0) _wQueenside = false;
            else if (move.To == 7) _wKingside = false;
            else if (move.To == 56) _bQueenside = false;
            else if (move.To == 63) _bKingside = false;
        }

        // En passant capture
        if (piece.Type == PieceType.Pawn && move.To == _epSquare)
            _squares[move.To + (piece.Color == Side.White ? -8 : 8)] = Piece.Empty;

        // Double pawn push → set en passant square
        if (piece.Type == PieceType.Pawn && Math.Abs(move.To - move.From) == 16)
            newEp = (move.From + move.To) / 2;

        _squares[move.To] = move.Promotion != PieceType.None
            ? new Piece(move.Promotion, piece.Color) : piece;
        _squares[move.From] = Piece.Empty;

        _epSquare = newEp;
        _halfMoveClock = (piece.Type == PieceType.Pawn || !captured.IsEmpty) ? 0 : _halfMoveClock + 1;
        if (_sideToMove == Side.Black) _fullMoveNumber++;
        _sideToMove = _sideToMove == Side.White ? Side.Black : Side.White;
    }

    // ── Pseudo-legal move generation ─────────────────────────────────────────
    private List<ChessMove> GeneratePseudo(Side side)
    {
        var moves = new List<ChessMove>(64);
        for (int sq = 0; sq < 64; sq++)
        {
            var p = _squares[sq];
            if (p.IsEmpty || p.Color != side) continue;
            switch (p.Type)
            {
                case PieceType.Pawn: PawnMoves(sq, side, moves); break;
                case PieceType.Knight: KnightMoves(sq, side, moves); break;
                case PieceType.Bishop: SlidingMoves(sq, side, moves, DiagDeltas); break;
                case PieceType.Rook: SlidingMoves(sq, side, moves, StraightDeltas); break;
                case PieceType.Queen: SlidingMoves(sq, side, moves, AllDeltas); break;
                case PieceType.King: KingMoves(sq, side, moves); break;
            }
        }
        return moves;
    }

    private static readonly int[] DiagDeltas = { 9, 7, -7, -9 };
    private static readonly int[] StraightDeltas = { 8, -8, 1, -1 };
    private static readonly int[] AllDeltas = { 9, 7, -7, -9, 8, -8, 1, -1 };

    private void PawnMoves(int sq, Side side, List<ChessMove> moves)
    {
        int dir = side == Side.White ? 8 : -8;
        int startRank = side == Side.White ? 1 : 6;
        int promoRank = side == Side.White ? 7 : 0;

        int fwd = sq + dir;
        if (fwd >= 0 && fwd < 64 && _squares[fwd].IsEmpty)
        {
            AddPawnMove(sq, fwd, promoRank, moves);
            if (Rank(sq) == startRank)
            {
                int dbl = sq + dir * 2;
                if (_squares[dbl].IsEmpty) moves.Add(new(sq, dbl));
            }
        }

        foreach (int df in new[] { -1, 1 })
        {
            int cf = File(sq) + df;
            if (cf < 0 || cf > 7) continue;
            int capSq = Rank(fwd) * 8 + cf;
            if (capSq < 0 || capSq >= 64) continue;
            bool ep = capSq == _epSquare;
            var target = _squares[capSq];
            if (ep || (!target.IsEmpty && target.Color != side))
                AddPawnMove(sq, capSq, promoRank, moves);
        }
    }

    private static void AddPawnMove(int from, int to, int promoRank, List<ChessMove> moves)
    {
        if (Rank(to) == promoRank)
            foreach (var pt in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
                moves.Add(new(from, to, pt));
        else
            moves.Add(new(from, to));
    }

    private void KnightMoves(int sq, Side side, List<ChessMove> moves)
    {
        int f = File(sq), r = Rank(sq);
        int[] df = { 1, 2, 2, 1, -1, -2, -2, -1 };
        int[] dr = { 2, 1, -1, -2, -2, -1, 1, 2 };
        for (int i = 0; i < 8; i++)
        {
            int nf = f + df[i], nr = r + dr[i];
            if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
            int dest = Square(nf, nr);
            if (_squares[dest].IsEmpty || _squares[dest].Color != side) moves.Add(new(sq, dest));
        }
    }

    private void SlidingMoves(int sq, Side side, List<ChessMove> moves, int[] deltas)
    {
        foreach (int delta in deltas)
        {
            int cur = sq;
            while (true)
            {
                int pf = File(cur), pr = Rank(cur);
                cur += delta;
                if (cur < 0 || cur >= 64) break;
                if (Math.Abs(File(cur) - pf) > 1 || Math.Abs(Rank(cur) - pr) > 1) break;
                var t = _squares[cur];
                if (t.IsEmpty) moves.Add(new(sq, cur));
                else { if (t.Color != side) moves.Add(new(sq, cur)); break; }
            }
        }
    }

    private void KingMoves(int sq, Side side, List<ChessMove> moves)
    {
        int f = File(sq), r = Rank(sq);
        for (int df = -1; df <= 1; df++)
        for (int dr = -1; dr <= 1; dr++)
        {
            if (df == 0 && dr == 0) continue;
            int nf = f + df, nr = r + dr;
            if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
            int dest = Square(nf, nr);
            if (_squares[dest].IsEmpty || _squares[dest].Color != side) moves.Add(new(sq, dest));
        }

        Side opp = Opponent(side);
        if (side == Side.White && sq == 4) // e1
        {
            if (_wKingside && _squares[5].IsEmpty && _squares[6].IsEmpty
                && !IsAttacked(4, opp) && !IsAttacked(5, opp) && !IsAttacked(6, opp)
                && _squares[7].Type == PieceType.Rook && _squares[7].Color == Side.White)
                moves.Add(new(4, 6));
            if (_wQueenside && _squares[3].IsEmpty && _squares[2].IsEmpty && _squares[1].IsEmpty
                && !IsAttacked(4, opp) && !IsAttacked(3, opp) && !IsAttacked(2, opp)
                && _squares[0].Type == PieceType.Rook && _squares[0].Color == Side.White)
                moves.Add(new(4, 2));
        }
        else if (side == Side.Black && sq == 60) // e8
        {
            if (_bKingside && _squares[61].IsEmpty && _squares[62].IsEmpty
                && !IsAttacked(60, opp) && !IsAttacked(61, opp) && !IsAttacked(62, opp)
                && _squares[63].Type == PieceType.Rook && _squares[63].Color == Side.Black)
                moves.Add(new(60, 62));
            if (_bQueenside && _squares[59].IsEmpty && _squares[58].IsEmpty && _squares[57].IsEmpty
                && !IsAttacked(60, opp) && !IsAttacked(59, opp) && !IsAttacked(58, opp)
                && _squares[56].Type == PieceType.Rook && _squares[56].Color == Side.Black)
                moves.Add(new(60, 58));
        }
    }

    // ── Attack detection ─────────────────────────────────────────────────────
    public bool IsInCheck(Side side)
    {
        int kingSq = -1;
        for (int i = 0; i < 64; i++)
            if (_squares[i].Type == PieceType.King && _squares[i].Color == side) { kingSq = i; break; }
        return kingSq >= 0 && IsAttacked(kingSq, Opponent(side));
    }

    public bool IsAttacked(int sq, Side byColor)
    {
        int f = File(sq), r = Rank(sq);

        // Pawn attacks
        int pawnDir = byColor == Side.White ? -8 : 8;
        foreach (int df in new[] { -1, 1 })
        {
            int psq = sq + pawnDir + df;
            if (psq < 0 || psq >= 64) continue;
            if (Math.Abs(File(psq) - f) != 1) continue;
            var p = _squares[psq];
            if (!p.IsEmpty && p.Color == byColor && p.Type == PieceType.Pawn) return true;
        }

        // Knight attacks
        int[] kdf = { 1, 2, 2, 1, -1, -2, -2, -1 };
        int[] kdr = { 2, 1, -1, -2, -2, -1, 1, 2 };
        for (int i = 0; i < 8; i++)
        {
            int nf = f + kdf[i], nr = r + kdr[i];
            if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
            var p = _squares[Square(nf, nr)];
            if (!p.IsEmpty && p.Color == byColor && p.Type == PieceType.Knight) return true;
        }

        // Diagonal (bishop/queen)
        foreach (int d in DiagDeltas)
        {
            int cur = sq;
            while (true)
            {
                int pf = File(cur), pr = Rank(cur); cur += d;
                if (cur < 0 || cur >= 64) break;
                if (Math.Abs(File(cur) - pf) > 1 || Math.Abs(Rank(cur) - pr) > 1) break;
                var p = _squares[cur];
                if (!p.IsEmpty)
                {
                    if (p.Color == byColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen)) return true;
                    break;
                }
            }
        }

        // Straight (rook/queen)
        foreach (int d in StraightDeltas)
        {
            int cur = sq;
            while (true)
            {
                int pf = File(cur), pr = Rank(cur); cur += d;
                if (cur < 0 || cur >= 64) break;
                if (Math.Abs(File(cur) - pf) > 1 || Math.Abs(Rank(cur) - pr) > 1) break;
                var p = _squares[cur];
                if (!p.IsEmpty)
                {
                    if (p.Color == byColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen)) return true;
                    break;
                }
            }
        }

        // King attacks
        for (int df = -1; df <= 1; df++)
        for (int dr = -1; dr <= 1; dr++)
        {
            if (df == 0 && dr == 0) continue;
            int nf = f + df, nr = r + dr;
            if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
            var p = _squares[Square(nf, nr)];
            if (!p.IsEmpty && p.Color == byColor && p.Type == PieceType.King) return true;
        }

        return false;
    }

    private bool HasAnyLegalMove(Side side)
    {
        foreach (var m in GeneratePseudo(side))
        {
            var nb = Clone(); nb.ApplyInternal(m);
            if (!nb.IsInCheck(side)) return true;
        }
        return false;
    }

    // ── SAN generation ───────────────────────────────────────────────────────
    private string BuildSan(ChessMove move, List<ChessMove> legalMoves)
    {
        var piece = _squares[move.From];

        // Castling
        if (piece.Type == PieceType.King)
        {
            int delta = move.To - move.From;
            if (delta == 2) return AddCheckSuffix("O-O", move);
            if (delta == -2) return AddCheckSuffix("O-O-O", move);
        }

        var sb = new System.Text.StringBuilder();
        if (piece.Type != PieceType.Pawn)
            sb.Append(piece.Type switch
            {
                PieceType.Knight => 'N', PieceType.Bishop => 'B',
                PieceType.Rook => 'R', PieceType.Queen => 'Q', _ => 'K'
            });

        // Disambiguation (only for non-pawns)
        if (piece.Type != PieceType.Pawn && piece.Type != PieceType.King)
        {
            var ambiguous = legalMoves
                .Where(m => m.From != move.From && _squares[m.From].Type == piece.Type
                            && _squares[m.From].Color == piece.Color && m.To == move.To)
                .ToList();
            if (ambiguous.Count > 0)
            {
                bool sameFile = ambiguous.Any(m => File(m.From) == File(move.From));
                bool sameRank = ambiguous.Any(m => Rank(m.From) == Rank(move.From));
                if (!sameFile) sb.Append((char)('a' + File(move.From)));
                else if (!sameRank) sb.Append((char)('1' + Rank(move.From)));
                else { sb.Append((char)('a' + File(move.From))); sb.Append((char)('1' + Rank(move.From))); }
            }
        }

        bool isCapture = !_squares[move.To].IsEmpty
            || (piece.Type == PieceType.Pawn && move.To == _epSquare);

        if (isCapture)
        {
            if (piece.Type == PieceType.Pawn) sb.Append((char)('a' + File(move.From)));
            sb.Append('x');
        }

        sb.Append(ToAlgebraic(move.To));

        if (move.Promotion != PieceType.None)
            sb.Append('=').Append(move.Promotion switch
            {
                PieceType.Queen => 'Q', PieceType.Rook => 'R',
                PieceType.Bishop => 'B', _ => 'N'
            });

        return AddCheckSuffix(sb.ToString(), move);
    }

    private string AddCheckSuffix(string baseSan, ChessMove move)
    {
        var nb = Clone(); nb.ApplyInternal(move);
        if (nb.IsInCheck(nb._sideToMove))
        {
            bool mated = !nb.HasAnyLegalMove(nb._sideToMove);
            return baseSan + (mated ? "#" : "+");
        }
        return baseSan;
    }

    // ── Game status ───────────────────────────────────────────────────────────
    public GameStatus GetStatus()
    {
        // A mate or stalemate on the move that reaches a draw count is still a mate or stalemate.
        bool inCheck = IsInCheck(_sideToMove);
        bool hasLegal = HasAnyLegalMove(_sideToMove);
        if (!hasLegal) return inCheck ? GameStatus.Checkmate : GameStatus.Stalemate;

        string key = PositionKey();
        if (_positionHistory.Count(p => p == key) >= 3) return GameStatus.DrawRepetition;
        if (_halfMoveClock >= 100) return GameStatus.DrawFiftyMoves;
        if (IsInsufficientMaterial()) return GameStatus.DrawInsufficientMaterial;

        return inCheck ? GameStatus.Check : GameStatus.Ongoing;
    }

    public string GetStatusText() => GetStatus() switch
    {
        GameStatus.Checkmate => _sideToMove == Side.White
            ? "Checkmate! Black wins!" : "Checkmate! White wins!",
        GameStatus.Stalemate => "Stalemate!",
        GameStatus.Check => "Check!",
        GameStatus.DrawInsufficientMaterial => "Draw (Insufficient material)",
        GameStatus.DrawFiftyMoves => "Draw (Fifty-move rule)",
        GameStatus.DrawRepetition => "Draw (Repetition)",
        _ => "Ongoing"
    };

    public bool IsGameOver() => GetStatus() is
        GameStatus.Checkmate or GameStatus.Stalemate or
        GameStatus.DrawInsufficientMaterial or GameStatus.DrawFiftyMoves or GameStatus.DrawRepetition;

    // FIDE dead positions: K v K, K+minor v K, and K+B v K+B with both bishops on one colour.
    private bool IsInsufficientMaterial()
    {
        var nonKing = new List<(int Square, Piece Piece)>();
        for (int i = 0; i < 64; i++)
            if (!_squares[i].IsEmpty && _squares[i].Type != PieceType.King) nonKing.Add((i, _squares[i]));

        if (nonKing.Count == 0) return true;
        if (nonKing.Count == 1) return nonKing[0].Piece.Type is PieceType.Knight or PieceType.Bishop;
        if (nonKing.Count == 2 && nonKing.All(p => p.Piece.Type == PieceType.Bishop)
            && nonKing[0].Piece.Color != nonKing[1].Piece.Color)
            return SquareColour(nonKing[0].Square) == SquareColour(nonKing[1].Square);
        return false;
    }

    private static int SquareColour(int sq) => (File(sq) + Rank(sq)) % 2;

    // ── Analysis helpers ─────────────────────────────────────────────────────
    public (int white, int black) MaterialCounts()
    {
        int w = 0, b = 0;
        for (int i = 0; i < 64; i++)
        {
            var p = _squares[i];
            if (!p.IsEmpty && p.Type != PieceType.King)
            { if (p.Color == Side.White) w += p.Value; else b += p.Value; }
        }
        return (w, b);
    }

    // ── Utility ───────────────────────────────────────────────────────────────
    private static Side Opponent(Side s) => s == Side.White ? Side.Black : Side.White;

    private string PositionKey()
    {
        var sb = new System.Text.StringBuilder(68);
        for (int i = 0; i < 64; i++) sb.Append(_squares[i].IsEmpty ? '.' : _squares[i].FenChar);
        sb.Append(_sideToMove == Side.White ? 'w' : 'b');
        if (_wKingside) sb.Append('K'); if (_wQueenside) sb.Append('Q');
        if (_bKingside) sb.Append('k'); if (_bQueenside) sb.Append('q');
        // FIDE 9.2: positions are the same when the same moves are possible, so the en passant square counts
        // only when a pawn can really capture there (a pinned pawn cannot).
        sb.Append(EnPassantCaptureIsLegal() ? ToAlgebraic(_epSquare) : "-");
        return sb.ToString();
    }

    private bool EnPassantCaptureIsLegal()
    {
        if (_epSquare < 0) return false;
        // The capturing pawn stands beside the pushed pawn: one rank behind the target square, from the mover's side.
        int fromRank = Rank(_epSquare) + (_sideToMove == Side.White ? -1 : 1);
        foreach (int df in new[] { -1, 1 })
        {
            int file = File(_epSquare) + df;
            if (file < 0 || file > 7) continue;
            int from = Square(file, fromRank);
            var p = _squares[from];
            if (p.Type != PieceType.Pawn || p.Color != _sideToMove) continue;
            var nb = Clone(); nb.ApplyInternal(new ChessMove(from, _epSquare));
            if (!nb.IsInCheck(_sideToMove)) return true;
        }
        return false;
    }

    private ChessBoard Clone()
    {
        var c = new ChessBoard();
        Array.Copy(_squares, c._squares, 64);
        c._sideToMove = _sideToMove;
        c._wKingside = _wKingside; c._wQueenside = _wQueenside;
        c._bKingside = _bKingside; c._bQueenside = _bQueenside;
        c._epSquare = _epSquare;
        c._halfMoveClock = _halfMoveClock;
        c._fullMoveNumber = _fullMoveNumber;
        // Don't copy position history — clones are only for legality checks
        return c;
    }
}
