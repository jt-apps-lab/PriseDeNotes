using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace PriseDeNotes;

/// <summary>
/// Vue de toutes les notes : titres seuls, disposés par lignes ou par colonnes.
/// Un appui long sur une carte passe en mode sélection (cartes entourées de bleu),
/// pour archiver les notes choisies dans un zip, ou en restaurer depuis un zip / un json.
/// </summary>
public sealed class NotesOverviewForm : Form
{
    /// <summary>Durée d'appui qui déclenche le mode sélection, comme dans une galerie Android.</summary>
    private const int LongPressMs = 500;

    private readonly IReadOnlyList<Note> _notes;
    private readonly NoteStore _store;
    private readonly RadioButton _horizontal = new() { Text = "Tri horizontal", AutoSize = true, Checked = true };
    private readonly RadioButton _vertical = new() { Text = "Tri vertical", AutoSize = true };
    private readonly Button _btnArchive = new();
    private readonly Button _btnRestore = new();
    private readonly ContextMenuStrip _archiveMenu = new();
    private readonly ToolStripMenuItem _archiveAll = new("Archiver toutes les notes");
    private readonly ToolStripMenuItem _enterSelect = new("Passer en mode sélection");
    private readonly ToolTip _tip = new();
    private readonly System.Windows.Forms.Timer _longPress = new() { Interval = LongPressMs };
    private readonly TextBox _search = new();
    private readonly CheckBox _searchEverywhere = new() { Text = "Chercher dans toutes les notes", AutoSize = true };
    private readonly TrackBar _rows = new();
    private readonly Label _rowCount = new() { AutoSize = true };
    private readonly FlowLayoutPanel _rowOptions = new();
    private readonly Panel _viewport = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Panel _canvas = new();
    private readonly Label _empty = new() { Text = "Aucune note trouvée.", AutoSize = true, ForeColor = Theme.TextDim };

    private readonly HashSet<Guid> _selected = new();
    private Button? _pressedCard;
    private Point _pressedPoint;
    private bool _longPressFired;
    private bool _selecting;
    private string _importFolder;

    public Note? SelectedNote { get; private set; }

    /// <summary>Des notes ont été restaurées : la fenêtre principale doit recharger sa liste.</summary>
    public bool Imported { get; private set; }

    private int S(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96f);

    public NotesOverviewForm(IReadOnlyList<Note> notes, NoteStore store)
    {
        _notes = notes;
        _store = store;
        _importFolder = AppContext.BaseDirectory;
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

        _tip.BackColor = Theme.Surface2;
        _tip.ForeColor = Theme.Text;
        _tip.InitialDelay = 250;
        _tip.ReshowDelay = 120;
        _tip.AutoPopDelay = 9000;
        _tip.ShowAlways = true;
        _tip.IsBalloon = false;

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
            option.Margin = new Padding(0, S(10), S(16), 0);
            sort.Controls.Add(option);
        }
        // Archiver et Restaurer, à droite du tri vertical.
        StyleActionBar(_btnArchive);
        StyleActionBar(_btnRestore);
        _btnArchive.Click += (_, _) => ArchiveFromButton();
        _btnRestore.Click += (_, _) => RestoreNotes();
        sort.Controls.Add(_btnArchive);
        sort.Controls.Add(_btnRestore);
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

        // Appui long : la carte pressée sans bouger fait basculer la vue en mode sélection.
        _longPress.Tick += (_, _) =>
        {
            _longPress.Stop();
            if (_pressedCard is not Button card) return;
            _longPressFired = true;
            _pressedCard = null;
            EnterSelectionMode();
            ToggleCard(card);
        };

        _btnArchive.Text = "Archiver";
        _btnArchive.AccessibleName = "Archiver des notes dans un zip";
        _btnRestore.Text = "Restaurer";
        _btnRestore.AccessibleName = "Restaurer des notes depuis une archive";
        BuildArchiveMenu();
        _tip.SetToolTip(_btnArchive, "Archiver toutes les notes, ou passer en mode sélection pour les choisir");
        _tip.SetToolTip(_btnRestore, "Restaurer des notes depuis une archive .zip ou un fichier .json");

        Shown += (_, _) => { LayoutCards(); _search.Focus(); };
        RebuildCards();
    }

    private void UpdateRowCount() => _rowCount.Text = $"Notes par colonne : {_rows.Value}";

    /// <summary>Bouton de la ligne de tri, dans le style des boutons de la barre principale.</summary>
    private void StyleActionBar(Button button)
    {
        button.AutoSize = true;
        button.AutoEllipsis = false;
        button.UseMnemonic = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.Surface2;
        button.ForeColor = Theme.Text;
        button.Padding = new Padding(S(9), S(4), S(9), S(4));
        button.Margin = new Padding(0, S(6), S(6), 0);
        button.MinimumSize = new Size(0, S(32));
        button.MaximumSize = new Size(0, S(32));
        button.Cursor = Cursors.Hand;
        button.TabStop = false;
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.MouseOverBackColor = Theme.Selection;
        button.FlatAppearance.MouseDownBackColor = Theme.Accent;
    }

    private void RebuildCards()
    {
        _longPress.Stop();
        _pressedCard = null;
        _viewport.AutoScrollPosition = Point.Empty;
        _canvas.SuspendLayout();
        _canvas.Controls.Remove(_empty);
        while (_canvas.Controls.Count > 0) _canvas.Controls[0].Dispose();
        foreach (var note in _notes)
        {
            if (!IsVisible(note)) continue;

            var card = new Button
            {
                Text = note.Tabs.Count > 1 ? $"{note.Title}  ·  {note.Tabs.Count} onglets" : note.Title,
                Tag = note,
                AccessibleName = note.Title,
                AutoEllipsis = true,
                UseMnemonic = false,
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(S(12), S(6), S(12), S(6)),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
            };
            ApplyLook(card, note);
            card.Paint += (_, e) => PaintBadge(e.Graphics, card);
            card.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                _pressedCard = card;
                _pressedPoint = e.Location;
                _longPressFired = false;
                _longPress.Stop();
                _longPress.Start();
            };
            card.MouseUp += (_, _) =>
            {
                _longPress.Stop();
                _pressedCard = null;
            };
            card.MouseMove += (_, e) =>
            {
                if (_pressedCard is null) return;
                int dx = SystemInformation.DragSize.Width / 2, dy = SystemInformation.DragSize.Height / 2;
                if (Math.Abs(e.X - _pressedPoint.X) > dx || Math.Abs(e.Y - _pressedPoint.Y) > dy)
                {
                    _longPress.Stop();
                    _pressedCard = null;
                }
            };
            card.MouseLeave += (_, _) => _longPress.Stop();
            card.Click += (_, _) =>
            {
                if (_longPressFired) { _longPressFired = false; return; }   // l'appui long a déjà choisi la carte
                if (_selecting) { ToggleCard(card); return; }
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

    // ─────────────────────────── Mode sélection ───────────────────────────

    private void EnterSelectionMode()
    {
        if (_selecting) return;
        _selecting = true;
        UpdateArchiveButton();
    }

    private void ExitSelectionMode()
    {
        _longPress.Stop();
        _pressedCard = null;
        _selecting = false;
        _selected.Clear();
        _btnArchive.Text = "Archiver";
        _tip.SetToolTip(_btnArchive, "Archiver toutes les notes, ou passer en mode sélection pour les choisir");
        RefreshLooks();
    }

    private void ToggleCard(Button card)
    {
        if (card.Tag is not Note note) return;
        if (!_selected.Add(note.Id)) _selected.Remove(note.Id);
        ApplyLook(card, note);
        UpdateArchiveButton();
    }

    private void UpdateArchiveButton()
    {
        int n = _selected.Count;
        _btnArchive.Text = n == 0 ? "Archiver" : $"Archiver ({n})";
        _tip.SetToolTip(_btnArchive, n == 0
            ? "Cliquez sur les notes pour les choisir (appui long pour entrer dans ce mode)"
            : $"Archiver {n} note{(n == 1 ? "" : "s")} dans un zip");
    }

    /// <summary>Carte choisie : bordure bleue, fond teinté d'accent, coche en haut à droite.</summary>
    private void ApplyLook(Button card, Note note)
    {
        bool chosen = _selected.Contains(note.Id);
        var back = ColorExt.ToColor(note.BackColor, Theme.Surface2);
        card.BackColor = chosen ? Blend(back, Theme.Accent, 0.34f) : back;
        card.ForeColor = ColorExt.ToColor(note.ForeColor, Theme.Text);
        card.FlatAppearance.BorderColor = chosen ? Theme.Accent : Theme.Border;
        card.FlatAppearance.BorderSize = chosen ? Math.Max(2, S(2)) : 1;
        card.FlatAppearance.MouseOverBackColor = chosen ? Blend(back, Theme.Accent, 0.52f) : Theme.Selection;
        card.Invalidate();
    }

    private void RefreshLooks()
    {
        foreach (Control control in _canvas.Controls)
            if (control is Button card && card.Tag is Note note) ApplyLook(card, note);
    }

    private void PaintBadge(Graphics g, Button card)
    {
        if (card.Tag is not Note note || !_selected.Contains(note.Id)) return;
        int d = S(19);
        var badge = new Rectangle(card.Width - d - S(7), S(7), d, d);
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var fill = new SolidBrush(Theme.Accent)) g.FillEllipse(fill, badge);
        using (var check = new Pen(Color.FromArgb(248, 248, 248), Math.Max(1.7f, S(2) * 0.9f)))
            g.DrawLines(check, new[]
            {
                new PointF(badge.Left + d * 0.26f, badge.Top + d * 0.53f),
                new PointF(badge.Left + d * 0.43f, badge.Top + d * 0.71f),
                new PointF(badge.Left + d * 0.76f, badge.Top + d * 0.29f),
            });
        g.SmoothingMode = previous;
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    // ─────────────────────────── Archive et restauration ───────────────────────────

    /// <summary>Menu du bouton Archiver : toutes les notes d'un coup, ou choix note par note.</summary>
    private void BuildArchiveMenu()
    {
        _archiveMenu.BackColor = Theme.Surface2;
        _archiveMenu.ForeColor = Theme.Text;
        _archiveMenu.ShowImageMargin = false;
        _archiveMenu.Renderer = new DarkMenuRenderer();
        _archiveAll.Click += (_, _) => ArchiveNotes(VisibleNotes());
        _enterSelect.Click += (_, _) =>
        {
            if (_selecting) ExitSelectionMode();
            else EnterSelectionMode();
        };
        _archiveMenu.Items.AddRange(new ToolStripItem[] { _archiveAll, new ToolStripSeparator(), _enterSelect });
    }

    /// <summary>Clic sur Archiver : les notes choisies partent direct, sinon le menu propose les deux voies.</summary>
    private void ArchiveFromButton()
    {
        if (_selecting && _selected.Count > 0) { ArchiveNotes(SelectedNotes()); return; }
        int shown = VisibleNotes().Count;
        _archiveAll.Text = shown == _notes.Count
            ? $"Archiver toutes les notes ({shown})"
            : $"Archiver les {shown} notes affichées";
        _enterSelect.Text = _selecting ? "Quitter le mode sélection" : "Passer en mode sélection";
        _archiveMenu.Show(_btnArchive, new Point(0, _btnArchive.Height));
    }

    private List<Note> VisibleNotes() => _notes.Where(IsVisible).ToList();

    private List<Note> SelectedNotes() => _notes.Where(n => _selected.Contains(n.Id)).ToList();

    /// <summary>La note est-elle affichée, filtre de recherche appliqué ?</summary>
    private bool IsVisible(Note note)
    {
        string query = _search.Text.Trim();
        if (query.Length == 0) return true;
        return note.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || _searchEverywhere.Checked
            && note.Tabs.Any(t => t.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void ArchiveNotes(IReadOnlyList<Note> chosen)
    {
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, "Aucune note à archiver.",
                "Archiver", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Archiver les notes",
            Filter = "Archive ZIP (*.zip)|*.zip",
            DefaultExt = "zip",
            FileName = DateTime.Now.ToString("'Archive'yyyy-MM-dd HH'h'mm", CultureInfo.InvariantCulture) + ".zip",
            InitialDirectory = AppContext.BaseDirectory,
            AddToRecent = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var archive = ZipFile.Open(dialog.FileName, ZipArchiveMode.Create);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var note in chosen)
            {
                using var entry = archive.CreateEntry(EntryName(used, note)).Open();
                using var writer = new StreamWriter(entry, new UTF8Encoding(false));
                writer.Write(_store.JsonFor(note));
            }
            int count = chosen.Count;
            ExitSelectionMode();
            MessageBox.Show(this,
                $"{count} note{(count == 1 ? "" : "s")} archivée{(count == 1 ? "" : "s")}.\n\n{dialog.FileName}",
                "Archiver", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                  or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, "Impossible de créer l'archive :\n" + e.Message,
                "Archiver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestoreNotes()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Restaurer des notes",
            Filter = "Notes Prise de notes (*.zip; *.json)|*.zip;*.json|Archives ZIP (*.zip)|*.zip|Notes (*.json)|*.json",
            InitialDirectory = _importFolder,
            Multiselect = true,
            AddToRecent = false,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var files = dialog.FileNames;
        if (files.Length > 0) _importFolder = Path.GetDirectoryName(files[0]) ?? _importFolder;
        var result = _store.Import(files);
        ExitSelectionMode();

        if (result.Notes.Count == 0)
        {
            MessageBox.Show(this,
                "Aucune note n'a pu être lue." + (result.Skipped > 0 ? $"\n{result.Skipped} élément(s) ignoré(s)." : "")
                + "\nL'archive doit contenir les fichiers .json écrits par « Archiver ».",
                "Restaurer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Imported = true;
        string detail = result.Skipped > 0 ? $"  ·  {result.Skipped} élément(s) ignoré(s)" : "";
        MessageBox.Show(this,
            $"{result.Notes.Count} note{(result.Notes.Count == 1 ? "" : "s")} restaurée{(result.Notes.Count == 1 ? "" : "s")}{detail}."
            + "\nElles apparaissent dans la liste des notes.",
            "Restaurer", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    /// <summary>Nom de fichier lisible dans l'archive, sans doublon : « Ma note.json », « Ma note (2).json »…</summary>
    private static string EntryName(HashSet<string> used, Note note)
    {
        var baseName = SafeEntryName(note.Title);
        var name = baseName + ".json";
        for (int n = 2; !used.Add(name); n++) name = $"{baseName} ({n}).json";
        return name;
    }

    private static string SafeEntryName(string title)
    {
        var kept = new string(title.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray());
        var name = string.Join(" ", kept.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (name.Length > 60) name = name[..60].TrimEnd();
        return name.Length == 0 ? "note" : name;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        LayoutCards();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_selecting) ExitSelectionMode();
            else Close();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _empty.Dispose();
            _longPress.Dispose();
            _tip.Dispose();
            _archiveMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
