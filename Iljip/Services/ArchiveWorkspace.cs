using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// (W2-06 이 W2-04 보관본에서 그대로 들임 — I3-3 고친 파일 다시 넣기의 바탕. 다른 프로그램의 청소(TempJanitor)는 W2-04 를 다시 할 때 들인다)
/// 압축 파일 하나의 '안의 파일 열기' 작업 폴더(W2-04 — I1-3, 설계 §3): <c>%LOCALAPPDATA%\Iljip\temp\open\&lt;key16&gt;\</c>.
/// key16 은 SHA-256(전체 경로|길이|수정 시각)의 앞 16글자 — 같은 파일이면 같고, 압축 파일이 바뀌면(수정 시각) 다르다.
/// 폴더 안 <c>owner.lock</c> 을 앱이 끝날 때까지 혼자 열어 두어(FileShare.None) '살아 있음'을 표시한다 — 다른 일집의 청소(TempJanitor — W2-04)가 지우지 않게.
/// 같은 항목을 다시 열면 순번 하위 폴더(1, 2, …)를 새로 쓴다 — 앞서 연 파일을 다른 프로그램(워드 등)이 잠그고 있어도 겹치지 않는다.
/// <see cref="Dispose"/>(앱 종료 — 압축을 바꾸거나 닫아도 앱을 닫을 때까지 둠, 그 압축을 다시 열면 다시 씀)에서 폴더를 지운다.
/// 다른 프로그램이 아직 잡고 있는 파일은 남기고(예외 없음), 다음 시작 때 청소가 다시 지운다.
/// </summary>
public sealed class ArchiveWorkspace : IDisposable
{
    public const string LockFileName = "owner.lock";

    private readonly object _gate = new();
    private readonly List<OpenedEntry> _opened = new();
    private FileStream? _lock;

    private ArchiveWorkspace(string archivePath, string key, string fullPath, FileStream lockStream)
    {
        ArchivePath = archivePath;
        Key = key;
        FullPath = fullPath;
        _lock = lockStream;
    }

    /// <summary>이 작업 폴더의 압축 파일(전체 경로).</summary>
    public string ArchivePath { get; }

    /// <summary>key16(<see cref="ComputeKey"/>).</summary>
    public string Key { get; }

    /// <summary>작업 폴더(<c>temp\open\key16</c> — 같은 압축을 다른 일집이 이미 열어 쓰고 있으면 <c>key16-2</c> …).</summary>
    public string FullPath { get; }

    public bool IsDisposed => _lock is null;

    /// <summary>이 작업 폴더에서 연 파일들(W2-06 의 고친 파일 감시가 쓴다).</summary>
    public IReadOnlyList<OpenedEntry> OpenedEntries
    {
        get { lock (_gate) return _opened.ToList(); }
    }

    /// <summary>SHA-256(전체 경로(대문자)|길이|수정 시각 UTC 틱)의 앞 16글자(소문자 16진).</summary>
    public static string ComputeKey(string archivePath)
    {
        var fi = new FileInfo(Path.GetFullPath(archivePath));
        string text = $"{fi.FullName.ToUpperInvariant()}|{(fi.Exists ? fi.Length : -1)}|{(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0)}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>지금 이 작업 폴더가 그 압축 파일(같은 경로·길이·수정 시각)의 것인가.</summary>
    public bool Matches(string archivePath)
    {
        try { return !IsDisposed && ComputeKey(archivePath) == Key; }
        catch { return false; }
    }

    /// <summary>
    /// 작업 폴더를 만들고 잡는다(<paramref name="openRoot"/> 를 주지 않으면 <see cref="AppPaths.OpenRoot"/>).
    /// 잡은 폴더에 예전(비정상 종료) 찌꺼기가 있으면 먼저 지운다 — 주인이 없으니 남의 것이 아니다.
    /// </summary>
    public static ArchiveWorkspace Open(string archivePath, string? openRoot = null)
    {
        string full = Path.GetFullPath(archivePath);
        string key = ComputeKey(full);
        string root = openRoot ?? AppPaths.OpenRoot;
        IOException? last = null;
        for (int n = 1; n <= 20; n++)
        {
            string dir = Path.Combine(root, n == 1 ? key : $"{key}-{n}");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var fs = new FileStream(Path.Combine(dir, LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var ws = new ArchiveWorkspace(full, key, dir, fs);
                    ws.RemoveLeftovers();
                    return ws;
                }
                catch (DirectoryNotFoundException ex)
                {
                    // 다른 일집의 청소가 막 지웠다 — 다시 만든다
                    last = ex;
                }
                catch (IOException ex) when (OutputFiles.IsTransientLock(ex))
                {
                    // 다른 일집이 이 압축을 열어 쓰고 있다(owner.lock 이 잡힘) → 다음 이름
                    last = ex;
                    break;
                }
            }
        }
        throw last ?? new IOException("임시 폴더를 만들지 못했어요: " + root);
    }

    /// <summary>새 순번 하위 폴더(<c>1</c>, <c>2</c>, …)를 만들어 돌려준다.</summary>
    public string NewSlot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            int max = 0;
            foreach (var d in Directory.EnumerateDirectories(FullPath))
                if (int.TryParse(Path.GetFileName(d), out int k) && k > max) max = k;
            for (int n = max + 1; ; n++)
            {
                string p = Path.Combine(FullPath, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (Directory.Exists(p) || File.Exists(p)) continue;
                Directory.CreateDirectory(p);
                return p;
            }
        }
    }

    /// <summary>
    /// 연 파일을 기록한다(처음 크기·시각·SHA-256 — 나중에 고쳤는지 본다). 파일 전체를 읽으므로 큰 파일은 오래 걸린다 — 화면은 뒤 스레드에서 부른다.
    /// 여러 스레드에서 불러도 된다.
    /// </summary>
    public OpenedEntry Record(string entryKey, string tempPath)
    {
        var e = OpenedEntry.Capture(entryKey, tempPath, ArchivePath);
        lock (_gate) _opened.Add(e);
        return e;
    }

    /// <summary>연 파일 중 다른 프로그램에서 고쳐 저장한 것.</summary>
    public IReadOnlyList<OpenedEntry> ModifiedEntries() => OpenedEntries.Where(e => e.IsModified()).ToList();

    /// <summary>잠금을 놓고 폴더를 지운다(다른 프로그램이 잡은 파일은 남김 — 예외 없음). 여러 번 불러도 된다.</summary>
    public void Dispose()
    {
        FileStream? fs;
        lock (_gate)
        {
            fs = _lock;
            _lock = null;
        }
        if (fs is null) return;
        try { fs.Dispose(); } catch { /* 무시 */ }
        TempFolders.DeleteBestEffort(FullPath);
    }

    private void RemoveLeftovers()
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(FullPath))
                TempFolders.DeleteBestEffort(d);
            foreach (var f in Directory.EnumerateFiles(FullPath))
            {
                if (string.Equals(Path.GetFileName(f), LockFileName, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); } catch { /* 남김 */ }
            }
        }
        catch
        {
            // 정리는 덤
        }
    }
}

/// <summary>
/// 압축 안에서 꺼내 기본 프로그램으로 연 파일 하나(W2-04). 처음 크기·수정 시각·SHA-256 을 적어 두고, 고쳐 저장했는지 본다
/// (일집을 닫으면 임시 폴더가 지워지므로 — 닫을 때 알려 주기, W2-06 의 고친 파일 감시).
/// </summary>
public sealed class OpenedEntry
{
    private OpenedEntry(string entryKey, string tempPath, string? archivePath, long size, DateTime writeUtc, string? sha)
    {
        EntryKey = entryKey;
        TempPath = tempPath;
        ArchivePath = archivePath;
        InitialSize = size;
        InitialWriteUtc = writeUtc;
        InitialSha256 = sha;
    }

    /// <summary>압축 안 경로('/' 구분).</summary>
    public string EntryKey { get; }

    /// <summary>꺼낸 임시 파일.</summary>
    public string TempPath { get; }

    /// <summary>이 파일을 꺼낸 압축 파일(전체 경로 — W2-06 다시 넣기가 씀). 모르면 null.</summary>
    public string? ArchivePath { get; }

    /// <summary>처음(또는 마지막으로 다시 넣었거나 '넣지 않기'로 넘긴 때 — <see cref="Rebase"/>) 크기.</summary>
    public long InitialSize { get; private set; }

    /// <summary>처음(또는 <see cref="Rebase"/> 때) 수정 시각(UTC).</summary>
    public DateTime InitialWriteUtc { get; private set; }

    /// <summary>처음(또는 <see cref="Rebase"/> 때) 내용의 SHA-256(16진 대문자, 못 읽었으면 null).</summary>
    public string? InitialSha256 { get; private set; }

    public string Name => Path.GetFileName(TempPath);

    /// <summary>
    /// 바뀐 것을 '나중에'로 미뤘다(W2-06) — 다시 저장해도 또 묻지 않고, 압축을 닫거나 일집을 닫을 때 한 번 더 묻는다.
    /// </summary>
    public bool Postponed { get; set; }

    internal static OpenedEntry Capture(string entryKey, string tempPath, string? archivePath = null)
    {
        var fi = new FileInfo(tempPath);
        return new OpenedEntry(entryKey, tempPath, archivePath, fi.Exists ? fi.Length : -1, fi.Exists ? fi.LastWriteTimeUtc : default, TrySha(tempPath));
    }

    /// <summary>
    /// 지금 내용을 새 기준으로 삼는다(W2-06). <see cref="Postponed"/> 도 푼다.
    /// ★ 다시 넣은 뒤에는 이것이 아니라 <see cref="Rebase(ContentStamp)"/>(실제로 넣은 바이트의 표) — 넣는 동안 또 저장한 내용이 기준이 되면
    /// 그 내용은 압축에 들어가지 않았는데도 '바뀌지 않음'으로 보여 말없이 사라진다(W2-06 검증 지적).
    /// </summary>
    public void Rebase() => Rebase(Stamp());

    /// <summary>주어진 내용(표)을 새 기준으로 삼는다 — 그 뒤로 이 내용과 다르게 고치면 다시 묻는다. <see cref="Postponed"/> 도 푼다.</summary>
    public void Rebase(ContentStamp stamp)
    {
        InitialSize = stamp.Size;
        InitialWriteUtc = stamp.WriteUtc;
        InitialSha256 = stamp.Sha256;
        Postponed = false;
    }

    /// <summary>지금 내용의 표(크기·수정 시각을 먼저 재고 그다음 SHA-256 — 그 사이에 바뀌면 SHA 가 새 내용이라 나중 비교가 맞게 됨). 파일 전체를 읽는다.</summary>
    public ContentStamp Stamp()
    {
        var fi = new FileInfo(TempPath);
        bool exists = fi.Exists;
        return new ContentStamp(exists ? fi.Length : -1, exists ? fi.LastWriteTimeUtc : default, TrySha(TempPath));
    }

    /// <summary>
    /// 지금 내용을 <paramref name="destination"/> 에 복사하고 '복사한 바이트'의 표를 돌려준다(I3-3 — 다시 넣을 내용·다른 곳에 저장할 내용을 고정).
    /// 복사하는 사이에 또 저장되면(크기·시각이 바뀜) 처음부터 다시 — 세 번까지. 그래도 바뀌면 마지막으로 복사한 것의 표(나중에 다르다고 보아 다시 묻게).
    /// 사본의 수정 시각은 원래 파일의 것으로 맞춘다(압축 안 항목 시각).
    /// </summary>
    internal ContentStamp CopyTo(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ContentStamp? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var before = new FileInfo(TempPath);
            if (!before.Exists) throw new FileNotFoundException("고친 파일을 찾지 못했어요.", TempPath);
            long size = before.Length;
            DateTime written = before.LastWriteTimeUtc;
            string sha;
            using (var src = new FileStream(TempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1 << 16];
                long copied = 0;
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, n);
                    dst.Write(buffer, 0, n);
                    copied += n;
                }
                sha = Convert.ToHexString(hash.GetHashAndReset());
                size = copied;
            }
            try { File.SetLastWriteTimeUtc(destination, written); } catch { /* 시각 맞추기 실패는 무시 */ }
            last = new ContentStamp(size, written, sha);
            var after = new FileInfo(TempPath);
            if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == written) return last;
        }
        return last!;
    }

    /// <summary>
    /// 고쳐 저장했는가: 파일이 있고 크기나 수정 시각이 달라졌으며 내용(SHA-256)도 다를 때. 지워졌으면 아니오(잃을 것이 없음).
    /// 다른 프로그램이 혼자 잡고 있어 내용을 못 읽으면 크기·시각만으로 판단한다(알리는 쪽으로).
    /// </summary>
    public bool IsModified()
    {
        try
        {
            var fi = new FileInfo(TempPath);
            if (!fi.Exists) return false;
            if (fi.Length == InitialSize && fi.LastWriteTimeUtc == InitialWriteUtc) return false;
            string? now = TrySha(TempPath);
            return now is null || InitialSha256 is null || !string.Equals(now, InitialSha256, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>시험용: SHA-256 을 잴 때 그 파일 경로로 부른다(어느 스레드에서 재는지 본다). 평소에는 null.</summary>
    internal static volatile Action<string>? HashingObserverForTests;

    private static string? TrySha(string path)
    {
        HashingObserverForTests?.Invoke(path);
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 파일 내용 한 벌의 표(W2-06 — I3-3): 크기·수정 시각(UTC)·SHA-256(16진 대문자, 못 읽었으면 null). '압축에 넣은 내용'·'물어본 내용'을 기준으로 삼을 때.
/// </summary>
public sealed record ContentStamp(long Size, DateTime WriteUtc, string? Sha256);

/// <summary>일집 임시 폴더 지우기 도우미(W2-04).</summary>
internal static class TempFolders
{
    /// <summary>
    /// 폴더를 지운다. 한 번에 안 지워지면(다른 프로그램이 잡은 파일) 파일·폴더를 하나씩 지우고 못 지운 것만 남긴다 — 예외 없음.
    /// 다 지웠으면 true.
    /// </summary>
    internal static bool DeleteBestEffort(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return true;
            Directory.Delete(dir, recursive: true);
            return true;
        }
        catch
        {
            // 하나씩
        }
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
            {
                try
                {
                    File.SetAttributes(f, FileAttributes.Normal);
                    File.Delete(f);
                }
                catch
                {
                    // 다른 프로그램이 잡고 있음 — 남긴다
                }
            }
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length).ToList())
            {
                try { Directory.Delete(d); } catch { /* 비지 않음 */ }
            }
            try { Directory.Delete(dir); } catch { /* 비지 않음 */ }
        }
        catch
        {
            // 목록을 못 읽음 — 남긴다
        }
        return !Directory.Exists(dir);
    }
}
