using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PriseDeNotes;

internal static class Program
{
    /// <summary>Nom du verrou d'instance : un seul par session Windows (préfixe Local\).</summary>
    private const string InstanceMutexName = @"Local\PriseDeNotes.Instance";
    private const long MaxLogBytes = 1024 * 1024;

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
        if (args.Contains("--diag"))
        {
            var f = new MainForm(new NoteStore());
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

        // Créé après le verrou : la récupération des fichiers .tmp ne doit pas courir pendant
        // qu'une autre instance écrit les siens.
        Application.Run(new MainForm(new NoteStore()));
    }

    private static void ReportError(Exception error, bool canContinue)
    {
        string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PriseDeNotes", "erreurs.log");
        try
        {
            // Journal borné : au-delà de 1 Mo, il devient erreurs.old.log (l'ancien est remplacé).
            var info = new FileInfo(log);
            if (info.Exists && info.Length > MaxLogBytes)
                File.Move(log, Path.ChangeExtension(log, ".old.log"), overwrite: true);
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {error}{Environment.NewLine}{Environment.NewLine}");
        }
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

    /// <summary>
    /// Ramène au premier plan la fenêtre de l'instance déjà lancée. Elle peut être encore en train de
    /// s'ouvrir : on la cherche pendant quelques secondes. Introuvable, on le dit au lieu de se fermer sans rien montrer.
    /// </summary>
    private static void ActivateRunningInstance()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (TryActivateOtherWindow()) return;
            Thread.Sleep(250);
        }
        MessageBox.Show(
            "Prise de notes est déjà ouverte, mais sa fenêtre ne répond pas.\n\n"
            + "Attendez quelques secondes et réessayez, ou fermez « PriseDeNotes » dans le Gestionnaire des tâches.",
            "Prise de notes", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool TryActivateOtherWindow()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var other in Process.GetProcessesByName(current.ProcessName))
        {
            using (other)
            {
                if (other.Id == current.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9); // SW_RESTORE
                SetForegroundWindow(other.MainWindowHandle);
                return true;
            }
        }
        return false;
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
