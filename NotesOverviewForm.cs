namespace PriseDeNotes;

/// <summary>Vue de toutes les notes : titres seuls, disposés par lignes ou par colonnes.</summary>
public sealed class NotesOverviewForm : Form
{
    private readonly IReadOnlyList<Note> _notes;
    private readonly RadioButton _horizontal = new() { Text = "Tri horizontal", AutoSize = true, Checked = true };
    private readonly RadioButton _vertical = new() { Text = "Tri vertical", AutoSize = true };
    private readonly TextBox _search = new();
    private readonly CheckBox _searchEverywhere = new() { Text = "Chercher dans toutes les notes", AutoSize = true };
    private readonly TrackBar _rows = new();
    private readonly Label _rowCount = new() { AutoSize = true };
    private readonly FlowLayoutPanel _rowOptions = new();
    private readonly Panel _viewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel _canvas = new();
    private readonly Label _empty = new() { Text = "Aucune note trouvée.", AutoSize = true, ForeColor = Theme.TextDim };

    public Note? SelectedNote { get; private set; }

    private int S(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);

    public NotesOverviewForm(IReadOnlyList<Note> notes)
    {
        _notes = notes;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi);
        Text = "Toutes les notes";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(S(1040), S(680));
        MinimumSize = new Size(S(800), S(450));
        ShowInTaskbar = false;
        MinimizeBox = false;

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = S(106),
            Padding = new Padding(S(14), S(8), S(14), S(8)),
            BackColor = Theme.Surface,
            ColumnCount = 2,
            RowCount = 2,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var sort = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = Padding.Empty,
        };
        foreach (var option in new[] { _horizontal, _vertical })
        {
            option.Margin = new Padding(0, S(10), S(22), 0);
            sort.Controls.Add(option);
        }
        header.Controls.Add(sort, 0, 0);

        _rows.Minimum = 1;
        _rows.Maximum = Math.Max(1, notes.Count);
        _rows.Value = Math.Min(5, _rows.Maximum);
        _rows.TickStyle = TickStyle.None;
        _rows.AutoSize = false;
        _rows.Size = new Size(S(180), S(34));
        _rows.SmallChange = 1;
        _rows.LargeChange = 1;
        _rows.AccessibleName = "Notes par colonne";
        _rows.BackColor = Theme.Surface;
        _rowCount.Margin = new Padding(0, S(10), S(8), 0);
        _rowOptions.AutoSize = true;
        _rowOptions.WrapContents = false;
        _rowOptions.Anchor = AnchorStyles.Left;
        _rowOptions.Margin = Padding.Empty;
        _rowOptions.Controls.Add(_rowCount);
        _rowOptions.Controls.Add(_rows);
        _rowOptions.Visible = false;
        header.Controls.Add(_rowOptions, 1, 0);
        UpdateRowCount();

        _search.PlaceholderText = "Chercher une note";
        _search.AccessibleName = "Chercher une note";
        _search.BackColor = Theme.Surface2;
        _search.ForeColor = Theme.Text;
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.Dock = DockStyle.Fill;
        _search.Margin = new Padding(0, S(9), S(18), S(6));
        header.Controls.Add(_search, 0, 1);
        _searchEverywhere.Anchor = AnchorStyles.Left;
        _searchEverywhere.Margin = Padding.Empty;
        header.Controls.Add(_searchEverywhere, 1, 1);

        _viewport.Controls.Add(_canvas);
        Controls.Add(_viewport);
        Controls.Add(header);

        _search.TextChanged += (_, _) => RebuildCards();
        _searchEverywhere.CheckedChanged += (_, _) => RebuildCards();
        _vertical.CheckedChanged += (_, _) =>
        {
            _rowOptions.Visible = _vertical.Checked;
            _viewport.AutoScrollPosition = Point.Empty;
            LayoutCards();
        };
        _rows.ValueChanged += (_, _) =>
        {
            UpdateRowCount();
            _viewport.AutoScrollPosition = Point.Empty;
            LayoutCards();
        };
        _viewport.ClientSizeChanged += (_, _) => LayoutCards();
        Shown += (_, _) => { LayoutCards(); _search.Focus(); };
        RebuildCards();
    }

    private void UpdateRowCount() => _rowCount.Text = $"Notes par colonne : {_rows.Value}";

    private void RebuildCards()
    {
        _viewport.AutoScrollPosition = Point.Empty;
        _canvas.SuspendLayout();
        _canvas.Controls.Remove(_empty);
        while (_canvas.Controls.Count > 0) _canvas.Controls[0].Dispose();
        string query = _search.Text.Trim();
        foreach (var note in _notes)
        {
            bool inTitle = note.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool inTabs = _searchEverywhere.Checked
                && note.Tabs.Any(t => t.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (query.Length > 0 && !inTitle && !inTabs) continue;

            var card = new Button
            {
                Text = note.Tabs.Count > 1 ? $"{note.Title}  ·  {note.Tabs.Count} onglets" : note.Title,
                Tag = note,
                AccessibleName = note.Title,
                AutoEllipsis = true,
                UseMnemonic = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = ColorExt.ToColor(note.BackColor, Theme.Surface2),
                ForeColor = ColorExt.ToColor(note.ForeColor, Theme.Text),
                Padding = new Padding(S(12), S(6), S(12), S(6)),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
            };
            card.FlatAppearance.BorderColor = Theme.Border;
            card.FlatAppearance.MouseOverBackColor = Theme.Selection;
            card.Click += (_, _) =>
            {
                SelectedNote = note;
                DialogResult = DialogResult.OK;
            };
            _canvas.Controls.Add(card);
        }
        if (_canvas.Controls.Count == 0) _canvas.Controls.Add(_empty);
        _canvas.ResumeLayout(false);
        LayoutCards();
    }

    private void LayoutCards()
    {
        int padding = S(16), gap = S(10), width = S(240), height = S(64);
        int available = Math.Max(1, _viewport.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        int count = _canvas.Controls.Count;
        bool empty = count == 1 && ReferenceEquals(_canvas.Controls[0], _empty);
        int columns = _vertical.Checked
            ? Math.Max(1, (count + _rows.Value - 1) / _rows.Value)
            : Math.Max(1, (available - padding * 2 + gap) / (width + gap));
        int rows = _vertical.Checked ? Math.Min(count, _rows.Value) : (count + columns - 1) / columns;
        _canvas.SuspendLayout();
        for (int i = 0; i < count; i++)
        {
            int column = _vertical.Checked ? i / _rows.Value : i % columns;
            int row = _vertical.Checked ? i % _rows.Value : i / columns;
            if (empty) _empty.Location = new Point(padding, padding);
            else _canvas.Controls[i].SetBounds(padding + column * (width + gap), padding + row * (height + gap), width, height);
        }
        _canvas.Size = new Size(
            Math.Max(available, padding * 2 + columns * (width + gap) - gap),
            Math.Max(1, padding * 2 + rows * (height + gap) - gap));
        _canvas.ResumeLayout(false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutCards();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _empty.Dispose();
        base.Dispose(disposing);
    }
}
