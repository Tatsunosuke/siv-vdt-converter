namespace VdtConverter.UI;

/// <summary>
/// 動画全体の中で「取り込む範囲」「ポスターの位置」「いま見ているコマ」を帯で見せる。
/// クリック / ドラッグで見ているコマを動かせる。
/// </summary>
sealed class RangeBar : Control
{
    int _total = 1, _start, _end, _poster = -1, _cursor;
    bool _drag;

    public event EventHandler<int>? Seek;

    public RangeBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 22;
        Cursor = Cursors.Hand;
    }

    public void SetState(int total, int start, int end, int poster, int cursor)
    {
        _total = Math.Max(1, total);
        _start = start; _end = end; _poster = poster; _cursor = cursor;
        Invalidate();
    }

    float X(int frame) => 1 + (Width - 3) * (frame / (float)Math.Max(1, _total - 1));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        int top = 5, h = Height - 10;
        var track = new RectangleF(1, top, Width - 3, h);
        using (var b = new SolidBrush(Enabled ? Color.FromArgb(215, 215, 220) : Color.Gainsboro)) g.FillRectangle(b, track);
        if (!Enabled) return;

        // 取り込む範囲
        float x0 = X(_start), x1 = X(_end);
        using (var b = new SolidBrush(Color.FromArgb(0, 120, 215))) g.FillRectangle(b, x0, top, Math.Max(2, x1 - x0), h);
        using (var p = new Pen(Color.FromArgb(0, 70, 140), 2))
        {
            g.DrawLine(p, x0, top - 4, x0, top + h + 4);
            g.DrawLine(p, x1, top - 4, x1, top + h + 4);
        }
        // ポスター
        if (_poster >= 0)
        {
            float xp = X(_poster);
            using var b = new SolidBrush(Color.DarkOrange);
            g.FillPolygon(b, new[] { new PointF(xp - 5, 0), new PointF(xp + 5, 0), new PointF(xp, 7) });
        }
        // いま見ているコマ
        float xc = X(_cursor);
        using (var p = new Pen(Color.Firebrick, 2)) g.DrawLine(p, xc, 0, xc, Height);
    }

    void SeekTo(int px)
    {
        int f = (int)Math.Round((px - 1) / (float)Math.Max(1, Width - 3) * (_total - 1));
        Seek?.Invoke(this, Math.Clamp(f, 0, _total - 1));
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (Enabled && e.Button == MouseButtons.Left) { _drag = true; SeekTo(e.X); } }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag) SeekTo(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = false; }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
}
