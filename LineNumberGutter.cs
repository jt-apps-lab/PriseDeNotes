using System.Runtime.InteropServices;

namespace PriseDeNotes;

/// <summary>
/// Numérote les lignes du texte (hors retours automatiques) aux positions fournies
/// par RichEdit. Aucune hauteur de ligne n'est déduite de la police de base.
/// </summary>
internal sealed class LineNumberGutter : Panel
{
    private const uint GetLineCount = 0x00BA, LineIndex = 0x00BB, FirstLine = 0x00CE, PosFromChar = 0x00D6;
    private readonly record struct VisibleLine(int Number, int Top, int Baseline);
    private readonly record struct Viewport(int First, int Top, int Count, Size Size);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private readonly RichTextBox _body;
    private readonly ScrollHook _hook;
    private Font _font = new("Segoe UI", 9f);
    private string _family = "Segoe UI";
    private float _points = 9f, _zoom = 1f;
    private int _digitWidth;
    private int _numberAscent;
    private RichEditLineMetrics? _lineMetrics;
    private bool _attached, _refreshing, _fontDirty = true;
    private VisibleLine[] _visibleLines = Array.Empty<VisibleLine>();
    private Viewport? _viewport;
    // Nombre de paragraphes (lignes numérotées), demandé à RichEdit une fois par modification du texte.
    private int _paragraphCount = -1;

    public LineNumberGutter(RichTextBox body)
    {
        _body = body;
        _hook = new ScrollHook(RefreshLayout);
        TabStop = false;
        BackColor = body.BackColor;
        ForeColor = Theme.TextDim;
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        _body.HandleCreated += BodyHandleCreated;
        _body.HandleDestroyed += BodyHandleDestroyed;
        _body.VScroll += BodyViewportChanged;
        _body.HScroll += BodyViewportChanged;
        _body.TextChanged += BodyTextChanged;
        if (_body.IsHandleCreated) BodyHandleCreated(_body, EventArgs.Empty);
    }

    private void BodyHandleCreated(object? sender, EventArgs e)
    {
        _hook.AssignHandle(_body.Handle);
        RefreshNumbers();
    }

    private void BodyHandleDestroyed(object? sender, EventArgs e)
    {
        _lineMetrics?.Dispose();
        _lineMetrics = null;
        _hook.ReleaseHandle();
        _visibleLines = Array.Empty<VisibleLine>();
        _viewport = null;
        _paragraphCount = -1;
    }

    private void BodyViewportChanged(object? sender, EventArgs e) => RefreshLayout(layoutChanged: false);

    private void BodyTextChanged(object? sender, EventArgs e)
    {
        _paragraphCount = -1;
        RefreshNumbers();
    }

    /// <summary>
    /// Nombre de lignes numérotées, lu dans le modèle natif de RichEdit (paragraphe de la fin du texte) :
    /// le texte n'est jamais recopié côté .NET. Repli sur le nombre de lignes affichées.
    /// </summary>
    private int ParagraphCount()
    {
        if (_paragraphCount > 0) return _paragraphCount;
        _lineMetrics ??= RichEditLineMetrics.Create(_body.Handle);
        int count = 0;
        if (_lineMetrics is null || !_lineMetrics.TryGetParagraphNumber(Math.Max(0, _body.TextLength), out count))
            count = DisplayLineCount();
        return _paragraphCount = Math.Max(1, count);
    }

    /// <summary>La police fournie appartient à l'appelant ; seules ses caractéristiques sont conservées.</summary>
    public void UseBaseFont(Font baseFont)
    {
        _family = baseFont.FontFamily.Name;
        _points = Math.Max(7f, baseFont.SizeInPoints * 0.82f);
        _fontDirty = true;
        RefreshNumbers();
    }

    private bool UpdateNumberFont()
    {
        float zoom = _body.IsHandleCreated ? _body.ZoomFactor : 1f;
        if (!_fontDirty && zoom == _zoom) return false;
        var candidate = new Font(_family, _points * zoom);
        var old = _font;
        _font = candidate;
        _zoom = zoom;
        _fontDirty = false;
        old.Dispose();
        // Certaines polices ont des chiffres de largeurs différentes.
        _digitWidth = Enumerable.Range(0, 10).Max(n => TextRenderer.MeasureText(n.ToString(), _font,
            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
        _numberAscent = MeasureAscent();
        return true;
    }

    private int MeasureAscent()
    {
        using var graphics = _body.CreateGraphics();
        nint dc = graphics.GetHdc(), font = _font.ToHfont();
        nint old = SelectObject(dc, font);
        try
        {
            if (!GetTextMetrics(dc, out var metrics)) throw new System.ComponentModel.Win32Exception();
            return metrics.Ascent;
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(font);
            graphics.ReleaseHdc(dc);
        }
    }

    /// <summary>Recalcule la largeur et invalide seulement si la géométrie visible a changé.</summary>
    public void RefreshNumbers() => RefreshLayout(layoutChanged: true);

    private void RefreshLayout(bool layoutChanged)
    {
        if (_refreshing || IsDisposed || !Visible || !_body.IsHandleCreated) return;
        _refreshing = true;
        try
        {
            bool fontChanged = UpdateNumberFont();
            var viewport = ReadViewport();
            if (!layoutChanged && !fontChanged && viewport == _viewport) return;
            bool widthChanged = false;
            int digits = Math.Max(2, ParagraphCount().ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
            int width = S(20) + digits * _digitWidth;
            if (width != Width)
            {
                Width = width;
                widthChanged = true;
            }

            var lines = CaptureVisibleLines();
            _viewport = ReadViewport();
            if (!fontChanged && !widthChanged && lines.SequenceEqual(_visibleLines)) return;
            _visibleLines = lines;
            Invalidate();
        }
        finally { _refreshing = false; }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        RefreshNumbers();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _fontDirty = true;
        RefreshNumbers();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (!_body.IsHandleCreated) return;
        int numberHeight = NumberHeight();
        foreach (var line in _visibleLines)
        {
            // La ligne de base du numéro coïncide avec celle du texte, même si sa police est plus petite.
            TextRenderer.DrawText(e.Graphics, line.Number.ToString(), _font,
                new Rectangle(0, line.Top, Math.Max(0, Width - S(8)), numberHeight), ForeColor,
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
    }

    private VisibleLine[] CaptureVisibleLines()
    {
        var lines = new List<VisibleLine>();
        _lineMetrics ??= RichEditLineMetrics.Create(_body.Handle);
        if (_lineMetrics is null) return Array.Empty<VisibleLine>();
        int count = DisplayLineCount(), numberHeight = NumberHeight();
        int first = Math.Clamp((int)SendMessage(_body.Handle, FirstLine, 0, 0), 0, count - 1);
        int firstLine = Math.Max(0, first - 1);
        // Numéro du paragraphe en cours : demandé une fois pour la première ligne visible,
        // puis incrémenté à chaque début de paragraphe. Coût proportionnel aux seules lignes affichées.
        int paragraph = 0;
        for (int line = firstLine; line < count; line++)
        {
            int top = LineTop(line);
            if (top >= ClientSize.Height) break;
            int start = (int)SendMessage(_body.Handle, LineIndex, line, 0);
            if (start < 0 || !_lineMetrics.TryStartsParagraph(start, out bool starts)) break;
            if (line == firstLine)
            {
                if (!_lineMetrics.TryGetParagraphNumber(start, out paragraph)) break;
            }
            else if (starts) paragraph++;
            if (!starts) continue; // Suite d'une ligne coupée automatiquement.
            if (!_lineMetrics.TryGetBaseline(start, out int baseline)) continue;
            int numberTop = baseline - _numberAscent;
            if (numberTop < ClientSize.Height && numberTop + numberHeight > 0)
                lines.Add(new VisibleLine(paragraph, numberTop, baseline));
        }
        return lines.ToArray();
    }

    private int LineTop(int line)
    {
        int start = (int)SendMessage(_body.Handle, LineIndex, line, 0);
        // La variante POINTL conserve les coordonnées sur 32 bits et évite le résultat compacté.
        var point = new NativePoint();
        SendPosition(_body.Handle, PosFromChar, ref point, start);
        return point.Y;
    }

    private Viewport ReadViewport()
    {
        int count = DisplayLineCount();
        int first = Math.Clamp((int)SendMessage(_body.Handle, FirstLine, 0, 0), 0, count - 1);
        return new Viewport(first, LineTop(first), count, _body.ClientSize);
    }

    private int DisplayLineCount() => Math.Max(1, (int)SendMessage(_body.Handle, GetLineCount, 0, 0));
    private int NumberHeight() => TextRenderer.MeasureText("0", _font, new Size(int.MaxValue, int.MaxValue),
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_attached)
            {
                _body.HandleCreated -= BodyHandleCreated;
                _body.HandleDestroyed -= BodyHandleDestroyed;
                _body.VScroll -= BodyViewportChanged;
                _body.HScroll -= BodyViewportChanged;
                _body.TextChanged -= BodyTextChanged;
            }
            _hook.ReleaseHandle();
            _lineMetrics?.Dispose();
            _font.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint parameter, nint value);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendPosition(nint window, uint message, ref NativePoint point, nint index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TextMetrics
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaximumWidth,
            Weight, Overhang, DigitizedAspectX, DigitizedAspectY;
        public char FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, PitchAndFamily, CharacterSet;
    }
    [DllImport("gdi32.dll", EntryPoint = "GetTextMetricsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTextMetrics(nint dc, out TextMetrics metrics);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint value);

    private sealed class ScrollHook(Action<bool> refresh) : NativeWindow
    {
        private const int Paint = 0x000F, Size = 0x0005, SetText = 0x000C, SetFont = 0x0030,
            KeyDown = 0x0100, Char = 0x0102, Timer = 0x0113, HScroll = 0x0114, VScroll = 0x0115,
            Wheel = 0x020A, LeftUp = 0x0202, LeftDoubleClick = 0x0203,
            LineScroll = 0x00B6, ScrollCaret = 0x00B7, SetCharFormat = 0x0444,
            SetParagraphFormat = 0x0447, StreamIn = 0x0449, SetZoom = 0x04E1;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // RichEdit peut défiler pendant sa peinture, le clavier ou ses minuteries.
            // Les messages de formatage couvrent aussi les changements sans saisie de texte.
            if (m.Msg is Paint or Size or SetText or SetFont or KeyDown or Char or Timer
                or HScroll or VScroll or Wheel or LeftUp or LeftDoubleClick or LineScroll
                or ScrollCaret or SetCharFormat or SetParagraphFormat or StreamIn or SetZoom)
                refresh(m.Msg is Size or SetText or SetFont or SetCharFormat or SetParagraphFormat or StreamIn or SetZoom);
        }
    }
}
