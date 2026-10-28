namespace Ilerda;

/// <summary>
/// Taules d'atacs precalculades. Els atacs de peces lliscants es calculen amb rajos
/// (correcte però lent): és el primer candidat a substituir per magic bitboards.
/// </summary>
public static class Attacks
{
    public static readonly ulong[] Knight = new ulong[64];
    public static readonly ulong[] King = new ulong[64];

    /// <summary>Pawn[color, sq] = caselles que ataca un peó d'aquest color situat a sq.</summary>
    public static readonly ulong[,] Pawn = new ulong[2, 64];

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
            Pawn[0, sq] = Bit(f - 1, r + 1) | Bit(f + 1, r + 1);
            Pawn[1, sq] = Bit(f - 1, r - 1) | Bit(f + 1, r - 1);
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

    public static ulong Bishop(int sq, ulong occ) => Slide(sq, occ, BishopDirs);
    public static ulong Rook(int sq, ulong occ) => Slide(sq, occ, RookDirs);
    public static ulong Queen(int sq, ulong occ) => Bishop(sq, occ) | Rook(sq, occ);
}
