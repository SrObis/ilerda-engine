using System.Numerics;

namespace Ilerda;

/// <summary>
/// Avaluació NNUE: xarxa 768 -> N (x2 perspectives) -> 1 amb SCReLU, entrenada amb bullet (format de
/// <c>quantised.bin</c>: pesos de la capa d'entrada [característica][neurona], biaixos, pesos de sortida
/// [propi N, rival N] i biaix de sortida, tot en i16 little-endian).
/// Les característiques són (peça, casella) vistes des de cada bàndol: les peces pròpies al bloc 0, les del
/// rival al 384, i la casella girada en vertical per a les negres. Com que no depenen del rei, els acumuladors
/// s'actualitzen sumant i restant una característica a cada peça afegida o treta.
/// </summary>
public static class Nnue
{
    const int Qa = 255, Qb = 64, Scale = 400;

    public static int Hidden { get; private set; }
    public static bool Loaded => Hidden != 0;

    /// <summary>Nombre de buckets de sortida (1 = xarxa sense buckets, 8 = per nombre de peces).</summary>
    public static int Buckets { get; private set; } = 1;

    static short[] _l0w = Array.Empty<short>(); // [768][Hidden]
    static short[] _l0b = Array.Empty<short>(); // [Hidden]
    static short[] _l1w = Array.Empty<short>(); // [Buckets][2 * Hidden]: bàndol que juga, després l'altre
    static short[] _l1b = Array.Empty<short>(); // [Buckets]

    /// <summary>Carrega la xarxa. Retorna false si el fitxer no existeix o no té la mida d'una xarxa 768 -> N -> 1.</summary>
    public static bool Load(string path, out string error)
    {
        error = "";
        try
        {
            return Parse(File.ReadAllBytes(path), out error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>Carrega la xarxa que va dins de l'executable (recurs <c>Ilerda.net</c>).</summary>
    public static bool LoadEmbedded()
    {
        using var stream = typeof(Nnue).Assembly.GetManifestResourceStream("Ilerda.net");
        if (stream == null) return false;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray(), out _);
    }

    static bool Parse(byte[] raw, out string error)
    {
        error = "";
        try
        {
            // Mida = (768N + N + B*2N + B) * 2 bytes, arrodonida a múltiple de 64, amb B = 1 (sense buckets) o 8.
            int h = 0, nb = 1;
            foreach (int b in new[] { 1, 8 })
            {
                for (int n = 16; n <= 4096 && h == 0; n += 16)
                {
                    int bytes = (768 * n + n + b * 2 * n + b) * 2;
                    if ((bytes + 63) / 64 * 64 == raw.Length) { h = n; nb = b; }
                }
                if (h != 0) break;
            }
            if (h == 0) { error = $"mida inesperada ({raw.Length} bytes)"; return false; }

            var s = new short[raw.Length / 2];
            Buffer.BlockCopy(raw, 0, s, 0, raw.Length);
            _l0w = s[..(768 * h)];
            _l0b = s[(768 * h)..(769 * h)];
            _l1w = s[(769 * h)..((769 + 2 * nb) * h)];
            _l1b = s[((769 + 2 * nb) * h)..((769 + 2 * nb) * h + nb)];
            Buckets = nb;
            Hidden = h;
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public static void Unload() => Hidden = 0;

    /// <summary>Índex de la característica de (peça, casella) des de la perspectiva de color <paramref name="persp"/>.</summary>
    static int Feature(int persp, int piece, int sq) =>
        (piece / 6 == persp ? 0 : 384) + (piece % 6) * 64 + (persp == 0 ? sq : sq ^ 56);

    /// <summary>Posa l'acumulador a punt per a una posició buida: només els biaixos, per a les dues perspectives.</summary>
    public static void Reset(Span<short> acc)
    {
        for (int c = 0; c < 2; c++) _l0b.AsSpan(0, Hidden).CopyTo(acc.Slice(c * Hidden, Hidden));
    }

    public static void Add(Span<short> acc, int piece, int sq)
    {
        int h = Hidden;
        for (int c = 0; c < 2; c++)
        {
            var dst = acc.Slice(c * h, h);
            var w = _l0w.AsSpan(Feature(c, piece, sq) * h, h);
            for (int i = 0; i < h; i++) dst[i] += w[i];
        }
    }

    /// <summary>
    /// Acumulador d'una jugada a partir del de la posició anterior, en una sola passada:
    /// dst = src + pesos(afegides) - pesos(tretes). <paramref name="adds"/> i <paramref name="subs"/>
    /// són parells (peça, casella): 1 o 2 afegides i 1 o 2 tretes.
    /// </summary>
    public static void ApplyDelta(ReadOnlySpan<short> src, Span<short> dst, ReadOnlySpan<int> adds, ReadOnlySpan<int> subs)
    {
        int h = Hidden, step = Vector<short>.Count;
        for (int c = 0; c < 2; c++)
        {
            var s = src.Slice(c * h, h);
            var d = dst.Slice(c * h, h);
            var a0 = _l0w.AsSpan(Feature(c, adds[0], adds[1]) * h, h);
            var s0 = _l0w.AsSpan(Feature(c, subs[0], subs[1]) * h, h);
            int i = 0;
            if (adds.Length == 2 && subs.Length == 2)
            {
                // Cas habitual (jugada sense captura): una afegida i una treta.
                for (; i <= h - step; i += step)
                    (new Vector<short>(s[i..]) + new Vector<short>(a0[i..]) - new Vector<short>(s0[i..])).CopyTo(d[i..]);
                for (; i < h; i++) d[i] = (short)(s[i] + a0[i] - s0[i]);
            }
            else if (adds.Length == 2)
            {
                // Captura: una afegida i dues tretes.
                var s1 = _l0w.AsSpan(Feature(c, subs[2], subs[3]) * h, h);
                for (; i <= h - step; i += step)
                    (new Vector<short>(s[i..]) + new Vector<short>(a0[i..]) - new Vector<short>(s0[i..]) - new Vector<short>(s1[i..])).CopyTo(d[i..]);
                for (; i < h; i++) d[i] = (short)(s[i] + a0[i] - s0[i] - s1[i]);
            }
            else
            {
                // Enroc: dues afegides i dues tretes.
                var a1 = _l0w.AsSpan(Feature(c, adds[2], adds[3]) * h, h);
                var s1 = _l0w.AsSpan(Feature(c, subs[2], subs[3]) * h, h);
                for (; i <= h - step; i += step)
                    (new Vector<short>(s[i..]) + new Vector<short>(a0[i..]) + new Vector<short>(a1[i..]) - new Vector<short>(s0[i..]) - new Vector<short>(s1[i..])).CopyTo(d[i..]);
                for (; i < h; i++) d[i] = (short)(s[i] + a0[i] + a1[i] - s0[i] - s1[i]);
            }
        }
    }

    /// <summary>
    /// Bucket de sortida d'una posició: per nombre total de peces (reis inclosos), de 2 a 32, en <see cref="Buckets"/> trams iguals.
    /// Ha de coincidir amb <c>MaterialCount</c> de bullet: (peces - 2) / (32 / Buckets).
    /// </summary>
    public static int BucketOf(int pieceCount) => Buckets == 1 ? 0 : Math.Clamp((pieceCount - 2) / (32 / Buckets), 0, Buckets - 1);

    /// <summary>Avaluació en centipeons des del bàndol que juga. <paramref name="bucket"/>: vegeu <see cref="BucketOf"/>.</summary>
    public static int Evaluate(ReadOnlySpan<short> acc, int side, int bucket = 0)
    {
        int h = Hidden;
        int wo = bucket * 2 * h; // desplaçament dels pesos de sortida d'aquest bucket
        var us = acc.Slice(side * h, h);
        var them = acc.Slice((side ^ 1) * h, h);
        long sum = 0;
        int i = 0;
        if (Vector.IsHardwareAccelerated && h % Vector<short>.Count == 0)
        {
            // SCReLU amb vectors: clamp a [0, QA], a² (≤ 65025) en i32 i producte amb el pes (i16 -> i32).
            var zero = Vector<short>.Zero;
            var qa = new Vector<short>(Qa);
            var vsum = Vector<int>.Zero;
            for (; i < h; i += Vector<short>.Count)
            {
                var a = Vector.Min(Vector.Max(new Vector<short>(us[i..]), zero), qa);
                var b = Vector.Min(Vector.Max(new Vector<short>(them[i..]), zero), qa);
                var wa = new Vector<short>(_l1w.AsSpan(wo + i));
                var wb = new Vector<short>(_l1w.AsSpan(wo + h + i));
                Vector.Widen(a, out var a0, out var a1);
                Vector.Widen(b, out var b0, out var b1);
                Vector.Widen(wa, out var wa0, out var wa1);
                Vector.Widen(wb, out var wb0, out var wb1);
                vsum += a0 * a0 * wa0 + a1 * a1 * wa1 + b0 * b0 * wb0 + b1 * b1 * wb1;
            }
            sum = Vector.Sum(vsum);
        }
        else
        {
            for (; i < h; i++)
            {
                int a = Math.Clamp((int)us[i], 0, Qa);
                int b = Math.Clamp((int)them[i], 0, Qa);
                sum += a * a * _l1w[wo + i] + b * b * _l1w[wo + h + i];
            }
        }
        sum /= Qa;
        sum += _l1b[bucket];
        return (int)(sum * Scale / (Qa * Qb));
    }
}
