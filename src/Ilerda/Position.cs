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
        c._histCount = _histCount;
        return c;
    }

    /// <summary>Descarta l'historial d'undo (després d'aplicar els moviments d'un "position").
    /// L'historial de claus es conserva per a la detecció de repeticions.</summary>
    public void ClearHistory() => _ply = 0;

    /// <summary>True si la posició actual ja s'ha vist dins del tram reversible (des de l'últim peó/captura).</summary>
    public bool IsRepetition()
    {
        int lowest = Math.Max(0, _histCount - Halfmove);
        for (int i = _histCount - 2; i >= lowest; i -= 2)
            if (_keyHist[i] == Key) return true;
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

    public void SetFen(string fen)
    {
        Array.Clear(Bb);
        Array.Clear(ByColor);
        Array.Fill(Board, -1);
        _ply = 0;
        _histCount = 0;
        Key = 0;

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

    static readonly int[] SeeValue = { 100, 320, 330, 500, 900, 20000 };

    /// <summary>Totes les peces (dels dos colors) que ataquen sq amb l'ocupació occ.</summary>
    ulong AttackersTo(int sq, ulong occ)
    {
        ulong diag = Bb[Pc.Bishop] | Bb[Pc.Queen] | Bb[6 + Pc.Bishop] | Bb[6 + Pc.Queen];
        ulong line = Bb[Pc.Rook] | Bb[Pc.Queen] | Bb[6 + Pc.Rook] | Bb[6 + Pc.Queen];
        return ((Attacks.Pawn[1, sq] & Bb[Pc.Pawn]) | (Attacks.Pawn[0, sq] & Bb[6 + Pc.Pawn])
                | (Attacks.Knight[sq] & (Bb[Pc.Knight] | Bb[6 + Pc.Knight]))
                | (Attacks.King[sq] & (Bb[Pc.King] | Bb[6 + Pc.King]))
                | (Attacks.Bishop(sq, occ) & diag)
                | (Attacks.Rook(sq, occ) & line)) & occ;
    }

    /// <summary>
    /// Static Exchange Evaluation: guany de material (en cp) de la captura m si els dos bàndols
    /// es recapturen a la casella de destinació amb la peça menys valuosa fins que els convingui parar.
    /// No es fa servir per a promocions.
    /// </summary>
    public int See(int m)
    {
        int from = MoveGen.From(m), to = MoveGen.To(m);
        bool ep = MoveGen.Flag(m) == MoveGen.FlagEp;
        Span<int> gain = stackalloc int[34];
        int victim = ep ? Pc.Pawn : (Board[to] == -1 ? -1 : Board[to] % 6);
        gain[0] = victim < 0 ? 0 : SeeValue[victim];
        int attPt = Board[from] % 6;

        ulong occ = Occ & ~(1UL << from);
        if (ep) occ &= ~(1UL << (to + (Side == 0 ? -8 : 8)));
        ulong atk = AttackersTo(to, occ);
        int side = Side ^ 1, d = 0;

        while (true)
        {
            ulong sideAtk = atk & ByColor[side];
            if (sideAtk == 0) break;
            int pt = 0;
            ulong bb = 0;
            for (; pt < 6; pt++)
            {
                bb = sideAtk & Bb[side * 6 + pt];
                if (bb != 0) break;
            }
            // El rei només pot capturar si no queda cap defensor rival.
            if (pt == Pc.King && (atk & ByColor[side ^ 1]) != 0) break;
            d++;
            gain[d] = SeeValue[attPt] - gain[d - 1];
            attPt = pt;
            occ &= ~(bb & (0UL - bb));
            atk = AttackersTo(to, occ);
            side ^= 1;
        }

        for (int i = d; i > 0; i--) gain[i - 1] = -Math.Max(-gain[i - 1], gain[i]);
        return gain[0];
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
            UnmakeMove();
            return false;
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
    public bool HasNonPawnMaterial(int color) =>
        (Bb[color * 6 + Pc.Knight] | Bb[color * 6 + Pc.Bishop] | Bb[color * 6 + Pc.Rook] | Bb[color * 6 + Pc.Queen]) != 0;

    public void UnmakeMove()
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
