using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 목록·해제를 7z.dll(<see cref="SevenZipEngine"/>)로 하고, 7z.dll 을 못 쓰는 PC(보안 정책이 막는 등)에서는
/// SharpCompress(베이스 — main 의 구현 그대로: 암호 흐름·CRC 검사·장부)로 대신하는 서비스의 공통 부분(7z·RAR, W2-01).
/// 파생 클래스는 <see cref="CreateSource"/> 에서 7z.dll 에 넘길 입력을 정하고, null 이면 SharpCompress 로 간다.
/// </summary>
public abstract class SevenZipEngineServiceBase : SharpCompressArchiveServiceBase
{
    /// <summary>7z.dll 로 열 입력. null 이면 SharpCompress(베이스)로 대신한다. 작업 스레드에서 불린다(파일을 읽어도 됨).</summary>
    internal abstract SevenZipEngine.Source? CreateSource(string archivePath);

    /// <summary>
    /// (7z.dll 을 못 쓸 때) SharpCompress 로 열기 전에 볼 구조 문제 — 끝 잘림·조각 없음(<see cref="ArchiveCompleteness"/>). 기본은 없음(null).
    /// SharpCompress 는 머리 사이에서 잘린 RAR 을 읽은 데까지만 풀고 오류를 내지 않아 같은 검사를 먼저 한다.
    /// </summary>
    protected virtual ArchiveException? FallbackStructureProblem(string archivePath) => null;

    public override Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var src = CreateSource(archivePath);
            if (src is not null) return SevenZipEngine.List(src, password, cancellationToken);
            if (FallbackStructureProblem(archivePath) is { } problem) throw problem;
            return ListCore(archivePath, password, cancellationToken);
        }, cancellationToken);
    }

    public override Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(archivePath, destinationFolder, _ => true, password, progress, cancellationToken);

    public override Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(archivePath, destinationFolder, EntrySelector(entryPaths), password, progress, cancellationToken);

    private Task Extract(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var src = CreateSource(archivePath);
            if (src is not null)
            {
                SevenZipEngine.Extract(src, destinationFolder, shouldExtract, password, progress, cancellationToken);
                return;
            }
            // 7z.dll 을 못 씀: 잘린 압축이면 아무것도 쓰기 전에 알리고, 아니면 main 의 SharpCompress 경로 그대로
            if (FallbackStructureProblem(archivePath) is { } problem) throw problem;
            ExtractCoreSync(archivePath, destinationFolder, shouldExtract, password, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// SharpCompress 로 같은 압축을 열어 항목 이름(일집의 한글 판별 — <see cref="SharpCompressArchiveServiceBase.BuildReaderOptions"/>)을 순서대로 얻고,
    /// 7z.dll 목록과 순서·폴더 여부·크기·CRC 가 모두 맞을 때만 (index → 이름)을 돌려준다(하나라도 어긋나면 null — 7z.dll 이름 그대로).
    /// 7z.dll 은 이름을 OEM 코드 페이지(한국어 Windows 는 949)로 읽어, 시스템 지역이 다른 PC 에서는 한글 이름이 깨진다 — ZIPX·RAR4 의
    /// 날 이름 바이트를 다른 형식과 같은 규칙으로 읽게 하려는 것(W2-01 설계 §10). W2-09: 이름 방식은 압축 파일 하나에 하나로 정한다
    /// (<see cref="SharpCompressArchiveServiceBase.OpenWithNameDetection"/> — Shift-JIS·GBK 이름의 ZIPX·RAR4 도).
    /// </summary>
    internal static IReadOnlyDictionary<int, string>? NamesFromSharpCompress(Func<SharpCompress.Readers.ReaderOptions, IArchive> open, IReadOnlyList<ArchiveFileInfo> data)
    {
        try
        {
            var opened = OpenWithNameDetection(open, NameReaderOptions());
            using var archive = opened.Archive;
            var entries = opened.Entries;
            if (entries.Count != data.Count) return null;
            var map = new Dictionary<int, string>(data.Count);
            for (int i = 0; i < data.Count; i++)
            {
                var e = entries[i];
                var d = data[i];
                if (e.IsDirectory != d.IsDirectory) return null;
                if (!e.IsDirectory && ((ulong)e.Size != d.Size || unchecked((uint)e.Crc) != d.Crc)) return null;
                string key = e.Key ?? string.Empty;
                if (key.Length == 0) return null;
                map[d.Index] = key;
            }
            return map;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>SharpCompress 로 이름만 읽을 때 쓸 옵션(암호 없음 — 목록 머리는 암호가 없어도 읽힌다).</summary>
    internal static SharpCompress.Readers.ReaderOptions NameReaderOptions() => BuildReaderOptions(null);

    /// <summary>파일 스트림(FileShare.Read) 여는 공용 도우미.</summary>
    internal static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
}
