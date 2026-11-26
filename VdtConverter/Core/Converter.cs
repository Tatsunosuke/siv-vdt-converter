namespace VdtConverter.Core;

public enum TimingMode
{
    /// <summary>60Hz 基準。フレーム n は入力の n×(TimeScale/60) 秒の絵。実時間どおり。</summary>
    Exact30,
    /// <summary>59.94Hz (NTSC) 基準。フレーム n は入力の n×(TimeScale×1001/60000) 秒の絵。
    /// 29.97fps の素材をコマ落ち・コマ重複なしで 1:1 に取り込み、音声もそれに合わせて詰める
    /// (再生は 0.1% 速くなるが絵と音はずれない)。</summary>
    Ntsc,
}

public enum PosterSource
{
    /// <summary>VDT の最初のフレーム (開始位置) をそのまま使う。</summary>
    FirstFrame,
    /// <summary>入力動画の PosterTime 秒の絵を使う。</summary>
    VideoFrame,
    /// <summary>画像ファイルを使う。</summary>
    ImageFile,
}

public sealed class ConvertOptions
{
    public string Input { get; set; } = "";
    public string Output { get; set; } = "";
    public string FfmpegPath { get; set; } = "";
    public int TimeScale { get; set; } = 2;
    public TimingMode Timing { get; set; } = TimingMode.Exact30;
    /// <summary>4:3 以外の素材の収め方 (トリム / 全体表示 / 引き伸ばし)。4:3 の素材には使わない。</summary>
    public FitMode Fit { get; set; } = FitMode.Letterbox;
    /// <summary>入力の縦横比。Auto なら DetectedAspect を使う。</summary>
    public SourceAspect Aspect { get; set; } = SourceAspect.Auto;
    /// <summary>ffmpeg で調べた入力の表示縦横比 (分からなければ null)。</summary>
    public double? DetectedAspect { get; set; }
    public bool Deinterlace { get; set; }
    public AdpcmRate Rate { get; set; } = AdpcmRate.K15_6;
    public bool Normalize { get; set; } = true;
    public double Volume { get; set; } = 1.0;
    public double Start { get; set; }
    public double Length { get; set; }
    public PosterSource Poster { get; set; } = PosterSource.FirstFrame;
    public string PosterImage { get; set; } = "";
    /// <summary>PosterSource.VideoFrame のときの、入力動画上の位置 (秒)。</summary>
    public double PosterTime { get; set; }
    public string Comment { get; set; } = " Version 0.97a (VDT Converter)";
    /// <summary>色質: 0 = 無圧縮、1〜3 = SiV の JPEG 類似圧縮 (4:4:4 / 4:2:2 / 4:2:0)。</summary>
    public int ColorType { get; set; }
    /// <summary>画質 1〜100 (圧縮時だけ意味がある。SiV の初期値は 30)。</summary>
    public int Quality { get; set; } = 30;

    /// <summary>実際に使う縦横比 (分からなければ null)。</summary>
    public double? SourceAspectValue => AspectRules.Resolve(Aspect, DetectedAspect);

    /// <summary>4:3 の素材ならそのまま (Stretch = 変形なし)、それ以外は Fit。</summary>
    public FitMode VideoFit => SourceAspectValue is double a && AspectRules.IsFourThree(a) ? FitMode.Stretch : Fit;

    /// <summary>VDT 1 フレームが表す入力上の時間 (秒)。</summary>
    public double FrameDuration => Timing == TimingMode.Ntsc ? TimeScale * 1001.0 / 60000.0 : TimeScale / 60.0;

    /// <summary>ffmpeg の fps フィルタに渡す値。</summary>
    public string FpsExpr => Timing == TimingMode.Ntsc ? $"60000/{1001 * TimeScale}" : $"60/{TimeScale}";
}

public sealed record ConvertProgress(int Frames, int ExpectedFrames, string? Message);

public sealed class Converter
{
    const int AudioExtractRate = 32000;

    public static async Task<byte[]> MakePosterFromImage(ConvertOptions o, CancellationToken ct)
    {
        var ff = new Ffmpeg(o.FfmpegPath);
        // 画像は自分自身の縦横比で判断する (4:3 の画像ならトリムでも全体表示でも同じ結果)
        var rgb = await ff.ReadStill(o.PosterImage, o.Fit, null, ct).ConfigureAwait(false);
        return X68Color.FrameFromRgb24(rgb);
    }

    public static async Task<byte[]> MakePosterFromVideo(ConvertOptions o, double time, CancellationToken ct)
    {
        var ff = new Ffmpeg(o.FfmpegPath);
        var rgb = await ff.ReadStill(o.Input, o.VideoFit, null, ct, time, o.Deinterlace, o.Aspect).ConfigureAwait(false);
        return X68Color.FrameFromRgb24(rgb);
    }

    public async Task<int> Run(ConvertOptions o, IProgress<ConvertProgress> progress, CancellationToken ct)
    {
        var ff = new Ffmpeg(o.FfmpegPath);
        void Log(string m) => progress.Report(new ConvertProgress(-1, -1, m));

        if (Vdt.V16Problem(o.Rate, o.TimeScale, o.ColorType) is { } bad) throw new InvalidDataException(bad);
        bool pcm16 = o.Rate.IsPcm16;
        int ch = o.Rate.Channels;
        int voiceBytes = Vdt.VoiceBytesPerFrame(o.Rate, o.TimeScale);
        int spf = pcm16 ? voiceBytes / 2 : voiceBytes * 2; // ADPCM はサンプル/フレーム、v16 は 16bit ワード/フレーム (L,R 交互)
        int extractRate = pcm16 ? 48000 : AudioExtractRate;
        if (!pcm16 && voiceBytes > 0 && o.Rate.BytesPerSecond / (60 / o.TimeScale) != voiceBytes)
            Log($"注意: この組み合わせ（1 フレーム {voiceBytes} バイト）は SiV では再生できますが、mmvp.x は {o.Rate.BytesPerSecond / (60 / o.TimeScale)} バイトとして読むため、アドレスエラー（Force exit!）で止まります。mmvp.x でも見たいときは 15.6kHz にしてください。");

        // ---- 音声 ----
        AudioResampler? rs = null;
        AudioResampler[] rsCh = Array.Empty<AudioResampler>();
        float gain = (float)o.Volume;
        if (voiceBytes > 0)
        {
            Log("音声を取り出しています…");
            var pcm = await ff.ReadAudio(o.Input, extractRate, o.Start, o.Length, Log, ct, ch).ConfigureAwait(false);
            if (pcm == null || pcm.Length == 0)
            {
                Log("音声トラックが見つからないので無音で埋めます。");
            }
            else
            {
                if (o.Normalize)
                {
                    float peak = 0;
                    foreach (var v in pcm) peak = Math.Max(peak, Math.Abs(v));
                    if (peak > 1e-4f) gain *= 0.95f / peak;
                }
                // 1 フレーム (FrameDuration 秒) に spf サンプルを詰める = 実効サンプリング周波数
                double effRate = spf / (double)ch / o.FrameDuration;
                if (pcm16)
                {
                    rsCh = new AudioResampler[ch];
                    int n = pcm.Length / ch;
                    for (int c = 0; c < ch; c++)
                    {
                        var one = new float[n];
                        for (int k = 0; k < n; k++) one[k] = pcm[k * ch + c];
                        rsCh[c] = new AudioResampler(one, extractRate, effRate);
                    }
                    Log($"音声 {n / (double)extractRate:0.00} 秒、v16 {ch} ch・1 フレーム {voiceBytes} バイト");
                }
                else
                {
                    rs = new AudioResampler(pcm, AudioExtractRate, effRate);
                    Log($"音声 {pcm.Length / (double)AudioExtractRate:0.00} 秒、1 フレーム {voiceBytes} バイト ({spf} サンプル)");
                }
            }
        }

        // ---- ポスター ----
        byte[]? poster = null;
        if (o.Poster == PosterSource.ImageFile)
        {
            Log("ポスター画像を読み込んでいます…");
            poster = await MakePosterFromImage(o, ct).ConfigureAwait(false);
        }
        else if (o.Poster == PosterSource.VideoFrame)
        {
            Log($"ポスターに {o.PosterTime:0.000} 秒の絵を使います。");
            poster = await MakePosterFromVideo(o, o.PosterTime, ct).ConfigureAwait(false);
        }

        var posterVoice = new byte[voiceBytes];
        if (!pcm16) Array.Fill(posterVoice, OkiAdpcm.SilenceByte);  // v16 の無音は 0

        // ---- 映像 + 書き出し ----
        Log("映像を変換しています…");
        int expected = 0;
        void OnDuration(double d)
        {
            double len = d - o.Start;
            if (o.Length > 0) len = Math.Min(len, o.Length);
            expected = Math.Max(0, (int)Math.Round(len / o.FrameDuration));
        }

        var tmp = o.Output + ".tmp";
        var adpcm = new OkiAdpcm();
        var voice = new byte[voiceBytes];
        var samples = new float[spf];
        int frame = 0;
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                var w = new VdtWriter(fs);
                await foreach (var rgb in ff.ReadVideoFrames(o.Input, o.FpsExpr, o.VideoFit, o.Deinterlace,
                                   o.Aspect, o.Start, o.Length, OnDuration, Log, ct).ConfigureAwait(false))
                {
                    var img = X68Color.FrameFromRgb24(rgb);
                    if (frame == 0)
                    {
                        w.WriteHeader(new VdtHeader
                        {
                            Comment = o.Comment,
                            Poster = poster ?? img,
                            Quality = o.ColorType == 0 ? 100 : o.Quality,
                            ColorType = o.ColorType,
                            PosterVoice = posterVoice,
                            TimeScale = o.TimeScale,
                            Rate = o.Rate,
                        });
                    }

                    if (voiceBytes > 0 && pcm16)
                    {
                        if (rsCh.Length == 0) Array.Clear(voice);
                        else
                        {
                            // フレームをまたいでも L,R の交互が続くよう、通しのワード番号で計算する
                            long word0 = (long)frame * spf;
                            for (int k = 0; k < spf; k++)
                            {
                                long word = word0 + k;
                                int c = (int)(word % ch);
                                double t = (word / ch) * o.FrameDuration * ch / spf;
                                int v = (int)Math.Round(rsCh[c].SampleAt(t) * gain * 32767);
                                v = Math.Clamp(v, -32768, 32767);
                                voice[k * 2] = (byte)(v >> 8);       // ビッグエンディアン
                                voice[k * 2 + 1] = (byte)v;
                            }
                        }
                    }
                    else if (voiceBytes > 0)
                    {
                        if (rs == null)
                        {
                            Array.Fill(voice, OkiAdpcm.SilenceByte);
                        }
                        else
                        {
                            double t0 = frame * o.FrameDuration;
                            double dt = o.FrameDuration / spf;
                            for (int k = 0; k < spf; k++) samples[k] = rs.SampleAt(t0 + k * dt) * gain;
                            adpcm.Encode(samples, voice);
                        }
                    }
                    w.WriteFrame(img, voice);
                    frame++;
                    if ((frame & 7) == 0) progress.Report(new ConvertProgress(frame, expected, null));
                }
                if (frame == 0) throw new InvalidDataException("映像フレームを 1 枚も取り出せませんでした。入力ファイルや開始位置を確認してください。");
                w.Finish();
                if (o.ColorType != 0)
                    Log($"圧縮: 色質 {o.ColorType}・画質 {o.Quality}。画像 {w.ImageBytes:N0} バイト（無圧縮なら {(long)frame * X68Color.FrameBytes:N0}、{100.0 * w.ImageBytes / Math.Max(1L, (long)frame * X68Color.FrameBytes):0.0}%）");
            }
            File.Move(tmp, o.Output, true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
        progress.Report(new ConvertProgress(frame, frame, $"完了: {frame} フレーム ({frame * o.FrameDuration:0.00} 秒相当)"));
        return frame;
    }

    /// <summary>X68000 側で全フレームを展開するのに必要なメモリの目安 (バイト)。</summary>
    public static long PlaybackMemory(int frames, AdpcmRate rate, int timeScale)
        => (long)frames * (X68Color.FrameBytes + Vdt.VoiceBytesPerFrame(rate, timeScale));
}
