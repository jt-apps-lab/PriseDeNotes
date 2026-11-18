using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PriseDeNotes;

internal static class Program
{
    /// <summary>Nom du verrou d'instance : un seul par session Windows (préfixe Local\).</summary>
    private const string InstanceMutexName = @"Local\PriseDeNotes.Instance";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Dernier filet : une erreur imprévue est consignée dans erreurs.log et signalée simplement,
        // au lieu de la boîte technique de WinForms. Les notes déjà saisies restent en mémoire.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception, canContinue: true);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ReportError(ex, canContinue: false);
        };
        var store = new NoteStore();

        if (args.Contains("--diag"))
        {
            var f = new MainForm(store);
            f.Shown += (_, _) =>
            {
                var dir = Environment.GetEnvironmentVariable("TMPDIR");
                if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                var path = Path.Combine(dir, "prisedenotes_diag.txt");
                File.WriteAllText(path, f.Diagnostics());
                f.Close();
            };
            Application.Run(f);
            return;
        }

        // Deux fenêtres ouvertes réécriraient chacune leur copie des notes : la dernière écraserait l'autre.
        // Une seconde instance ramène donc la première au premier plan, puis se ferme.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out bool first);
        if (!first)
        {
            ActivateRunningInstance();
            return;
        }

        Application.Run(new MainForm(store));
    }

    private static void ReportError(Exception error, bool canContinue)
    {
        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PriseDeNotes", "erreurs.log");
        try { File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {error}{Environment.NewLine}{Environment.NewLine}"); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        try
        {
            MessageBox.Show(
                "Une erreur inattendue s'est produite :\n" + error.Message + "\n\n"
                + (canContinue ? "L'application continue. " : "L'application va se fermer. ")
                + "Le détail est enregistré dans :\n" + log,
                "Prise de notes", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { /* affichage impossible : le journal suffit */ }
    }

    private static void ActivateRunningInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
}
