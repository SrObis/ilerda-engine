using System.Text;

namespace Ilerda;

/// <summary>
/// Reetiqueta la puntuació d'un fitxer de dades de <c>datagen</c> ("FEN | puntuació | resultat") fent que el motor actual
/// cerqui cada posició amb un límit de nodes. El FEN i el resultat es mantenen; només canvia la puntuació (cp des de les blanques).
/// Com a <c>datagen</c>, es descarten les posicions on la cerca troba un mat o on la millor jugada és una captura, una promoció
/// o una captura al pas (ja no són tranquil·les per al motor nou). El fitxer de sortida conserva l'ordre de l'entrada.
/// <para>Mode selectiu (<see cref="Options"/>): només es tornen a cercar les posicions amb un nombre de peces dins d'un interval i,
/// si es vol, només les "sospitoses" (el motor es veia guanyant per almenys un llindar i la partida no es va guanyar); la resta de
/// línies passen tal com són. Amb taules Syzygy carregades, la cerca les sondeja i, a més, les posicions de 6 peces o menys
/// reben el resultat exacte de la taula (com fa <c>tbrelabel</c>).</para>
/// </summary>
public static class Relabel
{
    const int Chunk = 100_000;
    const int NewGameEvery = 512; // neteja les taules d'història cada tants posicions
    const int MaxScore = 3000;    // les puntuacions noves es limiten a aquest valor (les victòries de taula surten molt altes)

    /// <summary>Opcions del mode selectiu. Sense opcions (per defecte) es cerquen totes les posicions.</summary>
    public sealed class Options
    {
        public int MinPieces = 0, MaxPieces = 32;
        public int Suspicion = 0;      // > 0: només |puntuació| >= llindar a favor d'un bàndol que no va guanyar
        public bool Selective => MinPieces > 0 || MaxPieces < 32 || Suspicion > 0;
    }

    public static int Run(string input, string output, long nodes, int threads, long maxLines, long skip = 0, Options? opt = null)
    {
        opt ??= new Options();
        threads = Math.Max(1, threads);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long total = 0, kept = 0, droppedMate = 0, droppedNoisy = 0, droppedBad = 0, passed = 0, tbChanged = 0, cooled = 0;
        double sumAbsDiff = 0;
        long cmp = 0;

        Console.WriteLine($"relabel: {nodes} nodes, {threads} fils, {input} -> {output}" + (maxLines > 0 ? $", màxim {maxLines} línies" : "") +
                          (opt.Selective ? $", només de {opt.MinPieces} a {opt.MaxPieces} peces" + (opt.Suspicion > 0 ? $" i sospitoses (>= {opt.Suspicion} cp sense victòria)" : "") : "") +
                          (Syzygy.Enabled ? $", Syzygy fins a {Syzygy.MaxCardinality} peces" : ""));
        using var reader = new StreamReader(input, Encoding.UTF8, false, 1 << 20);
        using var writer = new StreamWriter(output, false, new UTF8Encoding(false), 1 << 20);

        for (long k = 0; k < skip; k++)
            if (reader.ReadLine() == null) { Console.WriteLine("relabel: el fitxer té menys línies que les que cal saltar"); return 1; }
        if (skip > 0) Console.WriteLine($"relabel: saltades {skip} línies");

        var lines = new string[Chunk];
        var outLines = new string?[Chunk];
        var oldScore = new int[Chunk];
        var newScore = new int[Chunk];
        var status = new byte[Chunk]; // 0 = cercada i mantinguda, 1 = mat, 2 = no tranquil·la, 3 = mal formada, 4 = no seleccionada (passa igual), 5 = resultat canviat per la taula

        while (maxLines <= 0 || total < maxLines)
        {
            int n = 0;
            string? l;
            while (n < Chunk && (maxLines <= 0 || total + n < maxLines) && (l = reader.ReadLine()) != null) lines[n++] = l;
            if (n == 0) break;

            int next = 0;
            var workers = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                workers[t] = new Thread(() =>
                {
                    var searcher = new Searcher { NodeLimit = nodes };
                    searcher.SetHash(16);
                    var pos = new Position();
                    int done = 0;
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next) - 1;
                        if (i >= n) break;
                        if (done++ % NewGameEvery == 0) searcher.NewGame();
                        Process(searcher, pos, lines[i], i, outLines, oldScore, newScore, status, opt);
                    }
                }, 16 * 1024 * 1024);
                workers[t].Start();
            }
            foreach (var w in workers) w.Join();

            for (int i = 0; i < n; i++)
            {
                switch (status[i])
                {
                    case 0:
                        writer.WriteLine(outLines[i]);
                        kept++;
                        sumAbsDiff += Math.Abs(newScore[i] - oldScore[i]);
                        cmp++;
                        if (opt.Suspicion > 0 && Math.Abs(newScore[i]) < opt.Suspicion / 2) cooled++;
                        break;
                    case 4: writer.WriteLine(outLines[i]); passed++; break;
                    case 5: writer.WriteLine(outLines[i]); passed++; tbChanged++; break;
                    case 1: droppedMate++; break;
                    case 2: droppedNoisy++; break;
                    default: droppedBad++; break;
                }
            }
            total += n;
            double sec = Math.Max(1, sw.Elapsed.TotalSeconds);
            Console.WriteLine($"{total} línies, {kept} cercades i mantingudes, {passed} sense tocar, {sw.Elapsed.TotalMinutes:0.0} min, {total / sec:0} línies/s, |nova-vella| mitjana {(cmp > 0 ? sumAbsDiff / cmp : 0):0.0} cp");
        }

        Console.WriteLine($"relabel acabat: {total} línies, {kept} cercades i mantingudes, {passed} sense tocar ({tbChanged} amb el resultat canviat per la taula), {cooled} sospitoses que baixen a menys de la meitat del llindar; descartades: {droppedMate} per mat, {droppedNoisy} no tranquil·les, {droppedBad} mal formades; " +
                          $"|nova-vella| mitjana {(cmp > 0 ? sumAbsDiff / cmp : 0):0.0} cp, {sw.Elapsed.TotalMinutes:0.0} min");
        return 0;
    }

    static void Process(Searcher s, Position pos, string line, int i, string?[] outLines, int[] oldScore, int[] newScore, byte[] status, Options opt)
    {
        int bar = line.IndexOf('|');
        int lastBar = line.LastIndexOf('|');
        if (bar < 0 || lastBar == bar) { status[i] = 3; return; }
        string fen = line.Substring(0, bar).Trim();
        string res = line.Substring(lastBar + 1).Trim();
        if (!int.TryParse(line.AsSpan(bar + 1, lastBar - bar - 1).Trim(), out int old)) { status[i] = 3; return; }
        int pieces = 0;
        foreach (char c in fen) { if (c == ' ') break; if (char.IsLetter(c)) pieces++; }

        // Posicions que la taula resol i que no toca cercar: resultat exacte, sense cerca.
        if (Syzygy.Enabled && pieces <= Syzygy.MaxCardinality && pieces < opt.MinPieces)
        {
            try { pos.SetFen(fen); } catch { status[i] = 3; return; }
            outLines[i] = line; status[i] = 4;
            if (Syzygy.ProbeWdl(pos, out Wdl w))
            {
                int whiteWdl = pos.Side == 0 ? (int)w : -(int)w;
                string newRes = whiteWdl == 2 ? "1.0" : whiteWdl == -2 ? "0.0" : "0.5";
                if (newRes != res) { outLines[i] = $"{fen} | {old} | {newRes}"; status[i] = 5; }
            }
            return;
        }
        if (opt.Selective)
        {
            bool sel = pieces >= opt.MinPieces && pieces <= opt.MaxPieces;
            if (sel && opt.Suspicion > 0)
                sel = (old >= opt.Suspicion && res != "1.0") || (old <= -opt.Suspicion && res != "0.0");
            if (!sel) { outLines[i] = line; status[i] = 4; return; }
        }
        try { pos.SetFen(fen); } catch { status[i] = 3; return; }

        int best = s.Think(pos, 64, 0, null);
        int score = s.LastScore; // des del bàndol que juga
        if (best == 0 || Math.Abs(score) > 30000 - 100) { status[i] = 1; return; }
        bool quiet = pos.Board[MoveGen.To(best)] == -1 && MoveGen.Promo(best) == 0 && MoveGen.Flag(best) != MoveGen.FlagEp;
        if (!quiet) { status[i] = 2; return; }

        int white = Math.Clamp(pos.Side == 0 ? score : -score, -MaxScore, MaxScore);
        outLines[i] = $"{fen} | {white} | {res}";
        oldScore[i] = old;
        newScore[i] = white;
        status[i] = 0;
    }
}
