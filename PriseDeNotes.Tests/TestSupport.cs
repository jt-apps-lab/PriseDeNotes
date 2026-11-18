using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PriseDeNotes.Tests;

/// <summary>Dossier de données jetable : les tests ne touchent jamais aux vraies notes de %APPDATA%.</summary>
public sealed class TempStore : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "PriseDeNotes.Tests", Guid.NewGuid().ToString("N"));

    public NoteStore Open() => new(Root);

    public string NotesFolder => Path.Combine(Root, "notes");
    public string UiFile => Path.Combine(Root, "ui.json");

    /// <summary>Écrit un fichier de note brut, comme le ferait une ancienne version ou une édition à la main.</summary>
    public string WriteRaw(Guid id, string json)
    {
        Directory.CreateDirectory(NotesFolder);
        var path = Path.Combine(NotesFolder, id.ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Accès aux membres privés du formulaire, pour piloter et observer l'interface dans les tests.</summary>
internal static class Reflect
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, All)!.GetValue(target)!;

    public static object? Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, All)!.Invoke(target, args);

    public static object? CallStatic(Type type, string method, params object[] args) =>
        type.GetMethod(method, All)!.Invoke(null, args);
}

/// <summary>
/// Lance un formulaire sur un thread STA avec sa boucle de messages, déroule le scénario une fois la fenêtre
/// affichée, puis la ferme. Une assertion qui échoue dans le scénario fait échouer le test.
/// </summary>
internal static class Ui
{
    public static void Run<TForm>(Func<TForm> create, Func<TForm, Task> scenario) where TForm : Form
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            Application.EnableVisualStyles();
            var form = create();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Delay(300);   // première mise en page et peinture
                    await scenario(form);
                }
                catch (Exception e) { error = e; }
                finally { form.Close(); }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(90))) throw new TimeoutException("La fenêtre de test ne s'est pas fermée.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}

/// <summary>Les tests d'interface ouvrent de vraies fenêtres : ils s'exécutent un par un.</summary>
[CollectionDefinition("Interface", DisableParallelization = true)]
public sealed class InterfaceCollection { }
