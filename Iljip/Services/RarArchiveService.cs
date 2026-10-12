using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// RAR 포맷(해제만 — RAR 압축은 라이선스상 불가).
/// W2-01: 7z.dll(RAR4·RAR5, 헤더 암호, 여러 조각 '.partN.rar'·'.rNN')로 연다. 헤더 암호 RAR 은 머리를 직접 읽어 알아내 암호를 묻게 하고(I1-7),
/// 끝이 잘렸는지는 머리를 따라가 본다(<see cref="ArchiveCompleteness"/> — RAR4 ENDARC·RAR5 끝 머리). 빠진 조각은 그 이름으로 알린다.
/// 7z.dll 을 못 쓰는 PC 에서는 SharpCompress 로 대신 연다(아래 — main 의 구현 그대로, 통합 점검의 RAR 암호 흐름 포함).
/// SharpCompress 대체 경로의 암호: RAR5 는 엔진이 암호 확인값으로 틀린 암호를 바로 알아본다. RAR4(RAR 3.x AES)는 확인값이 없고 엔진이 암호 항목의 CRC 를
/// 보지 않아, 일집이 풀린 내용의 CRC 를 맞춰 본다(<see cref="PrepareExtract"/>). 파일 이름까지 암호가 걸린 RAR(머리 암호)은
/// 7z 헤더 암호와 같은 문구·같은 화면 길로 보낸다(<see cref="TranslateError"/>). — 통합 점검 2026-10-12, 합성 RAR4·RAR5 로 확인.
/// ★ 실물 RAR 픽스처가 없다(반디집은 RAR 를 못 만듦) — 실제 RAR 파일 확인은 MANUAL_CHECKLIST.
/// </summary>
public sealed class RarArchiveService : SevenZipEngineServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".rar" };

    private static readonly System.Text.RegularExpressions.Regex PartName =
        new(@"^(?<base>.*\.part)(?<n>\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>여러 조각 RAR 인가(주 머리의 볼륨 표시, 또는 이름 '.partN.rar' — 헤더 암호 RAR5 는 주 머리까지 암호라 이름으로도 본다).</summary>
    private static bool IsMultiVolume(string archivePath, ArchiveHeaderProbe.RarInfo info) =>
        info.IsVolume || PartName.IsMatch(Path.GetFileName(archivePath));

    internal override SevenZipEngine.Source? CreateSource(string archivePath)
    {
        if (!SevenZipLibrary.IsAvailable) return null;
        var info = ArchiveHeaderProbe.ProbeRar(archivePath);
        bool multiVolume = IsMultiVolume(archivePath, info);
        bool rar4 = info.IsRar && !info.IsRar5;
        return new SevenZipEngine.Source
        {
            ArchivePath = archivePath,
            // 서명이 RAR 가 아니면(손상) RAR5 로 시도 → 7z.dll 이 열지 못해 '손상'으로 알림
            Format = rar4 ? InArchiveFormat.Rar4 : InArchiveFormat.Rar,
            // 여러 조각은 경로로 연다 — 7z.dll 이 다음 조각(.part2.rar·.r00)을 스스로 찾는다(취소는 파일 단위).
            OpenStream = multiVolume ? null : () => OpenRead(archivePath),
            HeaderEncrypted = info.HeaderEncrypted,
            Structure = StructureKind.Rar,
            VolumeCheck = multiVolume ? () => ArchiveCompleteness.CheckRarVolumes(archivePath) : null,
            // RAR4 의 비유니코드 이름(CP949 등)은 다른 형식과 같은 한글 판별로 덮어쓴다(7z.dll 은 OEM 코드 페이지로 읽음).
            NameResolver = rar4 && !multiVolume && !info.HeaderEncrypted
                ? data => NamesFromSharpCompress(() => SharpCompress.Archives.Rar.RarArchive.OpenArchive(archivePath, NameReaderOptions()), data)
                : null,
        };
    }

    /// <summary>(7z.dll 을 못 쓸 때) 끝이 잘린 RAR·빠진 조각을 SharpCompress 로 열기 전에 알린다 — SharpCompress 는 머리 사이에서 잘린 RAR 을 조용히 푼다.</summary>
    protected override ArchiveException? FallbackStructureProblem(string archivePath)
    {
        var info = ArchiveHeaderProbe.ProbeRar(archivePath);
        if (IsMultiVolume(archivePath, info)) return ArchiveCompleteness.CheckRarVolumes(archivePath);
        try
        {
            using var fs = OpenRead(archivePath);
            return ArchiveCompleteness.Check(fs, StructureKind.Rar);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;   // 원본을 못 여는 문제는 아래 열기가 그대로 알린다
        }
    }

    /// <summary>
    /// (7z.dll 을 못 쓸 때) 항목이 하나도 없는 RAR 은 손상으로 알린다. SharpCompress 0.50 은 RAR5 서명 뒤가 쓰레기인 파일을 오류 없이 '항목 0개'로 열어
    /// 화면이 '0개 항목' → '압축 해제 완료'(아무것도 안 풀림)로 끝났다(0.38 은 'malformed vint' 오류).
    /// WinRAR 은 빈 RAR 을 만들지 않으므로(파일을 다 지우면 압축 파일도 지움) 정상 파일을 막을 일은 없다.
    /// 헤더 암호 RAR 을 암호 없이 열면 여기서 나는 암호 예외를 그대로 전한다(화면이 암호를 묻도록).
    /// 여러 조각('이름.partN.rar', W2-01): 고른 조각부터 뒤로 이어지는 조각을 직접 모아 넘긴다 — SharpCompress 는 둘째 조각을 열면 그 조각을
    /// 두 번 읽어 같은 파일이 두 번 나왔다(1차 W1-B 검증 지적).
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        var archive = OpenVolumes(archivePath, options) ?? base.OpenForRead(archivePath, options);
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

    /// <summary>(7z.dll 을 못 쓸 때) '이름.partN.rar' 이면 N 번부터 이어지는 조각으로 연다. 여러 조각 이름이 아니면 null.</summary>
    private static IArchive? OpenVolumes(string archivePath, ReaderOptions options)
    {
        string dir = Path.GetDirectoryName(archivePath) ?? string.Empty;
        var m = PartName.Match(Path.GetFileName(archivePath));
        if (!m.Success || !int.TryParse(m.Groups["n"].Value, out int first)) return null;
        int digits = m.Groups["n"].Value.Length;
        var volumes = new List<FileInfo>();
        for (int n = first; n < first + 10000; n++)
        {
            var fi = new FileInfo(Path.Combine(dir, $"{m.Groups["base"].Value}{n.ToString().PadLeft(digits, '0')}.rar"));
            if (!fi.Exists) break;
            volumes.Add(fi);
        }
        // 고른 조각이 마지막 하나뿐이어도 목록으로 넘긴다 — 경로로 열면 SharpCompress 가 그 조각을 두 번 읽는다
        return volumes.Count > 0 ? SharpCompress.Archives.Rar.RarArchive.OpenArchive(volumes, options) : null;
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
