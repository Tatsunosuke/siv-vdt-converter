using System.Globalization;
using VdtConverter.Core;

namespace VdtConverter.UI;

public sealed partial class MainForm : Form
{
    readonly AppSettings _cfg = AppSettings.Load();

    // ---- ファイル ----
    readonly TextBox _input = new() { Dock = DockStyle.Fill };
    readonly TextBox _output = new() { Dock = DockStyle.Fill };
    readonly TextBox _ffmpeg = new() { Dock = DockStyle.Fill };

    // ---- 映像 ----
    readonly ComboBox _fps = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
    readonly RadioButton _exact = new() { Text = "60Hz 基準（SiV 本来の速度: 60÷TimeScale fps、実時間どおり）", AutoSize = true, Checked = true };
    readonly RadioButton _ntsc = new() { Text = "NTSC に合わせる（59.94Hz 基準: 29.97 / 14.985 fps … ビデオ素材を 1:1 で取り込み）", AutoSize = true };
    readonly CheckBox _deint = new() { Text = "インターレース解除（yadif）", AutoSize = true };
    readonly ComboBox _colorType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    readonly NumericUpDown _quality = new() { Minimum = 1, Maximum = 100, Value = 30, Width = 60 };

    // ---- 縦横比 ----
    readonly RadioButton _aspAuto = new() { Text = "自動", AutoSize = true, Checked = true };
    readonly RadioButton _asp43 = new() { Text = "4:3", AutoSize = true };
    readonly RadioButton _asp169 = new() { Text = "16:9", AutoSize = true };
    readonly FitChoiceButton _fitCrop = new(FitMode.Crop, "高さに合わせてトリム", "画面いっぱい。左右が切れる");
    readonly FitChoiceButton _fitLetter = new(FitMode.Letterbox, "幅に合わせて全体表示", "全部見える。上下に黒帯");
    readonly FitChoiceButton _fitStretch = new(FitMode.Stretch, "引き伸ばし", "全部・いっぱい。絵が縦長に歪む");
    readonly FlowLayoutPanel _fitPanel = new() { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
    readonly Label _fitNote = new() { AutoSize = true, Padding = new Padding(0, 2, 0, 2) };

    // ---- 音声 ----
    readonly ComboBox _rate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    readonly CheckBox _extRates = new() { Text = "SiV 非対応の形式も", AutoSize = true };
    readonly ToolTip _leftTips = new() { AutoPopDelay = 15000 };
    readonly CheckBox _normalize = new() { Text = "ノーマライズ（ピークを揃える）", AutoSize = true };
    readonly NumericUpDown _volume = new() { Minimum = 10, Maximum = 400, Increment = 10, Value = 100, Width = 70 };

    // ---- 範囲（元動画のコマ番号）----
    readonly NumericUpDown _startF = new() { Maximum = 9_999_999, Width = 90, Enabled = false, ThousandsSeparator = true };
    readonly NumericUpDown _endF = new() { Maximum = 9_999_999, Width = 90, Enabled = false, ThousandsSeparator = true };
    readonly Button _rangeAll = new() { Text = "全体", AutoSize = true, Enabled = false };
    readonly Label _rangeInfo = new() { AutoSize = true, Text = "入力を選ぶと、コマ番号で範囲を指定できます。" };

    // ---- 動画を見ながら選ぶ ----
    readonly X68View _view = new() { Dock = DockStyle.Fill, MinimumSize = new Size(160, 120), Placeholder = "（入力動画を選んでください）" };
    readonly FrameScrubber _scroll = new() { Dock = DockStyle.Fill };
    readonly RangeBar _bar = new() { Dock = DockStyle.Fill, Enabled = false };
    readonly Label _cursorInfo = new() { AutoSize = true, Anchor = AnchorStyles.Left, Text = "コマ - / -" };
    readonly Button _setStart = new() { Text = "ここを開始に [I]", AutoSize = true, Enabled = false };
    readonly Button _setEnd = new() { Text = "ここを終了に [O]", AutoSize = true, Enabled = false };
    readonly Button _setPoster = new() { Text = "ここをポスターに [P]", AutoSize = true, Enabled = false };
    readonly Button _goStart = new() { Text = "⏮ 開始へ", AutoSize = true, Enabled = false };
    readonly Button _goEnd = new() { Text = "終了へ ⏭", AutoSize = true, Enabled = false };
    readonly System.Windows.Forms.Timer _viewDebounce = new() { Interval = 60 };
    readonly PreviewCache _cache = new();
    CancellationTokenSource _cacheCts = new();

    // ---- ポスター ----
    readonly RadioButton _posterVideo = new() { Text = "動画のコマ", AutoSize = true, Checked = true };
    readonly Label _posterInfo = new() { AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = Color.DarkOrange };
    readonly RadioButton _posterFile = new() { Text = "画像を指定", AutoSize = true };
    readonly TextBox _posterPath = new() { Dock = DockStyle.Fill };
    readonly Button _posterBrowse = new() { Text = "参照…", AutoSize = true };
    readonly X68View _posterView = new() { Size = new Size(160, 120), MinimumSize = new Size(160, 120), Placeholder = "（未設定）" };
    readonly FrameScrubber _posterScrub = new() { Dock = DockStyle.Fill };
    readonly Button _posterReset = new() { Text = "開始コマに戻す", AutoSize = true, Enabled = false };
    readonly System.Windows.Forms.Timer _posterDebounce = new() { Interval = 60 };
    int _posterFrame => _posterScrub.Value;
    bool _posterTouched;   // false のあいだはポスター = 開始コマ

    // ---- 実行 ----
    readonly TextBox _comment = new() { Dock = DockStyle.Fill, MaxLength = 200 };
    readonly Label _estimate = new() { AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 4) };
    readonly Button _run = new() { Text = "変換開始", AutoSize = true, Padding = new Padding(12, 2, 12, 2) };
    readonly Button _cancel = new() { Text = "キャンセル", AutoSize = true, Enabled = false };
    readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    readonly TextBox _log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 70 };

    readonly PreviewPanel _preview = new() { Dock = DockStyle.Fill };
    readonly StatusStrip _status = new() { SizingGrip = true, ShowItemToolTips = true };
    readonly ToolStripStatusLabel _playSiv = new() { BorderSides = ToolStripStatusLabelBorderSides.Right };
    readonly ToolStripStatusLabel _playHdd = new() { BorderSides = ToolStripStatusLabelBorderSides.Right };
    readonly ToolStripStatusLabel _playMmvp = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    bool tabsSelectedIsPreview() => _tabs.SelectedIndex == 1;

    MediaInfo? _info;
    double? _duration => _info?.Duration;
    CancellationTokenSource? _cts;
    CancellationTokenSource? _posterCts;
    bool _rangeUpdating;

    readonly string? _startupPath;

    public MainForm(string? startupPath = null)
    {
        _startupPath = startupPath;
        Text = AppTitle;
        Font = new Font("Yu Gothic UI", 9f);
        MinimumSize = new Size(1000, 760);
        Size = new Size(1180, 900);
        KeyPreview = true;
        Icon = LoadAppIcon();
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = _tabs;
        var tConv = new TabPage("変換") { Padding = new Padding(8) };
        var tPrev = new TabPage("プレビュー") { Padding = new Padding(8) };
        tConv.Controls.Add(BuildConvertPage());
        tPrev.Controls.Add(_preview);
        tabs.TabPages.Add(tConv);
        tabs.TabPages.Add(tPrev);
        Controls.Add(tabs);
        _status.Items.AddRange(new ToolStripItem[] { _playSiv, _playHdd, _playMmvp });
        Controls.Add(_status);       // Dock=Bottom (StatusStrip の既定)
        var menu = BuildMenu();
        Controls.Add(menu);          // 後から足した Top が上に来る
        MainMenuStrip = menu;

        _fitPanel.Controls.AddRange(new Control[] { _fitCrop, _fitLetter, _fitStretch });

        LoadSettings();
        SetupToolTips();
        WireEvents();
        HookDirty(this);
        UpdateEstimate();
        UpdateTitle();
        Shown += async (_, _) =>
        {
            // コマンドライン (エクスプローラーで .vdtproj や動画をこのアプリにドロップ・関連付け) から開く
            if (_startupPath == null || !File.Exists(_startupPath)) return;
            if (_startupPath.EndsWith(VdtProject.Extension, StringComparison.OrdinalIgnoreCase)) await OpenProject(_startupPath, confirmed: true);
            else if (_startupPath.EndsWith(".vdt", StringComparison.OrdinalIgnoreCase)) { _preview.Open(_startupPath); _tabs.SelectedIndex = 1; }
            else { _input.Text = _startupPath; await InputChanged(); }
        };
    }

    /// <summary>exe に埋め込んだ app.ico を読む (タイトルバー・タスクバー用)。見つからなければ既定のアイコン。</summary>
    static Icon? LoadAppIcon()
    {
        using var st = typeof(MainForm).Assembly.GetManifestResourceStream("VdtConverter.app.ico");
        return st == null ? null : new Icon(st);
    }

    sealed record FpsItem(int TimeScale, bool Ntsc)
    {
        public override string ToString() => Ntsc
            ? $"{60000.0 / 1001 / TimeScale:0.###} fps（NTSC・TimeScale {TimeScale}）"
            : $"{60 / TimeScale} fps（TimeScale {TimeScale}）";
    }

    /// <summary>NTSC の切り替えに合わせて、フレームレートの一覧の表示を書き換える (選択中の TimeScale は保つ)。</summary>
    void FillFps(int timeScale)
    {
        _fps.BeginUpdate();
        _fps.Items.Clear();
        foreach (var ts in Vdt.TimeScales)
            _fps.Items.Add(new FpsItem(ts, _ntsc.Checked));
        _fps.SelectedItem = _fps.Items.Cast<FpsItem>().FirstOrDefault(i => i.TimeScale == timeScale) ?? _fps.Items[0];
        _fps.EndUpdate();
    }

    // =====================================================================
    // レイアウト
    // =====================================================================

    Control BuildConvertPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));

        // ファイル
        var files = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        files.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        files.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddFileRow(files, "入力動画", _input, BrowseInput, "変換する動画を選びます。");
        AddFileRow(files, "出力 VDT", _output, BrowseOutput, "書き出す VDT ファイルの場所を選びます。");
        AddFileRow(files, "ffmpeg.exe", _ffmpeg, BrowseFfmpeg, "ffmpeg.exe の場所を選びます。");
        root.Controls.Add(Group("ファイル", files));

        // 中段: 左に設定、右にポスター
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 570));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var left = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        // 左の列は縦に収まるように詰める (長い説明はツールチップへ)。よく使う順: 映像 → 音声 → 範囲 → 縦横比
        var video = Stack(
            Line(new Label { Text = "フレームレート", AutoSize = true, Anchor = AnchorStyles.Left }, _fps, _deint),
            Line(new Label { Text = "圧縮", AutoSize = true, Anchor = AnchorStyles.Left }, _colorType,
                 new Label { Text = "画質", AutoSize = true, Anchor = AnchorStyles.Left }, _quality),
            _exact, _ntsc);
        left.Controls.Add(Group("映像（128×120・65536 色）", video, 530));

        var audio = Stack(
            Line(new Label { Text = "ADPCM サンプリング周波数", AutoSize = true, Anchor = AnchorStyles.Left }, _rate, _extRates),
            Line(_normalize, new Label { Text = "音量", AutoSize = true, Anchor = AnchorStyles.Left }, _volume,
                new Label { Text = "%", AutoSize = true, Anchor = AnchorStyles.Left }));
        left.Controls.Add(Group("音声（X68000 の ADPCM）", audio, 530));
        _leftTips.SetToolTip(_rate,
            "MSM6258 の 4bit ADPCM。SiV 0.97 が扱えるのは 無音 / 7.8kHz / 15.6kHz。\n" +
            "3.9kHz は SiV では「無音」と同じコード 0 になるため選べません。");
        _leftTips.SetToolTip(_extRates, "5.2kHz / 10.4kHz と、v16（16bit PCM・mmvp.x ＋ Mercury-Unit 用）も一覧に出します。\nどれも SiV 0.97 では再生できません。v16 は無圧縮（色質 0）のみです。");

        var range = Stack(
            Line(new Label { Text = "開始コマ", AutoSize = true, Anchor = AnchorStyles.Left }, _startF,
                 new Label { Text = "〜　終了コマ", AutoSize = true, Anchor = AnchorStyles.Left }, _endF, _rangeAll),
            _rangeInfo);
        left.Controls.Add(Group("範囲（元動画のコマ番号・終了コマも含む）", range, 530));

        var aspect = Stack(
            Line(new Label { Text = "入力の縦横比", AutoSize = true, Anchor = AnchorStyles.Left }, _aspAuto, _asp43, _asp169),
            _fitPanel,
            _fitNote);
        left.Controls.Add(Group("縦横比（4:3 の画面への収め方）", aspect, 530));
        _leftTips.SetToolTip(_asp169, "DVD などの横長スクイーズ素材で、自動判定が 4:3 になってしまうときに選びます");

        mid.Controls.Add(left, 0, 0);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 動画を見ながら選ぶ
        // スクロールバーなどは下にドッキングして必ず見えるようにし、画像は残りの場所に縮めて出す
        var viewer = new Panel { Dock = DockStyle.Fill };
        // 自動サイズ計算に頼らず、高さ固定のパネルを上から積む (TableLayout の AutoSize 行だとスクロールバーが高さ 0 に潰れた)
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Dock = DockStyle.Top, Height = 34 };
        foreach (var b in new Control[] { _setStart, _setEnd, _setPoster, _goStart, _goEnd }) { b.Margin = new Padding(3); buttons.Controls.Add(b); }
        var infoRow = new Panel { Dock = DockStyle.Top, Height = 22, Padding = new Padding(3, 3, 0, 0) };
        _cursorInfo.Dock = DockStyle.Fill;
        _cursorInfo.AutoSize = false;
        infoRow.Controls.Add(_cursorInfo);
        var barRow = new Panel { Dock = DockStyle.Top, Height = 22, Padding = new Padding(FrameScrubber.SideWidth, 0, FrameScrubber.SideWidth, 0) };
        _bar.Dock = DockStyle.Fill;
        _bar.Margin = Padding.Empty;
        barRow.Controls.Add(_bar);   // 帯はバー部分の幅にそろえる (左右のボタンぶん空ける)
        _scroll.Dock = DockStyle.Top;
        var controls = new Panel { Dock = DockStyle.Bottom, Height = 30 + 22 + 22 + 34 + 4 };
        // Dock=Top は後から追加したものほど上に来る → 下から順に追加
        controls.Controls.Add(buttons);
        controls.Controls.Add(infoRow);
        controls.Controls.Add(barRow);
        controls.Controls.Add(_scroll);
        viewer.Controls.Add(_view);      // 先に Fill、
        viewer.Controls.Add(controls);   // 後から Bottom を足すと Bottom が先に場所を取る
        var vg = _viewerGroup = Group(ViewerTitle, viewer);
        vg.AutoSize = false;             // 中身の大きさに縮まないように (スクロールバーが隠れていた原因)
        vg.Dock = DockStyle.Fill;
        right.Controls.Add(vg, 0, 0);

        // ポスター
        var posterBox = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, AutoSize = true };
        posterBox.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        posterBox.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));   // ポスター用スクロールバーの行は高さ固定
        posterBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        posterBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var pf = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        pf.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pf.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pf.Controls.Add(_posterPath, 0, 0);
        pf.Controls.Add(_posterBrowse, 1, 0);
        var pleft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
        pleft.Controls.Add(Line(_posterVideo, _posterInfo));
        pleft.Controls.Add(Line(_posterReset));
        pleft.Controls.Add(Line(_posterFile));
        pleft.Controls.Add(pf);
        pleft.Controls.Add(Note("触らなければ開始コマになります（帯の ▼ がポスターの位置）。\n上の画面の「ここをポスターに」でも決められます。"));
        posterBox.Controls.Add(pleft, 0, 0);
        posterBox.Controls.Add(_posterView, 1, 0);
        posterBox.Controls.Add(_posterScrub, 0, 1);
        posterBox.SetColumnSpan(_posterScrub, 2);
        var pg = _posterGroup = Group(PosterTitle, posterBox);
        right.Controls.Add(pg, 0, 1);

        mid.Controls.Add(right, 1, 0);
        root.Controls.Add(mid);

        // コメント
        var cm = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        cm.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        cm.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cm.Controls.Add(new Label { Text = "コメント（\"SiV\" の直後〜改行まで）", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        cm.Controls.Add(_comment, 1, 0);
        cm.Controls.Add(_estimate, 0, 1);
        cm.SetColumnSpan(_estimate, 2);
        root.Controls.Add(cm);

        // 実行
        var run = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        run.Controls.Add(_run, 0, 0);
        run.Controls.Add(_cancel, 1, 0);
        _progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        run.Controls.Add(_progress, 2, 0);
        root.Controls.Add(run);

        root.Controls.Add(_log);
        return root;
    }

    void AddFileRow(TableLayoutPanel t, string label, TextBox box, Action browse, string buttonTip)
    {
        int r = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, r);
        t.Controls.Add(box, 1, r);
        var b = new Button { Text = "参照…", AutoSize = true };
        _leftTips.SetToolTip(b, buttonTip);
        b.Click += (_, _) => browse();
        t.Controls.Add(b, 2, r);
    }

    static GroupBox Group(string title, Control content, int width = 0)
    {
        var g = new GroupBox { Text = title, Padding = new Padding(8), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        if (width > 0) { g.Width = width; g.MinimumSize = new Size(width, 0); }
        else g.Dock = DockStyle.Fill;
        content.Dock = DockStyle.Fill;
        g.Controls.Add(content);
        return g;
    }

    static FlowLayoutPanel Stack(params Control[] items)
    {
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        foreach (var c in items) { c.Margin = new Padding(3, 3, 3, 3); f.Controls.Add(c); }
        return f;
    }

    static FlowLayoutPanel Line(params Control[] items)
    {
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        foreach (var c in items) { c.Margin = new Padding(3, 4, 6, 3); f.Controls.Add(c); }
        return f;
    }

    static Label Note(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText };

    // =====================================================================
    // 設定・イベント
    // =====================================================================

    void LoadSettings()
    {
        _ffmpeg.Text = File.Exists(_cfg.FfmpegPath) ? _cfg.FfmpegPath : (Ffmpeg.Locate() ?? "");
        _ntsc.Checked = _cfg.Ntsc; _exact.Checked = !_cfg.Ntsc;
        FillFps(_cfg.TimeScale);
        SelectedFit = (FitMode)Math.Clamp(_cfg.Fit, 0, 2);
        _asp43.Checked = _cfg.SourceAspect == (int)SourceAspect.Ratio4x3;
        _asp169.Checked = _cfg.SourceAspect == (int)SourceAspect.Ratio16x9;
        _aspAuto.Checked = !_asp43.Checked && !_asp169.Checked;
        _deint.Checked = _cfg.Deinterlace;
        _colorType.Items.AddRange(new object[]
        {
            "無圧縮（色質 0）",
            "色質 1：JPEG 類似 4:4:4（色がきれい）",
            "色質 2：JPEG 類似 4:2:2",
            "色質 3：JPEG 類似 4:2:0（いちばん小さい）",
        });
        _colorType.SelectedIndex = Math.Clamp(_cfg.ColorType, 0, 3);
        _quality.Value = Math.Clamp(_cfg.Quality, 1, 100);
        _quality.Enabled = _colorType.SelectedIndex > 0;
        _extRates.Checked = _cfg.ShowExtendedRates;
        FillRates(AdpcmRate.FromCode(_cfg.RateCode));
        _normalize.Checked = _cfg.Normalize;
        _volume.Value = Math.Clamp(_cfg.VolumePercent, 10, 400);
        _comment.Text = _cfg.Comment;
    }

    void SaveSettings()
    {
        _cfg.FfmpegPath = _ffmpeg.Text;
        _cfg.TimeScale = SelectedTimeScale;
        _cfg.Ntsc = _ntsc.Checked;
        _cfg.Fit = (int)SelectedFit;
        _cfg.SourceAspect = (int)SelectedAspect;
        _cfg.Deinterlace = _deint.Checked;
        _cfg.ColorType = Math.Max(0, _colorType.SelectedIndex);
        _cfg.Quality = (int)_quality.Value;
        _cfg.RateCode = SelectedRate.Code;
        _cfg.ShowExtendedRates = _extRates.Checked;
        _cfg.Normalize = _normalize.Checked;
        _cfg.VolumePercent = (int)_volume.Value;
        _cfg.Comment = _comment.Text;
        _cfg.Save();
    }

    void FillRates(AdpcmRate select)
    {
        _rate.Items.Clear();
        foreach (var r in AdpcmRate.All)
            if (r.SivCompatible || _extRates.Checked) _rate.Items.Add(r);
        _rate.SelectedItem = _rate.Items.Cast<AdpcmRate>().FirstOrDefault(r => r.Code == select.Code) ?? AdpcmRate.K15_6;
    }

    FitMode SelectedFit
    {
        get => _fitPanel.Controls.OfType<FitChoiceButton>().FirstOrDefault(b => b.Checked)?.Mode ?? FitMode.Letterbox;
        set { foreach (var b in _fitPanel.Controls.OfType<FitChoiceButton>()) if (b.Mode == value) b.Checked = true; }
    }

    SourceAspect SelectedAspect => _asp43.Checked ? SourceAspect.Ratio4x3 : _asp169.Checked ? SourceAspect.Ratio16x9 : SourceAspect.Auto;

    int CurFrame => _scroll.Value;

    /// <summary>ボタンやバーを触ったら、そのスクロールバーにキャレットを置く (以後キーボードはそちらを動かす)。</summary>
    GroupBox? _viewerGroup, _posterGroup;
    const string ViewerTitle = "動画を見ながら選ぶ（← → 1 コマ／Shift 10 秒／Ctrl+Shift 1 分）";
    const string PosterTitle = "ポスター（サムネイル）";

    /// <summary>
    /// グループの中をクリックしたら (入力欄以外)、そのグループのスクロールバーにキャレットを移す。
    /// グループ内のボタンを押した後もキャレットはスクロールバーに戻す。
    /// </summary>
    void HookActivation(Control root, FrameScrubber target)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBoxBase or NumericUpDown or ComboBox or RadioButton or CheckBox: continue;   // 入力用はそのまま
                case FrameScrubber: continue;                                                     // 自分で処理する
                case ButtonBase b: b.Click += (_, _) => Activate(target); break;
                default: c.MouseDown += (_, _) => Activate(target); break;
            }
            HookActivation(c, target);
        }
    }

    /// <summary>キー操作の対象になっている方のグループ名に印を付ける。</summary>
    void MarkActiveGroups()
    {
        if (_viewerGroup != null) _viewerGroup.Text = (_scroll.ContainsFocus ? "▶ キー操作中：" : "") + ViewerTitle;
        if (_posterGroup != null) _posterGroup.Text = (_posterScrub.ContainsFocus ? "▶ キー操作中：" : "") + PosterTitle;
    }

    static void Activate(FrameScrubber s) { if (s.Enabled && !s.ContainsFocus) s.Focus(); }
    int LastFrame => Math.Max(0, (_info?.FrameCount ?? 1) - 1);
    bool HasFrames => _info?.FrameCount is > 0;

    int SelectedTimeScale => (_fps.SelectedItem as FpsItem)?.TimeScale ?? 2;
    AdpcmRate SelectedRate => _rate.SelectedItem as AdpcmRate ?? AdpcmRate.K15_6;

    void WireEvents()
    {
        _fps.SelectedIndexChanged += (_, _) => UpdateEstimate();
        _exact.CheckedChanged += (_, _) => { FillFps(SelectedTimeScale); UpdateEstimate(); };
        _rate.SelectedIndexChanged += (_, _) => UpdateEstimate();
        _colorType.SelectedIndexChanged += (_, _) => { _quality.Enabled = _colorType.SelectedIndex > 0; UpdateEstimate(); };
        _extRates.CheckedChanged += (_, _) => FillRates(SelectedRate);
        _startF.ValueChanged += (_, _) => RangeChanged(fromStart: true);
        _endF.ValueChanged += (_, _) => RangeChanged(fromStart: false);
        _rangeAll.Click += (_, _) => SetRange(0, LastFrame);
        foreach (var b in _fitPanel.Controls.OfType<FitChoiceButton>())
            b.CheckedChanged += (s, _) => { if (((FitChoiceButton)s!).Checked) RefreshImages(); };
        foreach (var r in new[] { _aspAuto, _asp43, _asp169 })
            r.CheckedChanged += (s, _) => { if (((RadioButton)s!).Checked) { UpdateAspectUi(); RefreshImages(); } };
        _deint.CheckedChanged += (_, _) => RefreshImages();
        _posterVideo.CheckedChanged += (_, _) => { UpdateControls(); RefreshPoster(); };

        _scroll.ValueChanged += (_, _) => { UpdateCursorUi(); RefreshView(); };
        _viewDebounce.Tick += (_, _) => { _viewDebounce.Stop(); LoadView(); };
        _bar.Seek += (_, f) => SetCursor(f);
        _scroll.Activated += (_, _) => Activate(_scroll);
        // クリックでの有効化: グループの中 (入力欄以外) を触ったら、そのグループのスクロールバーへ
        if (_viewerGroup != null) { _viewerGroup.MouseDown += (_, _) => Activate(_scroll); HookActivation(_viewerGroup, _scroll); }
        if (_posterGroup != null) { _posterGroup.MouseDown += (_, _) => Activate(_posterScrub); HookActivation(_posterGroup, _posterScrub); }
        foreach (var sc in new[] { _scroll, _posterScrub })
        {
            sc.Enter += (_, _) => MarkActiveGroups();
            sc.Leave += (_, _) => MarkActiveGroups();
        }
        // タブ順: 本動画のバー → 本動画のボタン → ポスターのバー
        _scroll.TabIndex = 0;
        if (_scroll.Parent is { } row)
            foreach (Control c in row.Controls) if (!ReferenceEquals(c, _scroll)) c.TabIndex = 1 + row.Controls.Count - row.Controls.IndexOf(c);
        _bar.MouseDown += (_, _) => Activate(_scroll);
        _view.MouseDown += (_, _) => Activate(_scroll);
        _posterScrub.Activated += (_, _) => { _posterTouched = true; Activate(_posterScrub); };
        _posterView.MouseDown += (_, _) => Activate(_posterScrub);
        _posterScrub.ValueChanged += (_, _) => { UpdateRangeUi(); RefreshPoster(); };
        _posterDebounce.Tick += (_, _) => { _posterDebounce.Stop(); LoadPosterFrame(); };
        _posterReset.Click += (_, _) => { _posterTouched = false; SetPosterFrame((int)_startF.Value, touched: false); };
        _setStart.Click += (_, _) => SetRange(CurFrame, Math.Max(CurFrame, (int)_endF.Value));
        _setEnd.Click += (_, _) => SetRange(Math.Min(CurFrame, (int)_startF.Value), CurFrame);
        _setPoster.Click += (_, _) => SetPosterFrame(CurFrame, touched: true);
        _goStart.Click += (_, _) => SetCursor((int)_startF.Value);
        _goEnd.Click += (_, _) => SetCursor((int)_endF.Value);
        _posterBrowse.Click += (_, _) => BrowsePoster();
        _posterPath.Leave += (_, _) => RefreshPoster();
        _input.Leave += async (_, _) => await InputChanged();
        _run.Click += async (_, _) => await RunConvert();
        _cancel.Click += (_, _) => _cts?.Cancel();
        FormClosing += (_, e) =>
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            _cts?.Cancel(); _posterCts?.Cancel(); _cacheCts.Cancel(); SaveSettings();
        };
        UpdateControls();
        UpdateAspectUi();

        AllowDrop = true;
        DragEnter += (_, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            {
                if (files[0].EndsWith(".vdt", StringComparison.OrdinalIgnoreCase)) { _preview.Open(files[0]); return; }
                if (files[0].EndsWith(VdtProject.Extension, StringComparison.OrdinalIgnoreCase)) { await OpenProject(files[0]); return; }
                _input.Text = files[0];
                await InputChanged();
            }
        };
    }

    void UpdateControls()
    {
        _posterPath.Enabled = _posterBrowse.Enabled = _posterFile.Checked;
        _posterInfo.Enabled = _posterVideo.Checked;
        bool on = HasFrames;
        foreach (var c in new Control[] { _scroll, _bar, _setStart, _setEnd, _setPoster, _goStart, _goEnd, _startF, _endF, _rangeAll })
            c.Enabled = on;
        _setPoster.Enabled = on && _posterVideo.Checked;
        _posterScrub.Enabled = _posterReset.Enabled = on && _posterVideo.Checked;

    }

    // ---- キーボード (テキスト入力中は使わない) ----

    // 矢印キーはボタンなどにフォーカスがあるとフォーカス移動に使われて KeyDown まで来ないので、ここで先に拾う
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleNavKey(keyData)) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    bool HandleNavKey(Keys keyData)
    {
        // キャレットがどちらかのスクロールバーにあるときだけ、そのスクロールバーを動かす
        FrameScrubber? target = _posterScrub.ContainsFocus ? _posterScrub : _scroll.ContainsFocus ? _scroll : null;
        if (target == null || !target.Enabled || !HasFrames) return false;
        var key = keyData & Keys.KeyCode;
        bool shift = (keyData & Keys.Shift) != 0, ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0;
        if (alt) return false;
        bool main = ReferenceEquals(target, _scroll);
        // 1 コマ / Shift: 10 秒 / Ctrl+Shift: 1 分
        int step = shift && ctrl ? Frames(60) : shift ? Frames(10) : 1;
        switch (key)
        {
            case Keys.Left when !ctrl || shift: Move(-step); return true;
            case Keys.Right when !ctrl || shift: Move(+step); return true;
            case Keys.Home when !shift && !ctrl: target.Value = main ? (int)_startF.Value : 0; return true;
            case Keys.End when !shift && !ctrl: target.Value = main ? (int)_endF.Value : LastFrame; return true;
            case Keys.I when main && !shift && !ctrl: _setStart.PerformClick(); return true;
            case Keys.O when main && !shift && !ctrl: _setEnd.PerformClick(); return true;
            case Keys.P when main && !shift && !ctrl: if (_posterVideo.Checked) _setPoster.PerformClick(); return true;
        }
        return false;

        void Move(int n)
        {
            if (!main) _posterTouched = true;
            target.Step(n);
        }
    }

    // ---- 縦横比 ----

    void UpdateAspectUi()
    {
        var info = _info;
        _aspAuto.Text = info?.DisplayAspect is double d
            ? $"自動（{AspectRules.Describe(d)}・{info.Width}×{info.Height}）"
            : "自動";
        double? a = AspectRules.Resolve(SelectedAspect, info?.DisplayAspect);
        bool is43 = a is double v && AspectRules.IsFourThree(v);
        foreach (var b in _fitPanel.Controls.OfType<FitChoiceButton>())
            b.SourceAspect = a is double x && !is43 ? x : AspectRules.SixteenNine;
        _fitPanel.Enabled = !is43;
        if (is43)
        {
            _fitNote.Text = "✔ 4:3 の素材なので、トリムも黒帯もなしでそのまま画面いっぱいに使います。";
            _fitNote.ForeColor = Color.SeaGreen;
        }
        else if (a is double w)
        {
            _fitNote.Text = $"{AspectRules.Describe(w)} の素材です。上の 3 つから収め方を選んでください（右の画面にすぐ反映されます）。";
            _fitNote.ForeColor = SystemColors.ControlText;
        }
        else
        {
            _fitNote.Text = "入力を選ぶと縦横比を調べます（分からないときは 16:9 として扱います）。";
            _fitNote.ForeColor = SystemColors.GrayText;
        }
    }

    // ---- コマ・範囲・ポスター ----

    /// <summary>入力が変わったら、スクロールバーと範囲を動画全体に合わせる。</summary>
    void SetupFrames()
    {
        int last = LastFrame;
        _rangeUpdating = true;
        _startF.Maximum = _endF.Maximum = Math.Max(1, last);
        _startF.Value = 0;
        _endF.Value = last;
        _rangeUpdating = false;
        _scroll.Setup(last + 1, _info?.Fps ?? 30);
        _posterScrub.Setup(last + 1, _info?.Fps ?? 30);
        _posterTouched = false;

        UpdateControls();
        UpdateRangeUi();
        UpdateCursorUi();
    }

    /// <summary>元動画で sec 秒ぶんのコマ数。</summary>
    int Frames(double sec) => Math.Max(1, (int)Math.Round(sec * (_info?.Fps ?? 30)));

    void SetCursor(int frame)
    {
        if (!HasFrames) return;
        frame = Math.Clamp(frame, 0, LastFrame);
        _scroll.Value = frame;   // ValueChanged → デバウンス → RefreshView
    }

    void SetRange(int start, int end)
    {
        if (!HasFrames) return;
        start = Math.Clamp(start, 0, LastFrame);
        end = Math.Clamp(end, start, LastFrame);
        _rangeUpdating = true;
        _endF.Value = Math.Max(_endF.Value, start);   // 一時的に start > end にならないように
        _startF.Value = start;
        _endF.Value = end;
        _rangeUpdating = false;
        RangeChanged(fromStart: true);
    }

    void RangeChanged(bool fromStart)
    {
        if (_rangeUpdating) return;
        // 開始 > 終了 になったら、動かしていない方を合わせる
        if (_startF.Value > _endF.Value)
        {
            _rangeUpdating = true;
            if (fromStart) _endF.Value = _startF.Value; else _startF.Value = _endF.Value;
            _rangeUpdating = false;
        }
        if (!_posterTouched) SetPosterFrame((int)_startF.Value, touched: false);
        UpdateRangeUi();
        UpdateEstimate();
    }

    void SetPosterFrame(int frame, bool touched)
    {
        _posterTouched |= touched;
        bool same = _posterScrub.Value == Math.Clamp(frame, 0, LastFrame);
        _posterScrub.Value = frame;   // ValueChanged → デバウンス → RefreshPoster
        UpdateRangeUi();
        if (same && touched) RefreshPoster();
    }

    void UpdateRangeUi()
    {
        if (_info is not { } info || !HasFrames)
        {
            _rangeInfo.Text = "入力を選ぶと、コマ番号で範囲を指定できます。";
            _posterInfo.Text = "";
            return;
        }
        int s = (int)_startF.Value, e = (int)_endF.Value, n = e - s + 1;
        _rangeInfo.Text = $"{Clock(info.FrameTime(s))} 〜 {Clock(info.FrameTime(e))}　{n:N0} コマ（{n / info.Fps:0.000} 秒）　元動画 {info.Fps:0.###} fps・全 {info.FrameCount:N0} コマ";
        _posterInfo.Text = $"▼ コマ {_posterFrame:N0}（{Clock(info.FrameTime(_posterFrame))}）" + (_posterTouched ? "" : "＝開始コマ");
        _bar.SetState(LastFrame + 1, s, e, _posterVideo.Checked ? _posterFrame : -1, CurFrame);
    }

    void UpdateCursorUi()
    {
        if (_info is not { } info || !HasFrames) { _cursorInfo.Text = "コマ - / -"; return; }
        int c = CurFrame;
        string where = c < _startF.Value || c > _endF.Value ? "　（範囲外）" : "";
        _cursorInfo.Text = $"コマ {c:N0} / {LastFrame:N0}　{Clock(info.FrameTime(c))} / {Clock(info.Duration ?? 0)}{where}";
        _bar.SetState(LastFrame + 1, (int)_startF.Value, (int)_endF.Value, _posterVideo.Checked ? _posterFrame : -1, c);
    }

    static string Clock(double s) => TimeSpan.FromSeconds(Math.Max(0, s)).ToString(s >= 3600 ? @"h\:mm\:ss\.fff" : @"mm\:ss\.fff");

    // =====================================================================
    // ファイル選択
    // =====================================================================

    async void BrowseInput()
    {
        using var d = new OpenFileDialog
        {
            Title = "変換する動画を選択",
            Filter = "動画ファイル|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.mpg;*.mpeg;*.m2ts;*.ts;*.webm;*.flv|すべてのファイル|*.*",
            InitialDirectory = Directory.Exists(_cfg.LastInputDir) ? _cfg.LastInputDir : "",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _input.Text = d.FileName;
        _cfg.LastInputDir = Path.GetDirectoryName(d.FileName) ?? "";
        await InputChanged();
    }

    void BrowseOutput()
    {
        using var d = new SaveFileDialog
        {
            Title = "保存する VDT ファイル",
            Filter = "SiV 動画データ (*.vdt)|*.vdt",
            DefaultExt = "vdt",
            FileName = SuggestOutputName(),
            InitialDirectory = Directory.Exists(_cfg.LastOutputDir) ? _cfg.LastOutputDir : "",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _output.Text = d.FileName;
        _cfg.LastOutputDir = Path.GetDirectoryName(d.FileName) ?? "";
    }

    void BrowseFfmpeg()
    {
        using var d = new OpenFileDialog { Title = "ffmpeg.exe を選択", Filter = "ffmpeg.exe|ffmpeg.exe|実行ファイル|*.exe" };
        if (d.ShowDialog(this) == DialogResult.OK) _ffmpeg.Text = d.FileName;
    }

    void BrowsePoster()
    {
        using var d = new OpenFileDialog
        {
            Title = "ポスターにする画像",
            Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|すべてのファイル|*.*",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _posterPath.Text = d.FileName;
        RefreshPoster();
    }

    string SuggestOutputName()
    {
        if (string.IsNullOrWhiteSpace(_input.Text)) return "MOVIE.VDT";
        // Human68k のファイル名 (8+3, 半角) を意識して短めに
        var n = new string(Path.GetFileNameWithoutExtension(_input.Text)
            .Where(c => c < 0x80 && (char.IsLetterOrDigit(c) || c == '_' || c == '-')).ToArray()).ToUpperInvariant();
        if (n.Length == 0) n = "MOVIE";
        if (n.Length > 8) n = n[..8];
        return n + ".VDT";
    }

    async Task InputChanged()
    {
        if (!File.Exists(_input.Text)) return;
        if (string.IsNullOrWhiteSpace(_output.Text))
            _output.Text = Path.Combine(Path.GetDirectoryName(_input.Text) ?? "", SuggestOutputName());
        _info = null;
        SetupFrames();
        UpdateEstimate();
        UpdateAspectUi();
        if (File.Exists(_ffmpeg.Text))
        {
            try
            {
                _info = await new Ffmpeg(_ffmpeg.Text).Probe(_input.Text, CancellationToken.None);
                if (_info.HasVideo && _info.DisplayAspect is double a)
                    Log($"入力: {_info.Width}×{_info.Height}、表示縦横比 {AspectRules.Describe(a)}、{_info.Fps:0.###} fps" +
                        (_info.Duration is double d ? $"、{d:0.00} 秒" : ""));
                else if (!_info.HasVideo) Log("映像トラックが見つかりませんでした。");
            }
            catch (Exception ex) { Log("入力の情報を取得できませんでした: " + ex.Message); }
        }
        UpdateEstimate();
        UpdateAspectUi();
        SetupFrames();
        ApplyPendingFrames();   // プロジェクトを開いたときの範囲・ポスター・位置
        UpdateEstimate();
        RefreshImages();
    }

    // =====================================================================
    // 見積もり・ポスター
    // =====================================================================

    ConvertOptions CurrentOptions() => new()
    {
        Input = _input.Text,
        Output = _output.Text,
        FfmpegPath = _ffmpeg.Text,
        TimeScale = SelectedTimeScale,
        Timing = _ntsc.Checked ? TimingMode.Ntsc : TimingMode.Exact30,
        Fit = SelectedFit,
        Aspect = SelectedAspect,
        DetectedAspect = _info?.DisplayAspect,
        Deinterlace = _deint.Checked,
        ColorType = Math.Max(0, _colorType.SelectedIndex),
        Quality = (int)_quality.Value,
        Rate = SelectedRate,
        Normalize = _normalize.Checked,
        Volume = (double)_volume.Value / 100.0,
        Start = HasFrames ? _info!.FrameToSeconds((int)_startF.Value) : 0,
        Length = HasFrames ? ((int)_endF.Value - (int)_startF.Value + 1) / _info!.Fps : 0,
        Poster = _posterFile.Checked ? PosterSource.ImageFile : PosterSource.VideoFrame,
        PosterImage = _posterPath.Text,
        PosterTime = HasFrames ? _info!.FrameToSeconds(_posterFrame) : 0,
        Comment = _comment.Text,
    };

    void UpdateEstimate()
    {
        var o = CurrentOptions();
        int vb = Vdt.VoiceBytesPerFrame(o.Rate, o.TimeScale);
        string per = o.ColorType == 0
            ? $"1 フレーム = 画像 30,720 + 音声 {vb:N0} バイト"
            : $"1 フレーム = 圧縮画像（色質 {o.ColorType}・画質 {o.Quality}、大きさは絵しだい）+ 音声 {vb:N0} バイト";
        if (_duration is not double d)
        {
            _estimate.Text = per + "　（入力を選ぶと長さ・サイズの目安を表示します）";
            UpdatePlayability(null);
            return;
        }
        double len = Math.Max(0, d - o.Start);
        if (o.Length > 0) len = Math.Min(len, o.Length);
        int frames = (int)Math.Round(len / o.FrameDuration);
        long bytes = 31100L + (long)frames * (X68Color.FrameBytes + vb);
        long mem = Converter.PlaybackMemory(frames, o.Rate, o.TimeScale);
        string warn = mem > 11L * 1024 * 1024
            ? "　⚠ 12MB 機のメモリには全部載りません。SiV.X・SIVP.X では途中までですが、SIVPH.X・SIVP@H.X なら HDD から読みながら最後まで再生できます"
            : "";
        string file = o.ColorType == 0 ? $"ファイル約 {bytes / 1024.0 / 1024.0:0.00} MB" : $"ファイルは無圧縮（{bytes / 1024.0 / 1024.0:0.00} MB）より小さくなります";
        _estimate.Text = $"{per}　／　{len:0.00} 秒 → 約 {frames:N0} フレーム、{file}、" +
                         $"X68000 側の展開メモリ約 {mem / 1024.0 / 1024.0:0.00} MB{warn}";
        _estimate.ForeColor = warn.Length > 0 ? Color.DarkOrange : SystemColors.ControlText;   // HDD 版なら再生できるので赤ではなく注意の色
        UpdatePlayability(mem);
    }

    /// <summary>ステータスバーに、いまの設定で再生できるプレイヤーを出す。</summary>
    /// <param name="mem">X68000 側で全フレームを展開するのに要るメモリ (入力が未選択なら null)。</param>
    void UpdatePlayability(long? mem)
    {
        var o = CurrentOptions();
        var (siv, mmvp) = Vdt.Playability(o.Rate, o.TimeScale, o.ColorType);

        // SiV.X / SIVP.X は全フレームをメモリに展開する。形式は OK でも長すぎると途中までしか再生できない
        if (siv != null) Show(_playSiv, "SiV.X・SIVP.X", siv, false);
        else if (mem > 11L * 1024 * 1024)
        {
            _playSiv.Text = "⚠ SiV.X・SIVP.X：長すぎて 12MB 機のメモリに載らず、途中までしか再生できません";
            _playSiv.ForeColor = Color.DarkOrange;
            _playSiv.ToolTipText = $"展開に約 {mem / 1024.0 / 1024.0:0.0} MB 要ります。SiV.X・SIVP.X は全フレームをメモリに読み込むので、入りきらない分は再生されません。\n" +
                                   "範囲を短くするか、SIVPH.X・SIVP@H.X（HDD から読みながら再生）を使ってください。";
        }
        else Show(_playSiv, "SiV.X・SIVP.X", null, false);

        // SIVPH.X / SIVP@H.X は入りきらないときは HDD から読むので、長さの制限はない
        Show(_playHdd, "SIVPH.X・SIVP@H.X", siv, false);
        Show(_playMmvp, "mmvp.x", mmvp, o.Rate.IsPcm16);

        static void Show(ToolStripStatusLabel l, string name, string? why, bool mercury)
        {
            l.Text = why == null ? $"✔ {name} で再生できます" + (mercury ? "（Mercury-Unit が必要）" : "") : $"✖ {name}：{why}";
            l.ForeColor = why == null ? Color.DarkGreen : Color.Firebrick;
            l.ToolTipText = why == null
                ? $"いまの設定（フレームレート・音声・圧縮・長さ）なら {name} で再生できます。"
                : $"いまの設定では {name} で再生できません。\n{why}";
        }
    }

    void RefreshImages() { RefreshView(); RefreshPoster(); }

    // ---- コマ送りプレビュー (PreviewCache 経由。ffmpeg は足りないときに前後 120 コマまとめて 1 回だけ) ----

    /// <summary>キャッシュを今の設定に合わせる。設定が変わっていたら読み込み中のものも止める。</summary>
    bool PrepareCache(out ConvertOptions o)
    {
        o = CurrentOptions();
        if (!File.Exists(o.FfmpegPath) || !HasFrames || !File.Exists(o.Input)) return false;
        string key = $"{o.Input}|{o.VideoFit}|{o.Aspect}|{o.Deinterlace}|{_info!.Fps}";
        if (key != _cacheKey)
        {
            _cacheKey = key;
            _cache.Use(key);          // 中身を捨てる
            _cacheCts.Cancel();       // 古い設定で読み込み中のものは止める
            _cacheCts = new CancellationTokenSource();
        }
        return true;
    }
    string _cacheKey = "";

    /// <summary>「動画を見ながら選ぶ」の大きな画面に、いま見ているコマを X68000 での見え方で出す。</summary>
    void RefreshView()
    {
        if (!PrepareCache(out _))
        {
            _view.Placeholder = File.Exists(_ffmpeg.Text) ? "（入力動画を選んでください）" : "（ffmpeg.exe を指定してください）";
            _view.Image = null;
            return;
        }
        int k = CurFrame;
        if (_cache.TryGet(k, out var img))
        {
            _view.Image = X68Bitmap.FromFrame(img);
            Prefetch(k);
        }
        else
        {
            // バーをドラッグ中に何度も読みに行かないよう、少し待ってから
            _viewDebounce.Stop();
            _viewDebounce.Start();
        }
    }

    async void LoadView()
    {
        if (!PrepareCache(out var o)) return;
        int k = CurFrame;
        try
        {
            await _cache.Load(k, LastFrame, o, _info!, _cacheCts.Token);
            if (CurFrame == k && !_cache.Has(k)) { _view.Image = null; _view.Placeholder = "（読み込めませんでした）"; return; }
            RefreshView();   // その間に動いていたら、今の位置で出し直す
        }
        catch (Exception ex) { Log("プレビュー: " + ex.Message); }
    }

    /// <summary>コマ送りの先回り。前後のコマが手元に無ければ裏で読んでおく。</summary>
    async void Prefetch(int k)
    {
        if (!PrepareCache(out var o)) return;
        try
        {
            int ahead = k + 40, behind = k - 10;
            if (ahead <= LastFrame && !_cache.Has(ahead)) await _cache.Load(ahead, LastFrame, o, _info!, _cacheCts.Token);
            else if (behind >= 0 && !_cache.Has(behind)) await _cache.Load(behind, LastFrame, o, _info!, _cacheCts.Token, forward: false);
        }
        catch (Exception ex) { Log("プレビュー: " + ex.Message); }
    }

    async void LoadPosterFrame()
    {
        if (!_posterVideo.Checked || !PrepareCache(out var o)) return;
        int k = _posterFrame;
        try
        {
            await _cache.Load(k, LastFrame, o, _info!, _cacheCts.Token);
            if (_posterFrame == k && !_cache.Has(k)) { _posterView.Image = null; _posterView.Placeholder = "（読み込めませんでした）"; return; }
            RefreshPoster();
        }
        catch (Exception ex) { Log("ポスター: " + ex.Message); }
    }

    async void RefreshPoster()
    {
        // 動画のコマはプレビューと同じキャッシュから (絵は変換時と同じフィルタで作っている)
        if (_posterVideo.Checked && PrepareCache(out _))
        {
            _posterCts?.Cancel();
            if (_cache.TryGet(_posterFrame, out var pimg)) _posterView.Image = X68Bitmap.FromFrame(pimg);
            else { _posterDebounce.Stop(); _posterDebounce.Start(); }
            return;
        }
        _posterCts?.Cancel();
        var cts = _posterCts = new CancellationTokenSource();
        var o = CurrentOptions();
        if (!File.Exists(o.FfmpegPath)) { _posterView.Placeholder = "（ffmpeg.exe を指定してください）"; _posterView.Image = null; return; }
        string? src = o.Poster == PosterSource.ImageFile ? o.PosterImage : o.Input;
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) { _posterView.Image = null; return; }
        try
        {
            byte[] frame = o.Poster == PosterSource.ImageFile
                ? await Converter.MakePosterFromImage(o, cts.Token)
                : await Converter.MakePosterFromVideo(o, o.PosterTime, cts.Token);
            if (cts.IsCancellationRequested) return;
            _posterView.Image = X68Bitmap.FromFrame(frame);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) { _posterView.Image = null; _posterView.Placeholder = "（読み込めませんでした）"; Log(ex.Message); }
        }
    }

    // =====================================================================
    // 変換
    // =====================================================================

    async Task RunConvert()
    {
        var o = CurrentOptions();
        if (!File.Exists(o.FfmpegPath)) { Warn("ffmpeg.exe が見つかりません。ffmpeg のパスを指定してください。"); return; }
        if (!File.Exists(o.Input)) { Warn("入力動画が見つかりません。"); return; }
        if (string.IsNullOrWhiteSpace(o.Output)) { Warn("出力ファイルを指定してください。"); return; }
        if (o.Poster == PosterSource.ImageFile && !File.Exists(o.PosterImage)) { Warn("ポスター画像が見つかりません。"); return; }
        if (Vdt.V16Problem(o.Rate, o.TimeScale, o.ColorType) is { } v16bad) { Warn(v16bad); return; }
        if (!o.Rate.SivCompatible &&
            MessageBox.Show(this, o.Rate.IsPcm16
                    ? $"{o.Rate.Label} の v16 ファイルを作ります。\nmmvp.x（Mercury-Unit 搭載機）専用で、SiV.X では再生できません。\nこのまま作りますか？"
                    : $"{o.Rate.Code} 番 ({o.Rate.Label}) は SiV.X 0.97 では再生できません。\nこのまま作りますか？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (File.Exists(o.Output) &&
            MessageBox.Show(this, $"{o.Output}\nは既にあります。上書きしますか？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SaveSettings();
        _cts = new CancellationTokenSource();
        SetBusy(true);
        _log.Clear();
        Log($"{1 / o.FrameDuration:0.###} fps / {(o.Timing == TimingMode.Ntsc ? "NTSC (59.94Hz 基準)" : "60Hz 基準")} / ADPCM {o.Rate.Label}");
        var progress = new Progress<ConvertProgress>(p =>
        {
            if (p.Message != null) Log(p.Message);
            if (p.Frames >= 0)
            {
                if (p.ExpectedFrames > 0)
                {
                    _progress.Style = ProgressBarStyle.Continuous;
                    _progress.Maximum = Math.Max(p.ExpectedFrames, p.Frames);
                    _progress.Value = Math.Min(p.Frames, _progress.Maximum);
                }
                else _progress.Style = ProgressBarStyle.Marquee;
            }
        });
        try
        {
            int frames = await Task.Run(() => new Converter().Run(o, progress, _cts.Token));
            long mem = Converter.PlaybackMemory(frames, o.Rate, o.TimeScale);
            Log($"出力: {o.Output}（{new FileInfo(o.Output).Length:N0} バイト、X68000 展開メモリ約 {mem / 1024.0 / 1024.0:0.00} MB）");
            _preview.Open(o.Output);
        }
        catch (OperationCanceledException) { Log("キャンセルしました。"); }
        catch (Exception ex) { Log("エラー: " + ex.Message); Warn(ex.Message); }
        finally
        {
            SetBusy(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    void SetBusy(bool busy)
    {
        _run.Enabled = !busy;
        _cancel.Enabled = busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!busy) _progress.Value = 0;
        UseWaitCursor = busy;
    }

    void Log(string m)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(m)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {m}{Environment.NewLine}");
    }

    void Warn(string m) => MessageBox.Show(this, m, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
