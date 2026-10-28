using System.Numerics;

namespace Ilerda;

/// <summary>
/// Moviment codificat en un int: from (6 bits) | to (6) | promoció (3) | flag (3).
/// Promoció: 1 = cavall, 2 = alfil, 3 = torre, 4 = dama.
/// </summary>
public static class MoveGen
{
    public const int FlagEp = 1, FlagDouble = 2, FlagCastle = 3;

    public static int Encode(int from, int to, int promo = 0, int flag = 0) =>
        from | (to << 6) | (promo << 12) | (flag << 16);

    public static int From(int m) => m & 63;
    public static int To(int m) => (m >> 6) & 63;
    public static int Promo(int m) => (m >> 12) & 7;
    public static int Flag(int m) => (m >> 16) & 7;

    public static string SquareName(int sq) => $"{(char)('a' + (sq & 7))}{(char)('1' + (sq >> 3))}";

    public static string ToUci(int m)
    {
        if (m == 0) return "0000";
        int promo = Promo(m);
        return SquareName(From(m)) + SquareName(To(m)) + (promo != 0 ? "nbrq"[promo - 1].ToString() : "");
    }

    static void AddPawnMove(Span<int> moves, ref int n, int from, int to, bool promo, int flag)
    {
        if (promo)
        {
            for (int p = 4; p >= 1; p--) moves[n++] = Encode(from, to, p, flag);
        }
        else moves[n++] = Encode(from, to, 0, flag);
    }

    /// <summary>Genera moviments pseudolegals (la legalitat es comprova a MakeMove).</summary>
    public static int Generate(Position p, Span<int> moves, bool capturesOnly = false)
    {
        int n = 0;
        int us = p.Side, them = us ^ 1;
        ulong ourOcc = p.ByColor[us], theirOcc = p.ByColor[them], occ = ourOcc | theirOcc;

        // Peons
        ulong pawns = p.Bb[us * 6 + Pc.Pawn];
        int dir = us == 0 ? 8 : -8;
        while (pawns != 0)
        {
            int from = BitOperations.TrailingZeroCount(pawns);
            pawns &= pawns - 1;
            int to = from + dir;
            bool promo = us == 0 ? to >= 56 : to < 8;

            if (p.Board[to] == -1 && (!capturesOnly || promo))
            {
                AddPawnMove(moves, ref n, from, to, promo, 0);
                bool start = us == 0 ? from < 16 : from >= 48;
                if (start && !capturesOnly && p.Board[to + dir] == -1)
                    moves[n++] = Encode(from, to + dir, 0, FlagDouble);
            }

            ulong caps = Attacks.Pawn[us, from] & theirOcc;
            while (caps != 0)
            {
                int t = BitOperations.TrailingZeroCount(caps);
                caps &= caps - 1;
                AddPawnMove(moves, ref n, from, t, promo, 0);
            }

            if (p.Ep >= 0 && (Attacks.Pawn[us, from] & (1UL << p.Ep)) != 0)
                moves[n++] = Encode(from, p.Ep, 0, FlagEp);
        }

        // Cavalls, alfils, torres, dames
        ulong mask = capturesOnly ? theirOcc : ~ourOcc;
        for (int pt = Pc.Knight; pt <= Pc.Queen; pt++)
        {
            ulong bb = p.Bb[us * 6 + pt];
            while (bb != 0)
            {
                int from = BitOperations.TrailingZeroCount(bb);
                bb &= bb - 1;
                ulong att = pt switch
                {
                    Pc.Knight => Attacks.Knight[from],
                    Pc.Bishop => Attacks.Bishop(from, occ),
                    Pc.Rook => Attacks.Rook(from, occ),
                    _ => Attacks.Queen(from, occ),
                } & mask;
                while (att != 0)
                {
                    int to = BitOperations.TrailingZeroCount(att);
                    att &= att - 1;
                    moves[n++] = Encode(from, to);
                }
            }
        }

        // Rei
        int ksq = p.KingSquare(us);
        ulong katt = Attacks.King[ksq] & mask;
        while (katt != 0)
        {
            int to = BitOperations.TrailingZeroCount(katt);
            katt &= katt - 1;
            moves[n++] = Encode(ksq, to);
        }

        // Enroc
        if (!capturesOnly)
        {
            if (us == 0)
            {
                if ((p.Castling & 1) != 0 && p.Board[5] == -1 && p.Board[6] == -1 &&
                    !p.IsAttacked(4, them) && !p.IsAttacked(5, them) && !p.IsAttacked(6, them))
                    moves[n++] = Encode(4, 6, 0, FlagCastle);
                if ((p.Castling & 2) != 0 && p.Board[1] == -1 && p.Board[2] == -1 && p.Board[3] == -1 &&
                    !p.IsAttacked(4, them) && !p.IsAttacked(3, them) && !p.IsAttacked(2, them))
                    moves[n++] = Encode(4, 2, 0, FlagCastle);
            }
            else
            {
                if ((p.Castling & 4) != 0 && p.Board[61] == -1 && p.Board[62] == -1 &&
                    !p.IsAttacked(60, them) && !p.IsAttacked(61, them) && !p.IsAttacked(62, them))
                    moves[n++] = Encode(60, 62, 0, FlagCastle);
                if ((p.Castling & 8) != 0 && p.Board[57] == -1 && p.Board[58] == -1 && p.Board[59] == -1 &&
                    !p.IsAttacked(60, them) && !p.IsAttacked(59, them) && !p.IsAttacked(58, them))
                    moves[n++] = Encode(60, 58, 0, FlagCastle);
            }
        }

        return n;
    }
}
