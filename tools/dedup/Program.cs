using System.Globalization;
using System.Text;

// Ús: dedup <entrada> <sortida> [bits]
//   Agrupa les posicions repetides d'un fitxer de datagen ("FEN | puntuació | resultat").
//   Sortida: "FEN (4 camps) | puntuació mitjana | victòries blanques | taules | victòries negres"
//   La posició es compara pels 4 primers camps del FEN (peces, torn, enroc, al pas).
//   bits: la taula té 2^bits entrades (27 per defecte, ~3,8 GB; ha de ser > 1,3 x posicions úniques).
if (args.Length < 2)
{
    Console.WriteLine("Ús: dedup <entrada> <sortida> [bits]");
    return 1;
}

string input = args[0], output = args[1];
int bits = args.Length > 2 ? int.Parse(args[2]) : 27;
long cap = 1L << bits, mask = cap - 1;
var keys = new ulong[cap];   // 0 = lliure
var wins = new uint[cap];    // bit 31: ja escrita a la sortida
var draws = new uint[cap];
var losses = new uint[cap];
var scoreSum = new long[cap];
long unique = 0, total = 0, bad = 0;
const uint Done = 1u << 31;
var sw = System.Diagnostics.Stopwatch.StartNew();

static bool Parse(string line, out ulong key, out int score, out int res)
{
    key = 0; score = 0; res = 0;
    int a = line.IndexOf(" | ", StringComparison.Ordinal);
    if (a < 0) return false;
    int b = line.IndexOf(" | ", a + 3, StringComparison.Ordinal);
    if (b < 0) return false;
    // Primers 4 camps del FEN.
    int sp = 0, end = -1;
    for (int i = 0; i < a; i++)
        if (line[i] == ' ' && ++sp == 4) { end = i; break; }
    if (end < 0) end = a; // FEN amb menys camps: es fa servir sencer
    ulong h = 14695981039346656037UL; // FNV-1a de 64 bits
    for (int i = 0; i < end; i++) { h ^= line[i]; h *= 1099511628211UL; }
    h ^= h >> 32; h *= 0x9E3779B97F4A7C15UL; h ^= h >> 29; // barreja final
    key = h == 0 ? 1 : h;
    if (!int.TryParse(line.AsSpan(a + 3, b - a - 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out score)) return false;
    var r = line.AsSpan(b + 3).Trim();
    res = r.SequenceEqual("1.0") ? 2 : r.SequenceEqual("0.5") ? 1 : r.SequenceEqual("0.0") ? 0 : -1;
    return res >= 0;
}

long Slot(ulong key)
{
    long i = (long)(key & (ulong)mask);
    while (keys[i] != 0 && keys[i] != key) i = (i + 1) & mask;
    return i;
}

// Passada 1: comptar.
using (var r = new StreamReader(input, Encoding.UTF8, false, 1 << 20))
{
    string? line;
    while ((line = r.ReadLine()) != null)
    {
        total++;
        if (!Parse(line, out ulong key, out int score, out int res)) { bad++; continue; }
        long s = Slot(key);
        if (keys[s] == 0)
        {
            keys[s] = key;
            if (++unique > cap * 85 / 100)
            {
                Console.WriteLine($"Taula plena ({unique} posicions úniques de {cap}). Repeteix amb més bits (ara {bits}).");
                return 2;
            }
        }
        scoreSum[s] += score;
        if (res == 2) wins[s]++; else if (res == 1) draws[s]++; else losses[s]++;
        if (total % 10_000_000 == 0)
            Console.WriteLine($"passada 1: {total} línies, {unique} úniques, {sw.Elapsed.TotalMinutes:0.0} min");
    }
}
Console.WriteLine($"passada 1 acabada: {total} línies, {unique} úniques, {bad} línies no vàlides, {sw.Elapsed.TotalMinutes:0.0} min");

// Passada 2: escriure la primera aparició de cada posició amb les estadístiques agrupades.
long written = 0;
using (var r = new StreamReader(input, Encoding.UTF8, false, 1 << 20))
using (var w = new StreamWriter(output, false, new UTF8Encoding(false), 1 << 20))
{
    string? line;
    var sb = new StringBuilder();
    while ((line = r.ReadLine()) != null)
    {
        if (!Parse(line, out ulong key, out _, out _)) continue;
        long s = Slot(key);
        if ((wins[s] & Done) != 0) continue;
        uint nw = wins[s], nd = draws[s], nl = losses[s];
        wins[s] |= Done;
        int sp = 0, end = line.IndexOf(" | ", StringComparison.Ordinal);
        for (int i = 0; i < end; i++)
            if (line[i] == ' ' && ++sp == 4) { end = i; break; }
        long n = (long)nw + nd + nl;
        sb.Clear().Append(line, 0, end).Append(" | ")
          .Append(Math.Round((double)scoreSum[s] / n).ToString("0", CultureInfo.InvariantCulture)).Append(" | ")
          .Append(nw).Append(" | ").Append(nd).Append(" | ").Append(nl);
        w.WriteLine(sb);
        if (++written % 10_000_000 == 0)
            Console.WriteLine($"passada 2: {written} posicions escrites, {sw.Elapsed.TotalMinutes:0.0} min");
    }
}
Console.WriteLine($"fet: {written} posicions úniques a {output} ({total} línies d'entrada), {sw.Elapsed.TotalMinutes:0.0} min");
return 0;
