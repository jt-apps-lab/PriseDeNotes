using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PriseDeNotes;

/// <summary>Persistance : une note = un fichier JSON dans %APPDATA%\PriseDeNotes\notes.</summary>
public sealed class NoteStore
{
    public string Folder { get; }
    public string UiFile { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public NoteStore(string? root = null)
    {
        var baseDir = root
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PriseDeNotes");
        Folder = Path.Combine(baseDir, "notes");
        UiFile = Path.Combine(baseDir, "ui.json");
        System.IO.Directory.CreateDirectory(Folder);
    }

    public string PathFor(Guid id) => Path.Combine(Folder, id.ToString("N") + ".json");

    private static string Serialize(Note note) => JsonSerializer.Serialize(note, JsonOpts);

    public List<Note> LoadAll()
    {
        var list = new List<Note>();
        int failed = 0;
        foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                var n = document.RootElement.Deserialize<Note>(JsonOpts);
                if (n is null) { failed++; continue; }
                if (n.Id == Guid.Empty) n.Id = Guid.NewGuid();

                // Migration d'avant les onglets : le texte devient l'onglet 1 de la note.
                bool migrated = n.Tabs.Count == 0;
                if (migrated)
                {
                    var tab = new NoteTab { Title = "Onglet 1", Content = n.LegacyContent, RichText = n.LegacyRichText };
                    n.Tabs.Add(tab);
                    n.ActiveTabId = tab.Id;
                    n.LegacyContent = "";
                    n.LegacyRichText = null;
                }
                n.EnsureTabs();

                if (!document.RootElement.TryGetProperty("autoTitle", out _) && n.Title == "Nouvelle note")
                {
                    n.AutoTitle = true;
                    n.Title = Note.TitleFromContent(n.Content);
                    migrated = true;
                }
                // Migration sans changer la date de la note.
                if (migrated)
                {
                    try { File.WriteAllText(file, JsonSerializer.Serialize(n, JsonOpts), Encoding.UTF8); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                list.Add(n);
            }
            catch
            {
                // fichier corrompu : on l'ignore, on ne le supprime jamais
                failed++;
            }
        }
        LastLoadFailures = failed;
        return list;
    }

    /// <summary>Nombre de fichiers de note illisibles au dernier chargement (averti dans la barre d'état).</summary>
    public int LastLoadFailures { get; private set; }

    public void Save(Note note)
    {
        note.ModifiedAt = DateTime.Now;
        File.WriteAllText(PathFor(note.Id), Serialize(note), Encoding.UTF8);
    }

    public void Delete(Note note)
    {
        var path = PathFor(note.Id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>Le JSON d'une note : le fichier sur disque s'il existe (le plus fidèle), sinon une sérialisation.</summary>
    public string JsonFor(Note note)
    {
        try
        {
            var path = PathFor(note.Id);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Serialize(note);
    }

    /// <summary>Relit un JSON de note (mêmes règles que le chargement), sans rien écrire. Null si illisible.</summary>
    public Note? ParseNote(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var n = document.RootElement.Deserialize<Note>(JsonOpts);
            if (n is null) return null;
            if (n.Id == Guid.Empty) n.Id = Guid.NewGuid();

            // Migration d'avant les onglets : le texte devient l'onglet 1 de la note.
            if (n.Tabs.Count == 0)
            {
                var tab = new NoteTab { Title = "Onglet 1", Content = n.LegacyContent, RichText = n.LegacyRichText };
                n.Tabs.Add(tab);
                n.ActiveTabId = tab.Id;
            }
            n.LegacyContent = "";
            n.LegacyRichText = null;
            n.EnsureTabs();

            if (!document.RootElement.TryGetProperty("autoTitle", out _) && n.Title == "Nouvelle note")
            {
                n.AutoTitle = true;
                n.Title = Note.TitleFromContent(n.Content);
            }
            if (string.IsNullOrWhiteSpace(n.Title)) n.Title = "Sans titre";
            foreach (var tab in n.Tabs)
                if (string.IsNullOrWhiteSpace(tab.Title)) tab.Title = n.NextTabTitle();
            n.EnsureTabs();
            return n;
        }
        catch
        {
            // contenu illisible : l'appelant compte l'élément ignoré
            return null;
        }
    }

    /// <summary>Import depuis des fichiers : un zip créé par « Archiver », ou des .json de note à l'unité.</summary>
    public ImportResult Import(IEnumerable<string> paths)
    {
        var result = new ImportResult();
        foreach (var path in paths)
        {
            try
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".zip")
                {
                    using var archive = ZipFile.OpenRead(path);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Length == 0) continue;                       // dossier dans l'archive
                        if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) { result.Skipped++; continue; }
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        AddImported(result, reader.ReadToEnd());
                    }
                }
                else if (extension == ".json") AddImported(result, File.ReadAllText(path));
                else result.Skipped++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                result.Skipped++;
            }
        }
        return result;
    }

    /// <summary>Ajoute une note importée sous un identifiant neuf : une restauration n'écrase jamais une note existante.</summary>
    private void AddImported(ImportResult result, string json)
    {
        var note = ParseNote(json);
        if (note is null) { result.Skipped++; return; }
        note.Id = Guid.NewGuid();
        note.CreatedAt = DateTime.Now;
        try
        {
            Save(note);
            result.Notes.Add(note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Skipped++;
        }
    }

    public UiState? LoadUi()
    {
        try { return File.Exists(UiFile) ? JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiFile), JsonOpts) : null; }
        catch { return null; }
    }

    public void SaveUi(UiState ui)
    {
        try { File.WriteAllText(UiFile, JsonSerializer.Serialize(ui, JsonOpts), Encoding.UTF8); }
        catch { /* fenêtre fermée : non critique */ }
    }
}

/// <summary>Ce qu'un import a rapporté : les notes créées, et le nombre d'éléments ignorés.</summary>
public sealed class ImportResult
{
    public List<Note> Notes { get; } = new();
    public int Skipped { get; set; }
}

public sealed class UiState
{
    public int Width { get; set; } = 1180;
    public int Height { get; set; } = 720;
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
    public int Splitter { get; set; } = 300;
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool ShowLineNumbers { get; set; }
    public bool WordWrap { get; set; } = true;
    public List<Guid> NoteOrder { get; set; } = new();
}
