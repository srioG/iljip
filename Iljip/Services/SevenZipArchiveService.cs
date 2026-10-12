using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 7Z 포맷.
/// W2-01: 목록·해제는 7z.dll(<see cref="SevenZipEngine"/>). 파일 이름까지 암호가 걸린(헤더 암호) 7z 는 열기 전에 머리를 읽어 알아내
/// 암호를 묻게 하고(I1-7·I0-1 — 우클릭 '여기에 풀기'도 같은 길), 틀린 암호를 '암호 문제'로 알린다(B5).
/// '.7z.001' 숫자 분할은 조각을 이어 붙여 연다(<see cref="SplitVolumeStream"/> — 빠진 조각·덜 받아진 조각을 이름으로 알림, I2-2 해제).
/// 7z.dll 을 못 쓰는 PC 에서는 SharpCompress 로 대신 연다(아래 — main 의 구현 그대로).
/// 압축(생성)은 아직 지원하지 않는다(7z 만들기는 W2-02 몫).
/// SharpCompress 대체 경로: 7z 는 암호 확인값이 없어 틀린 암호가 'Data Error' 로만 드러난다 — 베이스가 암호 항목의 데이터 오류를 틀린 암호로 바꾸고(B5),
/// 여기서는 헤더 암호 7z 를 열 때의 오류를 암호 예외로 바꾼다.
/// </summary>
public sealed class SevenZipArchiveService : SevenZipEngineServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".7z", ".7z.001" };

    internal override SevenZipEngine.Source? CreateSource(string archivePath)
    {
        if (!SevenZipLibrary.IsAvailable) return null;
        Func<Stream> open = SplitVolumeStream.IsNumberedFirstVolume(archivePath)
            ? () => OpenSplit(archivePath)
            : () => OpenRead(archivePath);
        bool headerEncrypted;
        using (var probe = open())
            headerEncrypted = ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(probe);
        return new SevenZipEngine.Source
        {
            ArchivePath = archivePath,
            Format = InArchiveFormat.SevenZip,
            OpenStream = open,
            HeaderEncrypted = headerEncrypted,
            // 7z 는 잘린 모든 지점에서 7z.dll 열기가 실패한다(실측 179/179) — 따로 구조 검사를 하지 않는다
            Structure = StructureKind.None,
        };
    }

    /// <summary>숫자 분할 조각을 이어 붙이고, 뒤 조각이 빠졌거나 덜 받아졌으면 그 이름으로 알린다.</summary>
    private static SplitVolumeStream OpenSplit(string firstVolume)
    {
        var s = SplitVolumeStream.Open(firstVolume);
        try
        {
            s.ThrowIfSevenZipIncomplete();
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>7z.dll 을 못 쓸 때(SharpCompress): 숫자 분할도 이어 붙인 스트림으로 연다.</summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        if (!SplitVolumeStream.IsNumberedFirstVolume(archivePath)) return base.OpenForRead(archivePath, options);
        var s = OpenSplit(archivePath);
        try
        {
            return SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(s, options with { LeaveStreamOpen = false });
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// (7z.dll 을 못 쓸 때) 열기·목록 단계(항목 없음)에서 암호 오류·데이터 오류가 나면 헤더 암호인지 확인해(암호 없이 다시 열어 보면 '암호 없음'이 남)
    /// 헤더 암호면 암호 없음 → <see cref="PasswordRequiredException"/>, 틀린 암호 → <see cref="WrongPasswordException"/>(손상과 구분 안 됨 문구).
    /// </summary>
    protected override Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
    {
        if (entry is null && ex is not ArchiveException and not OperationCanceledException
            && (ex is SharpCompress.Common.CryptographicException || EngineErrorTranslator.IsDataError(ex))
            && NeedsPasswordToList(archivePath))   // 암호 없이 머리를 읽으면 'Encrypted 7Zip archive has no password specified'
        {
            return string.IsNullOrEmpty(password)
                ? new PasswordRequiredException(isHeaderEncrypted: true, inner: ex)
                : new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
        }
        return base.TranslateError(ex, archivePath, password, entry, entryPath);
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
