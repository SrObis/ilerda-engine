namespace Ilerda;

public static class Perft
{
    public static ulong Run(Position pos, int depth)
    {
        if (depth == 0) return 1;

        Span<int> moves = stackalloc int[320];
        int n = MoveGen.Generate(pos, moves);
        ulong nodes = 0;
        for (int i = 0; i < n; i++)
        {
            if (!pos.MakeMove(moves[i])) continue;
            nodes += depth == 1 ? 1UL : Run(pos, depth - 1);
            pos.UnmakeMove();
        }
        return nodes;
    }

    /// <summary>Perft desglossat per moviment arrel (útil per depurar contra un altre motor).</summary>
    public static ulong Divide(Position pos, int depth, Action<string> output)
    {
        Span<int> moves = stackalloc int[320];
        int n = MoveGen.Generate(pos, moves);
        ulong total = 0;
        for (int i = 0; i < n; i++)
        {
            if (!pos.MakeMove(moves[i])) continue;
            ulong c = depth <= 1 ? 1UL : Run(pos, depth - 1);
            pos.UnmakeMove();
            output($"{MoveGen.ToUci(moves[i])}: {c}");
            total += c;
        }
        return total;
    }
}
