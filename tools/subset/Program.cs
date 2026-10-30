// Extreu un subconjunt aleatori de les posicions de tools/dedup i el convertix al format de text de bullet:
//   <FEN complet> | <puntuació en cp des de les blanques> | <resultat 1.0 / 0.5 / 0.0 des de les blanques>
// Ús: subset <entrada> <sortida> <quantes> [llavor]
using System.Globalization;
using System.Text;

if (args.Length < 3 || !long.TryParse(args[2], out long wanted))
{
    Console.Error.WriteLine("Ús: subset <entrada> <sortida> <quantes> [llavor]");
    return 1;
}
int seed = args.Length > 3 ? int.Parse(args[3]) : 12345;

// Primera passada: comptar línies, per saber quina fracció cal agafar.
long total = 0;
using (var r = new StreamReader(args[0], Encoding.UTF8, false, 1 << 20))
    while (r.ReadLine() != null) total++;
double p = Math.Min(1.0, (double)wanted / total);
Console.WriteLine($"{total:N0} línies, es manté el {p:P2}");

var rng = new Random(seed);
long kept = 0, bad = 0;
using (var r = new StreamReader(args[0], Encoding.UTF8, false, 1 << 20))
using (var w = new StreamWriter(args[1], false, new UTF8Encoding(false), 1 << 20))
{
    string? line;
    while ((line = r.ReadLine()) != null)
    {
        if (p < 1.0 && rng.NextDouble() >= p) continue;
        var f = line.Split('|', StringSplitOptions.TrimEntries);
        if (f.Length != 5
            || !int.TryParse(f[1], out int score)
            || !int.TryParse(f[2], out int wins) || !int.TryParse(f[3], out int draws) || !int.TryParse(f[4], out int losses)
            || wins + draws + losses == 0)
        {
            bad++;
            continue;
        }
        double result = (wins + 0.5 * draws) / (wins + draws + losses);
        w.Write(f[0]);
        w.Write(" 0 1 | ");
        w.Write(score.ToString(CultureInfo.InvariantCulture));
        w.Write(" | ");
        w.WriteLine(result.ToString("0.0#", CultureInfo.InvariantCulture));
        kept++;
    }
}
Console.WriteLine($"{kept:N0} posicions escrites, {bad:N0} línies no vàlides");
return 0;
