using System.Text.Json;
using System.Text.Json.Serialization;

namespace PriseDeNotes;

/// <summary>Accepte tous les formats de GUID (avec ou sans tirets) : un fichier édité à la main doit se charger.</summary>
public sealed class LenientGuidConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s)) return Guid.NewGuid();
        return Guid.TryParse(s, out var g) ? g : Guid.NewGuid();
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("D"));
}

/// <summary>Un onglet d'une note : titre, contenu texte, contenu enrichi.</summary>
public sealed class NoteTab
{
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Onglet 1";
    public string Content { get; set; } = "";
    public string? RichText { get; set; }

    public override string ToString() => Title;
}

/// <summary>Une note : titre, onglets, fond, couleur de police, police, dates. Police et couleurs sont partagées par les onglets.</summary>
public sealed class Note
{
    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Nouvelle note";
    public bool AutoTitle { get; set; }

    public List<NoteTab> Tabs { get; set; } = new();

    [JsonConverter(typeof(LenientGuidConverter))]
    public Guid ActiveTabId { get; set; }

    // Note d'avant les onglets : relu à la migration, vidé ensuite, jamais réécrit.
    [JsonPropertyName("content")] public string LegacyContent { get; set; } = "";
    [JsonPropertyName("richText")] public string? LegacyRichText { get; set; }

    public string BackColor { get; set; } = Theme.DefaultNoteBack;
    public string ForeColor { get; set; } = Theme.DefaultNoteFore;
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 11f;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    public Note()
    {
        EnsureTabs();
    }

    /// <summary>Onglet affiché : l'onglet mémorisé, sinon le premier.</summary>
    [JsonIgnore]
    public NoteTab? ActiveTab
    {
        get
        {
            EnsureTabs();
            return Tabs.FirstOrDefault(t => t.Id == ActiveTabId) ?? Tabs.FirstOrDefault();
        }
    }

    /// <summary>Contenu de l'onglet actif : sert au titre automatique et à l'éditeur.</summary>
    [JsonIgnore]
    public string Content => ActiveTab?.Content ?? "";

    [JsonIgnore]
    public string? RichText => ActiveTab?.RichText;

    [JsonIgnore]
    public int TotalChars => Tabs.Sum(t => t.Content.Length);

    [JsonIgnore]
    public int TotalWords => Tabs.Sum(t => CountWords(t.Content));

    /// <summary>La recherche porte sur le titre et sur le contenu de tous les onglets.</summary>
    public bool Matches(string query) =>
        Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Tabs.Any(t => t.Content.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ajoute un onglet en fin de barre, et le rend actif.</summary>
    public NoteTab AddTab(string? title = null)
    {
        var tab = new NoteTab { Title = string.IsNullOrWhiteSpace(title) ? NextTabTitle() : title!.Trim() };
        Tabs.Add(tab);
        ActiveTabId = tab.Id;
        return tab;
    }

    /// <summary>Le premier numéro « Onglet n » libre.</summary>
    public string NextTabTitle()
    {
        int n = 1;
        while (Tabs.Any(t => string.Equals(t.Title, $"Onglet {n}", StringComparison.OrdinalIgnoreCase))) n++;
        return $"Onglet {n}";
    }

    public int IndexOfTab(Guid id) => Tabs.FindIndex(t => t.Id == id);

    /// <summary>
    /// Range le texte d'une note d'avant les onglets (champs « content » / « richText ») dans un onglet.
    /// Le constructeur crée déjà un onglet vide : on ne peut donc pas se fier à Tabs.Count == 0.
    /// Si les onglets sont tous vides, l'ancien texte va dans le premier ; sinon il part dans un onglet
    /// ajouté, pour ne jamais rien écraser. Renvoie true si la note a changé.
    /// </summary>
    public bool MigrateLegacy()
    {
        bool hasText = !string.IsNullOrEmpty(LegacyContent);
        bool hasRich = !string.IsNullOrEmpty(LegacyRichText);
        if (!hasText && !hasRich) return false;

        EnsureTabs();
        bool tabsEmpty = Tabs.All(t => string.IsNullOrEmpty(t.Content) && string.IsNullOrEmpty(t.RichText));
        NoteTab target;
        if (tabsEmpty)
        {
            target = Tabs[0];
            Tabs.RemoveRange(1, Tabs.Count - 1);
        }
        else
        {
            target = new NoteTab { Title = "Texte récupéré" };
            Tabs.Add(target);
        }
        target.Content = LegacyContent ?? "";
        target.RichText = hasRich ? LegacyRichText : null;
        ActiveTabId = target.Id;
        LegacyContent = "";
        LegacyRichText = null;
        return true;
    }

    /// <summary>Répare un fichier édité à la main : onglets manquants, identifiants vides ou en double.</summary>
    public void EnsureTabs()
    {
        if (Tabs.Any(t => t is null)) Tabs.RemoveAll(t => t is null);
        var seen = new HashSet<Guid>();
        foreach (var t in Tabs)
            if (t.Id == Guid.Empty || !seen.Add(t.Id)) t.Id = Guid.NewGuid();
        if (Tabs.Count == 0) AddTab("Onglet 1");
        if (IndexOfTab(ActiveTabId) < 0) ActiveTabId = Tabs[0].Id;
    }

    /// <summary>Déplace l'onglet <paramref name="from"/> à la place <paramref name="target"/> (places visibles).</summary>
    public void MoveTab(int from, int target)
    {
        if (from < 0 || target < 0 || from >= Tabs.Count || target >= Tabs.Count || from == target) return;
        var tab = Tabs[from];
        Tabs.RemoveAt(from);
        Tabs.Insert(target, tab);
    }

    public static string TitleFromContent(string content)
    {
        var words = content.Split((char[]?)null, 6, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? "Nouvelle note" : string.Join(" ", words.Take(5));
    }

    public static int CountWords(string s) =>
        s.Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries).Length;

    public override string ToString() => Title;
}
