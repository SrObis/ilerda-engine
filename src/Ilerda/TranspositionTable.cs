namespace Ilerda;

public enum Bound : byte { None = 0, Exact = 1, Lower = 2, Upper = 3 }

/// <summary>
/// Taula de transposició de mida fixa (potència de 2 entrades) amb reemplaç per profunditat.
/// Una sola fil de cerca: no cal sincronització.
/// </summary>
public sealed class TranspositionTable
{
    struct Entry
    {
        public ulong Key;
        public int Move;
        public short Score;
        public sbyte Depth;
        public Bound Bound;
    }

    Entry[] _table = Array.Empty<Entry>();
    ulong _mask;

    public TranspositionTable(int megabytes) => Resize(megabytes);

    public void Resize(int megabytes)
    {
        long entries = Math.Max(1, megabytes) * 1024L * 1024L / 16;
        long size = 1;
        while (size * 2 <= entries) size *= 2;
        _table = new Entry[size];
        _mask = (ulong)size - 1;
    }

    public void Clear() => Array.Clear(_table);

    public bool Probe(ulong key, out int move, out int score, out int depth, out Bound bound)
    {
        ref Entry e = ref _table[key & _mask];
        if (e.Bound != Bound.None && e.Key == key)
        {
            move = e.Move;
            score = e.Score;
            depth = e.Depth;
            bound = e.Bound;
            return true;
        }
        move = 0; score = 0; depth = 0; bound = Bound.None;
        return false;
    }

    public void Store(ulong key, int move, int score, int depth, Bound bound)
    {
        ref Entry e = ref _table[key & _mask];
        // Conserva la millor jugada si la nova entrada no en porta (p. ex. fail-low).
        if (move == 0 && e.Key == key) move = e.Move;
        // No sobreescriguis una entrada més profunda de la mateixa posició amb una de més superficial.
        if (e.Bound != Bound.None && e.Key == key && e.Depth > depth && bound != Bound.Exact) return;
        e.Key = key;
        e.Move = move;
        e.Score = (short)score;
        e.Depth = (sbyte)Math.Min(depth, 127);
        e.Bound = bound;
    }
}
