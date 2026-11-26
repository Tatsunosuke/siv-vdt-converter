namespace VdtConverter.Core;

/// <summary>
/// 窓付き sinc による任意倍率のリサンプラ。
/// 入力 (srcRate) の任意の時刻 t[秒] の値を、ローパス (cutoff) をかけた状態で取り出す。
/// </summary>
public sealed class AudioResampler
{
    readonly float[] _src;
    readonly double _srcRate;
    readonly double _fc;     // 正規化カットオフ (入力ナイキスト=1)
    readonly int _half;      // 片側タップ数

    public AudioResampler(float[] src, double srcRate, double dstRate)
    {
        _src = src;
        _srcRate = srcRate;
        _fc = Math.Min(1.0, dstRate / srcRate) * 0.92;
        _half = (int)Math.Ceiling(6.0 / _fc);
    }

    public double DurationSeconds => _src.Length / _srcRate;

    public float SampleAt(double t)
    {
        double p = t * _srcRate;
        int c = (int)Math.Floor(p);
        double acc = 0, wsum = 0;
        for (int k = c - _half + 1; k <= c + _half; k++)
        {
            double x = p - k;
            double w = Window(x / _half) * Sinc(_fc * x);
            wsum += w;
            if ((uint)k < (uint)_src.Length) acc += _src[k] * w;
        }
        return wsum != 0 ? (float)(acc / wsum) : 0f;
    }

    static double Sinc(double x) => Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    // Blackman 窓 (|u|<=1)
    static double Window(double u)
    {
        if (Math.Abs(u) >= 1) return 0;
        double a = Math.PI * (u + 1); // 0..2π
        return 0.42 - 0.5 * Math.Cos(a) + 0.08 * Math.Cos(2 * a);
    }
}
