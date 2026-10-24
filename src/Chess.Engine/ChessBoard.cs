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

    public static string GetSymbol(Piece p) => (p.Type, p.Color) switch
    {
        (PieceType.King, Side.White) => "♔", (PieceType.Queen, Side.White) => "♕",
        (PieceType.Rook, Side.White) => "♖", (PieceType.Bishop, Side.White) => "♗",
        (PieceType.Knight, Side.White) => "♘", (PieceType.Pawn, Side.White) => "♙",
        (PieceType.King, Side.Black) => "♚", (PieceType.Queen, Side.Black) => "♛",
        (PieceType.Rook, Side.Black) => "♜", (PieceType.Bishop, Side.Black) => "♝",
        (PieceType.Knight, Side.Black) => "♞", (PieceType.Pawn, Side.Black) => "♟",
        _ => ""
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

    public void LoadFen(string fen)
    {
        Array.Fill(_squares, Piece.Empty);
        var parts = fen.Trim().Split(' ');

        // Piece placement: FEN goes rank8→rank1, left to right
        int sq = 56; // start at a8
        foreach (char c in parts[0])
        {
            if (c == '/') { sq -= 16; }
            else if (char.IsDigit(c)) { sq += c - '0'; }
            else { _squares[sq++] = Piece.FromFenChar(c); }
        }

        _sideToMove = parts.Length > 1 && parts[1] == "b" ? Side.Black : Side.White;

        string cr = parts.Length > 2 ? parts[2] : "-";
        _wKingside = cr.Contains('K'); _wQueenside = cr.Contains('Q');
        _bKingside = cr.Contains('k'); _bQueenside = cr.Contains('q');

        string ep = parts.Length > 3 ? parts[3] : "-";
        _epSquare = ep == "-" ? -1 : FromAlgebraic(ep);

        _halfMoveClock = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        _fullMoveNumber = parts.Length > 5 ? int.Parse(parts[5]) : 1;

        _positionHistory.Clear();
        _positionHistory.Add(PositionKey());
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
        => GetLegalMoves().FirstOrDefault(m => m.San == san);

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
        string key = PositionKey();
        if (_positionHistory.Count(p => p == key) >= 3) return GameStatus.DrawRepetition;
        if (_halfMoveClock >= 100) return GameStatus.DrawFiftyMoves;
        if (IsInsufficientMaterial()) return GameStatus.DrawInsufficientMaterial;

        bool inCheck = IsInCheck(_sideToMove);
        bool hasLegal = HasAnyLegalMove(_sideToMove);
        if (!hasLegal) return inCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
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

    private bool IsInsufficientMaterial()
    {
        var pieces = Enumerable.Range(0, 64).Select(i => _squares[i]).Where(p => !p.IsEmpty).ToList();
        if (pieces.Count == 2) return true; // K vs K
        if (pieces.Count == 3)
        {
            var nonK = pieces.Where(p => p.Type != PieceType.King).ToList();
            return nonK.Count == 1 && nonK[0].Type is PieceType.Knight or PieceType.Bishop;
        }
        return false;
    }

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

    public (int white, int black) SquareControl()
    {
        int w = GeneratePseudo(Side.White).Select(m => m.To).Distinct().Count();
        int b = GeneratePseudo(Side.Black).Select(m => m.To).Distinct().Count();
        return (w, b);
    }

    public (float white, float black) KingSafety()
    {
        float Eval(Side side)
        {
            int ks = -1;
            for (int i = 0; i < 64; i++)
                if (_squares[i].Type == PieceType.King && _squares[i].Color == side) { ks = i; break; }
            if (ks < 0) return 0;
            int pawns = 0;
            for (int df = -1; df <= 1; df++)
            for (int dr = -1; dr <= 1; dr++)
            {
                int nf = File(ks) + df, nr = Rank(ks) + dr;
                if (nf < 0 || nf > 7 || nr < 0 || nr > 7) continue;
                var p = _squares[Square(nf, nr)];
                if (!p.IsEmpty && p.Color == side && p.Type == PieceType.Pawn) pawns++;
            }
            return pawns;
        }
        return (Eval(Side.White), Eval(Side.Black));
    }

    public (int white, int black) Development()
    {
        int w = 0, b = 0;
        for (int i = 0; i < 64; i++)
        {
            var p = _squares[i];
            if (p.IsEmpty || p.Type is not (PieceType.Knight or PieceType.Bishop)) continue;
            bool onStart = p.Color == Side.White
                ? i is 1 or 2 or 5 or 6   // b1,c1,f1,g1
                : i is 57 or 58 or 61 or 62; // b8,c8,f8,g8
            if (!onStart) { if (p.Color == Side.White) w++; else b++; }
        }
        return (w, b);
    }

    public float SimpleEval()
    {
        var (wm, bm) = MaterialCounts();
        var (wc, bc) = SquareControl();
        return (wm - bm) + (wc - bc) * 0.1f;
    }

    // ── SVG board generation ─────────────────────────────────────────────────
    public string ToSvg()
    {
        const int S = 60, B = S * 8;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {B} {B}\" width=\"{B}\" height=\"{B}\">");
        for (int rank = 7; rank >= 0; rank--)
        for (int file = 0; file < 8; file++)
        {
            int x = file * S, y = (7 - rank) * S;
            string color = (rank + file) % 2 == 1 ? "#F0D9B5" : "#B58863";
            sb.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"{S}\" height=\"{S}\" fill=\"{color}\"/>");
            var p = _squares[Square(file, rank)];
            if (!p.IsEmpty)
            {
                string sym = Piece.GetSymbol(p);
                string fill = p.Color == Side.White ? "#fff" : "#222";
                string stroke = p.Color == Side.White ? "#333" : "#eee";
                sb.Append($"<text x=\"{x + S / 2}\" y=\"{y + S / 2 + 14}\" font-size=\"44\" text-anchor=\"middle\" "
                        + $"fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"0.5\" paint-order=\"stroke\">{sym}</text>");
            }
        }
        // Coordinate labels
        for (int i = 0; i < 8; i++)
        {
            sb.Append($"<text x=\"2\" y=\"{(7 - i) * S + 13}\" font-size=\"10\" fill=\"#888\">{i + 1}</text>");
            sb.Append($"<text x=\"{i * S + S - 10}\" y=\"{B - 2}\" font-size=\"10\" fill=\"#888\">{(char)('a' + i)}</text>");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
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
        sb.Append(_epSquare >= 0 ? ToAlgebraic(_epSquare) : "-");
        return sb.ToString();
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
