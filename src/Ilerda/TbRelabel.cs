using System.Text;

namespace Ilerda;

/// <summary>
/// Reetiqueta un fitxer de dades de <c>datagen</c> ("FEN | puntuació | resultat") amb el resultat exacte de les taules Syzygy
/// per a les posicions de 6 peces o menys. El resultat del joc (adjudicat o jugat amb errors) es substitueix pel WDL de la taula
/// (1,0 guanyen blanques, 0,5 taules, 0,0 guanyen negres; les victòries "maleïdes" per la regla dels 50 compten com a taules).
/// La puntuació no es toca. Les altres línies queden igual.
/// </summary>
public static class TbRelabel
{
    const int Chunk = 200_000;

    public static int Run(string input, string output, string tbPath, int threads)
    {
        if (!Syzygy.Init(tbPath, out string msg)) { Console.WriteLine($"tbrelabel: {msg}"); return 1; }
        Console.WriteLine($"tbrelabel: {msg}");
        threads = Math.Max(1, threads);

        long total = 0, probed = 0, changed = 0, toWin = 0, toDraw = 0, toLoss = 0, failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var reader = new StreamReader(input, Encoding.UTF8, false, 1 << 20);
        using var writer = new StreamWriter(output, false, new UTF8Encoding(false), 1 << 20);
        var opt = new ParallelOptions { MaxDegreeOfParallelism = threads };

        var lines = new string[Chunk];
        var outLines = new string[Chunk];
        var posPool = new System.Collections.Concurrent.ConcurrentBag<Position>();

        while (true)
        {
            int n = 0;
            string? l;
            while (n < Chunk && (l = reader.ReadLine()) != null) lines[n++] = l;
            if (n == 0) break;

            long cProbed = 0, cChanged = 0, cWin = 0, cDraw = 0, cLoss = 0, cFail = 0;
            Parallel.For(0, n, opt, () => new long[6], (i, _, acc) =>
            {
                string line = lines[i];
                outLines[i] = line;
                int bar = line.IndexOf('|');
                if (bar < 0) return acc;
                string fen = line.Substring(0, bar).Trim();
                if (CountPieces(fen) > 6) return acc;
                int lastBar = line.LastIndexOf('|');
                if (lastBar == bar) return acc;

                if (!posPool.TryTake(out var pos)) pos = new Position();
                try
                {
                    pos.SetFen(fen);
                    if (!Syzygy.ProbeWdl(pos, out Wdl w)) { acc[5]++; return acc; }
                    acc[0]++;
                    // des del bàndol que juga a les blanques
                    int whiteWdl = pos.Side == 0 ? (int)w : -(int)w;
                    double res = whiteWdl == 2 ? 1.0 : whiteWdl == -2 ? 0.0 : 0.5;
                    string newRes = res.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    string oldRes = line.Substring(lastBar + 1).Trim();
                    if (newRes != oldRes)
                    {
                        acc[1]++;
                        if (res == 1.0) acc[2]++; else if (res == 0.5) acc[3]++; else acc[4]++;
                        outLines[i] = line.Substring(0, lastBar + 1) + " " + newRes;
                    }
                }
                finally { posPool.Add(pos); }
                return acc;
            }, acc =>
            {
                Interlocked.Add(ref cProbed, acc[0]);
                Interlocked.Add(ref cChanged, acc[1]);
                Interlocked.Add(ref cWin, acc[2]);
                Interlocked.Add(ref cDraw, acc[3]);
                Interlocked.Add(ref cLoss, acc[4]);
                Interlocked.Add(ref cFail, acc[5]);
            });

            for (int i = 0; i < n; i++) writer.WriteLine(outLines[i]);
            total += n; probed += cProbed; changed += cChanged; toWin += cWin; toDraw += cDraw; toLoss += cLoss; failed += cFail;
            Console.WriteLine($"{total} línies, {probed} amb taula, {changed} canviades, {sw.Elapsed.TotalMinutes:0.0} min");
        }

        Console.WriteLine($"tbrelabel acabat: {total} línies, {probed} sondejades, {failed} sense taula; {changed} resultats canviats " +
                          $"(a victòria blanca {toWin}, a taules {toDraw}, a victòria negra {toLoss}), {sw.Elapsed.TotalMinutes:0.0} min");
        return 0;
    }

    /// <summary>Nombre de peces (reis inclosos) d'un FEN, sense construir la posició.</summary>
    static int CountPieces(string fen)
    {
        int n = 0;
        foreach (char c in fen)
        {
            if (c == ' ') break;
            if (char.IsLetter(c)) n++;
        }
        return n;
    }
}
