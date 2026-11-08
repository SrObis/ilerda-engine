using System.Numerics;
using System.Text;

namespace Ilerda;

public static class Pc
{
    public const int Pawn = 0, Knight = 1, Bishop = 2, Rook = 3, Queen = 4, King = 5;
}

/// <summary>
/// Posició amb bitboards. Caselles 0..63 amb a1 = 0 i h8 = 63.
/// Les peces s'indexen com color * 6 + tipus (color 0 = blanques, 1 = negres).
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public readonly ulong[] Bb = new ulong[12];
    public readonly ulong[] ByColor = new ulong[2];
    public readonly int[] Board = new int[64]; // -1 = buida, sinó color * 6 + tipus

    public int Side;
    public int Castling; // 1 = K blanc, 2 = Q blanc, 4 = k negre, 8 = q negre
    public int Ep = -1;
    public int Halfmove;
    public int Fullmove = 1;

    public ulong Occ => ByColor[0] | ByColor[1];

    /// <summary>Clau Zobrist de la posició, mantinguda de forma incremental.</summary>
    public ulong Key;

    // Pila d'acumuladors NNUE (blanques i negres, N neurones cadascun): una entrada per jugada feta.
    // MakeMove calcula la nova a partir de l'anterior en una sola passada i UnmakeMove només torna enrere,
    // així que desfer una jugada no costa res i les jugades il·legals no toquen l'acumulador.
    short[] _accBuf = Array.Empty<short>();
    int _accTop;
    static int AccSize => 2 * Nnue.Hidden;

    /// <summary>Acumulador NNUE de la posició actual.</summary>
    public ReadOnlySpan<short> Acc => _accBuf.AsSpan(_accTop * AccSize, AccSize);

    void EnsureAcc(int top)
    {
        int need = (top + 1) * AccSize;
        if (_accBuf.Length < need) Array.Resize(ref _accBuf, Math.Max(need, Math.Max(_accBuf.Length * 2, 160 * AccSize)));
    }

    static readonly ulong[,] ZPiece = new ulong[12, 64];
    static readonly ulong[] ZCastle = new ulong[16];
    static readonly ulong[] ZEp = new ulong[8];
    static readonly ulong ZSide;

    static Position()
    {
        // xorshift64* amb llavor fixa: les claus són reproduïbles entre execucions.
        ulong s = 0x9E3779B97F4A7C15UL;
        ulong Next()
        {
            s ^= s >> 12; s ^= s << 25; s ^= s >> 27;
            return s * 0x2545F4914F6CDD1DUL;
        }
        for (int p = 0; p < 12; p++)
            for (int sq = 0; sq < 64; sq++) ZPiece[p, sq] = Next();
        for (int i = 0; i < 16; i++) ZCastle[i] = Next();
        for (int i = 0; i < 8; i++) ZEp[i] = Next();
        ZSide = Next();
    }

    /// <summary>Recalcula la clau des de zero (per validar la incremental).</summary>
    public ulong ComputeKey()
    {
        ulong k = 0;
        for (int sq = 0; sq < 64; sq++)
            if (Board[sq] != -1) k ^= ZPiece[Board[sq], sq];
        if (Side == 1) k ^= ZSide;
        k ^= ZCastle[Castling];
        if (Ep != -1) k ^= ZEp[Ep & 7];
        return k;
    }

    struct Undo
    {
        public int Move, Captured, Castling, Ep, Halfmove;
        public ulong Key;
    }

    readonly Undo[] _stack = new Undo[2048];
    int _ply;

    // Claus de les posicions anteriors (abans de cada moviment), incloent la partida
    // sencera: ClearHistory no les esborra perquè la cerca detecti repeticions prèvies.
    ulong[] _keyHist = new ulong[4096];
    int _histCount;

    static readonly int[] CastleMask = BuildCastleMask();

    static int[] BuildCastleMask()
    {
        var m = new int[64];
        Array.Fill(m, 15);
        m[4] = 15 & ~3;   // e1
        m[0] = 15 & ~2;   // a1
        m[7] = 15 & ~1;   // h1
        m[60] = 15 & ~12; // e8
        m[56] = 15 & ~8;  // a8
        m[63] = 15 & ~4;  // h8
        return m;
    }

    public Position()
    {
        Array.Fill(Board, -1);
    }

    public Position Clone()
    {
        var c = new Position();
        Array.Copy(Bb, c.Bb, 12);
        Array.Copy(ByColor, c.ByColor, 2);
        Array.Copy(Board, c.Board, 64);
        c.Side = Side;
        c.Castling = Castling;
        c.Ep = Ep;
        c.Halfmove = Halfmove;
        c.Fullmove = Fullmove;
        c.Key = Key;
        Array.Copy(_keyHist, c._keyHist, _histCount);
        if (Nnue.Loaded)
        {
            c.EnsureAcc(0);
            Acc.CopyTo(c._accBuf);
        }
        c._histCount = _histCount;
        return c;
    }

    /// <summary>Descarta l'historial d'undo (després d'aplicar els moviments d'un "position").
    /// L'historial de claus es conserva per a la detecció de repeticions.</summary>
    public void ClearHistory()
    {
        _ply = 0;
        if (Nnue.Loaded && _accTop > 0)
        {
            Acc.CopyTo(_accBuf);
            _accTop = 0;
        }
    }

    /// <summary>True si la posició actual ja s'ha vist dins del tram reversible (des de l'últim peó/captura).</summary>
    public bool IsRepetition()
    {
        int lowest = Math.Max(0, _histCount - Halfmove);
        for (int i = _histCount - 2; i >= lowest; i -= 2)
            if (_keyHist[i] == Key) return true;
        return false;
    }

    /// <summary>
    /// Repetició dins de la cerca. <paramref name="ply"/> és la distància a l'arrel. Una posició que ja s'ha vist
    /// dins de l'arbre de cerca (arrel inclosa) compta com a taules a la primera repetició, perquè l'altre bàndol
    /// pot repetir-la; una de la partida anterior a l'arrel només si ja s'havia vist dues vegades (triple repetició),
    /// perquè l'adversari no està obligat a repetir i la primera repetició no és cap taula assegurada.
    /// </summary>
    public bool IsRepetition(int ply)
    {
        int lowest = Math.Max(0, _histCount - Halfmove);
        int seen = 0;
        for (int i = _histCount - 2; i >= lowest; i -= 2)
        {
            if (_keyHist[i] != Key) continue;
            if (i >= _histCount - ply) return true;
            if (++seen >= 2) return true;
        }
        return false;
    }

    void AddPiece(int sq, int piece)
    {
        Key ^= ZPiece[piece, sq];
        ulong b = 1UL << sq;
        Bb[piece] |= b;
        ByColor[piece / 6] |= b;
        Board[sq] = piece;
    }

    void RemovePiece(int sq)
    {
        int piece = Board[sq];
        Key ^= ZPiece[piece, sq];
        ulong b = ~(1UL << sq);
        Bb[piece] &= b;
        ByColor[piece / 6] &= b;
        Board[sq] = -1;
    }

    void MovePiece(int from, int to)
    {
        int piece = Board[from];
        RemovePiece(from);
        AddPiece(to, piece);
    }

    /// <summary>Recalcula els acumuladors NNUE des de zero (només els biaixos si <paramref name="clearOnly"/>).</summary>
    public void RefreshAccumulator()
    {
        if (!Nnue.Loaded) return;
        // La mida pot haver canviat (xarxa nova): es torna a començar la pila.
        if (_accBuf.Length % AccSize != 0) _accBuf = Array.Empty<short>();
        EnsureAcc(_accTop);
        var acc = _accBuf.AsSpan(_accTop * AccSize, AccSize);
        Nnue.Reset(acc);
        for (int sq = 0; sq < 64; sq++)
            if (Board[sq] != -1) Nnue.Add(acc, Board[sq], sq);
    }

    public void SetFen(string fen)
    {
        Array.Clear(Bb);
        Array.Clear(ByColor);
        Array.Fill(Board, -1);
        _ply = 0;
        _histCount = 0;
        Key = 0;
        _accTop = 0;

        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int rank = 7, file = 0;
        foreach (char c in parts[0])
        {
            if (c == '/') { rank--; file = 0; }
            else if (char.IsDigit(c)) file += c - '0';
            else
            {
                int color = char.IsUpper(c) ? 0 : 1;
                int pt = "pnbrqk".IndexOf(char.ToLowerInvariant(c));
                AddPiece(rank * 8 + file, color * 6 + pt);
                file++;
            }
        }

        Side = parts.Length > 1 && parts[1] == "b" ? 1 : 0;

        Castling = 0;
        if (parts.Length > 2)
        {
            foreach (char c in parts[2])
            {
                Castling |= c switch { 'K' => 1, 'Q' => 2, 'k' => 4, 'q' => 8, _ => 0 };
            }
        }

        Ep = -1;
        if (parts.Length > 3 && parts[3] != "-")
            Ep = (parts[3][1] - '1') * 8 + (parts[3][0] - 'a');

        Halfmove = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        Fullmove = parts.Length > 5 ? int.Parse(parts[5]) : 1;

        Key = ComputeKey();
        RefreshAccumulator();
    }

    public bool IsAttacked(int sq, int by)
    {
        ulong occ = Occ;
        if ((Attacks.Pawn[by ^ 1, sq] & Bb[by * 6 + Pc.Pawn]) != 0) return true;
        if ((Attacks.Knight[sq] & Bb[by * 6 + Pc.Knight]) != 0) return true;
        if ((Attacks.King[sq] & Bb[by * 6 + Pc.King]) != 0) return true;
        if ((Attacks.Bishop(sq, occ) & (Bb[by * 6 + Pc.Bishop] | Bb[by * 6 + Pc.Queen])) != 0) return true;
        if ((Attacks.Rook(sq, occ) & (Bb[by * 6 + Pc.Rook] | Bb[by * 6 + Pc.Queen])) != 0) return true;
        return false;
    }

    public int KingSquare(int color) => BitOperations.TrailingZeroCount(Bb[color * 6 + Pc.King]);

    public bool InCheck() => IsAttacked(KingSquare(Side), Side ^ 1);

    /// <summary>Fa el moviment. Si deixa el propi rei en escac, el desfà i retorna false.</summary>
    public bool MakeMove(int m)
    {
        int from = MoveGen.From(m), to = MoveGen.To(m), promo = MoveGen.Promo(m), flag = MoveGen.Flag(m);
        int us = Side, them = us ^ 1;
        int piece = Board[from];
        int epCapSq = to + (us == 0 ? -8 : 8);
        int captured = flag == MoveGen.FlagEp ? Board[epCapSq] : Board[to];

        _stack[_ply++] = new Undo { Move = m, Captured = captured, Castling = Castling, Ep = Ep, Halfmove = Halfmove, Key = Key };
        _keyHist[_histCount++] = Key;

        Key ^= ZCastle[Castling];
        if (Ep != -1) Key ^= ZEp[Ep & 7];

        Halfmove++;
        if (piece % 6 == Pc.Pawn || captured != -1) Halfmove = 0;

        if (flag == MoveGen.FlagEp) RemovePiece(epCapSq);
        else if (captured != -1) RemovePiece(to);

        MovePiece(from, to);

        if (promo != 0)
        {
            RemovePiece(to);
            AddPiece(to, us * 6 + promo);
        }

        if (flag == MoveGen.FlagCastle)
        {
            if (to > from) MovePiece(to + 1, to - 1); // enroc curt
            else MovePiece(to - 2, to + 1);           // enroc llarg
        }

        Ep = flag == MoveGen.FlagDouble ? (from + to) / 2 : -1;
        Castling &= CastleMask[from] & CastleMask[to];
        Key ^= ZCastle[Castling] ^ ZSide;
        if (Ep != -1) Key ^= ZEp[Ep & 7];
        Side = them;
        if (us == 1) Fullmove++;

        if (IsAttacked(KingSquare(us), them))
        {
            UnmakeBoard();
            return false;
        }

        if (Nnue.Loaded)
        {
            // Canvis de característiques: parells (peça, casella) afegits i trets.
            Span<int> adds = stackalloc int[4], subs = stackalloc int[4];
            int na = 0, ns = 0;
            subs[ns++] = piece; subs[ns++] = from;
            adds[na++] = promo != 0 ? us * 6 + promo : piece; adds[na++] = to;
            if (captured != -1) { subs[ns++] = captured; subs[ns++] = flag == MoveGen.FlagEp ? epCapSq : to; }
            else if (flag == MoveGen.FlagCastle)
            {
                int rook = us * 6 + Pc.Rook;
                int rf = to > from ? to + 1 : to - 2, rt = to > from ? to - 1 : to + 1;
                subs[ns++] = rook; subs[ns++] = rf;
                adds[na++] = rook; adds[na++] = rt;
            }
            EnsureAcc(_accTop + 1);
            Nnue.ApplyDelta(_accBuf.AsSpan(_accTop * AccSize, AccSize), _accBuf.AsSpan((_accTop + 1) * AccSize, AccSize), adds[..na], subs[..ns]);
            _accTop++;
        }
        return true;
    }

    /// <summary>Passa el torn sense moure (per a la poda null move). Només s'ha de fer fora d'escac.</summary>
    public void MakeNullMove()
    {
        _stack[_ply++] = new Undo { Move = 0, Captured = -1, Castling = Castling, Ep = Ep, Halfmove = Halfmove, Key = Key };
        _keyHist[_histCount++] = Key;
        if (Ep != -1) Key ^= ZEp[Ep & 7];
        Ep = -1;
        Key ^= ZSide;
        Side ^= 1;
        Halfmove++;
    }

    public void UnmakeNullMove()
    {
        var u = _stack[--_ply];
        _histCount--;
        Side ^= 1;
        Ep = u.Ep;
        Halfmove = u.Halfmove;
        Key = u.Key;
    }

    /// <summary>True si el bàndol té alguna peça que no sigui peó ni rei (evita null move en zugzwang).</summary>
    /// <summary>
    /// Material insuficient per fer mat: només reis, un sol cavall o alfil, o només alfils
    /// de totes dues bandes a caselles del mateix color.
    /// </summary>
    public bool InsufficientMaterial()
    {
        const ulong Light = 0x55AA55AA55AA55AAUL;
        ulong heavy = Bb[Pc.Pawn] | Bb[Pc.Rook] | Bb[Pc.Queen] | Bb[6 + Pc.Pawn] | Bb[6 + Pc.Rook] | Bb[6 + Pc.Queen];
        if (heavy != 0) return false;
        ulong knights = Bb[Pc.Knight] | Bb[6 + Pc.Knight];
        ulong bishops = Bb[Pc.Bishop] | Bb[6 + Pc.Bishop];
        if (knights == 0) return (bishops & Light) == 0 || (bishops & ~Light) == 0;
        return bishops == 0 && (knights & (knights - 1)) == 0;
    }

    public bool HasNonPawnMaterial(int color) =>
        (Bb[color * 6 + Pc.Knight] | Bb[color * 6 + Pc.Bishop] | Bb[color * 6 + Pc.Rook] | Bb[color * 6 + Pc.Queen]) != 0;

    public void UnmakeMove()
    {
        UnmakeBoard();
        if (Nnue.Loaded) _accTop--;
    }

    void UnmakeBoard()
    {
        var u = _stack[--_ply];
        _histCount--;
        int m = u.Move;
        int from = MoveGen.From(m), to = MoveGen.To(m), promo = MoveGen.Promo(m), flag = MoveGen.Flag(m);
        Side ^= 1;
        int us = Side;

        if (flag == MoveGen.FlagCastle)
        {
            if (to > from) MovePiece(to - 1, to + 1);
            else MovePiece(to + 1, to - 2);
        }

        if (promo != 0)
        {
            RemovePiece(to);
            AddPiece(from, us * 6 + Pc.Pawn);
        }
        else MovePiece(to, from);

        if (u.Captured != -1)
            AddPiece(flag == MoveGen.FlagEp ? to + (us == 0 ? -8 : 8) : to, u.Captured);

        Castling = u.Castling;
        Ep = u.Ep;
        Halfmove = u.Halfmove;
        Key = u.Key;
        if (us == 1) Fullmove--;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        for (int r = 7; r >= 0; r--)
        {
            for (int f = 0; f < 8; f++)
            {
                int p = Board[r * 8 + f];
                sb.Append(p == -1 ? '.' : "PNBRQKpnbrqk"[p]).Append(' ');
            }
            sb.AppendLine();
        }
        sb.Append(Side == 0 ? "Juguen blanques" : "Juguen negres");
        return sb.ToString();
    }
}
