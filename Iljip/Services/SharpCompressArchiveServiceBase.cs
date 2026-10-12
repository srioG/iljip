using System.IO;
using System.Text;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// SharpCompress 기반 포맷들의 공통 베이스.
/// 압축 해제와 항목 목록 읽기는 <see cref="OpenForRead"/>(기본: 형식 판별 → 형식별 OpenArchive)로 모든 지원 포맷에서
/// 동일하게 동작하므로 여기에 공통 구현을 두고, 각 포맷별 압축 로직은 파생 클래스가 구현한다.
/// </summary>
public abstract class SharpCompressArchiveServiceBase : IArchiveService
{
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            var options = BuildReaderOptions(password);
            using var archive = OpenForRead(archivePath, options);

            var entries = new List<ArchiveEntry>();
            foreach (var e in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // e.Key는 ReaderOptions.ArchiveEncoding.CustomDecoder(=DecodeBytes)로 이미
                // 올바르게 디코드된 상태다. 여기서 KoreanFileNameDecoder.Decode를 또 적용하면
                // (이중 디코드) 비한글 비ASCII 파일명이 CP437→CP949 오재해석으로 손상될 수 있어
                // 다시 디코드하지 않는다.
                string decoded = MapEntryKey(archivePath, e.Key);

                entries.Add(new ArchiveEntry
                {
                    Path = decoded,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    IsDirectory = e.IsDirectory,
                    LastModified = e.LastModifiedTime
                });
            }
            return entries;
        }, cancellationToken);
    }

    public virtual Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExtractCore(archivePath, destinationFolder, _ => true, password, progress, cancellationToken);

    public virtual Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 선택자 정규화: 파일은 정확히 일치, 폴더는 그 하위 전체를 포함
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

    /// <summary>해제 공통 구현. <paramref name="shouldExtract"/>로 항목을 필터링한다.</summary>
    private Task ExtractCore(
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

            var options = BuildReaderOptions(password);
            using var archive = OpenForRead(archivePath, options);

            long totalBytes = 0;
            int totalFiles = 0;
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory) continue;
                string dp = MapEntryKey(archivePath, e.Key);   // CustomDecoder가 이미 디코드 (이중 디코드 방지)
                if (!shouldExtract(dp)) continue;
                totalBytes += e.Size;
                totalFiles++;
            }

            long bytesProcessed = 0;
            int filesProcessed = 0;
            string fullDestRoot = Path.GetFullPath(destinationFolder + Path.DirectorySeparatorChar);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string decodedPath = MapEntryKey(archivePath, entry.Key);   // CustomDecoder가 이미 디코드 (이중 디코드 방지)
                if (!shouldExtract(decodedPath)) continue;

                string outputPath = Path.Combine(destinationFolder, decodedPath);
                string fullOutputPath = Path.GetFullPath(outputPath);

                // Zip Slip 방어
                if (!fullOutputPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"안전하지 않은 경로: {decodedPath}");

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(fullOutputPath);
                    continue;
                }

                string? parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = decodedPath,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext" 식으로 자동 변경 → 원본 유실 방지(OutputFiles).
                var output = OutputFiles.CreateNew(fullOutputPath, out string writePath);
                try
                {
                    using (output)
                    using (var input = OpenEntryData(archivePath, entry))
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

                if (entry.LastModifiedTime is DateTime dt)
                {
                    try { File.SetLastWriteTime(writePath, dt); }
                    catch { /* 일부 파일에서 실패 가능 */ }
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

    public abstract Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 압축 시 소스 파일 평탄화. 폴더면 재귀, 파일이면 그대로.
    /// 폴더 자체의 이름을 entry 루트로 두는 반디집/알집 기본 동작과 동일.
    /// </summary>
    protected static List<(string sourceFile, string entryName)> CollectFiles(IEnumerable<string> sourcePaths)
    {
        var result = new List<(string, string)>();
        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                result.Add((src, Path.GetFileName(src)));
            }
            else if (Directory.Exists(src))
            {
                string baseDir = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string parent = Path.GetDirectoryName(baseDir) ?? string.Empty;

                foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(parent, file).Replace(Path.DirectorySeparatorChar, '/');
                    result.Add((file, rel));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// 압축 소스에서 '완전히 빈 디렉터리'(파일도 하위 폴더도 전혀 없는)의 상대 경로를 수집한다.
    /// 반환 형식은 CollectFiles의 entryName과 동일(부모 기준 상대, '/' 구분)하되 끝에 '/'를 붙인다.
    /// <para>
    /// CollectFiles는 파일만 모으므로, 안에 파일이 하나도 없는 폴더는 압축 결과에서 조용히 누락된다.
    /// 파일이 있는 폴더는 파일 엔트리가, 하위 폴더가 있는 폴더는 그 하위 엔트리가 경로를 만들어주므로,
    /// '파일도 하위 폴더도 없는 완전히 빈 디렉터리'만 별도 엔트리로 보존하면 충분하다.
    /// </para>
    /// </summary>
    protected static List<string> CollectEmptyDirectories(IEnumerable<string> sourcePaths) =>
        CollectEmptyDirectoriesWithSource(sourcePaths).Select(d => d.entryName).ToList();

    /// <summary><see cref="CollectEmptyDirectories"/>와 같은 규칙. 원본 폴더 경로(수정 시각을 옮길 때 씀)도 같이 돌려준다.</summary>
    protected static List<(string sourceDir, string entryName)> CollectEmptyDirectoriesWithSource(IEnumerable<string> sourcePaths)
    {
        var result = new List<(string, string)>();
        foreach (var src in sourcePaths)
        {
            if (!Directory.Exists(src)) continue;

            string baseDir = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(baseDir) ?? string.Empty;

            var allDirs = new List<string> { baseDir };
            allDirs.AddRange(Directory.EnumerateDirectories(baseDir, "*", SearchOption.AllDirectories));

            foreach (var dir in allDirs)
            {
                // 파일도 하위 디렉터리도 전혀 없는 '완전히 빈' 디렉터리만 명시적 엔트리가 필요.
                if (Directory.EnumerateFileSystemEntries(dir).Any()) continue;

                string rel = Path.GetRelativePath(parent, dir).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.Length == 0 || rel == ".") continue;
                result.Add((dir, rel.TrimEnd('/') + "/"));
            }
        }
        return result;
    }

    /// <summary>입력 파일들의 총 바이트 합계 (진행률 계산용).</summary>
    protected static long SumFileSizes(IEnumerable<(string sourceFile, string entryName)> files)
    {
        long total = 0;
        foreach (var (sourceFile, _) in files)
        {
            try { total += new FileInfo(sourceFile).Length; }
            catch { /* 권한 등 */ }
        }
        return total;
    }

    /// <summary>
    /// 엔진이 준 항목 이름(이미 CustomDecoder 로 디코드됨)을 화면·해제에 쓸 이름으로 바꾼다. 기본은 그대로.
    /// 목록·해제·선택 해제 비교가 모두 이 이름을 쓰므로, 파생 클래스가 '이름 없는 항목'(FNAME 없는 .gz) 등을 여기서 메운다.
    /// </summary>
    protected virtual string MapEntryKey(string archivePath, string? key) => key ?? string.Empty;

    /// <summary>목록·해제에서 압축 파일을 여는 곳. 기본은 <see cref="OpenTyped"/>. 형식 하나만 다루는 파생 클래스가 바꿀 수 있다.</summary>
    protected virtual IArchive OpenForRead(string archivePath, ReaderOptions options) => OpenTyped(archivePath, options);

    /// <summary>
    /// 해제할 때 항목의 '풀린 내용' 스트림을 여는 곳. 기본은 엔진 그대로(<see cref="IArchiveEntry.OpenEntryStream"/>).
    /// 엔진이 내용 확인(CRC·길이)을 하지 않는 형식은 파생 클래스가 확인하는 스트림으로 바꾼다(.gz → GZipArchiveService).
    /// 여기서 나는 예외는 해제 공통 구현이 받아 쓰다 만 파일을 지우고 그대로 전한다.
    /// </summary>
    protected virtual Stream OpenEntryData(string archivePath, IArchiveEntry entry) => entry.OpenEntryStream();

    /// <summary>
    /// 암호 없이 형식만 먼저 알아낸 뒤, 그 형식 전용 OpenArchive 로 연다.
    /// SharpCompress 0.50 은 암호를 준 채 ArchiveFactory 로 형식을 판별하면 SharpZipLib 로 만든(일집) AES ZIP 에서
    /// 'Cannot determine compressed stream type' 으로 실패한다(틀린 암호가 '암호 문제'로 안 보임).
    /// 형식을 모르면(SFX·ACE 등) 기존 ArchiveFactory 경로로 보내 원래 예외가 그대로 나게 한다.
    /// </summary>
    protected static IArchive OpenTyped(string path, ReaderOptions options)
    {
        if (!ArchiveFactory.IsArchive(path, out var type) || type is null)
            return ArchiveFactory.OpenArchive(path, options);
        return type.Value switch
        {
            ArchiveType.Zip => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(path, options),
            ArchiveType.SevenZip => SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(path, options),
            ArchiveType.Rar => SharpCompress.Archives.Rar.RarArchive.OpenArchive(path, options),
            ArchiveType.Tar => SharpCompress.Archives.Tar.TarArchive.OpenArchive(path, options),
            ArchiveType.GZip => SharpCompress.Archives.GZip.GZipArchive.OpenArchive(path, options),
            _ => ArchiveFactory.OpenArchive(path, options),
        };
    }

    private static ReaderOptions BuildReaderOptions(string? password) => new()
    {
        // ""(빈 암호)는 '암호 없음'과 같게 — 엔진에 빈 문자열을 넘기면 그것을 실제 암호로 쓰려 한다.
        Password = string.IsNullOrEmpty(password) ? null : password,
        ArchiveEncoding = new ArchiveEncoding
        {
            // CP437은 0x00~0xFF를 무손실 1:1 매핑 → CustomDecoder가 적용되지 않는
            // 경로에서도 원본 바이트가 보존되어 호출부의 KoreanFileNameDecoder.Decode로 복구 가능.
            Default = Encoding.GetEncoding(437),
            // 파일명 원본 바이트를 직접 받아 UTF-8/CP949를 자동 판별 (가장 견고).
            // 네 번째 인자(EncodingType: UTF-8 플래그 등)는 무시 — 플래그와 무관하게 'strict UTF-8 먼저' 판별(기존 동작).
            CustomDecoder = (bytes, index, count, _) =>
                KoreanFileNameDecoder.DecodeBytes(bytes, index, count)
        }
    };
}
