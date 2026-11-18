namespace Ilerda;

/// <summary>
/// Taules d'atacs precalculades. Els atacs de peces lliscants fan servir magic bitboards;
/// el càlcul amb rajos (`Slide`) només serveix per omplir les taules en arrencar.
/// </summary>
public static class Attacks
{
    public static readonly ulong[] Knight = new ulong[64];
    public static readonly ulong[] King = new ulong[64];

    /// <summary>Pawn[color * 64 + sq] = caselles que ataca un peó d'aquest color situat a sq.</summary>
    public static readonly ulong[] Pawn = new ulong[128];

    static readonly (int df, int dr)[] BishopDirs = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
    static readonly (int df, int dr)[] RookDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    static Attacks()
    {
        (int, int)[] knightDeltas = { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
        (int, int)[] kingDeltas = { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

        for (int sq = 0; sq < 64; sq++)
        {
            int f = sq & 7, r = sq >> 3;
            foreach (var (df, dr) in knightDeltas) Knight[sq] |= Bit(f + df, r + dr);
            foreach (var (df, dr) in kingDeltas) King[sq] |= Bit(f + df, r + dr);
            Pawn[sq] = Bit(f - 1, r + 1) | Bit(f + 1, r + 1);
            Pawn[64 + sq] = Bit(f - 1, r - 1) | Bit(f + 1, r - 1);
        }
    }

    static ulong Bit(int f, int r) => (uint)f < 8 && (uint)r < 8 ? 1UL << (r * 8 + f) : 0UL;

    static ulong Slide(int sq, ulong occ, (int df, int dr)[] dirs)
    {
        ulong attacks = 0;
        foreach (var (df, dr) in dirs)
        {
            int f = (sq & 7) + df, r = (sq >> 3) + dr;
            while ((uint)f < 8 && (uint)r < 8)
            {
                ulong b = 1UL << (r * 8 + f);
                attacks |= b;
                if ((occ & b) != 0) break;
                f += df;
                r += dr;
            }
        }
        return attacks;
    }

    // Magic bitboards: per a cada casella, (occ & mask) * magic >> shift dona un índex únic a la taula d'atacs.
    // Els números màgics es busquen en arrencar (llavor fixa, uns quants ms) en lloc de portar-los escrits.
    sealed class Magic
    {
        public ulong Mask, Number;
        public int Shift, Offset;
    }

    static readonly Magic[] BishopMagics = new Magic[64];
    static readonly Magic[] RookMagics = new Magic[64];
    static readonly ulong[] BishopTable = BuildMagics(BishopMagics, BishopDirs);
    static readonly ulong[] RookTable = BuildMagics(RookMagics, RookDirs);

    static ulong[] BuildMagics(Magic[] magics, (int df, int dr)[] dirs)
    {
        var table = new List<ulong>();
        ulong seed = 0x9E3779B97F4A7C15UL;
        ulong Next()
        {
            seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17;
            return seed;
        }

        for (int sq = 0; sq < 64; sq++)
        {
            // Màscara d'ocupació rellevant: els rajos sense la casella de la vora (no canvia els atacs).
            ulong mask = 0;
            foreach (var (df, dr) in dirs)
            {
                int f = (sq & 7) + df, r = (sq >> 3) + dr;
                while ((uint)(f + df) < 8 && (uint)(r + dr) < 8)
                {
                    mask |= 1UL << (r * 8 + f);
                    f += df;
                    r += dr;
                }
            }

            int bits = System.Numerics.BitOperations.PopCount(mask);
            int size = 1 << bits;
            var occs = new ulong[size];
            var refs = new ulong[size];
            ulong sub = 0;
            for (int i = 0; i < size; i++)
            {
                occs[i] = sub;
                refs[i] = Slide(sq, sub, dirs);
                sub = (sub - mask) & mask; // següent subconjunt (carry-rippler)
            }

            var used = new ulong[size];
            var epoch = new int[size];
            int stamp = 0;
            while (true)
            {
                ulong magic = Next() & Next() & Next(); // pocs bits a 1: troba màgics molt més ràpid
                if (System.Numerics.BitOperations.PopCount((mask * magic) >> 56) < 6) continue;
                stamp++;
                bool ok = true;
                for (int i = 0; i < size && ok; i++)
                {
                    int idx = (int)((occs[i] * magic) >> (64 - bits));
                    if (epoch[idx] != stamp) { epoch[idx] = stamp; used[idx] = refs[i]; }
                    else if (used[idx] != refs[i]) ok = false;
                }
                if (!ok) continue;

                magics[sq] = new Magic { Mask = mask, Number = magic, Shift = 64 - bits, Offset = table.Count };
                for (int i = 0; i < size; i++) table.Add(0);
                for (int i = 0; i < size; i++)
                    table[magics[sq].Offset + (int)((occs[i] * magic) >> (64 - bits))] = refs[i];
                break;
            }
        }
        return table.ToArray();
    }

    public static ulong Bishop(int sq, ulong occ)
    {
        ref readonly Magic m = ref BishopMagics[sq];
        return BishopTable[m.Offset + (int)(((occ & m.Mask) * m.Number) >> m.Shift)];
    }

    public static ulong Rook(int sq, ulong occ)
    {
        ref readonly Magic m = ref RookMagics[sq];
        return RookTable[m.Offset + (int)(((occ & m.Mask) * m.Number) >> m.Shift)];
    }

    public static ulong Queen(int sq, ulong occ) => Bishop(sq, occ) | Rook(sq, occ);
}
