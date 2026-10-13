using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Iljip.Services;

/// <summary>휴지통으로 보낸 결과. <see cref="Succeeded"/> 가 false 면 하나도(또는 일부를) 못 보냈고 그 파일은 그대로다 — 영구 삭제는 하지 않는다.</summary>
public sealed record RecycleResult(bool Succeeded, string? Error = null)
{
    public static readonly RecycleResult Ok = new(true);
}

/// <summary>
/// 휴지통(설계 §5 — '풀고 나서 압축 파일 지우기' I2-5, W2-05). 앱이 사용자 파일을 지울 때는 늘 이것으로 휴지통에만 보낸다 — 영구 삭제 금지.
/// 진짜는 <see cref="RecycleBin"/>(IFileOperation), 시험은 기록만 하는 가짜(Iljip.Tests/TestSupport/FakeRecycleBin) — 시험은 진짜 휴지통을 절대 부르지 않는다.
/// </summary>
public interface IRecycleBin
{
    /// <summary>
    /// 이 파일을 휴지통에 넣을 수 있는 곳인가. 네트워크 드라이브·공유 폴더(\\서버\…)·USB 같은 이동식 드라이브는 휴지통이 없어
    /// 지우면 영구 삭제가 되므로 false. 휴지통을 끈 드라이브·휴지통보다 큰 파일도 false.
    /// </summary>
    bool IsRecyclable(string path);

    /// <summary>휴지통으로 보낸다. 못 보내면 영구 삭제하지 않고 그대로 둔 채 실패를 돌려준다(예외를 던지지 않음).</summary>
    RecycleResult Send(IReadOnlyList<string> paths);
}

/// <summary>
/// 진짜 휴지통 — Windows IFileOperation(FOFX_RECYCLEONDELETE | FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI).
/// 휴지통에 못 들어갈 파일은 먼저 걸러 보내지 않는다(<see cref="IsRecyclable"/> — 드라이브 종류·휴지통 꺼짐·크기 한도). 그래도 Windows 가 영구 삭제로
/// 바꾸려 하면 FOF_WANTNUKEWARNING 으로 사용자에게 묻게 한다(조용히 영구 삭제되는 일이 없게 — 마지막 안전망).
/// IFileOperation 은 STA 스레드에서만 되므로, 다른 스레드에서 부르면 STA 스레드를 잠깐 만들어 거기서 한다.
/// </summary>
public sealed class RecycleBin : IRecycleBin
{
    public bool IsRecyclable(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;   // 공유 폴더(\\서버\…)·장치 경로
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return false;
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed) return false;               // 네트워크·이동식·CD 등
            if (RecycleDisabledByPolicy()) return false;
            var (nuke, maxBytes) = VolumeSettings(root, drive);
            if (nuke) return false;
            long size = File.Exists(full) ? new FileInfo(full).Length : 0;
            return size <= maxBytes;
        }
        catch
        {
            return false;   // 모르면 지우지 않는다
        }
    }

    public RecycleResult Send(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return RecycleResult.Ok;
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return SendCore(paths);
        RecycleResult? result = null;
        var thread = new Thread(() => result = SendCore(paths)) { IsBackground = true, Name = "IljipRecycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result ?? new RecycleResult(false, "휴지통으로 옮기지 못했어요.");
    }

    private RecycleResult SendCore(IReadOnlyList<string> paths)
    {
        foreach (var p in paths)
            if (!IsRecyclable(p)) return new RecycleResult(false, $"'{Path.GetFileName(p)}' 은(는) 휴지통에 넣을 수 없는 곳(네트워크·이동식 드라이브 등)에 있어요.");

        IFileOperation? op = null;
        var items = new List<IShellItem>();
        try
        {
            op = (IFileOperation)new FileOperationClass();
            op.SetOperationFlags(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING | FOFX_RECYCLEONDELETE);
            foreach (var p in paths)
            {
                var iid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(Path.GetFullPath(p), IntPtr.Zero, ref iid, out IShellItem item);
                items.Add(item);
                op.DeleteItem(item, IntPtr.Zero);
            }
            op.PerformOperations();
            if (op.GetAnyOperationsAborted())
                return new RecycleResult(false, "휴지통으로 옮기는 중에 멈췄어요.");
            var left = paths.Where(File.Exists).ToList();
            return left.Count == 0
                ? RecycleResult.Ok
                : new RecycleResult(false, $"'{Path.GetFileName(left[0])}' 을(를) 휴지통으로 옮기지 못했어요.");
        }
        catch (Exception ex)
        {
            Logger.LogError("휴지통으로 옮기기 실패", ex);
            return new RecycleResult(false, "휴지통으로 옮기지 못했어요(다른 프로그램이 쓰고 있을 수 있어요).");
        }
        finally
        {
            foreach (var it in items)
            {
                try { Marshal.ReleaseComObject(it); } catch { /* 무시 */ }
            }
            if (op is not null)
            {
                try { Marshal.ReleaseComObject(op); } catch { /* 무시 */ }
            }
        }
    }

    /// <summary>그룹 정책 '지운 파일을 휴지통으로 옮기지 않음'(NoRecycleFiles)이 켜져 있는가.</summary>
    private static bool RecycleDisabledByPolicy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer");
        return key?.GetValue("NoRecycleFiles") is int v && v != 0;
    }

    /// <summary>
    /// 드라이브의 휴지통 설정(사용자별): 휴지통을 끄고 바로 지우기(NukeOnDelete), 최대 크기(MaxCapacity, MB).
    /// 값이 없으면 Windows 기본(켬, 드라이브의 약 5%)으로 본다 — 실제 기본보다 작게 잡아 넘칠 일을 피한다.
    /// </summary>
    private static (bool Nuke, long MaxBytes) VolumeSettings(string root, DriveInfo drive)
    {
        long fallback = Math.Max(0, drive.TotalSize / 20);
        var sb = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPoint(root.EndsWith('\\') ? root : root + "\\", sb, sb.Capacity)) return (false, fallback);
        string volume = sb.ToString();
        int a = volume.IndexOf('{');
        int b = volume.IndexOf('}');
        if (a < 0 || b <= a) return (false, fallback);
        string guid = volume[a..(b + 1)];
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid);
        if (key is null) return (false, fallback);
        bool nuke = key.GetValue("NukeOnDelete") is int n && n != 0;
        long max = key.GetValue("MaxCapacity") is int mb && mb > 0 ? mb * 1024L * 1024L : fallback;
        return (nuke, max);
    }

    // ───── IFileOperation(ShObjIdl) ─────

    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOF_WANTNUKEWARNING = 0x4000;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;

    [ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    private class FileOperationClass
    {
    }

    /// <summary>vtable 순서가 ShObjIdl.h 와 같아야 한다(부르는 것은 SetOperationFlags·DeleteItem·PerformOperations·GetAnyOperationsAborted 뿐).</summary>
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        uint Advise(IntPtr pfops);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IntPtr pfopsItem);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IntPtr pfopsItem);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IntPtr pfopsItem);
        void PerformOperations();
        [return: MarshalAs(UnmanagedType.Bool)]
        bool GetAnyOperationsAborted();
    }

    /// <summary>넘겨주기만 하고 부르지 않는다(메서드 선언 없음).</summary>
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, int cchBufferLength);
}
