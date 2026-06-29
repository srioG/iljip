using System.IO;
using EggDotNet;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 이스트소프트 EGG/ALZ 해제 전용 서비스 (EggDotNet 기반).
///
/// SharpCompress는 egg/alz를 못 풀기 때문에 <see cref="SharpCompressArchiveServiceBase"/>를
/// 상속하지 않고 <see cref="IArchiveService"/>를 직접 구현한다. 따라서 베이스가 주던 안전장치
/// (Zip Slip 방어·동명파일 비파괴 자동개명·진행률 보고)를 여기서 직접 갖춘다.
///
/// EggDotNet은 store/deflate/bzip2/lzma + 이스트소프트 독자 AZO + 분할볼륨(.volN.egg)을 모두
/// 해제한다(자체 테스트 픽스처로 CRC 일치 검증 완료). 압축(생성)은 지원하지 않는다(반디집/알집 영역).
/// EGG 파일명은 유니코드, ALZ는 CP949이며 EggDotNet이 디코드한 FullName을 그대로 쓴다(이중 디코드 금지).
/// </summary>
public sealed class EggAlzArchiveService : IArchiveService
{
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".egg", ".alz" };

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            using var archive = OpenArchive(archivePath, password);
            var entries = new List<ArchiveEntry>();
            foreach (var e in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full = (e.FullName ?? e.Name ?? string.Empty).Replace('\\', '/');
                entries.Add(new ArchiveEntry
                {
                    Path = full,
                    Size = e.UncompressedLength,
                    CompressedSize = e.CompressedLength,
                    IsDirectory = full.EndsWith("/", StringComparison.Ordinal),
                    LastModified = e.LastWriteTime
                });
            }
            return entries;
        }, cancellationToken);
    }

    public Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExtractCore(archivePath, destinationFolder, _ => true, password, progress, cancellationToken);

    public Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selectors = entryPaths
            .Select(p => p.Replace('\\', '/').Trim('/'))
            .Where(p => p.Length > 0)
            .ToList();

        bool ShouldExtract(string decodedPath)
        {
            string d = decodedPath.Replace('\\', '/').TrimEnd('/');
            foreach (var s in selectors)
            {
                if (d.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
                if (d.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        return ExtractCore(archivePath, destinationFolder, ShouldExtract, password, progress, cancellationToken);
    }

    private static Task ExtractCore(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            using var archive = OpenArchive(archivePath, password);

            // 진행률 총량 선계산 (디렉터리·필터제외·암호화 제외분은 빼고 파일만)
            long totalBytes = 0;
            int totalFiles = 0;
            foreach (var e in archive.Entries)
            {
                string fp = (e.FullName ?? e.Name ?? string.Empty).Replace('\\', '/');
                if (fp.EndsWith("/", StringComparison.Ordinal)) continue;
                if (!shouldExtract(fp)) continue;
                totalBytes += e.UncompressedLength;
                totalFiles++;
            }

            long bytesProcessed = 0;
            int filesProcessed = 0;
            string fullDestRoot = Path.GetFullPath(destinationFolder + Path.DirectorySeparatorChar);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string decodedPath = (entry.FullName ?? entry.Name ?? string.Empty).Replace('\\', '/');
                if (decodedPath.Length == 0) continue;
                if (!shouldExtract(decodedPath)) continue;

                string outputPath = Path.Combine(destinationFolder, decodedPath);
                string fullOutputPath = Path.GetFullPath(outputPath);

                // Zip Slip 방어: 해제 경로가 대상 폴더 밖으로 나가면 거부.
                if (!fullOutputPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"안전하지 않은 경로: {decodedPath}");

                if (decodedPath.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(fullOutputPath);
                    continue;
                }

                string? parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext" 식으로 → 원본 유실 방지.
                string writePath = GetNonCollidingPath(fullOutputPath);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = decodedPath,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                // entry.Open()이 store/deflate/bzip2/lzma/AZO·암호화를 알아서 해제해 평문 스트림을 준다.
                // 암호화인데 비번이 없으면 여기서 예외 → 해제 전체 실패(부분 추출로 위장하지 않음).
                try
                {
                    using (var input = entry.Open())
                    using (var output = File.Create(writePath))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            bytesProcessed += read;

                            progress?.Report(new ArchiveProgress
                            {
                                CurrentFile = decodedPath,
                                BytesProcessed = bytesProcessed,
                                TotalBytes = totalBytes,
                                FilesProcessed = filesProcessed,
                                TotalFiles = totalFiles
                            });
                        }
                    }
                }
                catch
                {
                    // 취소/오류로 중단되면 절반만 기록된 writePath(정상 이름이라 완전한 파일로 오인됨)를
                    // 지우고 예외를 그대로 전파한다(이미 완료된 앞 파일들은 보존).
                    try { File.Delete(writePath); } catch { /* 정리 실패는 무시 */ }
                    throw;
                }

                if (entry.LastWriteTime is { } lwt)
                {
                    try { File.SetLastWriteTime(writePath, lwt); }
                    catch { /* 일부 값/파일에서 실패 가능 — 무시 */ }
                }

                filesProcessed++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });
        }, cancellationToken);
    }

    /// <summary>
    /// EGG/ALZ 아카이브를 연다.
    ///
    /// 비밀번호가 없으면 <see cref="EggFile.Open(string)"/>(= 2-인자 EggArchive 생성자: 기본 분할볼륨·콜백)을
    /// 그대로 써서 기존 검증된 동작을 100% 보존한다.
    ///
    /// 비밀번호가 주어지면 EggFile.Open에는 비밀번호 오버로드가 없어 암호화 egg/alz를 풀 수 없으므로,
    /// 4-인자 EggArchive 생성자에 복호화 콜백을 주입한다. 분할볼륨(.volN.egg) 콜백은 EggDotNet의 기본 구현
    /// (DefaultStreamCallbacks.DefaultFileStreamCallback)이 internal이라 직접 호출할 수 없어 동일 로직
    /// (형제 파일을 분할 후보 스트림으로 제공)을 여기서 복제한다.
    /// </summary>
    private static EggArchive OpenArchive(string archivePath, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return EggFile.Open(archivePath);

        // ownStream:true 이므로 EggArchive(=using) 폐기 시 이 스트림도 닫힌다.
        // 단, 생성자가 던지면 EggArchive가 책임지지 못하므로 여기서 직접 닫는다.
        var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            // EggDotNet 기본 분할 콜백 복제: 같은 폴더의 형제 파일들을 분할볼륨 후보로 넘긴다.
            Callbacks.SplitFileReceiverCallback splitCallback = st =>
            {
                var streams = new List<Stream>();
                if (st is FileStream fs && Path.GetDirectoryName(fs.Name) is { Length: > 0 } dir)
                {
                    foreach (var sibling in Directory.GetFiles(dir))
                        if (!string.Equals(sibling, fs.Name, StringComparison.Ordinal))
                            streams.Add(new FileStream(sibling, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                return streams;
            };
            // Retry=false: 비번이 틀리면 즉시 DecryptFailedException → 상위 재시도 루프가 다시 프롬프트.
            Callbacks.FileDecryptPasswordCallback passwordCallback =
                (_, opts) => { opts.Password = password!; opts.Retry = false; };
            return new EggArchive(stream, ownStream: true, splitCallback, passwordCallback);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "EGG/ALZ 압축(생성)은 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }

    /// <summary>
    /// 대상 경로에 동명 파일/디렉터리가 이미 있으면 "이름 (1).ext" … 식으로 충돌 없는 새 경로를
    /// 만들어 반환한다(기존 파일 비파괴). SharpCompressArchiveServiceBase와 동일 규칙.
    /// </summary>
    private static string GetNonCollidingPath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
            return desiredPath;

        string? dir = Path.GetDirectoryName(desiredPath);
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);

        for (int i = 1; i < int.MaxValue; i++)
        {
            string candidateName = $"{name} ({i}){ext}";
            string candidate = string.IsNullOrEmpty(dir)
                ? candidateName
                : Path.Combine(dir, candidateName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        // 사실상 도달 불가. desiredPath를 그대로 반환하면 기존 파일을 덮어쓰므로(원본 비파괴 계약
        // 위반), GUID 접미사로 고유 경로를 만들고 그것마저 충돌하면 조용한 덮어쓰기 대신 예외로 실패.
        string guidName = $"{name} ({Guid.NewGuid():N}){ext}";
        string guidPath = string.IsNullOrEmpty(dir) ? guidName : Path.Combine(dir, guidName);
        if (!File.Exists(guidPath) && !Directory.Exists(guidPath))
            return guidPath;
        throw new IOException($"고유한 출력 경로를 만들 수 없습니다: {desiredPath}");
    }
}
