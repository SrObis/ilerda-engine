using System.Numerics;
using System.Text;

namespace Ilerda;

/// <summary>
/// Autojoc amb límit de nodes per generar dades d'entrenament (Texel / NNUE).
/// Cada línia del fitxer: "FEN | puntuació (cp, des de les blanques) | resultat (1.0 blanques, 0.5 taules, 0.0 negres)".
/// Es desen només les posicions tranquil·les (sense escac, i la millor jugada no és captura ni promoció).
/// </summary>
public static class DataGen
{
    const int RandomPlies = 8;      // jugades aleatòries inicials per diversificar les obertures
    const int MaxOpeningScore = 400; // descarta obertures aleatòries ja decidides
    const int WinScore = 2000, WinPlies = 6;
    const int DrawScore = 8, DrawPlies = 12, DrawAfterPly = 80;

    public static void Run(long games, long nodes, string path, int threads, int seed, string? bookPath = null)
    {
        _book = bookPath == null ? null : LoadBook(bookPath);
        if (bookPath != null && _book!.Length == 0) { Console.WriteLine($"datagen: llibre buit o il·legible: {bookPath}"); return; }
        threads = Math.Max(1, threads);
        long started = 0, finished = 0, positions = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var outLock = new object();
        using var writer = new StreamWriter(path, append: true, new UTF8Encoding(false));
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Volatile.Write(ref _quit, 1); };

        Console.WriteLine($"datagen: {(games == 0 ? "sense límit" : games)} partides, {nodes} nodes, {threads} fils, fitxer {path}, obertures {(_book == null ? "aleatòries" : _book.Length + " del llibre")}");

        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            var rng = new Random(seed + t * 7919);
            workers[t] = new Thread(() =>
            {
                var searcher = new Searcher { NodeLimit = nodes };
                searcher.SetHash(16);
                var buffer = new List<string>();
                while (Volatile.Read(ref _quit) == 0)
                {
                    if (games > 0 && Interlocked.Increment(ref started) > games) break;
                    if (games == 0) Interlocked.Increment(ref started);

                    buffer.Clear();
                    double result = PlayGame(searcher, rng, buffer);
                    if (result < 0) continue; // obertura descartada
                    string res = result.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                    lock (outLock)
                    {
                        foreach (var l in buffer) writer.WriteLine($"{l} | {res}");
                        writer.Flush();
                    }
                    long f = Interlocked.Increment(ref finished);
                    long p = Interlocked.Add(ref positions, buffer.Count);
                    if (f % 20 == 0)
                        Console.WriteLine($"{f} partides, {p} posicions, {sw.Elapsed.TotalMinutes:0.0} min, {p / Math.Max(1, sw.Elapsed.TotalSeconds):0} pos/s");
                }
            }, 16 * 1024 * 1024);
            workers[t].Start();
        }
        foreach (var w in workers) w.Join();
        Console.WriteLine($"datagen acabat: {finished} partides, {positions} posicions, {sw.Elapsed.TotalMinutes:0.0} min");
    }

    static int _quit;
    static string[]? _book;

    /// <summary>Llegeix un llibre d'obertures: un FEN o EPD per línia (només es fan servir els 4 primers camps).</summary>
    static string[] LoadBook(string path)
    {
        var list = new List<string>();
        var probe = new Position();
        foreach (var raw in File.ReadLines(path))
        {
            var f = raw.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 4 || !f[0].Contains('/')) continue;
            string fen = $"{f[0]} {f[1]} {f[2]} {f[3]} 0 1";
            try { probe.SetFen(fen); } catch { continue; }
            list.Add(fen);
        }
        return list.ToArray();
    }

    /// <summary>Juga una partida. Afegeix a buffer "FEN | puntuació". Retorna el resultat o -1 si es descarta.</summary>
    static double PlayGame(Searcher s, Random rng, List<string> buffer)
    {
        var pos = new Position();
        pos.SetFen(_book == null ? Position.StartFen : _book[rng.Next(_book.Length)]);
        s.NewGame();

        // Jugades aleatòries (2-4 després d'una obertura del llibre, perquè no surtin partides idèntiques).
        Span<int> moves = stackalloc int[320];
        Span<int> ok = stackalloc int[320];
        int randomPlies = _book == null ? RandomPlies + rng.Next(2) : 2 + rng.Next(3);
        for (int i = 0; i < randomPlies; i++)
        {
            int n = MoveGen.Generate(pos, moves);
            int legal = 0;
            for (int k = 0; k < n; k++)
                if (pos.MakeMove(moves[k])) { pos.UnmakeMove(); ok[legal++] = moves[k]; }
            if (legal == 0) return -1;
            pos.MakeMove(ok[rng.Next(legal)]);
        }

        int winRun = 0, drawRun = 0, ply = 0;
        int winSide = -1;
        while (true)
        {
            // Final de partida per regles.
            if (NoLegalMoves(pos))
                return pos.InCheck() ? (pos.Side == 0 ? 0.0 : 1.0) : 0.5;
            if (pos.Halfmove >= 100 || pos.IsRepetition() || InsufficientMaterial(pos)) return 0.5;

            int best = s.Think(pos, 64, 0, null);
            int score = s.LastScore; // des del bàndol que juga
            int white = pos.Side == 0 ? score : -score;

            if (ply == 0 && Math.Abs(white) > MaxOpeningScore) return -1;

            if (Math.Abs(score) > 30000 - 100)
                return white > 0 ? 1.0 : 0.0; // mat trobat

            // Adjudicació.
            if (Math.Abs(white) >= WinScore)
            {
                int side = white > 0 ? 0 : 1;
                winRun = side == winSide ? winRun + 1 : 1;
                winSide = side;
                if (winRun >= WinPlies) return side == 0 ? 1.0 : 0.0;
            }
            else winRun = 0;
            drawRun = Math.Abs(white) <= DrawScore && ply >= DrawAfterPly ? drawRun + 1 : 0;
            if (drawRun >= DrawPlies) return 0.5;

            bool quiet = pos.Board[MoveGen.To(best)] == -1 && MoveGen.Promo(best) == 0 && MoveGen.Flag(best) != MoveGen.FlagEp;
            if (quiet && !pos.InCheck() && ply >= 0)
                buffer.Add($"{Fen(pos)} | {white}");

            pos.MakeMove(best);
            ply++;
            if (ply > 600) return 0.5;
        }
    }

    static bool NoLegalMoves(Position pos)
    {
        Span<int> moves = stackalloc int[320];
        int n = MoveGen.Generate(pos, moves);
        for (int i = 0; i < n; i++)
            if (pos.MakeMove(moves[i])) { pos.UnmakeMove(); return false; }
        return true;
    }

    static bool InsufficientMaterial(Position p)
    {
        for (int c = 0; c < 2; c++)
            if ((p.Bb[c * 6 + Pc.Pawn] | p.Bb[c * 6 + Pc.Rook] | p.Bb[c * 6 + Pc.Queen]) != 0) return false;
        int minors = 0;
        for (int c = 0; c < 2; c++)
            minors += BitOperations.PopCount(p.Bb[c * 6 + Pc.Knight] | p.Bb[c * 6 + Pc.Bishop]);
        return minors <= 1;
    }

    public static string Fen(Position p)
    {
        var sb = new StringBuilder();
        for (int r = 7; r >= 0; r--)
        {
            int empty = 0;
            for (int f = 0; f < 8; f++)
            {
                int pc = p.Board[r * 8 + f];
                if (pc == -1) { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append("PNBRQKpnbrqk"[pc]);
            }
            if (empty > 0) sb.Append(empty);
            if (r > 0) sb.Append('/');
        }
        sb.Append(p.Side == 0 ? " w " : " b ");
        if (p.Castling == 0) sb.Append('-');
        else
        {
            if ((p.Castling & 1) != 0) sb.Append('K');
            if ((p.Castling & 2) != 0) sb.Append('Q');
            if ((p.Castling & 4) != 0) sb.Append('k');
            if ((p.Castling & 8) != 0) sb.Append('q');
        }
        sb.Append(' ').Append(p.Ep == -1 ? "-" : MoveGen.SquareName(p.Ep));
        sb.Append(' ').Append(p.Halfmove).Append(' ').Append(p.Fullmove);
        return sb.ToString();
    }
}
