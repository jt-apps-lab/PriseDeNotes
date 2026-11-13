namespace PriseDeNotes;

/// <summary>
/// Liste des titres, dessinée à la main pour un rendu sombre (pas de clignotement blanc).
/// Toutes les hauteurs sont déduites des métriques réelles de la police : la liste reste lisible
/// quelle que soit la mise à l'échelle Windows (100 %, 150 %, 200 %).
/// </summary>
public sealed class TitleListBox : ListBox
{
    private Font _subFont = null!;
    private int _pad, _titleH, _subH, _actionSize;
    private int _pressedIndex = -1;
    private NoteListAction _pressedAction;
    private Point _pressPoint;
    private Note? _draggedNote;
    private int _insertionIndex = -1;
    private readonly System.Windows.Forms.Timer _dragScroll = new() { Interval = 120 };

    public event Action<Note>? DeleteRequested;
    public event Action<Note>? RenameRequested;
    public event Action<Note, int>? MoveRequested;
    public event EventHandler? ViewportChanging;

    public TitleListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        IntegralHeight = false;
        AllowDrop = true;
        _dragScroll.Tick += (_, _) => UpdateDropTarget(PointToClient(MousePosition), scroll: true);
        RecomputeLayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        RecomputeLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RecomputeLayout();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        RecomputeLayout();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _subFont?.Dispose();
            _dragScroll.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Hauteur de ligne = hauteur réelle des deux lignes de texte + respiration.</summary>
    private void RecomputeLayout()
    {
        var probe = new Size(int.MaxValue, int.MaxValue);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        _titleH = TextRenderer.MeasureText("Tgpq", Font, probe, flags).Height;
        if (_titleH <= 0) _titleH = Font.Height;

        var old = _subFont;
        _subFont = new Font(Font.FontFamily, Font.SizeInPoints * 0.92f, GraphicsUnit.Point);
        old?.Dispose();
        _subH = TextRenderer.MeasureText("Tgpq", _subFont, probe, flags).Height;
        if (_subH <= 0) _subH = _titleH;

        _pad = Math.Max(4, _titleH / 3);
        _actionSize = Math.Max(_titleH, (int)Math.Round(24 * DeviceDpi / 96f));
        ItemHeight = _pad * 2 + _titleH + _subH + 2;
        Invalidate();
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var note = (Note)Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        var r = e.Bounds;
        int left = r.Left + _pad * 2 + _actionSize;
        int actionZone = _actionSize * 2 + _pad * 3;

        var g = e.Graphics;
        var back = ColorExt.ToColor(note.BackColor, Theme.Window);
        var fore = ColorExt.ToColor(note.ForeColor, Theme.Text);
        using (var background = new SolidBrush(back))
            g.FillRectangle(background, r);
        DrawGrip(g, new Rectangle(r.Left + _pad, r.Top + (r.Height - _actionSize) / 2,
            _actionSize, _actionSize), fore);

        var titleRect = new Rectangle(left, r.Top + _pad,
            Math.Max(0, r.Right - left - actionZone - _pad), _titleH);
        TextRenderer.DrawText(g, note.Title, Font, titleRect,
            fore,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        var subRect = new Rectangle(left, r.Top + _pad + _titleH + 2,
            Math.Max(0, r.Right - left - actionZone - _pad), _subH);
        TextRenderer.DrawText(g, note.ModifiedAt.ToString("dd/MM 'à' HH:mm"), _subFont, subRect, fore,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        DrawAction(g, GetActionBounds(r, NoteListAction.Delete), fore, NoteListAction.Delete);
        DrawAction(g, GetActionBounds(r, NoteListAction.Rename), fore, NoteListAction.Rename);

        // Un seul pixel physique : délimitation discrète, même à fort grossissement.
        var border = Color.FromArgb((back.R * 3 + fore.R) / 4,
            (back.G * 3 + fore.G) / 4, (back.B * 3 + fore.B) / 4);
        using var outline = new Pen(border);
        g.DrawRectangle(outline, r.Left, r.Top, r.Width - 1, r.Height - 1);
        if (selected)
        {
            int thickness = Math.Max(2, (int)Math.Round(2 * DeviceDpi / 96f));
            using var accent = new SolidBrush(Theme.Accent);
            g.FillRectangle(accent, r.Left, r.Top, r.Width, thickness);
            g.FillRectangle(accent, r.Left, r.Bottom - thickness, r.Width, thickness);
            g.FillRectangle(accent, r.Left, r.Top, thickness, r.Height);
            g.FillRectangle(accent, r.Right - thickness, r.Top, thickness, r.Height);
        }
        if (_insertionIndex == e.Index || (_insertionIndex == Items.Count && e.Index == Items.Count - 1))
        {
            int thickness = Math.Max(3, (int)Math.Round(3 * DeviceDpi / 96f));
            int y = _insertionIndex == e.Index ? r.Top : r.Bottom - thickness;
            using var marker = new SolidBrush(Theme.Accent);
            g.FillRectangle(marker, r.Left + _pad, y, Math.Max(0, r.Width - _pad * 2), thickness);
        }
    }

    private Rectangle GetActionBounds(Rectangle row, NoteListAction action)
    {
        int right = row.Right - _pad;
        int x = action == NoteListAction.Delete ? right - _actionSize : right - _actionSize * 2 - _pad;
        return new Rectangle(x, row.Top + (row.Height - _actionSize) / 2, _actionSize, _actionSize);
    }

    public NoteListAction HitTestAction(Point point)
    {
        int index = IndexFromPoint(point);
        if (index < 0 || index >= Items.Count) return NoteListAction.None;
        var row = GetItemRectangle(index);
        if (GetActionBounds(row, NoteListAction.Delete).Contains(point)) return NoteListAction.Delete;
        if (GetActionBounds(row, NoteListAction.Rename).Contains(point)) return NoteListAction.Rename;
        return NoteListAction.None;
    }

    public Rectangle GetTitleBounds(int index)
    {
        var row = GetItemRectangle(index);
        int left = row.Left + _pad * 2 + _actionSize;
        int actionZone = _actionSize * 2 + _pad * 3;
        return new Rectangle(left, row.Top + _pad,
            Math.Max(0, row.Right - left - actionZone - _pad), _titleH);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressedIndex = e.Button == MouseButtons.Left ? IndexFromPoint(e.Location) : -1;
        _pressedAction = e.Button == MouseButtons.Left ? HitTestAction(e.Location) : NoteListAction.None;
        _pressPoint = e.Location;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int index = IndexFromPoint(e.Location);
        var action = HitTestAction(e.Location);
        bool activate = e.Button == MouseButtons.Left && index == _pressedIndex && action == _pressedAction;
        _pressedIndex = -1;
        _pressedAction = NoteListAction.None;
        if (!activate || index < 0 || index >= Items.Count) return;
        var note = (Note)Items[index];
        if (action == NoteListAction.Delete) DeleteRequested?.Invoke(note);
        else if (action == NoteListAction.Rename) RenameRequested?.Invoke(note);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Cursor = IndexFromPoint(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
        if (e.Button != MouseButtons.Left || _pressedIndex < 0 || _pressedIndex >= Items.Count
            || _draggedNote is not null) return;
        var threshold = new Rectangle(_pressPoint.X - SystemInformation.DragSize.Width / 2,
            _pressPoint.Y - SystemInformation.DragSize.Height / 2,
            SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
        if (threshold.Contains(e.Location)) return;

        _draggedNote = (Note)Items[_pressedIndex];
        _pressedIndex = -1;
        _pressedAction = NoteListAction.None;
        ViewportChanging?.Invoke(this, EventArgs.Empty);
        try { DoDragDrop(_draggedNote, DragDropEffects.Move); }
        finally
        {
            _draggedNote = null;
            _dragScroll.Stop();
            SetInsertionIndex(-1);
            Cursor = Cursors.Default;
        }
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        UpdateDrag(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateDrag(e);
    }

    private void UpdateDrag(DragEventArgs e)
    {
        if (_draggedNote is null || !ReferenceEquals(e.Data?.GetData(typeof(Note)), _draggedNote))
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        UpdateDropTarget(PointToClient(new Point(e.X, e.Y)), scroll: false);
        e.Effect = _insertionIndex >= 0 ? DragDropEffects.Move : DragDropEffects.None;
        if (_insertionIndex >= 0) _dragScroll.Start();
    }

    private void UpdateDropTarget(Point point, bool scroll)
    {
        if (_draggedNote is null || !ClientRectangle.Contains(point) || Items.Count == 0)
        {
            SetInsertionIndex(-1);
            _dragScroll.Stop();
            return;
        }
        if (scroll)
        {
            if (point.Y < ItemHeight / 2 && TopIndex > 0) TopIndex--;
            else if (point.Y > ClientSize.Height - ItemHeight / 2
                && GetItemRectangle(Items.Count - 1).Bottom > ClientSize.Height) TopIndex++;
        }
        int index = IndexFromPoint(point);
        if (index < 0)
            index = point.Y < GetItemRectangle(TopIndex).Top ? TopIndex : Items.Count;
        else if (point.Y >= GetItemRectangle(index).Top + ItemHeight / 2)
            index++;
        SetInsertionIndex(index);
    }

    private void SetInsertionIndex(int index)
    {
        if (_insertionIndex == index) return;
        _insertionIndex = index;
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        _dragScroll.Stop();
        SetInsertionIndex(-1);
        base.OnDragLeave(e);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        UpdateDrag(e);
        _dragScroll.Stop();
        var note = _draggedNote;
        int from = note is null ? -1 : Items.IndexOf(note);
        int target = _insertionIndex;
        SetInsertionIndex(-1);
        if (from < 0 || target < 0) return;
        if (target > from) target--;
        if (from != target) MoveRequested?.Invoke(note!, target);
    }

    private static void DrawGrip(Graphics g, Rectangle bounds, Color color)
    {
        float scale = bounds.Width / 24f;
        using var brush = new SolidBrush(color);
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        for (int row = 0; row < 3; row++)
            for (int column = 0; column < 2; column++)
                g.FillEllipse(brush, bounds.Left + (8 + column * 6) * scale,
                    bounds.Top + (5 + row * 6) * scale, 2.5f * scale, 2.5f * scale);
        g.SmoothingMode = old;
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x0005 or 0x0114 or 0x0115 or 0x020A)
            ViewportChanging?.Invoke(this, EventArgs.Empty);
        base.WndProc(ref message);
    }

    private static void DrawAction(Graphics g, Rectangle bounds, Color color, NoteListAction action)
    {
        // Tracés vectoriels pour garder des icônes nettes à toute échelle Windows.
        float scale = bounds.Width / 24f;
        PointF P(float x, float y) => new(bounds.Left + x * scale, bounds.Top + y * scale);
        using var pen = new Pen(color, Math.Max(1f, 1.4f * scale))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (action == NoteListAction.Delete)
        {
            g.DrawLine(pen, P(5, 7), P(19, 7));
            g.DrawLines(pen, new[] { P(9, 7), P(9, 4), P(15, 4), P(15, 7) });
            g.DrawLines(pen, new[] { P(7, 7), P(8, 20), P(16, 20), P(17, 7) });
            g.DrawLine(pen, P(10, 10), P(10.5f, 17));
            g.DrawLine(pen, P(14, 10), P(13.5f, 17));
        }
        else
        {
            g.DrawPolygon(pen, new[] { P(5, 15), P(15, 5), P(19, 9), P(9, 19), P(4, 20) });
            g.DrawLine(pen, P(13, 7), P(17, 11));
            g.DrawLine(pen, P(5, 15), P(9, 19));
        }
        g.SmoothingMode = old;
    }

    // ---- diagnostics de mise en page (vérifie qu'aucun texte n'est tronqué) ----

    public int DbgPad => _pad;
    public int DbgTitleH => _titleH;
    public int DbgSubH => _subH;
    public int DbgRowHeight => ItemHeight;

    /// <summary>Largeur disponible pour un titre dans la ligne courante, et largeur mesurée du texte.</summary>
    public (int available, int needed, bool fits) MeasureTitle(string title)
    {
        int left = _pad * 2 + _actionSize;
        int actionZone = _actionSize * 2 + _pad * 3;
        int available = Math.Max(0, ClientSize.Width - left - actionZone - _pad);
        int needed = TextRenderer.MeasureText(title, Font, new Size(int.MaxValue, _titleH),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        return (available, needed, needed <= available);
    }

    /// <summary>La date tient-elle dans sa ligne, et la ligne 2 est-elle dans la hauteur de la rangee ?</summary>
    public (int available, int needed, bool fits, bool rowFits) MeasureSub(string sub)
    {
        int left = _pad * 2 + _actionSize;
        int available = Math.Max(0, ClientSize.Width - left - (_actionSize * 2 + _pad * 3) - _pad);
        int needed = TextRenderer.MeasureText(sub, _subFont, new Size(int.MaxValue, _subH),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        return (available, needed, needed <= available, _pad * 2 + _titleH + 2 + _subH <= ItemHeight);
    }
}

public enum NoteListAction { None, Delete, Rename }
