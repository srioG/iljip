using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Iljip.Models;
using SharpCompress.Archives;

namespace Iljip.Services;

/// <summary>
/// ZIP 포맷.
/// - 해제/목록: 베이스(SharpCompress)에서 처리. 한글 파일명 자동 디코딩 포함.
///   해제 전에 <see cref="ZipPasswordProbe"/> 로 암호를 판정한다(아무것도 만들기 전): 암호 없음·틀림은 바로 예외,
///   반디집의 CP949 한글 암호(B3)는 ZipCrypto 면 엔진에 암호 바이트를 그대로 넘기고, WinZip AES 항목은 <see cref="WinZipAes"/> 가 직접 푼다
///   (인증값 HMAC 까지 확인 — AE-2 는 CRC 칸이 0 이라 이것이 유일한 무결성 확인). 풀린 내용의 CRC-32 도 맞춰 본다(B4).
///   두 후보가 다 통과해 못 가리면 다른 후보로 다시 풀 계획을 함께 넘기고, 판정이 암호를 충분히 확인했으면(<see cref="ZipPasswordProbeResult.Confirmed"/>)
///   풀다가 나는 데이터 오류를 틀린 암호가 아니라 손상으로 알린다.
/// - 압축: SharpZipLib로 처리. AES-256(기본) 또는 ZipCrypto(호환, I2-11). ZipCrypto 암호는 CP949 로 바꿀 수 있으면 CP949 바이트로 쓴다
///   (한국어 Windows 반디집과 양방향 호환 실측). AES 암호는 SharpZipLib 가 늘 UTF-8 로 쓴다(바꿀 수 없음 — 한글이면 화면이 경고).
/// - 나눠 만들기(W2-03, <see cref="CompressionOptions.SplitBytes"/>): 보통 ZIP 을 임시 폴더에 다 만든 뒤 PKZIP 나눔(이름.z01 … 이름.zip)으로 바꾼다
///   (<see cref="SplitArchiveOutput"/>·<see cref="ZipSpanWriter"/>). 읽을 때 나눔 세트는 <see cref="SpannedZipStream"/> 로 보통 ZIP 처럼 연다(빠진 조각은 그 이름).
/// </summary>
public sealed class ZipArchiveService : SharpCompressArchiveServiceBase
{
    /// <summary>'.zip.001' = 숫자 분할 ZIP 의 첫 조각(W2-01, I2-2 해제 — 조각을 이어 붙여 보통 ZIP 처럼 연다).</summary>
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zip", ".zip.001" };

    /// <summary>
    /// ZIP 을 바이트로 읽는 스트림(암호 판정·WinZip AES 해제용): 숫자 분할 첫 조각('.zip.001')이면 조각을 이어 붙인 스트림
    /// (뒤 조각이 빠졌거나 덜 받아졌으면 그 이름으로 알림 — <see cref="SplitVolumeStream"/>),
    /// PKZIP 나눔(.z01 … .zip)의 마지막 조각이면 세트를 보통 ZIP 처럼 보이게 한 스트림(앞 조각이 없으면 그 이름으로 알림 — <see cref="SpannedZipStream"/>, W2-03),
    /// 아니면 파일 그대로.
    /// </summary>
    internal static Stream OpenRawStream(string zipPath)
    {
        if (!SplitVolumeStream.IsNumberedFirstVolume(zipPath))
        {
            if (SpannedZipStream.IsLastVolume(zipPath)) return SpannedZipStream.Open(zipPath);
            return new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        }
        var s = SplitVolumeStream.Open(zipPath);
        try
        {
            s.ThrowIfZipIncomplete();
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 숫자 분할 첫 조각·PKZIP 나눔의 마지막 조각이면 이어 붙인 스트림으로 연다(한글 이름 판별·암호 판정은 보통 ZIP 과 같음). 아니면 베이스 그대로.
    /// (W2-03 전에는 나눔 ZIP 을 SharpCompress 가 형제 조각을 찾아 열었다 — 목록이 마지막 조각을 넘으면 못 읽어 <see cref="SpannedZipStream"/> 로 바꿈)
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, SharpCompress.Readers.ReaderOptions options)
    {
        if (!SplitVolumeStream.IsNumberedFirstVolume(archivePath) && !SpannedZipStream.IsLastVolume(archivePath))
            return base.OpenForRead(archivePath, options);
        var s = OpenRawStream(archivePath);
        try
        {
            return SharpCompress.Archives.Zip.ZipArchive.OpenArchive(s, options with { LeaveStreamOpen = false });
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    protected override ExtractPlan PrepareExtract(string archivePath, string? password, Func<string, bool> shouldExtract)
    {
        // 나뉜 ZIP(.zip.001·.z01 … .zip)의 조각이 빠졌으면 아무것도 만들기 전에 그 이름으로 알린다 — 대상 폴더도 안 생긴다(W2-03, RAR·7z 와 같게).
        // (판정은 실패를 삼키므로 여기서 먼저 열어 본다. 나눔 ZIP 의 모양은 기억해 두어 판정·해제가 다시 만들지 않음)
        if (SplitVolumeStream.IsNumberedFirstVolume(archivePath) || SpannedZipStream.IsLastVolume(archivePath))
            OpenRawStream(archivePath).Dispose();

        var probe = ZipPasswordProbe.Probe(archivePath, password, shouldExtract);
        if (!probe.AnyEncrypted || probe.Match is PasswordBytes.Unknown or PasswordBytes.NotApplicable)
            return new ExtractPlan(BuildReaderOptions(password)) { VerifyCrc = true };   // 판정 못 함 → 엔진이 확인(+ CRC·번역·장부)

        if (string.IsNullOrEmpty(password)) throw new PasswordRequiredException();
        if (probe.Match == PasswordBytes.None || probe.MatchedPasswordBytes is not { } bytes) throw new WrongPasswordException();

        // 두 후보(UTF-8·CP949)가 모두 검사를 통과해 못 가렸으면, 먼저 쓴 후보로 데이터가 틀릴 때 다른 후보로 처음부터 다시 푼다(B3).
        var alternate = probe.AlternatePasswordBytes is { } alt
            ? PlanFor(archivePath, password, probe, probe.AlternateMatch, alt, alternate: null)
            : null;
        return PlanFor(archivePath, password, probe, probe.Match, bytes, alternate);
    }

    private static ExtractPlan PlanFor(
        string archivePath, string password, ZipPasswordProbeResult probe, PasswordBytes kind, byte[] bytes, ExtractPlan? alternate)
    {
        // 엔진에 암호 '문자열'을 그대로 줘도 되는가: UTF-8 로 맞았고, ASCII 이거나 ZipCrypto 항목이 없을 때(AES 는 엔진도 UTF-8 로 키를 만듦).
        // 아니면 맞은 '바이트'를 그대로 넘긴다 — CP949 는 물론, SharpCompress 는 ZipCrypto 암호를 글자 단위로 바꿔서
        // 이모지(서로게이트 쌍) 같은 UTF-8 암호도 문자열로는 안 맞는다(W1-C 실측).
        bool asIs = kind == PasswordBytes.Utf8 && (IsAscii(password) || !probe.HasZipCrypto);
        return new ExtractPlan(asIs ? BuildReaderOptions(password) : BuildReaderOptions(password, bytes))
        {
            VerifyCrc = true,
            PasswordConfirmed = probe.Confirmed,
            Alternate = alternate,
            OpenOverride = probe.HasAes ? AesOpener(archivePath, probe.Entries, bytes, engineFallback: asIs) : null,
        };
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
            if (c > 0x7F) return false;
        return true;
    }

    /// <summary>
    /// WinZip AES 항목을 <see cref="WinZipAes"/> 로 푸는 여닫이. 엔진 항목과 중앙 디렉터리 항목은 순번으로 짝짓고(크기·CRC·암호 여부로 확인),
    /// 어긋나면 이름으로 한 번 더 찾는다. 짝을 못 찾거나 안쪽 방식을 직접 못 풀면: 엔진에 암호 문자열을 그대로 준 경우(<paramref name="engineFallback"/>)는
    /// 엔진(SharpCompress)에 맡기고, 바이트로 넘긴 경우(CP949 등)는 엔진이 AES 키를 못 만들므로 '못 푸는 방식'으로 알린다.
    /// </summary>
    private static Func<int, IArchiveEntry, Stream?> AesOpener(
        string archivePath, IReadOnlyList<ZipCentralEntry> central, byte[] passwordBytes, bool engineFallback) => (index, entry) =>
    {
        if (!IsEncrypted(entry)) return null;
        var cd = FindCentral(central, index, entry);
        if (cd is { IsAes: true } && WinZipAes.CanDecompress(cd))
            return WinZipAes.OpenEntry(archivePath, cd, passwordBytes);
        if (cd is { IsAes: false } || engineFallback) return null;
        throw new UnsupportedMethodException(cd is null ? "한글 암호 AES" : $"한글 암호 AES + 방식 {cd.AesMethod}", entryPath: entry.Key);
    };

    private static ZipCentralEntry? FindCentral(IReadOnlyList<ZipCentralEntry> central, int index, IArchiveEntry entry)
    {
        bool Same(ZipCentralEntry c) => c.UncompressedSize == entry.Size && c.Crc == unchecked((uint)entry.Crc) && c.IsEncrypted;
        if (index < central.Count && Same(central[index])) return central[index];
        var byName = central.Where(c => c.Name == entry.Key && Same(c)).Take(2).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CompressionOptions.Default;
        return Task.Run(() =>
        {
            // 파일·빈 폴더·못 들어간 폴더를 한 번에 모은다(I0-2 — 하위 폴더 권한 오류로 전체가 실패하지 않게).
            // 빼기 목록(W2-07, I3-6)에 걸리는 것은 여기서 빠지고 결과의 ExcludedPaths 로 알린다.
            var sources = SourceCollector.Collect(sourcePaths, options.ExcludeFilter, cancellationToken);

            // 나눠 만들기(W2-03): 대상 폴더 안 숨김 임시 폴더에 보통 ZIP 을 다 만든 뒤 PKZIP 나눔(.z01 … .zip)으로 바꾼다(다시 압축하지 않음).
            // 원본을 다 모은 뒤에 임시 폴더를 만든다 — 원본 폴더 안에 저장해도 임시 폴더가 원본으로 담기지 않게. 저장 창에서 고른 이름은 계속 담지 않는다.
            if (options.SplitBytes is > 0 and long split)
                return SplitArchiveOutput.Write(archivePath, split, SplitArchiveOutput.VolumeKind.Zip, progress, cancellationToken,
                    (single, p) => RewriteWithoutUnreadable(single,
                        dropped => WriteZip(sources, dropped, single, options, p, cancellationToken, alsoSkip: archivePath)))
                    .WithExcluded(sources.Excluded);

            // 읽는 도중 막힌 원본이 있으면 그 파일을 빼고 처음부터 다시 쓴다(베이스 RewriteWithoutUnreadable)
            return RewriteWithoutUnreadable(archivePath,
                dropped => WriteZip(sources, dropped, archivePath, options, progress, cancellationToken))
                .WithExcluded(sources.Excluded);
        }, cancellationToken);
    }

    /// <summary>
    /// 이미 모은 원본(파일마다 정해 준 항목 이름)으로 ZIP 을 쓴다(W2-06 — 압축 고치기의 '넣기'가 넣을 파일만 담은 임시 ZIP 을 만들 때).
    /// 압축 고치기는 이 ZIP 의 압축된 데이터만 원래 압축으로 옮겨 담고 이름·암호는 그쪽에서 정하므로, 여기서는 암호 없이 수준만 맞춘다.
    /// 잠긴 원본 건너뛰기·읽는 도중 막힌 파일 빼고 다시 쓰기·하나도 못 담으면 <see cref="NothingToCompressException"/> 은 <see cref="CompressAsync"/> 와 같다.
    /// </summary>
    /// 수준이 '저장만'이면 방식도 저장(0)으로 쓴다(만들기의 '저장만'은 예전처럼 Deflate 수준 0 — 그대로 둠).
    internal static CompressResult WriteCollected(
        SourceSet sources, string archivePath, CompressionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
        => RewriteWithoutUnreadable(archivePath, dropped => WriteZip(sources, dropped, archivePath, options, progress, cancellationToken,
            storedMethod: options.Level == CompressionLevelOption.Store));

    /// <summary>
    /// ZIP 을 한 번 쓴다. <paramref name="dropped"/>(앞 회차에서 읽는 도중 막힌 파일)는 열지 않고 건너뜀으로 적는다.
    /// 담은 파일이 하나도 없고 건너뛴 것이 있으면 압축 파일을 만들지 않고 <see cref="NothingToCompressException"/>(I0-2).
    /// </summary>
    /// <param name="alsoSkip">이것도 담지 않는다(나눠 만들 때 저장 창에서 고른 이름 — 실제로 쓰는 곳은 임시 폴더, W2-03).</param>
    /// <param name="storedMethod">true 면 파일을 방식 0(저장)으로 쓴다(W2-06 — 저장만 ZIP 에 넣는 파일).</param>
    private static CompressResult WriteZip(
        SourceSet sources, IReadOnlyDictionary<string, string> dropped, string archivePath,
        CompressionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken, string? alsoSkip = null,
        bool storedMethod = false)
    {
        // 압축 파일을 원본 폴더 안에 저장하는 경우: 지금 쓰고 있는 자기 자신(나눠 만들 때는 저장 창에서 고른 이름도)은 담지 않는다 —
        // 못 열어 '건너뜀'으로 보이지 않게. 진행률의 총 파일 수·바이트에서도 미리 뺀다(W2-03 — 예전엔 총수에 들어가 끝에도 N-1/N 으로 보였다).
        var files = sources.Files
            .Where(f => !IsSameFile(f.SourceFile, archivePath) && (alsoSkip is null || !IsSameFile(f.SourceFile, alsoSkip)))
            .ToList();
        var skipped = new List<SkippedSource>(sources.Skipped);
        long totalBytes = SumFileSizes(files);
        int totalFiles = files.Count;
        long bytesProcessed = 0;
        int filesProcessed = 0;

        // 암호 방식(I2-11): AES-256(기본, 안전) 또는 ZipCrypto(호환 — 탐색기·오래된 프로그램).
        bool zipCrypto = options.HasPassword && options.Encryption == ZipEncryption.ZipCrypto;

        // 압축 파일은 담을 첫 파일을 연 뒤에 만든다 — 모두 못 읽으면 빈 zip 을 남기지 않게(덮어쓰려던 기존 파일도 그대로)
        using var output = new LazyArchiveOutput<ZipOutputStream>(archivePath, fs =>
        {
            var zip = new ZipOutputStream(fs, ZipCryptoCodec(zipCrypto ? options.Password : null))
            {
                IsStreamOwner = false,
                UseZip64 = UseZip64.Dynamic
            };
            zip.SetLevel(ToSharpZipLevel(options.Level));
            if (options.HasPassword)
            {
                zip.Password = options.Password;
            }
            return zip;
        });

        var buffer = new byte[81920];
        foreach (var (sourceFile, entryName) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 앞 회차에서 읽는 도중 막혔던 파일 — 다시 열지 않고 건너뜀
            if (dropped.TryGetValue(sourceFile, out var droppedReason))
            {
                skipped.Add(new SkippedSource(sourceFile, droppedReason));
                bytesProcessed += SafeLength(sourceFile);
                continue;
            }

            // 원본을 '먼저' 연다(I0-2): 다른 프로그램이 쓰는 중이어도 공유를 허락했으면 읽고, 아예 막았으면 이 파일만 건너뛴다.
            // 항목 머리(PutNextEntry)를 쓰기 전에 열어야(첫 블록까지 읽어 봄) 못 연 파일이 빈 항목으로 남지 않는다.
            if (!SourceFileOpener.TryOpenRead(sourceFile, out var opened, out var reason))
            {
                skipped.Add(new SkippedSource(sourceFile, reason!));
                bytesProcessed += SafeLength(sourceFile);   // 진행률이 건너뛴 만큼 뒤처지지 않게
                continue;
            }

            using var input = opened!;
            var zipStream = output.Writer;
            var entry = new ZipEntry(entryName)
            {
                DateTime = File.GetLastWriteTime(sourceFile),
                // Size는 명시하지 않는다. FileInfo.Length로 미리 박아두면, 폴더 압축 도중
                //   쓰기 중인 파일(로그/DB 등)의 길이가 스트리밍 사이에 바뀔 때 실제 기록 바이트와
                //   불일치해 CloseEntry가 'size was incorrect' 예외를 던지고 압축 전체가 깨진다.
                //   생략하면 SharpZipLib이 CloseEntry에서 실제 기록 길이로 헤더를 채운다.
                // 파일명을 UTF-8 + EFS(General Purpose Bit 11) 플래그로 기록.
                // 한글 파일명이 일집/탐색기/macOS 등 어디서든 올바르게 보이게 함.
                IsUnicodeText = true,
                // AES-256 (WinZip 호환). 0 이면 암호가 있을 때 ZipCrypto. 암호 없으면 무시됨.
                AESKeySize = options.HasPassword && !zipCrypto ? 256 : 0
            };
            if (storedMethod) entry.CompressionMethod = CompressionMethod.Stored;

            zipStream.PutNextEntry(entry);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                BytesProcessed = bytesProcessed,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });

            int read;
            // 읽는 도중 막히면(뒤쪽 바이트 범위 잠금) SourceUnreadableException → 베이스가 이 파일을 빼고 처음부터 다시 쓴다
            while ((read = SourceFileOpener.Read(input, buffer, sourceFile)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zipStream.Write(buffer, 0, read);
                bytesProcessed += read;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });
            }

            zipStream.CloseEntry();
            filesProcessed++;
        }

        // 담은 파일이 하나도 없는데 건너뛴 것이 있다 = 사용자가 고른 것을 하나도 못 담음 → 빈(또는 빈 폴더뿐인) 압축을 만들지 않는다
        if (filesProcessed == 0 && skipped.Count > 0)
            throw new NothingToCompressException(skipped);

        // 빈 디렉터리 보존: 파일이 하나도 없는 폴더는 위 루프에서 누락되므로
        // 끝에 '/'를 붙인 디렉터리 엔트리로 별도 기록한다(SharpZipLib이 디렉터리로 인식).
        foreach (var (_, dirEntry) in sources.EmptyDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Writer.PutNextEntry(new ZipEntry(dirEntry)
            {
                DateTime = DateTime.Now,
                IsUnicodeText = true
            });
            output.Writer.CloseEntry();
        }

        // 담을 것이 원래 없었으면(예: 원본 폴더 안의 압축 파일 자신뿐) 예전처럼 빈 zip 을 만든다
        output.Writer.Finish();
        output.Complete();

        progress?.Report(new ArchiveProgress
        {
            CurrentFile = string.Empty,
            BytesProcessed = totalBytes,
            TotalBytes = totalBytes,
            FilesProcessed = filesProcessed,
            TotalFiles = totalFiles
        });
        return new CompressResult(skipped);
    }

    /// <summary>
    /// ZipCrypto 암호를 쓸 바이트 인코딩. CP949 로 바꿀 수 있는 암호면 CP949(한국어 Windows 의 반디집·알집이 쓰는 방식 — 반디집 7.46
    /// bz.exe 로 양방향 실측), 이모지처럼 못 바꾸는 글자가 있으면 UTF-8(화면이 '다른 프로그램에서 안 풀릴 수 있어요' 경고 후 고른 경우).
    /// 이름은 항목마다 IsUnicodeText(UTF-8 + EFS 플래그)라 이 코덱의 이름 인코딩은 쓰이지 않는다.
    /// </summary>
    private static StringCodec ZipCryptoCodec(string? zipCryptoPassword)
    {
        bool cp949 = zipCryptoPassword is not null && ZipPasswordProbe.TryCp949Bytes(zipCryptoPassword) is not null;
        return StringCodec.Default.WithZipCryptoEncoding(cp949 ? Encoding.GetEncoding(949) : Encoding.UTF8);
    }

    private static int ToSharpZipLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Store => 0,
        CompressionLevelOption.Fastest => 1,
        CompressionLevelOption.Normal => 6,
        CompressionLevelOption.Maximum => 9,
        _ => 6
    };
}
