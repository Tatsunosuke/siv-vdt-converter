using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VdtConverter.Core;

/// <summary>縦横比が 4:3 と違う素材を 4:3 の画面にどう収めるか。</summary>
public enum FitMode
{
    /// <summary>縦横比を無視して画面いっぱいに引き伸ばす。</summary>
    Stretch,
    /// <summary>幅に合わせて全体を表示 (上下に黒帯)。</summary>
    Letterbox,
    /// <summary>高さに合わせて左右をトリム。</summary>
    Crop,
}

/// <summary>入力映像の縦横比の扱い。Auto は ffmpeg が報告する表示縦横比 (DAR) をそのまま使う。</summary>
public enum SourceAspect { Auto, Ratio4x3, Ratio16x9 }

/// <summary>ffmpeg で調べた入力の情報。</summary>
public sealed record MediaInfo(double? Duration, int Width, int Height, double? DisplayAspect, double? FrameRate = null)
{
    public bool HasVideo => Width > 0 && Height > 0;

    /// <summary>コマ番号の計算に使うフレームレート (分からなければ 30)。</summary>
    public double Fps => FrameRate is double f && f > 0 ? f : 30.0;

    /// <summary>入力の総コマ数 (長さが分からなければ null)。</summary>
    public int? FrameCount => Duration is double d ? Math.Max(1, (int)Math.Round(d * Fps)) : null;

    /// <summary>
    /// コマ番号 → シーク位置 (秒)。k/fps ちょうどだと丸め誤差で次のコマに飛ぶことがあるので、
    /// 1/4 コマ手前を指す (ffmpeg は指定時刻以降の最初のコマを返す)。
    /// </summary>
    public double FrameToSeconds(int frame) => frame <= 0 ? 0 : (frame - 0.25) / Fps;

    /// <summary>コマ番号 → そのコマの表示時刻 (秒)。表示用。</summary>
    public double FrameTime(int frame) => frame / Fps;
}

public static class AspectRules
{
    public const double FourThree = 4.0 / 3.0;
    public const double SixteenNine = 16.0 / 9.0;

    /// <summary>4:3 とみなせるか (±3%)。</summary>
    public static bool IsFourThree(double dar) => Math.Abs(dar / FourThree - 1) < 0.03;

    public static double? Resolve(SourceAspect a, double? detected) => a switch
    {
        SourceAspect.Ratio4x3 => FourThree,
        SourceAspect.Ratio16x9 => SixteenNine,
        _ => detected,
    };

    /// <summary>縦横比を「16:9」「4:3」「2.35:1」のような文字列にする。</summary>
    public static string Describe(double dar)
    {
        (string name, double v)[] known = { ("4:3", FourThree), ("16:9", SixteenNine), ("16:10", 1.6), ("3:2", 1.5),
            ("5:4", 1.25), ("1:1", 1), ("1.85:1", 1.85), ("2.35:1", 2.35), ("2.39:1", 2.39), ("9:16", 9 / 16.0), ("3:4", 0.75) };
        foreach (var (name, v) in known)
            if (Math.Abs(dar / v - 1) < 0.012) return name;
        return dar.ToString("0.00", CultureInfo.InvariantCulture) + ":1";
    }
}

/// <summary>ffmpeg.exe を使った映像・音声・静止画の取り出し。</summary>
public sealed class Ffmpeg
{
    public string ExePath { get; }

    public Ffmpeg(string exePath) { ExePath = exePath; }

    /// <summary>アプリのフォルダ → PATH の順で ffmpeg を探す。</summary>
    public static string? Locate()
    {
        string exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var here = Path.Combine(AppContext.BaseDirectory, exe);
        if (File.Exists(here)) return here;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                var p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// 128x120 へ縮小するフィルタ。X68000 の 128x120 は 4:3 画面いっぱいに表示されるので
    /// 画素の縦横比は 1.25:1。アスペクトを保つ場合は一度 640x480 (正方画素の 4:3) で合わせてから縮める。
    /// </summary>
    /// <param name="aspect">Auto 以外なら、入力のフラグを無視してその縦横比の絵として扱う (スクイーズ素材向け)。</param>
    public static string ScaleFilter(FitMode fit, bool deinterlace, SourceAspect aspect = SourceAspect.Auto)
    {
        var sb = new StringBuilder();
        if (deinterlace) sb.Append("yadif=0:-1:0,");
        sb.Append(aspect switch
        {
            SourceAspect.Ratio4x3 => "scale=trunc(ih*4/3/2)*2:ih:flags=lanczos,setsar=1,",
            SourceAspect.Ratio16x9 => "scale=trunc(ih*16/9/2)*2:ih:flags=lanczos,setsar=1,",
            _ => "scale=iw*sar:ih:flags=lanczos,setsar=1,",
        });
        switch (fit)
        {
            case FitMode.Letterbox:
                sb.Append("scale=640:480:force_original_aspect_ratio=decrease:flags=lanczos,");
                sb.Append("pad=640:480:(ow-iw)/2:(oh-ih)/2:black,");
                break;
            case FitMode.Crop:
                sb.Append("scale=640:480:force_original_aspect_ratio=increase:flags=lanczos,");
                sb.Append("crop=640:480,");
                break;
        }
        sb.Append("scale=128:120:flags=area,format=rgb24");
        return sb.ToString();
    }

    static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    static string Sec(double s) => s.ToString("0.###", CultureInfo.InvariantCulture);

    static string RangeArgs(double start, double length)
    {
        var a = "";
        if (start > 0) a += $"-ss {Sec(start)} ";
        if (length > 0) a += $"-t {Sec(length)} ";
        return a;
    }

    /// <summary>
    /// 映像を 128x120 RGB24 で 1 フレームずつ返す。fpsExpr は "30" や "60000/2002" など。
    /// onDuration には入力の長さ(秒)が分かった時点で通知される。
    /// </summary>
    public async IAsyncEnumerable<byte[]> ReadVideoFrames(string input, string fpsExpr, FitMode fit, bool deinterlace,
        SourceAspect aspect, double start, double length, Action<double>? onDuration, Action<string>? log,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string vf = $"fps={fpsExpr}:round=near," + ScaleFilter(fit, deinterlace, aspect);
        string args = $"-hide_banner -nostdin {RangeArgs(start, length)}-i {Q(input)} -an -sn -vf {Q(vf)} -f rawvideo -pix_fmt rgb24 -";
        using var p = Start(args, onDuration, log);
        using var reg = ct.Register(() => TryKill(p));
        var stdout = p.StandardOutput.BaseStream;
        const int size = 128 * 120 * 3;
        while (true)
        {
            var buf = new byte[size];
            int got = 0;
            while (got < size)
            {
                int n = await stdout.ReadAsync(buf.AsMemory(got, size - got), ct).ConfigureAwait(false);
                if (n == 0) break;
                got += n;
            }
            if (got < size) break;
            yield return buf;
        }
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>音声をモノラル float で丸ごと取り出す。音声トラックが無ければ null。</summary>
    public async Task<float[]?> ReadAudio(string input, int sampleRate, double start, double length,
        Action<string>? log, CancellationToken ct, int channels = 1)
    {
        string args = $"-hide_banner -nostdin {RangeArgs(start, length)}-i {Q(input)} -vn -sn -ac {channels} -ar {sampleRate} -f f32le -";
        using var p = Start(args, null, log);
        using var reg = ct.Register(() => TryKill(p));
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (ms.Length < 4) return null;
        var bytes = ms.GetBuffer();
        var f = new float[ms.Length / 4];
        Buffer.BlockCopy(bytes, 0, f, 0, f.Length * 4);
        return f;
    }

    /// <summary>静止画 (または動画の start 秒の位置) を 128x120 RGB24 で 1 枚読む。</summary>
    public async Task<byte[]> ReadStill(string input, FitMode fit, Action<string>? log, CancellationToken ct,
        double start = 0, bool deinterlace = false, SourceAspect aspect = SourceAspect.Auto)
    {
        string ss = start > 0 ? $"-ss {Sec(start)} " : "";
        string args = $"-hide_banner -nostdin {ss}-i {Q(input)} -an -sn -frames:v 1 -vf {Q(ScaleFilter(fit, deinterlace, aspect))} -f rawvideo -pix_fmt rgb24 -";
        using var p = Start(args, null, log);
        using var reg = ct.Register(() => TryKill(p));
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        if (ms.Length < 128 * 120 * 3) throw new InvalidDataException("画像を読み込めませんでした。");
        return ms.ToArray()[..(128 * 120 * 3)];
    }

    /// <summary>
    /// start 秒から連続する count コマを、128x120 RGB24 でまとめて読む (コマ送りのプレビュー用キャッシュ)。
    /// 1 コマずつ ffmpeg を起動するとシークのたびに数百 ms かかるので、まとめて取ってくる。
    /// </summary>
    public async Task<List<byte[]>> ReadFrameRun(string input, double start, int count, FitMode fit, bool deinterlace,
        SourceAspect aspect, CancellationToken ct)
    {
        string ss = start > 0 ? $"-ss {Sec(start)} " : "";
        string args = $"-hide_banner -nostdin {ss}-i {Q(input)} -an -sn -frames:v {count} -vf {Q(ScaleFilter(fit, deinterlace, aspect))} -f rawvideo -pix_fmt rgb24 -";
        using var p = Start(args, null, null);
        using var reg = ct.Register(() => TryKill(p));
        var list = new List<byte[]>(count);
        var stdout = p.StandardOutput.BaseStream;
        const int size = 128 * 120 * 3;
        while (list.Count < count)
        {
            var buf = new byte[size];
            int got = 0;
            while (got < size)
            {
                int n = await stdout.ReadAsync(buf.AsMemory(got, size - got), ct).ConfigureAwait(false);
                if (n == 0) break;
                got += n;
            }
            if (got < size) break;
            list.Add(buf);
        }
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return list;
    }

    /// <summary>入力の長さ (秒) を調べる。分からなければ null。</summary>
    public async Task<double?> ProbeDuration(string input, CancellationToken ct)
        => (await Probe(input, ct).ConfigureAwait(false)).Duration;

    static readonly Regex VideoSizeRx = new(@"Video:.*?,\s*(\d{2,5})x(\d{2,5})", RegexOptions.Compiled);
    static readonly Regex DarRx = new(@"\[SAR (\d+):(\d+) DAR (\d+):(\d+)\]", RegexOptions.Compiled);
    static readonly Regex FpsRx = new(@",\s*(\d+(?:\.\d+)?)\s*fps", RegexOptions.Compiled);
    static readonly Regex TbrRx = new(@",\s*(\d+(?:\.\d+)?)\s*tbr", RegexOptions.Compiled);

    /// <summary>ffmpeg の表示 (29.97 など) を NTSC の正確な値 (30000/1001) に直す。</summary>
    static double ExactRate(double f)
    {
        foreach (var n in new[] { 24000.0, 30000.0, 60000.0, 120000.0 })
            if (Math.Abs(f - n / 1001) < 0.006) return n / 1001;
        return f;
    }

    static readonly Regex RotationRx = new(@"rotation of (-?\d+(?:\.\d+)?) degrees", RegexOptions.Compiled);

    /// <summary>長さ・解像度・表示縦横比 (SAR を考慮、回転メタデータも考慮) を調べる。</summary>
    public async Task<MediaInfo> Probe(string input, CancellationToken ct)
    {
        double? dur = null;
        var lines = new List<string>();
        using var p = Start($"-hide_banner -nostdin -i {Q(input)}", d => dur = d, null, l => { lock (lines) lines.Add(l); });
        await p.StandardOutput.BaseStream.CopyToAsync(Stream.Null, ct).ConfigureAwait(false);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        p.WaitForExit(); // stderr の非同期読み取りを最後まで流す

        int w = 0, h = 0;
        double? dar = null, fps = null;
        bool rotated = false;
        lock (lines)
        {
            foreach (var l in lines)
            {
                if (w == 0 && l.Contains("Stream #") && l.Contains("Video:"))
                {
                    var m = VideoSizeRx.Match(l);
                    if (!m.Success) continue;
                    w = int.Parse(m.Groups[1].Value);
                    h = int.Parse(m.Groups[2].Value);
                    var d = DarRx.Match(l);
                    if (d.Success && int.Parse(d.Groups[1].Value) > 0 && int.Parse(d.Groups[2].Value) > 0)
                        dar = w * double.Parse(d.Groups[1].Value) / double.Parse(d.Groups[2].Value) / h;
                    else
                        dar = (double)w / h;
                    var fm = FpsRx.Match(l);
                    if (!fm.Success) fm = TbrRx.Match(l);
                    if (fm.Success && double.TryParse(fm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv) && fv > 0)
                        fps = ExactRate(fv);
                }
                else if (w > 0 && !rotated)
                {
                    var r = RotationRx.Match(l);
                    if (r.Success && Math.Abs(Math.Abs(double.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture)) % 180 - 90) < 1)
                        rotated = true;
                }
            }
        }
        if (rotated && dar is double dd) { (w, h) = (h, w); dar = 1 / dd; }
        return new MediaInfo(dur, w, h, dar, fps);
    }

    static readonly Regex DurationRx = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    Process Start(string args, Action<double>? onDuration, Action<string>? log, Action<string>? onLine = null)
    {
        var psi = new ProcessStartInfo(ExePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        var p = new Process { StartInfo = psi };
        bool durationSent = false;
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            onLine?.Invoke(e.Data);
            if (!durationSent && onDuration != null)
            {
                var m = DurationRx.Match(e.Data);
                if (m.Success)
                {
                    durationSent = true;
                    double sec = int.Parse(m.Groups[1].Value) * 3600 + int.Parse(m.Groups[2].Value) * 60
                                 + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                    onDuration(sec);
                }
            }
            if (log != null && (e.Data.Contains("Error", StringComparison.OrdinalIgnoreCase)
                                || e.Data.Contains("Invalid", StringComparison.OrdinalIgnoreCase)
                                || e.Data.Contains("No such file", StringComparison.OrdinalIgnoreCase)))
                log("ffmpeg: " + e.Data.Trim());
        };
        if (!p.Start()) throw new InvalidOperationException("ffmpeg を起動できませんでした。");
        p.BeginErrorReadLine();
        return p;
    }

    static void TryKill(Process p)
    {
        try { if (!p.HasExited) p.Kill(true); } catch { }
    }
}
