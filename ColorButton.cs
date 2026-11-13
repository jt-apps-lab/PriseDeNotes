using System.Drawing.Drawing2D;

namespace PriseDeNotes;

public sealed class ColorButton : Button
{
    private Color _selectedColor;

    public bool IsTextColor { get; init; }

    public Color SelectedColor
    {
        get => _selectedColor;
        set { _selectedColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        float scale = DeviceDpi / 96f * 0.82f;
        float left = (ClientSize.Width - 32 * scale) / 2f;
        float top = (ClientSize.Height - 32 * scale) / 2f;
        PointF P(float x, float y) => new(left + x * scale, top + y * scale);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Enabled ? Theme.Text : Theme.TextDim, 1.6f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        if (IsTextColor)
        {
            g.DrawLines(pen, new[] { P(9, 21), P(16, 5), P(23, 21) });
            g.DrawLine(pen, P(12, 15), P(20, 15));
        }
        else
        {
            g.DrawPolygon(pen, new[] { P(13, 5), P(23, 15), P(14, 23), P(5, 14) });
            g.DrawLine(pen, P(6, 14), P(22, 14));
            g.DrawLines(pen, new[] { P(9, 10), P(9, 4), P(13, 4) });
            using var drop = new GraphicsPath();
            drop.AddBezier(P(25, 17), P(24, 20), P(21, 23), P(25, 23));
            drop.AddBezier(P(25, 23), P(29, 23), P(26, 20), P(25, 17));
            using var ink = new SolidBrush(pen.Color);
            g.FillPath(ink, drop);
        }

        g.SmoothingMode = SmoothingMode.None;
        var indicator = new RectangleF(left + 6 * scale, top + 27 * scale, 20 * scale, 3 * scale);
        using var color = new SolidBrush(SelectedColor);
        using var border = new Pen(Theme.TextDim, 1);
        g.FillRectangle(color, indicator);
        g.DrawRectangle(border, indicator.X, indicator.Y, indicator.Width, indicator.Height);
        g.SmoothingMode = old;
    }
}
