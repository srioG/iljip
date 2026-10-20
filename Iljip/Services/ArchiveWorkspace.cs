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
/// 다른 프로그램이 아직 잡고 있는 파일은 남기고(예외 없음), 다음 시작 때 청소가 다시 본다.
/// I5-A: <c>owner.lock</c> 안에 '연 파일 목록'(<see cref="WorkspaceManifest"/> — 압축 안 경로·지금 이름·기준 크기·시각·SHA-256)을 적어 둔다(열 때·다시 넣거나 넘길 때·이름이 바뀔 때).
/// 일집을 닫을 때 아직 다른 프로그램이 잡고 있는 파일(또는 넣지도 넘기지도 않은 고친 파일)의 순번 폴더는 통째로 남기고 이 목록도 남긴다 —
/// 일집을 닫은 뒤 그 프로그램에서 저장한 내용을 다음 시작 때 청소(<see cref="TempJanitor"/>)가 말없이 지우지 않고 알리게(X3-F1·I3-3-close).
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
                    // I5-A: 꺼진 일집이 '연 파일 목록'을 남긴 폴더(닫은 뒤 다른 프로그램에서 저장했을 수 있는 파일)는 다시 쓰지 않는다 — 찌꺼기 지우기로
                    // 고친 파일이 사라지지 않게. 청소(TempJanitor)가 보고 바뀐 것이 없으면 지우고, 있으면 알린다. 이 압축은 옆 이름 폴더를 쓴다.
                    if (WorkspaceManifest.TryRead(fs) is { Entries.Count: > 0 })
                    {
                        fs.Dispose();
                        break;
                    }
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
        e.BaselineChanged = _ => WriteManifest();   // I5-A: 다시 넣음·넘김·이름 바뀜 → 목록 다시 적기
        lock (_gate)
        {
            _opened.Add(e);
            WriteManifestLocked(_lock, _opened);
        }
        return e;
    }

    /// <summary>연 파일 중 다른 프로그램에서 고쳐 저장한 것.</summary>
    public IReadOnlyList<OpenedEntry> ModifiedEntries() => OpenedEntries.Where(e => e.IsModified()).ToList();

    /// <summary>'연 파일 목록'을 <c>owner.lock</c> 에 다시 적는다(I5-A — 실패는 조용히: 목록은 덤, 없으면 예전처럼 청소).</summary>
    private void WriteManifest()
    {
        lock (_gate) WriteManifestLocked(_lock, _opened);
    }

    private void WriteManifestLocked(FileStream? fs, IReadOnlyCollection<OpenedEntry> entries)
    {
        if (fs is null) return;
        try
        {
            byte[] bytes = WorkspaceManifest.Serialize(FullPath, ArchivePath, entries);
            fs.SetLength(0);
            fs.Position = 0;
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush();
        }
        catch (Exception ex)
        {
            Logger.LogError("연 파일 목록을 적지 못함(작업 폴더: " + FullPath + ")", ex);
        }
    }

    /// <summary>
    /// 잠금을 놓고 폴더를 지운다(다른 프로그램이 잡은 파일은 남김 — 예외 없음). 여러 번 불러도 된다.
    /// I5-A: 연 파일 중 다른 프로그램이 아직 잡고 있는 것(Light PDF·워드·엑셀 — 일집을 닫은 뒤 거기서 저장할 수 있음)과
    /// 고쳐 놓고 넣지도 넘기지도 않은 것(닫기 질문을 거치지 않고 끝날 때 — 로그오프 등)은 그 순번 폴더를 통째로 남기고(편집기의 자동 저장·잠금 파일 포함)
    /// <c>owner.lock</c> 에 목록을 남긴다 — 다음 시작 때 청소가 보고, 일집을 닫은 뒤 바뀌었으면 지우지 않고 알린다.
    /// </summary>
    public void Dispose()
    {
        FileStream? fs;
        List<OpenedEntry> entries;
        lock (_gate)
        {
            fs = _lock;
            _lock = null;
            entries = _opened.ToList();
        }
        if (fs is null) return;
        var keep = new List<string>();
        foreach (var e in entries)
        {
            try
            {
                e.FollowRename();
                string path = e.TempPath;
                if (!File.Exists(path)) continue;
                if (FileInUse.IsHeld(path) || e.IsModified()) keep.Add(Path.GetFullPath(Path.GetDirectoryName(path)!));
            }
            catch
            {
                // 판단 못 함 — 지우는 쪽으로(예전과 같음)
            }
        }
        if (keep.Count > 0) WriteManifestLocked(fs, entries);
        try { fs.Dispose(); } catch { /* 무시 */ }
        if (keep.Count == 0)
        {
            TempFolders.DeleteBestEffort(FullPath);
            return;
        }
        TempFolders.DeleteAllBut(FullPath, keep, LockFileName);
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
/// I5-A: 편집기가 그 파일의 이름을 바꾸면(Light PDF 탭 '이름 바꾸기' — 같은 순번 폴더 안에서) 새 이름을 따라간다(<see cref="FollowRename"/>) —
/// 예전엔 원래 경로에 파일이 없으면 '지워짐(잃을 것 없음)'으로 보아, 이름을 바꾸기 전·후에 저장한 내용을 묻지도 않고 닫을 때 지웠다(X3-F2).
/// 다시 넣을 때 압축 안 이름은 늘 원래 항목 이름(<see cref="EntryKey"/>)이다.
/// </summary>
public sealed class OpenedEntry
{
    private readonly object _followGate = new();
    private volatile string _path;

    private OpenedEntry(string entryKey, string tempPath, string? archivePath, long size, DateTime writeUtc, string? sha)
    {
        EntryKey = entryKey;
        ExtractedPath = tempPath;
        _path = tempPath;
        ArchivePath = archivePath;
        InitialSize = size;
        InitialWriteUtc = writeUtc;
        InitialSha256 = sha;
    }

    /// <summary>압축 안 경로('/' 구분).</summary>
    public string EntryKey { get; }

    /// <summary>꺼낸 임시 파일 — 지금 이름(편집기가 같은 폴더에서 이름을 바꿨으면 새 이름, I5-A).</summary>
    public string TempPath => _path;

    /// <summary>처음 꺼낸 자리(이름을 바꾸기 전 — I5-A).</summary>
    public string ExtractedPath { get; }

    /// <summary>처음 꺼낸 이름(압축 안 이름 — Windows 에 못 쓰는 글자는 바꾼 것).</summary>
    public string ExtractedName => Path.GetFileName(ExtractedPath);

    /// <summary>편집기가 이름을 바꿨는가(지금 이름이 처음 꺼낸 이름과 다름).</summary>
    public bool IsRenamed => !string.Equals(_path, ExtractedPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>기준(다시 넣은·넘긴 내용)이나 지금 이름이 바뀌었을 때(작업 폴더가 '연 파일 목록'을 다시 적음 — I5-A).</summary>
    internal Action<OpenedEntry>? BaselineChanged { get; set; }

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
        BaselineChanged?.Invoke(this);
    }

    /// <summary>지금 내용의 표(크기·수정 시각을 먼저 재고 그다음 SHA-256 — 그 사이에 바뀌면 SHA 가 새 내용이라 나중 비교가 맞게 됨). 파일 전체를 읽는다.</summary>
    public ContentStamp Stamp()
    {
        FollowRename();
        string path = TempPath;
        var fi = new FileInfo(path);
        bool exists = fi.Exists;
        return new ContentStamp(exists ? fi.Length : -1, exists ? fi.LastWriteTimeUtc : default, TrySha(path));
    }

    /// <summary>
    /// 지금 이름의 파일이 없으면(편집기가 이름을 바꿈) 같은 폴더에서 새 이름을 찾아 따라간다(I5-A). 따라갔으면 true.
    /// 처음 꺼낸 이름이 다시 있으면(이름을 되돌림·오피스의 '임시 파일에 써서 바꿔 저장' 도중) 그쪽. 아니면 같은 확장자인 파일 중 편집기 임시·잠금·숨김 파일
    /// (<see cref="IsEditorScratch"/>)이 아닌 것의 가장 최근 것 — 순번 폴더에는 열어 준 파일 하나만 꺼내므로 그것이 이름을 바꾼 그 파일이다.
    /// 찾지 못하면(정말 지움) 그대로(<see cref="IsModified"/> 는 '잃을 것 없음').
    /// </summary>
    public bool FollowRename()
    {
        lock (_followGate)
        {
            string now = _path;
            if (File.Exists(now)) return false;
            string? next = File.Exists(ExtractedPath)
                ? ExtractedPath
                : FindRenamed(Path.GetDirectoryName(now)!, ExtractedPath);
            if (next is null || string.Equals(next, now, StringComparison.OrdinalIgnoreCase)) return false;
            _path = next;
        }
        BaselineChanged?.Invoke(this);
        return true;
    }

    /// <summary>
    /// <paramref name="folder"/> 에서 <paramref name="originalPath"/> 의 새 이름을 찾는다(I5-A — 일집이 꺼진 동안 이름을 바꾼 것을 청소가 찾을 때도).
    /// 같은 확장자이고 편집기 임시·잠금·숨김 파일이 아닌 것 중 가장 최근에 쓴 것. 없으면 null.
    /// </summary>
    internal static string? FindRenamed(string folder, string originalPath)
    {
        string ext = Path.GetExtension(originalPath);
        try
        {
            return new DirectoryInfo(folder).EnumerateFiles()
                .Where(f => !IsEditorScratch(f) && string.Equals(f.Extension, ext, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 편집기가 옆에 만드는 임시·잠금·숨김 파일인가(이름을 바꾼 '그 파일'로 보지 않음): '~'·'.~' 로 시작(오피스 소유자 파일 ~$·저장 임시 ~WRL·리브레오피스 .~lock·일집 덮어쓰기 임시),
    /// '.tmp'·'.autosave' 로 끝남(Light PDF 저장·자동 저장 임시), 숨김·시스템 속성.
    /// </summary>
    internal static bool IsEditorScratch(FileInfo f)
    {
        string n = f.Name;
        if (n.StartsWith('~') || n.StartsWith(".~", StringComparison.Ordinal)) return true;
        if (n.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".autosave", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>다른 프로그램이 지금 이 파일을 열어 두었는가(I5-A — 일집을 닫기 전에 알림·닫을 때 남기기). 이름을 바꿨으면 새 이름으로 본다.</summary>
    public bool IsHeldByOtherProgram()
    {
        FollowRename();
        return FileInUse.IsHeld(TempPath);
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
            FollowRename();
            string path = TempPath;
            var before = new FileInfo(path);
            if (!before.Exists) throw new FileNotFoundException("고친 파일을 찾지 못했어요.", path);
            long size = before.Length;
            DateTime written = before.LastWriteTimeUtc;
            string sha;
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
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
            var after = new FileInfo(path);
            if (after.Exists && after.Length == before.Length && after.LastWriteTimeUtc == written) return last;
        }
        return last!;
    }

    /// <summary>
    /// 고쳐 저장했는가: 파일이 있고 크기나 수정 시각이 달라졌으며 내용(SHA-256)도 다를 때. 지워졌으면 아니오(잃을 것이 없음).
    /// 다른 프로그램이 혼자 잡고 있어 내용을 못 읽으면 크기·시각만으로 판단한다(알리는 쪽으로).
    /// I5-A: 편집기가 이름을 바꿨으면 새 이름의 파일로 본다(<see cref="FollowRename"/> — 이름 바꾸기는 지우기가 아님).
    /// </summary>
    public bool IsModified()
    {
        try
        {
            FollowRename();
            string path = TempPath;
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;
            if (fi.Length == InitialSize && fi.LastWriteTimeUtc == InitialWriteUtc) return false;
            string? now = TrySha(path);
            return now is null || InitialSha256 is null || !string.Equals(now, InitialSha256, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>시험용: SHA-256 을 잴 때 그 파일 경로로 부른다(어느 스레드에서 재는지 본다). 평소에는 null.</summary>
    internal static volatile Action<string>? HashingObserverForTests;

    internal static string? TrySha(string path)
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

    /// <summary>
    /// <paramref name="root"/> 안을 지우되 <paramref name="keepDirs"/>(그 폴더 통째로 — 위 폴더는 길로 남김)와 맨 위의 <paramref name="keepFile"/> 은 남긴다(I5-A —
    /// 일집을 닫을 때 다른 프로그램이 잡은 파일의 순번 폴더·연 파일 목록). 예외 없음.
    /// </summary>
    internal static void DeleteAllBut(string root, IReadOnlyCollection<string> keepDirs, string keepFile)
    {
        string Norm(string p) => Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
        var keep = keepDirs.Select(Norm).ToList();
        bool IsKept(string d) => keep.Any(k => string.Equals(k, d, StringComparison.OrdinalIgnoreCase));
        bool HoldsKept(string d) => keep.Any(k => k.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

        void Walk(string dir, bool top)
        {
            List<string> files, dirs;
            try
            {
                files = Directory.EnumerateFiles(dir).ToList();
                dirs = Directory.EnumerateDirectories(dir).Select(Norm).ToList();
            }
            catch
            {
                return;
            }
            foreach (var f in files)
            {
                if (top && string.Equals(Path.GetFileName(f), keepFile, StringComparison.OrdinalIgnoreCase)) continue;
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
            foreach (var d in dirs)
            {
                if (IsKept(d)) continue;
                if (HoldsKept(d)) Walk(d, top: false);
                else DeleteBestEffort(d);
            }
        }

        Walk(Norm(root), top: true);
    }

    /// <summary>폴더 안에 <paramref name="exceptFile"/>(맨 위) 말고 파일이 하나도 없는가(I5-A — 남긴 순번 폴더를 다 처리했으면 작업 폴더째 지움).</summary>
    internal static bool HasNoFilesBut(string root, string exceptFile)
    {
        try
        {
            string top = Path.GetFullPath(Path.Combine(root, exceptFile));
            return !Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Any(f => !string.Equals(Path.GetFullPath(f), top, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>다른 프로그램이 파일을 열어 두었는지(I5-A).</summary>
internal static class FileInUse
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// 혼자 열기(공유 없음)를 해 본다 — 공유·잠금 위반이면 누군가 열어 두었음(Light PDF·워드·엑셀·한글은 연 동안 파일을 잡는다. 메모장처럼 읽고 바로 놓는 프로그램은 알 수 없음).
    /// 없거나 다른 이유로 못 열면 아니오. 연 것은 바로 놓는다.
    /// </summary>
    internal static bool IsHeld(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException ex) when (ex.HResult is ErrorSharingViolation or ErrorLockViolation)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 작업 폴더의 '연 파일 목록'(I5-A): <c>owner.lock</c> 안에 JSON 으로(새 파일을 만들지 않음 — 잠금을 쥔 동안 일집만 씀). 일집을 닫은 뒤에도 남은 순번 폴더가 있으면
/// 그대로 남아, 다음 시작 때 청소(<see cref="TempJanitor"/>)가 압축 경로·압축 안 경로·기준 내용으로 '일집을 닫은 뒤 바뀌었는지' 본다. 경로는 작업 폴더 기준 상대 경로.
/// </summary>
internal static class WorkspaceManifest
{
    /// <summary>목록이라는 표지(빈 잠금 파일·다른 내용과 가름).</summary>
    internal const string Marker = "iljip-open-v1";

    internal sealed record Entry(string Key, string Extracted, string Current, long Size, long WriteTicks, string? Sha);

    internal sealed record Data(string Marker, string Archive, List<Entry> Entries);

    private static readonly System.Text.Json.JsonSerializerOptions Json = new() { WriteIndented = false };

    internal static byte[] Serialize(string root, string archive, IEnumerable<OpenedEntry> entries)
    {
        var list = entries.Select(e => new Entry(e.EntryKey, Path.GetRelativePath(root, e.ExtractedPath), Path.GetRelativePath(root, e.TempPath),
            e.InitialSize, e.InitialWriteUtc.Ticks, e.InitialSha256)).ToList();
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new Data(Marker, archive, list), Json);
    }

    /// <summary>열어 둔 잠금 파일에서 읽는다(처음부터 — 목록이 아니거나 비었으면 null). 위치는 처음으로 돌려 둔다.</summary>
    internal static Data? TryRead(FileStream fs)
    {
        try
        {
            if (fs.Length == 0 || fs.Length > 4 << 20) return null;
            fs.Position = 0;
            var bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            fs.Position = 0;
            return Parse(bytes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>꺼진 일집의 잠금 파일에서 읽는다(없거나 못 읽으면 null).</summary>
    internal static Data? TryRead(string lockPath)
    {
        try
        {
            var fi = new FileInfo(lockPath);
            if (!fi.Exists || fi.Length == 0 || fi.Length > 4 << 20) return null;
            using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[fs.Length];
            fs.ReadExactly(bytes);
            return Parse(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static Data? Parse(byte[] bytes)
    {
        var d = System.Text.Json.JsonSerializer.Deserialize<Data>(bytes, Json);
        return d is { Marker: Marker, Archive: not null, Entries: not null } ? d : null;
    }

    /// <summary>
    /// 그 항목의 지금 파일(작업 폴더 안 — 밖을 가리키면 null): 적어 둔 지금 이름 → 처음 꺼낸 이름 → 같은 폴더에서 이름을 바꾼 것(<see cref="OpenedEntry.FindRenamed"/>). 없으면 null.
    /// </summary>
    internal static string? ResolveFile(string root, Entry e)
    {
        string? Inside(string rel)
        {
            try
            {
                string full = Path.GetFullPath(Path.Combine(root, rel));
                return full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch
            {
                return null;
            }
        }
        string? current = Inside(e.Current), extracted = Inside(e.Extracted);
        if (current is null || extracted is null) return null;
        if (File.Exists(current)) return current;
        if (File.Exists(extracted)) return extracted;
        string? folder = Path.GetDirectoryName(current);
        return folder is not null && Directory.Exists(folder) ? OpenedEntry.FindRenamed(folder, extracted) : null;
    }
}

/// <summary>
/// 꺼진 일집이 남긴, 압축 파일에 아직 넣지 않은 고친 파일 하나(I5-A — 다음 시작 때 청소가 찾아 알림: [압축에 넣기][폴더 열기][버리기][나중에]).
/// </summary>
/// <param name="WorkspaceDir">남은 작업 폴더(<c>temp\open\key16</c>).</param>
/// <param name="ArchivePath">그 파일을 꺼낸 압축 파일.</param>
/// <param name="EntryKey">압축 안 원래 경로(넣을 때 이 이름으로).</param>
/// <param name="FilePath">고친 파일(이름을 바꿨으면 새 이름).</param>
/// <param name="ExtractedName">처음 꺼낸 이름.</param>
public sealed record LeftoverFile(string WorkspaceDir, string ArchivePath, string EntryKey, string FilePath, string ExtractedName)
{
    /// <summary>지금 이름.</summary>
    public string Name => Path.GetFileName(FilePath);

    /// <summary>편집기가 이름을 바꿨는가.</summary>
    public bool IsRenamed => !string.Equals(Name, ExtractedName, StringComparison.OrdinalIgnoreCase);

    /// <summary>그 파일이 든 순번 폴더.</summary>
    public string SlotDir => Path.GetDirectoryName(FilePath)!;
}
