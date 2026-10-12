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
