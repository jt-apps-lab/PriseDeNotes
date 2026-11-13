namespace PriseDeNotes;

/// <summary>
/// Infobulle sombre dessinée à la main : l'infobulle Windows ignore les couleurs
/// personnalisées quand les styles visuels sont actifs, elle reste claire dans un thème sombre.
/// </summary>
public sealed class DarkToolTip : Form
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly System.Windows.Forms.Timer _autoPop = new() { Interval = 9000 };
    private string _text = "";

    public DarkToolTip()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Theme.Surface2;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9f);
        int s(int px) => (int)Math.Round(px * DeviceDpi / 96f);
        Padding = new Padding(s(11), s(9), s(11), s(9));
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        _autoPop.Tick += (_, _) => HideTip();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;   // ni activation, ni alt-tab
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>Affiche <paramref name="text"/> dont le coin haut-gauche est en coordonnées écran.</summary>
    public void ShowFor(string text, Point screenTopLeft, int width = 0)
    {
        if (string.IsNullOrEmpty(text)) { HideTip(); return; }
        if (width <= 0) width = (int)Math.Round(360 * DeviceDpi / 96f);

        _text = text;
        var inner = new Size(width - Padding.Horizontal, 600);
        var measured = TextRenderer.MeasureText(text, Font, inner,
            TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        ClientSize = new Size(width, measured.Height + Padding.Vertical + 4);

        // rappel : la bulle ne doit jamais sortir de l'écran
        var screen = Screen.FromPoint(screenTopLeft).Bounds;
        int left = Math.Min(screenTopLeft.X, screen.Right - Width - 4);
        int top = Math.Min(screenTopLeft.Y, screen.Bottom - Height - 4);
        Location = new Point(Math.Max(left, screen.Left + 4), Math.Max(top, screen.Top + 4));

        Invalidate();
        if (!Visible) Show();
        else BringToFront();

        _autoPop.Stop();
        _autoPop.Start();
    }

    public void HideTip()
    {
        _autoPop.Stop();
        if (Visible) Hide();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _autoPop.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Surface2);
        var rect = new Rectangle(Padding.Left, Padding.Top,
            Width - Padding.Horizontal, Height - Padding.Vertical);
        TextRenderer.DrawText(g, _text, Font, rect, Theme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);
        using var pen = new Pen(Theme.Border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        using var accent = new SolidBrush(Theme.Accent);
        g.FillRectangle(accent, 0, 0, 3, Height);
    }
}
