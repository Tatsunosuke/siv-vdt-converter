namespace VdtConverter.Core;

/// <summary>
/// X68000 65536色モードのピクセル (GGGGGRRRRRBBBBBI, ビッグエンディアン1ワード) との相互変換。
/// I ビットは R/G/B 共通の「輝度ビット」で、各色の 6bit 目 (最下位) として働く。
/// </summary>
public static class X68Color
{
    public const int Width = 128;
    public const int Height = 120;
    public const int FrameBytes = Width * Height * 2; // 30720

    /// <summary>RGB888 → X68 ワード。I ビットは誤差が小さくなる方を選ぶ。</summary>
    public static ushort FromRgb(byte r, byte g, byte b)
    {
        ushort best = 0;
        int bestErr = int.MaxValue;
        for (int i = 0; i <= 1; i++)
        {
            int r5 = Quant5(r, i), g5 = Quant5(g, i), b5 = Quant5(b, i);
            int err = Sq(r - Expand(r5, i)) + Sq(g - Expand(g5, i)) + Sq(b - Expand(b5, i));
            if (err < bestErr)
            {
                bestErr = err;
                best = (ushort)((g5 << 11) | (r5 << 6) | (b5 << 1) | i);
            }
        }
        return best;
    }

    /// <summary>X68 ワード → RGB888 (表示用)。</summary>
    public static void ToRgb(ushort w, out byte r, out byte g, out byte b)
    {
        int i = w & 1;
        g = (byte)Expand((w >> 11) & 31, i);
        r = (byte)Expand((w >> 6) & 31, i);
        b = (byte)Expand((w >> 1) & 31, i);
    }

    // 6bit 値 v6 = c5*2 + I を 0..255 に展開
    static int Expand(int c5, int i) => ((c5 << 1) | i) * 255 / 63;

    static int Quant5(int c, int i)
    {
        // c ≒ (2*c5 + i) * 255/63 となる c5 を求める
        double v6 = c * 63.0 / 255.0;
        int c5 = (int)Math.Round((v6 - i) / 2.0);
        return Math.Clamp(c5, 0, 31);
    }

    static int Sq(int x) => x * x;

    /// <summary>RGB24 (128x120) → VDT 画像データ 30720 バイト (ビッグエンディアン)。</summary>
    public static byte[] FrameFromRgb24(ReadOnlySpan<byte> rgb)
    {
        var dst = new byte[FrameBytes];
        for (int p = 0, s = 0; p < Width * Height; p++, s += 3)
        {
            ushort w = FromRgb(rgb[s], rgb[s + 1], rgb[s + 2]);
            dst[p * 2] = (byte)(w >> 8);
            dst[p * 2 + 1] = (byte)w;
        }
        return dst;
    }

    /// <summary>VDT 画像データ → BGRA32 (Bitmap 用)。</summary>
    public static void FrameToBgra32(ReadOnlySpan<byte> frame, Span<byte> bgra)
    {
        for (int p = 0; p < Width * Height; p++)
        {
            ushort w = (ushort)((frame[p * 2] << 8) | frame[p * 2 + 1]);
            ToRgb(w, out var r, out var g, out var b);
            bgra[p * 4] = b; bgra[p * 4 + 1] = g; bgra[p * 4 + 2] = r; bgra[p * 4 + 3] = 255;
        }
    }
}
