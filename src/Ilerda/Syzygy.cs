/*
 * Lector de taules de finals Syzygy per a Ilerda.
 *
 * Adaptació a C# de tbprobe.c de Fathom (https://github.com/jdart1/Fathom),
 * que al seu torn deriva del codi de sondeig de Ronald de Man.
 *
 * Copyright (c) 2013-2018 Ronald de Man
 * Copyright (c) 2015 basil00
 * Modifications Copyright (c) 2016-2025 by Jon Dart
 * Adaptació a C# Copyright (c) 2026 Jordi Obis
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics;

namespace Ilerda;

/// <summary>Resultat d'una posició segons les taules, des del bàndol que juga.</summary>
public enum Wdl
{
    Loss = -2,
    BlessedLoss = -1, // derrota que la regla dels 50 jugades converteix en taules
    Draw = 0,
    CursedWin = 1,    // victòria que la regla dels 50 jugades converteix en taules
    Win = 2,
}

/// <summary>
/// Taules de finals Syzygy (WDL). Port de tbprobe.c de Fathom: només el sondeig WDL; el DTZ (per filtrar jugades
/// a l'arrel) encara no hi és. El pla és a <c>tools/syzygy/PLAN.md</c>. Suposa un processador little-endian (x64).
/// </summary>
public static unsafe class Syzygy
{
    // ---------- Estructures ----------

    sealed class PairsData
    {
        public byte* IndexTable;
        public ushort* SizeTable;
        public byte* Data;
        public ushort* Offset;       // sense desplaçar: Offset[l - MinLen] és el de Fathom offset[l]
        public byte[] SymLen = Array.Empty<byte>();
        public byte* SymPat;
        public byte BlockSize, IdxBits, MinLen;
        public byte ConstValue;
        public ulong[] Base = Array.Empty<ulong>(); // Base[l - MinLen]
    }

    sealed class EncInfo
    {
        public PairsData? Precomp;
        public readonly long[] Factor = new long[7];
        public readonly byte[] Pieces = new byte[7];
        public readonly byte[] Norm = new byte[7];
    }

    sealed class BaseEntry
    {
        public string Name = "";
        public string FilePath = "";
        public ulong Key;
        public byte* Data;
        public MemoryMappedFile? Mapping;
        public MemoryMappedViewAccessor? View;
        public volatile bool Ready;
        public volatile bool Error;
        public int Num;
        public bool Symmetric, HasPawns, KkEnc;
        public int Pawns0, Pawns1;
        public readonly EncInfo[] Ei = new EncInfo[8]; // peces: 2 (per bàndol); peons: 4 columnes x 2 bàndols
        // DTZ (fitxer .rtbz)
        public string? DtzPath;
        public MemoryMappedFile? DtzMapping;
        public MemoryMappedViewAccessor? DtzView;
        public volatile bool DtzReady;
        public volatile bool DtzError;
        public readonly EncInfo[] DtzEi = new EncInfo[4];
        public readonly byte[] DtzFlags = new byte[4];
        public byte* DtzMap;
        public readonly ushort[,] DtzMapIdx = new ushort[4, 4];
    }

    // ---------- Estat ----------

    const int PieceEnc = 0, FileEnc = 1, RankEnc = 2;
    const uint WdlMagic = 0x5d23e871, DtzMagic = 0xa50c66d7;
    static readonly int[] WdlToMap = { 1, 3, 0, 2, 0 };
    static readonly byte[] PaFlags = { 8, 0, 0, 0, 4 };
    static readonly int[] WdlToDtz = { -1, -101, 0, 101, 1 };

    static readonly Dictionary<ulong, BaseEntry> _hash = new();
    static readonly List<BaseEntry> _entries = new();
    static readonly Dictionary<string, string> _dtzFiles = new(StringComparer.OrdinalIgnoreCase);
    static readonly object _lock = new();

    /// <summary>Nombre màxim de peces (reis inclosos) de les taules trobades; 0 si no n'hi ha.</summary>
    public static int MaxCardinality { get; private set; }

    /// <summary>True si hi ha taules carregades.</summary>
    public static bool Enabled => MaxCardinality > 0;

    /// <summary>Carpetes ajuntades amb el separador de la plataforma (';' a Windows, ':' a Linux), tal com les dona l'opció UCI.</summary>
    public static string Path { get; private set; } = "";

    const ulong PrimeWhiteQueen = 11811845319353239651UL, PrimeWhiteRook = 10979190538029446137UL,
        PrimeWhiteBishop = 12311744257139811149UL, PrimeWhiteKnight = 15202887380319082783UL,
        PrimeWhitePawn = 17008651141875982339UL, PrimeBlackQueen = 15484752644942473553UL,
        PrimeBlackRook = 18264461213049635989UL, PrimeBlackBishop = 15394650811035483107UL,
        PrimeBlackKnight = 13469005675588064321UL, PrimeBlackPawn = 11695583624105689831UL;

    static readonly long[,] Binomial = new long[7, 64];
    static readonly long[,,] PawnIdx = new long[2, 6, 24];
    static readonly long[,] PawnFactorFile = new long[6, 4];
    static readonly long[,] PawnFactorRank = new long[6, 6];
    static readonly byte[] FileToFile = { 0, 1, 2, 3, 3, 2, 1, 0 };

    static Syzygy()
    {
        for (int i = 0; i < 7; i++)
            for (int j = 0; j < 64; j++)
            {
                long f = 1, l = 1;
                for (int k = 0; k < i; k++) { f *= (j - k); l *= (k + 1); }
                Binomial[i, j] = f / l;
            }

        for (int i = 0; i < 6; i++)
        {
            long s = 0;
            for (int j = 0; j < 24; j++)
            {
                PawnIdx[0, i, j] = s;
                s += Binomial[i, SyzygyTables.PawnTwist[(1 + (j % 6)) * 8 + (j / 6)]];
                if ((j + 1) % 6 == 0) { PawnFactorFile[i, j / 6] = s; s = 0; }
            }
        }

        for (int i = 0; i < 6; i++)
        {
            long s = 0;
            for (int j = 0; j < 24; j++)
            {
                PawnIdx[1, i, j] = s;
                s += Binomial[i, SyzygyTables.PawnTwist[64 + (1 + (j / 4)) * 8 + (j % 4)]];
                if ((j + 1) % 4 == 0) { PawnFactorRank[i, j / 4] = s; s = 0; }
            }
        }
    }

    // ---------- Inicialització ----------

    /// <summary>
    /// Busca fitxers <c>.rtbw</c> i <c>.rtbz</c> a les carpetes de <paramref name="path"/> i prepara les entrades
    /// (el contingut de cada taula es mapa a memòria la primera vegada que es sondeja). Una ruta buida o
    /// <c>&lt;empty&gt;</c> desactiva les taules.
    /// </summary>
    public static bool Init(string path, out string message)
    {
        lock (_lock)
        {
            FreeAll();
            MaxCardinality = 0;
            Path = "";

            if (string.IsNullOrWhiteSpace(path) || path == "<empty>")
            {
                message = "Syzygy desactivat";
                return true;
            }

            int dirs = 0;
            foreach (var dir in path.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(dir)) continue;
                dirs++;
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    string ext = System.IO.Path.GetExtension(file);
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (Cardinality(name) == 0) continue;
                    if (ext.Equals(".rtbz", StringComparison.OrdinalIgnoreCase)) _dtzFiles.TryAdd(name, file);
                    else if (ext.Equals(".rtbw", StringComparison.OrdinalIgnoreCase)) AddTable(name, file);
                }
            }

            foreach (var be in _entries)
                if (_dtzFiles.TryGetValue(be.Name, out var dz) && (new FileInfo(dz).Length & 63) == 16) be.DtzPath = dz;

            Path = path;
            message = dirs == 0
                ? $"Syzygy: cap carpeta existent a '{path}'"
                : $"Syzygy: {_entries.Count} taules WDL i {_dtzFiles.Count} DTZ, fins a {MaxCardinality} peces";
            return dirs > 0;
        }
    }

    static void FreeAll()
    {
        foreach (var be in _entries)
        {
            if (be.View != null)
            {
                be.View.SafeMemoryMappedViewHandle.ReleasePointer();
                be.View.Dispose();
            }
            be.Mapping?.Dispose();
            if (be.DtzView != null)
            {
                be.DtzView.SafeMemoryMappedViewHandle.ReleasePointer();
                be.DtzView.Dispose();
            }
            be.DtzMapping?.Dispose();
        }
        _entries.Clear();
        _hash.Clear();
        _dtzFiles.Clear();
    }

    static ulong KeyFromPcs(int[] pcs, bool mirror)
    {
        int m = mirror ? 8 : 0;
        return unchecked(
            (ulong)pcs[5 ^ m] * PrimeWhiteQueen + (ulong)pcs[4 ^ m] * PrimeWhiteRook +
            (ulong)pcs[3 ^ m] * PrimeWhiteBishop + (ulong)pcs[2 ^ m] * PrimeWhiteKnight +
            (ulong)pcs[1 ^ m] * PrimeWhitePawn + (ulong)pcs[13 ^ m] * PrimeBlackQueen +
            (ulong)pcs[12 ^ m] * PrimeBlackRook + (ulong)pcs[11 ^ m] * PrimeBlackBishop +
            (ulong)pcs[10 ^ m] * PrimeBlackKnight + (ulong)pcs[9 ^ m] * PrimeBlackPawn);
    }

    /// <summary>Equivalent a init_tb de Fathom: crea l'entrada d'una taula WDL a partir del nom del fitxer.</summary>
    static void AddTable(string name, string file)
    {
        if ((new FileInfo(file).Length & 63) != 16) return; // fitxer incomplet

        var pcs = new int[16];
        int color = 0;
        foreach (char c in name)
        {
            if (c == 'v') color = 8;
            else pcs[("PNBRQK".IndexOf(c) + 1) | color]++;
        }

        ulong key = KeyFromPcs(pcs, false), key2 = KeyFromPcs(pcs, true);
        if (_hash.ContainsKey(key)) return;

        bool hasPawns = pcs[1] != 0 || pcs[9] != 0;
        var be = new BaseEntry { Name = name, FilePath = file, Key = key, HasPawns = hasPawns, Symmetric = key == key2 };
        for (int i = 0; i < 16; i++) be.Num += pcs[i];

        if (!hasPawns)
        {
            int j = 0;
            for (int i = 0; i < 16; i++) if (pcs[i] == 1) j++;
            be.KkEnc = j == 2;
        }
        else
        {
            be.Pawns0 = pcs[1];
            be.Pawns1 = pcs[9];
            if (pcs[9] != 0 && (pcs[1] == 0 || pcs[1] > pcs[9])) (be.Pawns0, be.Pawns1) = (be.Pawns1, be.Pawns0);
        }

        _entries.Add(be);
        _hash[key] = be;
        if (key != key2) _hash[key2] = be;
        MaxCardinality = Math.Max(MaxCardinality, be.Num);
    }

    /// <summary>Nombre de peces d'un nom de taula com <c>KRPvKR</c> (5); 0 si no té el format esperat.</summary>
    internal static int Cardinality(string name)
    {
        int v = name.IndexOf('v');
        if (v <= 0 || v == name.Length - 1 || name.IndexOf('v', v + 1) >= 0) return 0;
        foreach (char c in name)
            if (c != 'v' && "KQRBNP".IndexOf(c) < 0) return 0;
        return name.Length - 1;
    }

    // ---------- Lectura d'una taula (init_table, setup_pairs, init_enc_info) ----------

    static long Subfactor(long k, long n)
    {
        long f = n, l = 1;
        for (long i = 1; i < k; i++) { f *= n - i; l *= i + 1; }
        return f / l;
    }

    static long InitEncInfo(EncInfo ei, BaseEntry be, byte* tb, int shift, int t, int enc)
    {
        bool morePawns = enc != PieceEnc && be.Pawns1 > 0;
        int extra = morePawns ? 1 : 0;

        for (int i = 0; i < be.Num; i++)
        {
            ei.Pieces[i] = (byte)((tb[i + 1 + extra] >> shift) & 0x0f);
            ei.Norm[i] = 0;
        }

        int order = (tb[0] >> shift) & 0x0f;
        int order2 = morePawns ? (tb[1] >> shift) & 0x0f : 0x0f;

        int k = enc != PieceEnc ? be.Pawns0 : be.KkEnc ? 2 : 3;
        ei.Norm[0] = (byte)k;

        if (morePawns)
        {
            ei.Norm[k] = (byte)be.Pawns1;
            k += ei.Norm[k];
        }

        for (int i = k; i < be.Num; i += ei.Norm[i])
            for (int j = i; j < be.Num && ei.Pieces[j] == ei.Pieces[i]; j++)
                ei.Norm[i]++;

        int n = 64 - k;
        long f = 1;

        for (int i = 0; k < be.Num || i == order || i == order2; i++)
        {
            if (i == order)
            {
                ei.Factor[0] = f;
                f *= enc == FileEnc ? PawnFactorFile[ei.Norm[0] - 1, t]
                   : enc == RankEnc ? PawnFactorRank[ei.Norm[0] - 1, t]
                   : be.KkEnc ? 462 : 31332;
            }
            else if (i == order2)
            {
                ei.Factor[ei.Norm[0]] = f;
                f *= Subfactor(ei.Norm[ei.Norm[0]], 48 - ei.Norm[0]);
            }
            else
            {
                ei.Factor[k] = f;
                f *= Subfactor(ei.Norm[k], n);
                n -= ei.Norm[k];
                k += ei.Norm[k];
            }
        }

        return f;
    }

    static void CalcSymLen(PairsData d, uint s, bool[] tmp)
    {
        byte* w = d.SymPat + 3 * s;
        uint s2 = ((uint)w[2] << 4) | ((uint)w[1] >> 4);
        if (s2 == 0x0fff)
            d.SymLen[s] = 0;
        else
        {
            uint s1 = (((uint)w[1] & 0xf) << 8) | w[0];
            if (!tmp[s1]) CalcSymLen(d, s1, tmp);
            if (!tmp[s2]) CalcSymLen(d, s2, tmp);
            d.SymLen[s] = (byte)(d.SymLen[s1] + d.SymLen[s2] + 1);
        }
        tmp[s] = true;
    }

    static PairsData SetupPairs(ref byte* ptr, long tbSize, out long size0, out long size1, out long size2, out byte flags, bool wdl = true)
    {
        var d = new PairsData();
        byte* data = ptr;

        flags = data[0];
        if ((data[0] & 0x80) != 0)
        {
            d.IdxBits = 0;
            d.ConstValue = wdl ? data[1] : (byte)0;
            ptr = data + 2;
            size0 = size1 = size2 = 0;
            return d;
        }

        byte blockSize = data[1];
        byte idxBits = data[2];
        uint realNumBlocks = *(uint*)(data + 4);
        uint numBlocks = realNumBlocks + data[3];
        int maxLen = data[8];
        int minLen = data[9];
        int h = maxLen - minLen + 1;
        uint numSyms = *(ushort*)(data + 10 + 2 * h);

        d.BlockSize = blockSize;
        d.IdxBits = idxBits;
        d.Offset = (ushort*)(data + 10);
        d.SymLen = new byte[numSyms];
        d.SymPat = data + 12 + 2 * h;
        d.MinLen = (byte)minLen;
        d.Base = new ulong[h];
        ptr = data + 12 + 2 * h + 3 * numSyms + (numSyms & 1);

        long numIndices = (tbSize + (1L << idxBits) - 1) >> idxBits;
        size0 = 6L * numIndices;
        size1 = 2L * numBlocks;
        size2 = (long)realNumBlocks << blockSize;

        var tmp = new bool[numSyms];
        for (uint s = 0; s < numSyms; s++)
            if (!tmp[s]) CalcSymLen(d, s, tmp);

        d.Base[h - 1] = 0;
        for (int i = h - 2; i >= 0; i--)
            d.Base[i] = unchecked((d.Base[i + 1] + d.Offset[i] - d.Offset[i + 1]) / 2);
        for (int i = 0; i < h; i++)
            d.Base[i] <<= 64 - (minLen + i);

        return d;
    }

    /// <summary>Mapa el fitxer .rtbw i en llegeix la capçalera (equivalent a init_table per a WDL).</summary>
    static bool InitTable(BaseEntry be)
    {
        MemoryMappedFile? mf = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            mf = MemoryMappedFile.CreateFromFile(be.FilePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            view = mf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* basePtr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            basePtr += view.PointerOffset;
            byte* data = basePtr;

            if (*(uint*)data != WdlMagic)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                view.Dispose();
                mf.Dispose();
                return false;
            }

            be.Mapping = mf;
            be.View = view;
            be.Data = data;

            bool split = (data[4] & 0x01) != 0;
            data += 5;

            int num = be.HasPawns ? 4 : 1;
            int enc = be.HasPawns ? FileEnc : PieceEnc;

            var tbSize = new long[4, 2];
            for (int t = 0; t < num; t++)
            {
                be.Ei[t] = new EncInfo();
                tbSize[t, 0] = InitEncInfo(be.Ei[t], be, data, 0, t, enc);
                if (split)
                {
                    be.Ei[num + t] = new EncInfo();
                    tbSize[t, 1] = InitEncInfo(be.Ei[num + t], be, data, 4, t, enc);
                }
                data += be.Num + 1 + (be.HasPawns && be.Pawns1 != 0 ? 1 : 0);
            }
            data += (long)data & 1;

            var size = new long[4, 2, 3];
            for (int t = 0; t < num; t++)
            {
                be.Ei[t].Precomp = SetupPairs(ref data, tbSize[t, 0], out size[t, 0, 0], out size[t, 0, 1], out size[t, 0, 2], out _);
                if (split)
                    be.Ei[num + t].Precomp = SetupPairs(ref data, tbSize[t, 1], out size[t, 1, 0], out size[t, 1, 1], out size[t, 1, 2], out _);
            }

            for (int t = 0; t < num; t++)
            {
                be.Ei[t].Precomp!.IndexTable = data;
                data += size[t, 0, 0];
                if (split)
                {
                    be.Ei[num + t].Precomp!.IndexTable = data;
                    data += size[t, 1, 0];
                }
            }

            for (int t = 0; t < num; t++)
            {
                be.Ei[t].Precomp!.SizeTable = (ushort*)data;
                data += size[t, 0, 1];
                if (split)
                {
                    be.Ei[num + t].Precomp!.SizeTable = (ushort*)data;
                    data += size[t, 1, 1];
                }
            }

            for (int t = 0; t < num; t++)
            {
                data = (byte*)(((long)data + 0x3f) & ~0x3fL);
                be.Ei[t].Precomp!.Data = data;
                data += size[t, 0, 2];
                if (split)
                {
                    data = (byte*)(((long)data + 0x3f) & ~0x3fL);
                    be.Ei[num + t].Precomp!.Data = data;
                    data += size[t, 1, 2];
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Mapa el fitxer .rtbz i en llegeix la capçalera (equivalent a init_table per a DTZ).</summary>
    static bool InitDtzTable(BaseEntry be)
    {
        MemoryMappedFile? mf = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            mf = MemoryMappedFile.CreateFromFile(be.DtzPath!, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            view = mf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* basePtr = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);
            basePtr += view.PointerOffset;
            byte* data = basePtr;

            if (*(uint*)data != DtzMagic)
            {
                view.SafeMemoryMappedViewHandle.ReleasePointer();
                view.Dispose();
                mf.Dispose();
                return false;
            }

            be.DtzMapping = mf;
            be.DtzView = view;

            data += 5; // el DTZ no es divideix per bàndols

            int num = be.HasPawns ? 4 : 1;
            int enc = be.HasPawns ? FileEnc : PieceEnc;

            var tbSize = new long[4];
            for (int t = 0; t < num; t++)
            {
                be.DtzEi[t] = new EncInfo();
                tbSize[t] = InitEncInfo(be.DtzEi[t], be, data, 0, t, enc);
                data += be.Num + 1 + (be.HasPawns && be.Pawns1 != 0 ? 1 : 0);
            }
            data += (long)data & 1;

            var size = new long[4, 3];
            for (int t = 0; t < num; t++)
            {
                be.DtzEi[t].Precomp = SetupPairs(ref data, tbSize[t], out size[t, 0], out size[t, 1], out size[t, 2], out byte fl, false);
                be.DtzFlags[t] = fl;
            }

            byte* map = data;
            be.DtzMap = map;
            for (int t = 0; t < num; t++)
            {
                if ((be.DtzFlags[t] & 2) == 0) continue;
                if ((be.DtzFlags[t] & 16) == 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        be.DtzMapIdx[t, i] = (ushort)(data + 1 - map);
                        data += 1 + data[0];
                    }
                }
                else
                {
                    data += (long)data & 1;
                    for (int i = 0; i < 4; i++)
                    {
                        be.DtzMapIdx[t, i] = (ushort)((ushort*)data + 1 - (ushort*)map);
                        data += 2 + 2 * *(ushort*)data;
                    }
                }
            }
            data += (long)data & 1;

            for (int t = 0; t < num; t++)
            {
                be.DtzEi[t].Precomp!.IndexTable = data;
                data += size[t, 0];
            }
            for (int t = 0; t < num; t++)
            {
                be.DtzEi[t].Precomp!.SizeTable = (ushort*)data;
                data += size[t, 1];
            }
            for (int t = 0; t < num; t++)
            {
                data = (byte*)(((long)data + 0x3f) & ~0x3fL);
                be.DtzEi[t].Precomp!.Data = data;
                data += size[t, 2];
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------- Codificació i descompressió ----------

    static int LeadingPawn(Span<int> p, BaseEntry be, int enc)
    {
        int o = (enc - 1) * 64;
        for (int i = 1; i < be.Pawns0; i++)
            if (SyzygyTables.Flap[o + p[0]] > SyzygyTables.Flap[o + p[i]])
                (p[0], p[i]) = (p[i], p[0]);

        return enc == FileEnc ? FileToFile[p[0] & 7] : (p[0] - 8) >> 3;
    }

    static long Encode(Span<int> p, EncInfo ei, BaseEntry be, int enc)
    {
        int n = be.Num;
        long idx;
        int k;

        if ((p[0] & 0x04) != 0)
            for (int i = 0; i < n; i++) p[i] ^= 0x07;

        if (enc == PieceEnc)
        {
            if ((p[0] & 0x20) != 0)
                for (int i = 0; i < n; i++) p[i] ^= 0x38;

            for (int i = 0; i < n; i++)
                if (SyzygyTables.OffDiag[p[i]] != 0)
                {
                    if (SyzygyTables.OffDiag[p[i]] > 0 && i < (be.KkEnc ? 2 : 3))
                        for (int j = 0; j < n; j++) p[j] = SyzygyTables.FlipDiag[p[j]];
                    break;
                }

            if (be.KkEnc)
            {
                idx = SyzygyTables.KKIdx[SyzygyTables.Triangle[p[0]] * 64 + p[1]];
                k = 2;
            }
            else
            {
                int s1 = p[1] > p[0] ? 1 : 0;
                int s2 = (p[2] > p[0] ? 1 : 0) + (p[2] > p[1] ? 1 : 0);

                if (SyzygyTables.OffDiag[p[0]] != 0)
                    idx = SyzygyTables.Triangle[p[0]] * 63 * 62 + (p[1] - s1) * 62 + (p[2] - s2);
                else if (SyzygyTables.OffDiag[p[1]] != 0)
                    idx = 6 * 63 * 62 + SyzygyTables.Diag[p[0]] * 28 * 62 + SyzygyTables.Lower[p[1]] * 62 + p[2] - s2;
                else if (SyzygyTables.OffDiag[p[2]] != 0)
                    idx = 6 * 63 * 62 + 4 * 28 * 62 + SyzygyTables.Diag[p[0]] * 7 * 28 + (SyzygyTables.Diag[p[1]] - s1) * 28 + SyzygyTables.Lower[p[2]];
                else
                    idx = 6 * 63 * 62 + 4 * 28 * 62 + 4 * 7 * 28 + SyzygyTables.Diag[p[0]] * 7 * 6 + (SyzygyTables.Diag[p[1]] - s1) * 6 + (SyzygyTables.Diag[p[2]] - s2);
                k = 3;
            }
            idx *= ei.Factor[0];
        }
        else
        {
            int o = (enc - 1) * 64;
            for (int i = 1; i < be.Pawns0; i++)
                for (int j = i + 1; j < be.Pawns0; j++)
                    if (SyzygyTables.PawnTwist[o + p[i]] < SyzygyTables.PawnTwist[o + p[j]])
                        (p[i], p[j]) = (p[j], p[i]);

            k = be.Pawns0;
            idx = PawnIdx[enc - 1, k - 1, SyzygyTables.Flap[o + p[0]]];
            for (int i = 1; i < k; i++)
                idx += Binomial[k - i, SyzygyTables.PawnTwist[o + p[i]]];
            idx *= ei.Factor[0];

            // Peons de l'altre color
            if (be.Pawns1 != 0)
            {
                int t = k + be.Pawns1;
                for (int i = k; i < t; i++)
                    for (int j = i + 1; j < t; j++)
                        if (p[i] > p[j]) (p[i], p[j]) = (p[j], p[i]);
                long s = 0;
                for (int i = k; i < t; i++)
                {
                    int sq = p[i];
                    int skips = 0;
                    for (int j = 0; j < k; j++) skips += sq > p[j] ? 1 : 0;
                    s += Binomial[i - k + 1, sq - skips - 8];
                }
                idx += s * ei.Factor[k];
                k = t;
            }
        }

        while (k < n)
        {
            int t = k + ei.Norm[k];
            for (int i = k; i < t; i++)
                for (int j = i + 1; j < t; j++)
                    if (p[i] > p[j]) (p[i], p[j]) = (p[j], p[i]);
            long s = 0;
            for (int i = k; i < t; i++)
            {
                int sq = p[i];
                int skips = 0;
                for (int j = 0; j < k; j++) skips += sq > p[j] ? 1 : 0;
                s += Binomial[i - k + 1, sq - skips];
            }
            idx += s * ei.Factor[k];
            k = t;
        }

        return idx;
    }

    /// <summary>Descomprimeix el valor de l'índex <paramref name="idx"/>: retorna w[0] | w[1] &lt;&lt; 8.</summary>
    static int DecompressPairs(PairsData d, long idx)
    {
        if (d.IdxBits == 0) return d.ConstValue;

        uint mainIdx = (uint)(idx >> d.IdxBits);
        int litIdx = (int)((idx & ((1L << d.IdxBits) - 1)) - (1L << (d.IdxBits - 1)));
        uint block = *(uint*)(d.IndexTable + 6 * (long)mainIdx);
        litIdx += *(ushort*)(d.IndexTable + 6 * (long)mainIdx + 4);

        if (litIdx < 0)
            while (litIdx < 0)
                litIdx += d.SizeTable[--block] + 1;
        else
            while (litIdx > d.SizeTable[block])
                litIdx -= d.SizeTable[block++] + 1;

        uint* ptr = (uint*)(d.Data + ((long)block << d.BlockSize));

        int m = d.MinLen;
        ulong[] bas = d.Base;
        byte[] symLen = d.SymLen;
        uint sym;

        ulong code = BinaryPrimitives.ReverseEndianness(*(ulong*)ptr);
        ptr += 2;
        int bitCnt = 0; // nombre de bits buits a code
        for (; ; )
        {
            int l = m;
            while (code < bas[l - m]) l++;
            sym = d.Offset[l - m];
            sym += (uint)((code - bas[l - m]) >> (64 - l));
            if (litIdx < symLen[sym] + 1) break;
            litIdx -= symLen[sym] + 1;
            code <<= l;
            bitCnt += l;
            if (bitCnt >= 32)
            {
                bitCnt -= 32;
                uint tmp = BinaryPrimitives.ReverseEndianness(*ptr++);
                code |= (ulong)tmp << bitCnt;
            }
        }

        byte* symPat = d.SymPat;
        while (symLen[sym] != 0)
        {
            byte* w = symPat + 3 * sym;
            uint s1 = (((uint)w[1] & 0xf) << 8) | w[0];
            if (litIdx < symLen[s1] + 1)
                sym = s1;
            else
            {
                litIdx -= symLen[s1] + 1;
                sym = ((uint)w[2] << 4) | ((uint)w[1] >> 4);
            }
        }

        return symPat[3 * sym] | (symPat[3 * sym + 1] << 8);
    }

    // ---------- Sondeig ----------

    static ulong CalcKey(Position pos)
    {
        return unchecked(
            (ulong)BitOperations.PopCount(pos.Bb[Pc.Queen]) * PrimeWhiteQueen +
            (ulong)BitOperations.PopCount(pos.Bb[Pc.Rook]) * PrimeWhiteRook +
            (ulong)BitOperations.PopCount(pos.Bb[Pc.Bishop]) * PrimeWhiteBishop +
            (ulong)BitOperations.PopCount(pos.Bb[Pc.Knight]) * PrimeWhiteKnight +
            (ulong)BitOperations.PopCount(pos.Bb[Pc.Pawn]) * PrimeWhitePawn +
            (ulong)BitOperations.PopCount(pos.Bb[6 + Pc.Queen]) * PrimeBlackQueen +
            (ulong)BitOperations.PopCount(pos.Bb[6 + Pc.Rook]) * PrimeBlackRook +
            (ulong)BitOperations.PopCount(pos.Bb[6 + Pc.Bishop]) * PrimeBlackBishop +
            (ulong)BitOperations.PopCount(pos.Bb[6 + Pc.Knight]) * PrimeBlackKnight +
            (ulong)BitOperations.PopCount(pos.Bb[6 + Pc.Pawn]) * PrimeBlackPawn);
    }

    /// <summary>
    /// Omple p[i..] amb les caselles de les peces del tipus pc[i] (1 = peó blanc ... 14 = rei negre; xor 8 si flip).
    /// Les peces iguals són consecutives.
    /// </summary>
    static int FillSquares(Position pos, byte[] pc, bool flip, int mirror, Span<int> p, int i)
    {
        int color = pc[i] >> 3;
        if (flip) color ^= 1;
        ulong bb = pos.Bb[color * 6 + (pc[i] & 7) - 1];
        do
        {
            p[i++] = BitOperations.TrailingZeroCount(bb) ^ mirror;
            bb &= bb - 1;
        } while (bb != 0);
        return i;
    }

    /// <summary>Valor WDL (-2..2) de la taula per a la posició; false si no hi ha taula o no es pot llegir.</summary>
    static bool ProbeWdlTable(Position pos, out int value)
    {
        value = 0;
        ulong key = CalcKey(pos);
        if (key == 0) return true; // KvK

        if (!_hash.TryGetValue(key, out var be) || be.Error) return false;

        if (!be.Ready)
        {
            lock (_lock)
            {
                if (be.Error) return false;
                if (!be.Ready)
                {
                    if (!InitTable(be)) { be.Error = true; return false; }
                    be.Ready = true;
                }
            }
        }

        bool whiteToMove = pos.Side == 0;
        bool flip, bside;
        if (!be.Symmetric)
        {
            flip = key != be.Key;
            bside = whiteToMove == flip;
        }
        else
        {
            flip = !whiteToMove;
            bside = false;
        }

        Span<int> p = stackalloc int[7];
        EncInfo ei;
        long idx;

        if (!be.HasPawns)
        {
            ei = be.Ei[bside ? 1 : 0];
            for (int i = 0; i < be.Num;)
                i = FillSquares(pos, ei.Pieces, flip, 0, p, i);
            idx = Encode(p, ei, be, PieceEnc);
        }
        else
        {
            ei = be.Ei[0];
            int mirror = flip ? 0x38 : 0;
            int i = FillSquares(pos, ei.Pieces, flip, mirror, p, 0);
            int t = LeadingPawn(p, be, FileEnc);
            ei = be.Ei[t + (bside ? 4 : 0)];
            while (i < be.Num)
                i = FillSquares(pos, ei.Pieces, flip, mirror, p, i);
            idx = Encode(p, ei, be, FileEnc);
        }

        if (ei.Precomp == null) return false;
        value = (DecompressPairs(ei.Precomp, idx) & 0xff) - 2;
        return true;
    }

    /// <summary>Valor DTZ cru de la taula (en mitges jugades, sense el signe); 1 = bé, 0 = no hi ha taula, -1 = caldria l'altre bàndol.</summary>
    static int ProbeDtzTable(Position pos, int wdl, out int value)
    {
        value = 0;
        ulong key = CalcKey(pos);
        if (!_hash.TryGetValue(key, out var be) || be.DtzPath == null || be.DtzError) return 0;

        if (!be.DtzReady)
        {
            lock (_lock)
            {
                if (be.DtzError) return 0;
                if (!be.DtzReady)
                {
                    if (!InitDtzTable(be)) { be.DtzError = true; return 0; }
                    be.DtzReady = true;
                }
            }
        }

        bool whiteToMove = pos.Side == 0;
        bool flip, bside;
        if (!be.Symmetric)
        {
            flip = key != be.Key;
            bside = whiteToMove == flip;
        }
        else
        {
            flip = !whiteToMove;
            bside = false;
        }

        Span<int> p = stackalloc int[7];
        EncInfo ei;
        long idx;
        int t = 0;
        byte flags;

        if (!be.HasPawns)
        {
            flags = be.DtzFlags[0];
            if (((flags & 1) != 0) != bside && !be.Symmetric) return -1;
            ei = be.DtzEi[0];
            for (int i = 0; i < be.Num;)
                i = FillSquares(pos, ei.Pieces, flip, 0, p, i);
            idx = Encode(p, ei, be, PieceEnc);
        }
        else
        {
            ei = be.DtzEi[0];
            int mirror = flip ? 0x38 : 0;
            int i = FillSquares(pos, ei.Pieces, flip, mirror, p, 0);
            t = LeadingPawn(p, be, FileEnc);
            flags = be.DtzFlags[t];
            if (((flags & 1) != 0) != bside && !be.Symmetric) return -1;
            ei = be.DtzEi[t];
            while (i < be.Num)
                i = FillSquares(pos, ei.Pieces, flip, mirror, p, i);
            idx = Encode(p, ei, be, FileEnc);
        }

        if (ei.Precomp == null) return 0;
        int r = DecompressPairs(ei.Precomp, idx);
        int v = (r & 0xff) | (r & 0x0f00);

        if ((flags & 2) != 0)
        {
            int m = WdlToMap[wdl + 2];
            if ((flags & 16) == 0)
                v = be.DtzMap[be.DtzMapIdx[t, m] + v];
            else
                v = ((ushort*)be.DtzMap)[be.DtzMapIdx[t, m] + v];
        }
        if ((flags & PaFlags[wdl + 2]) == 0 || (wdl & 1) != 0) v *= 2;

        value = v;
        return 1;
    }

    static bool IsCapture(Position pos, int m) =>
        MoveGen.Flag(m) == MoveGen.FlagEp || pos.Board[MoveGen.To(m)] != -1;

    // No es crida per a posicions amb captura al pas.
    static int ProbeAb(Position pos, int alpha, int beta, ref bool ok)
    {
        Span<int> moves = stackalloc int[256];
        int n = MoveGen.Generate(pos, moves, true);
        for (int i = 0; i < n; i++)
        {
            int m = moves[i];
            if (!IsCapture(pos, m)) continue;
            if (!pos.MakeMove(m)) continue; // jugada il·legal
            int v = -ProbeAb(pos, -beta, -alpha, ref ok);
            pos.UnmakeMove();
            if (!ok) return 0;
            if (v > alpha)
            {
                if (v >= beta) return v;
                alpha = v;
            }
        }

        if (!ProbeWdlTable(pos, out int t)) { ok = false; return 0; }
        return alpha >= t ? alpha : t;
    }

    /// <summary>
    /// Sondeja WDL d'una posició (equivalent a probe_wdl de Fathom). Només és vàlid sense drets d'enroc.
    /// Retorna false si no hi ha taula per a aquest material o no es pot llegir.
    /// </summary>
    public static bool ProbeWdl(Position pos, out Wdl wdl) => ProbeWdlEx(pos, out wdl, out _);

    /// <summary>Com <see cref="ProbeWdl"/>; <paramref name="special"/> és l'"èxit 2" de Fathom (captura guanyadora o captura al pas com a única millor).</summary>
    static bool ProbeWdlEx(Position pos, out Wdl wdl, out bool special)
    {
        wdl = Wdl.Draw;
        special = false;
        if (!Enabled || pos.Castling != 0) return false;
        if (BitOperations.PopCount(pos.Occ) > MaxCardinality) return false;

        bool ok = true;
        Span<int> moves = stackalloc int[256];
        int n = MoveGen.Generate(pos, moves, true);
        int bestCap = -3, bestEp = -3;

        // Resolució de captures: bestCap és la millor captura sense al pas, bestEp la millor captura al pas.
        for (int i = 0; i < n; i++)
        {
            int m = moves[i];
            if (!IsCapture(pos, m)) continue;
            if (!pos.MakeMove(m)) continue;
            int v = -ProbeAb(pos, -2, -bestCap, ref ok);
            pos.UnmakeMove();
            if (!ok) return false;
            if (v > bestCap)
            {
                if (v == 2) { wdl = Wdl.Win; special = true; return true; }
                if (MoveGen.Flag(m) != MoveGen.FlagEp) bestCap = v;
                else if (v > bestEp) bestEp = v;
            }
        }

        if (!ProbeWdlTable(pos, out int tv)) return false;

        if (bestEp > bestCap)
        {
            if (bestEp > tv) { wdl = (Wdl)bestEp; special = true; return true; } // la captura al pas és la millor
            bestCap = bestEp;
        }

        if (bestCap >= tv)
        {
            wdl = (Wdl)bestCap;
            special = bestCap > 0;
            return true;
        }

        // Cas d'ofegat: la taula no compta les captures al pas.
        if (bestEp > -3 && tv == 0)
        {
            Span<int> all = stackalloc int[256];
            int cnt = MoveGen.Generate(pos, all);
            bool anyLegal = false;
            for (int i = 0; i < cnt && !anyLegal; i++)
            {
                if (MoveGen.Flag(all[i]) == MoveGen.FlagEp) continue;
                if (pos.MakeMove(all[i])) { pos.UnmakeMove(); anyLegal = true; }
            }
            if (!anyLegal && !pos.InCheck())
            {
                wdl = (Wdl)bestEp;
                special = true;
                return true;
            }
        }

        wdl = (Wdl)tv;
        return true;
    }

    static bool IsMate(Position pos)
    {
        if (!pos.InCheck()) return false;
        Span<int> mv = stackalloc int[256];
        int n = MoveGen.Generate(pos, mv);
        for (int i = 0; i < n; i++)
            if (pos.MakeMove(mv[i])) { pos.UnmakeMove(); return false; }
        return true;
    }

    static bool IsPawnMove(Position pos, int m) => pos.Board[MoveGen.From(m)] % 6 == Pc.Pawn;

    /// <summary>
    /// DTZ d'una posició (equivalent a probe_dtz de Fathom), en mitges jugades fins al proper canvi (captura o moviment de peó):
    /// positiu si guanya el bàndol que juga, negatiu si perd, 0 si taules; 101 o més per a les victòries "maleïdes".
    /// Retorna false si no hi ha les taules necessàries.
    /// </summary>
    public static bool ProbeDtz(Position pos, out int dtz)
    {
        dtz = 0;
        if (!ProbeWdlEx(pos, out var w, out bool special)) return false;
        int wdl = (int)w;
        if (wdl == 0) return true;
        if (special) { dtz = WdlToDtz[wdl + 2]; return true; }

        Span<int> moves = stackalloc int[256];
        int n;

        if (wdl > 0)
        {
            // Una jugada de peó sense captura que mantingui la victòria dona DTZ = 1 (o 101).
            n = MoveGen.Generate(pos, moves);
            for (int i = 0; i < n; i++)
            {
                int m = moves[i];
                if (!IsPawnMove(pos, m) || IsCapture(pos, m)) continue;
                if (!pos.MakeMove(m)) continue;
                bool ok1 = ProbeWdlEx(pos, out var w1, out _);
                pos.UnmakeMove();
                if (!ok1) return false;
                if (-(int)w1 == wdl) { dtz = WdlToDtz[wdl + 2]; return true; }
            }
        }

        int st = ProbeDtzTable(pos, wdl, out int raw);
        if (st > 0)
        {
            dtz = WdlToDtz[wdl + 2] + (wdl > 0 ? raw : -raw);
            return true;
        }
        if (st == 0) return false;

        // st < 0: la taula és de l'altre bàndol; cal mirar les jugades i sondejar el DTZ de la posició resultant.
        int best = wdl > 0 ? int.MaxValue : WdlToDtz[wdl + 2];
        n = MoveGen.Generate(pos, moves);
        for (int i = 0; i < n; i++)
        {
            int m = moves[i];
            if (IsCapture(pos, m) || IsPawnMove(pos, m)) continue;
            if (!pos.MakeMove(m)) continue;
            bool ok2 = ProbeDtz(pos, out int d);
            bool mate = ok2 && -d == 1 && IsMate(pos);
            pos.UnmakeMove();
            if (!ok2) return false;
            int v = -d;
            if (v == 1 && mate) best = 1;
            else if (wdl > 0) { if (v > 0 && v + 1 < best) best = v + 1; }
            else if (v - 1 < best) best = v - 1;
        }
        dtz = best;
        return true;
    }

    /// <summary>
    /// Jugades de l'arrel que conserven el millor resultat segons les taules (equivalent a root_probe_dtz / root_probe_wdl de Fathom).
    /// Amb victòria queden només les que guanyen més de pressa (DTZ mínim) sense passar-se de la regla dels 50; amb derrota, les que
    /// resisteixen més; amb taules, les que no perden. Retorna null si la posició no és a les taules.
    /// </summary>
    public static int[]? RootMoves(Position pos, int pieceLimit)
    {
        if (!Enabled || pos.Castling != 0) return null;
        int pieces = BitOperations.PopCount(pos.Occ);
        if (pieces > MaxCardinality || pieces > pieceLimit) return null;

        Span<int> all = stackalloc int[320];
        int n = MoveGen.Generate(pos, all);
        var mv = new List<int>();
        var rank = new List<int>();
        var vs = new List<int>();
        int cnt50 = pos.Halfmove;
        bool useDtz = true;

        for (int pass = 0; pass < 2; pass++)
        {
            mv.Clear(); rank.Clear(); vs.Clear();
            bool ok = true;
            for (int i = 0; i < n && ok; i++)
            {
                int m = all[i];
                if (!pos.MakeMove(m)) continue;
                int v = 0, r = 0;
                if (pass == 0)
                {
                    if (pos.Halfmove == 0)
                    {
                        if (ProbeWdl(pos, out var w)) v = WdlToDtz[-(int)w + 2]; else ok = false;
                    }
                    else if (ProbeDtz(pos, out int d))
                    {
                        v = -d;
                        if (v > 0) v++; else if (v < 0) v--;
                        if (v == 2 && IsMate(pos)) v = 1;
                    }
                    else ok = false;
                    r = v > 0 ? (v + cnt50 <= 99 ? 1000 : 1000 - (v + cnt50))
                      : v < 0 ? (-v * 2 + cnt50 < 100 ? -1000 : -1000 + (-v + cnt50))
                      : 0;
                }
                else
                {
                    if (ProbeWdl(pos, out var w)) r = new[] { -1000, -899, 0, 899, 1000 }[-(int)w + 2]; else ok = false;
                }
                pos.UnmakeMove();
                mv.Add(m); rank.Add(r); vs.Add(v);
            }
            if (ok) { useDtz = pass == 0; break; }
            if (pass == 1) return null;
        }

        int best = int.MinValue;
        foreach (int r in rank) best = Math.Max(best, r);

        // Entre les victòries "segures" (rang 1000) es tria el DTZ mínim; entre les derrotes "segures", el màxim.
        int target = 0;
        if (useDtz && best == 1000) { target = int.MaxValue; for (int i = 0; i < mv.Count; i++) if (rank[i] == best) target = Math.Min(target, vs[i]); }
        else if (useDtz && best == -1000) { target = int.MaxValue; for (int i = 0; i < mv.Count; i++) if (rank[i] == best) target = Math.Min(target, vs[i]); }

        var keep = new List<int>();
        for (int i = 0; i < mv.Count; i++)
            if (rank[i] == best && (target == 0 || vs[i] == target)) keep.Add(mv[i]);
        return keep.ToArray();
    }
}
