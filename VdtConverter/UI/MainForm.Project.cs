using VdtConverter.Core;

namespace VdtConverter.UI;

/// <summary>プロジェクトの保存・読み込み (ファイル メニュー)。</summary>
public sealed partial class MainForm
{
    const string AppTitle = "VDT Converter for SiV（X68000 Silicon VTR）";

    string? _projectPath;
    bool _dirty;
    bool _applyingProject;          // 読み込み中の変更は「未保存」にしない
    VdtProject? _pendingFrames;     // 入力動画が見つからなかったときの、範囲・ポスター位置の待ち
    readonly ToolStripMenuItem _recentMenu = new("最近使ったプロジェクト(&R)");

    MenuStrip BuildMenu()
    {
        var file = new ToolStripMenuItem("ファイル(&F)");
        file.DropDownItems.Add(new ToolStripMenuItem("新しいプロジェクト(&N)", null, async (_, _) => await NewProject(), Keys.Control | Keys.N));
        file.DropDownItems.Add(new ToolStripMenuItem("プロジェクトを開く(&O)…", null, async (_, _) => await OpenProjectDialog(), Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("上書き保存(&S)", null, (_, _) => SaveProject(saveAs: false), Keys.Control | Keys.S));
        file.DropDownItems.Add(new ToolStripMenuItem("名前を付けて保存(&A)…", null, (_, _) => SaveProject(saveAs: true), Keys.Control | Keys.Shift | Keys.S));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("終了(&X)", null, (_, _) => Close()));
        file.DropDownOpening += (_, _) => FillRecent();
        var menu = new MenuStrip { Dock = DockStyle.Top };
        menu.Items.Add(file);
        return menu;
    }

    // =====================================================================
    // 状態 ⇔ プロジェクト
    // =====================================================================

    VdtProject ToProject() => new()
    {
        Input = _input.Text,
        Output = _output.Text,
        TimeScale = SelectedTimeScale,
        Ntsc = _ntsc.Checked,
        Deinterlace = _deint.Checked,
        ColorType = Math.Max(0, _colorType.SelectedIndex),
        Quality = (int)_quality.Value,
        SourceAspect = (int)SelectedAspect,
        Fit = (int)SelectedFit,
        RateCode = SelectedRate.Code,
        ShowExtendedRates = _extRates.Checked,
        Normalize = _normalize.Checked,
        VolumePercent = (int)_volume.Value,
        StartFrame = HasFrames ? (int)_startF.Value : _pendingFrames?.StartFrame ?? 0,
        EndFrame = HasFrames ? ((int)_endF.Value >= LastFrame ? -1 : (int)_endF.Value) : _pendingFrames?.EndFrame ?? -1,
        CursorFrame = HasFrames ? CurFrame : _pendingFrames?.CursorFrame ?? 0,
        SourceFps = _info?.Fps ?? _pendingFrames?.SourceFps ?? 0,
        PosterFromImage = _posterFile.Checked,
        PosterFrame = HasFrames ? _posterFrame : _pendingFrames?.PosterFrame ?? 0,
        PosterTouched = HasFrames ? _posterTouched : _pendingFrames?.PosterTouched ?? false,
        PosterImage = _posterPath.Text,
        Comment = _comment.Text,
    };

    async Task ApplyProject(VdtProject p)
    {
        _applyingProject = true;
        try
        {
            _input.Text = p.Input;
            _output.Text = p.Output;
            _ntsc.Checked = p.Ntsc; _exact.Checked = !p.Ntsc;
            FillFps(p.TimeScale);
            _deint.Checked = p.Deinterlace;
            _colorType.SelectedIndex = Math.Clamp(p.ColorType, 0, 3);
            _quality.Value = Math.Clamp(p.Quality, 1, 100);
            _quality.Enabled = _colorType.SelectedIndex > 0;
            _asp43.Checked = p.SourceAspect == (int)SourceAspect.Ratio4x3;
            _asp169.Checked = p.SourceAspect == (int)SourceAspect.Ratio16x9;
            _aspAuto.Checked = !_asp43.Checked && !_asp169.Checked;
            SelectedFit = (FitMode)Math.Clamp(p.Fit, 0, 2);
            _extRates.Checked = p.ShowExtendedRates;
            FillRates(AdpcmRate.FromCode(p.RateCode));
            _normalize.Checked = p.Normalize;
            _volume.Value = Math.Clamp(p.VolumePercent, 10, 400);
            _posterFile.Checked = p.PosterFromImage; _posterVideo.Checked = !p.PosterFromImage;
            _posterPath.Text = p.PosterImage;
            _comment.Text = p.Comment;
            UpdateControls();
            UpdateAspectUi();

            _pendingFrames = p;
            if (File.Exists(p.Input))
            {
                await InputChanged();   // 動画を調べ終わったところで、範囲・ポスター・位置を戻す (ApplyPendingFrames)
            }
            else
            {
                _info = null;
                SetupFrames();
                UpdateEstimate();
                RefreshImages();
                if (!string.IsNullOrWhiteSpace(p.Input))
                    Warn($"入力動画が見つかりません。\n{p.Input}\n\n「入力動画」で場所を指定し直すと、保存した範囲とポスターの位置を戻します。");
            }
            if (p.PosterFromImage && !string.IsNullOrWhiteSpace(p.PosterImage) && !File.Exists(p.PosterImage))
                Log("ポスター画像が見つかりません: " + p.PosterImage);
        }
        finally
        {
            _applyingProject = false;
        }
    }

    /// <summary>InputChanged の最後に呼ぶ。プロジェクトの範囲・ポスター・位置を、動画を調べた後で戻す。</summary>
    void ApplyPendingFrames()
    {
        var p = _pendingFrames;
        if (p == null || !HasFrames) return;
        _pendingFrames = null;
        bool was = _applyingProject;
        _applyingProject = true;
        try
        {
            if (p.SourceFps > 0 && _info != null && Math.Abs(p.SourceFps - _info.Fps) > 0.01)
                Log($"保存したときと動画のフレームレートが違います（{p.SourceFps:0.###} → {_info.Fps:0.###} fps）。範囲のコマ番号を確かめてください。");
            int end = p.EndFrame < 0 ? LastFrame : p.EndFrame;
            if (p.StartFrame > LastFrame || end > LastFrame)
                Log("保存した範囲が動画の長さを超えていたので、最後のコマまでにしました。");
            SetRange(p.StartFrame, end);
            _posterTouched = p.PosterTouched;
            SetPosterFrame(p.PosterTouched ? p.PosterFrame : (int)_startF.Value, touched: false);
            _posterTouched = p.PosterTouched;
            SetCursor(p.CursorFrame);
            UpdateRangeUi();
        }
        finally
        {
            _applyingProject = was;
        }
    }

    // =====================================================================
    // 未保存の印
    // =====================================================================

    void MarkDirty()
    {
        if (_applyingProject || _dirty) return;
        _dirty = true;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        string name = _projectPath != null ? Path.GetFileNameWithoutExtension(_projectPath) : "無題";
        Text = $"{name}{(_dirty ? " *" : "")} - {AppTitle}";
    }

    /// <summary>設定を変える部品すべてに「変わったら未保存」を付ける。</summary>
    void HookDirty(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case TextBox t when !ReferenceEquals(t, _log) && !ReferenceEquals(t, _ffmpeg): t.TextChanged += (_, _) => MarkDirty(); break;
                case NumericUpDown n: n.ValueChanged += (_, _) => MarkDirty(); break;
                case CheckBox cb: cb.CheckedChanged += (_, _) => MarkDirty(); break;
                case RadioButton rb: rb.CheckedChanged += (_, _) => MarkDirty(); break;
                case ComboBox co: co.SelectedIndexChanged += (_, _) => MarkDirty(); break;
                case FitChoiceButton f: f.CheckedChanged += (_, _) => MarkDirty(); break;
                case FrameScrubber s when ReferenceEquals(s, _posterScrub): s.ValueChanged += (_, _) => MarkDirty(); break;
            }
            HookDirty(c);
        }
    }

    /// <summary>未保存の変更があれば保存するか尋ねる。続けてよければ true。</summary>
    bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        string name = _projectPath != null ? Path.GetFileName(_projectPath) : "無題のプロジェクト";
        var r = MessageBox.Show(this, $"{name} の変更を保存しますか？", AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return SaveProject(saveAs: false);
        return true;
    }

    // =====================================================================
    // 新規・開く・保存
    // =====================================================================

    async Task NewProject()
    {
        if (!ConfirmDiscard()) return;
        var d = new VdtProject();
        // 前回の設定 (アプリの設定) を初期値にする。ファイルと範囲は空にする
        d.TimeScale = _cfg.TimeScale; d.Ntsc = _cfg.Ntsc; d.Deinterlace = _cfg.Deinterlace;
        d.ColorType = _cfg.ColorType; d.Quality = _cfg.Quality; d.SourceAspect = _cfg.SourceAspect; d.Fit = _cfg.Fit;
        d.RateCode = _cfg.RateCode; d.ShowExtendedRates = _cfg.ShowExtendedRates; d.Normalize = _cfg.Normalize;
        d.VolumePercent = _cfg.VolumePercent; d.Comment = _cfg.Comment;
        _projectPath = null;
        await ApplyProject(d);
        _pendingFrames = null;
        _preview.Close();
        _log.Clear();
        _dirty = false;
        UpdateTitle();
    }

    async Task OpenProjectDialog()
    {
        if (!ConfirmDiscard()) return;
        using var d = new OpenFileDialog
        {
            Title = "プロジェクトを開く",
            Filter = VdtProject.Filter + "|すべてのファイル|*.*",
            InitialDirectory = Directory.Exists(_cfg.LastProjectDir) ? _cfg.LastProjectDir : "",
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        await OpenProject(d.FileName, confirmed: true);
    }

    async Task OpenProject(string path, bool confirmed = false)
    {
        if (!confirmed && !ConfirmDiscard()) return;
        VdtProject p;
        try { p = VdtProject.Load(path); }
        catch (Exception ex) { Warn("プロジェクトを開けませんでした。\n" + ex.Message); RemoveRecent(path); return; }
        _projectPath = Path.GetFullPath(path);
        _cfg.LastProjectDir = Path.GetDirectoryName(_projectPath) ?? "";
        AddRecent(_projectPath);
        _log.Clear();
        Log("プロジェクトを開きました: " + _projectPath);
        await ApplyProject(p);
        _dirty = false;
        UpdateTitle();
    }

    /// <summary>保存できたら true。</summary>
    bool SaveProject(bool saveAs)
    {
        string? path = _projectPath;
        if (saveAs || path == null)
        {
            using var d = new SaveFileDialog
            {
                Title = "プロジェクトを保存",
                Filter = VdtProject.Filter,
                DefaultExt = VdtProject.Extension.TrimStart('.'),
                FileName = path != null ? Path.GetFileName(path)
                         : (File.Exists(_input.Text) ? Path.GetFileNameWithoutExtension(_input.Text) : "新しいプロジェクト") + VdtProject.Extension,
                InitialDirectory = path != null ? Path.GetDirectoryName(path) ?? ""
                                 : Directory.Exists(_cfg.LastProjectDir) ? _cfg.LastProjectDir
                                 : File.Exists(_input.Text) ? Path.GetDirectoryName(_input.Text) ?? "" : "",
            };
            if (d.ShowDialog(this) != DialogResult.OK) return false;
            path = d.FileName;
        }
        try
        {
            ToProject().Save(path);
        }
        catch (Exception ex)
        {
            Warn("保存できませんでした。\n" + ex.Message);
            return false;
        }
        _projectPath = Path.GetFullPath(path);
        _cfg.LastProjectDir = Path.GetDirectoryName(_projectPath) ?? "";
        AddRecent(_projectPath);
        _dirty = false;
        UpdateTitle();
        Log("プロジェクトを保存しました: " + _projectPath);
        return true;
    }

    // ---- 最近使ったプロジェクト ----

    void AddRecent(string path)
    {
        _cfg.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _cfg.RecentProjects.Insert(0, path);
        if (_cfg.RecentProjects.Count > 10) _cfg.RecentProjects.RemoveRange(10, _cfg.RecentProjects.Count - 10);
        _cfg.Save();
    }

    void RemoveRecent(string path)
    {
        _cfg.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _cfg.Save();
    }

    void FillRecent()
    {
        _recentMenu.DropDownItems.Clear();
        int i = 1;
        foreach (var p in _cfg.RecentProjects.ToList())
        {
            var path = p;
            var item = new ToolStripMenuItem($"&{i++} {Path.GetFileName(path)}", null, async (_, _) => await OpenProject(path))
            {
                ToolTipText = path,
                Enabled = File.Exists(path),
            };
            _recentMenu.DropDownItems.Add(item);
        }
        if (_recentMenu.DropDownItems.Count == 0)
            _recentMenu.DropDownItems.Add(new ToolStripMenuItem("（まだありません）") { Enabled = false });
    }
}
