using System.Text;

namespace VdtConverter.Core;

/// <summary>ADPCM レート (VDT ヘッダに書く値 = IOCS _ADPCMOUT のサンプリング指定)。</summary>
public sealed record AdpcmRate(int Code, string Label, int BytesPerSecond, bool SivCompatible)
{
    /// <summary>実機の MSM6258 のサンプリング周波数 (Hz)。</summary>
    public double HardwareHz => Code switch
    {
        1 => 4_000_000.0 / 768,  // 5.208kHz
        2 => 8_000_000.0 / 1024, // 7.8125kHz
        3 => 8_000_000.0 / 768,  // 10.417kHz
        4 => 8_000_000.0 / 512,  // 15.625kHz
        _ => 0,
    };

    /// <summary>SiV が想定する公称サンプル数/秒 (= バイト数×2)。v16 では 1 チャンネルあたりのサンプル数/秒。</summary>
    public int NominalSamplesPerSecond => IsPcm16 ? BytesPerSecond / 2 / Channels : BytesPerSecond * 2;

    /// <summary>v16（mmvp.x の拡張。Mercury-Unit の 16bit PCM）。ヘッダ値は $1xx = モノラル、$2xx = ステレオ。</summary>
    public bool IsPcm16 => Code >= 0x100;
    public int Channels => Code >= 0x200 ? 2 : 1;

    public override string ToString() => Label;

    // SiV.X 0.97 は 0(無音)/2(7.8kHz)/4(15.6kHz) のみ扱う (SiV.X 内 $325a の分岐で確認)。
    // コード 0 は IOCS では 3.9kHz だが、SiV では「無音」に割り当てられているので 3.9kHz は表現できない。
    public static readonly AdpcmRate Silent = new(0, "無音", 0, true);
    public static readonly AdpcmRate K7_8 = new(2, "7.8kHz", 3900, true);
    public static readonly AdpcmRate K15_6 = new(4, "15.6kHz", 7800, true);
    public static readonly AdpcmRate K5_2 = new(1, "5.2kHz（SiV 0.97 非対応）", 2600, false);
    public static readonly AdpcmRate K10_4 = new(3, "10.4kHz（SiV 0.97 非対応）", 5200, false);

    // v16（mmvp.x 0.70d の $156a〜 で確認）: 下位バイト 0=32kHz / 1=44.1kHz / 2=48kHz、$2xx はステレオ (L,R の順に交互)。
    // 16bit 符号付き・ビッグエンディアンを Mercury-Unit ($ECC080) へ DMA で流す。
    public static readonly AdpcmRate P32M = new(0x100, "v16 32kHz モノラル（mmvp・Mercury-Unit）", 64000, false);
    public static readonly AdpcmRate P44M = new(0x101, "v16 44.1kHz モノラル（mmvp・Mercury-Unit）", 88200, false);
    public static readonly AdpcmRate P48M = new(0x102, "v16 48kHz モノラル（mmvp・Mercury-Unit）", 96000, false);
    public static readonly AdpcmRate P32S = new(0x200, "v16 32kHz ステレオ（mmvp・Mercury-Unit）", 128000, false);
    public static readonly AdpcmRate P44S = new(0x201, "v16 44.1kHz ステレオ（mmvp・Mercury-Unit）", 176400, false);
    public static readonly AdpcmRate P48S = new(0x202, "v16 48kHz ステレオ（mmvp・Mercury-Unit）", 192000, false);

    public static IReadOnlyList<AdpcmRate> All { get; } = new[] { Silent, K7_8, K15_6, K5_2, K10_4, P32M, P44M, P48M, P32S, P44S, P48S };
    public static AdpcmRate FromCode(int code) => All.FirstOrDefault(r => r.Code == code)
        ?? new AdpcmRate(code, $"不明({code})", 0, false);
}

/// <summary>VDT ファイルの定数と計算式 (SiV.X 0.97a の実装に合わせたもの)。</summary>
public static class Vdt
{
    /// <summary>TimeScale (= 60/TimeScale fps) として指定できる値。</summary>
    public static readonly int[] TimeScales = { 2, 3, 4, 5, 6, 10, 12, 15, 20, 30, 60 };

    public const int PosterBytes = X68Color.FrameBytes;

    /// <summary>
    /// 1 フレームあたりの音声バイト数。SiV.X の計算式そのまま:
    ///   bytes/sec ÷ (60 ÷ TimeScale) を整数除算し、奇数なら +1。
    /// </summary>
    public static int VoiceBytesPerFrame(AdpcmRate rate, int timeScale)
    {
        if (rate.BytesPerSecond == 0) return 0;
        // v16 は mmvp.x の計算 (bytes/sec ÷ fps の整数除算、偶数への切り上げなし)
        if (rate.IsPcm16) return rate.BytesPerSecond / (60 / timeScale);
        int v = rate.BytesPerSecond / (60 / timeScale);
        if ((v & 1) != 0) v++;
        return v;
    }

    /// <summary>mmvp.x で再生できない組み合わせなら理由を返す (v16 のみ)。</summary>
    public static string? V16Problem(AdpcmRate rate, int timeScale, int colorType)
    {
        if (!rate.IsPcm16) return null;
        if (colorType != 0) return "v16 は無圧縮（色質 0）でしか作れません（mmvp.x は圧縮データを読めません）。";
        int v = VoiceBytesPerFrame(rate, timeScale);
        int fps = 60 / timeScale;
        if ((v & 1) != 0) return $"{fps} fps だと 1 フレームの音声が {v} バイト（奇数）になり、mmvp.x で正しく再生できません。";
        if (v + X68Color.FrameBytes > 0xFFFF) return $"{fps} fps だと 1 フレームが 64KB を超え、mmvp.x で扱えません。フレームレートを上げてください。";
        return null;
    }

    /// <summary>
    /// その設定の VDT がどのプレイヤーで再生できるか。理由は再生できないときだけ入る。
    /// SiV.X / SIVPH.X は 0.97a・0.60H の実装、mmvp.x は 0.70d の実装（$156a〜）に合わせた判定。
    /// </summary>
    public static (string? Siv, string? Mmvp) Playability(AdpcmRate rate, int timeScale, int colorType)
    {
        string? siv = rate.IsPcm16 ? "v16（16bit PCM）は扱えません"
                    : !rate.SivCompatible ? $"{rate.Label} は扱えません" : null;

        string? mmvp;
        if (rate.IsPcm16) mmvp = V16Problem(rate, timeScale, colorType);
        else if (colorType != 0) mmvp = "圧縮（色質 1〜3）は読めません";
        else if (rate.Code is 1 or 3) mmvp = $"{rate.Label} は正しく鳴りません";
        else
        {
            int fps = 60 / timeScale;
            int sivBytes = VoiceBytesPerFrame(rate, timeScale);
            int mmvpBytes = rate.BytesPerSecond / fps;
            mmvp = sivBytes != mmvpBytes
                ? $"{fps} fps・{rate.Label} だと音声を {mmvpBytes} バイトと読み違え、Force exit! で止まります（15.6kHz なら OK）"
                : null;
        }
        return (siv, mmvp);
    }

    public static Encoding ShiftJis
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932);
        }
    }
}

public sealed class VdtHeader
{
    public string Comment { get; set; } = "";
    public byte[] Poster { get; set; } = Array.Empty<byte>();
    public int Quality { get; set; } = 100;
    public int ColorType { get; set; }      // 0 = 無圧縮, 1..3 = 圧縮
    public byte[] PosterVoice { get; set; } = Array.Empty<byte>();
    public int TimeScale { get; set; } = 2;
    public AdpcmRate Rate { get; set; } = AdpcmRate.K15_6;
    public int FrameCount { get; set; }
    public long FramesOffset { get; set; }
    public double Fps => 60.0 / TimeScale;
}

/// <summary>
/// VDT をストリーム書き出しする。色質 0 は無圧縮、1〜3 は SiV の JPEG 類似圧縮 (SivJpeg)。
/// ポスター・フレームとも 30720 バイトの画像を渡せば、圧縮が必要ならここで圧縮する。
/// </summary>
public sealed class VdtWriter : IDisposable
{
    readonly Stream _s;
    long _countPos;
    int _frames;
    int _voiceBytes;
    int _colorType, _quality;
    bool _headerWritten;

    /// <summary>書き出した画像データの合計 (圧縮後) バイト数。</summary>
    public long ImageBytes { get; private set; }

    public VdtWriter(Stream s) { _s = s; }

    public int FramesWritten => _frames;

    public void WriteHeader(VdtHeader h)
    {
        if (h.ColorType is < 0 or > 3) throw new ArgumentException("色質は 0〜3 です。");
        if (h.ColorType != 0 && h.Quality is < 1 or > 100) throw new ArgumentException("画質は 1〜100 です。");
        if (h.Poster.Length != Vdt.PosterBytes) throw new ArgumentException("ポスター画像は 30720 バイトです。");
        _colorType = h.ColorType;
        _quality = h.Quality;
        _voiceBytes = Vdt.VoiceBytesPerFrame(h.Rate, h.TimeScale);
        if (h.PosterVoice.Length != _voiceBytes) throw new ArgumentException("ポスター音声のサイズが不正です。");

        // "SiV" + コメント + $0A
        _s.Write("SiV"u8);
        var comment = (h.Comment ?? "").Replace("\r", "").Replace("\n", " ");
        _s.Write(Vdt.ShiftJis.GetBytes(comment));
        _s.WriteByte(0x0A);

        // ポスター: 圧縮時は長さが変わる (SiV の保存処理と同じく Poster Len + データ、詰め物なし)
        var poster = _colorType == 0 ? h.Poster : SivJpeg.Encode(h.Poster, _colorType, _quality);
        WriteL(poster.Length);
        _s.Write(poster);
        WriteL(h.Quality);
        WriteL(_colorType);       // Type: 0 = 無圧縮、1〜3 = 色質
        WriteL(_voiceBytes);
        _s.Write(h.PosterVoice);
        WriteL(h.TimeScale);
        WriteL(h.Rate.Code);
        _countPos = _s.Position;
        WriteL(0);                // フレーム数 (最後に書き戻す)
        _headerWritten = true;
    }

    /// <summary>
    /// フレームを 1 枚書く。無圧縮: 画像 30720 バイト + 音声 (Flame Len は書かない)。
    /// 圧縮: Flame Len (L) + 圧縮データ + 音声。
    /// </summary>
    public void WriteFrame(ReadOnlySpan<byte> image, ReadOnlySpan<byte> voice)
    {
        if (!_headerWritten) throw new InvalidOperationException("ヘッダが未出力です。");
        if (image.Length != X68Color.FrameBytes) throw new ArgumentException("画像サイズが不正です。");
        if (voice.Length != _voiceBytes) throw new ArgumentException("音声サイズが不正です。");
        if (_colorType == 0)
        {
            _s.Write(image);
            ImageBytes += image.Length;
        }
        else
        {
            var c = SivJpeg.Encode(image, _colorType, _quality);
            WriteL(c.Length);
            _s.Write(c);
            ImageBytes += 4 + c.Length;
        }
        _s.Write(voice);
        _frames++;
    }

    public void Finish()
    {
        long end = _s.Position;
        _s.Position = _countPos;
        WriteL(_frames);
        _s.Position = end;
        _s.Flush();
    }

    void WriteL(int v)
    {
        Span<byte> b = stackalloc byte[4];
        b[0] = (byte)(v >> 24); b[1] = (byte)(v >> 16); b[2] = (byte)(v >> 8); b[3] = (byte)v;
        _s.Write(b);
    }

    public void Dispose() { }
}

/// <summary>VDT 読み込み (プレビュー用)。圧縮 (色質 1〜3) の画像も展開する。</summary>
public sealed class VdtFile
{
    public VdtHeader Header { get; } = new();
    public List<byte[]?> Images { get; } = new();   // 圧縮フレームは null
    public List<byte[]> Voices { get; } = new();
    public bool Compressed => Header.ColorType != 0;

    public static VdtFile Load(string path)
    {
        using var fs = File.OpenRead(path);
        var r = new BinaryReader(fs);
        var f = new VdtFile();
        var h = f.Header;
        if (r.ReadByte() != 'S' || r.ReadByte() != 'i' || r.ReadByte() != 'V')
            throw new InvalidDataException("SiV の VDT ファイルではありません。");
        var cm = new List<byte>();
        for (int c; (c = fs.ReadByte()) != 0x0A;)
        {
            if (c < 0) throw new EndOfStreamException();
            cm.Add((byte)c);
        }
        h.Comment = Vdt.ShiftJis.GetString(cm.ToArray());

        int posterLen = ReadL(r);
        var poster = ReadExact(r, posterLen);
        h.Quality = ReadL(r);
        h.ColorType = ReadL(r);
        h.Poster = h.ColorType == 0 ? poster : TryDecode(poster, h.ColorType, h.Quality);
        int pvLen = ReadL(r);
        h.PosterVoice = ReadExact(r, pvLen);
        h.TimeScale = ReadL(r);
        h.Rate = AdpcmRate.FromCode(ReadL(r));
        h.FrameCount = ReadL(r);
        h.FramesOffset = fs.Position;
        if (h.TimeScale <= 0 || h.TimeScale > 60) throw new InvalidDataException($"TimeScale が不正です ({h.TimeScale})。");

        int vb = pvLen; // 各フレームの音声サイズはポスター音声と同じ
        for (int i = 0; i < h.FrameCount; i++)
        {
            if (fs.Position >= fs.Length) break; // 途中で切れているファイル
            if (h.ColorType == 0)
            {
                f.Images.Add(ReadExact(r, X68Color.FrameBytes));
            }
            else
            {
                int len = ReadL(r);
                var data = ReadExact(r, len);
                f.Images.Add(TryDecode(data, h.ColorType, h.Quality));
            }
            f.Voices.Add(ReadExact(r, vb));
        }
        return f;
    }

    /// <summary>圧縮画像を展開する。色質・画質が範囲外などで展開できなければ空配列 (プレビューでは「表示できません」)。</summary>
    static byte[] TryDecode(byte[] data, int colorType, int quality)
    {
        if (colorType is < 1 or > 3 || quality is < 1 or > 100) return Array.Empty<byte>();
        try { return SivJpeg.Decode(data, colorType, quality); }
        catch (InvalidDataException) { return Array.Empty<byte>(); }
    }

    static int ReadL(BinaryReader r)
    {
        var b = ReadExact(r, 4);
        return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
    }

    static byte[] ReadExact(BinaryReader r, int n)
    {
        if (n < 0 || n > 64 * 1024 * 1024) throw new InvalidDataException($"サイズ値が不正です ({n})。");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException("ファイルが途中で終わっています。");
        return b;
    }
}
