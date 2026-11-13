namespace PriseDeNotes;

/// <summary>
/// Barre d'onglets dessinée à la main, dans le thème sombre : un onglet par feuille de la note,
/// un bouton « + » fixé à droite pour en ajouter. Le clic sélectionne, le glisser-déposer réordonne,
/// le clic droit ouvre le menu du formulaire. La barre ne dessine et ne mesure rien pour la
/// saisie : le champ du titre d'onglet appartient au formulaire, comme celui du titre d'une note.
/// Toutes les largeurs viennent des métriques réelles de la police : la barre reste nette
/// quelle que soit la mise à l'échelle Windows (100 %, 150 %, 200 %).
/// </summary>
public sealed class TabStrip : Control
{
    private int _pad, _textH, _plusSize, _rowH, _maxTabW, _rail;
    private readonly List<NoteTab> _tabs = new();
    private readonly List<int> _widths = new();
    private readonly List<Rectangle> _rects = new();
    private int _selected = -1;
    private int _hover = -1;
    private bool _hoverPlus;
    private int _pressed = -1;
    private bool _pressedPlus;
    private Point _pressPoint;
    private int _insertion = -1;
    private int _dragIndex = -1;
    private int _scrollX;
    private Rectangle _plusRect;
    private Color _activeBack = ColorExt.ToColor(Theme.DefaultNoteBack, Theme.Window);
    private Color _activeFore = ColorExt.ToColor(Theme.DefaultNoteFore, Theme.Text);

    public event Action<int>? TabSelected;
    public event Action? AddRequested;
    public event Action<int, int>? MoveRequested;
    public event Action<int, Point>? ContextRequested;

    /// <summary>Indique qu'un onglet est en cours de renommage : le TabStrip ne doit pas voler le focus.</summary>
    public bool IsRenaming { get; set; }

    public TabStrip()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        TabStop = false;
        AllowDrop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        RecomputeLayout();
    }

    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    /// <summary>Hauteur d'une barre d'onglets, calculée sur la police courante.</summary>
    public int PreferredStripHeight => _rowH + _rail + S(4);

    /// <summary>Fond et encre de l'onglet actif, repris de la note.</summary>
    public void SetActiveColors(Color back, Color fore)
    {
        _activeBack = back;
        _activeFore = fore;
        Invalidate();
    }

    public int TabCount => _tabs.Count;
    public int SelectedIndex => _selected;

    /// <summary>Recopie les titres de la note courante. <paramref name="selectedIndex"/> est l'onglet actif.</summary>
    public void SetTabs(IReadOnlyList<NoteTab>? tabs, int selectedIndex)
    {
        _tabs.Clear();
        if (tabs is not null) _tabs.AddRange(tabs);
        _selected = selectedIndex;
        LayoutTabs();
        Invalidate();
    }

    /// <summary>Change la sélection sans lever d'événement (le formulaire est à l'origine du changement).</summary>
    public void MarkSelected(int index)
    {
        if (index == _selected) return;
        _selected = index;
        ScrollToShow(index);
        Invalidate();
    }

    public int IndexFromPoint(Point point)
    {
        for (int i = 0; i < _rects.Count; i++)
            if (_rects[i].Contains(point)) return i;
        return -1;
    }

    /// <summary>Le point tombe-t-il sur le bouton « + » fixé à droite ?</summary>
    public bool PlusHitTest(Point point) => _plusRect.Contains(point);

    public Rectangle TabBounds(int index) => index >= 0 && index < _rects.Count ? _rects[index] : Rectangle.Empty;

    // ─────────────────────────── Mise en page ───────────────────────────

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

    private void RecomputeLayout()
    {
        var probe = new Size(int.MaxValue, int.MaxValue);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        _textH = TextRenderer.MeasureText("Tgpq", Font, probe, flags).Height;
        if (_textH <= 0) _textH = Math.Max(1, Font.Height);

        _pad = Math.Max(S(8), _textH / 2);
        _rail = Math.Max(1, S(1));
        _plusSize = Math.Max(S(22), _textH + S(8));
        _rowH = _textH + S(10);
        _maxTabW = S(200);
        // La hauteur de la barre est imposée à la ligne du panneau : rien ne se déforme au changement d'échelle.
        MinimumSize = new Size(0, PreferredStripHeight);
        MaximumSize = new Size(0, PreferredStripHeight);
        LayoutTabs();
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(S(80), proposedSize.Width), PreferredStripHeight);

    /// <summary>Barre masquée (aucune note) : sa ligne se replie, la zone de texte reprend la place.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        int height = PreferredStripHeight;
        MinimumSize = new Size(0, Visible ? height : 0);
        MaximumSize = new Size(0, Visible ? height : 0);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutTabs();
    }

    private void LayoutTabs()
    {
        _widths.Clear();
        _rects.Clear();
        var probe = new Size(int.MaxValue, int.MaxValue);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        int total = 0;
        foreach (var tab in _tabs)
        {
            int needed = TextRenderer.MeasureText(tab.Title, Font, probe, flags).Width;
            int width = Math.Clamp(needed + _pad * 2, _pad * 4, _maxTabW);
            _widths.Add(width);
            total += width;
        }

        _plusRect = new Rectangle(Math.Max(0, ClientSize.Width - _plusSize), (ClientSize.Height - _plusSize) / 2,
            _plusSize, _plusSize);
        int viewport = Math.Max(0, _plusRect.Left - S(4));
        int rowTop = ClientSize.Height - _rail - _rowH;

        // L'onglet actif doit rester dans la zone visible.
        int content = total;
        int maxScroll = Math.Max(0, content - viewport);
        if (_selected >= 0 && _selected < _widths.Count)
        {
            int left = _widths.Take(_selected).Sum();
            int right = left + _widths[_selected];
            if (left - _scrollX < 0) _scrollX = left;
            else if (right - _scrollX > viewport) _scrollX = right - viewport;
        }
        _scrollX = Math.Clamp(_scrollX, 0, maxScroll);

        int x = -_scrollX;
        for (int index = 0; index < _widths.Count; index++)
        {
            _rects.Add(new Rectangle(x, rowTop, _widths[index], _rowH));
            x += _widths[index];
        }
    }

    private void ScrollToShow(int index) => LayoutTabs();

    // ─────────────────────────── Dessin ───────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var background = new SolidBrush(BackColor))
            g.FillRectangle(background, ClientRectangle);

        using var border = new Pen(Theme.Border);
        using var accent = new SolidBrush(Theme.Accent);
        using var rail = new SolidBrush(Theme.Border);
        int railY = ClientSize.Height - _rail;
        g.FillRectangle(rail, 0, railY, ClientSize.Width, _rail);

        for (int index = 0; index < _rects.Count; index++)
        {
            var r = _rects[index];
            if (r.Right < 0 || r.Left > _plusRect.Left) continue;
            bool active = index == _selected;
            bool dragged = index == _dragIndex;

            Color back = active ? _activeBack : index == _hover ? Theme.Selection : Theme.Surface2;
            Color fore = dragged ? Theme.TextDim : active ? _activeFore : index == _hover ? Theme.Text : Theme.TextDim;
            using (var fill = new SolidBrush(back))
                g.FillRectangle(fill, r.Left, r.Top, r.Width, r.Height);
            g.DrawRectangle(border, r.Left, r.Top, r.Width - 1, r.Height - 1);
            if (active)
            {
                int thickness = Math.Max(2, S(2));
                g.FillRectangle(accent, r.Left, r.Top, r.Width, thickness);
            }

            var text = new Rectangle(r.Left + _pad, r.Top, Math.Max(0, r.Width - _pad * 2), r.Height);
            TextRenderer.DrawText(g, _tabs[index].Title, Font, text, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        DrawPlus(g);

        if (_insertion >= 0)
        {
            int x = _insertion < _rects.Count ? _rects[_insertion].Left : RightEdge();
            int thickness = Math.Max(3, S(3));
            g.FillRectangle(accent, Math.Max(0, x - thickness / 2), 0, thickness, Math.Max(0, railY));
        }
    }

    private int RightEdge() => _rects.Count == 0 ? 0 : _rects[^1].Right;

    private void DrawPlus(Graphics g)
    {
        var r = _plusRect;
        if (_pressedPlus || _hoverPlus)
        {
            using var fill = new SolidBrush(_pressedPlus ? Theme.Selection : Theme.Surface2);
            g.FillRectangle(fill, r);
            using var frame = new Pen(Theme.Border);
            g.DrawRectangle(frame, r.Left, r.Top, r.Width - 1, r.Height - 1);
        }
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(_hoverPlus || _pressedPlus ? Theme.Text : Theme.TextDim,
            Math.Max(1f, DeviceDpi / 96f * 1.6f))
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round,
        };
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f, arm = r.Width * 0.28f;
        g.DrawLine(pen, cx - arm, cy, cx + arm, cy);
        g.DrawLine(pen, cx, cy - arm, cx, cy + arm);
        g.SmoothingMode = old;
    }

    // ─────────────────────────── Souris ───────────────────────────

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!IsRenaming) Focus();
        if (e.Button == MouseButtons.Left && _plusRect.Contains(e.Location))
        {
            _pressedPlus = true;
            Invalidate();
            return;
        }
        int index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            _pressed = index;
            _pressPoint = e.Location;
            if (index != _selected)
            {
                _selected = index;
                LayoutTabs();
                TabSelected?.Invoke(index);
            }
            Invalidate();
            return;
        }
        if (e.Button == MouseButtons.Right && index >= 0)
        {
            if (index != _selected)
            {
                _selected = index;
                LayoutTabs();
                TabSelected?.Invoke(index);
            }
            Invalidate();
            ContextRequested?.Invoke(index, PointToScreen(e.Location));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _pressed = -1;
        if (!_pressedPlus) return;
        _pressedPlus = false;
        Invalidate();
        if (_plusRect.Contains(e.Location)) AddRequested?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool overPlus = _plusRect.Contains(e.Location);
        int index = IndexFromPoint(e.Location);
        if (overPlus != _hoverPlus) { _hoverPlus = overPlus; Invalidate(); }
        if (index != _hover) { _hover = index; Invalidate(); }
        Cursor = overPlus || index >= 0 ? Cursors.Hand : Cursors.Default;

        if (_pressed < 0 || e.Button != MouseButtons.Left || _dragIndex >= 0) return;
        var threshold = new Rectangle(_pressPoint.X - SystemInformation.DragSize.Width / 2,
            _pressPoint.Y - SystemInformation.DragSize.Height / 2,
            SystemInformation.DragSize.Width, SystemInformation.DragSize.Height);
        if (!threshold.Contains(e.Location)) return;

        _dragIndex = _pressed;
        _pressed = -1;
        var dragged = _tabs[_dragIndex];
        Invalidate();
        try { DoDragDrop(dragged, DragDropEffects.Move); }
        finally
        {
            _dragIndex = -1;
            SetInsertion(-1);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverPlus = false;
        Invalidate();
    }

    // ─────────────────────────── Glisser-déposer ───────────────────────────

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
        if (_dragIndex < 0 || !ReferenceEquals(e.Data?.GetData(typeof(NoteTab)), _tabs[_dragIndex]))
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        var point = PointToClient(new Point(e.X, e.Y));
        int target = InsertionAt(point.X);
        SetInsertion(target);
        e.Effect = target >= 0 ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>Place d'insertion (0 = avant le premier, Count = après le dernier) la plus proche de x.</summary>
    private int InsertionAt(int x)
    {
        if (_rects.Count == 0) return _tabs.Count == 0 ? -1 : 0;
        for (int index = 0; index < _rects.Count; index++)
            if (x < _rects[index].Left + _rects[index].Width / 2) return index;
        return _rects.Count;
    }

    private void SetInsertion(int index)
    {
        if (_insertion == index) return;
        _insertion = index;
        Invalidate();
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        SetInsertion(-1);
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        UpdateDrag(e);
        if (_dragIndex < 0) return;
        int from = _dragIndex;
        int target = _insertion;
        SetInsertion(-1);
        if (target < 0) return;
        if (target > from) target--;
        if (from != target) MoveRequested?.Invoke(from, target);
    }

    // ---- diagnostics de mise en page (vérifie qu'aucun texte n'est tronqué) ----

    public int DbgPad => _pad;
    public int DbgRowHeight => _rowH;
    public int DbgScroll => _scrollX;
    public int DbgContentWidth => _widths.Sum();
    public int DbgPlusWidth => _plusSize;

    /// <summary>Largeur disponible pour le titre d'un onglet, et largeur mesurée de ce titre.</summary>
    public (int available, int needed, bool fits) MeasureTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return (0, 0, true);
        int available = Math.Max(0, _widths[index] - _pad * 2);
        int needed = TextRenderer.MeasureText(_tabs[index].Title, Font, new Size(int.MaxValue, _textH),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        return (available, needed, needed <= available);
    }
}
