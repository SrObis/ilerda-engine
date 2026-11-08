using System.Diagnostics;

namespace Ilerda;

/// <summary>
/// Cerca negamax alfa-beta amb aprofundiment iteratiu, cerca de quiescència,
/// taula de transposició (Zobrist), detecció de repeticions, ordenació MVV-LVA i control de temps.
/// </summary>
public sealed class Searcher
{
    const int Inf = 32000;
    const int Mate = 30000;

    Position _pos = null!;
    long _nodes;
    readonly Stopwatch _sw = new();
    long _limitMs;   // límit dur: la cerca s'atura encara que sigui a mitja iteració
    long _softMs;    // temps òptim (0 = no n'hi ha): no s'inicia cap iteració nova passat aquest temps, ajustat per l'estabilitat
    bool _stopped;
    int _rootBest;

    readonly TranspositionTable _tt;

    // Lazy SMP: el fil principal (_master == this) porta el temps i tria la jugada; els ajudants
    // cerquen la mateixa posició amb la taula de transposició compartida i s'aturen quan el principal acaba.
    readonly Searcher _master;
    int[]? _rootAllowed; // jugades de l'arrel permeses per les taules Syzygy (null = totes)
    readonly int _id;
    readonly List<Searcher> _helpers = new();
    int _threads = 1;
    volatile bool _halt;

    public Searcher() : this(new TranspositionTable(64), 0, null) { }

    Searcher(TranspositionTable tt, int id, Searcher? master)
    {
        _tt = tt;
        _id = id;
        _master = master ?? this;
    }

    // Ordenació de jugades tranquil·les: killers (2 per ply) i history [bàndol, des de, a].
    readonly int[,] _killers = new int[128, 2];
    readonly int[,,] _history = new int[2, 64, 64];
    const int HistoryMax = 6000;

    // Reducció LMR segons profunditat i número de jugada: 0,75 + ln(d) * ln(m) / 2,25.
    int[,] _lmr = BuildLmrTable(2.25);
    int _lmrStart = 5;   // primera jugada (comptant des d'1) que es pot reduir
    int _lmrHist = 2500; // cada tants punts de history, la reducció baixa 1

    int _rfpMargin = 90;  // marge de reverse futility per nivell de profunditat (1000 = pràcticament desactivada)
    int _futMargin = 110; // marge de futility per nivell de profunditat
    int _singDepth = 8;   // profunditat mínima de l'extensió singular (1000 = desactivada)
    int _singMargin = 32; // marge de la cerca singular, en 1/16 de cp per nivell de profunditat
    readonly int[] _excl = new int[128]; // jugada exclosa a cada ply (cerca de verificació singular)
    int _iirDepth = 4;    // profunditat mínima de la reducció iterativa interna (1000 = desactivada)
    // Correcció de l'avaluació estàtica: per a cada estructura de peons es manté una mitjana mòbil de
    // (puntuació de la cerca - avaluació de la xarxa), en 1/CorrGrain de centipeó, i s'afegeix a l'avaluació.
    // Així la cerca compensa els errors sistemàtics de la xarxa en posicions que s'assemblen.
    const int CorrGrain = 256, CorrSize = 16384, CorrLimit = 100;
    readonly int[,] _corr = new int[2, CorrSize];
    int _tbProbeDepth = 1;  // profunditat mínima per sondejar les taules Syzygy
    bool _tbRoot = true;    // filtrar les jugades de l'arrel amb les taules (DTZ)
    int _tbProbeLimit = 7;  // nombre màxim de peces per sondejar
    const int TbWin = 20000; // puntuació de victòria per taules (per sota dels mats: Mate - 100)
    int _corrWeight = 100; // % de la correcció que s'aplica (0 = desactivada)
    int _softTime = 100;  // % del límit de temps passat el qual no s'inicia una iteració nova (100 = només el límit dur)

    // Gestió del temps: multiplicador del temps òptim segons quantes iteracions seguides
    // la millor jugada no ha canviat (0, 1, 2, 3, 4 o més). Jugada inestable: més temps; estable: menys.
    static readonly double[] StabilityScale = { 2.0, 1.4, 1.1, 0.9, 0.8 };

    static int[,] BuildLmrTable(double divisor)
    {
        var t = new int[64, 64];
        for (int d = 1; d < 64; d++)
            for (int m = 1; m < 64; m++)
                t[d, m] = (int)(0.75 + Math.Log(d) * Math.Log(m) / divisor);
        return t;
    }

    /// <summary>Paràmetres afinables per UCI (per provar variants sense recompilar). Retorna false si no el coneix.</summary>
    public bool SetOption(string name, int value)
    {
        switch (name.ToLowerInvariant())
        {
            case "lmrdiv": _lmr = BuildLmrTable(Math.Clamp(value, 50, 1000) / 100.0); return true;
            case "lmrstart": _lmrStart = Math.Clamp(value, 2, 20); return true;
            case "lmrhist": _lmrHist = Math.Clamp(value, 100, 100000); return true;
            case "rfpmargin": _rfpMargin = Math.Clamp(value, 0, 1000); return true;
            case "futmargin": _futMargin = Math.Clamp(value, 0, 1000); return true;
            case "singdepth": _singDepth = Math.Clamp(value, 2, 1000); return true;
            case "singmargin": _singMargin = Math.Clamp(value, 0, 512); return true;
            case "iirdepth": _iirDepth = Math.Clamp(value, 2, 1000); return true;
            case "syzygyprobedepth": _tbProbeDepth = Math.Clamp(value, 1, 100); return true;
            case "syzygyroot": _tbRoot = value != 0; return true;
            case "syzygyprobelimit": _tbProbeLimit = Math.Clamp(value, 0, 7); return true;
            case "corrweight": _corrWeight = Math.Clamp(value, 0, 300); return true;
            case "softtime": _softTime = Math.Clamp(value, 10, 100); return true;
            default: return false;
        }
    }

    public volatile bool Stop;
    public int BestMove { get; private set; }

    /// <summary>Puntuació (cp, des del bàndol que juga) de l'última iteració completada.</summary>
    public int LastScore { get; private set; }

    /// <summary>Límit de nodes (0 = sense límit). No s'inicia una iteració nova passat el límit, i la cerca es talla a 8 vegades el límit.</summary>
    public long NodeLimit { get; set; }

    public void SetHash(int megabytes) => _tt.Resize(megabytes);
    public void SetThreads(int n) => _threads = Math.Clamp(n, 1, 256);
    public int Threads => _threads;

    /// <summary>Nodes totals de l'última cerca (per a bench).</summary>
    public long Nodes => TotalNodes();

    public void NewGame()
    {
        _tt.Clear();
        Array.Clear(_history);
        Array.Clear(_corr);
        foreach (var h in _helpers) { Array.Clear(h._history); Array.Clear(h._corr); }
    }

    /// <summary>Nodes de tots els fils (per a la informació; el límit de nodes compta només el fil principal).</summary>
    long TotalNodes()
    {
        long n = Volatile.Read(ref _nodes);
        foreach (var h in _helpers) n += Volatile.Read(ref h._nodes);
        return n;
    }

    // History amb "gravity": el canvi es redueix a mesura que el valor s'acosta al màxim, així queda acotat.
    void BumpHistory(int m, int bonus)
    {
        ref int h = ref _history[_pos.Side, MoveGen.From(m), MoveGen.To(m)];
        h += bonus - h * Math.Abs(bonus) / HistoryMax;
    }

    void UpdateQuietHistory(int m, int ply, int depth, ReadOnlySpan<int> tried)
    {
        if (ply < 128 && _killers[ply, 0] != m)
        {
            _killers[ply, 1] = _killers[ply, 0];
            _killers[ply, 0] = m;
        }
        // Bonus per a la jugada que talla i malus per a les tranquil·les provades abans que no han tallat.
        int bonus = Math.Min(depth * depth, 1500);
        BumpHistory(m, bonus);
        foreach (int q in tried) BumpHistory(q, -bonus);
    }

    bool IsQuiet(int m) =>
        MoveGen.Promo(m) == 0 && MoveGen.Flag(m) != MoveGen.FlagEp && _pos.Board[MoveGen.To(m)] == -1;

    public int Think(Position pos, int maxDepth, long timeMs, Action<string>? info, long softMs = 0)
    {
        _limitMs = timeMs;
        _softMs = softMs;
        _sw.Restart();
        _halt = false;
        _tt.NewSearch();

        // Moviment de reserva: el primer legal.
        BestMove = 0;
        LastScore = 0;
        Span<int> rootMoves = stackalloc int[320];
        int count = MoveGen.Generate(pos, rootMoves);
        for (int i = 0; i < count; i++)
        {
            if (!pos.MakeMove(rootMoves[i])) continue;
            pos.UnmakeMove();
            BestMove = rootMoves[i];
            break;
        }
        if (BestMove == 0) return 0;

        // Taules Syzygy a l'arrel: només es cerquen les jugades que conserven el millor resultat (DTZ).
        _rootAllowed = null;
        if (Syzygy.Enabled && _tbRoot)
        {
            var keep = Syzygy.RootMoves(pos, _tbProbeLimit);
            if (keep != null && keep.Length > 0)
            {
                _rootAllowed = keep;
                if (keep.Length == 1)
                {
                    BestMove = keep[0];
                    info?.Invoke($"info string Syzygy: jugada unica {MoveGen.ToUci(BestMove)}");
                    return BestMove;
                }
            }
        }

        // Fils ajudants (reutilitzats entre cerques perquè conservin la history).
        while (_helpers.Count < _threads - 1) _helpers.Add(new Searcher(_tt, _helpers.Count + 1, this));
        var threads = new List<Thread>();
        for (int i = 0; i < _threads - 1; i++)
        {
            var h = _helpers[i];
            var hp = pos.Clone();
            var th = new Thread(() => h.Iterate(hp, maxDepth, null), 16 * 1024 * 1024);
            th.Start();
            threads.Add(th);
        }

        Iterate(pos, maxDepth, info);

        _halt = true;
        foreach (var th in threads) th.Join();
        _halt = false;
        return BestMove;
    }

    /// <summary>Aprofundiment iteratiu d'un fil. El principal (id 0) informa i fixa BestMove; els ajudants només omplen la taula.</summary>
    void Iterate(Position pos, int maxDepth, Action<string>? info)
    {
        _pos = pos;
        _nodes = 0;
        _stopped = false;
        Array.Clear(_killers);
        // La history es conserva entre jugades però s'envelleix perquè no domini la cerca nova.
        for (int c = 0; c < 2; c++)
            for (int f = 0; f < 64; f++)
                for (int t = 0; t < 64; t++) _history[c, f, t] /= 2;

        int stable = 0, prevBest = 0, prevScore = 0;
        for (int depth = 1; depth <= maxDepth; depth++)
        {
            // Els ajudants se salten alguns nivells perquè no repeteixin tots la mateixa feina.
            if (_id > 0 && depth > 1 && (depth + _id) % 3 == 0) continue;

            // Finestres d'aspiració: a partir de depth 5 es cerca al voltant de l'última puntuació
            // i s'eixampla (x2) cada cop que falla.
            int score;
            if (depth < 5 || _id > 0)
                score = Negamax(depth, -Inf, Inf, 0);
            else
            {
                int delta = 20, a = Math.Max(-Inf, LastScore - delta), b = Math.Min(Inf, LastScore + delta);
                while (true)
                {
                    score = Negamax(depth, a, b, 0);
                    if (_stopped) break;
                    if (score <= a && a > -Inf) { a = Math.Max(-Inf, a - delta); delta *= 2; }
                    else if (score >= b && b < Inf) { b = Math.Min(Inf, b + delta); delta *= 2; }
                    else break;
                    if (delta > 600) { a = -Inf; b = Inf; }
                }
            }
            if (_stopped) break;
            if (_id > 0)
            {
                if (Math.Abs(score) > Mate - 100) break;
                continue;
            }

            BestMove = _rootBest;
            LastScore = score;
            long ms = _sw.ElapsedMilliseconds;
            long total = TotalNodes();
            long nps = ms > 0 ? total * 1000 / ms : total;
            info?.Invoke($"info depth {depth} score {FormatScore(score)} nodes {total} time {ms} nps {nps} pv {MoveGen.ToUci(BestMove)}");

            if (Math.Abs(score) > Mate - 100) break;
            if (NodeLimit > 0 && _nodes >= NodeLimit) break;
            // Temps tou: una iteració nova costa uns 2-3 cops l'anterior, així que si ja s'ha gastat
            // més de la fracció SoftTime del límit no s'arriba a acabar i només es malgasta el temps.
            if (_limitMs > 0 && ms * 100 >= _limitMs * _softTime) break;

            // Temps òptim: si la millor jugada es manté, s'acaba abans; si canvia o la puntuació
            // cau respecte de la iteració anterior, es pensa més (sempre dins del límit dur).
            if (_softMs > 0)
            {
                stable = depth > 1 && BestMove == prevBest ? stable + 1 : 0;
                double scale = StabilityScale[Math.Min(stable, StabilityScale.Length - 1)];
                if (depth > 1 && score < prevScore) scale *= 1.0 + Math.Min(prevScore - score, 100) / 200.0;
                prevBest = BestMove;
                prevScore = score;
                if (depth >= 4 && ms >= _softMs * scale) break;
            }
        }
    }

    static string FormatScore(int score)
    {
        if (Math.Abs(score) > Mate - 100)
        {
            int moves = (Mate - Math.Abs(score) + 1) / 2;
            return $"mate {(score > 0 ? moves : -moves)}";
        }
        return $"cp {score}";
    }

    bool CheckStop()
    {
        if ((_nodes & 2047) == 0)
        {
            if (_id > 0) { if (_master.Stop || _master._halt) _stopped = true; }
            else if (Stop || (_limitMs > 0 && _sw.ElapsedMilliseconds >= _limitMs)
                     || (NodeLimit > 0 && _nodes >= NodeLimit * 8))
                _stopped = true;
        }
        return _stopped;
    }

    int Negamax(int depth, int alpha, int beta, int ply, bool allowNull = true)
    {
        if (CheckStop()) return 0;
        _nodes++;

        if (ply > 0 && (_pos.Halfmove >= 100 || _pos.IsRepetition(ply) || _pos.InsufficientMaterial())) return 0;

        // Taules de finals Syzygy: just després d'una captura o d'un moviment de peó (Halfmove 0), amb poques peces.
        if (ply > 0 && _pos.Halfmove == 0 && Syzygy.Enabled && depth >= _tbProbeDepth && _pos.Castling == 0
            && System.Numerics.BitOperations.PopCount(_pos.Occ) <= Math.Min(Syzygy.MaxCardinality, _tbProbeLimit)
            && Syzygy.ProbeWdl(_pos, out Wdl tbWdl))
        {
            // Només la victòria i la derrota netes valen més que unes taules; les "maleïdes" són taules per la regla dels 50.
            return tbWdl == Wdl.Win ? TbWin - ply : tbWdl == Wdl.Loss ? -TbWin + ply : 0;
        }

        bool inCheck = _pos.InCheck();
        if (inCheck && ply < 60) depth++;
        if (depth <= 0) return Quiesce(alpha, beta, ply);

        ulong key = _pos.Key;
        int alpha0 = alpha;
        int ttMove = 0;
        int ttEval = TranspositionTable.NoEval;
        int excl = _excl[ply];
        bool ttHit = false;
        int ttScore = 0, ttDepth = 0;
        Bound ttBound = Bound.Upper;
        if (_tt.Probe(key, out int tm, out int ts, out ttEval, out int td, out Bound tb))
        {
            ttMove = tm;
            // Les puntuacions de mat es guarden relatives a la posició, no a l'arrel.
            if (ts > Mate - 100) ts -= ply;
            else if (ts < -Mate + 100) ts += ply;
            ttHit = true; ttScore = ts; ttDepth = td; ttBound = tb;
            if (ply > 0 && td >= depth && excl == 0)
            {
                if (tb == Bound.Exact) return ts;
                if (tb == Bound.Lower && ts >= beta) return ts;
                if (tb == Bound.Upper && ts <= alpha) return ts;
            }
        }

        // Reducció iterativa interna: sense jugada de la taula la posició no s'ha explorat mai a fons,
        // i l'ordenació serà dolenta; es cerca un nivell menys (la propera iteració ja trobarà la jugada).
        if (depth >= _iirDepth && ttMove == 0) depth--;

        bool pv = beta - alpha > 1;
        int rawEval = inCheck ? -Inf : ttEval != TranspositionTable.NoEval ? ttEval : Eval.Evaluate(_pos);
        int corrIdx = CorrIndex();
        int staticEval = rawEval;
        if (!inCheck && _corrWeight > 0)
            staticEval = Math.Clamp(rawEval + _corr[_pos.Side, corrIdx] * _corrWeight / (100 * CorrGrain), -Mate + 100, Mate - 100);

        // Reverse futility: si l'avaluació estàtica supera beta amb un bon marge a poca profunditat,
        // la posició és tan bona que es talla sense cercar. No en PV, escac ni quan busquem mats.
        if (!pv && !inCheck && ply > 0 && depth <= 6 && beta < Mate - 100 && beta > -Mate + 100
            && staticEval - _rfpMargin * depth >= beta)
            return staticEval;

        // Null move: si passant el torn la posició encara supera beta, és molt probable que
        // qualsevol jugada real també ho faci, i es poda. No en escac, ni sense peces
        // (zugzwang), ni dues vegades seguides, ni quan busquem mats.
        if (allowNull && !inCheck && ply > 0 && depth >= 3 && beta < Mate - 100
            && _pos.HasNonPawnMaterial(_pos.Side) && staticEval >= beta)
        {
            int r = 2 + depth / 6;
            _pos.MakeNullMove();
            int ns = -Negamax(depth - 1 - r, -beta, -beta + 1, ply + 1, false);
            _pos.UnmakeNullMove();
            if (_stopped) return 0;
            if (ns >= beta) return ns >= Mate - 100 ? beta : ns;
        }

        Span<int> moves = stackalloc int[320];
        Span<int> scores = stackalloc int[320];
        int n = MoveGen.Generate(_pos, moves);
        ScoreMoves(moves, scores, n, ttMove, ply);

        int legal = 0, best = -Inf, bestMove = 0, triedCount = 0;
        Span<int> tried = stackalloc int[64];
        for (int i = 0; i < n; i++)
        {
            PickBest(moves, scores, i, n);
            int m = moves[i];
            if (m == excl) continue;
            if (ply == 0 && _master._rootAllowed != null && Array.IndexOf(_master._rootAllowed, m) < 0) continue;
            bool quiet = IsQuiet(m); // abans de fer la jugada
            int hist = _history[_pos.Side, MoveGen.From(m), MoveGen.To(m)];
            // Extensió singular: si la jugada de la taula és molt millor que totes les altres (la cerca a
            // profunditat reduïda sense ella queda per sota d'un llindar), s'allarga un nivell. Si fins i tot
            // sense ella es supera beta, la posició es poda (multi-cut).
            int ext = 0;
            if (ply > 0 && excl == 0 && m == ttMove && ttHit && depth >= _singDepth && ttDepth >= depth - 3
                && ttBound != Bound.Upper && Math.Abs(ttScore) < Mate - 100)
            {
                int sBeta = ttScore - depth * _singMargin / 16;
                _excl[ply] = m;
                int ss = Negamax((depth - 1) / 2, sBeta - 1, sBeta, ply, false);
                _excl[ply] = 0;
                if (_stopped) return 0;
                if (ss < sBeta) ext = 1;
                else if (sBeta >= beta) return sBeta;
            }

            if (!_pos.MakeMove(m)) continue;
            legal++;

            // Futility: a poca profunditat, una jugada tranquil·la que no dona escac i amb l'avaluació
            // molt per sota d'alfa no la remuntarà. Sempre es cerca la primera jugada legal.
            if (quiet && legal > 1 && !pv && !inCheck && depth <= 4 && alpha > -Mate + 100
                && staticEval + _futMargin * depth <= alpha && !_pos.InCheck())
            {
                _pos.UnmakeMove();
                continue;
            }

            // PVS: la primera jugada es cerca amb finestra completa; la resta amb finestra nul·la
            // (amb LMR si cal) i només es repeteixen amb finestra completa si superen alfa en un node PV.
            int score;
            if (legal == 1) score = -Negamax(depth - 1 + ext, -beta, -alpha, ply + 1);
            else
            {
                bool reduce = quiet && depth >= 3 && legal >= _lmrStart && ply > 0 && !inCheck
                              && m != _killers[ply, 0] && m != _killers[ply, 1] && !_pos.InCheck();
                int r = 0;
                if (reduce)
                {
                    // Les jugades amb bon history (que han tallat sovint) es redueixen menys.
                    r = _lmr[Math.Min(depth, 63), Math.Min(legal, 63)] - hist / _lmrHist;
                    r = Math.Min(r, depth - 2);
                }
                score = r > 0 ? -Negamax(depth - 1 + ext - r, -alpha - 1, -alpha, ply + 1) : alpha + 1;
                if (score > alpha) score = -Negamax(depth - 1 + ext, -alpha - 1, -alpha, ply + 1);
                if (pv && score > alpha && score < beta) score = -Negamax(depth - 1 + ext, -beta, -alpha, ply + 1);
            }
            _pos.UnmakeMove();
            if (_stopped) return 0;

            if (score > best)
            {
                best = score;
                bestMove = m;
                if (ply == 0) _rootBest = m;
                if (score > alpha)
                {
                    alpha = score;
                    if (alpha >= beta)
                    {
                        if (quiet) UpdateQuietHistory(m, ply, depth, tried[..triedCount]);
                        break;
                    }
                }
            }
            if (quiet && triedCount < tried.Length) tried[triedCount++] = m;
        }

        if (legal == 0) return excl != 0 ? alpha0 : inCheck ? -Mate + ply : 0;

        int stored = best;
        if (stored > Mate - 100) stored += ply;
        else if (stored < -Mate + 100) stored -= ply;
        Bound bound = best >= beta ? Bound.Lower : best > alpha0 ? Bound.Exact : Bound.Upper;

        // Aprèn la correcció: només si el resultat no és un mat, la millor jugada no és una captura i la
        // cota és coherent amb el signe de l'error (una cota inferior per sota de l'avaluació no diu res).
        if (excl == 0 && !inCheck && Math.Abs(best) < Mate - 100 && (bestMove == 0 || IsQuiet(bestMove))
            && !(bound == Bound.Lower && best <= staticEval) && !(bound == Bound.Upper && best >= staticEval))
        {
            int target = Math.Clamp(best - rawEval, -CorrLimit, CorrLimit) * CorrGrain;
            ref int e = ref _corr[_pos.Side, corrIdx];
            e += (target - e) * Math.Min(depth + 1, 16) / 256;
        }

        if (excl == 0) _tt.Store(key, bestMove, stored, inCheck ? TranspositionTable.NoEval : Math.Clamp(rawEval, -Mate, Mate), depth, bound);
        return best;
    }

    // Índex de la taula de correcció: hash de les posicions dels peons de les dues bandes.
    int CorrIndex()
    {
        ulong h = _pos.Bb[Pc.Pawn] * 0x9E3779B97F4A7C15UL ^ _pos.Bb[6 + Pc.Pawn] * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 29;
        return (int)(h & (CorrSize - 1));
    }

    int Quiesce(int alpha, int beta, int ply)
    {
        if (CheckStop()) return 0;
        _nodes++;

        if (_pos.InsufficientMaterial()) return 0;

        // Taula de transposició: qualsevol entrada (de qualsevol profunditat) serveix per tallar fora de PV
        // i dona l'avaluació estàtica sense tornar-la a calcular.
        ulong key = _pos.Key;
        bool pv = beta - alpha > 1;
        int ttMove = 0, ttEval = TranspositionTable.NoEval;
        if (_tt.Probe(key, out int tm, out int ts, out ttEval, out _, out Bound tb))
        {
            ttMove = tm;
            if (ts > Mate - 100) ts -= ply;
            else if (ts < -Mate + 100) ts += ply;
            if (!pv)
            {
                if (tb == Bound.Exact) return ts;
                if (tb == Bound.Lower && ts >= beta) return ts;
                if (tb == Bound.Upper && ts <= alpha) return ts;
            }
        }

        int stand = ttEval != TranspositionTable.NoEval ? ttEval : Eval.Evaluate(_pos);
        int standStore = Math.Clamp(stand, -Mate, Mate);
        if (stand >= beta)
        {
            _tt.Store(key, 0, beta, standStore, 0, Bound.Lower);
            return beta;
        }
        int alpha0 = alpha;
        if (stand > alpha) alpha = stand;

        Span<int> moves = stackalloc int[320];
        Span<int> scores = stackalloc int[320];
        int n = MoveGen.Generate(_pos, moves, capturesOnly: true);
        ScoreMoves(moves, scores, n, ttMove);

        int bestMove = 0;
        for (int i = 0; i < n; i++)
        {
            PickBest(moves, scores, i, n);
            int m = moves[i];
            if (!_pos.MakeMove(m)) continue;
            int score = -Quiesce(-beta, -alpha, ply + 1);
            _pos.UnmakeMove();
            if (_stopped) return 0;
            if (score >= beta)
            {
                _tt.Store(key, m, beta, standStore, 0, Bound.Lower);
                return beta;
            }
            if (score > alpha) { alpha = score; bestMove = m; }
        }
        _tt.Store(key, bestMove, alpha, standStore, 0, alpha > alpha0 ? Bound.Exact : Bound.Upper);
        return alpha;
    }

    void ScoreMoves(Span<int> moves, Span<int> scores, int n, int ttMove = 0, int ply = -1)
    {
        for (int i = 0; i < n; i++)
        {
            int m = moves[i];
            if (ttMove != 0 && (m & TranspositionTable.MoveMask) == ttMove) { scores[i] = 1_000_000; continue; }
            int from = MoveGen.From(m), to = MoveGen.To(m);
            int attacker = _pos.Board[from] % 6;
            int score = 0;
            int victim = MoveGen.Flag(m) == MoveGen.FlagEp ? Pc.Pawn : (_pos.Board[to] == -1 ? -1 : _pos.Board[to] % 6);
            if (victim >= 0) score = 10000 + Eval.Value[victim] * 10 - attacker;
            int promo = MoveGen.Promo(m);
            if (promo != 0) score += 9000 + Eval.Value[promo];
            else if (victim < 0 && ply >= 0 && ply < 128)
            {
                // Jugada tranquil·la: killers per sobre, després history (< 7000, sota de les captures).
                if (m == _killers[ply, 0]) score = 8000;
                else if (m == _killers[ply, 1]) score = 7500;
                else score = _history[_pos.Side, from, to];
            }
            scores[i] = score;
        }
    }

    static void PickBest(Span<int> moves, Span<int> scores, int start, int n)
    {
        int best = start;
        for (int j = start + 1; j < n; j++)
            if (scores[j] > scores[best]) best = j;
        if (best == start) return;
        (moves[start], moves[best]) = (moves[best], moves[start]);
        (scores[start], scores[best]) = (scores[best], scores[start]);
    }
}
