using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Iljip.Services;

/// <summary>
/// 끌어낼 때 탐색기에 건네는 '가상 파일' 데이터(W2-04 — I1-4). OLE IDataObject 를 C# 에서 직접 구현한다.
/// <list type="bullet">
/// <item><c>FileGroupDescriptorW</c>(HGLOBAL): 고른 파일·폴더의 상대 경로(한글 이름은 UTF-16)·폴더 표시·크기·수정 시각. 목록 정보로 곧바로 만든다(꺼내지 않음).</item>
/// <item><c>FileContents</c>(lindex = i, ISTREAM — 작은 파일은 HGLOBAL 도): GetData 를 받은 '그 순간에' 꺼내(<see cref="LazyEntryExtractor"/> — 파일이 여럿이면
///   첫 요청 때 한 번에, 솔리드 압축이 느려지지 않게) i번 파일을 감싼 IStream 을 돌려준다(지연 렌더링 — 놓기 전에는 아무것도 꺼내지 않음).</item>
/// <item><c>Preferred DropEffect</c> = 복사.</item>
/// <item><c>ZoneIdentifier</c>(HGLOBAL DWORD — 셸 형식 CFSTR_ZONEIDENTIFIER): 원본 압축에 인터넷 표식이 있을 때만 그 영역 번호(3 = 인터넷). 탐색기가 놓은 곳에
///   새로 만드는 파일에 표식을 붙이게 한다 — 임시 파일의 표식은 내용 스트림으로는 옮겨지지 않아서(W2-04 1차 검증). 탐색기가 실제로 붙이는지는 손 점검.</item>
/// <item>비동기 모드(<see cref="IDataObjectAsyncCapability"/>, 처음부터 켬): 탐색기가 놓은 뒤 자기 스레드에서 데이터를 당겨 가 끌기가 곧바로 끝난다.
///   끝나면 탐색기가 EndOperation 을 불러 주고, 그때 임시 폴더를 지운다.</item>
/// </list>
/// CF_HDROP 지연 렌더링은 쓰지 않는다 — 탐색기가 DragEnter 때 CF_HDROP 을 달라고 해 다 꺼내 버릴 수 있어서.
/// 끌기는 WPF DragDrop 이 아니라 ole32 DoDragDrop 을 직접 부른다(<see cref="DoDragDrop"/>) — WPF DataObject 로 감싸면 탐색기가 비동기 모드를 찾지 못한다.
/// COM 메서드는 창 없이 직접 불러 시험한다(DragOutTests). 실제 탐색기와 주고받는 것은 손 점검(MANUAL_CHECKLIST 20절).
/// </summary>
[ComVisible(true)]
public sealed class VirtualFileDataObject : ComIDataObject, IDataObjectAsyncCapability, IDisposable
{
    public const string FileDescriptorFormatName = "FileGroupDescriptorW";
    public const string FileContentsFormatName = "FileContents";
    public const string PreferredDropEffectFormatName = "Preferred DropEffect";
    public const string ZoneIdentifierFormatName = "ZoneIdentifier";

    /// <summary>등록된 클립보드 형식 번호(RegisterClipboardFormat — 0xC000 이상이라 short 로는 음수).</summary>
    public static readonly short CfFileDescriptorW = Native.Register(FileDescriptorFormatName);
    public static readonly short CfFileContents = Native.Register(FileContentsFormatName);
    public static readonly short CfPreferredDropEffect = Native.Register(PreferredDropEffectFormatName);
    public static readonly short CfZoneIdentifier = Native.Register(ZoneIdentifierFormatName);

    /// <summary>FILEDESCRIPTORW 한 개의 크기(바이트).</summary>
    public const int DescriptorSize = 592;

    /// <summary>FileContents 를 HGLOBAL 로 줄 때의 상한(그보다 크면 ISTREAM 만).</summary>
    public const long MaxHGlobalBytes = 64L * 1024 * 1024;

    internal const uint FD_CLSID = 0x1, FD_ATTRIBUTES = 0x4, FD_WRITESTIME = 0x20, FD_FILESIZE = 0x40, FD_PROGRESSUI = 0x4000, FD_UNICODE = 0x80000000;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    internal const int S_OK = 0, S_FALSE = 1;
    internal const int DATA_S_SAMEFORMATETC = 0x00040130;
    internal const int DRAGDROP_S_DROP = 0x00040100, DRAGDROP_S_CANCEL = 0x00040101, DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
    internal const int E_NOTIMPL = unchecked((int)0x80004001), E_ABORT = unchecked((int)0x80004004), E_FAIL = unchecked((int)0x80004005);
    internal const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    internal const int DV_E_FORMATETC = unchecked((int)0x80040064), DV_E_LINDEX = unchecked((int)0x80040068), DV_E_TYMED = unchecked((int)0x80040069), DV_E_DVASPECT = unchecked((int)0x8004006B);

    private readonly LazyEntryExtractor _extractor;
    private readonly IReadOnlyList<DragItem> _items;
    private readonly int? _zoneId;
    private readonly Dictionary<short, byte[]> _stored = new();
    private bool _asyncMode = true;
    private volatile bool _inOperation;
    private int _finished;

    public VirtualFileDataObject(LazyEntryExtractor extractor)
    {
        _extractor = extractor;
        _items = extractor.Items;
        _zoneId = extractor.ZoneId;   // 원본 압축의 인터넷 표식(작은 대체 스트림 하나만 읽음 — 꺼내지 않음)
    }

    /// <summary>탐색기에 알릴 인터넷 표식 영역(원본 압축에 표식이 없으면 null — 'ZoneIdentifier' 형식을 주지 않음).</summary>
    public int? ZoneId => _zoneId;

    public LazyEntryExtractor Extractor => _extractor;

    public IReadOnlyList<DragItem> Items => _items;

    /// <summary>끌기가 끝나(취소·같은 스레드에서 다 읽음·비동기 EndOperation) 임시 폴더를 지운 뒤.</summary>
    public event Action<VirtualFileDataObject>? Finished;

    public bool IsFinished => Volatile.Read(ref _finished) != 0;

    /// <summary>
    /// ole32 DoDragDrop 으로 끌기(STA·UI 스레드에서, 마우스 왼쪽 단추를 누른 채로 불러야 함). 놓을 때까지 돌아오지 않는다.
    /// 비동기로 받는 곳(탐색기)이면 놓자마자 돌아오고 정리는 EndOperation 때, 아니면(취소·같은 스레드에서 다 읽음) 여기서 바로 정리한다.
    /// </summary>
    public DragDropEffects DoDragDrop(DragDropEffects allowed)
    {
        int effect = 0;
        int hr;
        try
        {
            hr = Native.DoDragDrop(this, new DropSource(), (int)allowed, out effect);
        }
        catch
        {
            Finish();
            throw;
        }
        if (!_inOperation) Finish();
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return hr == DRAGDROP_S_DROP ? (DragDropEffects)effect : DragDropEffects.None;
    }

    /// <summary>늘 주는 형식(GetData 로 받을 수 있는 것).</summary>
    internal static FORMATETC[] OwnFormats() => new[]
    {
        Format(CfFileDescriptorW, TYMED.TYMED_HGLOBAL, -1),
        Format(CfFileContents, TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL, -1),
        Format(CfPreferredDropEffect, TYMED.TYMED_HGLOBAL, -1),
    };

    /// <summary>이 끌기가 주는 형식: 늘 주는 것 + 원본 압축에 인터넷 표식이 있으면 'ZoneIdentifier'.</summary>
    private List<FORMATETC> Formats()
    {
        var list = OwnFormats().ToList();
        if (_zoneId is not null) list.Add(Format(CfZoneIdentifier, TYMED.TYMED_HGLOBAL, -1));
        return list;
    }

    internal static FORMATETC Format(short cf, TYMED tymed, int lindex) => new()
    {
        cfFormat = cf,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = lindex,
        ptd = IntPtr.Zero,
        tymed = tymed,
    };

    // ───────────────────────────── IDataObject ─────────────────────────────

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        medium = default;
        if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) throw Hr(DV_E_DVASPECT);

        if (format.cfFormat == CfFileDescriptorW)
        {
            RequireHGlobal(format);
            medium = HGlobalMedium(BuildFileGroupDescriptor(_items));
            return;
        }
        if (format.cfFormat == CfPreferredDropEffect)
        {
            RequireHGlobal(format);
            medium = HGlobalMedium(BitConverter.GetBytes((int)DragDropEffects.Copy));
            return;
        }
        if (format.cfFormat == CfZoneIdentifier && _zoneId is int zone)
        {
            RequireHGlobal(format);
            medium = HGlobalMedium(BitConverter.GetBytes(zone));
            return;
        }
        if (format.cfFormat == CfFileContents)
        {
            medium = GetContents(format);
            return;
        }
        byte[]? stored;
        lock (_stored) _stored.TryGetValue(format.cfFormat, out stored);
        if (stored is not null)
        {
            RequireHGlobal(format);
            medium = HGlobalMedium(stored);
            return;
        }
        throw Hr(DV_E_FORMATETC);
    }

    private STGMEDIUM GetContents(FORMATETC format)
    {
        int i = format.lindex;
        if (i < 0 || i >= _items.Count || _items[i].IsDirectory) throw Hr(DV_E_LINDEX);
        if ((format.tymed & (TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL)) == 0) throw Hr(DV_E_TYMED);

        string path;
        try
        {
            path = _extractor.GetContent(i);
        }
        catch (OperationCanceledException)
        {
            throw Hr(E_ABORT);
        }
        catch (Exception)
        {
            throw Hr(E_FAIL);   // 원인은 LazyEntryExtractor 가 로그로 남기고 화면에 알림
        }

        if ((format.tymed & TYMED.TYMED_ISTREAM) != 0)
        {
            var stream = new ComStream(path, _extractor.Track);
            return new STGMEDIUM
            {
                tymed = TYMED.TYMED_ISTREAM,
                unionmember = Marshal.GetComInterfaceForObject(stream, typeof(IStream)),
                pUnkForRelease = null!,
            };
        }
        if (new FileInfo(path).Length > MaxHGlobalBytes) throw Hr(DV_E_TYMED);
        return HGlobalMedium(File.ReadAllBytes(path));
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw Hr(E_NOTIMPL);

    public int QueryGetData(ref FORMATETC format)
    {
        if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) return DV_E_DVASPECT;
        if (format.cfFormat == CfFileDescriptorW || format.cfFormat == CfPreferredDropEffect || (format.cfFormat == CfZoneIdentifier && _zoneId is not null))
            return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? S_OK : DV_E_TYMED;
        if (format.cfFormat == CfFileContents)
            return (format.tymed & (TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL)) != 0 ? S_OK : DV_E_TYMED;
        lock (_stored)
            if (_stored.ContainsKey(format.cfFormat))
                return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? S_OK : DV_E_TYMED;
        return DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return DATA_S_SAMEFORMATETC;
    }

    /// <summary>받는 쪽이 넣어 주는 값(Performed DropEffect·DropDescription 등) — HGLOBAL 만 복사해 두고 GetData 로 돌려준다.</summary>
    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        if (medium.tymed != TYMED.TYMED_HGLOBAL || medium.unionmember == IntPtr.Zero) throw Hr(E_NOTIMPL);
        byte[] bytes = ReadHGlobal(medium.unionmember);
        lock (_stored) _stored[formatIn.cfFormat] = bytes;
        if (release) Native.ReleaseStgMedium(ref medium);
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) throw Hr(E_NOTIMPL);
        var list = Formats();
        lock (_stored)
            foreach (var cf in _stored.Keys)
                if (list.All(f => f.cfFormat != cf)) list.Add(Format(cf, TYMED.TYMED_HGLOBAL, -1));
        return new FormatEnumerator(list.ToArray());
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    public void DUnadvise(int connection) => throw Hr(OLE_E_ADVISENOTSUPPORTED);

    public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
    {
        enumAdvise = null!;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    // ───────────────────────────── 비동기 모드 ─────────────────────────────

    public void SetAsyncMode(bool fDoOpAsync) => _asyncMode = fDoOpAsync;

    public void GetAsyncMode(out bool pfIsOpAsync) => pfIsOpAsync = _asyncMode;

    public void StartOperation(IntPtr pbcReserved) => _inOperation = true;

    public void InOperation(out bool pfInAsyncOp) => pfInAsyncOp = _inOperation;

    public void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects)
    {
        _inOperation = false;
        Finish();
    }

    /// <summary>정리(넘긴 스트림 닫기·임시 폴더 지우기). 앱을 닫을 때도 부른다.</summary>
    public void Dispose() => Finish();

    private void Finish()
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0) return;
        _extractor.Dispose();
        Finished?.Invoke(this);
    }

    // ───────────────────────────── FILEGROUPDESCRIPTORW ─────────────────────────────

    /// <summary>
    /// FILEGROUPDESCRIPTORW 바이트: UINT cItems + FILEDESCRIPTORW[cItems](각 592바이트 — dwFlags·clsid·sizel·pointl·dwFileAttributes·
    /// 만든/연/고친 시각·크기 상·하위·cFileName[260] UTF-16). 파일은 FD_FILESIZE·FD_PROGRESSUI(탐색기 진행 창), 폴더는 FILE_ATTRIBUTE_DIRECTORY.
    /// </summary>
    public static byte[] BuildFileGroupDescriptor(IReadOnlyList<DragItem> items)
    {
        var buf = new byte[4 + items.Count * DescriptorSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var d = buf.AsSpan(4 + i * DescriptorSize, DescriptorSize);
            uint flags = FD_ATTRIBUTES | FD_UNICODE;
            if (!it.IsDirectory) flags |= FD_FILESIZE | FD_PROGRESSUI;
            long? ft = ToFileTime(it.LastModified);
            if (ft is not null) flags |= FD_WRITESTIME;
            BinaryPrimitives.WriteUInt32LittleEndian(d[0..], flags);
            BinaryPrimitives.WriteUInt32LittleEndian(d[36..], it.IsDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL);
            if (ft is { } t) BinaryPrimitives.WriteInt64LittleEndian(d[56..], t);
            ulong size = it.IsDirectory ? 0 : (ulong)Math.Max(0, it.Size);
            BinaryPrimitives.WriteUInt32LittleEndian(d[64..], (uint)(size >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(d[68..], (uint)(size & 0xFFFFFFFF));
            string name = it.RelativePath.Length > DragPayloadBuilder.MaxRelativePathLength
                ? it.RelativePath[..DragPayloadBuilder.MaxRelativePathLength]
                : it.RelativePath;
            Encoding.Unicode.GetBytes(name, d[72..(72 + name.Length * 2)]);
        }
        return buf;
    }

    private static long? ToFileTime(DateTime? value)
    {
        if (value is not { } v) return null;
        try { return v.Kind == DateTimeKind.Utc ? v.ToFileTimeUtc() : v.ToFileTime(); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // ───────────────────────────── 도우미 ─────────────────────────────

    private static void RequireHGlobal(FORMATETC format)
    {
        if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) throw Hr(DV_E_TYMED);
    }

    private static COMException Hr(int hr) => new(null, hr);

    internal static STGMEDIUM HGlobalMedium(byte[] data)
    {
        IntPtr h = Native.GlobalAlloc(Native.GMEM_MOVEABLE | Native.GMEM_ZEROINIT, (UIntPtr)Math.Max(1, data.Length));
        if (h == IntPtr.Zero) throw new OutOfMemoryException();
        IntPtr p = Native.GlobalLock(h);
        try
        {
            if (data.Length > 0) Marshal.Copy(data, 0, p, data.Length);
        }
        finally
        {
            Native.GlobalUnlock(h);
        }
        return new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = h, pUnkForRelease = null! };
    }

    /// <summary>HGLOBAL 의 내용 전체(시험도 씀).</summary>
    internal static byte[] ReadHGlobal(IntPtr h)
    {
        int size = (int)(ulong)Native.GlobalSize(h);
        var bytes = new byte[size];
        IntPtr p = Native.GlobalLock(h);
        try
        {
            if (size > 0) Marshal.Copy(p, bytes, 0, size);
        }
        finally
        {
            Native.GlobalUnlock(h);
        }
        return bytes;
    }

    /// <summary>STGMEDIUM 놓기(받은 쪽 몫 — 시험이 GetData 결과를 정리할 때).</summary>
    internal static void Release(ref STGMEDIUM medium) => Native.ReleaseStgMedium(ref medium);

    /// <summary>끄는 쪽(드롭 소스): Esc 면 취소, 왼쪽 단추를 놓으면 놓기.</summary>
    [ComVisible(true)]
    private sealed class DropSource : IOleDropSource
    {
        private const int MK_LBUTTON = 0x0001;

        public int QueryContinueDrag(bool fEscapePressed, int grfKeyState)
        {
            if (fEscapePressed) return DRAGDROP_S_CANCEL;
            if ((grfKeyState & MK_LBUTTON) == 0) return DRAGDROP_S_DROP;
            return S_OK;
        }

        public int GiveFeedback(int dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }

    internal static class Native
    {
        internal const uint GMEM_MOVEABLE = 0x0002, GMEM_ZEROINIT = 0x0040;

        internal static short Register(string name)
        {
            uint id = RegisterClipboardFormatW(name);
            if (id == 0) throw new InvalidOperationException("클립보드 형식을 등록하지 못함: " + name);
            return unchecked((short)id);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterClipboardFormatW(string lpszFormat);

        [DllImport("ole32.dll")]
        internal static extern int DoDragDrop(ComIDataObject pDataObj, IOleDropSource pDropSource, int dwOKEffects, out int pdwEffect);

        [DllImport("ole32.dll")]
        internal static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern UIntPtr GlobalSize(IntPtr hMem);
    }
}

/// <summary>IDataObjectAsyncCapability(예전 이름 IAsyncOperation) — 받는 쪽이 자기 스레드에서 데이터를 당겨 가는 비동기 끌어 놓기.</summary>
[ComImport, Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDataObjectAsyncCapability
{
    void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool fDoOpAsync);

    void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool pfIsOpAsync);

    void StartOperation(IntPtr pbcReserved);

    void InOperation([MarshalAs(UnmanagedType.Bool)] out bool pfInAsyncOp);

    void EndOperation(int hResult, IntPtr pbcReserved, uint dwEffects);
}

/// <summary>OLE IDropSource.</summary>
[ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleDropSource
{
    [PreserveSig]
    int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, int grfKeyState);

    [PreserveSig]
    int GiveFeedback(int dwEffect);
}

/// <summary>형식 목록 열거자(EnumFormatEtc).</summary>
[ComVisible(true)]
internal sealed class FormatEnumerator : IEnumFORMATETC
{
    private readonly FORMATETC[] _formats;
    private int _pos;

    public FormatEnumerator(FORMATETC[] formats, int pos = 0)
    {
        _formats = formats;
        _pos = pos;
    }

    public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
    {
        int n = 0;
        while (n < celt && _pos < _formats.Length && rgelt is not null && n < rgelt.Length)
            rgelt[n++] = _formats[_pos++];
        if (pceltFetched is { Length: > 0 }) pceltFetched[0] = n;
        return n == celt ? VirtualFileDataObject.S_OK : VirtualFileDataObject.S_FALSE;
    }

    public int Skip(int celt)
    {
        _pos = Math.Min(_formats.Length, _pos + Math.Max(0, celt));
        return _pos < _formats.Length ? VirtualFileDataObject.S_OK : VirtualFileDataObject.S_FALSE;
    }

    public int Reset()
    {
        _pos = 0;
        return VirtualFileDataObject.S_OK;
    }

    public void Clone(out IEnumFORMATETC newEnum) => newEnum = new FormatEnumerator(_formats, _pos);
}

/// <summary>꺼낸 파일을 읽기 전용 IStream 으로(탐색기가 FileContents 를 읽어 감). <see cref="Dispose"/> 는 끌기가 끝날 때 꺼내기 일꾼이 부른다.</summary>
[ComVisible(true)]
internal sealed class ComStream : IStream, IDisposable
{
    private const int STG_E_ACCESSDENIED = unchecked((int)0x80030005);
    private const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);
    private const int STATFLAG_NONAME = 1;
    private const int STGTY_STREAM = 2;

    private readonly string _path;
    private readonly FileStream _fs;
    private readonly Action<IDisposable>? _track;

    public ComStream(string path, Action<IDisposable>? track)
    {
        _path = path;
        _track = track;
        _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        track?.Invoke(this);
    }

    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        int total = 0;
        while (total < cb)
        {
            int n = _fs.Read(pv, total, cb - total);
            if (n == 0) break;
            total += n;
        }
        if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, total);
    }

    public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new COMException(null, STG_E_ACCESSDENIED);

    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        long pos = _fs.Seek(dlibMove, (SeekOrigin)dwOrigin);
        if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
    }

    public void SetSize(long libNewSize) => throw new COMException(null, STG_E_ACCESSDENIED);

    public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        var buf = new byte[81920];
        long done = 0;
        while (done < cb)
        {
            int n = _fs.Read(buf, 0, (int)Math.Min(buf.Length, cb - done));
            if (n == 0) break;
            pstm.Write(buf, n, IntPtr.Zero);
            done += n;
        }
        if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, done);
        if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, done);
    }

    public void Commit(int grfCommitFlags)
    {
    }

    public void Revert() => throw new COMException(null, STG_E_INVALIDFUNCTION);

    public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, STG_E_INVALIDFUNCTION);

    public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, STG_E_INVALIDFUNCTION);

    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        long mtime = 0;
        try { mtime = File.GetLastWriteTimeUtc(_path).ToFileTimeUtc(); } catch { /* 모름 */ }
        pstatstg = new STATSTG
        {
            type = STGTY_STREAM,
            cbSize = _fs.Length,
            grfMode = 0,   // STGM_READ
            pwcsName = (grfStatFlag & STATFLAG_NONAME) != 0 ? null! : Path.GetFileName(_path),
            mtime = new System.Runtime.InteropServices.ComTypes.FILETIME { dwLowDateTime = (int)(mtime & 0xFFFFFFFF), dwHighDateTime = (int)(mtime >> 32) },
        };
    }

    public void Clone(out IStream ppstm)
    {
        var c = new ComStream(_path, _track);
        c._fs.Position = _fs.Position;
        ppstm = c;
    }

    public void Dispose() => _fs.Dispose();
}
