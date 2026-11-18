using System.Drawing;
using System.Runtime.InteropServices;

namespace PriseDeNotes;

internal static class RichTextFormatting
{
    private const uint Face = 0x20000000, Size = 0x80000000;
    private const uint CFMColor = 0x40000000;
    private const uint InSelection = 1, InAll = 4; // SCF_SELECTION, SCF_ALL

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CharFormat
    {
        public uint cbSize, mask, effects;
        public int height, offset, color;
        public byte charSet, pitch;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string face;
    }

    private static CharFormat EmptyFormat() => new()
    {
        cbSize = (uint)Marshal.SizeOf<CharFormat>(),
        face = "",
    };

    public static bool HasStyle(RichTextBox editor, FontStyle style)
    {
        var format = EmptyFormat();
        SendMessage(editor.Handle, 0x043A, (IntPtr)InSelection, ref format); // EM_GETCHARFORMAT, sélection
        uint flag = (uint)style;
        return (format.mask & flag) != 0 && (format.effects & flag) != 0;
    }

    public static void ToggleStyle(RichTextBox editor, FontStyle style)
    {
        bool enabled = HasStyle(editor, style);
        var format = EmptyFormat();
        format.mask = (uint)style;
        format.effects = enabled ? 0 : (uint)style;
        SendMessage(editor.Handle, 0x0444, (IntPtr)InSelection, ref format); // EM_SETCHARFORMAT, sélection
    }

    /// <summary>Police et taille du texte sélectionné, ou du point d'insertion. Un champ mélangé ou absent vaut null.</summary>
    public static (string? family, float? points) GetFont(RichTextBox editor)
    {
        var format = EmptyFormat();
        SendMessage(editor.Handle, 0x043A, (IntPtr)InSelection, ref format); // EM_GETCHARFORMAT, sélection
        string? family = (format.mask & Face) != 0 && !string.IsNullOrEmpty(format.face) ? format.face : null;
        float? points = (format.mask & Size) != 0 && format.height > 0 ? format.height / 20f : null;
        return (family, points);
    }

    /// <param name="toSelection">true : ne touche que la sélection (ou le point d'insertion), false : toute la note.</param>
    public static void SetFont(RichTextBox editor, string family, float points, bool toSelection)
    {
        var format = EmptyFormat();
        format.mask = Face | Size;
        format.face = family;
        format.height = (int)Math.Round(Math.Clamp(points, 6f, 72f) * 20);
        // Ne change que la famille et la taille, sans effacer les styles des passages.
        SendMessage(editor.Handle, 0x0444, (IntPtr)(toSelection ? InSelection : InAll), ref format);
    }

    /// <summary>Couleur du texte sélectionné, ou du point d'insertion. null si mélangée ou couleur par défaut.</summary>
    public static Color? GetColor(RichTextBox editor)
    {
        var format = EmptyFormat();
        SendMessage(editor.Handle, 0x043A, (IntPtr)InSelection, ref format); // EM_GETCHARFORMAT, sélection
        if ((format.mask & CFMColor) == 0) return null;
        if ((format.effects & CFMColor) != 0) return null; // CFE_AUTOCOLOR
        return ColorTranslator.FromWin32(format.color);
    }

    /// <param name="toSelection">true : ne touche que la sélection (ou le point d'insertion), false : toute la note.</param>
    public static void SetColor(RichTextBox editor, Color color, bool toSelection)
    {
        var format = EmptyFormat();
        format.mask = CFMColor;
        format.color = ColorTranslator.ToWin32(color);
        SendMessage(editor.Handle, 0x0444, (IntPtr)(toSelection ? InSelection : InAll), ref format);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextRange
    {
        public int cpMin, cpMax;
        public IntPtr text;
    }

    /// <summary>Les <paramref name="maxChars"/> premiers caractères du texte, sans copier tout le contenu (EM_GETTEXTRANGE).</summary>
    public static string GetTextStart(RichTextBox editor, int maxChars)
    {
        int length = Math.Min(editor.TextLength, maxChars);
        if (length <= 0) return "";
        IntPtr buffer = Marshal.AllocHGlobal((length + 1) * sizeof(char));
        try
        {
            var range = new TextRange { cpMin = 0, cpMax = length, text = buffer };
            int copied = (int)SendMessage(editor.Handle, 0x044B, IntPtr.Zero, ref range); // EM_GETTEXTRANGE
            return copied > 0 ? Marshal.PtrToStringUni(buffer, Math.Min(copied, length)) : "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, ref CharFormat format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, ref TextRange range);
}
