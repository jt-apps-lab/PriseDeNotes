namespace PriseDeNotes;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
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

        Application.Run(new MainForm(store));
    }
}
