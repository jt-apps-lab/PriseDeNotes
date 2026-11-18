using System.Runtime.InteropServices;

namespace PriseDeNotes;

/// <summary>
/// Lit la ligne de base dans le modèle de texte natif de RichEdit, sans toucher à la sélection.
/// Le range privé reste attaché au handle qui l'a créé et est libéré avec celui-ci.
/// </summary>
internal sealed class RichEditLineMetrics : IDisposable
{
    private const uint GetOleInterface = 0x043C;
    private const int Start = 32, ClientCoordinates = 256, AllowOffClient = 512, Baseline = 24;
    // Emplacements du contrat COM TOM (IUnknown + IDispatch + méthodes de TOM.h).
    // https://learn.microsoft.com/windows/win32/api/tom/nf-tom-itextrange-getpoint
    private const int DocumentRangeSlot = 24, RangeGetCharSlot = 9, RangeGetIndexSlot = 26,
        RangeSetRangeSlot = 28, RangeGetPointSlot = 54;
    private const int TomParagraph = 4;
    private nint _range;
    private readonly SetRange _setRange;
    private readonly GetPoint _getPoint;
    private readonly GetChar _getChar;
    private readonly GetIndex _getIndex;

    private RichEditLineMetrics(nint range)
    {
        _range = range;
        _setRange = Method<SetRange>(range, RangeSetRangeSlot);
        _getPoint = Method<GetPoint>(range, RangeGetPointSlot);
        _getChar = Method<GetChar>(range, RangeGetCharSlot);
        _getIndex = Method<GetIndex>(range, RangeGetIndexSlot);
    }

    /// <summary>Numéro (à partir de 1) du paragraphe qui contient la position <paramref name="index"/>.</summary>
    public bool TryGetParagraphNumber(int index, out int number)
    {
        number = 0;
        if (_range == 0 || _setRange(_range, index, index) < 0) return false;
        return _getIndex(_range, TomParagraph, out number) == 0 && number > 0;
    }

    /// <summary>
    /// La position <paramref name="index"/> commence-t-elle un paragraphe ? Vrai au début du texte,
    /// ou si le caractère précédent est une fin de paragraphe (une coupure automatique n'en est pas une).
    /// </summary>
    public bool TryStartsParagraph(int index, out bool starts)
    {
        starts = index <= 0;
        if (starts) return true;
        if (_range == 0 || _setRange(_range, index - 1, index) < 0 || _getChar(_range, out int ch) != 0) return false;
        starts = ch is 0x0D or 0x0A or 0x2029; // CR, LF, séparateur de paragraphe Unicode
        return true;
    }

    public static RichEditLineMetrics? Create(nint editor)
    {
        if (SendMessage(editor, GetOleInterface, 0, out nint ole) == 0 || ole == 0) return null;
        nint document = 0, range = 0;
        try
        {
            var id = new Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"); // ITextDocument
            if (Marshal.QueryInterface(ole, ref id, out document) < 0) return null;
            if (Method<CreateRange>(document, DocumentRangeSlot)(document, 0, 0, out range) < 0 || range == 0)
                return null;
            var metrics = new RichEditLineMetrics(range);
            range = 0; // la propriété du pointeur est transférée à metrics
            return metrics;
        }
        finally
        {
            if (range != 0) Marshal.Release(range);
            if (document != 0) Marshal.Release(document);
            Marshal.Release(ole);
        }
    }

    public bool TryGetBaseline(int index, out int y)
    {
        y = 0;
        // Un caractère plutôt qu'un point d'insertion ambigu à une coupure automatique.
        // RichEdit borne lui-même la fin à la longueur de son texte.
        if (_range == 0 || _setRange(_range, index, index + 1) < 0) return false;
        return _getPoint(_range, Start | ClientCoordinates | AllowOffClient | Baseline, out _, out y) == 0;
    }

    public void Dispose()
    {
        if (_range == 0) return;
        Marshal.Release(_range);
        _range = 0;
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateRange(nint document, int start, int end, out nint range);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetRange(nint range, int start, int end);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPoint(nint range, int flags, out int x, out int y);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetChar(nint range, out int character);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetIndex(nint range, int unit, out int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint parameter, out nint value);
}
