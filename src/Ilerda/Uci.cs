namespace Ilerda;

public static class Uci
{
    public const string Name = "Ilerda";
    public const string Version = "0.7.0";
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

    public static void Loop()
    {
        var pos = new Position();
        pos.SetFen(Position.StartFen);
        var searcher = new Searcher();
        Thread? worker = null;
        long moveOverhead = 0;

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
                    Send("option name MoveOverhead type spin default 0 min 0 max 5000");
                    Send("option name LmrDiv type spin default 225 min 50 max 1000");
                    Send("option name LmrStart type spin default 5 min 2 max 20");
                    Send("option name LmrHist type spin default 2500 min 100 max 100000");
                    Send("option name RfpMargin type spin default 90 min 0 max 1000");
                    Send("option name FutMargin type spin default 110 min 0 max 1000");
                    Send("option name SoftTime type spin default 100 min 10 max 100");
                    Send("uciok");
                    break;

                case "setoption":
                    // setoption name Hash value <mb>
                    if (t.Length >= 5 && t[1] == "name" && t[3] == "value" && int.TryParse(t[4], out int ov))
                    {
                        StopSearch();
                        if (t[2].Equals("Hash", StringComparison.OrdinalIgnoreCase)) searcher.SetHash(Math.Clamp(ov, 1, 4096));
                        else if (t[2].Equals("Threads", StringComparison.OrdinalIgnoreCase)) searcher.SetThreads(ov);
                        else if (t[2].Equals("MoveOverhead", StringComparison.OrdinalIgnoreCase)) moveOverhead = Math.Clamp(ov, 0, 5000);
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
                    StartSearch(pos.Clone(), t, searcher, moveOverhead, out worker);
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

                case "d":
                    Send(pos.ToString());
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

    static void StartSearch(Position pos, string[] t, Searcher searcher, long moveOverhead, out Thread worker)
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

        long limit = 0;
        if (movetime > 0) limit = Math.Max(1, movetime - moveOverhead);
        else if (hasTime && !infinite)
        {
            long remaining = Math.Max(0, (pos.Side == 0 ? wtime : btime) - moveOverhead);
            long inc = pos.Side == 0 ? winc : binc;
            int divisor = movestogo > 0 ? Math.Min(movestogo, 40) : 30;
            limit = remaining / divisor + inc * 3 / 4;
            // Mai més d'un terç del temps que queda, i sempre un mínim perquè hi hagi jugada.
            limit = Math.Min(limit, remaining / 3);
            limit = Math.Max(limit, 5);
        }

        // Si "stop" arriba abans que el fil comenci, no es perdi: es restableix aquí, no dins de Think.
        searcher.Stop = false;
        worker = new Thread(() =>
        {
            int best = searcher.Think(pos, depth, limit, Send);
            if (infinite)
            {
                while (!searcher.Stop) Thread.Sleep(5);
            }
            Send($"bestmove {MoveGen.ToUci(best)}");
        }, 16 * 1024 * 1024);
        worker.Start();
    }
}
