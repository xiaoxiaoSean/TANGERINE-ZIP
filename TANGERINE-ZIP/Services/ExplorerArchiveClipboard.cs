using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using IDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace TANGERINE_ZIP.Services;

// Exposes extracted files through CF_HDROP. A cut is finalized only after the
// shell explicitly reports a successful MOVE; merely reading the clipboard
// must never remove members from the source archive.
internal sealed class ExplorerArchiveClipboard
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const string PasteSucceeded = "Paste Succeeded";
    private const string PerformedDropEffect = "Performed DropEffect";
    private const string LogicalPerformedDropEffect = "Logical Performed DropEffect";
    private readonly ShellDataObject _cutObject;
    [ThreadStatic] private static bool _oleReady;

    private ExplorerArchiveClipboard(ShellDataObject cutObject) => _cutObject = cutObject;

    public static ExplorerArchiveClipboard PutFiles(IEnumerable<string> paths, bool cut, Action onMoveSucceeded)
    {
        if (!_oleReady)
        {
            int initialized = OleInitialize(IntPtr.Zero);
            if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
            _oleReady = true;
        }
        StringCollection files = new();
        files.AddRange(paths.ToArray());
        DataObject data = new();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
        ShellDataObject wrapped = new(data, cut ? onMoveSucceeded : null);
        int result = OleSetClipboard(wrapped);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
        // Keep the COM owner alive for subsequent shell SetData callbacks.
        return new ExplorerArchiveClipboard(wrapped);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int OleSetClipboard([MarshalAs(UnmanagedType.Interface)] IDataObject dataObject);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int OleInitialize(IntPtr reserved);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string format);

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ShellDataObject : IDataObject
    {
        private readonly IDataObject _inner;
        private readonly Action? _onMoveSucceeded;
        private readonly short _pasteSucceededFormat;
        private readonly short _performedDropEffectFormat;
        private readonly short _logicalPerformedDropEffectFormat;
        private bool _moveReported;

        public ShellDataObject(DataObject inner, Action? onMoveSucceeded)
        {
            _inner = (IDataObject)inner;
            _onMoveSucceeded = onMoveSucceeded;
            _pasteSucceededFormat = unchecked((short)RegisterClipboardFormat(PasteSucceeded));
            _performedDropEffectFormat = unchecked((short)RegisterClipboardFormat(PerformedDropEffect));
            _logicalPerformedDropEffectFormat = unchecked((short)RegisterClipboardFormat(LogicalPerformedDropEffect));
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium) => _inner.GetData(ref format, out medium);
        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => _inner.GetDataHere(ref format, ref medium);
        public int QueryGetData(ref FORMATETC format) => _inner.QueryGetData(ref format);
        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut) =>
            _inner.GetCanonicalFormatEtc(ref formatIn, out formatOut);

        public void SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release)
        {
            if (format.cfFormat == _pasteSucceededFormat ||
                format.cfFormat == _performedDropEffectFormat ||
                format.cfFormat == _logicalPerformedDropEffectFormat)
            {
                if (_onMoveSucceeded is not null && !_moveReported &&
                    format.cfFormat == _pasteSucceededFormat && medium.tymed == TYMED.TYMED_HGLOBAL &&
                    medium.unionmember != IntPtr.Zero && Marshal.ReadInt32(medium.unionmember) == 2)
                {
                    _moveReported = true;
                    _onMoveSucceeded();
                }
                if (release) ReleaseStgMedium(ref medium);
                return;
            }
            _inner.SetData(ref format, ref medium, release);
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => direction == DATADIR.DATADIR_SET
            ? new SetFormatEnumerator([_pasteSucceededFormat, _performedDropEffectFormat,
                _logicalPerformedDropEffectFormat])
            : _inner.EnumFormatEtc(direction);
        public int DAdvise(ref FORMATETC format, ADVF advf, IAdviseSink sink, out int connection) =>
            _inner.DAdvise(ref format, advf, sink, out connection);
        public void DUnadvise(int connection) => _inner.DUnadvise(connection);
        public int EnumDAdvise(out IEnumSTATDATA? enumerator) => _inner.EnumDAdvise(out enumerator);
    }

    private sealed class SetFormatEnumerator : IEnumFORMATETC
    {
        private readonly short[] _formats;
        private int _position;

        public SetFormatEnumerator(short[] formats, int position = 0)
        {
            _formats = formats;
            _position = position;
        }

        public int Next(int count, FORMATETC[] formats, int[] fetched)
        {
            int actual = 0;
            while (actual < count && _position < _formats.Length)
            {
                formats[actual++] = new FORMATETC
                {
                    cfFormat = _formats[_position++],
                    dwAspect = DVASPECT.DVASPECT_CONTENT,
                    lindex = -1,
                    tymed = TYMED.TYMED_HGLOBAL
                };
            }
            if (fetched.Length > 0) fetched[0] = actual;
            return actual == count ? 0 : 1;
        }

        public int Skip(int count)
        {
            _position = Math.Min(_position + count, _formats.Length);
            return _position < _formats.Length ? 0 : 1;
        }

        public int Reset() { _position = 0; return 0; }
        public void Clone(out IEnumFORMATETC clone) => clone = new SetFormatEnumerator(_formats, _position);
    }
}
