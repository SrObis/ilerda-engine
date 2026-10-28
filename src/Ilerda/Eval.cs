using System.Numerics;

namespace Ilerda;

/// <summary>
/// Avaluació provisional: material + petits bonus posicionals.
/// Es substituirà per una xarxa NNUE entrenada amb dades d'autojoc.
/// </summary>
public static class Eval
{
    public static readonly int[] Value = { 100, 320, 330, 500, 900, 0 };

    /// <summary>Puntuació en centipeons des del punt de vista del bàndol que juga.</summary>
    public static int Evaluate(Position p)
    {
        int score = 0;
        for (int color = 0; color < 2; color++)
        {
            int sign = color == 0 ? 1 : -1;
            for (int pt = 0; pt < 6; pt++)
            {
                ulong bb = p.Bb[color * 6 + pt];
                while (bb != 0)
                {
                    int sq = BitOperations.TrailingZeroCount(bb);
                    bb &= bb - 1;
                    score += sign * (Value[pt] + Positional(pt, sq, color));
                }
            }
        }
        return p.Side == 0 ? score : -score;
    }

    static int Positional(int pt, int sq, int color)
    {
        int f = sq & 7, r = sq >> 3;
        int dist = Math.Max(Math.Abs(2 * f - 7), Math.Abs(2 * r - 7)); // 1..7
        int centre = 7 - dist; // 0..6
        return pt switch
        {
            Pc.Pawn => (color == 0 ? r : 7 - r) * 5,
            Pc.Knight => centre * 5,
            Pc.Bishop => centre * 3,
            Pc.Queen => centre,
            _ => 0,
        };
    }
}
