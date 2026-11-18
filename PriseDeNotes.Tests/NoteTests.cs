namespace PriseDeNotes.Tests;

public class NoteTests
{
    [Theory]
    [InlineData("  un deux\ttrois\nquatre cinq six sept", "un deux trois quatre cinq")]
    [InlineData("seul", "seul")]
    [InlineData("", "Nouvelle note")]
    [InlineData("   \n\t ", "Nouvelle note")]
    public void TitleFromContent_garde_les_cinq_premiers_mots(string content, string expected) =>
        Assert.Equal(expected, Note.TitleFromContent(content));

    [Theory]
    [InlineData("", 0)]
    [InlineData("un deux  trois\r\nquatre", 4)]
    public void CountWords_compte_les_mots(string text, int expected) =>
        Assert.Equal(expected, Note.CountWords(text));

    [Fact]
    public void Une_note_neuve_a_un_onglet_actif()
    {
        var n = new Note();
        Assert.Single(n.Tabs);
        Assert.Same(n.Tabs[0], n.ActiveTab);
    }

    [Fact]
    public void EnsureTabs_repare_identifiants_en_double_et_onglet_actif_inconnu()
    {
        var n = new Note();
        var id = Guid.NewGuid();
        n.Tabs = new List<NoteTab> { new() { Id = id, Title = "A" }, new() { Id = id, Title = "B" } };
        n.ActiveTabId = Guid.NewGuid();

        n.EnsureTabs();

        Assert.Equal(2, n.Tabs.Select(t => t.Id).Distinct().Count());
        Assert.Equal(n.Tabs[0].Id, n.ActiveTabId);
    }

    [Fact]
    public void EnsureTabs_recree_un_onglet_si_la_liste_est_vide()
    {
        var n = new Note { Tabs = new List<NoteTab>() };
        n.EnsureTabs();
        Assert.Single(n.Tabs);
    }

    [Fact]
    public void ActiveTab_ne_modifie_pas_la_note()
    {
        var n = new Note { Tabs = new List<NoteTab>() };
        Assert.Null(n.ActiveTab);
        Assert.Empty(n.Tabs);
    }

    [Fact]
    public void NextTabTitle_prend_le_premier_numero_libre()
    {
        var n = new Note();
        n.Tabs[0].Title = "Onglet 1";
        n.AddTab("onglet 2");
        Assert.Equal("Onglet 3", n.NextTabTitle());
    }

    [Theory]
    [InlineData(0, 2, "B,C,A")]
    [InlineData(2, 0, "C,A,B")]
    [InlineData(-1, 1, "A,B,C")]
    [InlineData(0, 3, "A,B,C")]
    public void MoveTab_deplace_ou_ignore_les_positions_invalides(int from, int to, string expected)
    {
        var n = new Note();
        n.Tabs[0].Title = "A";
        n.AddTab("B");
        n.AddTab("C");
        n.MoveTab(from, to);
        Assert.Equal(expected, string.Join(",", n.Tabs.Select(t => t.Title)));
    }

    [Fact]
    public void MigrateLegacy_range_l_ancien_texte_dans_l_onglet_vide()
    {
        var n = new Note { LegacyContent = "ancien", LegacyRichText = @"{\rtf1 ancien}" };
        Assert.True(n.MigrateLegacy());
        Assert.Single(n.Tabs);
        Assert.Equal("ancien", n.Tabs[0].Content);
        Assert.Equal(@"{\rtf1 ancien}", n.Tabs[0].RichText);
        Assert.Null(n.LegacyContent);
        Assert.Null(n.LegacyRichText);
    }

    [Fact]
    public void MigrateLegacy_n_ecrase_jamais_un_onglet_rempli()
    {
        var n = new Note { LegacyContent = "ancien" };
        n.Tabs[0].Content = "nouveau";
        Assert.True(n.MigrateLegacy());
        Assert.Equal(new[] { "nouveau", "ancien" }, n.Tabs.Select(t => t.Content));
        Assert.Equal("Texte récupéré", n.Tabs[1].Title);
        Assert.Same(n.Tabs[1], n.ActiveTab);
    }

    [Fact]
    public void MigrateLegacy_efface_les_champs_vides_sans_rien_signaler()
    {
        var n = new Note { LegacyContent = "" };
        Assert.False(n.MigrateLegacy());
        Assert.Null(n.LegacyContent);
    }

    [Fact]
    public void Matches_cherche_dans_le_titre_et_tous_les_onglets()
    {
        var n = new Note { Title = "Courses" };
        n.AddTab().Content = "acheter du PAIN";
        Assert.True(n.Matches("cour"));
        Assert.True(n.Matches("pain"));
        Assert.False(n.Matches("lait"));
    }
}
