using Ilerda;

// Ús:
//   ilerda                       -> mode UCI (per a GUIs i torneigs)
//   ilerda perft <depth> [fen]   -> comptador de nodes del generador de moviments
//   ilerda test                  -> suite de perft; surt amb codi 1 si alguna comprovació falla
if (args.Length >= 1 && args[0] == "test")
{
    return PerftSuite.Run(Console.WriteLine) ? 0 : 1;
}

//   ilerda datagen <partides> <nodes> <fitxer> [fils] [llavor] [llibre.epd]  -> partides d'autojoc per generar dades
if (args.Length >= 4 && args[0] == "datagen")
{
    DataGen.Run(
        long.Parse(args[1]), long.Parse(args[2]), args[3],
        args.Length > 4 ? int.Parse(args[4]) : Math.Max(1, Environment.ProcessorCount - 1),
        args.Length > 5 ? int.Parse(args[5]) : Environment.TickCount,
        args.Length > 6 ? args[6] : null);
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
