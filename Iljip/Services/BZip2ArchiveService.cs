using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Iljip.Services;

/// <summary>
/// BZIP2 포맷 — 단일 파일 압축 전용. GZip과 마찬가지로 폴더는 .tar.bz2 필요.
/// </summary>
public sealed class BZip2ArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".bz2" };

    // ─────────────────────────────────────────────────────────────────────────
    // 해제/목록: 순수 .bz2(단일 raw bzip2 스트림)는 SharpCompress의 ArchiveFactory.Open이
    // 컨테이너로 인식하지 못해 열 수 없다(=자가 생성 .bz2 라운드트립 실패).
    // 따라서 베이스(ArchiveFactory) 경로를 먼저 시도하고, 실패하면 raw bzip2 스트림으로 직접 해제한다.
    // (.tar.bz2 같은 컨테이너는 베이스 경로에서 그대로 처리됨)
    // ─────────────────────────────────────────────────────────────────────────

    public override Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            // 컨테이너(.tar.bz2 등)로 열리면 베이스로 목록을 읽는다.
            // 핵심: '컨테이너로 열리는가'만 먼저 판별하고, 일단 컨테이너로 열린 뒤의 목록/해제
            //       오류는 raw 폴백으로 삼키지 않고 그대로 전파시킨다(잘못된 출력·원오류 은폐 방지).
            if (OpensAsContainer(archivePath))
            {
                return base.ListEntriesAsync(archivePath, password, cancellationToken)
                           .GetAwaiter().GetResult();
            }

            // 컨테이너로 열리지 않음 → raw bzip2 스트림인지 BZh 시그니처로 판별.
            // 시그니처가 없으면 손상되었거나 .bz2가 아닌 파일이므로, 가짜 단일 엔트리를
            // 만들지 않고 오류로 처리한다(예전에는 무조건 단일 엔트리를 반환해 해제 단계에서야 실패).
            if (!HasBzip2Signature(archivePath))
                throw new InvalidDataException(
                    "이 파일은 올바른 BZIP2(.bz2) 형식이 아니거나 손상되었습니다.");

            // raw bzip2 폴백: 항목은 하나, 이름은 .bz2 확장자를 떼어 추정(원본 파일명을 저장하지 않음).
            string entryName = RawEntryName(archivePath);
            long compressed = SafeFileLength(archivePath);
            return new List<ArchiveEntry>
            {
                new ArchiveEntry
                {
                    Path = entryName,
                    Size = 0,               // 원본 크기는 해제 전 알 수 없음
                    CompressedSize = compressed,
                    IsDirectory = false,
                    LastModified = null
                }
            };
        }, cancellationToken);
    }

    public override Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            // 컨테이너(.tar.bz2 등)로 열리면 베이스로 해제하고, 해제 도중 오류는 그대로 전파한다.
            // (예전엔 catch-all로 해제 중 오류까지 삼켜 raw bzip2로 잘못 폴백 → tar 자체를 단일
            //  파일로 덤프하는 잘못된 출력 + 원오류 은폐가 발생했다.)
            if (OpensAsContainer(archivePath))
            {
                base.ExtractAsync(archivePath, destinationFolder, password, progress, cancellationToken)
                    .GetAwaiter().GetResult();
                return;
            }

            // 컨테이너로 열리지 않음 → raw bzip2 단일 스트림으로 직접 해제
            ExtractRawBzip2(archivePath, destinationFolder, progress, cancellationToken);
        }, cancellationToken);
    }

    public override Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            // 컨테이너로 열리면 베이스로 선택 해제하고, 해제 도중 오류는 그대로 전파한다.
            if (OpensAsContainer(archivePath))
            {
                base.ExtractEntriesAsync(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken)
                    .GetAwaiter().GetResult();
                return;
            }

            // 컨테이너 아님 → raw 폴백: 항목이 하나뿐이므로, 선택자가 그 항목과 일치할 때만 해제
            string entryName = RawEntryName(archivePath);
            bool selected = entryPaths
                .Select(p => p.Replace('\\', '/').Trim('/'))
                .Any(p => p.Equals(entryName, StringComparison.OrdinalIgnoreCase));

            if (selected)
                ExtractRawBzip2(archivePath, destinationFolder, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>raw bzip2 스트림 단일 파일을 destinationFolder/엔트리명 으로 해제.</summary>
    private static void ExtractRawBzip2(
        string archivePath,
        string destinationFolder,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        // raw 해제 직전에도 BZh 시그니처를 확인 → 손상/비-bz2 파일에 빈 출력 파일을 만들지 않는다.
        if (!HasBzip2Signature(archivePath))
            throw new InvalidDataException(
                "이 파일은 올바른 BZIP2(.bz2) 형식이 아니거나 손상되었습니다.");

        Directory.CreateDirectory(destinationFolder);

        // 엔트리명은 파일명만 사용 → 디렉터리 구분자가 없어 Zip Slip 위험 없음
        string entryName = RawEntryName(archivePath);
        // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext"로 자동 변경 → 원본 유실 방지.
        // (베이스 ExtractCore의 충돌 정책과 동일하게 맞춤)
        string outputPath = GetNonCollidingPath(Path.Combine(destinationFolder, entryName));

        progress?.Report(new ArchiveProgress
        {
            CurrentFile = entryName,
            BytesProcessed = 0,
            TotalBytes = 0,
            FilesProcessed = 0,
            TotalFiles = 1
        });

        using (var input = File.OpenRead(archivePath))
        // decompressConcatenated: true → 단일/연결(multi-stream) bzip2 모두 안전하게 읽음
        using (var bz = new BZip2Stream(input, CompressionMode.Decompress, decompressConcatenated: true))
        using (var output = File.Create(outputPath))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = bz.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(buffer, 0, read);
                written += read;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = written,
                    TotalBytes = written,   // 원본 총 크기 미상 → 누적치로 표시
                    FilesProcessed = 0,
                    TotalFiles = 1
                });
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = written,
                TotalBytes = written,
                FilesProcessed = 1,
                TotalFiles = 1
            });
        }
    }

    /// <summary>.bz2 확장자를 떼어 추정한 엔트리명. 빈 문자열이면 "output"으로 대체.</summary>
    private static string RawEntryName(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(name) ? "output" : name;
    }

    private static long SafeFileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    /// <summary>파일 선두가 raw bzip2 매직 시그니처("BZh")인지 확인. 읽기 실패 시 false.</summary>
    private static bool HasBzip2Signature(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var sig = new byte[3];
            int read = fs.Read(sig, 0, 3);
            return read == 3 && sig[0] == (byte)'B' && sig[1] == (byte)'Z' && sig[2] == (byte)'h';
        }
        catch { return false; }
    }

    /// <summary>
    /// 파일이 SharpCompress가 인식하는 '컨테이너'(.tar.bz2 등)로 열리는지 확인한다.
    /// 열리면 true(베이스 경로로 처리), 못 열면 false(raw bzip2 단일 스트림 경로).
    /// 이 판별을 '실제 해제 시도'와 분리함으로써, 컨테이너로 일단 열린 뒤의 해제 도중 오류를
    /// raw 폴백이 잘못 흡수하지 않도록 한다(원오류는 베이스에서 그대로 전파).
    /// </summary>
    private static bool OpensAsContainer(string archivePath)
    {
        try
        {
            using var archive = ArchiveFactory.Open(archivePath);
            _ = archive.Type;   // 포맷 감지 강제
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (options?.HasPassword == true)
                throw new NotSupportedException("BZIP2는 암호화를 지원하지 않아요.");

            var srcList = sourcePaths.ToList();
            if (srcList.Count == 0)
                throw new InvalidOperationException("압축할 파일이 없습니다.");

            // BZ2도 단일 파일 포맷. 여러 항목이면 조용히 첫 파일만 압축하지 않고 명확히 안내.
            if (srcList.Count > 1)
                throw new InvalidOperationException(
                    "BZIP2(.bz2)는 파일 하나만 압축할 수 있어요.\n" +
                    "여러 개를 한 번에 압축하려면 ZIP을 사용하거나, 하나로 묶어 .tar.bz2로 만들어주세요. (.tar.bz2는 추후 지원 예정)");

            string sourceFile = srcList[0];
            if (Directory.Exists(sourceFile))
                throw new InvalidOperationException("BZIP2는 단일 파일만 압축할 수 있어요. 폴더는 .tar.bz2를 사용해주세요. (추후 지원 예정)");

            string? archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
                Directory.CreateDirectory(archiveDir);

            long totalBytes = new FileInfo(sourceFile).Length;
            string entryName = Path.GetFileName(sourceFile);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                TotalBytes = totalBytes,
                TotalFiles = 1
            });

            using (var input = File.OpenRead(sourceFile))
            using (var output = File.Create(archivePath))
            using (var bz = new BZip2Stream(output, CompressionMode.Compress, decompressConcatenated: false))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bz.Write(buffer, 0, read);
                    copied += read;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryName,
                        BytesProcessed = copied,
                        TotalBytes = totalBytes,
                        TotalFiles = 1
                    });
                }
            }

            progress?.Report(new ArchiveProgress
            {
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = 1,
                TotalFiles = 1
            });
        }, cancellationToken);
    }
}
