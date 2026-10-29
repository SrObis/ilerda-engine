using System.Text;

namespace Ilerda;

/// <summary>
/// Crea un llibre d'obertures en EPD per a <see cref="DataGen"/>: jugades aleatòries filtrades pel mateix motor.
/// Una línia es descarta si, després de la primera jugada de cada bàndol, el motor la valora fora de ±EarlyMargin
/// (jugades com 1.h4 o 1.a4), o si la posició final és fora de ±Margin, està en escac o té una captura com a millor jugada.
/// </summary>
public static class BookGen
{
    const int EarlyMargin = 30;      // cp: límit després de la 1a jugada de cada bàndol
    const int MinPlies = 8, MaxPlies = 12; // semijugades aleatòries (inclusives)

    const int EarlyPlies = 6;        // semijugades inicials sense peons de les columnes a, b, g, h, cavalls a la vora ni torres
    const int StepNodes = 1500;      // nodes de les cerques de cada jugada candidata (la final fa servir tots els nodes)
    const int PickWindow = 40;       // cp: només es trien jugades a aquesta distància de la millor

    // Jugades que l'avaluació PeSTO no castiga però que cap obertura sensata fa: l'avaluació sola no les veu.
    static bool Rim(Position pos, int m)
    {
        int from = MoveGen.From(m), to = MoveGen.To(m);
        int kind = pos.Board[from] % 6;
        if (kind == Pc.Rook) return true;
        if (kind == Pc.Pawn) { int f = from & 7; return f <= 1 || f >= 6; }
        if (kind == Pc.Knight) { int f = to & 7; return f == 0 || f == 7; }
        return false;
    }

    public static void Run(long lines, string path, int threads, int seed, long nodes, int margin)
    {
        threads = Math.Max(1, threads);
        var seen = new HashSet<ulong>();
        var gate = new object();
        long written = 0, tried = 0, rejectedEarly = 0, rejectedFinal = 0, dup = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int quit = 0;
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Volatile.Write(ref quit, 1); };
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));

        Console.WriteLine($"book: {lines} línies, {nodes} nodes, marge ±{margin} cp (±{EarlyMargin} a les 2 primeres semijugades), {threads} fils, fitxer {path}");

        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            var rng = new Random(seed + t * 7919);
            workers[t] = new Thread(() =>
            {
                var s = new Searcher { NodeLimit = nodes };
                s.SetHash(4);
                var pos = new Position();
                Span<int> moves = stackalloc int[320];
                Span<int> ok = stackalloc int[320];
                while (Volatile.Read(ref quit) == 0 && Volatile.Read(ref written) < lines)
                {
                    Interlocked.Increment(ref tried);
                    pos.SetFen(Position.StartFen);
                    int plies = MinPlies + rng.Next(MaxPlies - MinPlies + 1);
                    bool alive = true;
                    for (int i = 0; i < plies && alive; i++)
                    {
                        int n = MoveGen.Generate(pos, moves);
                        int legal = 0;
                        for (int k = 0; k < n; k++)
                            if (pos.MakeMove(moves[k])) { pos.UnmakeMove(); if (i >= EarlyPlies || !Rim(pos, moves[k])) ok[legal++] = moves[k]; }
                        if (legal == 0) { alive = false; break; }

                        // El motor valora cada jugada candidata amb una cerca curta (puntuació pel bàndol que mou) i
                        // només se'n tria una d'entre les que queden a menys de PickWindow cp de la millor.
                        Span<int> sc = stackalloc int[320];
                        int top = int.MinValue;
                        s.NodeLimit = StepNodes;
                        for (int k = 0; k < legal; k++)
                        {
                            pos.MakeMove(ok[k]);
                            s.NewGame();
                            s.Think(pos, 64, 0, null);
                            sc[k] = -s.LastScore;
                            pos.UnmakeMove();
                            if (sc[k] > top) top = sc[k];
                        }
                        s.NodeLimit = nodes;
                        int good = 0;
                        for (int k = 0; k < legal; k++)
                            if (sc[k] >= top - PickWindow) { ok[good] = ok[k]; sc[good] = sc[k]; good++; }
                        int pick = rng.Next(good);
                        pos.MakeMove(ok[pick]);

                        // La posició resultant ha de ser equilibrada (es guarda com a puntuació des del bàndol que mou al sortir-ne).
                        int w = pos.Side == 0 ? -sc[pick] : sc[pick];
                        if (Math.Abs(w) > (i < 2 ? EarlyMargin : margin)) { Interlocked.Increment(ref rejectedEarly); alive = false; }
                    }
                    if (!alive) continue;

                    if (pos.InCheck()) { Interlocked.Increment(ref rejectedFinal); continue; }
                    s.NewGame();
                    int best = s.Think(pos, 64, 0, null);
                    int fw = pos.Side == 0 ? s.LastScore : -s.LastScore;
                    bool capture = pos.Board[MoveGen.To(best)] != -1 || MoveGen.Flag(best) == MoveGen.FlagEp;
                    if (best == 0 || capture || Math.Abs(fw) > margin) { Interlocked.Increment(ref rejectedFinal); continue; }

                    string[] f = DataGen.Fen(pos).Split(' ');
                    string epd = $"{f[0]} {f[1]} {f[2]} {f[3]}";
                    lock (gate)
                    {
                        if (written >= lines) break;
                        if (!seen.Add(pos.Key)) { dup++; continue; }
                        writer.WriteLine(epd);
                        written++;
                        if (written % 1000 == 0)
                        {
                            writer.Flush();
                            Console.WriteLine($"{written} línies, {tried} intents, {sw.Elapsed.TotalMinutes:0.0} min");
                        }
                    }
                }
            }, 16 * 1024 * 1024);
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();
        writer.Flush();
        Console.WriteLine($"book acabat: {written} línies de {tried} intents (descartats: {rejectedEarly} a l'inici, {rejectedFinal} al final, {dup} duplicats), {sw.Elapsed.TotalMinutes:0.0} min");
    }
}
