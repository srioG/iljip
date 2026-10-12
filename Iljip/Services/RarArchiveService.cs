using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// RAR 포맷. SharpCompress는 RAR 4/5 해제만 지원 (RAR 압축은 라이선스상 미지원).
/// 해제와 항목 목록 읽기는 베이스의 ArchiveFactory.Open으로 자동 처리됨.
/// 암호: RAR5 는 엔진이 암호 확인값으로 틀린 암호를 바로 알아본다. RAR4(RAR 3.x AES)는 확인값이 없고 엔진이 암호 항목의 CRC 를
/// 보지 않아, 일집이 풀린 내용의 CRC 를 맞춰 본다(<see cref="PrepareExtract"/>). 파일 이름까지 암호가 걸린 RAR(머리 암호)은
/// 7z 헤더 암호와 같은 문구·같은 화면 길로 보낸다(<see cref="TranslateError"/>). — 통합 점검 2026-10-12, 합성 RAR4·RAR5 로 확인.
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

    /// <summary>
    /// RAR4 는 풀린 내용의 CRC-32 를 일집이 맞춰 본다. SharpCompress 는 암호 항목의 CRC 를 보지 않아(암호 없는 항목은 엔진이 봄),
    /// RAR4 를 틀린 암호로 풀면 쓰레기 파일이 '완료'로 남았다(RAR4 는 암호 확인값이 없어 틀린 암호가 데이터에서야 드러남).
    /// 맞지 않으면 베이스가 '암호가 맞지 않아요(손상일 수도)'로 알리고 이번에 만든 것을 모두 지운다.
    /// RAR5 는 하지 않는다 — 암호 확인값이 따로 있고, WinRAR 은 암호 항목의 CRC 를 암호에 묶인 값(HMAC)으로 바꿔 적어 평범한 CRC 와 다르다.
    /// </summary>
    protected override ExtractPlan PrepareExtract(string archivePath, string? password, Func<string, bool> shouldExtract) =>
        new(BuildReaderOptions(password)) { VerifyCrc = IsRar4(archivePath) };

    /// <summary>
    /// 열기·목록 단계에서 암호 문제로 멈췄는데, 암호 없이 읽으면 '암호 없음'인 RAR(머리 암호 — WinRAR '파일 이름 암호화')이면
    /// 7z 헤더 암호와 같은 예외(문구 '파일 이름까지 암호가 걸려 있어요…')로 바꾼다. RAR4 머리 암호는 틀린 암호가
    /// 'Unknown Rar Header'·'rar header crc mismatch' 같은 형식 오류로만 드러나 예전엔 영어 오류 창이 뜨고 암호를 다시 묻지 않았다.
    /// 암호 없는 항목의 CRC 불일치(엔진 'file crc mismatch')는 ZIP 과 같은 <see cref="ChecksumMismatchException"/>(쉬운 문구).
    /// </summary>
    protected override Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
    {
        if (entry is null && ex is not ArchiveException and not OperationCanceledException
            && (ex is SharpCompress.Common.CryptographicException || EngineErrorTranslator.IsDataError(ex))
            && NeedsPasswordToList(archivePath))
        {
            return string.IsNullOrEmpty(password)
                ? new PasswordRequiredException(isHeaderEncrypted: true, inner: ex)
                : new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
        }
        var translated = base.TranslateError(ex, archivePath, password, entry, entryPath);
        if (translated is null && entry is not null && ex is SharpCompress.Common.SharpCompressException
            && ex.Message.StartsWith("file crc mismatch", StringComparison.OrdinalIgnoreCase))
            return new ChecksumMismatchException(entryPath ?? entry.Key ?? string.Empty, ex);
        return translated;
    }

    /// <summary>RAR4(RAR 1.5~4.x 형식 — 표시 'Rar!\x1A\x07\x00')인가. RAR5 는 'Rar!\x1A\x07\x01\x00'.</summary>
    private static bool IsRar4(string archivePath)
    {
        try
        {
            using var fs = File.OpenRead(archivePath);
            Span<byte> sig = stackalloc byte[7];
            return fs.Read(sig) == 7 && sig.SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 });
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
            "RAR 압축은 라이선스 제한으로 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }
}
