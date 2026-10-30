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
    long _limitMs;
    bool _stopped;
    int _rootBest;

    readonly TranspositionTable _tt;

    // Lazy SMP: el fil principal (_master == this) porta el temps i tria la jugada; els ajudants
    // cerquen la mateixa posició amb la taula de transposició compartida i s'aturen quan el principal acaba.
    readonly Searcher _master;
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
    int _softTime = 100;  // % del límit de temps passat el qual no s'inicia una iteració nova (100 = només el límit dur)

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

    public void NewGame()
    {
        _tt.Clear();
        Array.Clear(_history);
        foreach (var h in _helpers) Array.Clear(h._history);
    }

    /// <summary>Nodes de tots els fils (per a la informació; el límit de nodes compta només el fil principal).</summary>
    long TotalNodes()
    {
        long n = Volatile.Read(ref _nodes);
        foreach (var h in _helpers) n += Volatile.Read(ref h._nodes);
        return n;
    }

    void UpdateQuietHistory(int m, int ply, int depth)
    {
        if (ply < 128 && _killers[ply, 0] != m)
        {
            _killers[ply, 1] = _killers[ply, 0];
            _killers[ply, 0] = m;
        }
        ref int h = ref _history[_pos.Side, MoveGen.From(m), MoveGen.To(m)];
        h = Math.Min(h + depth * depth, HistoryMax);
    }

    bool IsQuiet(int m) =>
        MoveGen.Promo(m) == 0 && MoveGen.Flag(m) != MoveGen.FlagEp && _pos.Board[MoveGen.To(m)] == -1;

    public int Think(Position pos, int maxDepth, long timeMs, Action<string>? info)
    {
        _limitMs = timeMs;
        _sw.Restart();
        _halt = false;

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

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            // Els ajudants se salten alguns nivells perquè no repeteixin tots la mateixa feina.
            if (_id > 0 && depth > 1 && (depth + _id) % 3 == 0) continue;

            int score = Negamax(depth, -Inf, Inf, 0);
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

        if (ply > 0 && (_pos.Halfmove >= 100 || _pos.IsRepetition())) return 0;

        bool inCheck = _pos.InCheck();
        if (inCheck && ply < 60) depth++;
        if (depth <= 0) return Quiesce(alpha, beta, ply);

        ulong key = _pos.Key;
        int alpha0 = alpha;
        int ttMove = 0;
        if (_tt.Probe(key, out int tm, out int ts, out int td, out Bound tb))
        {
            ttMove = tm;
            if (ply > 0 && td >= depth)
            {
                // Les puntuacions de mat es guarden relatives a la posició, no a l'arrel.
                if (ts > Mate - 100) ts -= ply;
                else if (ts < -Mate + 100) ts += ply;

                if (tb == Bound.Exact) return ts;
                if (tb == Bound.Lower && ts >= beta) return ts;
                if (tb == Bound.Upper && ts <= alpha) return ts;
            }
        }

        bool pv = beta - alpha > 1;
        int staticEval = inCheck ? -Inf : Eval.Evaluate(_pos);

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

        int legal = 0, best = -Inf, bestMove = 0;
        for (int i = 0; i < n; i++)
        {
            PickBest(moves, scores, i, n);
            int m = moves[i];
            bool quiet = IsQuiet(m); // abans de fer la jugada
            int hist = _history[_pos.Side, MoveGen.From(m), MoveGen.To(m)];
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

            // LMR: les jugades tranquil·les que es proven tard (ja ordenades) es cerquen a menys
            // profunditat amb finestra nul·la; si sorprenen (superen alfa) es tornen a cercar a fons.
            int score;
            bool reduce = quiet && depth >= 3 && legal >= _lmrStart && ply > 0 && !inCheck
                          && m != _killers[ply, 0] && m != _killers[ply, 1] && !_pos.InCheck();
            if (reduce)
            {
                // Les jugades amb bon history (que han tallat sovint) es redueixen menys.
                int r = _lmr[Math.Min(depth, 63), Math.Min(legal, 63)] - hist / _lmrHist;
                r = Math.Min(r, depth - 2);
                score = r > 0 ? -Negamax(depth - 1 - r, -alpha - 1, -alpha, ply + 1) : alpha + 1;
                if (score > alpha) score = -Negamax(depth - 1, -beta, -alpha, ply + 1);
            }
            else score = -Negamax(depth - 1, -beta, -alpha, ply + 1);
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
                        if (quiet) UpdateQuietHistory(m, ply, depth);
                        break;
                    }
                }
            }
        }

        if (legal == 0) return inCheck ? -Mate + ply : 0;

        int stored = best;
        if (stored > Mate - 100) stored += ply;
        else if (stored < -Mate + 100) stored -= ply;
        Bound bound = best >= beta ? Bound.Lower : best > alpha0 ? Bound.Exact : Bound.Upper;
        _tt.Store(key, bestMove, stored, depth, bound);
        return best;
    }

    int Quiesce(int alpha, int beta, int ply)
    {
        if (CheckStop()) return 0;
        _nodes++;

        int stand = Eval.Evaluate(_pos);
        if (stand >= beta) return beta;
        if (stand > alpha) alpha = stand;

        Span<int> moves = stackalloc int[320];
        Span<int> scores = stackalloc int[320];
        int n = MoveGen.Generate(_pos, moves, capturesOnly: true);
        ScoreMoves(moves, scores, n);

        for (int i = 0; i < n; i++)
        {
            PickBest(moves, scores, i, n);
            if (!_pos.MakeMove(moves[i])) continue;
            int score = -Quiesce(-beta, -alpha, ply + 1);
            _pos.UnmakeMove();
            if (_stopped) return 0;
            if (score >= beta) return beta;
            if (score > alpha) alpha = score;
        }
        return alpha;
    }

    void ScoreMoves(Span<int> moves, Span<int> scores, int n, int ttMove = 0, int ply = -1)
    {
        for (int i = 0; i < n; i++)
        {
            int m = moves[i];
            if (m == ttMove && m != 0) { scores[i] = 1_000_000; continue; }
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
