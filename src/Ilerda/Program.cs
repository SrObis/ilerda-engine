using Ilerda;

Nnue.LoadEmbedded();

// Ús:
//   ilerda                       -> mode UCI (per a GUIs i torneigs)
//   ilerda perft <depth> [fen]   -> comptador de nodes del generador de moviments
//   ilerda test                  -> suite de perft; surt amb codi 1 si alguna comprovació falla
if (args.Length >= 1 && args[0] == "test")
{
    bool perftOk = PerftSuite.Run(Console.WriteLine);
    bool benchOk = PerftSuite.RunBench(Console.WriteLine);
    return perftOk && benchOk ? 0 : 1;
}

//   ilerda datagen <partides> <nodes> <fitxer> [fils] [llavor] [llibre.epd] [--finals] [--syzygy carpetes]  -> partides d'autojoc per generar dades
//     --syzygy: la cerca sondeja les taules i la partida s'acaba amb el resultat exacte quan la posició hi és.
//     --finals: el llibre són posicions de final; es juguen tal com són (sense jugades aleatòries). Necessita --syzygy.
if (args.Length >= 4 && args[0] == "datagen")
{
    var dp = new List<string>();
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--finals") DataGen.Endgames = true;
        else if (args[i] == "--syzygy") { if (!Syzygy.Init(args[++i], out string msg)) { Console.WriteLine($"datagen: {msg}"); return 1; } }
        else dp.Add(args[i]);
    }
    DataGen.Run(
        long.Parse(dp[0]), long.Parse(dp[1]), dp[2],
        dp.Count > 3 ? int.Parse(dp[3]) : Math.Max(1, Environment.ProcessorCount - 1),
        dp.Count > 4 ? int.Parse(dp[4]) : Environment.TickCount,
        dp.Count > 5 ? dp[5] : null);
    return 0;
}

//   ilerda tbrelabel <entrada> <sortida> <carpetes_syzygy> [fils]  -> posa el resultat exacte de les taules a les posicions de <= 6 peces
if (args.Length >= 4 && args[0] == "tbrelabel")
{
    return TbRelabel.Run(args[1], args[2], args[3], args.Length > 4 ? int.Parse(args[4]) : Math.Max(1, Environment.ProcessorCount - 1));
}

//   ilerda relabel <entrada> <sortida> <nodes> [fils] [línies] [salta] [--peces A-B] [--sospita N] [--xarxa fitxer] [--syzygy carpetes]
//     -> torna a puntuar cada posició amb el motor actual (línies: 0 = totes; salta: línies inicials que s'ometen, per fer-ho a trossos)
//     --peces A-B: només les posicions de A a B peces; --sospita N: només les que es veien guanyant per >= N cp i no es van guanyar;
//     la resta passa igual. --xarxa: NNUE en lloc de la incrustada. --syzygy: la cerca sondeja les taules i <= 6 peces reben el resultat exacte.
if (args.Length >= 4 && args[0] == "relabel")
{
    var rp = new List<string>();
    var ro = new Relabel.Options();
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--peces") { var ab = args[++i].Split('-'); ro.MinPieces = int.Parse(ab[0]); ro.MaxPieces = int.Parse(ab[1]); }
        else if (args[i] == "--sospita") ro.Suspicion = int.Parse(args[++i]);
        else if (args[i] == "--xarxa") { if (!Nnue.Load(args[++i], out string err)) { Console.WriteLine($"relabel: {err}"); return 1; } }
        else if (args[i] == "--syzygy") { if (!Syzygy.Init(args[++i], out string msg)) { Console.WriteLine($"relabel: {msg}"); return 1; } }
        else rp.Add(args[i]);
    }
    return Relabel.Run(rp[0], rp[1], long.Parse(rp[2]),
        rp.Count > 3 ? int.Parse(rp[3]) : Math.Max(1, Environment.ProcessorCount - 1),
        rp.Count > 4 ? long.Parse(rp[4]) : 0,
        rp.Count > 5 ? long.Parse(rp[5]) : 0, ro);
}

//   ilerda book <línies> <fitxer> [fils] [llavor] [nodes] [marge]  -> llibre d'obertures EPD filtrat pel motor
if (args.Length >= 3 && args[0] == "book")
{
    BookGen.Run(
        long.Parse(args[1]), args[2],
        args.Length > 3 ? int.Parse(args[3]) : 2,
        args.Length > 4 ? int.Parse(args[4]) : Environment.TickCount,
        args.Length > 5 ? long.Parse(args[5]) : 20000,
        args.Length > 6 ? int.Parse(args[6]) : 50);
    return 0;
}

if (args.Length >= 2 && args[0] == "perft")
{
    var pos = new Position();
    pos.SetFen(args.Length > 2 ? string.Join(' ', args.Skip(2)) : Position.StartFen);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    ulong nodes = Perft.Run(pos, int.Parse(args[1]));
    Console.WriteLine($"nodes {nodes} time {sw.ElapsedMilliseconds} ms");
    return 0;
}

Uci.Loop();
return 0;
