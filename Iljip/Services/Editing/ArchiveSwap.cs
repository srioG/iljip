using System.IO;

namespace Iljip.Services.Editing;

/// <summary>
/// 고친 압축 파일을 원본과 바꾸기(W2-06, I2-3). 새 압축은 원본과 같은 폴더의 숨김 임시 파일(<c>.~이름.8글자.tmp</c>)에 쓰고,
/// 검사가 맞을 때만 여기서 바꾼다 — 실패하면 원본은 바이트 그대로이고 임시 파일·예비 파일(<c>.~이름.bak</c>)도 남기지 않는다.
/// <list type="bullet">
/// <item>바꾸기는 <see cref="File.Replace(string,string,string?,bool)"/>(원본의 속성·만든 날짜·권한을 이어받음). 예비 이름을 주어,
///   바꾸다 중간에 멈춰도 원본이 예비 이름으로 남아 되돌릴 수 있게 한다(성공하면 예비 파일은 지움).</item>
/// <item>DLP(PCFILTER)·백신이 잠깐 잡으면 200ms 간격으로 3번 다시 해 본다(<see cref="OutputFiles.IsTransientLock"/>).</item>
/// <item>File.Replace 를 못 쓰는 곳(일부 네트워크 드라이브 등)은 '원본 → 예비 이름, 임시 → 원본 이름, 예비 지움'. 중간에 실패하면 예비를 원래 이름으로 되돌린다.</item>
/// </list>
/// </summary>
public static class ArchiveSwap
{
    internal const string LockedMessage = "다른 프로그램이 이 압축 파일을 쓰고 있어서 고치지 못했어요. 그 프로그램을 닫고 다시 해 주세요.";
    internal const string ReadOnlyMessage = "이 압축 파일은 읽기 전용이라 고칠 수 없어요. 파일 속성에서 '읽기 전용'을 끄고 다시 해 주세요.";
    internal const string MissingMessage = "압축 파일을 찾지 못했어요. 옮겨졌거나 지워졌는지 확인해 주세요.";
    internal const string NoAccessMessage = "이 폴더에는 고친 압축 파일을 쓸 수 없어요(권한 없음). 다른 폴더로 옮긴 뒤 다시 해 주세요.";

    /// <summary>고치는 동안 다른 프로그램(또는 다른 고치기)이 원본을 바꿨을 때 — 그 변경을 덮어쓰지 않으려고 바꾸지 않는다.</summary>
    internal const string ChangedMessage =
        "고치는 동안 다른 곳에서 이 압축 파일이 바뀌어서, 그 내용을 덮어쓰지 않으려고 고친 것을 넣지 않았어요. 목록을 새로 읽고 다시 해 주세요.";

    /// <summary>원본의 표(크기·마지막 수정 시각) — 읽기 시작할 때 재 두었다가 바꾸기 직전에 같은지 본다(<see cref="Commit(string, string, FileStamp?)"/>).</summary>
    public readonly record struct FileStamp(long Length, DateTime LastWriteUtc);

    /// <summary>지금 원본의 표. 못 재면(지워짐 등) <see cref="MissingMessage"/>.</summary>
    public static FileStamp Stamp(string target)
    {
        try
        {
            var fi = new FileInfo(target);
            if (!fi.Exists) throw new ArchiveEditException(MissingMessage);
            return new FileStamp(fi.Length, fi.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArchiveEditException(MissingMessage, ex);
        }
    }

    /// <summary>새 임시 파일 이름(같은 폴더, <c>.~이름.8글자.tmp</c>).</summary>
    public static string TempPath(string target) => OutputFiles.TempSibling(Path.GetFullPath(target));

    /// <summary>이 압축의 임시 파일·예비 파일인가(같은 폴더, 이름 모양) — 시험·청소용.</summary>
    internal static bool IsLeftover(string path, string target)
    {
        string name = Path.GetFileName(path);
        string prefix = OutputFiles.TempPrefix + Path.GetFileName(target) + ".";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 고치기 전에 원본을 바꿀 수 있는지 본다(무거운 작업 전에 빨리 알리려고): 있는지, 읽기 전용이 아닌지, 다른 프로그램이 쓰기를 막지 않았는지.
    /// 쓰기로 잠깐 열었다 닫기만 한다(내용·시각은 그대로).
    /// </summary>
    public static void EnsureWritable(string target)
    {
        if (!File.Exists(target)) throw new ArchiveEditException(MissingMessage);
        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArchiveEditException(MissingMessage, ex);
        }
        if ((attrs & FileAttributes.ReadOnly) != 0) throw new ArchiveEditException(ReadOnlyMessage);
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using (new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { }
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex))
            {
                throw new ArchiveEditException(LockedMessage, ex);
            }
        }
    }

    /// <summary>
    /// 검사를 마친 임시 파일 <paramref name="temp"/> 로 원본 <paramref name="target"/> 을 바꾼다. 실패하면 원본은 그대로 두고(되돌림) 예외 —
    /// 임시 파일은 호출자가 지운다(이 메서드가 이미 옮겼으면 없음).
    /// <paramref name="expected"/>(읽기 시작할 때 잰 원본의 표)를 주면 바꾸기 직전에 지금 원본과 같은지 보고, 다르면(그 사이 다른 프로그램·다른 고치기가 바꿈)
    /// 바꾸지 않고 <see cref="ChangedMessage"/> — 그 변경을 말없이 덮어쓰지 않게(잃어버린 갱신). 일집 안의 고치기는 명령 차례로 한 번에 하나라 보통은 같다.
    /// </summary>
    public static void Commit(string temp, string target, FileStamp? expected = null)
    {
        // 같은 프로세스 안의 바꾸기는 한 번에 하나 — '같은지 보기'와 '바꾸기' 사이에 다른 고치기가 끼어들지 않게(동시에 고친 둘이 서로 덮어쓰지 않음)
        lock (CommitGate) CommitCore(temp, Path.GetFullPath(target), expected);
    }

    private static readonly object CommitGate = new();

    private static void CommitCore(string temp, string target, FileStamp? expected)
    {
        if (expected is { } before && Stamp(target) != before)
            throw new ArchiveEditException(ChangedMessage);
        string backup = BackupPath(target);
        FileAttributes originalAttributes = SafeAttributes(target);
        bool fallback = false;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Replace(temp, target, backup, ignoreMetadataErrors: true);
                DeleteQuietly(backup);
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                RestoreIfMoved(backup, target);
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex))
            {
                RestoreIfMoved(backup, target);
                throw new ArchiveEditException(LockedMessage, ex);
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or NotSupportedException)
            {
                // 이 드라이브가 바꾸기를 못 함(또는 중간에 멈춤) — 원본이 예비 이름으로 옮겨졌으면 되돌리고 아래 방법으로
                Logger.LogError("File.Replace 실패 — 예비 이름 방식으로 바꿈: " + target, ex);
                RestoreIfMoved(backup, target);
                fallback = true;
            }
            if (fallback) break;
        }

        // 원본 → 예비, 임시 → 원본 이름, 예비 지움
        if (!File.Exists(target)) throw new ArchiveEditException(MissingMessage);
        MoveWithRetry(target, backup);
        try
        {
            MoveWithRetry(temp, target);
        }
        catch
        {
            RestoreIfMoved(backup, target);
            throw;
        }
        // 임시 파일은 숨김이었다 — 원본의 속성으로 되돌린다
        try { File.SetAttributes(target, originalAttributes); }
        catch { /* 속성 맞추기 실패는 무시(내용은 바뀜) */ }
        DeleteQuietly(backup);
    }

    /// <summary>예비 파일 이름: 같은 폴더의 <c>.~이름.bak</c>(이미 있으면 <c>.~이름.8글자.bak</c>).</summary>
    private static string BackupPath(string target)
    {
        string dir = Path.GetDirectoryName(target) ?? string.Empty;
        string name = Path.GetFileName(target);
        string p = Path.Combine(dir, $"{OutputFiles.TempPrefix}{name}.bak");
        if (!File.Exists(p) && !Directory.Exists(p)) return p;
        return Path.Combine(dir, $"{OutputFiles.TempPrefix}{name}.{Guid.NewGuid().ToString("N")[..8]}.bak");
    }

    /// <summary>원본 자리가 비었고 예비가 있으면(바꾸기가 중간에 멈춤) 예비를 원래 이름으로 되돌린다.</summary>
    private static void RestoreIfMoved(string backup, string target)
    {
        try
        {
            if (File.Exists(backup) && !File.Exists(target)) MoveWithRetry(backup, target);
            else if (File.Exists(backup) && File.Exists(target)) DeleteQuietly(backup);
        }
        catch (Exception ex)
        {
            Logger.LogError($"원본 되돌리기 실패 — 예비 파일 '{backup}' 을(를) '{target}' 으로 직접 이름을 바꿔 주세요", ex);
            throw new ArchiveEditException(
                $"고치다 멈췄어요. 원래 압축 파일은 '{Path.GetFileName(backup)}' 이름으로 같은 폴더에 남아 있어요(숨김 파일). 이름을 원래대로 바꿔 주세요.", ex);
        }
    }

    private static void MoveWithRetry(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: false);
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex))
            {
                throw new ArchiveEditException(LockedMessage, ex);
            }
        }
    }

    private static FileAttributes SafeAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch { return FileAttributes.Normal; }
    }

    /// <summary>임시·예비 파일 지우기(DLP 가 잠깐 잡으면 몇 번 다시). 끝내 못 지우면 로그만.</summary>
    internal static void DeleteQuietly(string path)
    {
        for (int attempt = 0; attempt <= OutputFiles.MoveLockRetries; attempt++)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == OutputFiles.MoveLockRetries)
                {
                    Logger.LogError("고치기 임시 파일을 지우지 못함: " + path, ex);
                    return;
                }
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }
}
