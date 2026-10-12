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
