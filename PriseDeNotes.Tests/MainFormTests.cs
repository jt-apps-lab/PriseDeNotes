using System.Runtime.InteropServices;

namespace PriseDeNotes.Tests;

/// <summary>
/// Tests de la fenêtre principale, ouverte pour de vrai sur un dossier de notes jetable.
/// Ils demandent une session Windows avec bureau (pas un service sans interface).
/// </summary>
[Collection("Interface")]
public sealed class MainFormTests : IDisposable
{
    private readonly TempStore _temp = new();

    public void Dispose() => _temp.Dispose();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    /// <summary>Une note de trois onglets ; le premier contient <paramref name="content"/>.</summary>
    private Note SeedNote(string content, bool lineNumbers = false)
    {
        Directory.CreateDirectory(_temp.Root);
        File.WriteAllText(_temp.UiFile,
            $"{{\"width\":1100,\"height\":700,\"left\":40,\"top\":40,\"showLineNumbers\":{(lineNumbers ? "true" : "false")},\"wordWrap\":true}}");
        var n = new Note { Title = "Test", AutoTitle = false };
        n.Tabs[0].Title = "Un";
        n.Tabs[0].Content = content;
        n.AddTab("Deux");
        n.AddTab("Trois");
        n.ActiveTabId = n.Tabs[0].Id;
        _temp.Open().Save(n);
        return n;
    }

    private MainForm Open() => new(_temp.Open());

    private static Note Current(MainForm form) => Reflect.Get<Note>(form, "_current");

    // ─────────── Numéros de ligne ───────────

    [Fact]
    public void Les_numeros_de_ligne_suivent_les_paragraphes_et_ignorent_les_retours_automatiques()
    {
        var paragraphs = Enumerable.Range(1, 130).Select(i =>
            i % 7 == 0 ? "" : i % 3 == 0 ? string.Join(" ", Enumerable.Repeat($"long{i}", 60)) : $"ligne {i}");
        SeedNote(string.Join("\n", paragraphs), lineNumbers: true);

        Ui.Run(Open, async form =>
        {
            var body = Reflect.Get<RichTextBox>(form, "_body");
            var gutter = Reflect.Get<object>(form, "_gutter");

            void AssertMatchesText(string where)
            {
                Reflect.Call(gutter, "RefreshNumbers");
                var shown = Reflect.Get<Array>(gutter, "_visibleLines").Cast<object>()
                    .Select(l => (int)l.GetType().GetProperty("Number")!.GetValue(l)!).ToList();
                // Attendu, recalculé à part depuis le texte : lignes affichées qui commencent un paragraphe.
                string text = body.Text;
                int firstLine = body.GetLineFromCharIndex(body.GetCharIndexFromPosition(new Point(1, 1)));
                int lastLine = body.GetLineFromCharIndex(body.GetCharIndexFromPosition(new Point(1, body.ClientSize.Height - 2)));
                var expected = new List<int>();
                for (int line = firstLine; line <= lastLine; line++)
                {
                    int start = body.GetFirstCharIndexFromLine(line);
                    if (start < 0) break;
                    if (start == 0 || text[start - 1] == '\n') expected.Add(text.Take(start).Count(c => c == '\n') + 1);
                }
                Assert.True(expected.Count > 0, where);
                Assert.All(expected, number => Assert.Contains(number, shown));
                // Seule tolérance : la ligne à moitié visible juste avant ou après la zone affichée.
                Assert.All(shown, number => Assert.InRange(number, expected[0] - 1, expected[^1] + 1));
                Assert.Equal(shown.Distinct().Count(), shown.Count);
            }

            AssertMatchesText("haut");
            Assert.Equal(130, Reflect.Get<int>(gutter, "_paragraphCount"));

            body.SelectionStart = body.GetFirstCharIndexFromLine(body.GetLineFromCharIndex(body.TextLength) / 2);
            body.ScrollToCaret();
            await Task.Delay(150);
            AssertMatchesText("milieu");

            body.SelectionStart = body.TextLength;
            body.ScrollToCaret();
            await Task.Delay(150);
            AssertMatchesText("fin");

            body.WordWrap = false;
            await Task.Delay(150);
            AssertMatchesText("sans retour automatique");
        });
    }

    // ─────────── Renommage d'un onglet ───────────

    [Fact]
    public void Renommer_depuis_le_menu_donne_le_focus_au_champ_et_un_clic_ailleurs_applique()
    {
        var seeded = SeedNote("texte");

        Ui.Run(Open, async form =>
        {
            var tabs = Reflect.Get<TabStrip>(form, "_tabs");
            var menu = Reflect.Get<ContextMenuStrip>(form, "_tabMenu");
            var field = Reflect.Get<TextBox>(form, "_tabTitle");
            var body = Reflect.Get<RichTextBox>(form, "_body");

            Reflect.Call(form, "ShowTabMenu", 1, tabs.PointToScreen(new Point(tabs.TabBounds(1).X + 5, 5)));
            await Task.Delay(150);
            menu.Items.Cast<ToolStripItem>().First(i => i.Text == "Renommer").PerformClick();
            await Task.Delay(300);

            Assert.False(menu.Visible);
            Assert.True(field.Visible, "le champ de titre doit être affiché");
            Assert.True(field.Focused, "le champ de titre doit garder le focus après la fermeture du menu");

            field.Text = "Renommé";
            body.Focus();
            await Task.Delay(200);

            Assert.False(field.Visible);
            Assert.Equal("Renommé", Current(form).Tabs[1].Title);
        });

        Assert.Equal("Renommé", _temp.Open().LoadAll().Single(n => n.Id == seeded.Id).Tabs[1].Title);
    }

    [Fact]
    public void Le_champ_reprend_le_focus_rendu_a_la_barre_par_programme()
    {
        SeedNote("texte");

        Ui.Run(Open, async form =>
        {
            var tabs = Reflect.Get<TabStrip>(form, "_tabs");
            var field = Reflect.Get<TextBox>(form, "_tabTitle");

            Reflect.Call(form, "BeginTabRename", 2);
            await Task.Delay(200);
            tabs.Focus();   // ce que fait un menu qui se ferme
            await Task.Delay(200);

            Assert.True(field.Visible);
            Assert.True(field.Focused);
            SendMessage(field.Handle, 0x0100, (IntPtr)Keys.Escape, IntPtr.Zero);
        });
    }

    [Fact]
    public void Un_clic_sur_un_autre_onglet_applique_le_titre_puis_selectionne_l_onglet()
    {
        SeedNote("texte");

        Ui.Run(Open, async form =>
        {
            var tabs = Reflect.Get<TabStrip>(form, "_tabs");
            var field = Reflect.Get<TextBox>(form, "_tabTitle");

            Reflect.Call(form, "BeginTabRename", 2);
            await Task.Delay(200);
            field.Text = "Trois bis";

            var target = tabs.TabBounds(0);
            var point = (IntPtr)(((target.Y + target.Height / 2) << 16) | (target.X + target.Width / 2));
            SendMessage(tabs.Handle, 0x0201, (IntPtr)1, point);   // WM_LBUTTONDOWN
            SendMessage(tabs.Handle, 0x0202, IntPtr.Zero, point); // WM_LBUTTONUP
            await Task.Delay(200);

            Assert.False(field.Visible);
            Assert.Equal("Trois bis", Current(form).Tabs[2].Title);
            Assert.Equal(0, tabs.SelectedIndex);
        });
    }

    [Fact]
    public void Echap_annule_le_renommage()
    {
        SeedNote("texte");

        Ui.Run(Open, async form =>
        {
            var field = Reflect.Get<TextBox>(form, "_tabTitle");

            Reflect.Call(form, "BeginTabRename", 1);
            await Task.Delay(200);
            field.Text = "ANNULÉ";
            SendMessage(field.Handle, 0x0100, (IntPtr)Keys.Escape, IntPtr.Zero);
            await Task.Delay(150);

            Assert.False(field.Visible);
            Assert.Equal("Deux", Current(form).Tabs[1].Title);
        });
    }

    // ─────────── Suppression ───────────

    [Theory]
    [InlineData(Keys.Control | Keys.D)]
    [InlineData(Keys.Control | Keys.Delete)]
    public void Aucun_raccourci_clavier_ne_supprime_la_note(Keys keys)
    {
        var seeded = SeedNote("texte");

        Ui.Run(Open, form =>
        {
            var body = Reflect.Get<RichTextBox>(form, "_body");
            var message = Message.Create(body.Handle, 0x0100, (IntPtr)(keys & Keys.KeyCode), IntPtr.Zero);
            var handled = (bool)Reflect.Call(form, "ProcessCmdKey", message, keys)!;

            Assert.False(handled);
            Assert.True(File.Exists(_temp.Open().PathFor(seeded.Id)));
            return Task.CompletedTask;
        });
    }

    // ─────────── Échec d'enregistrement ───────────

    [Fact]
    public void Un_echec_d_enregistrement_est_signale_puis_rattrape()
    {
        var seeded = SeedNote("début");
        var path = _temp.Open().PathFor(seeded.Id);

        Ui.Run(Open, async form =>
        {
            var body = Reflect.Get<RichTextBox>(form, "_body");
            var unsaved = Reflect.Get<HashSet<Note>>(form, "_unsaved");
            var status = Reflect.Get<ToolStripStatusLabel>(form, "_lblStatus");

            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))   // fichier verrouillé
            {
                body.Text = "saisie à ne pas perdre";
                Reflect.Call(form, "Commit");

                Assert.Contains(Current(form), unsaved);
                Assert.Contains("Échec d'enregistrement", status.Text);
            }

            Reflect.Call(form, "RetryUnsaved");
            await Task.Delay(50);

            Assert.Empty(unsaved);
        });

        Assert.Contains("saisie à ne pas perdre", File.ReadAllText(path));
    }

    // ─────────── Fenêtre et listes ───────────

    [Fact]
    public void Une_fenetre_agrandie_est_memorisee_avec_sa_taille_normale()
    {
        SeedNote("texte");

        Ui.Run(Open, form =>
        {
            form.WindowState = FormWindowState.Maximized;
            Reflect.Call(form, "SaveUiState");
            return Task.CompletedTask;
        });

        var ui = _temp.Open().LoadUi()!;
        Assert.True(ui.Maximized);
        Assert.Equal(1100, ui.Width);
        Assert.Equal(40, ui.Left);
    }

    [Fact]
    public void Une_valeur_hors_liste_n_occupe_qu_une_place_dans_la_liste()
    {
        Ui.Run(() => new Form(), form =>
        {
            var combo = new ComboBox();
            combo.Items.AddRange(new object[] { "10", "11", "12" });
            form.Controls.Add(combo);

            Reflect.CallStatic(typeof(MainForm), "AddItem", combo, "13.5");
            Reflect.CallStatic(typeof(MainForm), "AddItem", combo, "14.5");
            Assert.Equal(new[] { "10", "11", "12", "14.5" }, combo.Items.Cast<string>());

            Reflect.CallStatic(typeof(MainForm), "AddItem", combo, "11");
            Assert.Equal(new[] { "10", "11", "12" }, combo.Items.Cast<string>());
            Assert.Equal("11", combo.SelectedItem);
            return Task.CompletedTask;
        });
    }
}
