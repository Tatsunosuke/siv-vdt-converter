namespace VdtConverter.Core;

/// <summary>
/// X68000 の ADPCM (OKI MSM6258) 4bit ADPCM エンコーダ／デコーダ。
/// 12bit 内部精度、ステップ表 49 段。1 バイトに 2 サンプル、下位ニブルが先に再生される。
/// 差分計算は MAME の okim6258 実装と同じ整数演算に合わせてある。
/// </summary>
public sealed class OkiAdpcm
{
    static readonly int[] IndexShift = { -1, -1, -1, -1, 2, 4, 6, 8 };
    static readonly int[] DiffLookup = BuildTable();

    int _signal;   // -2048..2047
    int _step;     // 0..48

    public void Reset() { _signal = 0; _step = 0; }

    static int[] BuildTable()
    {
        var t = new int[49 * 16];
        for (int step = 0; step < 49; step++)
        {
            int stepval = (int)Math.Floor(16.0 * Math.Pow(11.0 / 10.0, step));
            for (int nib = 0; nib < 16; nib++)
            {
                int sign = (nib & 8) != 0 ? -1 : 1;
                int b2 = (nib >> 2) & 1, b1 = (nib >> 1) & 1, b0 = nib & 1;
                t[step * 16 + nib] = sign * (stepval * b2 + stepval / 2 * b1 + stepval / 4 * b0 + stepval / 8);
            }
        }
        return t;
    }

    int Apply(int nib)
    {
        _signal = Math.Clamp(_signal + DiffLookup[_step * 16 + nib], -2048, 2047);
        _step = Math.Clamp(_step + IndexShift[nib & 7], 0, 48);
        return _signal;
    }

    /// <summary>12bit サンプルを 1 ニブルにエンコード (16 通りから最も誤差の小さいものを選ぶ)。</summary>
    public int EncodeSample(int sample12)
    {
        sample12 = Math.Clamp(sample12, -2048, 2047);
        int best = 0, bestErr = int.MaxValue;
        int baseIdx = _step * 16;
        for (int nib = 0; nib < 16; nib++)
        {
            int s = Math.Clamp(_signal + DiffLookup[baseIdx + nib], -2048, 2047);
            int e = Math.Abs(sample12 - s);
            if (e < bestErr) { bestErr = e; best = nib; }
        }
        Apply(best);
        return best;
    }

    /// <summary>float (-1..1) サンプル列 (偶数個) を ADPCM バイト列へ。状態は呼び出し間で継続する。</summary>
    public void Encode(ReadOnlySpan<float> samples, Span<byte> dst)
    {
        for (int i = 0; i < dst.Length; i++)
        {
            int lo = EncodeSample(ToS12(samples[i * 2]));
            int hi = EncodeSample(ToS12(samples[i * 2 + 1]));
            dst[i] = (byte)(lo | (hi << 4));
        }
    }

    static int ToS12(float f) => (int)Math.Round(Math.Clamp(f, -1f, 1f) * 2047f);

    /// <summary>ADPCM バイト列 → 16bit PCM。状態は呼び出し間で継続する。</summary>
    public void Decode(ReadOnlySpan<byte> src, Span<short> dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            dst[i * 2] = (short)(Apply(src[i] & 15) << 4);
            dst[i * 2 + 1] = (short)(Apply(src[i] >> 4) << 4);
        }
    }

    /// <summary>無音バイト (+最小差分 / -最小差分 の交互で 0 付近に留まる)。</summary>
    public const byte SilenceByte = 0x80;
}
