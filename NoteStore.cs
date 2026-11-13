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

    private string FileFor(Guid id) => Path.Combine(Folder, id.ToString("N") + ".json");

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
        File.WriteAllText(FileFor(note.Id), JsonSerializer.Serialize(note, JsonOpts), Encoding.UTF8);
    }

    public void Delete(Note note)
    {
        var path = FileFor(note.Id);
        if (File.Exists(path)) File.Delete(path);
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
