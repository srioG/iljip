namespace Iljip.Tests.TestSupport;

/// <summary>
/// 테스트 하나가 쓰는 고유 임시 폴더. Dispose 때 통째로 지운다.
/// 병렬 실행 안전: 폴더 이름에 GUID 를 넣어 테스트끼리 절대 겹치지 않는다.
/// 회사 PC 의 DLP 가 파일을 잠깐 잡고 있거나, 열기 실패 때 라이브러리가 파일 핸들을 놓친 경우(B8)가 있어
/// 지우기는 GC 한 번 + 짧은 대기로 3번까지만 다시 해 본다(무한 반복 금지).
/// </summary>
public sealed class TempDir : IDisposable
{
    public static string BaseRoot { get; } = Path.Combine(Path.GetTempPath(), "IljipTests");

    public string Root { get; }

    public TempDir(string tag = "t")
    {
        Root = Path.Combine(BaseRoot, $"{tag}_{Guid.NewGuid():N}"[..Math.Min(tag.Length + 13, 40)]);
        Directory.CreateDirectory(Root);
    }

    /// <summary>Root 아래 경로를 만든다(폴더는 만들지 않음).</summary>
    public string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>Root 아래에 폴더를 만들고 그 경로를 돌려준다.</summary>
    public string Dir(params string[] parts)
    {
        string p = P(parts);
        Directory.CreateDirectory(p);
        return p;
    }

    public void Dispose()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(Root)) return;
                foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* 무시 */ }
                }
                Directory.Delete(Root, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 라이브러리가 열기 실패 때 놓친 파일 핸들(B8)은 GC 종료자가 닫는다 — 한 번 거둬 들인 뒤 다시 시도
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(150 * (attempt + 1));
            }
        }
        // 끝내 못 지우면 테스트 결과에는 영향 주지 않고 남겨 둔다(다음 실행과 이름이 겹치지 않음).
    }
}
