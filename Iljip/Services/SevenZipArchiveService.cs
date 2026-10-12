using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// 7Z 포맷.
/// SharpCompress는 7Z **해제만** 지원하고 압축 생성은 지원하지 않는다.
/// 해제는 베이스의 ArchiveFactory.Open으로 자동 처리.
/// 7z 는 암호 확인값이 없어 틀린 암호가 'Data Error' 로만 드러난다 — 베이스가 암호 항목의 데이터 오류를 틀린 암호로 바꾸고(B5),
/// 여기서는 파일 이름까지 암호가 걸린(헤더 암호) 7z 를 열 때의 오류를 암호 예외로 바꾼다.
/// </summary>
public sealed class SevenZipArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".7z" };

    /// <summary>
    /// 열기·목록 단계(항목 없음)에서 암호 오류·데이터 오류가 나면 헤더 암호인지 확인해(암호 없이 다시 열어 보면 '암호 없음'이 남)
    /// 헤더 암호면 암호 없음 → <see cref="PasswordRequiredException"/>, 틀린 암호 → <see cref="WrongPasswordException"/>(손상과 구분 안 됨 문구).
    /// </summary>
    protected override Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
    {
        if (entry is null && ex is not ArchiveException and not OperationCanceledException
            && (ex is SharpCompress.Common.CryptographicException || EngineErrorTranslator.IsDataError(ex))
            && IsHeaderEncrypted(archivePath))
        {
            return string.IsNullOrEmpty(password)
                ? new PasswordRequiredException(isHeaderEncrypted: true, inner: ex)
                : new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
        }
        return base.TranslateError(ex, archivePath, password, entry, entryPath);
    }

    /// <summary>암호 없이 머리를 읽어 보면 SharpCompress 가 'Encrypted 7Zip archive has no password specified' 로 멈추는가(실패 경로에서만 부름).</summary>
    private static bool IsHeaderEncrypted(string archivePath)
    {
        try
        {
            using var archive = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(archivePath, new ReaderOptions());
            foreach (var _ in archive.Entries) { }
            return false;
        }
        catch (SharpCompress.Common.CryptographicException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "7Z 압축 생성은 현재 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP이나 TAR을 사용해주세요.");
    }
}
