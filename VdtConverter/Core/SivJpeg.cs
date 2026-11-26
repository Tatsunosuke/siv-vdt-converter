namespace VdtConverter.Core;

/// <summary>
/// SiV の「JPEG 類似圧縮」(VDT の色質 1〜3)。SIV097C.X の圧縮・展開ルーチンを逆アセンブルして移植したもの。
///
/// 色質 1 = 8×8 の MCU、Y Cb Cr 各 1 ブロック (4:4:4)
/// 色質 2 = 16×8 の MCU、Y 2 ブロック + Cb + Cr (4:2:2)
/// 色質 3 = 16×16 の MCU、Y 4 ブロック + Cb + Cr (4:2:0)。最下段は 8 ライン分だけ使う
///
/// ・量子化表は JPEG 標準表 × 10 / 画質 を切り捨てたものの「符号を反転した」値 (0 になったら 1)。
///   量子化・逆量子化とも同じ表を使うので符号は打ち消し合う
/// ・ハフマン符号は JPEG 標準の DC/AC 表 (輝度・色差)。ビットは MSB から詰め、0xFF のスタッフィングもマーカーも無い
/// ・DC の予測値はフレームの先頭で 0 に戻す
///
/// 圧縮側は SiV.X と 1 バイト単位で同じ結果になる (68000 エミュレータで SiV.X 自身の出力と照合済み)。
/// ただし SiV の展開ルーチンには次の 2 つの弱点があり、SiV 自身が作ったデータでも絵が崩れたり暴走したりする。
///   1. 色差の AC 符号「ラン 1・サイズ 8」(0x18) を読み違える → 以降のビットがずれて、メモリを書き壊すことがある
///   2. 逆 DCT の掛け算表が ±1023 までしか無い → 逆量子化した係数がそれを超えると、その 8×8 が崩れる
/// safe = true (既定) のときは、この 2 つに当たる係数だけを少し小さくして避ける (ほとんどのフレームは SiV と同じバイト列のまま)。
/// 展開側の逆 DCT は SiV の表引き版ではなく浮動小数点の計算なので、5bit の色で ±1 程度ずれることがある。
/// </summary>
public static class SivJpeg
{
    public const int Width = 128, Height = 120;

    static readonly int[] LumaTable =
    {
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    };

    static readonly int[] ChromaTable =
    {
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    };

    static readonly int[] ZigZag =
    {
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
    };

    /// <summary>色質ごとの MCU の幅・高さ・ブロック数。</summary>
    public static (int W, int H, int Blocks) Mcu(int colorType) => colorType switch
    {
        1 => (8, 8, 3),
        2 => (16, 8, 4),
        3 => (16, 16, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(colorType), "色質は 1〜3 です。"),
    };

    /// <summary>SiV と同じ量子化表 (輝度, 色差)。値は負になる。</summary>
    public static (short[] Luma, short[] Chroma) QuantTables(int quality)
    {
        if (quality < 1 || quality > 100) throw new ArgumentOutOfRangeException(nameof(quality), "画質は 1〜100 です。");
        static short[] Make(int[] t, int q)
        {
            var r = new short[64];
            for (int i = 0; i < 64; i++)
            {
                int v = -(t[i] * 10 / q);   // SiV の float 計算と全画質・全要素で一致することを確認済み
                r[i] = (short)(v == 0 ? 1 : v);
            }
            return r;
        }
        return (Make(LumaTable, quality), Make(ChromaTable, quality));
    }

    // =====================================================================
    // ハフマン表 (JPEG 標準)
    // =====================================================================

    sealed class Huff
    {
        public readonly ushort[] Code = new ushort[256];
        public readonly byte[] Len = new byte[256];
        // 復号用: 長さ l の符号の最小値・最大値と、値の並び
        public readonly int[] MinCode = new int[17], MaxCode = new int[17], ValPtr = new int[17];
        public readonly byte[] Vals;

        public Huff(byte[] bits, byte[] vals)
        {
            Vals = vals;
            int code = 0, k = 0;
            for (int l = 1; l <= 16; l++)
            {
                ValPtr[l] = k;
                MinCode[l] = code;
                for (int i = 0; i < bits[l - 1]; i++)
                {
                    Code[vals[k]] = (ushort)code;
                    Len[vals[k]] = (byte)l;
                    k++; code++;
                }
                MaxCode[l] = bits[l - 1] > 0 ? code - 1 : -1;
                code <<= 1;
            }
        }
    }

    static readonly Huff DcLuma = new(
        new byte[] { 0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0 },
        new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

    static readonly Huff DcChroma = new(
        new byte[] { 0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 },
        new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 });

    static readonly Huff AcLuma = new(
        new byte[] { 0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d },
        new byte[]
        {
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
            0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
            0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
            0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
            0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
            0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
            0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
        });

    static readonly Huff AcChroma = new(
        new byte[] { 0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77 },
        new byte[]
        {
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
            0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0, 0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
            0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
            0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
            0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
            0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
            0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
        });

    // =====================================================================
    // 圧縮
    // =====================================================================

    // SiV の整数 DCT の係数 (cos 表 × 4096)。1 段目は (Σ + 0x800) >> 12、2 段目は (Σ + 0x4000) >> 15。
    // 0 番と 4 番は掛け算をせず、1 段目はそのまま足し引き、2 段目は 16bit で足して >> 3 (切り捨て)。
    static readonly int[,] K =
    {
        { 4096, 4096, 4096, 4096, 4096, 4096, 4096, 4096 },
        { 5681, 4816, 3218, 1130, -1130, -3218, -4816, -5681 },
        { 5352, 2217, -2217, -5352, -5352, -2217, 2217, 5352 },
        { 4816, -1130, -5681, -3218, 3218, 5681, 1130, -4816 },
        { 4096, -4096, -4096, 4096, 4096, -4096, -4096, 4096 },
        { 3218, -5681, 1130, 4816, -4816, -1130, 5681, -3218 },
        { 2217, -5352, 5352, -2217, -2217, 5352, -5352, 2217 },
        { 1130, -3218, 4816, -5681, 5681, -4816, 3218, -1130 },
    };

    static short W16(int v) => unchecked((short)v);

    /// <summary>SiV の順方向 DCT。block は 64 個 (行優先)、その場で係数に置き換える。</summary>
    static void Fdct(short[] b)
    {
        Span<short> t = stackalloc short[64];
        for (int r = 0; r < 8; r++)
            for (int u = 0; u < 8; u++)
            {
                int s = 0;
                for (int j = 0; j < 8; j++) s += K[u, j] * b[r * 8 + j];
                t[r * 8 + u] = W16((s + 0x800) >> 12);
            }
        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                int s;
                if (v == 0)
                {
                    int sum = 0;
                    for (int r = 0; r < 8; r++) sum += t[r * 8 + u];
                    s = W16(sum) >> 3;
                }
                else if (v == 4)
                {
                    int sum = 0;
                    for (int r = 0; r < 8; r++) sum += (K[4, r] >> 12) * t[r * 8 + u];
                    s = W16(sum) >> 3;
                }
                else
                {
                    s = 0;
                    for (int r = 0; r < 8; r++) s += K[v, r] * t[r * 8 + u];
                    s = (s + 0x4000) >> 15;
                }
                b[v * 8 + u] = W16(s);
            }
        }
    }

    static void Quantize(short[] b, short[] q)
    {
        for (int i = 0; i < 64; i++)
        {
            int v = b[i], d = q[i], h = d >> 1;
            int n = W16(v >= 0 ? v + h : v - h);
            b[i] = W16(n / d);   // divs.w: 0 方向へ切り捨て
        }
    }

    /// <summary>SiV の展開ルーチンの弱点を避けるため、係数を必要なぶんだけ小さくする (safe モード)。</summary>
    static void AvoidSivDecoderBugs(short[] c, short[] q, bool chroma)
    {
        // 逆 DCT の表は ±1023 まで
        for (int i = 0; i < 64; i++)
        {
            int lim = 1023 / Math.Abs((int)q[i]);
            if (c[i] > lim) c[i] = (short)lim;
            else if (c[i] < -lim) c[i] = (short)-lim;
        }
        if (!chroma) return;
        // 色差の「ラン 1・サイズ 8」(|値| 128〜255 で、直前の 0 がちょうど 1 個) を使わない → 127 に抑える
        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int z = ZigZag[k];
            int v = c[z];
            if (v == 0) { run++; continue; }
            if (run % 16 == 1 && Math.Abs(v) >= 128 && Math.Abs(v) <= 255) c[z] = (short)(v > 0 ? 127 : -127);
            run = 0;
        }
    }

    sealed class BitWriter
    {
        readonly List<byte> _buf = new(8192);
        int _acc, _n;
        public void Put(int value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                _acc = (_acc << 1) | ((value >> i) & 1);
                if (++_n == 8) { _buf.Add((byte)_acc); _acc = 0; _n = 0; }
            }
        }
        public byte[] ToArray()
        {
            if (_n > 0) { _buf.Add((byte)(_acc << (8 - _n))); _acc = 0; _n = 0; }
            return _buf.ToArray();
        }
    }

    static int Category(int v)
    {
        v = Math.Abs(v);
        int n = 0;
        while (v != 0) { n++; v >>= 1; }
        return n;
    }

    static void EncodeBlock(BitWriter bw, short[] c, Huff dc, Huff ac, ref short pred)
    {
        int diff = W16(c[0] - pred);
        pred = c[0];
        int cat = Category(diff);
        bw.Put(dc.Code[cat], dc.Len[cat]);
        if (cat > 0) bw.Put((diff >= 0 ? diff : diff - 1) & ((1 << cat) - 1), cat);

        int run = 0, last = 0;
        for (int k = 1; k < 64; k++)
        {
            int v = c[ZigZag[k]];
            last = v;
            if (v == 0) { run++; continue; }
            while (run > 15) { bw.Put(ac.Code[0xf0], ac.Len[0xf0]); run -= 16; }
            int s = Category(v);
            int sym = (run << 4) | s;
            bw.Put(ac.Code[sym], ac.Len[sym]);
            bw.Put((v >= 0 ? v : v - 1) & ((1 << s) - 1), s);
            run = 0;
        }
        if (last == 0) bw.Put(ac.Code[0x00], ac.Len[0x00]);   // EOB (最後の係数が 0 のときだけ)
    }

    /// <summary>
    /// 128×120 の画像 (VDT の無圧縮フレーム、30720 バイト) を SiV の色質 colorType・画質 quality で圧縮する。
    /// </summary>
    public static byte[] Encode(ReadOnlySpan<byte> frame, int colorType, int quality, bool safe = true)
    {
        if (frame.Length < X68Color.FrameBytes) throw new ArgumentException("フレームは 30720 バイトです。", nameof(frame));
        var (mw, mh, nb) = Mcu(colorType);
        var (ql, qc) = QuantTables(quality);
        var bw = new BitWriter();
        short pY = 0, pCb = 0, pCr = 0;

        // SiV の作業領域と同じく、16×16 の Y / Cb / Cr 平面を MCU をまたいで使い回す
        // (最下段の MCU で画像の外になる行は、前の MCU の値が残る。SiV と同じバイト列にするため)
        var Y = new short[256];
        var Cb = new short[256];
        var Cr = new short[256];
        var blocks = new short[nb][];
        for (int i = 0; i < nb; i++) blocks[i] = new short[64];

        for (int y = 0; y < Height; y += mh)
        {
            int h = Math.Min(mh, Height - y);
            for (int x = 0; x < Width; x += mw)
            {
                // ---- 画素 → YCbCr (SiV の 16bit 整数演算そのまま) ----
                for (int r = 0; r < h; r++)
                {
                    int o = ((y + r) * Width + x) * 2;
                    for (int cIdx = 0; cIdx < mw; cIdx++, o += 2)
                    {
                        int p = ((frame[o] << 8) | frame[o + 1]) >> 1;
                        int B = (p & 31) << 4; p >>= 5;
                        int R = (p & 31) << 4; p >>= 5;
                        int G = (p & 31) << 4;
                        int k = r * 16 + cIdx;
                        Y[k] = W16((R * 0x2646 + G * 0x4b23 + B * 0xe98 + 0x4000) >> 16);
                        Cb[k] = W16((-R * 0x158e - G * 0x2a72 + (B << 14) + 0x4000) >> 16);
                        Cr[k] = W16(((R << 14) - G * 0x3598 - B * 0xa68 + 0x4000) >> 16);
                    }
                }

                // ---- ブロックに分ける ----
                switch (colorType)
                {
                    case 1:
                        LumaRows(Y, 0, 0, blocks[0]);
                        CopyRows(Cb, 0, blocks[1]);
                        CopyRows(Cr, 0, blocks[2]);
                        break;
                    case 2:
                        LumaRows(Y, 0, 0, blocks[0]);
                        LumaRows(Y, 0, 8, blocks[1]);
                        ChromaPairs(Cb, 1, blocks[2]);
                        ChromaPairs(Cr, 1, blocks[3]);
                        break;
                    default:
                        LumaRows(Y, 0, 0, blocks[0]);
                        LumaRows(Y, 0, 8, blocks[1]);
                        LumaRows(Y, 8, 0, blocks[2]);
                        LumaRows(Y, 8, 8, blocks[3]);
                        ChromaPairs(Cb, 2, blocks[4]);
                        ChromaPairs(Cr, 2, blocks[5]);
                        break;
                }

                for (int i = 0; i < nb; i++)
                {
                    bool luma = i < nb - 2;
                    Fdct(blocks[i]);
                    Quantize(blocks[i], luma ? ql : qc);
                    if (safe) AvoidSivDecoderBugs(blocks[i], luma ? ql : qc, !luma);
                    if (luma) EncodeBlock(bw, blocks[i], DcLuma, AcLuma, ref pY);
                    else if (i == nb - 2) EncodeBlock(bw, blocks[i], DcChroma, AcChroma, ref pCb);
                    else EncodeBlock(bw, blocks[i], DcChroma, AcChroma, ref pCr);
                }
            }
        }
        return bw.ToArray();
    }

    /// <summary>
    /// Y の 8×8 を取り出して 128 を引く。SiV は 2 画素ずつ 32bit で引くので、
    /// 下位の画素が 128 未満だと上位の画素からさらに 1 引かれる (その癖もそのまま再現)。
    /// </summary>
    static void LumaRows(short[] plane, int row0, int col0, short[] dst)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c += 2)
            {
                int a = plane[(row0 + r) * 16 + col0 + c], b = plane[(row0 + r) * 16 + col0 + c + 1];
                uint v = unchecked((uint)(((a & 0xffff) << 16) | (b & 0xffff)) - 0x00800080u);
                dst[r * 8 + c] = unchecked((short)(v >> 16));
                dst[r * 8 + c + 1] = unchecked((short)v);
            }
    }

    static void CopyRows(short[] plane, int row0, short[] dst)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                dst[r * 8 + c] = plane[(row0 + r) * 16 + c];
    }

    /// <summary>
    /// 色差の間引き。SiV は 16 画素から 2,3,6,7,10,11,14,15 列目を取り、
    /// 色質 3 では偶数行だけを使う (rowStep = 2)。SiV と同じにしておく。
    /// </summary>
    static void ChromaPairs(short[] plane, int rowStep, short[] dst)
    {
        ReadOnlySpan<int> cols = stackalloc int[] { 2, 3, 6, 7, 10, 11, 14, 15 };
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                dst[r * 8 + c] = plane[r * rowStep * 16 + cols[c]];
    }

    // =====================================================================
    // 展開
    // =====================================================================

    sealed class BitReader
    {
        readonly byte[] _d;
        int _pos;
        public BitReader(byte[] d) { _d = d; }
        public int Bit()
        {
            int i = _pos >> 3;
            int b = i < _d.Length ? (_d[i] >> (7 - (_pos & 7))) & 1 : 0;
            _pos++;
            return b;
        }
        public int Get(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++) v = (v << 1) | Bit();
            return v;
        }
        public int Decode(Huff h)
        {
            int code = 0;
            for (int l = 1; l <= 16; l++)
            {
                code = (code << 1) | Bit();
                if (h.MaxCode[l] >= 0 && code <= h.MaxCode[l] && code >= h.MinCode[l])
                    return h.Vals[h.ValPtr[l] + code - h.MinCode[l]];
            }
            throw new InvalidDataException("圧縮データが壊れています (ハフマン符号が見つかりません)。");
        }
    }

    static int Extend(int v, int n) => n == 0 ? 0 : v < (1 << (n - 1)) ? v - (1 << n) + 1 : v;

    static readonly double[,] Cos = BuildCos();

    static double[,] BuildCos()
    {
        var c = new double[8, 8];
        for (int u = 0; u < 8; u++)
            for (int x = 0; x < 8; x++)
                c[u, x] = (u == 0 ? Math.Sqrt(1.0 / 8) : Math.Sqrt(2.0 / 8)) * Math.Cos((2 * x + 1) * u * Math.PI / 16);
        return c;
    }

    static void DecodeBlock(BitReader br, Huff dc, Huff ac, ref short pred, short[] q, double[] outPix)
    {
        Span<int> coef = stackalloc int[64];
        coef.Clear();
        int cat = br.Decode(dc);
        int diff = cat > 0 ? Extend(br.Get(cat), cat) : 0;
        pred = W16(pred + diff);
        coef[0] = pred;
        for (int k = 1; k < 64;)
        {
            int rs = br.Decode(ac);
            int run = rs >> 4, s = rs & 15;
            if (s == 0)
            {
                if (run == 15) { k += 16; continue; }
                break;   // EOB
            }
            k += run;
            if (k > 63) break;
            coef[ZigZag[k]] = Extend(br.Get(s), s);
            k++;
        }
        // 逆量子化 (muls.w の結果を 16bit で持つ) → 逆 DCT
        Span<double> f = stackalloc double[64];
        for (int i = 0; i < 64; i++) f[i] = W16(coef[i] * q[i]);
        Span<double> t = stackalloc double[64];
        for (int v = 0; v < 8; v++)
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int u = 0; u < 8; u++) s += Cos[u, x] * f[v * 8 + u];
                t[v * 8 + x] = s;
            }
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int v = 0; v < 8; v++) s += Cos[v, y] * t[v * 8 + x];
                outPix[y * 8 + x] = s;
            }
    }

    /// <summary>圧縮データを 128×120 の画像 (30720 バイト、GGGGGRRRRRBBBBBI) に戻す。</summary>
    public static byte[] Decode(byte[] data, int colorType, int quality)
    {
        var (mw, mh, nb) = Mcu(colorType);
        var (ql, qc) = QuantTables(quality);
        var br = new BitReader(data);
        short pY = 0, pCb = 0, pCr = 0;
        var Yp = new double[128 * 128];
        var Cbp = new double[128 * 128];
        var Crp = new double[128 * 128];
        var blk = new double[nb][];
        for (int i = 0; i < nb; i++) blk[i] = new double[64];

        for (int y = 0; y < Height; y += mh)
            for (int x = 0; x < Width; x += mw)
            {
                for (int i = 0; i < nb; i++)
                {
                    bool luma = i < nb - 2;
                    if (luma) DecodeBlock(br, DcLuma, AcLuma, ref pY, ql, blk[i]);
                    else if (i == nb - 2) DecodeBlock(br, DcChroma, AcChroma, ref pCb, qc, blk[i]);
                    else DecodeBlock(br, DcChroma, AcChroma, ref pCr, qc, blk[i]);
                }
                switch (colorType)
                {
                    case 1:
                        Put(Yp, blk[0], x, y, 1, 1);
                        Put(Cbp, blk[1], x, y, 1, 1);
                        Put(Crp, blk[2], x, y, 1, 1);
                        break;
                    case 2:
                        Put(Yp, blk[0], x, y, 1, 1);
                        Put(Yp, blk[1], x + 8, y, 1, 1);
                        Put(Cbp, blk[2], x, y, 2, 1);
                        Put(Crp, blk[3], x, y, 2, 1);
                        break;
                    default:
                        Put(Yp, blk[0], x, y, 1, 1);
                        Put(Yp, blk[1], x + 8, y, 1, 1);
                        Put(Yp, blk[2], x, y + 8, 1, 1);
                        Put(Yp, blk[3], x + 8, y + 8, 1, 1);
                        Put(Cbp, blk[4], x, y, 2, 2);
                        Put(Crp, blk[5], x, y, 2, 2);
                        break;
                }
            }

        // YCbCr → GGGGGRRRRRBBBBBI (SiV の変換式と丸め・飽和をそのまま)
        var outBuf = new byte[X68Color.FrameBytes];
        for (int r = 0; r < Height; r++)
            for (int c = 0; c < Width; c++)
            {
                int i = r * 128 + c;
                int yy = (int)Math.Round(Yp[i]) + 128;
                int cb = (int)Math.Round(Cbp[i]);
                int cr = (int)Math.Round(Crp[i]);
                int y8 = (yy << 8) + 0x80;
                int g = y8 - cb * 0x58 - cr * 0xb6;
                int G = g <= 0 ? 0 : g > 0xfffe ? 0xf800 : g & 0xf800;
                int rr = (y8 + cr * 0x167) >> 5;
                int R = rr < 0x2f ? 0 : rr >= 0x7fe ? 0x7c0 : rr & 0x7c0;
                int b = y8 + cb * 0x1c6;
                int B = 0;
                if (b > 0x600) { int bv = (b >> 10) & 0xff; B = bv >= 0x3e ? 0x3e : bv; }
                int px = G | R | B;
                outBuf[(r * Width + c) * 2] = (byte)(px >> 8);
                outBuf[(r * Width + c) * 2 + 1] = (byte)px;
            }
        return outBuf;
    }

    /// <summary>8×8 のブロックを平面に置く (sx, sy 倍に引き伸ばして)。128×128 の外は捨てる。</summary>
    static void Put(double[] plane, double[] b, int x0, int y0, int sx, int sy)
    {
        for (int y = 0; y < 8 * sy; y++)
        {
            int py = y0 + y;
            if (py >= 128) break;
            for (int x = 0; x < 8 * sx; x++)
            {
                int px = x0 + x;
                if (px >= 128) break;
                plane[py * 128 + px] = b[(y / sy) * 8 + x / sx];
            }
        }
    }
}
