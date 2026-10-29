namespace Ilerda;

/// <summary>
/// Posicions de referència del Chess Programming Wiki. Si alguna falla, el generador
/// de moviments té un error: no té sentit continuar amb la cerca fins que passi tot.
/// </summary>
public static class PerftSuite
{
    static readonly (string name, string fen, ulong[] nodes)[] Cases =
    {
        ("Inicial", Position.StartFen, new ulong[] { 20, 400, 8902, 197281 }),
        ("Kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", new ulong[] { 48, 2039, 97862 }),
        ("Posició 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", new ulong[] { 14, 191, 2812, 43238 }),
        ("Posició 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", new ulong[] { 6, 264, 9467 }),
        ("Posició 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", new ulong[] { 44, 1486, 62379 }),
        ("Posició 6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", new ulong[] { 46, 2079, 89890 }),
    };

    /// <summary>Recorre l'arbre comprovant que la clau incremental coincideix amb la recalculada.</summary>
    static bool KeysConsistent(Position pos, int depth)
    {
        if (depth == 0) return true;
        Span<int> moves = stackalloc int[320];
        int n = MoveGen.Generate(pos, moves);
        ulong before = pos.Key;
        if (!pos.InCheck())
        {
            pos.MakeNullMove();
            bool nullOk = pos.Key == pos.ComputeKey();
            pos.UnmakeNullMove();
            if (!nullOk || pos.Key != before) return false;
        }
        for (int i = 0; i < n; i++)
        {
            if (!pos.MakeMove(moves[i])) continue;
            bool ok = pos.Key == pos.ComputeKey() && KeysConsistent(pos, depth - 1);
            pos.UnmakeMove();
            if (!ok || pos.Key != before) return false;
        }
        return true;
    }

    /// <summary>Retorna true si tots els recomptes coincideixen.</summary>
    public static bool Run(Action<string> output)
    {
        bool ok = true;
        foreach (var (name, fen, expected) in Cases)
        {
            var pos = new Position();
            for (int d = 1; d <= expected.Length; d++)
            {
                pos.SetFen(fen);
                ulong got = Perft.Run(pos, d);
                bool pass = got == expected[d - 1];
                ok &= pass;
                output($"{(pass ? "OK  " : "FAIL")} {name} profunditat {d}: {got} (esperat {expected[d - 1]})");
            }
            pos.SetFen(fen);
            bool keysOk = KeysConsistent(pos, 3);
            ok &= keysOk;
            output($"{(keysOk ? "OK  " : "FAIL")} {name} clau Zobrist incremental");
        }
        ok &= SeeCases(output);
        return ok;
    }

    /// <summary>Captures amb el resultat del SEE conegut (casella = fila * 8 + columna, a1 = 0).</summary>
    static bool SeeCases(Action<string> output)
    {
        var cases = new (string name, string fen, int from, int to, int expected)[]
        {
            ("peó guanya cavall defensat", "4k3/8/3p4/4n3/3P4/8/8/4K3 w - - 0 1", 27, 36, 220),
            ("dama perd contra peó defensor", "4k3/8/3p4/4p3/8/8/4Q3/4K3 w - - 0 1", 12, 36, -800),
            ("torre captura torre defensada", "3rk3/8/8/3r4/8/8/3R4/3RK3 w - - 0 1", 11, 35, 500),
        };
        bool ok = true;
        foreach (var (name, fen, from, to, expected) in cases)
        {
            var pos = new Position();
            pos.SetFen(fen);
            int got = pos.See(MoveGen.Encode(from, to));
            bool pass = got == expected;
            ok &= pass;
            output($"{(pass ? "OK  " : "FAIL")} SEE {name}: {got} (esperat {expected})");
        }
        return ok;
    }
}
