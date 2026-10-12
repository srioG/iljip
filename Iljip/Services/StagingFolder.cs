using System.IO;

namespace Iljip.Services;

/// <summary>
/// 풀기 임시 폴더(설계 §3, W2-01): 대상 폴더 안의 숨김 폴더 <c>.일집-임시-&lt;8글자&gt;</c>. 같은 볼륨이라 다 맞으면 옮기기만 하면 된다
/// (내용 복사 없음, 수정 시각 유지). 7z.dll 경로(<see cref="SevenZipEngine"/>)와 압축된 tar(<see cref="CompressedTarArchiveService"/>)가 쓴다.
/// 다 풀고 검증이 끝난 뒤에만 대상으로 옮기므로(<see cref="Commit"/>), 손상·잘림이면 대상에 아무것도 남지 않는다.
/// 비정상 종료로 남은 것은 같은 대상에 다시 풀 때 하루 지난 것을 지운다(<see cref="CleanupStale"/>).
/// </summary>
internal sealed class StagingFolder : IDisposable
{
    /// <summary>임시 폴더 이름 머리(숨김).</summary>
    internal const string Prefix = ".일집-임시-";

    public string FullPath { get; }

    private StagingFolder(string fullPath) => FullPath = fullPath;

    /// <summary>대상 폴더(있어야 함) 안에 새 임시 폴더를 만든다. 하루 지난 임시 폴더는 먼저 지운다.</summary>
    public static StagingFolder Create(string destinationFolder)
    {
        CleanupStale(destinationFolder);
        for (int i = 0; i < 10; i++)
        {
            string p = Path.Combine(destinationFolder, Prefix + Guid.NewGuid().ToString("N")[..8]);
            if (Directory.Exists(p) || File.Exists(p)) continue;
            var di = Directory.CreateDirectory(p);
            try { di.Attributes |= FileAttributes.Hidden; } catch { /* 숨김 실패는 무시 */ }
            return new StagingFolder(p);
        }
        throw new IOException("임시 폴더를 만들지 못했어요: " + destinationFolder);
    }

    /// <summary>
    /// 임시 폴더의 파일들을 대상으로 옮긴다(같은 이름이 있으면 "이름 (1).ext" — <see cref="OutputFiles.MoveNew"/>, 잠김은 다시 시도).
    /// <paramref name="files"/> = (임시 폴더 안 전체 경로, 대상 폴더 기준 상대 경로), <paramref name="directories"/> = 만들 폴더(상대 경로).
    /// 크기 0 파일을 엔진이 만들지 않았으면(원본 경로가 없음) 빈 파일을 만든다.
    /// 중간에 실패하면 이번에 옮긴 파일·만든 폴더를 모두 지우고(<see cref="ExtractionLedger"/>) 그 예외를 그대로 던진다 — 반쪽 결과를 남기지 않는다.
    /// </summary>
    public static void Commit(string destinationFolder, IEnumerable<(string From, string TargetRel)> files, IEnumerable<string>? directories = null)
    {
        var ledger = new ExtractionLedger();
        try
        {
            ledger.EnsureDirectory(destinationFolder);
            if (directories is not null)
                foreach (var d in directories)
                    ledger.EnsureDirectory(Path.Combine(destinationFolder, d));
            foreach (var (from, rel) in files)
            {
                string to = Path.Combine(destinationFolder, rel);
                string? parent = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(parent)) ledger.EnsureDirectory(parent);
                if (File.Exists(from))
                {
                    ledger.AddFile(OutputFiles.MoveNew(from, to));
                    continue;
                }
                using (OutputFiles.CreateNew(to, out string written))
                    ledger.AddFile(written);
            }
        }
        catch
        {
            ledger.Rollback();
            throw;
        }
    }

    public void Dispose() => Delete(FullPath, attempts: OutputFiles.MoveLockRetries + 1);

    /// <summary>임시 폴더 지우기 — 회사 PC 의 DLP 가 파일을 잠깐 잡을 수 있어 몇 번 다시 해 본다(200ms 간격).</summary>
    internal static void Delete(string path, int attempts)
    {
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path)) return;
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* 무시 */ }
                }
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == attempts - 1)
                {
                    Logger.LogError("임시 폴더를 지우지 못함(다음 해제 때 하루 지난 것을 다시 지움): " + path, ex);
                    return;
                }
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    /// <summary>같은 대상 폴더에 남은 하루 지난 임시 폴더(비정상 종료 흔적)를 지운다. 실패해도 조용히 넘어간다.</summary>
    internal static void CleanupStale(string destinationFolder)
    {
        try
        {
            foreach (var d in Directory.EnumerateDirectories(destinationFolder, Prefix + "*"))
            {
                if (Directory.GetCreationTime(d) < DateTime.Now.AddDays(-1))
                    Delete(d, attempts: 1);
            }
        }
        catch
        {
            // 정리는 덤 — 실패 무시
        }
    }
}
