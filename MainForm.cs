using System.Drawing.Text;
using System.Globalization;

namespace PriseDeNotes;

public sealed class MainForm : Form
{
    private static readonly float[] Sizes = { 9f, 10f, 11f, 12f, 14f, 16f, 18f, 20f, 24f, 28f };
    /// <summary>
    /// Convertit une taille conçue pour 96 ppp en pixels réels de l'écran courant.
    /// Sans ça, les hauteurs figées deviennent trop petites à 150 % ou 200 % de mise à l'échelle.
    /// </summary>
    private int S(int px) => (int)Math.Round(px * DeviceDpi / 96f);

    private readonly NoteStore _store;
    private readonly List<Note> _all = new();
    private readonly List<Note> _view = new();
    // Noms des polices installées, lus une seule fois : chaque lecture de InstalledFontCollection.Families
    // recrée des centaines d'objets FontFamily GDI+.
    private readonly string[] _fontNames = LoadFontNames();
    private readonly HashSet<string> _fontSet;
    // Notes dont le dernier enregistrement a échoué (fichier verrouillé, disque plein…) : réessayées ensuite.
    private readonly HashSet<Note> _unsaved = new();
    private string? _saveError;
    private readonly ToolTip _tip = new();
    private readonly DarkToolTip _dateTip = new();
    private readonly System.Windows.Forms.Timer _autosave = new() { Interval = 1200 };

    private readonly TitleListBox _list = new();
    private readonly RichTextBox _body = new();
    private TextBox _search = new();
    private TextBox _title = new();
    private TextBox _tabTitle = new();
    private ComboBox _fontFamily = new();
    private ComboBox _fontSize = new();
    private ColorButton _btnBack = new();
    private ColorButton _btnFore = new();
    private Button _btnNew = new();
    private Button _btnViewAll = new();
    private Button _btnSettings = new();
    private readonly Dictionary<FontStyle, CheckBox> _formatButtons = new();
    private readonly Dictionary<HorizontalAlignment, CheckBox> _alignmentButtons = new();
    private readonly ContextMenuStrip _settingsMenu = new();
    private readonly ContextMenuStrip _bodyMenu = new();
    private readonly CheckBox _confirmDelete = new()
    {
        Text = "Demande de confirmation avant suppression",
        Checked = true,
        AutoSize = false,
        CheckAlign = ContentAlignment.MiddleRight,
        TextAlign = ContentAlignment.MiddleLeft,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Surface2,
        ForeColor = Theme.Text,
        UseVisualStyleBackColor = false,
    };
    private readonly CheckBox _lineNumbers = new()
    {
        Text = "Afficher les n° de lignes",
        AutoSize = false,
        CheckAlign = ContentAlignment.MiddleRight,
        TextAlign = ContentAlignment.MiddleLeft,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Surface2,
        ForeColor = Theme.Text,
        UseVisualStyleBackColor = false,
    };
    private readonly CheckBox _wordWrap = new()
    {
        Text = "retour auto à la ligne",
        Checked = true,
        AutoSize = false,
        CheckAlign = ContentAlignment.MiddleRight,
        TextAlign = ContentAlignment.MiddleLeft,
        FlatStyle = FlatStyle.Flat,
        BackColor = Theme.Surface2,
        ForeColor = Theme.Text,
        UseVisualStyleBackColor = false,
    };
    private LineNumberGutter _gutter = null!;
    private Panel _noteBox = null!;
    private readonly TabStrip _tabs = new();
    private readonly ContextMenuStrip _tabMenu = new();
    private int _tabMenuIndex = -1;
    // Onglet dont le titre est en cours de saisie dans _tabTitle : la saisie lui est liée,
    // elle ne peut donc pas glisser sur l'onglet d'une autre note.
    private NoteTab? _tabRenaming;
    // Renommage demandé par le menu contextuel : lancé seulement une fois le menu fermé,
    // quand il a rendu le focus à la barre (sinon il le reprendrait au champ de titre).
    private int _pendingTabRename = -1;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _lblStatus = new();

    private Note? _current;
    private Font? _bodyFont;
    private bool _loading;
    private bool _richTextDirty;
    private bool _manualTitleChanged;
    private bool _syncingFont;
    private int _lastTipIndex = -1;
    // Police de base de la note : sert au texte sans mise en forme, et aux listes du haut.
    private string _noteFamily = "Segoe UI";
    private float _noteSize = 11f;
    private Color _noteFore = Theme.Text;

    public MainForm(NoteStore store)
    {
        _store = store;
        _fontSet = new HashSet<string>(_fontNames, StringComparer.Ordinal);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi);

        Text = "Prise de notes";
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(1180), S(720));
        MinimumSize = new Size(S(1000), S(540));
        Icon = MakeIcon();

        BuildUi();
        _tip.BackColor = Theme.Surface2;
        _tip.ForeColor = Theme.Text;
        _tip.InitialDelay = 250;
        _tip.ReshowDelay = 120;
        _tip.AutoPopDelay = 9000;
        _tip.ShowAlways = true;
        _tip.IsBalloon = false;
        var ui = _store.LoadUi();
        ApplyUi(ui);
        WireEvents();
        ReloadNotes();
        RebuildView(_all.FirstOrDefault());
    }

    /// <summary>Relit toutes les notes depuis le disque, en gardant l'ordre mémorisé.</summary>
    private void ReloadNotes()
    {
        var notes = _store.LoadAll().OrderByDescending(n => n.ModifiedAt).ToList();
        // Les notes nouvelles ou absentes de l'ordre mémorisé restent en tête.
        var order = _store.LoadUi()?.NoteOrder ?? new List<Guid>();
        _all.Clear();
        _all.AddRange(notes.Where(n => !order.Contains(n.Id)));
        _all.AddRange(notes.Where(n => order.Contains(n.Id)).OrderBy(n => order.IndexOf(n.Id)));
        if (_all.Count == 0) SeedFirstNote();
    }

    // ─────────────────────────── Construction de l'interface ───────────────────────────

    private void BuildUi()
    {
        // ---- barre d'outils (haut) ----
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 8,
            RowCount = 1,
            BackColor = Theme.Surface,
            Padding = new Padding(S(8), S(7), S(8), S(7)),
            Height = S(48),
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));  // nouvelle
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));  // voir tout
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // mise en forme
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(190))); // police
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(78)));  // taille
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(52)));  // fond
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(52)));  // encre
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(42)));  // paramètres
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _bar = bar;

        _btnNew = FlatButton("Nouvelle");
        _btnViewAll = FlatButton("Voir tout");

        var formatting = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(S(12), 0, S(10), 0),
        };
        foreach (var (alignment, label) in new[]
        {
            (HorizontalAlignment.Left, "Aligner à gauche"),
            (HorizontalAlignment.Center, "Centrer"),
            (HorizontalAlignment.Right, "Aligner à droite"),
        })
        {
            var button = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoCheck = false,
                AccessibleName = label,
                Size = new Size(S(30), S(32)),
                Margin = new Padding(S(2), 0, S(2), 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface2,
                ForeColor = Theme.Text,
                TabStop = false,
            };
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.MouseOverBackColor = Theme.Selection;
            button.FlatAppearance.MouseDownBackColor = Theme.Selection;
            button.FlatAppearance.CheckedBackColor = Theme.Selection;
            button.Paint += (_, e) => DrawAlignmentIcon(e.Graphics, button.ClientRectangle, alignment, button.ForeColor);
            button.Click += (_, _) => ApplyAlignment(alignment);
            _tip.SetToolTip(button, label);
            _alignmentButtons.Add(alignment, button);
            formatting.Controls.Add(button);
        }
        foreach (var (style, glyph, label) in new[]
        {
            (FontStyle.Bold, "B", "Gras"),
            (FontStyle.Italic, "I", "Italique"),
            (FontStyle.Underline, "U", "Souligné"),
            (FontStyle.Strikeout, "S", "Barré"),
        })
        {
            var button = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoCheck = false,
                Text = glyph,
                AccessibleName = label,
                Font = new Font("Segoe UI", 10f, style),
                Size = new Size(S(30), S(32)),
                Margin = new Padding(S(2), 0, S(2), 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface2,
                ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                TabStop = false,
            };
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.MouseOverBackColor = Theme.Selection;
            button.FlatAppearance.MouseDownBackColor = Theme.Selection;
            button.FlatAppearance.CheckedBackColor = Theme.Selection;
            button.Click += (_, _) => ToggleStyle(style);
            _tip.SetToolTip(button, label);
            _formatButtons.Add(style, button);
            formatting.Controls.Add(button);
        }

        _title = DarkTextBox();
        _title.PlaceholderText = "Titre de la note…";
        _title.AccessibleName = "Titre de la note";
        _title.Dock = DockStyle.None;
        _title.BorderStyle = BorderStyle.FixedSingle;
        _title.HideSelection = false;
        _title.Visible = false;

        _tabTitle = DarkTextBox();
        _tabTitle.AccessibleName = "Titre de l'onglet";
        _tabTitle.Dock = DockStyle.None;
        _tabTitle.BorderStyle = BorderStyle.FixedSingle;
        _tabTitle.HideSelection = false;
        _tabTitle.Visible = false;

        _fontFamily.DropDownStyle = ComboBoxStyle.DropDownList;
        _fontSize.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var name in _fontNames) _fontFamily.Items.Add(name);
        _fontFamily.SelectedItem = "Segoe UI";
        foreach (var s in Sizes) _fontSize.Items.Add(s.ToString("0.#", CultureInfo.InvariantCulture));
        _fontSize.SelectedItem = "11";
        StyleCombo(_fontFamily);
        StyleCombo(_fontSize);

        _btnBack = ColorSelector(Theme.DefaultNoteBack, false);
        _btnBack.AccessibleName = "Couleur de fond de la note";
        _btnFore = ColorSelector(Theme.DefaultNoteFore, true);
        _btnFore.AccessibleName = "Couleur de la police de la note";
        _btnSettings = Swatch(Theme.Surface2.ToHex());
        _btnSettings.AccessibleName = "Paramètres";
        _btnSettings.MinimumSize = new Size(S(32), S(32));
        _btnSettings.MaximumSize = new Size(S(32), S(32));
        _btnSettings.Size = _btnSettings.MinimumSize;
        _btnSettings.Paint += (_, e) => DrawSettingsIcon(e.Graphics, _btnSettings.ClientRectangle);
        _settingsMenu.BackColor = Theme.Surface2;
        _settingsMenu.ForeColor = Theme.Text;
        _settingsMenu.ShowImageMargin = false;
        _settingsMenu.ShowCheckMargin = false;
        _settingsMenu.Padding = new Padding(S(1));
        _settingsMenu.Items.Clear();
        void AddSetting(CheckBox box)
        {
            box.Font = Font;
            box.Padding = new Padding(S(12), 0, S(12), 0);
            int textWidth = TextRenderer.MeasureText(box.Text, Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
            box.Size = new Size(textWidth + S(80), S(38));
            box.FlatAppearance.MouseOverBackColor = Theme.Surface2;
            box.FlatAppearance.MouseDownBackColor = Theme.Surface2;
            box.FlatAppearance.CheckedBackColor = Theme.Surface2;
            _settingsMenu.Items.Add(new ToolStripControlHost(box)
            {
                AutoSize = false,
                Size = box.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
            });
        }
        AddSetting(_confirmDelete);
        AddSetting(_lineNumbers);
        AddSetting(_wordWrap);

        bar.Controls.Add(_btnNew, 0, 0);
        bar.Controls.Add(_btnViewAll, 1, 0);
        bar.Controls.Add(formatting, 2, 0);
        bar.Controls.Add(_fontFamily, 3, 0);
        bar.Controls.Add(_fontSize, 4, 0);
        bar.Controls.Add(_btnBack, 5, 0);
        bar.Controls.Add(_btnFore, 6, 0);
        bar.Controls.Add(_btnSettings, 7, 0);

        // ---- panneau latéral (gauche) ----
        _search = DarkTextBox();
        _search.PlaceholderText = "Rechercher (Ctrl+F)";
        _search.AccessibleName = "Filtrer les notes";

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface, Padding = new Padding(S(8), S(8), S(4), S(8)) };
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _list.Dock = DockStyle.Fill;
        _list.HorizontalScrollbar = false;
        var listHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Margin = _list.Margin,
        };
        _list.Margin = Padding.Empty;
        listHost.Controls.Add(_list);
        listHost.Controls.Add(_title);
        left.Controls.Add(WithBorder(_search), 0, 0);
        left.Controls.Add(listHost, 0, 1);

        // ---- zone de note (droite) ----
        _body.BorderStyle = BorderStyle.None;
        _body.BackColor = ColorExt.ToColor(Theme.DefaultNoteBack, Theme.Window);
        _body.ForeColor = ColorExt.ToColor(Theme.DefaultNoteFore, Theme.Text);
        _gutter = new LineNumberGutter(_body) { Visible = false };
        _gutter.Attach();
        SetBodyFont(new Font("Segoe UI", 11f));
        ApplyGutterFont();
        _body.AcceptsTab = true;
        _body.WordWrap = _wordWrap.Checked;
        _body.ScrollBars = RichTextBoxScrollBars.Both;
        _body.HideSelection = false;
        _body.AccessibleName = "Contenu de la note";
        _body.Dock = DockStyle.Fill;
        BuildBodyMenu();
        BuildTabMenu();

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Window, Padding = new Padding(S(10), S(8), S(8), S(8)) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // Ligne de la barre d'onglets : hauteur calculée sur la police de la barre, tenue à jour ensuite.
        var tabsRow = new RowStyle(SizeType.Absolute, _tabs.PreferredStripHeight);
        right.RowStyles.Add(tabsRow);
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Hôte de la barre : le champ de titre d'onglet y est posé librement sur l'onglet ciblé,
        // comme le champ de titre de note au-dessus de la liste.
        var tabsHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Margin = new Padding(S(3), 0, S(3), 0) };
        _tabs.Margin = Padding.Empty;
        _tabs.Dock = DockStyle.Fill;
        tabsHost.Controls.Add(_tabs);
        tabsHost.Controls.Add(_tabTitle);
        right.Controls.Add(tabsHost, 0, 0);
        void SizeTabsRow() => tabsRow.Height = _tabs.Visible ? _tabs.PreferredStripHeight : 0;
        _tabs.SizeChanged += (_, _) => SizeTabsRow();
        _tabs.VisibleChanged += (_, _) => SizeTabsRow();
        right.Controls.Add(NoteBox(_body, _gutter), 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterWidth = S(6),
            Panel1MinSize = S(230),
            FixedPanel = FixedPanel.Panel1,
            BackColor = Theme.Border,
        };
        split.Panel1.BackColor = Theme.Surface;
        split.Panel2.BackColor = Theme.Window;
        split.Panel1.Padding = new Padding(0);
        split.Panel2.Padding = new Padding(0);
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);
        _split = split;

        // ---- barre d'état (bas) ----
        _lblStatus.ForeColor = Theme.TextDim;
        _lblStatus.Spring = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _status.Dock = DockStyle.Bottom;
        _status.BackColor = Theme.Surface;
        _status.SizingGrip = false;
        _status.Items.Add(_lblStatus);

        // ordre d'ajout : Fill d'abord, puis bas, puis haut
        Controls.Add(split);
        Controls.Add(_status);
        Controls.Add(bar);

        try { split.SplitterDistance = S(300); } catch { /* taille hors bornes */ }
    }

    private SplitContainer _split = null!;
    private TableLayoutPanel _bar = null!;

    private void BuildBodyMenu()
    {
        _bodyMenu.BackColor = Theme.Surface2;
        _bodyMenu.ForeColor = Theme.Text;
        _bodyMenu.ShowImageMargin = false;
        _bodyMenu.Renderer = new DarkMenuRenderer();
        var undo = new ToolStripMenuItem("Annuler", null, (_, _) => EditFromMenu(_body.Undo));
        var redo = new ToolStripMenuItem("Rétablir", null, (_, _) => EditFromMenu(_body.Redo));
        var cut = new ToolStripMenuItem("Couper", null, (_, _) => EditFromMenu(_body.Cut));
        var copy = new ToolStripMenuItem("Copier", null, (_, _) => _body.Copy());
        var paste = new ToolStripMenuItem("Coller", null, (_, _) => EditFromMenu(_body.Paste));
        var selectAll = new ToolStripMenuItem("Tout sélectionner", null, (_, _) => _body.SelectAll());
        _bodyMenu.Items.AddRange(new ToolStripItem[]
        {
            undo, redo, new ToolStripSeparator(), cut, copy, paste, new ToolStripSeparator(), selectAll,
        });
        _bodyMenu.Opening += (_, e) =>
        {
            if (_current is null) { e.Cancel = true; return; }
            undo.Enabled = _body.CanUndo;
            redo.Enabled = _body.CanRedo;
            cut.Enabled = copy.Enabled = _body.SelectionLength > 0;
            paste.Enabled = _body.CanPaste(DataFormats.GetFormat(DataFormats.Rtf))
                || _body.CanPaste(DataFormats.GetFormat(DataFormats.UnicodeText));
            selectAll.Enabled = _body.TextLength > 0;
        };
        _body.ContextMenuStrip = _bodyMenu;
    }

    private void EditFromMenu(Action edit)
    {
        edit();
        _richTextDirty = true;
        MarkDirty();
        UpdateFormattingButtons();
    }

    private void BuildTabMenu()
    {
        _tabMenu.BackColor = Theme.Surface2;
        _tabMenu.ForeColor = Theme.Text;
        _tabMenu.ShowImageMargin = false;
        _tabMenu.Renderer = new DarkMenuRenderer();
        var add = new ToolStripMenuItem("Nouvel onglet", null, (_, _) => AddTab());
        var rename = new ToolStripMenuItem("Renommer", null, (_, _) =>
        {
            _pendingTabRename = _tabMenuIndex;
            if (!_tabMenu.Visible) StartPendingTabRename();   // menu déjà refermé : on peut lancer
        });
        // Le menu rend le focus à la barre en se fermant ; le renommage part après ce message.
        _tabMenu.Closed += (_, _) => { if (_pendingTabRename >= 0) StartPendingTabRename(); };
        var remove = new ToolStripMenuItem("Supprimer l'onglet", null, (_, _) => RemoveTab(_tabMenuIndex));
        var left = new ToolStripMenuItem("Déplacer à gauche", null, (_, _) => MoveTab(_tabMenuIndex, _tabMenuIndex - 1));
        var right = new ToolStripMenuItem("Déplacer à droite", null, (_, _) => MoveTab(_tabMenuIndex, _tabMenuIndex + 1));
        _tabMenu.Items.AddRange(new ToolStripItem[]
        {
            add, new ToolStripSeparator(), rename, remove, new ToolStripSeparator(), left, right,
        });
        _tabMenu.Opening += (_, e) =>
        {
            var tabs = _current?.Tabs;
            if (tabs is null || _tabMenuIndex < 0 || _tabMenuIndex >= tabs.Count) { e.Cancel = true; return; }
            rename.Enabled = true;
            remove.Enabled = tabs.Count > 1;   // il reste toujours au moins un onglet
            left.Enabled = _tabMenuIndex > 0;
            right.Enabled = _tabMenuIndex < tabs.Count - 1;
        };
    }

    private void StartPendingTabRename()
    {
        int index = _pendingTabRename;
        _pendingTabRename = -1;
        if (index < 0) return;
        BeginInvoke((Action)(() => { if (!IsDisposed) BeginTabRename(index); }));
    }

    private void ShowTabMenu(int index, Point screen)
    {
        _tabMenuIndex = index;
        _tabMenu.Show(screen);
    }

    private Button FlatButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(S(16), S(5), S(16), S(5)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface2,
            ForeColor = Theme.Text,
            Margin = new Padding(S(3), 0, S(3), 0),
            MinimumSize = new Size(0, S(32)),
            MaximumSize = new Size(0, S(32)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            TabStop = false,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        b.FlatAppearance.MouseOverBackColor = Theme.Selection;
        b.FlatAppearance.MouseDownBackColor = Theme.Accent;
        return b;
    }

    private Button Swatch(string hex)
    {
        var b = FlatButton("");
        b.AutoSize = false;
        b.Size = new Size(S(42), S(32));
        b.MinimumSize = new Size(S(42), S(32));
        b.MaximumSize = new Size(S(42), S(32));
        b.BackColor = ColorExt.ToColor(hex, Theme.Surface2);
        b.TabStop = false;
        return b;
    }

    private ColorButton ColorSelector(string hex, bool textColor)
    {
        var b = new ColorButton
        {
            IsTextColor = textColor,
            SelectedColor = ColorExt.ToColor(hex, Theme.Text),
            Size = new Size(S(42), S(32)),
            MinimumSize = new Size(S(42), S(32)),
            MaximumSize = new Size(S(42), S(32)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(S(3), 0, S(3), 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Surface2,
            ForeColor = Theme.Text,
            TabStop = false,
        };
        b.FlatAppearance.BorderColor = Theme.Border;
        b.FlatAppearance.MouseOverBackColor = Theme.Selection;
        b.FlatAppearance.MouseDownBackColor = Theme.Accent;
        return b;
    }

    private TextBox DarkTextBox() => new()
    {
        BackColor = Theme.Surface2,
        ForeColor = Theme.Text,
        BorderStyle = BorderStyle.None,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Font = new Font("Segoe UI", 9.5f),
    };

    private void DrawSettingsIcon(Graphics graphics, Rectangle bounds)
    {
        float cx = bounds.Width / 2f, cy = bounds.Height / 2f;
        float outer = S(10), inner = S(8);
        var points = Enumerable.Range(0, 32).Select(i =>
        {
            double angle = i * Math.PI / 16;
            float radius = i % 4 < 2 ? outer : inner;
            return new PointF(cx + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
        }).ToArray();
        var old = graphics.SmoothingMode;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Text, Math.Max(1f, DeviceDpi / 96f * 1.3f));
        graphics.DrawPolygon(pen, points);
        float hole = S(3);
        graphics.DrawEllipse(pen, cx - hole, cy - hole, hole * 2, hole * 2);
        graphics.SmoothingMode = old;
    }

    /// <summary>Entoure un contrôle sans bordure d'un liseré sombre (faux cadre).</summary>
    private Panel WithBorder(Control inner)
    {
        var p = new Panel
        {
            BackColor = inner.BackColor,
            Padding = new Padding(S(1)),
            Dock = DockStyle.Fill,
            Margin = new Padding(S(3), 0, S(3), 0),
        };
        p.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, p.ClientSize.Width - 1, p.ClientSize.Height - 1);
        };
        inner.Margin = new Padding(0);
        p.Controls.Add(inner);
        if (inner is TextBox)
        {
            inner.Dock = DockStyle.None;
            p.Layout += (_, _) => inner.SetBounds(S(6), Math.Max(S(1), (p.ClientSize.Height - inner.PreferredSize.Height) / 2),
                Math.Max(0, p.ClientSize.Width - S(12)), inner.PreferredSize.Height);
        }
        return p;
    }

    /// <summary>Cadre de la note : liseré sombre, numéros de ligne à gauche, texte à droite.</summary>
    private Panel NoteBox(RichTextBox body, LineNumberGutter gutter)
    {
        _noteBox = new Panel
        {
            BackColor = body.BackColor,
            Padding = new Padding(S(1)),
            Dock = DockStyle.Fill,
            Margin = new Padding(S(3), 0, S(3), 0),
        };
        _noteBox.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, _noteBox.ClientSize.Width - 1, _noteBox.ClientSize.Height - 1);
        };
        body.Margin = new Padding(0);
        body.Dock = DockStyle.Fill;
        gutter.Dock = DockStyle.Left;
        // ordre d'ajout : le texte d'abord, la colonne ensuite, pour qu'elle prenne la gauche
        _noteBox.Controls.Add(body);
        _noteBox.Controls.Add(gutter);
        return _noteBox;
    }

    private void StyleCombo(ComboBox c)
    {
        c.FlatStyle = FlatStyle.Flat;
        c.BackColor = Theme.Surface2;
        c.ForeColor = Theme.Text;
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        c.Margin = new Padding(S(3), 0, S(3), 0);
    }

    private static Icon MakeIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("PriseDeNotes.AppIcon.ico")
            ?? throw new InvalidOperationException("Icône de l'application introuvable.");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }

    // ─────────────────────────── Événements ───────────────────────────

    private void WireEvents()
    {
        _btnNew.Click += (_, _) => NewNote();
        _btnViewAll.Click += (_, _) => ShowAllNotes();
        _btnBack.Click += (_, _) => PickColor(_btnBack, "Couleur de fond de la note");
        _btnFore.Click += (_, _) => PickColor(_btnFore, "Couleur de police de la note");
        _btnSettings.Click += (_, _) => _settingsMenu.Show(_btnSettings,
            new Point(_btnSettings.Width, _btnSettings.Height), ToolStripDropDownDirection.BelowLeft);
        _confirmDelete.CheckedChanged += (_, _) => SaveUiState();
        _lineNumbers.CheckedChanged += (_, _) => { ApplyLineNumbers(); SaveUiState(); };
        _wordWrap.CheckedChanged += (_, _) =>
        {
            _body.WordWrap = _wordWrap.Checked;
            _gutter.RefreshNumbers();
            SaveUiState();
        };

        _tabs.AccessibleName = "Onglets de la note";
        _tabs.TabSelected += SelectTab;
        _tabs.AddRequested += AddTab;
        _tabs.MoveRequested += MoveTab;
        _tabs.ContextRequested += ShowTabMenu;
        _tabs.RenameRequested += BeginTabRename;
        // Un clic dans la barre pendant un renommage l'applique d'abord : la barre reprend ensuite
        // son comportement normal (sélection, glisser, menu) sur un état propre.
        _tabs.MouseDown += (_, _) => { if (_tabRenaming is not null) CommitTabRename(); };
        _tabs.MouseMove += Tabs_MouseMove;
        _tabs.MouseLeave += (_, _) => { _lastTipIndex = -1; _dateTip.HideTip(); };

        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            SelectNote(_list.SelectedIndex >= 0 && _list.SelectedIndex < _view.Count ? _view[_list.SelectedIndex] : null);
        };

        _list.MouseMove += List_MouseMove;
        _list.MouseLeave += (_, _) => { _lastTipIndex = -1; _dateTip.HideTip(); };
        _list.DeleteRequested += note => { _dateTip.HideTip(); SelectNote(note); DeleteNote(); };
        _list.RenameRequested += note =>
        {
            _dateTip.HideTip();
            SelectNote(note);
            // Attendre la fin du clic natif de la liste avant de donner le focus au champ.
            BeginInvoke((Action)(() =>
            {
                if (!IsDisposed && ReferenceEquals(_current, note)) BeginRename();
            }));
        };
        _list.MoveRequested += MoveNote;
        _list.ViewportChanging += (_, _) =>
        {
            _lastTipIndex = -1;
            _dateTip.HideTip();
            if (!_title.Visible) return;
            Commit();
            _title.Hide();
        };

        _search.TextChanged += (_, _) => RebuildView(_current);

        _title.TextChanged += (_, _) =>
        {
            if (!_loading && _title.Visible) _manualTitleChanged = true;
        };
        _title.Leave += (_, _) => { Commit(); _title.Hide(); };
        _title.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                _title.Hide();
                _list.Focus();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _title.Text = _current?.Title ?? "";
                _manualTitleChanged = false;
                _title.Hide();
                _list.Focus();
                e.SuppressKeyPress = true;
            }
        };

        // Titre d'onglet : mêmes règles que le titre de note — Entrée valide, Échap annule,
        // un clic ailleurs applique aussi. Le champ est lié à l'onglet ouvert (_tabRenaming).
        _tabTitle.Leave += (_, _) =>
        {
            var tab = _tabRenaming;
            if (tab is null) return;
            // BeginInvoke : on attend la fin du changement de focus pour savoir où il est allé.
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || !ReferenceEquals(_tabRenaming, tab) || _tabTitle.Focused) return;
                // La barre ne prend jamais le focus pendant un renommage (un clic dessus l'applique d'abord) :
                // si elle l'a, c'est qu'il lui a été rendu par programme (fermeture d'un menu). Le champ le reprend.
                if (_tabs.Focused)
                {
                    _tabTitle.Focus();
                    _tabTitle.SelectAll();
                    return;
                }
                CommitTabRename();
            }));
        };
        _tabTitle.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitTabRename();
                _tabTitle.Hide();
                _body.Focus();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                _tabRenaming = null;
                _tabs.IsRenaming = false;
                _tabTitle.Hide();
                _body.Focus();
                e.SuppressKeyPress = true;
            }
        };

        _body.TextChanged += (_, _) =>
        {
            _gutter.RefreshNumbers();
            if (_loading) return;
            _richTextDirty = true;
            UpdateAutomaticTitle();
            MarkDirty();
        };
        _body.SelectionChanged += (_, _) => { UpdateFormattingButtons(); UpdateFontSelectors(); };
        _body.Leave += (_, _) => Commit();

        _tip.SetToolTip(_btnNew, "Nouvelle note  (Ctrl+N)");
        _tip.SetToolTip(_btnViewAll, "Voir toutes les notes");
        _tip.SetToolTip(_btnBack, "Couleur de fond de la note");
        _tip.SetToolTip(_btnFore, "Couleur de police : la sélection, sinon toute la note");
        _tip.SetToolTip(_fontFamily, "Police : la sélection, sinon toute la note");
        _tip.SetToolTip(_fontSize, "Taille : la sélection, sinon toute la note");
        _tip.SetToolTip(_btnSettings, "Paramètres");
        _fontFamily.SelectedIndexChanged += (_, _) => { if (_loading || _syncingFont) return; ApplyFont(); MarkDirty(); };
        _fontSize.SelectedIndexChanged += (_, _) => { if (_loading || _syncingFont) return; ApplyFont(); MarkDirty(); };

        _autosave.Tick += (_, _) =>
        {
            _autosave.Stop();
            Commit();
            if (_unsaved.Count > 0)
            {
                RetryUnsaved();
                UpdateStatus();
            }
        };

        Deactivate += (_, _) => { _dateTip.HideTip(); CloseTabRename(); };
        Move += (_, _) => { _dateTip.HideTip(); CloseTabRename(); };
        Resize += (_, _) => { _dateTip.HideTip(); CloseTabRename(); };

        FormClosing += (_, e) =>
        {
            Commit();
            RetryUnsaved();
            _dateTip.HideTip();
            if (_unsaved.Count > 0)
            {
                var answer = MessageBox.Show(this,
                    $"{_unsaved.Count} note(s) n'ont pas pu être enregistrées :\n{_saveError}\n\n"
                    + "Fermer quand même ? Les modifications non enregistrées seront perdues.",
                    "Enregistrement impossible", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) { e.Cancel = true; return; }
            }
            SaveUiState();
        };
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.N: NewNote(); return true;
            case Keys.Control | Keys.S: _autosave.Stop(); Commit(); UpdateStatus("Enregistrée"); return true;
            // Aucun raccourci clavier ne supprime une note : seule la corbeille de la liste le fait,
            // pour qu'une touche pressée par erreur pendant la saisie ne puisse rien effacer.
            case Keys.Control | Keys.F: _search.Focus(); _search.SelectAll(); return true;
            case Keys.Control | Keys.T: AddTab(); return true;
            default: return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private void List_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_list.HitTestAction(e.Location) != NoteListAction.None)
        {
            _lastTipIndex = -1;
            _dateTip.HideTip();
            return;
        }
        int idx = _list.IndexFromPoint(e.Location);
        if (idx < 0 || idx >= _view.Count)
        {
            if (_lastTipIndex != -1) { _lastTipIndex = -1; _dateTip.HideTip(); }
            return;
        }
        if (idx == _lastTipIndex) return;
        _lastTipIndex = idx;

        var n = _view[idx];
        var text =
            $"{n.Title}\n" +
            $"Créée le  : {n.CreatedAt:dddd dd/MM/yyyy 'à' HH:mm}\n" +
            $"Modifiée le : {n.ModifiedAt:dddd dd/MM/yyyy 'à' HH:mm}\n" +
            $"{n.Tabs.Count} onglet{(n.Tabs.Count == 1 ? "" : "s")} · " +
            $"{n.TotalChars} caractères · {n.TotalWords} mots\n" +
            $"Fond {n.BackColor} · Police {n.FontFamily} {n.FontSize:0.#} · {n.ForeColor}";
        _dateTip.ShowFor(text, _list.PointToScreen(new Point(e.X + 22, e.Y + 26)));
    }

    // ─────────────────────────── Notes ───────────────────────────

    private void SeedFirstNote()
    {
        var n = new Note { Title = "Bienvenue", CreatedAt = DateTime.Now, ModifiedAt = DateTime.Now };
        n.EnsureTabs();
        n.Tabs[0].Title = "Onglet 1";
        n.Tabs[0].Content =
            "Prise de notes — thème sombre.\n\n" +
            "· Gauche : la liste des titres. Survolez un titre pour voir les dates.\n" +
            "· Droite : le texte de la note, découpée en onglets.\n" +
            "· Le crayon dans la liste permet de renommer une note.\n" +
            "· En haut : police, taille, puis les deux pastilles couleur (fond de la note / couleur de la police).\n" +
            "· Sélectionnez du texte pour ne changer que ce passage ; sans sélection, c'est toute la note.\n\n" +
            "Onglets : le « + » à droite de la barre ajoute un onglet, un clic l'affiche,\n" +
            "le glisser-déposer le déplace, le clic droit le renomme ou le supprime.\n\n" +
            "Raccourcis : Ctrl+N nouvelle note · Ctrl+S enregistrer · Ctrl+F rechercher ·\n" +
            "Ctrl+T nouvel onglet.";
        TrySave(n);
        _all.Add(n);
    }

    private void RebuildView(Note? keep)
    {
        Commit();
        int top = _list.Items.Count > 0 ? _list.TopIndex : 0;
        var q = _search.Text.Trim();
        _view.Clear();
        _view.AddRange(_all.Where(n => q.Length == 0 || n.Matches(q)));

        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var n in _view) _list.Items.Add(n);
        _lastTipIndex = -1;
        _dateTip.HideTip();

        int idx = keep is null ? 0 : _view.IndexOf(keep);
        if (idx < 0) idx = 0;

        _list.SelectedIndex = _view.Count == 0 ? -1 : idx;
        if (_view.Count > 0 && ReferenceEquals(keep, _current))
            _list.TopIndex = Math.Min(top, _view.Count - 1);
        _list.EndUpdate();
        _loading = false;
        SelectNote(_view.Count == 0 ? null : _view[idx]);
        UpdateStatus();
    }

    private void MoveNote(Note note, int target)
    {
        int from = _view.IndexOf(note);
        if (from < 0 || target < 0 || target >= _view.Count || from == target) return;
        Commit();
        // En recherche, seules les places des notes visibles sont réordonnées.
        var visible = _view.ToHashSet();
        var reordered = _view.ToList();
        reordered.RemoveAt(from);
        reordered.Insert(target, note);
        int next = 0;
        for (int i = 0; i < _all.Count; i++)
            if (visible.Contains(_all[i])) _all[i] = reordered[next++];
        RebuildView(note);
        SaveUiState();
    }

    private void SelectNote(Note? n)
    {
        if (n is not null && ReferenceEquals(_current, n)) return;
        Commit();
        _title.Hide();
        _tabTitle.Hide();
        if (n is not null) n.EnsureTabs();
        _current = n;
        _loading = true;
        _manualTitleChanged = false;

        bool has = n is not null;
        _title.Enabled = has;
        _body.Enabled = has;
        _fontFamily.Enabled = has;
        _fontSize.Enabled = has;
        _btnBack.Enabled = has;
        _btnFore.Enabled = has;

        if (n is null)
        {
            _title.Text = "";
            _body.Clear();
            _tabs.SetTabs(null, -1);
            _tabs.Visible = false;
            _loading = false;
            _richTextDirty = false;
            UpdateFormattingButtons();
            UpdateStatus();
            return;
        }

        _title.Text = n.Title;
        _body.BackColor = ColorExt.ToColor(n.BackColor, Theme.Window);
        _body.ForeColor = ColorExt.ToColor(n.ForeColor, Theme.Text);
        ApplyNoteBackground();
        SetBodyFont(FontFor(n));

        _noteFamily = AvailableFont(n.FontFamily);
        _noteSize = Math.Clamp(n.FontSize, 6f, 72f);
        _noteFore = ColorExt.ToColor(n.ForeColor, Theme.Text);
        ShowFontSelectors();
        ApplyGutterFont();

        _btnBack.SelectedColor = ColorExt.ToColor(n.BackColor, Theme.Window);
        _btnFore.SelectedColor = _noteFore;

        _loading = false;
        RefreshTabs();
        LoadTabContent();
        UpdateStatus();
    }

    private Font FontFor(Note n) =>
        new(AvailableFont(n.FontFamily), Math.Clamp(n.FontSize, 6f, 72f), FontStyle.Regular);

    /// <summary>La police demandée si elle est installée, sinon Segoe UI, sinon la première police du système.</summary>
    private string AvailableFont(string? family)
    {
        if (family is not null && _fontSet.Contains(family)) return family;
        if (_fontSet.Contains("Segoe UI") || _fontNames.Length == 0) return "Segoe UI";
        return _fontNames[0];
    }

    private static string[] LoadFontNames()
    {
        using var collection = new InstalledFontCollection();
        var families = collection.Families;
        try { return families.Select(f => f.Name).ToArray(); }
        finally { foreach (var f in families) f.Dispose(); }
    }

    // ─────────────────────────── Onglets ───────────────────────────

    /// <summary>Recopie la barre d'onglets sur la note courante (couleurs comprises).</summary>
    private void RefreshTabs()
    {
        // Pendant le renommage : ne pas interrompre l'édition en cours
        if (_tabRenaming is not null) return;
        _tabTitle.Hide();
        var n = _current;
        _tabs.Visible = n is not null;
        _tabs.Enabled = n is not null;
        _tabs.SetActiveColors(
            ColorExt.ToColor(n?.BackColor ?? Theme.DefaultNoteBack, Theme.Window),
            _noteFore);
        _tabs.SetTabs(n?.Tabs, n is null ? -1 : Math.Max(0, n.IndexOfTab(n.ActiveTabId)));
    }

    /// <summary>Charge le texte de l'onglet actif dans l'éditeur, sans mise en forme résiduelle.</summary>
    private void LoadTabContent()
    {
        var n = _current;
        var tab = n?.ActiveTab;
        _loading = true;
        try
        {
            _body.Clear();
            _body.SelectionAlignment = HorizontalAlignment.Left;
            if (n is not null)
            {
                using (var initialFont = FontFor(n)) _body.SelectionFont = initialFont;
                _body.SelectionColor = ColorExt.ToColor(n.ForeColor, Theme.Text);
            }
            _body.Text = tab?.Content ?? "";
            if (!string.IsNullOrEmpty(tab?.RichText))
            {
                try { _body.Rtf = tab.RichText; }
                catch (ArgumentException) { _body.Text = tab.Content; }
            }
            _body.SelectionStart = 0;
            _body.SelectionLength = 0;
            _body.ClearUndo();
            _richTextDirty = false;
        }
        finally { _loading = false; }
        _gutter.RefreshNumbers();
        UpdateFormattingButtons();
        UpdateFontSelectors();
    }

    private void SelectTab(int index)
    {
        var n = _current;
        if (n is null || index < 0 || index >= n.Tabs.Count) return;
        if (n.Tabs[index].Id == n.ActiveTabId) return;
        Commit();
        n.ActiveTabId = n.Tabs[index].Id;
        LoadTabContent();
        // Afficher un autre onglet n'est pas une modification : la date de la note ne bouge pas.
        TrySave(n, touch: false);
        UpdateStatus($"Onglet « {n.Tabs[index].Title} »");
    }

    private void AddTab()
    {
        var n = _current;
        if (n is null) return;
        Commit();
        var tab = n.AddTab();
        TrySave(n);
        LoadTabContent();
        RefreshTabs();
        BeginTabRename(n.IndexOfTab(tab.Id));
        UpdateStatus("Onglet ajouté");
    }

    private void RemoveTab(int index)
    {
        var n = _current;
        if (n is null || n.Tabs.Count <= 1) return;   // le dernier onglet reste
        if (index < 0 || index >= n.Tabs.Count) return;
        Commit();
        var tab = n.Tabs[index];
        if (_confirmDelete.Checked && tab.Content.Trim().Length > 0)
        {
            var answer = MessageBox.Show(this,
                $"Supprimer définitivement l'onglet « {tab.Title} » de la note « {n.Title} » ?",
                "Suppression d'un onglet", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        _autosave.Stop();
        n.Tabs.RemoveAt(index);
        if (n.IndexOfTab(n.ActiveTabId) < 0) n.ActiveTabId = n.Tabs[Math.Min(index, n.Tabs.Count - 1)].Id;
        TrySave(n);
        LoadTabContent();
        RefreshTabs();
        UpdateStatus("Onglet supprimé");
    }

    private void MoveTab(int from, int target)
    {
        var n = _current;
        if (n is null) return;
        Commit();
        n.MoveTab(from, target);
        TrySave(n);
        RefreshTabs();
        UpdateStatus("Onglet déplacé");
    }

    /// <summary>Ferme le champ de titre d'onglet ouvert (fenêtre déplacée, redimensionnée, séparateur bougé…).</summary>
    private void CloseTabRename()
    {
        if (_tabTitle.Visible) CommitTabRename();
    }

    private void BeginTabRename(int index)
    {
        var n = _current;
        if (n is null || index < 0 || index >= n.Tabs.Count) return;
        // Toujours repartir de l'onglet demandé : un champ ouvert appartient à un autre onglet,
        // le garder ferait écrire son texte sur le mauvais onglet.
        CommitTabRename();
        Commit();
        var tab = n.Tabs[index];
        _tabRenaming = tab;
        _tabs.IsRenaming = true;
        _tabTitle.Text = tab.Title;
        _tabTitle.BackColor = ColorExt.ToColor(n.BackColor, Theme.Surface2);
        _tabTitle.ForeColor = ColorExt.ToColor(n.ForeColor, Theme.Text);
        var bounds = _tabs.TabBounds(index);
        _tabTitle.Bounds = new Rectangle(_tabs.Left + bounds.X,
            _tabs.Top + bounds.Y + (bounds.Height - _tabTitle.PreferredHeight) / 2,
            Math.Max(S(70), bounds.Width - S(6)), _tabTitle.PreferredHeight);
        _tabTitle.Show();
        _tabTitle.BringToFront();
        // Focus donné après la fin du message en cours (double-clic, clic sur « + »), comme pour le titre de note.
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || !ReferenceEquals(_tabRenaming, tab)) return;
            _tabTitle.Focus();
            _tabTitle.SelectAll();
        }));
    }

    /// <summary>Écrit le titre saisi dans l'onglet qui a ouvert le champ, puis enregistre sa note.</summary>
    private void CommitTabRename()
    {
        var tab = _tabRenaming;
        _tabRenaming = null;
        _tabs.IsRenaming = false;
        _tabTitle.Hide();   // le champ ne survit jamais à la fin du renommage
        if (tab is null) return;
        // On écrit uniquement si l'onglet existe encore, dans la note qui le possède — jamais dans
        // celle qui se trouve affichée à ce moment-là.
        var owner = _all.FirstOrDefault(n => n.Tabs.Contains(tab));
        if (owner is null) return;
        var title = _tabTitle.Text.Trim();
        if (title.Length > 0 && title != tab.Title)
        {
            tab.Title = title;
            TrySave(owner);
            UpdateStatus($"Onglet « {title} »");
        }
        RefreshTabs();
    }

    private void Tabs_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_tabRenaming is not null)
        {
            _lastTipIndex = -1;
            _dateTip.HideTip();
            _tip.Hide(_tabs);
            return;
        }
        if (_tabs.PlusHitTest(e.Location))
        {
            _lastTipIndex = -1;
            _dateTip.HideTip();
            _tip.Show("Nouvel onglet  (Ctrl+T)", _tabs, e.X + 18, e.Y + 26, 4000);
            return;
        }
        _tip.Hide(_tabs);

        int index = _tabs.IndexFromPoint(e.Location);
        if (_current is null || index < 0 || index >= _current.Tabs.Count)
        {
            if (_lastTipIndex != -1) { _lastTipIndex = -1; _dateTip.HideTip(); }
            return;
        }
        if (index == _lastTipIndex) return;
        _lastTipIndex = index;

        var tab = _current.Tabs[index];
        var text =
            $"{tab.Title}\n" +
            $"Note : {_current.Title}\n" +
            $"{tab.Content.Length} caractères · {Note.CountWords(tab.Content)} mots\n" +
            $"Modifiée le : {_current.ModifiedAt:dddd dd/MM/yyyy 'à' HH:mm}";
        _dateTip.ShowFor(text, _tabs.PointToScreen(new Point(e.X + 22, e.Y + 26)));
    }

    /// <summary>
    /// Applique la police choisie : sur la sélection s'il y en a une, sinon sur toute la note,
    /// qui devient alors la police de base (le texte sans mise en forme, et la saisie suivante).
    /// </summary>
    private void ApplyFont()
    {
        if (_current is null) return;
        var family = _fontFamily.SelectedItem?.ToString() ?? _noteFamily;
        float size = _noteSize;
        if (_fontSize.SelectedItem is not null &&
            float.TryParse(_fontSize.SelectedItem.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            size = s;
        var available = AvailableFont(family);
        size = Math.Clamp(size, 6f, 72f);

        bool toSelection = _body.SelectionLength > 0;
        RichTextFormatting.SetFont(_body, available, size, toSelection);
        _richTextDirty = true;
        if (!toSelection)
        {
            _noteFamily = available;
            _noteSize = size;
            SetBodyFont(new Font(available, size, FontStyle.Regular));
            ApplyGutterFont();
        }
        UpdateFormattingButtons();
        _body.Focus();
    }

    /// <summary>Aligne les deux listes du haut sur la police de base de la note.</summary>
    private void ShowFontSelectors()
    {
        _syncingFont = true;
        try
        {
            AddItem(_fontFamily, _noteFamily);
            AddItem(_fontSize, _noteSize.ToString("0.#", CultureInfo.InvariantCulture));
        }
        finally { _syncingFont = false; }
    }

    /// <summary>
    /// Rappelle aux listes du haut la police du texte sélectionné (ou du point d'insertion),
    /// pour que le choix suivant parte de là. Un passage au formatage mélangé laisse les listes telles quelles.
    /// </summary>
    private void UpdateFontSelectors()
    {
        if (_loading || _current is null) return;
        var (family, points) = RichTextFormatting.GetFont(_body);
        _syncingFont = true;
        try
        {
            if (family is not null)
                AddItem(_fontFamily, family);
            if (points is not null)
                AddItem(_fontSize, NearestSize((float)points).ToString("0.#", CultureInfo.InvariantCulture));
        }
        finally { _syncingFont = false; }

        var color = RichTextFormatting.GetColor(_body);
        if (color.HasValue)
            _btnFore.SelectedColor = color.Value;
        else if (_body.SelectionLength == 0)
            _btnFore.SelectedColor = _noteFore;
    }

    /// <summary>
    /// RichEdit rend 11 pt en 11,25 (arrondis GDI) : on revient à la taille proposée la plus proche,
    /// sauf taille venue d'ailleurs (collage RTF), conservée telle quelle.
    /// </summary>
    private static float NearestSize(float points)
    {
        float best = Sizes[0];
        foreach (var s in Sizes)
            if (Math.Abs(s - points) < Math.Abs(best - points)) best = s;
        return Math.Abs(points - best) <= 0.75f ? best : points;
    }

    /// <summary>
    /// Sélectionne <paramref name="value"/>. Une valeur hors liste (police absente, taille collée en RTF)
    /// est ajoutée en un seul exemplaire, retiré dès qu'on sélectionne autre chose : la liste ne grossit plus.
    /// </summary>
    private static void AddItem(ComboBox combo, string value)
    {
        if (combo.Tag is string extra && extra != value)
        {
            combo.Items.Remove(extra);
            combo.Tag = null;
        }
        if (!combo.Items.Contains(value))
        {
            combo.Items.Add(value);
            combo.Tag = value;
        }
        combo.SelectedItem = value;
    }

    private static void DrawAlignmentIcon(Graphics graphics, Rectangle bounds, HorizontalAlignment alignment, Color color)
    {
        float scale = Math.Min(bounds.Width / 30f, bounds.Height / 32f);
        float width = 16 * scale;
        float left = bounds.Left + (bounds.Width - width) / 2;
        float top = bounds.Top + (bounds.Height - 17 * scale) / 2;
        using var brush = new SolidBrush(color);
        for (int i = 0; i < 4; i++)
        {
            float length = (i % 2 == 0 ? 16 : 10) * scale;
            float offset = alignment == HorizontalAlignment.Right ? width - length
                : alignment == HorizontalAlignment.Center ? (width - length) / 2 : 0;
            graphics.FillRectangle(brush, left + offset, top + i * 5 * scale, length, Math.Max(1, 2 * scale));
        }
    }

    private void ApplyAlignment(HorizontalAlignment alignment)
    {
        if (_loading || _current is null) return;
        // Le RichTextBox applique l'alignement aux paragraphes sélectionnés ou au paragraphe du curseur.
        _body.SelectionAlignment = alignment;
        _richTextDirty = true;
        MarkDirty();
        UpdateFormattingButtons();
        _gutter.RefreshNumbers();
        _body.Focus();
    }

    private void ToggleStyle(FontStyle style)
    {
        if (_loading || _current is null) return;
        RichTextFormatting.ToggleStyle(_body, style);
        _richTextDirty = true;
        MarkDirty();
        UpdateFormattingButtons();
        _body.Focus();
    }

    private void UpdateFormattingButtons()
    {
        if (_loading) return;
        foreach (var (style, button) in _formatButtons)
        {
            button.Enabled = _current is not null;
            button.Checked = _current is not null && RichTextFormatting.HasStyle(_body, style);
            button.FlatAppearance.BorderColor = button.Checked ? Theme.Accent : Theme.Border;
        }
        foreach (var (alignment, button) in _alignmentButtons)
        {
            button.Enabled = _current is not null;
            button.Checked = _current is not null && _body.SelectionAlignment == alignment;
            button.FlatAppearance.BorderColor = button.Checked ? Theme.Accent : Theme.Border;
        }
    }

    private void SetBodyFont(Font font)
    {
        // WinForms conserve l'ancienne instance si les deux polices sont égales.
        if (font.Equals(_body.Font))
        {
            font.Dispose();
            return;
        }
        var old = _bodyFont;
        _body.Font = font;
        _bodyFont = font;
        old?.Dispose();
    }

    /// <summary>
    /// Taille des numéros calée sur la police de base de la note : _body.Font n'est pas fiable
    /// après un EM_SETCHARFORMAT, on repart des réglages de la note.
    /// </summary>
    private void ApplyGutterFont()
    {
        using var font = new Font(_noteFamily, _noteSize, FontStyle.Regular);
        _gutter.UseBaseFont(font);
    }

    /// <summary>Montre ou cache la colonne de numéros de ligne.</summary>
    private void ApplyLineNumbers()
    {
        _gutter.Visible = _lineNumbers.Checked;
        if (_lineNumbers.Checked) ApplyGutterFont();
        _gutter.RefreshNumbers();
    }

    /// <summary>Le cadre de la note et sa colonne de numéros reprennent son fond.</summary>
    private void ApplyNoteBackground()
    {
        _noteBox.BackColor = _body.BackColor;
        _gutter.BackColor = _body.BackColor;
        _gutter.RefreshNumbers();
    }

    private void PickColor(ColorButton swatch, string caption)
    {
        if (_current is null) return;
        int selStart = _body.SelectionStart;
        int selLength = _body.SelectionLength;
        using var dlg = new ColorDialog
        {
            FullOpen = true,
            Color = swatch.SelectedColor,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        swatch.SelectedColor = dlg.Color;
        if (swatch == _btnBack)
        {
            _body.BackColor = dlg.Color;
            ApplyNoteBackground();
            RefreshTabs();
        }
        else if (swatch == _btnFore)
        {
            bool toSelection = selLength > 0;
            if (toSelection)
            {
                _body.Select(selStart, selLength);
                RichTextFormatting.SetColor(_body, dlg.Color, toSelection: true);
                _richTextDirty = true;
            }
            else
            {
                _noteFore = dlg.Color;
                _body.ForeColor = dlg.Color;
                RichTextFormatting.SetColor(_body, dlg.Color, toSelection: false);
                _richTextDirty = true;
                RefreshTabs();
            }
        }
        Commit();
        if (swatch == _btnFore && selLength > 0)
        {
            _body.Select(selStart, selLength);
        }
        _body.Focus();
        UpdateStatus(caption.Split(' ')[0] + " appliquée");
    }

    private void NewNote()
    {
        Commit();
        if (_search.TextLength > 0) _search.Clear();
        var n = new Note { Title = "Nouvelle note", AutoTitle = true };
        n.EnsureTabs();
        TrySave(n);
        _all.Insert(0, n);
        RebuildView(n);
        SaveUiState();
        _body.Focus();
    }

    private void ShowAllNotes()
    {
        Commit();
        _dateTip.HideTip();
        using var overview = new NotesOverviewForm(_all, _store) { Icon = Icon };
        var result = overview.ShowDialog(this);
        if (overview.ImportedNotes.Count > 0)
        {
            // Les notes restaurées sont neuves (identifiants neufs) : on les ajoute en tête de liste,
            // sans relire le disque, ce qui garderait les notes en mémoire et leurs saisies non enregistrées.
            _all.InsertRange(0, overview.ImportedNotes);
            if (_search.TextLength > 0) _search.Clear();
            RebuildView(_current ?? _all.FirstOrDefault());
            SaveUiState();
            UpdateStatus("Notes restaurées");
            _body.Focus();
            return;
        }
        if (result != DialogResult.OK || overview.SelectedNote is not Note note) return;
        _search.Clear();
        RebuildView(note);
        _body.Focus();
    }

    private void UpdateAutomaticTitle()
    {
        if (_current is null || !_current.AutoTitle || _title.Visible) return;
        // Seul le début du texte compte pour le titre : inutile de copier toute la note à chaque frappe.
        var title = Note.TitleFromContent(RichTextFormatting.GetTextStart(_body, 4096));
        _title.Text = title;
        if (_current.Title == title) return;
        _current.Title = title;
        int index = _view.IndexOf(_current);
        if (index >= 0) _list.Invalidate(_list.GetItemRectangle(index));
    }

    private void BeginRename()
    {
        if (_current is null) return;
        if (_title.Visible)
        {
            _title.Focus();
            return;
        }
        Commit();
        _manualTitleChanged = false;
        int index = _list.Items.IndexOf(_current);
        if (index < 0) return;
        _list.SelectedIndex = index;
        var bounds = _list.GetTitleBounds(index);
        _title.Bounds = new Rectangle(_list.Left + bounds.X, _list.Top + bounds.Y - S(2), bounds.Width, _title.PreferredHeight);
        _title.BackColor = ColorExt.ToColor(_current.BackColor, Theme.Surface2);
        _title.ForeColor = ColorExt.ToColor(_current.ForeColor, Theme.Text);
        _title.Show();
        _title.BringToFront();
        _title.Focus();
        _title.SelectAll();
    }

    private void DeleteNote()
    {
        var n = _current;
        if (n is null) return;
        if (_confirmDelete.Checked)
        {
            var answer = MessageBox.Show(this,
                $"Supprimer la note « {n.Title} » ?\n\nSon fichier part dans la corbeille de Windows.",
                "Suppression", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        RemoveNote(n);
    }

    private void RemoveNote(Note n)
    {
        _autosave.Stop();
        try { _store.Delete(n); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // Fichier verrouillé ou suppression annulée : la note reste en place, rien n'est perdu.
            MessageBox.Show(this, "La note n'a pas pu être supprimée :\n" + e.Message,
                "Suppression", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _current = null;
        _all.Remove(n);
        int at = _view.IndexOf(n);
        if (at >= 0) _view.RemoveAt(at);
        var next = _view.Count == 0 ? null : _view[Math.Clamp(at, 0, _view.Count - 1)];
        RebuildView(next);
        SaveUiState();
    }

    /// <summary>Recopie les réglages affichés et le texte de l'onglet actif, puis enregistre.</summary>
    private void Commit()
    {
        if (_loading || _current is null) return;
        var n = _current;
        var tab = n.ActiveTab;
        if (tab is null) return;

        var content = _body.Text;
        bool autoTitle = n.AutoTitle && !_manualTitleChanged;
        var title = autoTitle ? Note.TitleFromContent(content)
            : string.IsNullOrWhiteSpace(_title.Text) ? "Sans titre" : _title.Text.Trim();
        var richText = _richTextDirty ? _body.Rtf : tab.RichText;
        var family = _noteFamily;
        var size = _noteSize;
        var back = _btnBack.SelectedColor.ToHex();
        var fore = _noteFore.ToHex();

        if (n.Title == title && n.AutoTitle == autoTitle && tab.Content == content && tab.RichText == richText &&
            n.FontFamily == family && n.FontSize == size && n.BackColor == back && n.ForeColor == fore)
        {
            _richTextDirty = false;
            _manualTitleChanged = false;
            if (_unsaved.Contains(n)) TrySave(n, touch: false);
            return;
        }

        n.Title = title;
        n.AutoTitle = autoTitle;
        tab.Content = content;
        tab.RichText = richText;
        n.FontFamily = family;
        n.FontSize = size;
        n.BackColor = back;
        n.ForeColor = fore;

        // Même en cas d'échec, la note en mémoire porte la saisie : TrySave la réenregistrera.
        bool saved = TrySave(n);
        _richTextDirty = false;
        _manualTitleChanged = false;

        int i = _view.IndexOf(n);
        if (i >= 0) _list.Invalidate(_list.GetItemRectangle(i));
        UpdateStatus(saved ? "Enregistrée" : null);
    }

    /// <summary>
    /// Enregistre sans jamais lever d'exception : un échec (fichier verrouillé, disque plein…) est signalé
    /// dans la barre d'état, la note est gardée en mémoire et réenregistrée au prochain passage.
    /// </summary>
    private bool TrySave(Note n, bool touch = true)
    {
        try
        {
            _store.Save(n, touch);
            _unsaved.Remove(n);
            if (_unsaved.Count == 0) _saveError = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _unsaved.Add(n);
            _saveError = e.Message;
            UpdateStatus();
            _autosave.Stop();
            _autosave.Start();   // nouvel essai au prochain tic
            return false;
        }
    }

    /// <summary>Réessaie les notes restées non enregistrées (sauf supprimées entre-temps).</summary>
    private void RetryUnsaved()
    {
        foreach (var n in _unsaved.ToList())
        {
            if (!_all.Contains(n)) { _unsaved.Remove(n); continue; }
            TrySave(n, touch: false);
        }
        if (_unsaved.Count == 0) _saveError = null;
    }

    private void MarkDirty()
    {
        if (_loading) return;
        _autosave.Stop();
        _autosave.Start();
    }

    private void UpdateStatus(string? flash = null)
    {
        var parts = new List<string> { $"{_all.Count} note{(_all.Count == 1 ? "" : "s")}" };
        if (_search.Text.Trim().Length > 0) parts.Add($"{_view.Count} affichée{(_view.Count == 1 ? "" : "s")}");
        if (_current is not null)
        {
            var tab = _current.ActiveTab;
            parts.Add($"{_current.Tabs.Count} onglet{(_current.Tabs.Count == 1 ? "" : "s")}");
            if (tab is not null) parts.Add($"« {tab.Title} »");
            parts.Add($"{_body.Text.Length} caractères");
            parts.Add($"modifiée {_current.ModifiedAt:HH:mm:ss}");
        }
        if (_saveError is not null)
            parts.Add($"⚠ Échec d'enregistrement de {_unsaved.Count} note(s), nouvel essai en cours : {_saveError}");
        if (_store.LastLoadFailures > 0)
            parts.Add($"⚠ {_store.LastLoadFailures} fichier(s) de note illisible(s) dans " + _store.Folder);
        if (!string.IsNullOrEmpty(flash)) parts.Add(flash);
        _lblStatus.Text = "  " + string.Join("  ·  ", parts);
    }

    // ─────────────────────────── Géométrie ───────────────────────────

    private int SafeSplitter()
    {
        try { return _split.SplitterDistance; } catch { return 300; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autosave.Dispose();
            _tip.Dispose();
            _dateTip.Dispose();
            _settingsMenu.Dispose();
            _bodyMenu.Dispose();
            _tabMenu.Dispose();
        }
        base.Dispose(disposing);
        if (disposing) _bodyFont?.Dispose();
    }

    /// <summary>Rapport de mise en page : prouve que rien n'est tronqué à l'échelle d'affichage courante.</summary>
    public string Diagnostics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DPI=" + DeviceDpi + "  ClientSize=" + ClientSize.Width + "x" + ClientSize.Height
                      + "  splitter=" + SafeSplitter());

        sb.AppendLine("liste: largeur=" + _list.Width + " rangee=" + _list.ItemHeight
                      + " pad=" + _list.DbgPad + " hauteurTitre=" + _list.DbgTitleH
                      + " hauteurDate=" + _list.DbgSubH);

        foreach (var n in _all)
        {
            var t = _list.MeasureTitle(n.Title);
            var s = _list.MeasureSub(n.ModifiedAt.ToString("dd/MM 'à' HH:mm"));
            sb.AppendLine($"  titre «{n.Title}» dispo={t.available} besoin={t.needed} tient={(t.fits ? "OUI" : "NON")}"
                          + $" | date dispo={s.available} besoin={s.needed} tient={(s.fits ? "OUI" : "NON")}"
                          + $" rangeeOK={(s.rowFits ? "OUI" : "NON")}");
        }

        void Check(string label, Control c, string text)
        {
            int need = TextRenderer.MeasureText(text, c.Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            int avail = c.Width - c.Padding.Horizontal - 2;
            sb.AppendLine($"  {label}: largeur={c.Width} hauteur={c.Height} texte='{text}' besoin={need} dispo={avail} "
                          + (need <= avail ? "tient=OUI" : "tient=NON"));
        }

        Check("bouton Nouvelle", _btnNew, _btnNew.Text);
        Check("bouton Voir tout", _btnViewAll, _btnViewAll.Text);
        Check("combo police", _fontFamily, _fontFamily.Text);
        Check("combo taille", _fontSize, _fontSize.Text);
        Check("recherche", _search, _search.PlaceholderText);
        sb.AppendLine("  swatch fond=" + _btnBack.Width + "x" + _btnBack.Height
                      + " swatch encre=" + _btnFore.Width + "x" + _btnFore.Height);
        sb.AppendLine("  barre: largeur=" + _bar.Width + " hauteur=" + _bar.Height);
        sb.AppendLine("  corps note: " + _body.Width + "x" + _body.Height
                      + " police=" + _body.Font.Name + " " + _body.Font.SizeInPoints + "pt");
        sb.AppendLine("  barre onglets: " + (_tabs.Visible ? "OUI" : "NON") + " largeur=" + _tabs.Width
                      + " hauteur=" + _tabs.Height + " compteur=" + _tabs.TabCount
                      + " contenu=" + _tabs.DbgContentWidth + " défilement=" + _tabs.DbgScroll
                      + " plus=" + _tabs.DbgPlusWidth + " rangee=" + _tabs.DbgRowHeight);
        var tabs = _current?.Tabs;
        if (tabs is not null)
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = _tabs.MeasureTab(i);
                sb.AppendLine($"  onglet «{tabs[i].Title}» dispo={t.available} besoin={t.needed} tient={(t.fits ? "OUI" : "NON")}");
            }
        sb.AppendLine("  numeros lignes: " + (_gutter.Visible ? "OUI" : "NON") + " largeur=" + _gutter.Width
                      + " lignes texte=" + (_body.GetLineFromCharIndex(_body.TextLength) + 1));
        return sb.ToString();
    }

    /// <summary>Barre de titre sombre (DWM), pour que la fenêtre reste cohérente avec le thème.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int attribute = Environment.OSVersion.Version.Build >= 22000 ? 20 : 19;
            int on = 1;
            DwmSetWindowAttribute(Handle, attribute, ref on, sizeof(int));
        }
        catch { /* thème clair de l'OS : on laisse la barre telle quelle */ }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ApplyUi(UiState? ui)
    {
        if (ui is null) return;
        _confirmDelete.Checked = ui.ConfirmBeforeDelete;
        _lineNumbers.Checked = ui.ShowLineNumbers;
        _wordWrap.Checked = ui.WordWrap;
        _body.WordWrap = _wordWrap.Checked;
        ApplyLineNumbers();
        try
        {
            if (ui.Width >= MinimumSize.Width && ui.Height >= MinimumSize.Height)
                Size = new Size(ui.Width, ui.Height);
            // Une position négative est valable (écran secondaire placé à gauche ou au-dessus).
            if (ui.Left is int savedLeft && ui.Top is int savedTop)
            {
                // bornage : une fenêtre restaurée hors de l'écran doit rester atteignable
                int vl = int.MaxValue, vt = int.MaxValue, vr = int.MinValue, vb = int.MinValue;
                foreach (var s in Screen.AllScreens)
                {
                    vl = Math.Min(vl, s.Bounds.Left);
                    vt = Math.Min(vt, s.Bounds.Top);
                    vr = Math.Max(vr, s.Bounds.Right);
                    vb = Math.Max(vb, s.Bounds.Bottom);
                }
                int left = Math.Clamp(savedLeft, vl, Math.Max(vl, vr - Width));
                int top = Math.Clamp(savedTop, vt, Math.Max(vt, vb - Height));
                StartPosition = FormStartPosition.Manual;
                Location = new Point(left, top);
            }
            if (ui.Maximized) WindowState = FormWindowState.Maximized;
            if (ui.Splitter > 0)
            {
                try { _split.SplitterDistance = ui.Splitter; } catch { }
            }
        }
        catch { /* écran débranché depuis : on garde la taille par défaut */ }
    }

    private void SaveUiState()
    {
        // Fenêtre agrandie ou réduite : on garde sa taille « normale », pas celle de l'écran ni -32000.
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _store.SaveUi(new UiState
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Left = bounds.X,
            Top = bounds.Y,
            Maximized = WindowState == FormWindowState.Maximized,
            Splitter = SafeSplitter(),
            ConfirmBeforeDelete = _confirmDelete.Checked,
            ShowLineNumbers = _lineNumbers.Checked,
            WordWrap = _wordWrap.Checked,
            NoteOrder = _all.Select(n => n.Id).ToList(),
        });
    }
}
