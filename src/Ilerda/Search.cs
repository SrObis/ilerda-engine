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

    readonly TranspositionTable _tt = new(64);

    // Ordenació de jugades tranquil·les: killers (2 per ply) i history [bàndol, des de, a].
    readonly int[,] _killers = new int[128, 2];
    readonly int[,,] _history = new int[2, 64, 64];
    const int HistoryMax = 6000;

    public volatile bool Stop;
    public int BestMove { get; private set; }

    public void SetHash(int megabytes) => _tt.Resize(megabytes);
    public void NewGame()
    {
        _tt.Clear();
        Array.Clear(_history);
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
        _pos = pos;
        _nodes = 0;
        _limitMs = timeMs;
        _stopped = false;
        Stop = false;
        BestMove = 0;
        _sw.Restart();
        Array.Clear(_killers);
        // La history es conserva entre jugades però s'envelleix perquè no domini la cerca nova.
        for (int c = 0; c < 2; c++)
            for (int f = 0; f < 64; f++)
                for (int t = 0; t < 64; t++) _history[c, f, t] /= 2;

        // Moviment de reserva: el primer legal.
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

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            int score = Negamax(depth, -Inf, Inf, 0);
            if (_stopped) break;

            BestMove = _rootBest;
            long ms = _sw.ElapsedMilliseconds;
            long nps = ms > 0 ? _nodes * 1000 / ms : _nodes;
            info?.Invoke($"info depth {depth} score {FormatScore(score)} nodes {_nodes} time {ms} nps {nps} pv {MoveGen.ToUci(BestMove)}");

            if (Math.Abs(score) > Mate - 100) break;
        }
        return BestMove;
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
        if ((_nodes & 2047) == 0 && (Stop || (_limitMs > 0 && _sw.ElapsedMilliseconds >= _limitMs)))
            _stopped = true;
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

        // Null move: si passant el torn la posició encara supera beta, és molt probable que
        // qualsevol jugada real també ho faci, i es poda. No en escac, ni sense peces
        // (zugzwang), ni dues vegades seguides, ni quan busquem mats.
        if (allowNull && !inCheck && ply > 0 && depth >= 3 && beta < Mate - 100
            && _pos.HasNonPawnMaterial(_pos.Side) && Eval.Evaluate(_pos) >= beta)
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
            if (!_pos.MakeMove(m)) continue;
            legal++;
            int score = -Negamax(depth - 1, -beta, -alpha, ply + 1);
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
