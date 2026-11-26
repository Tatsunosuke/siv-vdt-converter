namespace VdtConverter.UI;

/// <summary>
/// コマ送り用のスクロールバー一式: [◀◀][◀][━━━ バー ━━━][▶][▶▶]
/// バーで大まかな位置、◀ ▶ で 1 コマ、◀◀ ▶▶ で 10 秒。バーの空いた所のクリックも 10 秒。
/// キーボード操作はフォームから Step() を呼んでもらう (最後に触ったスクラバーが対象)。
/// 自動サイズ計算に頼るとボタンが潰れることがあるので、高さ固定・Dock で並べる。
/// </summary>
sealed class FrameScrubber : Control
{
    const int H = 30;
    readonly HScrollBar _bar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 0 };
    readonly Button _back10 = new() { Text = "◀◀", Dock = DockStyle.Left, Width = 44 };
    readonly Button _prev = new() { Text = "◀", Dock = DockStyle.Left, Width = 34 };
    readonly Button _next = new() { Text = "▶", Dock = DockStyle.Right, Width = 34 };
    readonly Button _fwd10 = new() { Text = "▶▶", Dock = DockStyle.Right, Width = 44 };
    readonly Panel _barHolder = new() { Dock = DockStyle.Fill, Padding = new Padding(4, 3, 4, 3) };
    readonly ToolTip _tips = new();
    int _total = 1;
    double _fps = 30;
    bool _active;

    /// <summary>位置が変わった (操作でもプログラムからでも)。</summary>
    public event EventHandler? ValueChanged;
    /// <summary>ユーザーがこのスクラバーを操作した (キーボードの操作対象にする合図)。</summary>
    public event EventHandler? Activated;

    /// <summary>左右のボタンの幅 (帯の位置合わせ用)。</summary>
    public static int SideWidth => 44 + 34 + 4 + 2;

    public FrameScrubber()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        Height = H;
        MinimumSize = new Size(200, H);
        Padding = new Padding(2);
        _barHolder.Controls.Add(_bar);
        // Dock は後から追加したものが先に場所を取る: Fill を最初に、端のボタンを後に
        Controls.Add(_barHolder);
        Controls.Add(_prev);
        Controls.Add(_back10);
        Controls.Add(_next);
        Controls.Add(_fwd10);

        _tips.SetToolTip(_back10, "10 秒戻る（Shift+←）");
        _tips.SetToolTip(_prev, "1 コマ戻る（←）");
        _tips.SetToolTip(_next, "1 コマ進む（→）");
        _tips.SetToolTip(_fwd10, "10 秒進む（Shift+→）");
        _tips.SetToolTip(_bar, "大まかな位置。端の矢印は 1 コマ、バーの空いた所のクリックは 10 秒（Ctrl+Shift+← → で 1 分）");

        _bar.ValueChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _bar.Scroll += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        _bar.Enter += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        foreach (var b in new[] { _back10, _prev, _next, _fwd10 }) b.TabStop = false;
        _back10.Click += (_, _) => { Activated?.Invoke(this, EventArgs.Empty); Step(-FramesFor(10)); };
        _prev.Click += (_, _) => { Activated?.Invoke(this, EventArgs.Empty); Step(-1); };
        _next.Click += (_, _) => { Activated?.Invoke(this, EventArgs.Empty); Step(+1); };
        _fwd10.Click += (_, _) => { Activated?.Invoke(this, EventArgs.Empty); Step(+FramesFor(10)); };
        Enabled = false;
    }

    public override Size GetPreferredSize(Size proposedSize) => new(Math.Max(200, proposedSize.Width), H);

    /// <summary>総コマ数と元動画の fps を設定し、先頭に戻す。</summary>
    public void Setup(int total, double fps)
    {
        _total = Math.Max(1, total);
        _fps = fps > 0 ? fps : 30;
        _bar.Minimum = 0;
        _bar.SmallChange = 1;
        _bar.LargeChange = Math.Max(1, Math.Min(FramesFor(10), _total));
        _bar.Maximum = (_total - 1) + _bar.LargeChange - 1;   // HScrollBar は Maximum - LargeChange + 1 までしか動かない
        _bar.Value = 0;
    }

    public int Value
    {
        get => _bar.Value;
        set { int v = Math.Clamp(value, 0, _total - 1); if (_bar.Value != v) _bar.Value = v; }
    }

    public int Last => _total - 1;

    public int FramesFor(double sec) => Math.Max(1, (int)Math.Round(sec * _fps));

    public void Step(int frames) => Value = Value + frames;

    /// <summary>キャレットをバーに置く (この部品そのものはフォーカスを持てないので中のバーへ)。</summary>
    public new bool Focus() => _bar.Focus();

    /// <summary>キーボードの操作対象かどうか (枠の色で見せる)。</summary>
    public bool Active
    {
        get => _active;
        set { if (_active != value) { _active = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_active && Enabled)
        {
            using var p = new Pen(Color.FromArgb(0, 120, 215), 2);
            e.Graphics.DrawRectangle(p, 1, 1, Width - 2, Height - 2);
        }
    }

    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    // キャレット (フォーカス) がこの中にある間は青い枠で示す。キーボード操作の対象はこれで決まる
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Active = true; }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Active = false; }
}
