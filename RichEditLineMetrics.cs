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
    private const int DocumentRangeSlot = 24, RangeSetRangeSlot = 28, RangeGetPointSlot = 54;
    private nint _range;
    private readonly SetRange _setRange;
    private readonly GetPoint _getPoint;

    private RichEditLineMetrics(nint range)
    {
        _range = range;
        _setRange = Method<SetRange>(range, RangeSetRangeSlot);
        _getPoint = Method<GetPoint>(range, RangeGetPointSlot);
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
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint parameter, out nint value);
}
