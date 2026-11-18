// Prepara les dades de datagen per entrenar: valida, treu repetides, filtra (opcional) i barreja.
// Entrada i sortida en el format de text de bullet: "FEN | puntuació | resultat" (tot des de les blanques).
//
// Ús: prepara <prefix de sortida> <trossos> [--max-score N] [--min-jugada N] <entrada1> <entrada2> ...
//   Escriu <prefix>-00.txt, <prefix>-01.txt, ... Cada tros és una mostra aleatòria uniforme i ja barrejada,
//   així que es pot entrenar amb un tros sol, amb uns quants o amb tots.
//   --max-score N   descarta les posicions amb |puntuació| > N
//   --min-jugada N  descarta les posicions amb número de jugada (últim camp del FEN) < N
//
// Com funciona (no cap tot a la memòria): primera passada, cada línia va a un de 60 cubells segons el hash de
// les 4 primeres parts del FEN (peces, torn, enroc, al pas), de manera que les repetides cauen al mateix cubell.
// Segona passada, cada cubell es carrega sencer, es treuen les repetides (es queda la primera), es barreja
// i s'afegeix al tros que li toca. Com que el hash reparteix a l'atzar, cada cubell és una mostra uniforme.
using System.Diagnostics;
using System.Text;

const int Buckets = 60;

if (args.Length < 3 || !int.TryParse(args[1], out int parts) || parts < 1 || Buckets % parts != 0)
{
    Console.Error.WriteLine("Ús: prepara <prefix de sortida> <trossos (divisor de 60)> [--max-score N] [--min-jugada N] <entrades...>");
    return 1;
}
string prefix = args[0];
int maxScore = int.MaxValue, minMove = 0;
var inputs = new List<string>();
for (int i = 2; i < args.Length; i++)
{
    if (args[i] == "--max-score") maxScore = int.Parse(args[++i]);
    else if (args[i] == "--min-jugada") minMove = int.Parse(args[++i]);
    else inputs.Add(args[i]);
}

var sw = Stopwatch.StartNew();
string TmpName(int b) => $"{prefix}.tmp{b:00}";
var tmp = new FileStream[Buckets];
for (int b = 0; b < Buckets; b++)
    tmp[b] = new FileStream(TmpName(b), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);

long total = 0, bad = 0, byScore = 0, byMove = 0, sent = 0;
long[] results = new long[3];
// Histograma de |puntuació|, per decidir llindars: <100, <300, <600, <1000, <1500, <2000, <2500, <5000, <20000, la resta.
int[] edges = { 100, 300, 600, 1000, 1500, 2000, 2500, 5000, 20000 };
long[] hist = new long[edges.Length + 1];
byte[] nl = { (byte)'\n' };

var buf = new byte[8 << 20];
foreach (string path in inputs)
{
    long before = total;
    using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
    int have = 0;
    while (true)
    {
        int n = f.Read(buf, have, buf.Length - have);
        if (n == 0) break;
        have += n;
        int start = 0;
        while (true)
        {
            int rel = buf.AsSpan(start, have - start).IndexOf((byte)'\n');
            if (rel < 0) break;
            var line = buf.AsSpan(start, rel);
            start += rel + 1;
            if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
            total++;
            if (!Parse(line, out ulong key, out int score, out int res, out int move)) { bad++; continue; }
            results[res]++;
            int a = Math.Abs(score), h = 0;
            while (h < edges.Length && a >= edges[h]) h++;
            hist[h]++;
            if (a > maxScore) { byScore++; continue; }
            if (move < minMove) { byMove++; continue; }
            var o = tmp[(int)(key % Buckets)];
            o.Write(line);
            o.Write(nl);
            sent++;
        }
        // El que queda és una línia a mitges: es mou al principi. Si el fitxer acaba sense salt de línia, es perd (línia tallada).
        buf.AsSpan(start, have - start).CopyTo(buf);
        have -= start;
        if (have == buf.Length) throw new InvalidDataException("línia de més de 8 MB a " + path);
    }
    if (have > 0) { total++; bad++; }
    Console.WriteLine($"{Path.GetFileName(path)}: {total - before:N0} línies ({sw.Elapsed.TotalMinutes:F1} min)");
}
foreach (var t in tmp) t.Dispose();

Console.WriteLine($"llegides {total:N0}, mal formades {bad:N0}, fora per puntuació {byScore:N0}, fora per jugada {byMove:N0}");
long valid = total - bad;
Console.WriteLine($"resultats: blanques {100.0 * results[2] / valid:F1} %, taules {100.0 * results[1] / valid:F1} %, negres {100.0 * results[0] / valid:F1} %");
for (int h = 0; h < hist.Length; h++)
{
    string label = h == 0 ? $"|p| < {edges[0]}" : h == edges.Length ? $"|p| >= {edges[^1]}" : $"{edges[h - 1]} <= |p| < {edges[h]}";
    Console.WriteLine($"  {label,-22} {hist[h],14:N0}  {100.0 * hist[h] / valid:F2} %");
}

// Segona passada: repetides fora i barreja, cubell a cubell.
var rng = new Random(20261116);
long kept = 0, dup = 0;
int perPart = Buckets / parts;
for (int p = 0; p < parts; p++)
{
    string outName = $"{prefix}-{p:00}.txt";
    long inPart = 0;
    using (var o = new FileStream(outName, FileMode.Create, FileAccess.Write, FileShare.None, 4 << 20))
    {
        for (int b = p * perPart; b < (p + 1) * perPart; b++)
        {
            byte[] data = File.ReadAllBytes(TmpName(b));
            var offs = new List<int>(data.Length / 60);
            var seen = new HashSet<ulong>(data.Length / 60);
            int pos = 0;
            while (pos < data.Length)
            {
                int rel = data.AsSpan(pos).IndexOf((byte)'\n');
                var line = data.AsSpan(pos, rel);
                Parse(line, out ulong key, out _, out _, out _);
                if (seen.Add(key)) offs.Add(pos); else dup++;
                pos += rel + 1;
            }
            seen = null!;
            for (int i = offs.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (offs[i], offs[j]) = (offs[j], offs[i]);
            }
            foreach (int s in offs)
            {
                int rel = data.AsSpan(s).IndexOf((byte)'\n');
                o.Write(data, s, rel + 1);
            }
            inPart += offs.Count;
            File.Delete(TmpName(b));
        }
    }
    kept += inPart;
    Console.WriteLine($"{Path.GetFileName(outName)}: {inPart:N0} posicions ({sw.Elapsed.TotalMinutes:F1} min)");
}
Console.WriteLine($"enviades a cubells {sent:N0}, repetides {dup:N0} ({100.0 * dup / Math.Max(1, sent):F2} %), finals {kept:N0}");
return 0;

// Valida "FEN | puntuació | resultat" i treu: hash de les 4 primeres parts del FEN, puntuació, resultat (0/1/2) i número de jugada.
static bool Parse(ReadOnlySpan<byte> line, out ulong key, out int score, out int res, out int move)
{
    key = 0; score = 0; res = 0; move = 0;
    ReadOnlySpan<byte> sep = " | "u8;
    int s1 = line.IndexOf(sep);
    if (s1 < 0) return false;
    var rest = line[(s1 + 3)..];
    int s2 = rest.IndexOf(sep);
    if (s2 < 0) return false;
    var fen = line[..s1];
    var sc = rest[..s2];
    var rs = rest[(s2 + 3)..];
    if (rs.IndexOf(sep) >= 0) return false;

    if (rs.SequenceEqual("1.0"u8)) res = 2;
    else if (rs.SequenceEqual("0.5"u8)) res = 1;
    else if (rs.SequenceEqual("0.0"u8)) res = 0;
    else return false;

    if (sc.Length == 0 || sc.Length > 7) return false;
    int i = 0;
    bool neg = sc[0] == (byte)'-';
    if (neg) i = 1;
    if (i == sc.Length) return false;
    for (; i < sc.Length; i++)
    {
        int d = sc[i] - '0';
        if ((uint)d > 9) return false;
        score = score * 10 + d;
    }
    if (neg) score = -score;

    // Les 4 primeres parts del FEN: fins al quart espai (o tot el FEN si només en té 4).
    int spaces = 0, end = fen.Length, lastSpace = -1;
    for (int k = 0; k < fen.Length; k++)
        if (fen[k] == (byte)' ')
        {
            spaces++;
            lastSpace = k;
            if (spaces == 4) end = k;
        }
    if (spaces < 3) return false;
    // 8 files al tauler: 7 barres a la primera part.
    int slashes = 0;
    for (int k = 0; k < fen.Length && fen[k] != (byte)' '; k++) if (fen[k] == (byte)'/') slashes++;
    if (slashes != 7) return false;
    if (spaces >= 5)
    {
        for (int k = lastSpace + 1; k < fen.Length; k++)
        {
            int d = fen[k] - '0';
            if ((uint)d > 9) return false;
            move = move * 10 + d;
        }
    }

    // FNV-1a de 64 bits amb una barreja final (els bits baixos de FNV sols reparteixen malament).
    ulong h = 14695981039346656037UL;
    for (int k = 0; k < end; k++) { h ^= fen[k]; h *= 1099511628211UL; }
    h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
    key = h;
    return true;
}
