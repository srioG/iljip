using System.IO;

namespace Iljip.Services;

/// <summary>
/// 해제 한 번이 '새로 만든' 파일·폴더 장부. 암호 오류(<see cref="ArchivePasswordException"/>)면 <see cref="Rollback"/> 으로
/// 이번 실행이 만든 것을 모두 지운다 — 틀린 암호가 검사를 우연히 통과해 쓰레기 파일을 쓴 뒤에야 걸려도, 맞는 암호로 다시 풀 때
/// 진짜 파일이 '이름 (1)' 로 비켜 써지지 않게(B4). 손상·CRC 오류·취소는 지금 계약대로 쓰다 만 그 파일만 지운다(장부는 안 씀).
/// ZIP 의 두 암호 후보를 못 가려 다른 후보로 다시 풀 때도 첫 시도가 만든 것을 이것으로 모두 지운다(같은 이름으로 다시 쓰게).
/// 원래 있던 파일·폴더는 장부에 없으므로 절대 지우지 않는다. 폴더는 우리가 만들었고 비어 있을 때만 지운다.
/// 서비스 객체는 여러 해제가 같이 쓰므로 장부는 해제마다 새로 만든다.
/// </summary>
internal sealed class ExtractionLedger
{
    private readonly List<string> _createdDirectories = new();
    private readonly List<string> _createdFiles = new();

    /// <summary>폴더를 만든다(없는 조상까지). 새로 생긴 폴더만 위에서부터 차례로 장부에 적는다.</summary>
    public void EnsureDirectory(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || Directory.Exists(fullPath)) return;
        var missing = new Stack<string>();
        for (string? d = Path.GetFullPath(fullPath); !string.IsNullOrEmpty(d) && !Directory.Exists(d); d = Path.GetDirectoryName(d))
            missing.Push(d);
        Directory.CreateDirectory(fullPath);
        while (missing.Count > 0) _createdDirectories.Add(missing.Pop());
    }

    public void AddFile(string path) => _createdFiles.Add(path);

    /// <summary>이미 지운 파일(쓰다 만 파일 정리)은 장부에서 뺀다.</summary>
    public void ForgetFile(string path) => _createdFiles.Remove(path);

    /// <summary>만든 순서의 거꾸로 지운다. 실패는 무시(다른 프로그램이 잡고 있는 등).</summary>
    public void Rollback()
    {
        for (int i = _createdFiles.Count - 1; i >= 0; i--)
        {
            try { File.Delete(_createdFiles[i]); } catch { /* 정리 실패는 무시 */ }
        }
        _createdFiles.Clear();
        for (int i = _createdDirectories.Count - 1; i >= 0; i--)
        {
            string d = _createdDirectories[i];
            try
            {
                if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any())
                    Directory.Delete(d, recursive: false);
            }
            catch { /* 정리 실패는 무시 */ }
        }
        _createdDirectories.Clear();
    }
}
