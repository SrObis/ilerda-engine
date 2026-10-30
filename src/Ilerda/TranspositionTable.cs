namespace Ilerda;

public enum Bound : byte { None = 0, Exact = 1, Lower = 2, Upper = 3 }

/// <summary>
/// Taula de transposició de mida fixa (potència de 2 entrades) amb reemplaç per profunditat.
/// Compartida entre els fils de la cerca (Lazy SMP) sense bloquejos: cada entrada són dos paraules de
/// 64 bits, <c>Data</c> i <c>Check = clau ^ Data</c>. Si dos fils escriuen a la vegada i l'entrada queda
/// a mitges, la comprovació falla i es tracta com a no trobada.
/// </summary>
public sealed class TranspositionTable
{
    struct Entry
    {
        public ulong Check;
        public ulong Data;
    }

    // Data: jugada (32 bits) | puntuació (16, amb signe) | profunditat (8) | límit (8).
    static ulong Pack(int move, int score, int depth, Bound bound) =>
        (uint)move | ((ulong)(ushort)(short)score << 32) | ((ulong)(byte)depth << 48) | ((ulong)(byte)bound << 56);

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
        ulong data = Volatile.Read(ref e.Data);
        ulong check = Volatile.Read(ref e.Check);
        var b = (Bound)(data >> 56);
        if (b != Bound.None && (check ^ data) == key)
        {
            move = (int)(uint)data;
            score = (short)(ushort)(data >> 32);
            depth = (sbyte)(byte)(data >> 48);
            bound = b;
            return true;
        }
        move = 0; score = 0; depth = 0; bound = Bound.None;
        return false;
    }

    public void Store(ulong key, int move, int score, int depth, Bound bound)
    {
        ref Entry e = ref _table[key & _mask];
        ulong oldData = Volatile.Read(ref e.Data);
        bool same = (Volatile.Read(ref e.Check) ^ oldData) == key && (Bound)(oldData >> 56) != Bound.None;
        if (same)
        {
            // Conserva la millor jugada si la nova entrada no en porta (p. ex. fail-low).
            if (move == 0) move = (int)(uint)oldData;
            // No sobreescriguis una entrada més profunda de la mateixa posició amb una de més superficial.
            if ((sbyte)(byte)(oldData >> 48) > depth && bound != Bound.Exact) return;
        }
        ulong data = Pack(move, score, Math.Min(depth, 127), bound);
        Volatile.Write(ref e.Data, data);
        Volatile.Write(ref e.Check, key ^ data);
    }
}
