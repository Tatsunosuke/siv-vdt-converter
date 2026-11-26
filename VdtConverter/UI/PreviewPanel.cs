using System.Diagnostics;
using System.Media;
using VdtConverter.Core;

namespace VdtConverter.UI;

/// <summary>VDT ファイルの簡易プレーヤー（無圧縮データの画像＋ADPCM 音声）。</summary>
sealed class PreviewPanel : UserControl
{
    readonly Button _open = new() { Text = "VDT を開く…", AutoSize = true };
    readonly Button _play = new() { Text = "▶ 再生", AutoSize = true, Enabled = false };
    readonly Button _stop = new() { Text = "■ 停止", AutoSize = true, Enabled = false };
    readonly Button _posterBtn = new() { Text = "ポスター", AutoSize = true, Enabled = false };
    readonly CheckBox _repeat = new() { Text = "Repeat", AutoSize = true };
    readonly CheckBox _sound = new() { Text = "音声", AutoSize = true, Checked = true };
    readonly TrackBar _seek = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 0 };
    readonly Label _frameLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Text = "0 / 0" };
    readonly X68View _view = new() { Dock = DockStyle.Fill, Placeholder = "VDT ファイルを開くか、ウインドウにドロップしてください" };
    readonly Label _info = new() { Dock = DockStyle.Fill, AutoSize = true };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 10 };
    readonly ToolTip _tips = new() { AutoPopDelay = 20000, InitialDelay = 500, ShowAlways = true };

    VdtFile? _file;
    string _path = "";
    SoundPlayer? _player;
    MemoryStream? _wav;
    readonly Stopwatch _clock = new();
    int _startFrame;
    int _shownFrame = -1;

    public PreviewPanel()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        bar.Controls.AddRange(new Control[] { _open, _play, _stop, _posterBtn, _repeat, _sound });
        root.Controls.Add(bar);
        root.Controls.Add(_view);

        var seekRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seekRow.Controls.Add(_seek, 0, 0);
        seekRow.Controls.Add(_frameLabel, 1, 0);
        root.Controls.Add(seekRow);
        root.Controls.Add(_info);
        Controls.Add(root);

        _tips.SetToolTip(_open, "VDT ファイルを開きます。ウインドウへのドロップでも開けます。");
        _tips.SetToolTip(_play, "再生します。音声は ADPCM をデコードして鳴らします。");
        _tips.SetToolTip(_stop, "再生を止めます。");
        _tips.SetToolTip(_posterBtn, "VDT に入っているポスター（一覧に出る代表の絵）を表示します。");
        _tips.SetToolTip(_repeat, "最後まで再生したら、先頭に戻って繰り返します。");
        _tips.SetToolTip(_sound, "音声を鳴らすかどうか。");
        _tips.SetToolTip(_seek, "コマ送り。つまみを動かすと、そのコマを表示します。");
        _tips.SetToolTip(_frameLabel, "表示中のコマ / 全コマ数");
        _tips.SetToolTip(_view, "X68000 での見え方（128×120）。圧縮（色質 1〜3）の VDT も展開して表示します。");

        _open.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "SiV 動画データ (*.vdt)|*.vdt|すべてのファイル|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) Open(d.FileName);
        };
        _play.Click += (_, _) => Play();
        _stop.Click += (_, _) => Stop();
        _posterBtn.Click += (_, _) => { Stop(); ShowPoster(); };
        _seek.Scroll += (_, _) =>
        {
            bool playing = _timer.Enabled;
            if (playing) Stop();
            ShowFrame(_seek.Value);
            if (playing) Play();
        };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>開いている VDT を閉じて、何も表示していない状態に戻す。</summary>
    public void Close()
    {
        Stop();
        _file = null;
        _view.Image = null;
        _info.Text = "";
        _seek.Value = 0;
        _seek.Maximum = 0;
        _shownFrame = -1;
    }

    public void Open(string path)
    {
        Stop();
        try
        {
            _file = VdtFile.Load(path);
            _path = path;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "読み込めませんでした: " + ex.Message, "VDT", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var h = _file.Header;
        int n = _file.Voices.Count;
        _seek.Maximum = Math.Max(0, n - 1);
        _seek.Value = 0;
        _play.Enabled = _stop.Enabled = n > 0;
        _posterBtn.Enabled = h.Poster.Length > 0;
        string type = h.ColorType switch
        {
            0 => "0（無圧縮）",
            1 => "1（JPEG 類似圧縮 4:4:4）",
            2 => "2（JPEG 類似圧縮 4:2:2）",
            3 => "3（JPEG 類似圧縮 4:2:0）",
            _ => $"{h.ColorType}（不明）",
        };
        string cut = n < h.FrameCount ? $"　⚠ ヘッダは {h.FrameCount} フレームですがファイルが途中で終わっています" : "";
        _info.Text =
            $"{Path.GetFileName(path)}　コメント:「{h.Comment}」\n" +
            $"TimeScale {h.TimeScale}（{h.Fps:0.##} fps）／ ADPCM {h.Rate.Code} = {h.Rate.Label} ／ 1 フレーム音声 {h.PosterVoice.Length} バイト\n" +
            $"画質 {h.Quality} ／ 色質 {type} ／ {n} フレーム（{n / h.Fps:0.00} 秒）{cut}";
        ShowPoster();
        BuildWav();
    }

    void ShowPoster()
    {
        if (_file == null) return;
        _view.Image = _file.Header.Poster.Length == X68Color.FrameBytes ? X68Bitmap.FromFrame(_file.Header.Poster) : null;
        _view.Placeholder = "（ポスター: 展開できませんでした）";
        _shownFrame = -1;
    }

    void ShowFrame(int i)
    {
        if (_file == null || i < 0 || i >= _file.Images.Count || i == _shownFrame) return;
        _shownFrame = i;
        var img = _file.Images[i];
        _view.Placeholder = "（このフレームは展開できませんでした）";
        _view.Image = img is { Length: X68Color.FrameBytes } ? X68Bitmap.FromFrame(img) : null;
        _seek.Value = i;
        _frameLabel.Text = $"{i + 1} / {_file.Images.Count}";
    }

    // ADPCM を全部デコードして WAV に。途中からの再生時は先頭位置を変えて作り直す。
    short[] _pcm = Array.Empty<short>();

    void BuildWav()
    {
        if (_file == null) return;
        int vb = _file.Header.PosterVoice.Length;
        if (_file.Header.Rate.IsPcm16)
        {
            // v16: 16bit ビッグエンディアン、ステレオは L,R 交互 (フレームをまたいで続く)
            _pcm = new short[_file.Voices.Count * (vb / 2)];
            int n = 0;
            foreach (var v in _file.Voices)
                for (int k = 0; k + 1 < vb; k += 2) _pcm[n++] = (short)((v[k] << 8) | v[k + 1]);
            return;
        }
        var dec = new OkiAdpcm();
        _pcm = new short[_file.Voices.Count * vb * 2];
        for (int i = 0; i < _file.Voices.Count; i++)
            dec.Decode(_file.Voices[i], _pcm.AsSpan(i * vb * 2, vb * 2));
    }

    void Play()
    {
        if (_file == null || _file.Voices.Count == 0) return;
        Stop();
        _startFrame = _seek.Value >= _seek.Maximum ? 0 : _seek.Value;
        var h = _file.Header;
        int sps = h.Rate.NominalSamplesPerSecond;
        if (_sound.Checked && sps > 0 && _pcm.Length > 0)
        {
            int ch = h.Rate.IsPcm16 ? h.Rate.Channels : 1;
            int spf = h.Rate.IsPcm16 ? h.PosterVoice.Length / 2 : h.PosterVoice.Length * 2;
            int from = _startFrame * spf;
            if (ch == 2) from &= ~1;   // L から始める
            _wav = MakeWav(_pcm.AsSpan(from), sps, ch);
            _player = new SoundPlayer(_wav);
            try { _player.Play(); } catch { }
        }
        _clock.Restart();
        _timer.Start();
        _play.Enabled = false;
    }

    void Tick()
    {
        if (_file == null) return;
        int f = _startFrame + (int)(_clock.Elapsed.TotalSeconds * _file.Header.Fps);
        if (f >= _file.Images.Count)
        {
            if (_repeat.Checked) { _seek.Value = 0; Play(); return; }
            ShowFrame(_file.Images.Count - 1);
            Stop();
            return;
        }
        ShowFrame(f);
    }

    void Stop()
    {
        _timer.Stop();
        _clock.Stop();
        try { _player?.Stop(); } catch { }
        _player?.Dispose();
        _player = null;
        _wav?.Dispose();
        _wav = null;
        _play.Enabled = _file != null && _file.Voices.Count > 0;
    }

    static MemoryStream MakeWav(ReadOnlySpan<short> pcm, int rate, int ch = 1)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)ch);
        w.Write(rate); w.Write(rate * 2 * ch); w.Write((short)(2 * ch)); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var s in pcm) w.Write(s);
        w.Flush();
        ms.Position = 0;
        return ms;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
}
