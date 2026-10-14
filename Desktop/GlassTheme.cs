using System.Drawing.Drawing2D;
namespace ERateLookup.Desktop;

// Painted glass-style treatment: layered gradients and highlights, no compositor dependency.
public sealed class ShadowLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var shadow = new SolidBrush(Color.FromArgb(55, 22, 53, 48));
        using var foreground = new SolidBrush(ForeColor);
        var bounds = new RectangleF(Padding.Left, Padding.Top, ClientSize.Width - Padding.Horizontal, ClientSize.Height - Padding.Vertical);
        var offset = bounds; offset.Offset(1.5f, 2.5f);
        using var format = new StringFormat() { Trimming = StringTrimming.EllipsisCharacter };
        e.Graphics.DrawString(Text, Font, shadow, offset, format);
        e.Graphics.DrawString(Text, Font, foreground, bounds, format);
    }
}
public sealed class GlassLabel : Label
{
    [System.ComponentModel.DefaultValue(false)]
    public bool Dark { get; set; }
    [System.ComponentModel.DefaultValue(false)]
    public bool Classic { get; set; }
    public GlassLabel() { DoubleBuffered = true; BackColor = Color.Transparent; ForeColor = Color.FromArgb(19, 53, 47); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = Rounded(rect, 14);
        using var fill = new LinearGradientBrush(rect, Dark ? Color.FromArgb(44, 61, 70) : Color.FromArgb(247, 253, 252), Dark ? Color.FromArgb(25, 41, 52) : Classic ? Color.FromArgb(246, 246, 246) : Color.FromArgb(217, 237, 232), LinearGradientMode.Vertical);
        e.Graphics.FillPath(fill, path);
        using var border = new Pen(Dark ? Color.FromArgb(80, 120, 145) : Color.FromArgb(160, 255, 255, 255), 2); e.Graphics.DrawPath(border, path);
        using var edge = new Pen(Color.FromArgb(126, 177, 168), 1); e.Graphics.DrawLine(edge, 16, Height - 2, Width - 16, Height - 2);
    }
    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath(); var size = radius * 2; p.AddArc(r.X, r.Y, size, size, 180, 90); p.AddArc(r.Right - size, r.Y, size, size, 270, 90); p.AddArc(r.Right - size, r.Bottom - size, size, size, 0, 90); p.AddArc(r.X, r.Bottom - size, size, size, 90, 90); p.CloseFigure(); return p;
    }
}
