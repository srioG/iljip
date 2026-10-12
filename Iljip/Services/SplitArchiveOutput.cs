using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 나눠 만들기(분할 압축)의 공통 틀(W2-03, I2-2 만들기 — ZIP·7Z 서비스가 <see cref="CompressionOptions.SplitBytes"/> 가 있을 때 부름).
/// <list type="number">
/// <item>대상 폴더 안 숨김 임시 폴더(<see cref="StagingFolder"/> — <c>.일집-임시-8글자</c>, 같은 볼륨)에 보통 압축 파일 하나를 다 만든다
///   (형식별 규칙·암호·건너뛰기·진행률·취소는 그 서비스 그대로).</item>
/// <item>다 만든 것이 조각 크기 이하면 나누지 않고 저장 창에서 고른 이름의 보통 파일로 둔다(조각 하나짜리 세트는 받는 사람만 헷갈림).</item>
/// <item>아니면 조각으로 바꾼다 — ZIP 은 <see cref="ZipSpanWriter"/>(이름.z01 … 이름.zip), 7Z 는 <see cref="VolumeSplitter"/>(이름.7z.001 …).</item>
/// <item>다 맞게 끝났을 때만 조각을 대상 폴더로 옮긴다. 같은 이름의 조각이 하나라도 있으면 세트 전체를 '이름 (2)' …로 바꾼다(세트 단위 —
///   있던 세트는 바이트 그대로, 섞이지 않음). 7Z 는 다음 번호 조각(.00N+1)이 있어도 바꾼다(이어 붙여 읽다가 남의 조각이 붙지 않게).</item>
/// </list>
/// 실패·취소면 임시 폴더째 지워 조각도 반쪽 파일도 남기지 않는다(옮기다 실패하면 이번에 옮긴 조각도 지움 — 일집이 만든 것뿐).
/// 진행률: 압축이 끝났다는 보고(현재 파일 '')는 조각까지 다 만든 뒤 한 번만 보낸다 — 나누는 동안은 '조각으로 나누는 중'.
/// </summary>
internal static class SplitArchiveOutput
{
    /// <summary>조각으로 나누는 동안의 진행 창 문구.</summary>
    internal const string StageSplitting = "조각으로 나누는 중";

    /// <summary>임시 폴더 안에서 다 만드는 보통 ZIP 의 이름(설계의 full.zip — 조각 이름과 겹치지 않게 확장자를 .tmp 로).</summary>
    private const string SingleZipName = "full.tmp";

    internal enum VolumeKind
    {
        /// <summary>PKZIP 나눔: 이름.z01 … 이름.zip.</summary>
        Zip,
        /// <summary>숫자 조각: 이름.7z.001 ….</summary>
        SevenZip,
    }

    /// <param name="archivePath">저장 창에서 고른 경로(이름.zip / 이름.7z).</param>
    /// <param name="writeSingle">(임시 폴더 안 경로, 진행률) → 보통 압축 파일 하나를 그 경로에 만든다.</param>
    public static CompressResult Write(
        string archivePath, long splitBytes, VolumeKind kind, IProgress<ArchiveProgress>? progress, CancellationToken ct,
        Func<string, IProgress<ArchiveProgress>, CompressResult> writeSingle)
    {
        string target = Path.GetFullPath(archivePath);
        string folder = Path.GetDirectoryName(target) ?? throw new IOException("압축 파일을 둘 폴더를 알 수 없어요: " + archivePath);
        Directory.CreateDirectory(folder);
        bool existed = File.Exists(target);
        var deferred = new DeferredFinishProgress(progress);
        using var staging = StagingFolder.Create(folder);
        // ZIP 은 마지막 조각 이름(이름.zip)과 겹치지 않게 'full.tmp' 에 만든다(조각 이름은 늘 .zNN·.zip). 7Z 는 고른 이름 — 그 파일이 그대로 .001 이 된다.
        string single = Path.Combine(staging.FullPath, kind == VolumeKind.Zip ? SingleZipName : Path.GetFileName(target));

        var result = writeSingle(single, deferred);
        ct.ThrowIfCancellationRequested();

        if (new FileInfo(single).Length <= splitBytes)
        {
            // 한 조각에 다 들어간다 — 나누지 않고 고른 이름 그대로(있던 파일은 저장 창에서 덮어쓰기를 고른 것)
            MoveWithRetry(single, target, overwrite: existed);
            deferred.Finish();
            return result;
        }

        deferred.Stage(StageSplitting);
        var pieces = kind == VolumeKind.Zip
            ? ZipSpanWriter.Split(single, Path.Combine(staging.FullPath, ZipStem(Path.GetFileName(target))), splitBytes, ct)
            : VolumeSplitter.Split(single, single, splitBytes, ct);
        ct.ThrowIfCancellationRequested();
        var moved = CommitSet(folder, Path.GetFileName(target), kind, pieces);
        deferred.Finish();
        return new CompressResult(result.Skipped)
        {
            Volumes = moved,
            OpenPath = kind == VolumeKind.Zip ? moved[^1] : moved[0],
        };
    }

    /// <summary>
    /// 세트의 조각 이름(대상 폴더 안 파일 이름). <paramref name="number"/> 1 = 고른 이름 그대로, 2 이상 = '이름 (2)' ….
    /// ZIP: 이름.z01 … 이름.zip, 7Z: 이름.7z.001 ….
    /// </summary>
    internal static IReadOnlyList<string> SetNames(VolumeKind kind, string fileName, int number, int count)
    {
        if (kind == VolumeKind.Zip)
        {
            string stem = ZipStem(fileName);
            return ZipSpanWriter.VolumeNames(number == 1 ? stem : $"{stem} ({number})", count);
        }
        string baseName = SevenZipBase(fileName, number);
        return Enumerable.Range(0, count).Select(i => VolumeSplitter.VolumeName(baseName, i)).ToList();
    }

    /// <summary>세트 이름을 정할 때 '있으면 안 되는' 이름 전부 — 세트의 조각들(7Z 는 다음 번호 조각도).</summary>
    internal static IEnumerable<string> GuardNames(VolumeKind kind, string fileName, int number, int count)
    {
        foreach (var n in SetNames(kind, fileName, number, count)) yield return n;
        if (kind == VolumeKind.SevenZip) yield return VolumeSplitter.VolumeName(SevenZipBase(fileName, number), count);
    }

    /// <summary>'이름.zip' → '이름'(확장자가 .zip 이 아니면 이름 그대로).</summary>
    private static string ZipStem(string fileName) =>
        fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && fileName.Length > 4 ? fileName[..^4] : fileName;

    /// <summary>'이름.7z' → 1: '이름.7z', 2: '이름 (2).7z'.</summary>
    private static string SevenZipBase(string fileName, int number)
    {
        if (number == 1) return fileName;
        string ext = Path.GetExtension(fileName);
        string stem = ext.Length > 0 && fileName.Length > ext.Length ? fileName[..^ext.Length] : fileName;
        return ext.Length > 0 && stem != fileName ? $"{stem} ({number}){ext}" : $"{fileName} ({number})";
    }

    /// <summary>
    /// 임시 폴더의 조각들을 대상 폴더로 옮긴다(덮어쓰지 않음). 세트 이름 중 하나라도 있으면 다음 번호 세트. 옮기다 실패하면 이번에 옮긴 조각을 지우고 그 예외.
    /// </summary>
    private static IReadOnlyList<string> CommitSet(string folder, string fileName, VolumeKind kind, IReadOnlyList<string> pieces)
    {
        for (int number = 1; number < 10_000; number++)
        {
            if (GuardNames(kind, fileName, number, pieces.Count).Any(n => Exists(Path.Combine(folder, n)))) continue;
            var names = SetNames(kind, fileName, number, pieces.Count);
            var moved = new List<string>(pieces.Count);
            try
            {
                for (int i = 0; i < pieces.Count; i++)
                {
                    string dest = Path.Combine(folder, names[i]);
                    MoveWithRetry(pieces[i], dest, overwrite: false);
                    moved.Add(dest);
                }
                return moved;
            }
            catch
            {
                foreach (var m in moved)
                {
                    try { File.Delete(m); } catch { /* 정리 실패는 무시 */ }
                }
                throw;
            }
        }
        throw new IOException("조각 이름을 정할 수 없어요: " + fileName);

        static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    }

    /// <summary>옮기기 — DLP·백신이 잠깐 잡으면 200ms 간격으로 3번 다시(설계 §3).</summary>
    private static void MoveWithRetry(string from, string to, bool overwrite)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite);
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    /// <summary>
    /// 압축 서비스의 진행률을 그대로 넘기되 '끝남'(현재 파일 '') 보고만 붙잡아 두었다가 조각까지 다 만든 뒤 한 번 보낸다(진행 창은 끝 보고를 한 번만 받음).
    /// </summary>
    private sealed class DeferredFinishProgress : IProgress<ArchiveProgress>
    {
        private readonly IProgress<ArchiveProgress>? _inner;
        private ArchiveProgress? _final;

        public DeferredFinishProgress(IProgress<ArchiveProgress>? inner) => _inner = inner;

        public void Report(ArchiveProgress value)
        {
            if (value.CurrentFile.Length == 0)
            {
                _final = value;
                return;
            }
            _inner?.Report(value);
        }

        /// <summary>단계 문구(압축은 다 끝남 — 처리량은 다 찬 값).</summary>
        public void Stage(string text)
        {
            if (_final is null) return;
            _inner?.Report(new ArchiveProgress
            {
                CurrentFile = text,
                BytesProcessed = _final.BytesProcessed,
                TotalBytes = _final.TotalBytes,
                FilesProcessed = _final.FilesProcessed,
                TotalFiles = _final.TotalFiles,
            });
        }

        public void Finish()
        {
            if (_final is not null) _inner?.Report(_final);
        }
    }
}
