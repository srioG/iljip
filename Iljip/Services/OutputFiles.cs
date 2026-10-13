using System.IO;

namespace Iljip.Services;

/// <summary>
/// 풀어 낸 파일을 '덮어쓰지 않고 새로' 만드는 공용 규칙(모든 해제 서비스가 같이 씀).
/// 같은 이름의 파일·폴더가 있으면 "이름 (1).ext", "이름 (2).ext" … 로 비켜 쓴다(원본 비파괴).
/// '있는지 확인'과 '만들기' 사이에 다른 해제(같은 폴더에 동시에 여러 개를 풀 때)가 같은 이름을 먼저 잡을 수 있어서,
/// 만들기는 FileMode.CreateNew(없을 때만 만듦)로 한 번에 하고, 그새 생겼으면 다음 이름으로 넘어간다
/// (예전엔 진 쪽이 '다른 프로세스가 사용 중' 오류로 해제 전체가 실패했다).
/// </summary>
internal static class OutputFiles
{
    private const int ErrorFileExists = unchecked((int)0x80070050);      // ERROR_FILE_EXISTS
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);   // ERROR_ALREADY_EXISTS

    /// <summary>
    /// <paramref name="desiredPath"/>(없으면) 또는 비켜 쓴 이름으로 새 파일을 만들어 연다. 실제로 만든 경로는 <paramref name="writePath"/>.
    /// 닫은 뒤 실패 정리(부분 파일 지우기)는 호출자 몫 — 이 메서드가 만든 파일이므로 지워도 남의 파일이 아니다.
    /// </summary>
    internal static FileStream CreateNew(string desiredPath, out string writePath)
    {
        foreach (var candidate in Candidates(desiredPath))
        {
            // 이미 있는 이름은 예외 없이 건너뛴다(흔한 경우). 같은 이름의 '폴더'도 충돌로 본다 — CreateNew 가 '접근 거부'로 실패하므로.
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            try
            {
                var fs = new FileStream(candidate, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                writePath = candidate;
                return fs;
            }
            catch (IOException ex) when (ex.HResult is ErrorFileExists or ErrorAlreadyExists)
            {
                // 확인한 뒤 그새 다른 해제가 같은 이름을 만들었다 → 다음 후보
            }
        }
        throw new IOException($"고유한 출력 경로를 만들 수 없습니다: {desiredPath}");
    }

    /// <summary>
    /// 다 풀린 파일(<paramref name="sourcePath"/>, 같은 볼륨의 임시 폴더)을 <paramref name="desiredPath"/>(없으면) 또는 비켜 쓴 이름으로 옮긴다(W2-01).
    /// File.Move(덮어쓰기 안 함)가 '이미 있음'으로 실패하면 다음 이름 — <see cref="CreateNew"/> 와 같은 규칙. 옮긴 경로를 돌려준다.
    /// 같은 볼륨 안 이동이라 내용 복사가 없고 수정 시각도 그대로다(7z.dll·압축된 tar 경로의 임시 폴더 → 대상).
    /// 다른 프로그램(회사 PC 의 DLP(PCFILTER)·백신)이 파일을 잠깐 잡아 공유 위반이 나면 <see cref="MoveLockDelayMs"/> 간격으로
    /// <see cref="MoveLockRetries"/> 번 다시 해 본다. 그래도 안 되면 그 예외 그대로(호출자가 이미 옮긴 것을 되돌림).
    /// </summary>
    internal static string MoveNew(string sourcePath, string desiredPath)
    {
        foreach (var candidate in Candidates(desiredPath))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(sourcePath, candidate, overwrite: false);
                    return candidate;
                }
                catch (IOException ex) when (ex.HResult is ErrorFileExists or ErrorAlreadyExists)
                {
                    // 확인한 뒤 그새 다른 해제가 같은 이름을 만들었다 → 다음 후보
                    break;
                }
                catch (Exception ex) when (IsTransientLock(ex) && attempt < MoveLockRetries)
                {
                    // DLP·백신이 막 풀린 파일을 잠깐 열어 검사하는 동안은 옮길 수 없다(공유 위반) → 조금 기다렸다 다시
                    Thread.Sleep(MoveLockDelayMs);
                }
            }
        }
        throw new IOException($"고유한 출력 경로를 만들 수 없습니다: {desiredPath}");
    }

    // ───── W2-05(I1-5 같은 이름 처리)에서 더한 것 ─────
    // 화면이 해제를 ExtractionScope 로 감쌌을 때만 묻는다. 범위가 없으면 위 두 메서드와 똑같이 '이름 (1)' 로 비켜 쓴다(창 없이 풀기 계약).

    /// <summary>
    /// 같은 이름 처리를 거쳐 새 출력 파일을 연다(직접 쓰는 길 — SharpCompress·EGG/ALZ·raw bz2). null 이면 ‘건너뛰기’(쓰지 말 것).
    /// ‘덮어쓰기’면 같은 폴더의 숨김 임시 파일(<c>.~이름.8글자.tmp</c>)을 열어 준다 — 다 쓰고 닫은 뒤 <see cref="OutputTarget.Commit"/> 이 원래 파일과 바꾼다
    /// (쓰다 실패하면 임시 파일만 지우면 되고 원래 파일은 그대로). ‘취소’면 <see cref="CollisionCancelledException"/>.
    /// </summary>
    /// <param name="incomingSize">새 파일 크기(모르면 -1).</param>
    /// <param name="incomingTime">새 파일 수정 시각(모르면 null).</param>
    /// <param name="remaining">이 파일 뒤에 남은 파일 수(질문 창 안내용, 모르면 0).</param>
    internal static OutputTarget? OpenTarget(string desiredPath, long incomingSize, DateTime? incomingTime, int remaining)
    {
        var scope = ExtractionScope.Current;
        if (scope is not null && (File.Exists(desiredPath) || Directory.Exists(desiredPath)))
        {
            switch (scope.Decide(desiredPath, incomingSize, incomingTime, remaining))
            {
                case CollisionChoice.Skip:
                    return null;
                case CollisionChoice.Overwrite:
                    {
                        string tmp = TempSibling(desiredPath);
                        var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                        try { File.SetAttributes(tmp, File.GetAttributes(tmp) | FileAttributes.Hidden); } catch { /* 숨김 실패는 무시 */ }
                        return new OutputTarget(fs, tmp, desiredPath);
                    }
            }
            // Rename(또는 이번 해제가 만든 파일·폴더와 겹침): 아래 예전 규칙
        }
        var created = CreateNew(desiredPath, out string writePath);
        return new OutputTarget(created, writePath, writePath);
    }

    /// <summary>
    /// 다 풀린 파일을 같은 이름 처리를 거쳐 옮긴다(임시 폴더 → 대상 — 7z.dll·압축된 tar). null 이면 ‘건너뛰기’(옮기지 않음 — 임시 폴더째 지워짐).
    /// ‘덮어쓰기’면 File.Move(덮어쓰기)로 한 번에 바꾸고 <paramref name="overwrote"/> = true(그 경로는 원래 있던 파일이라 되돌릴 장부에 넣지 말 것).
    /// 범위가 없으면 <see cref="MoveNew"/> 그대로.
    /// </summary>
    internal static string? MoveResolved(string sourcePath, string desiredPath, int remaining, out bool overwrote)
    {
        overwrote = false;
        var scope = ExtractionScope.Current;
        if (scope is not null && (File.Exists(desiredPath) || Directory.Exists(desiredPath)))
        {
            long size = -1;
            DateTime? time = null;
            try
            {
                var fi = new FileInfo(sourcePath);
                size = fi.Length;
                time = fi.LastWriteTime;
            }
            catch
            {
                // 모르면 '알 수 없음'으로 보인다
            }
            switch (scope.Decide(desiredPath, size, time, remaining))
            {
                case CollisionChoice.Skip:
                    return null;
                case CollisionChoice.Overwrite:
                    ReplaceExisting(sourcePath, desiredPath);
                    overwrote = true;
                    scope.NoteCreated(desiredPath);
                    return desiredPath;
            }
        }
        return MoveNew(sourcePath, desiredPath);
    }

    /// <summary>
    /// <paramref name="sourcePath"/> 로 <paramref name="targetPath"/>(원래 있던 파일)를 바꾼다(같은 볼륨 — File.Move 덮어쓰기 한 번).
    /// 다른 프로그램(DLP·백신·엑셀)이 잡고 있으면 200ms 간격으로 3번 다시 해 보고, 그래도 안 되면 원래 파일을 그대로 둔 채
    /// 쉬운 한국어 문구의 IOException(원래 HResult 유지 — 공간·권한 판정이 그대로 되게).
    /// W2-11 검증 지적: 원래 파일이 '읽기 전용'이라 못 바꾼 것은 다시 해 봐도·관리자 권한으로도 안 되므로 기다리지 않고 <see cref="ReadOnlyTargetException"/>
    /// ('읽기 전용'을 끄라는 안내 — 관리자 권한으로 다시 시도를 권하지 않음). 그 밖의 접근 거부는 '쓸 권한이 없거나 다른 프로그램이 쓰고 있어요',
    /// 공유 위반은 '다른 프로그램이 쓰고 있어요 — 그 프로그램을 닫고'로 문구를 가른다(예전 한 문구는 관리자 권한 질문과 앞뒤가 맞지 않았음).
    /// </summary>
    internal static void ReplaceExisting(string sourcePath, string targetPath)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, targetPath, overwrite: true);
                return;
            }
            catch (UnauthorizedAccessException ex) when (IsReadOnlyFile(targetPath))
            {
                throw new ReadOnlyTargetException(targetPath, ex);
            }
            catch (Exception ex) when (IsTransientLock(ex) && attempt < MoveLockRetries)
            {
                Thread.Sleep(MoveLockDelayMs);
            }
            catch (Exception ex) when (IsTransientLock(ex))
            {
                string why = ex is UnauthorizedAccessException
                    ? "이 폴더에 쓸 권한이 없거나 다른 프로그램(보안 프로그램 포함)이 쓰고 있어요."
                    : "다른 프로그램(보안 프로그램 포함)이 쓰고 있어요. 그 프로그램을 닫고 다시 해 주세요.";
                throw new IOException($"'{Path.GetFileName(targetPath)}' 파일을 덮어쓰지 못했어요. {why}", ex) { HResult = ex.HResult };
            }
        }
    }

    /// <summary>있는 파일이고 '읽기 전용' 속성인가(모르면 false).</summary>
    internal static bool IsReadOnlyFile(string path)
    {
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>덮어쓰기용 임시 파일 이름: 같은 폴더의 <c>.~이름.8글자.tmp</c>(숨김) — 같은 볼륨이라 바꾸기가 한 번에 된다.</summary>
    internal static string TempSibling(string targetPath)
    {
        string dir = Path.GetDirectoryName(targetPath) ?? string.Empty;
        string name = Path.GetFileName(targetPath);
        for (int i = 0; i < 10; i++)
        {
            string p = Path.Combine(dir, $"{TempPrefix}{name}.{Guid.NewGuid().ToString("N")[..8]}.tmp");
            if (!File.Exists(p) && !Directory.Exists(p)) return p;
        }
        return Path.Combine(dir, $"{TempPrefix}{name}.{Guid.NewGuid():N}.tmp");
    }

    /// <summary>덮어쓰기 임시 파일 이름 머리.</summary>
    internal const string TempPrefix = ".~";

    private const int ErrorSharingViolation = unchecked((int)0x80070020);   // ERROR_SHARING_VIOLATION
    private const int ErrorLockViolation = unchecked((int)0x80070021);      // ERROR_LOCK_VIOLATION

    /// <summary>옮기기·지우기를 다시 해 볼 횟수와 간격(설계 §3 — IOException 이면 200ms 간격으로 3번).</summary>
    internal const int MoveLockRetries = 3;
    internal const int MoveLockDelayMs = 200;

    /// <summary>다른 프로그램이 잠깐 잡고 있어 생기는 실패(공유·잠금 위반, 접근 거부)인지.</summary>
    internal static bool IsTransientLock(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException io && io.HResult is ErrorSharingViolation or ErrorLockViolation);

    /// <summary>원래 이름 → "이름 (1).ext" … → (사실상 도달 불가) GUID 접미사 이름.</summary>
    private static IEnumerable<string> Candidates(string desiredPath)
    {
        yield return desiredPath;
        string? dir = Path.GetDirectoryName(desiredPath);
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);
        string Combine(string n) => string.IsNullOrEmpty(dir) ? n : Path.Combine(dir, n);
        for (int i = 1; i < int.MaxValue; i++)
            yield return Combine($"{name} ({i}){ext}");
        yield return Combine($"{name} ({Guid.NewGuid():N}){ext}");
    }
}

/// <summary>
/// 덮어쓸 파일이 '읽기 전용'이라 못 바꿈(W2-11 검증 지적). 권한 문제가 아니라 파일 속성이라 관리자 권한으로 다시 해도 같다 —
/// <see cref="ElevationService.IsAccessDenied"/> 가 권한 오류로 보지 않는다. 문구는 쉬운 한국어(속성에서 '읽기 전용'을 끄라는 안내), 원래 파일은 그대로.
/// </summary>
public sealed class ReadOnlyTargetException : IOException
{
    public ReadOnlyTargetException(string targetPath, Exception inner)
        : base($"'{Path.GetFileName(targetPath)}' 파일이 읽기 전용이라 덮어쓰지 못했어요. 탐색기에서 그 파일을 오른쪽 클릭 → [속성] → '읽기 전용'을 끄고 다시 해 주세요.", inner)
    {
        TargetPath = targetPath;
        HResult = inner.HResult;
    }

    /// <summary>덮어쓰려던 파일.</summary>
    public string TargetPath { get; }
}

/// <summary>
/// 같은 이름 처리를 거친 출력 파일 하나(<see cref="OutputFiles.OpenTarget"/>). <see cref="Stream"/> 에 다 쓰고 닫은 뒤 <see cref="Commit"/>.
/// 실패하면 <see cref="WritePath"/> 만 지우면 된다(덮어쓰기면 임시 파일 — 원래 파일은 그대로).
/// </summary>
internal sealed class OutputTarget
{
    public OutputTarget(FileStream stream, string writePath, string finalPath)
    {
        Stream = stream;
        WritePath = writePath;
        FinalPath = finalPath;
    }

    public FileStream Stream { get; }

    /// <summary>지금 쓰는 파일(덮어쓰기면 같은 폴더의 숨김 임시 파일, 아니면 최종 경로).</summary>
    public string WritePath { get; }

    /// <summary>다 맞으면 놓일 경로.</summary>
    public string FinalPath { get; }

    /// <summary>원래 있던 파일을 바꾸는가(그 경로는 되돌릴 장부에 넣지 말 것 — 되돌리면 사용자 파일이 사라짐).</summary>
    public bool Overwrites => !string.Equals(WritePath, FinalPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 다 쓰고 닫은 뒤 부른다. 덮어쓰기면 임시 파일로 원래 파일을 바꾼다(실패하면 임시 파일을 지우고 예외 — 원래 파일 그대로). 최종 경로를 돌려준다.
    /// </summary>
    public string Commit()
    {
        if (!Overwrites) return FinalPath;
        try
        {
            // 쓰는 동안만 숨김 — 옮기면 속성이 따라가므로 먼저 푼다
            try { File.SetAttributes(WritePath, File.GetAttributes(WritePath) & ~FileAttributes.Hidden); } catch { /* 무시 */ }
            OutputFiles.ReplaceExisting(WritePath, FinalPath);
        }
        catch
        {
            try { File.Delete(WritePath); } catch { /* 정리 실패는 무시 */ }
            throw;
        }
        ExtractionScope.Current?.NoteCreated(FinalPath);
        return FinalPath;
    }
}
