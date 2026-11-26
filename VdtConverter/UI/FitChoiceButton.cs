using System.Drawing.Drawing2D;
using VdtConverter.Core;

namespace VdtConverter.UI;

/// <summary>
/// 「4:3 の画面に、横長の絵をどう収めるか」を絵で見せて選ばせるボタン。
/// 同じ親の中の FitChoiceButton どうしでラジオボタンのように動く。
/// </summary>
sealed class FitChoiceButton : Control
{
    bool _checked;
    bool _hover;
    double _sourceAspect = AspectRules.SixteenNine;

    public FitMode Mode { get; }
    public string Caption { get; }
    public string SubCaption { get; }

    public event EventHandler? CheckedChanged;

    public FitChoiceButton(FitMode mode, string caption, string subCaption)
    {
        Mode = mode;
        Caption = caption;
        SubCaption = subCaption;
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick | ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Size = new Size(156, 112);
        Cursor = Cursors.Hand;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (value && Parent != null)
                foreach (var b in Parent.Controls.OfType<FitChoiceButton>())
                    if (!ReferenceEquals(b, this)) b.Checked = false;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>図に描く入力の縦横比 (幅÷高さ)。</summary>
    public double SourceAspect
    {
        get => _sourceAspect;
        set { _sourceAspect = Math.Clamp(value, 0.4, 3.0); Invalidate(); }
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); if (Enabled) { Focus(); Checked = true; } }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) { Checked = true; e.Handled = true; }
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    static readonly Color Accent = Color.FromArgb(0, 120, 215);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = Parent?.BackColor ?? SystemColors.Control;
        g.Clear(bg);

        var card = new Rectangle(1, 1, Width - 3, Height - 3);
        var fill = !Enabled ? SystemColors.Control
                 : _checked ? Color.FromArgb(229, 241, 251)
                 : _hover ? Color.FromArgb(243, 247, 252) : SystemColors.Window;
        using (var path = Round(card, 6))
        {
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            using var pen = new Pen(_checked && Enabled ? Accent : SystemColors.ControlDark, _checked ? 2.5f : 1f);
            g.DrawPath(pen, path);
        }

        // ---- 図: 4:3 の画面 ----
        int dh = 48, dw = 64;
        var screen = new RectangleF((Width - dw) / 2f, 8, dw, dh);
        DrawDiagram(g, screen);

        // ---- 文字 ----
        var fg = Enabled ? SystemColors.ControlText : SystemColors.GrayText;
        var capRect = new Rectangle(4, (int)screen.Bottom + 6, Width - 8, 20);
        using (var bold = new Font(Font, FontStyle.Bold))
            TextRenderer.DrawText(g, Caption, bold, capRect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        var subRect = new Rectangle(4, capRect.Bottom, Width - 8, Height - capRect.Bottom - 4);
        TextRenderer.DrawText(g, SubCaption, Font, subRect, SystemColors.GrayText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);

        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(card, -4, -4));
    }

    void DrawDiagram(Graphics g, RectangleF screen)
    {
        double screenAspect = AspectRules.FourThree;
        double a = _sourceAspect;
        RectangleF pic = Mode switch
        {
            // 高さを画面に合わせる → 横にはみ出す (縦長の素材なら上下にはみ出す)
            FitMode.Crop => a >= screenAspect
                ? Centered(screen, screen.Height * (float)a, screen.Height)
                : Centered(screen, screen.Width, screen.Width / (float)a),
            // 幅を画面に合わせる → 上下に黒帯 (縦長なら左右)
            FitMode.Letterbox => a >= screenAspect
                ? Centered(screen, screen.Width, screen.Width / (float)a)
                : Centered(screen, screen.Height * (float)a, screen.Height),
            _ => screen,
        };

        // 画面 (黒)
        using (var black = new SolidBrush(Enabled ? Color.FromArgb(20, 20, 24) : Color.FromArgb(120, 120, 120)))
            g.FillRectangle(black, screen);

        // はみ出して捨てられる部分 (トリムのとき)
        if (Mode == FitMode.Crop)
        {
            var st = g.Save();
            using var outside = new Region(pic);
            outside.Exclude(screen);
            g.SetClip(outside, CombineMode.Replace);
            DrawPicture(g, pic, ghost: true);
            using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(150, Color.Firebrick), Color.Transparent);
            g.FillRectangle(hatch, pic);
            g.Restore(st);
        }

        // 画面に映る部分
        var s2 = g.Save();
        g.SetClip(screen);
        // 引き伸ばしは「本来の縦横比の絵」を画面いっぱいに押し込む → 丸が楕円になる
        DrawPicture(g, pic, ghost: false, squash: Mode == FitMode.Stretch ? (float)(screenAspect / a) : 1f);
        g.Restore(s2);

        using var frame = new Pen(Enabled ? Color.White : Color.Gainsboro, 2f);
        g.DrawRectangle(frame, screen.X, screen.Y, screen.Width, screen.Height);
        using var outer = new Pen(Color.FromArgb(90, 90, 90), 1f);
        g.DrawRectangle(outer, screen.X - 2, screen.Y - 2, screen.Width + 4, screen.Height + 4);
    }

    /// <summary>空・山・太陽の簡単な絵。squash は太陽の横方向の倍率 (引き伸ばしで歪む様子)。</summary>
    void DrawPicture(Graphics g, RectangleF r, bool ghost, float squash = 1f)
    {
        int alpha = ghost ? 90 : 255;
        if (!Enabled) alpha = Math.Min(alpha, 110);
        using (var sky = new LinearGradientBrush(r, Color.FromArgb(alpha, 120, 190, 245), Color.FromArgb(alpha, 210, 235, 255), 90f))
            g.FillRectangle(sky, r);
        // 山
        var m1 = new[] { new PointF(r.Left, r.Bottom), new PointF(r.Left + r.Width * 0.30f, r.Top + r.Height * 0.40f), new PointF(r.Left + r.Width * 0.58f, r.Bottom) };
        var m2 = new[] { new PointF(r.Left + r.Width * 0.38f, r.Bottom), new PointF(r.Left + r.Width * 0.72f, r.Top + r.Height * 0.30f), new PointF(r.Right, r.Bottom) };
        using (var dark = new SolidBrush(Color.FromArgb(alpha, 60, 130, 80))) g.FillPolygon(dark, m1);
        using (var lite = new SolidBrush(Color.FromArgb(alpha, 90, 160, 95))) g.FillPolygon(lite, m2);
        // 太陽 (縦横比の歪みが分かるように丸)
        float d = r.Height * 0.30f;
        float sw = d * squash;
        var sun = new RectangleF(r.Left + r.Width * 0.82f - sw / 2, r.Top + r.Height * 0.12f, sw, d);
        using (var sb = new SolidBrush(Color.FromArgb(alpha, 255, 200, 40))) g.FillEllipse(sb, sun);
        // 人 (左端) … トリムで切れることが分かるように
        float ph = r.Height * 0.42f, pw = ph * 0.35f * squash;
        var body = new RectangleF(r.Left + r.Width * 0.07f, r.Bottom - ph, pw, ph * 0.7f);
        var head = new RectangleF(body.Left + pw * 0.1f, body.Top - pw * 0.85f, pw * 0.8f, pw * 0.8f);
        using (var pb = new SolidBrush(Color.FromArgb(alpha, 230, 90, 120))) { g.FillEllipse(pb, head); g.FillRectangle(pb, body); }
    }

    static RectangleF Centered(RectangleF box, float w, float h)
        => new(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);

    static GraphicsPath Round(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
