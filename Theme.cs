namespace PriseDeNotes;

/// <summary>Palette sombre unique de l'application.</summary>
public static class Theme
{
    public static readonly Color Window   = Color.FromArgb(30, 30, 30);   // fond général
    public static readonly Color Surface   = Color.FromArgb(37, 37, 38);   // panneau latéral
    public static readonly Color Surface2  = Color.FromArgb(45, 45, 48);   // champs, boutons
    public static readonly Color Selection = Color.FromArgb(56, 58, 62);   // ligne sélectionnée
    public static readonly Color Accent    = Color.FromArgb(86, 156, 214); // liseré, liens
    public static readonly Color Text      = Color.FromArgb(226, 226, 226);
    public static readonly Color TextDim   = Color.FromArgb(148, 148, 148);
    public static readonly Color Border    = Color.FromArgb(62, 62, 66);
    public static readonly Color Danger    = Color.FromArgb(196, 74, 74);

    public const string DefaultNoteBack = "#1E1E1E";
    public const string DefaultNoteFore = "#E2E2E2";
}

public static class ColorExt
{
    /// <summary>#RRGGBB -> Color (repli sur <paramref name="fallback"/> si invalide).</summary>
    public static Color ToColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try
        {
            var c = ColorTranslator.FromHtml(hex);
            return c.IsEmpty ? fallback : c;
        }
        catch { return fallback; }
    }

    public static string ToHex(this Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
