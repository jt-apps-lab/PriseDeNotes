using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PriseDeNotes;

/// <summary>Persistance : une note = un fichier JSON dans %APPDATA%\PriseDeNotes\notes.</summary>
public sealed class NoteStore
{
    /// <summary>Taille maximale d'un fichier de note lu dans une archive (protège d'un zip piégé).</summary>
    private const long MaxImportEntryBytes = 64L * 1024 * 1024;

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
            Note? n;
            bool repaired;
            try { n = ReadNote(File.ReadAllText(file), out repaired); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { n = null; repaired = false; }
            if (n is null)
            {
                // fichier corrompu : on l'ignore, on ne le supprime jamais
                failed++;
                continue;
            }
            // Réparation écrite sans changer la date de la note.
            if (repaired)
            {
                try { WriteAtomic(file, Serialize(n)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            list.Add(n);
        }
        LastLoadFailures = failed;
        return list;
    }

    /// <summary>Nombre de fichiers de note illisibles au dernier chargement (averti dans la barre d'état).</summary>
    public int LastLoadFailures { get; private set; }

    /// <param name="touch">false : enregistrer sans changer la date de modification (simple affichage d'un onglet).</param>
    public void Save(Note note, bool touch = true)
    {
        if (touch) note.ModifiedAt = DateTime.Now;
        WriteAtomic(PathFor(note.Id), Serialize(note));
    }

    /// <summary>
    /// Écrit d'abord un fichier temporaire vidé sur le disque, puis le substitue à l'original :
    /// un plantage ou une coupure de courant laisse l'ancienne version intacte, jamais un fichier tronqué.
    /// Le temporaire finit en « .tmp » : le chargement (« *.json ») ne le lit pas.
    /// </summary>
    internal static void WriteAtomic(string path, string text)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
        {
            writer.Write(text);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        try
        {
            if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Certains volumes (réseau, synchronisés) refusent File.Replace : déplacement avec écrasement.
            File.Move(temp, path, overwrite: true);
        }
    }

    /// <summary>Envoie le fichier de la note à la corbeille de Windows : une suppression se rattrape.</summary>
    public void Delete(Note note)
    {
        var path = PathFor(note.Id);
        if (!File.Exists(path)) return;
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
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
    public static Note? ParseNote(string json) => ReadNote(json, out _);

    /// <summary>
    /// Lecture unique d'une note, pour le chargement comme pour l'import : migration d'avant les onglets,
    /// titre automatique d'avant ce réglage, titres vides, onglets manquants ou en double.
    /// <paramref name="repaired"/> indique qu'une correction mérite d'être réécrite sur disque.
    /// </summary>
    private static Note? ReadNote(string json, out bool repaired)
    {
        repaired = false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var n = document.RootElement.Deserialize<Note>(JsonOpts);
            if (n is null) return null;
            if (n.Id == Guid.Empty) { n.Id = Guid.NewGuid(); repaired = true; }

            n.EnsureTabs();
            if (n.MigrateLegacy()) repaired = true;

            if (!document.RootElement.TryGetProperty("autoTitle", out _) && n.Title == "Nouvelle note")
            {
                n.AutoTitle = true;
                n.Title = Note.TitleFromContent(n.Content);
                repaired = true;
            }
            if (string.IsNullOrWhiteSpace(n.Title)) { n.Title = "Sans titre"; repaired = true; }
            foreach (var tab in n.Tabs)
                if (string.IsNullOrWhiteSpace(tab.Title)) { tab.Title = n.NextTabTitle(); repaired = true; }
            return n;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException
                                  or ArgumentException or FormatException)
        {
            // contenu illisible : l'appelant compte l'élément ignoré
            return null;
        }
    }

    /// <summary>
    /// Import depuis des fichiers : un zip créé par « Archiver », ou des .json de note à l'unité.
    /// Une note identique à une note déjà présente (titre, onglets, textes) n'est pas réimportée.
    /// </summary>
    public ImportResult Import(IEnumerable<string> paths, IEnumerable<Note> existing)
    {
        var result = new ImportResult();
        var known = new HashSet<string>(existing.Select(Signature));
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
                        if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            || entry.Length > MaxImportEntryBytes) { result.Skipped++; continue; }
                        using var stream = entry.Open();
                        using var reader = new StreamReader(stream);
                        AddImported(result, known, reader.ReadToEnd());
                    }
                }
                else if (extension == ".json") AddImported(result, known, File.ReadAllText(path));
                else result.Skipped++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                result.Skipped++;
            }
        }
        return result;
    }

    /// <summary>
    /// Ajoute une note importée sous un identifiant neuf : une restauration n'écrase jamais une note existante.
    /// Ses dates de création et de modification sont conservées.
    /// </summary>
    private void AddImported(ImportResult result, HashSet<string> known, string json)
    {
        var note = ParseNote(json);
        if (note is null) { result.Skipped++; return; }
        if (!known.Add(Signature(note))) { result.Duplicates++; return; }
        note.Id = Guid.NewGuid();
        try
        {
            Save(note, touch: false);
            result.Notes.Add(note);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Skipped++;
        }
    }

    /// <summary>Empreinte du contenu d'une note : titre, puis titre, texte et texte enrichi de chaque onglet.</summary>
    private static string Signature(Note note)
    {
        var sb = new StringBuilder(note.Title);
        foreach (var tab in note.Tabs)
            sb.Append('\u0001').Append(tab.Title).Append('\u0002').Append(tab.Content).Append('\u0003').Append(tab.RichText);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public UiState? LoadUi()
    {
        try { return File.Exists(UiFile) ? JsonSerializer.Deserialize<UiState>(File.ReadAllText(UiFile), JsonOpts) : null; }
        catch { return null; }
    }

    public void SaveUi(UiState ui)
    {
        try { WriteAtomic(UiFile, JsonSerializer.Serialize(ui, JsonOpts)); }
        catch { /* fenêtre fermée : non critique */ }
    }
}

/// <summary>Ce qu'un import a rapporté : les notes créées, les doublons écartés, les éléments illisibles.</summary>
public sealed class ImportResult
{
    public List<Note> Notes { get; } = new();
    public int Skipped { get; set; }
    public int Duplicates { get; set; }
}

public sealed class UiState
{
    public int Width { get; set; } = 1180;
    public int Height { get; set; } = 720;
    /// <summary>Position de la fenêtre ; null tant qu'elle n'a jamais été enregistrée (peut être négative sur un écran à gauche).</summary>
    public int? Left { get; set; }
    public int? Top { get; set; }
    public bool Maximized { get; set; }
    public int Splitter { get; set; } = 300;
    public bool ConfirmBeforeDelete { get; set; } = true;
    public bool ShowLineNumbers { get; set; }
    public bool WordWrap { get; set; } = true;
    public List<Guid> NoteOrder { get; set; } = new();
}
