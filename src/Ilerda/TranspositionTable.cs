namespace Ilerda;

public enum Bound : byte { None = 0, Exact = 1, Lower = 2, Upper = 3 }

/// <summary>
/// Taula de transposició amb buckets de 4 entrades (64 bytes) i reemplaç per profunditat i edat.
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

    /// <summary>Valor de l'avaluació estàtica quan no es coneix (escac).</summary>
    public const int NoEval = -32768;

    const int BucketSize = 4;
    const int AgeBits = 5, AgeMask = (1 << AgeBits) - 1;

    // Data: jugada (16 bits: des de, a, promoció; sense l'indicador) | puntuació (16, amb signe) |
    //       avaluació estàtica (16, amb signe) | profunditat (8) | edat (5) i límit (2) a l'últim byte.
    public const int MoveMask = 0x7FFF;

    static ulong Pack(int move, int score, int eval, int depth, Bound bound, int age) =>
        (ushort)(move & MoveMask)
        | ((ulong)(ushort)(short)score << 16)
        | ((ulong)(ushort)(short)eval << 32)
        | ((ulong)(byte)depth << 48)
        | ((ulong)(byte)(((age & AgeMask) << 2) | (int)bound) << 56);

    static Bound BoundOf(ulong data) => (Bound)((data >> 56) & 3);
    static int AgeOf(ulong data) => (int)((data >> 58) & AgeMask);
    static int DepthOf(ulong data) => (int)((data >> 48) & 0xFF);

    Entry[] _table = Array.Empty<Entry>();
    ulong _bucketMask;
    volatile int _age;

    public TranspositionTable(int megabytes) => Resize(megabytes);

    public void Resize(int megabytes)
    {
        long entries = Math.Max(1, megabytes) * 1024L * 1024L / 16;
        long buckets = 1;
        while (buckets * 2 * BucketSize <= entries) buckets *= 2;
        _table = new Entry[buckets * BucketSize];
        _bucketMask = (ulong)buckets - 1;
        _age = 0;
    }

    public void Clear()
    {
        Array.Clear(_table);
        _age = 0;
    }

    /// <summary>Marca l'inici d'una cerca nova: les entrades de cerques anteriors passen a ser reemplaçables abans.</summary>
    public void NewSearch() => _age = (_age + 1) & AgeMask;

    public bool Probe(ulong key, out int move, out int score, out int eval, out int depth, out Bound bound)
    {
        int idx = (int)(key & _bucketMask) * BucketSize;
        for (int i = 0; i < BucketSize; i++)
        {
            ref Entry e = ref _table[idx + i];
            ulong data = Volatile.Read(ref e.Data);
            ulong check = Volatile.Read(ref e.Check);
            var b = BoundOf(data);
            if (b != Bound.None && (check ^ data) == key)
            {
                move = (int)(data & 0xFFFF);
                score = (short)(ushort)(data >> 16);
                eval = (short)(ushort)(data >> 32);
                depth = DepthOf(data);
                bound = b;
                return true;
            }
        }
        move = 0; score = 0; eval = NoEval; depth = 0; bound = Bound.None;
        return false;
    }

    public void Store(ulong key, int move, int score, int eval, int depth, Bound bound)
    {
        int idx = (int)(key & _bucketMask) * BucketSize;
        int age = _age;
        int victim = idx, victimValue = int.MaxValue;

        for (int i = 0; i < BucketSize; i++)
        {
            ref Entry e = ref _table[idx + i];
            ulong old = Volatile.Read(ref e.Data);
            bool used = BoundOf(old) != Bound.None;

            if (used && (Volatile.Read(ref e.Check) ^ old) == key)
            {
                // Mateixa posició: es conserva la jugada vella si la nova no en porta (p. ex. fail-low), i no es
                // sobreescriu una entrada més profunda d'aquesta mateixa cerca amb una de més superficial.
                if (move == 0) move = (int)(old & MoveMask);
                if (DepthOf(old) > depth && bound != Bound.Exact && AgeOf(old) == age) return;
                Write(ref e, key, move, score, eval, depth, bound, age);
                return;
            }

            // Víctima: la lliure, o la de menys valor (profunditat menys 8 per cada cerca d'antiguitat).
            int value = used ? DepthOf(old) - 8 * ((age - AgeOf(old)) & AgeMask) : int.MinValue;
            if (value < victimValue) { victimValue = value; victim = idx + i; }
        }

        Write(ref _table[victim], key, move, score, eval, depth, bound, age);
    }

    static void Write(ref Entry e, ulong key, int move, int score, int eval, int depth, Bound bound, int age)
    {
        ulong data = Pack(move, score, eval, Math.Min(depth, 127), bound, age);
        Volatile.Write(ref e.Data, data);
        Volatile.Write(ref e.Check, key ^ data);
    }
}
