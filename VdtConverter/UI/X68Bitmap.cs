using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VdtConverter.Core;

namespace VdtConverter.UI;

static class X68Bitmap
{
    /// <summary>VDT 画像 (30720 バイト) → 128x120 Bitmap。</summary>
    public static Bitmap FromFrame(byte[] frame)
    {
        var bmp = new Bitmap(X68Color.Width, X68Color.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var buf = new byte[X68Color.Width * X68Color.Height * 4];
        X68Color.FrameToBgra32(frame, buf);
        for (int y = 0; y < X68Color.Height; y++)
            Marshal.Copy(buf, y * X68Color.Width * 4, data.Scan0 + y * data.Stride, X68Color.Width * 4);
        bmp.UnlockBits(data);
        return bmp;
    }
}

/// <summary>X68000 の 4:3 画面に見えるように (画素比 1.25:1) ドットのまま拡大表示する。</summary>
sealed class X68View : Control
{
    Bitmap? _image;
    string _placeholder = "（画像なし）";

    public X68View()
    {
        DoubleBuffered = true;
        BackColor = Color.Black;
        ForeColor = Color.Gray;
        Size = new Size(320, 240);
    }

    public Bitmap? Image
    {
        get => _image;
        set { var old = _image; _image = value; if (!ReferenceEquals(old, value)) old?.Dispose(); Invalidate(); }
    }

    public string Placeholder { get => _placeholder; set { _placeholder = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        // 4:3 の枠に収める
        float w = ClientSize.Width, h = ClientSize.Height;
        float dw = Math.Min(w, h * 4 / 3f), dh = dw * 3 / 4f;
        var dst = new RectangleF((w - dw) / 2, (h - dh) / 2, dw, dh);
        if (_image == null)
        {
            TextRenderer.DrawText(g, _placeholder, Font, Rectangle.Round(dst), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_image, dst);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image?.Dispose();
        base.Dispose(disposing);
    }
}
