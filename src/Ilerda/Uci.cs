namespace Ilerda;

public static class Uci
{
    public const string Name = "Ilerda";
    public const string Version = "0.13.6";
    public const string Author = "Jordi Obis";

    static readonly object OutLock = new();

    static void Send(string s)
    {
        lock (OutLock)
        {
            Console.Out.WriteLine(s);
            Console.Out.Flush();
        }
    }

    static readonly string[] BenchFens =
    {
        Position.StartFen,
        "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1",
        "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1",
        "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1",
        "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8",
        "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10",
        "2rq1rk1/pp3ppp/2n1pn2/3p4/3P4/2NBPN2/PP3PPP/R2Q1RK1 w - - 0 11",
        "8/8/4k3/8/2p5/2K5/8/3R4 w - - 0 1",
        "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1",
        "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
    };

    public static void Loop()
    {
        var pos = new Position();
        pos.SetFen(Position.StartFen);
        var searcher = new Searcher();
        Thread? worker = null;
        long moveOverhead = 20;
        int tm = 1, tmDiv = 50, tmHard = 500;

        void StopSearch()
        {
            if (worker is { IsAlive: true })
            {
                searcher.Stop = true;
                worker.Join();
            }
        }

        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            var t = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;

            switch (t[0])
            {
                case "uci":
                    Send($"id name {Name} {Version}");
                    Send($"id author {Author}");
                    Send("option name Hash type spin default 64 min 1 max 4096");
                    Send("option name Threads type spin default 1 min 1 max 256");
                    Send("option name MoveOverhead type spin default 20 min 0 max 5000");
                    Send("option name LmrDiv type spin default 225 min 50 max 1000");
                    Send("option name LmrStart type spin default 5 min 2 max 20");
                    Send("option name LmrHist type spin default 2500 min 100 max 100000");
                    Send("option name RfpMargin type spin default 90 min 0 max 1000");
                    Send("option name FutMargin type spin default 110 min 0 max 1000");
                    Send("option name Tm type spin default 1 min 0 max 1");
                    Send("option name TmDiv type spin default 50 min 5 max 100");
                    Send("option name TmHard type spin default 500 min 100 max 1000");
                    Send("option name IirDepth type spin default 4 min 2 max 1000");
                    Send("option name SoftTime type spin default 100 min 10 max 100");
                    Send("option name EvalFile type string default <empty>");
                    Send("option name SyzygyPath type string default <empty>");
                    Send("uciok");
                    break;

                case "setoption":
                    // setoption name EvalFile value <ruta> (buit = avaluació PeSTO)
                    if (t.Length >= 3 && t[1] == "name" && t[2].Equals("EvalFile", StringComparison.OrdinalIgnoreCase))
                    {
                        StopSearch();
                        string file = t.Length > 4 && t[3] == "value" ? string.Join(' ', t[4..]) : "";
                        if (file.Length == 0 || file == "<empty>") Nnue.LoadEmbedded();
                        else if (file.Equals("none", StringComparison.OrdinalIgnoreCase) || file.Equals("pesto", StringComparison.OrdinalIgnoreCase)) Nnue.Unload();
                        else if (!Nnue.Load(file, out string err)) Send($"info string no es pot carregar la xarxa: {err}");
                        pos.RefreshAccumulator();
                        break;
                    }
                    if (t.Length >= 3 && t[1] == "name" && t[2].Equals("SyzygyPath", StringComparison.OrdinalIgnoreCase))
                    {
                        StopSearch();
                        string tbPath = t.Length > 4 && t[3] == "value" ? string.Join(' ', t[4..]) : "";
                        Syzygy.Init(tbPath, out string tbMsg);
                        Send($"info string {tbMsg}");
                        break;
                    }
                    // setoption name Hash value <mb>
                    if (t.Length >= 5 && t[1] == "name" && t[3] == "value" && int.TryParse(t[4], out int ov))
                    {
                        StopSearch();
                        if (t[2].Equals("Hash", StringComparison.OrdinalIgnoreCase)) searcher.SetHash(Math.Clamp(ov, 1, 4096));
                        else if (t[2].Equals("Threads", StringComparison.OrdinalIgnoreCase)) searcher.SetThreads(ov);
                        else if (t[2].Equals("MoveOverhead", StringComparison.OrdinalIgnoreCase)) moveOverhead = Math.Clamp(ov, 0, 5000);
                        else if (t[2].Equals("Tm", StringComparison.OrdinalIgnoreCase)) tm = Math.Clamp(ov, 0, 1);
                        else if (t[2].Equals("TmDiv", StringComparison.OrdinalIgnoreCase)) tmDiv = Math.Clamp(ov, 5, 100);
                        else if (t[2].Equals("TmHard", StringComparison.OrdinalIgnoreCase)) tmHard = Math.Clamp(ov, 100, 1000);
                        else searcher.SetOption(t[2], ov);
                    }
                    break;

                case "isready":
                    Send("readyok");
                    break;

                case "ucinewgame":
                    StopSearch();
                    pos.SetFen(Position.StartFen);
                    searcher.NewGame();
                    break;

                case "position":
                    StopSearch();
                    ParsePosition(pos, t);
                    break;

                case "go":
                    StopSearch();
                    StartSearch(pos.Clone(), t, searcher, moveOverhead, tm, tmDiv, tmHard, out worker);
                    break;

                case "stop":
                    searcher.Stop = true;
                    break;

                case "perft":
                case "divide":
                {
                    int depth = t.Length > 1 ? int.Parse(t[1]) : 1;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    ulong nodes = t[0] == "divide" ? Perft.Divide(pos, depth, Send) : Perft.Run(pos, depth);
                    Send($"nodes {nodes} time {sw.ElapsedMilliseconds}");
                    break;
                }

                case "bench":
                {
                    // Cerca de profunditat fixa sobre posicions fixes: el total de nodes és la signatura de la versió
                    // (determinista amb Threads=1).
                    StopSearch();
                    int bdepth = t.Length > 1 ? int.Parse(t[1]) : 12;
                    long total = 0;
                    var bsw = System.Diagnostics.Stopwatch.StartNew();
                    var bp = new Position();
                    foreach (var fen in BenchFens)
                    {
                        searcher.NewGame();
                        bp.SetFen(fen);
                        searcher.Think(bp, bdepth, 0, null);
                        total += searcher.Nodes;
                    }
                    long bms = Math.Max(1, bsw.ElapsedMilliseconds);
                    Send($"{total} nodes {total * 1000 / bms} nps");
                    break;
                }

                case "tbprobe":
                    Send(Syzygy.ProbeWdl(pos, out var tbw) ? $"tbprobe wdl {(int)tbw} ({tbw})" : "tbprobe no disponible");
                    break;

                case "tbcheck":
                {
                    // tbcheck <fitxer>: compara el WDL d'Ilerda amb el de les linies "FEN | wdl | dtz" (tools/syzygy/oracle.py)
                    int tot = 0, bad = 0, nope = 0;
                    var cp = new Position();
                    foreach (var l in File.ReadLines(string.Join(' ', t[1..])))
                    {
                        var f = l.Split('|');
                        if (f.Length < 2) continue;
                        cp.SetFen(f[0].Trim());
                        int want = int.Parse(f[1].Trim());
                        tot++;
                        if (!Syzygy.ProbeWdl(cp, out var got)) { nope++; continue; }
                        if ((int)got != want && ++bad <= 5) Send($"diferent: {f[0].Trim()} oracle {want} nostre {(int)got}");
                    }
                    Send($"tbcheck {tot} posicions, {bad} diferents, {nope} sense resposta");
                    break;
                }

                case "d":
                    Send(pos.ToString());
                    break;

                case "eval":
                    Send($"eval {Eval.Evaluate(pos)} cp (bàndol que juga){(Nnue.Loaded ? " [NNUE]" : " [PeSTO]")}");
                    break;

                case "quit":
                    StopSearch();
                    return;
            }
        }
        StopSearch();
    }

    static void ParsePosition(Position pos, string[] t)
    {
        int i = 1;
        if (i < t.Length && t[i] == "startpos")
        {
            pos.SetFen(Position.StartFen);
            i++;
        }
        else if (i < t.Length && t[i] == "fen")
        {
            i++;
            var fen = new List<string>();
            while (i < t.Length && t[i] != "moves") fen.Add(t[i++]);
            pos.SetFen(string.Join(' ', fen));
        }

        if (i < t.Length && t[i] == "moves")
        {
            for (i++; i < t.Length; i++) ApplyUciMove(pos, t[i]);
        }
        pos.ClearHistory();
    }

    public static bool ApplyUciMove(Position pos, string uci)
    {
        Span<int> moves = stackalloc int[320];
        int n = MoveGen.Generate(pos, moves);
        for (int i = 0; i < n; i++)
        {
            if (MoveGen.ToUci(moves[i]) != uci) continue;
            return pos.MakeMove(moves[i]);
        }
        return false;
    }

    static long ArgLong(string[] t, ref int i) =>
        i + 1 < t.Length && long.TryParse(t[i + 1], out long v) ? (i++, v).Item2 : 0;

    static void StartSearch(Position pos, string[] t, Searcher searcher, long moveOverhead, int tm, int tmDiv, int tmHard, out Thread worker)
    {
        long wtime = 0, btime = 0, winc = 0, binc = 0, movetime = 0;
        int movestogo = 0, depth = 64;
        bool infinite = false, hasTime = false;

        for (int i = 1; i < t.Length; i++)
        {
            switch (t[i])
            {
                case "wtime": wtime = ArgLong(t, ref i); hasTime = true; break;
                case "btime": btime = ArgLong(t, ref i); hasTime = true; break;
                case "winc": winc = ArgLong(t, ref i); break;
                case "binc": binc = ArgLong(t, ref i); break;
                case "movestogo": movestogo = (int)ArgLong(t, ref i); break;
                case "movetime": movetime = ArgLong(t, ref i); break;
                case "depth": depth = (int)Math.Clamp(ArgLong(t, ref i), 1, 64); break;
                case "infinite": infinite = true; break;
            }
        }

        long limit = 0, soft = 0;
        if (movetime > 0) limit = Math.Max(1, movetime - moveOverhead);
        else if (hasTime && !infinite)
        {
            long remaining = Math.Max(0, (pos.Side == 0 ? wtime : btime) - moveOverhead);
            long inc = pos.Side == 0 ? winc : binc;
            if (tm == 0)
            {
                // Gestió antiga (fins a la 0.13.1): un sol límit, que s'esgota gairebé sempre.
                int divisor = movestogo > 0 ? Math.Min(movestogo, 40) : 30;
                limit = remaining / divisor + inc * 3 / 4;
            }
            else
            {
                // Temps òptim (tou) i límit dur: la cerca decideix entre tots dos segons l'estabilitat.
                int divisor = movestogo > 0 ? Math.Min(movestogo, 40) : tmDiv;
                soft = remaining / divisor + inc * 3 / 4;
                limit = soft * tmHard / 100;
            }
            // Mai més d'un terç del temps que queda, i sempre un mínim perquè hi hagi jugada.
            limit = Math.Min(limit, remaining / 3);
            limit = Math.Max(limit, 5);
            soft = Math.Min(soft, limit);
        }

        // Si "stop" arriba abans que el fil comenci, no es perdi: es restableix aquí, no dins de Think.
        searcher.Stop = false;
        worker = new Thread(() =>
        {
            int best = searcher.Think(pos, depth, limit, Send, soft);
            if (infinite)
            {
                while (!searcher.Stop) Thread.Sleep(5);
            }
            Send($"bestmove {MoveGen.ToUci(best)}");
        }, 16 * 1024 * 1024);
        worker.Start();
    }
}
