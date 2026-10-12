using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// RAR 포맷. SharpCompress는 RAR 4/5 해제만 지원 (RAR 압축은 라이선스상 미지원).
/// 해제와 항목 목록 읽기는 베이스의 ArchiveFactory.Open으로 자동 처리됨.
/// </summary>
public sealed class RarArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".rar" };

    /// <summary>
    /// 항목이 하나도 없는 RAR 은 손상으로 알린다. SharpCompress 0.50 은 RAR5 서명 뒤가 쓰레기인 파일을 오류 없이 '항목 0개'로 열어
    /// 화면이 '0개 항목' → '압축 해제 완료'(아무것도 안 풀림)로 끝났다(0.38 은 'malformed vint' 오류).
    /// WinRAR 은 빈 RAR 을 만들지 않으므로(파일을 다 지우면 압축 파일도 지움) 정상 파일을 막을 일은 없다.
    /// 헤더 암호 RAR 을 암호 없이 열면 여기서 나는 암호 예외를 그대로 전한다(화면이 암호를 묻도록).
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        var archive = base.OpenForRead(archivePath, options);
        bool hasEntries;
        try
        {
            hasEntries = archive.Entries.Any();
        }
        catch
        {
            archive.Dispose();
            throw;
        }
        if (hasEntries) return archive;
        archive.Dispose();
        throw new InvalidDataException("이 RAR 파일은 손상되었거나 들어 있는 파일이 없어요.");
    }

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "RAR 압축은 라이선스 제한으로 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }
}
