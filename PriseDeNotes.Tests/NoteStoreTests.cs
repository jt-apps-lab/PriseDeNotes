using System.IO.Compression;
using System.Text.Json;

namespace PriseDeNotes.Tests;

public sealed class NoteStoreTests : IDisposable
{
    private readonly TempStore _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string TabJson(string title, string content) =>
        $"{{\"id\":\"{Guid.NewGuid()}\",\"title\":\"{title}\",\"content\":\"{content}\"}}";

    // ─────────── Chargement et migration ───────────

    [Fact]
    public void Une_note_d_avant_les_onglets_retrouve_son_texte_et_le_fichier_est_migre()
    {
        var id = Guid.NewGuid();
        var path = _temp.WriteRaw(id, $"{{\"id\":\"{id}\",\"title\":\"Vieille\",\"content\":\"TEXTE\",\"modifiedAt\":\"2021-05-06T07:08:09\"}}");

        var note = Assert.Single(_temp.Open().LoadAll());

        Assert.Equal("TEXTE", note.Tabs.Single().Content);
        Assert.Equal(new DateTime(2021, 5, 6, 7, 8, 9), note.ModifiedAt);   // la migration ne date pas la note
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.False(json.RootElement.TryGetProperty("content", out _));      // ancien champ plus réécrit
        Assert.Equal("TEXTE", json.RootElement.GetProperty("tabs")[0].GetProperty("content").GetString());
    }

    [Fact]
    public void Un_fichier_abime_par_l_ancien_bug_est_repare()
    {
        var id = Guid.NewGuid();
        _temp.WriteRaw(id, $"{{\"id\":\"{id}\",\"title\":\"B\",\"autoTitle\":false,\"tabs\":[{TabJson("Onglet 1", "")}],\"content\":\"TEXTE B\"}}");

        var note = Assert.Single(_temp.Open().LoadAll());

        Assert.Equal("TEXTE B", note.Tabs.Single().Content);
    }

    [Fact]
    public void L_ancien_texte_ne_remplace_jamais_un_onglet_rempli()
    {
        var id = Guid.NewGuid();
        _temp.WriteRaw(id, $"{{\"id\":\"{id}\",\"title\":\"C\",\"autoTitle\":false,\"tabs\":[{TabJson("", "NOUVEAU")}],\"content\":\"VIEUX\"}}");

        var note = Assert.Single(_temp.Open().LoadAll());

        Assert.Equal(new[] { "NOUVEAU", "VIEUX" }, note.Tabs.Select(t => t.Content));
        Assert.Equal("Onglet 1", note.Tabs[0].Title);   // titre vide réparé
    }

    [Fact]
    public void Un_fichier_illisible_est_compte_et_jamais_supprime()
    {
        var path = _temp.WriteRaw(Guid.NewGuid(), "{pas du json");
        var store = _temp.Open();

        Assert.Empty(store.LoadAll());
        Assert.Equal(1, store.LastLoadFailures);
        Assert.True(File.Exists(path));
    }

    // ─────────── Enregistrement ───────────

    [Fact]
    public void Save_ecrit_sans_laisser_de_fichier_temporaire()
    {
        var store = _temp.Open();
        var n = new Note { Title = "X" };
        n.Tabs[0].Content = "contenu";

        store.Save(n);
        store.Save(n);   // deuxième passage : remplacement d'un fichier existant

        Assert.Empty(Directory.GetFiles(_temp.Root, "*.tmp", SearchOption.AllDirectories));
        Assert.Equal("contenu", Assert.Single(_temp.Open().LoadAll()).Tabs[0].Content);
    }

    [Fact]
    public void Save_touch_false_garde_la_date_de_modification()
    {
        var store = _temp.Open();
        var date = new DateTime(2020, 1, 1);
        var n = new Note { ModifiedAt = date };

        store.Save(n, touch: false);
        Assert.Equal(date, n.ModifiedAt);

        store.Save(n);
        Assert.NotEqual(date, n.ModifiedAt);
    }

    [Fact]
    public void JsonFor_reflete_la_note_en_memoire_meme_non_enregistree()
    {
        var store = _temp.Open();
        var n = new Note { Title = "Avant" };
        store.Save(n);
        n.Title = "Après";   // modifié en mémoire, pas encore sur disque

        Assert.Contains("Après", NoteStore.JsonFor(n));
    }

    // ─────────── Récupération des .tmp après un plantage ───────────

    [Fact]
    public void Un_tmp_complet_remplace_la_note_et_un_tmp_tronque_est_efface()
    {
        var store = _temp.Open();
        var complete = new Note { Title = "Complet", AutoTitle = false };
        var broken = new Note { Title = "Tronqué", AutoTitle = false };
        store.Save(complete);
        store.Save(broken);

        complete.Title = "Complet v2";
        File.WriteAllText(store.PathFor(complete.Id) + ".tmp", NoteStore.JsonFor(complete));   // plantage avant le remplacement
        File.WriteAllText(store.PathFor(broken.Id) + ".tmp", "{\"id\":\"");                    // plantage pendant l'écriture

        var notes = _temp.Open().LoadAll();

        Assert.Contains(notes, n => n.Title == "Complet v2");
        Assert.Contains(notes, n => n.Title == "Tronqué");
        Assert.Empty(Directory.GetFiles(_temp.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Un_ui_json_tmp_complet_est_recupere()
    {
        Directory.CreateDirectory(_temp.Root);
        File.WriteAllText(_temp.UiFile, "{\"width\":1000}");
        File.WriteAllText(_temp.UiFile + ".tmp", "{\"width\":1234}");

        Assert.Equal(1234, _temp.Open().LoadUi()!.Width);
    }

    // ─────────── ui.json ───────────

    [Fact]
    public void Ui_ancien_format_et_position_negative_sont_relus()
    {
        Directory.CreateDirectory(_temp.Root);
        File.WriteAllText(_temp.UiFile, "{\"width\":1300,\"left\":-1500,\"top\":20}");

        var ui = _temp.Open().LoadUi()!;

        Assert.Equal(-1500, ui.Left);
        Assert.Equal(20, ui.Top);
        Assert.False(ui.Maximized);
    }

    [Fact]
    public void Ui_sans_position_laisse_la_position_vide()
    {
        var store = _temp.Open();
        Assert.True(store.SaveUi(new UiState()));
        Assert.Null(store.LoadUi()!.Left);
    }

    [Fact]
    public void SaveUi_signale_un_echec_d_ecriture()
    {
        var store = _temp.Open();
        store.SaveUi(new UiState());
        using (new FileStream(_temp.UiFile, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(store.SaveUi(new UiState { Width = 999 }));
        Assert.True(store.SaveUi(new UiState { Width = 999 }));
    }

    // ─────────── Archive et restauration ───────────

    private string Archive(params Note[] notes)
    {
        var zip = Path.Combine(_temp.Root, Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var n in notes)
        {
            using var writer = new StreamWriter(archive.CreateEntry(n.Title + ".json").Open());
            writer.Write(NoteStore.JsonFor(n));
        }
        return zip;
    }

    [Fact]
    public void Restaurer_ignore_les_notes_deja_presentes()
    {
        var store = _temp.Open();
        var a = new Note { Title = "A", AutoTitle = false };
        a.Tabs[0].Content = "texte A";
        store.Save(a);

        var result = store.Import(new[] { Archive(a) }, store.LoadAll());

        Assert.Empty(result.Notes);
        Assert.Equal(1, result.Duplicates);
    }

    [Fact]
    public void Restaurer_conserve_les_dates_et_donne_un_identifiant_neuf()
    {
        var store = _temp.Open();
        var a = new Note
        {
            Title = "A", AutoTitle = false,
            CreatedAt = new DateTime(2019, 1, 2), ModifiedAt = new DateTime(2020, 3, 4),
        };
        var zip = Archive(a);

        var result = store.Import(new[] { zip }, Array.Empty<Note>());

        var imported = Assert.Single(result.Notes);
        Assert.NotEqual(a.Id, imported.Id);
        Assert.Equal(new DateTime(2019, 1, 2), imported.CreatedAt);
        Assert.Equal(new DateTime(2020, 3, 4), imported.ModifiedAt);
        Assert.True(File.Exists(store.PathFor(imported.Id)));
    }

    [Fact]
    public void Restaurer_deux_fois_le_meme_zip_ne_cree_pas_de_doublon()
    {
        var store = _temp.Open();
        var zip = Archive(new Note { Title = "A", AutoTitle = false });

        var first = store.Import(new[] { zip }, store.LoadAll());
        var second = store.Import(new[] { zip }, store.LoadAll());

        Assert.Single(first.Notes);
        Assert.Empty(second.Notes);
        Assert.Equal(1, second.Duplicates);
    }

    [Fact]
    public void Restaurer_compte_les_elements_illisibles()
    {
        var store = _temp.Open();
        var bad = Path.Combine(_temp.Root, "mauvais.json");
        File.WriteAllText(bad, "pas une note");
        var other = Path.Combine(_temp.Root, "image.png");
        File.WriteAllText(other, "x");

        var result = store.Import(new[] { bad, other }, Array.Empty<Note>());

        Assert.Empty(result.Notes);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public void Restaurer_un_ancien_fichier_garde_son_texte()
    {
        var store = _temp.Open();
        var legacy = Path.Combine(_temp.Root, "ancienne.json");
        File.WriteAllText(legacy, "{\"title\":\"Ancienne\",\"content\":\"IMPORT ANCIEN\"}");

        var imported = Assert.Single(store.Import(new[] { legacy }, Array.Empty<Note>()).Notes);

        Assert.Equal("IMPORT ANCIEN", imported.Content);
    }
}
