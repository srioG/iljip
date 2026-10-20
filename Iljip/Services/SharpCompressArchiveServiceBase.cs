using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
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
public abstract class SharpCompressArchiveServiceBase : IArchiveService, IArchiveTestable
{
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ListCore(archivePath, password, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 목록 읽기 본체(동기). 7z.dll 을 못 쓰는 PC 에서 SharpCompress 로 대신 여는 서비스(<see cref="SevenZipEngineServiceBase"/>)도 부른다(W2-01).
    /// </summary>
    protected IReadOnlyList<ArchiveEntry> ListCore(string archivePath, string? password, CancellationToken cancellationToken)
    {
        try
        {
            var options = BuildReaderOptions(password);
            var opened = OpenNamed(archivePath, options);
            using var archive = opened.Archive;

            var entries = new List<ArchiveEntry>();
            foreach (var e in opened.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // e.Key는 ReaderOptions.ArchiveEncoding.CustomDecoder(=이름 방식 판정 — OpenNamed)로 이미
                // 올바르게 디코드된 상태다. 여기서 KoreanFileNameDecoder.Decode를 또 적용하면
                // (이중 디코드) 비한글 비ASCII 파일명이 CP437→CP949 오재해석으로 손상될 수 있어
                // 다시 디코드하지 않는다. 화면 이름은 NFC(맥 NFD 이름 — W2-09, I2-10).
                string decoded = EntryNames.Nfc(MapEntryKey(archivePath, e.Key));

                entries.Add(new ArchiveEntry
                {
                    Path = decoded,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    IsDirectory = e.IsDirectory,
                    LastModified = AsLocalTime(e.LastModifiedTime),
                    IsEncrypted = !e.IsDirectory && IsEncrypted(e),   // W2-04: 끌어내기·미리 보기 전 암호 확인
                });
            }
            return entries;
        }
        catch (Exception ex) when (TranslateError(ex, archivePath, password, null, null) is { } translated)
        {
            // 헤더 암호 7z 의 '암호 없음'·'틀린 암호'를 화면이 타입으로 알아보게(B5)
            throw translated;
        }
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
        => ExtractCore(archivePath, destinationFolder, EntrySelector(entryPaths), password, progress, cancellationToken);

    /// <summary>
    /// 선택 해제의 '고른 항목인가' 판정. 선택자 정규화: 파일은 정확히 일치, 폴더는 그 하위 전체를 포함
    /// ('\'·끝 '/'·앞 '/'·대소문자 차이는 같은 것으로 봄). 7z.dll·압축된 tar·자동 풀림 exe 서비스도 같은 규칙을 쓴다(W2-01).
    /// 앞 '/' 도 뗀다 — '/abs.txt' 같은 항목을 고르면 아무 알림 없이 빠지던 것. 고른 뒤 경로 공격 검사가 막는다.
    /// W2-09(I2-10): 양쪽을 NFC 로 맞춰 비교한다 — 목록의 완성형 이름으로 골라도, 압축에 적힌 자모 분리(NFD) 이름으로 골라도 같은 항목.
    /// </summary>
    internal static Func<string, bool> EntrySelector(IReadOnlyCollection<string> entryPaths)
    {
        var selectors = SelectorSet(entryPaths);
        return decodedPath => MatchesSelector(EntryNames.SelectorKey(decodedPath), selectors);
    }

    /// <summary>고른 열쇠 모음(<see cref="EntryNames.SelectorKey"/> 로 맞춘 것, 대소문자 무시 — 빈 열쇠는 뺌).</summary>
    internal static HashSet<string> SelectorSet(IEnumerable<string> entryPaths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in entryPaths)
        {
            string k = EntryNames.SelectorKey(p);
            if (k.Length > 0) set.Add(k);
        }
        return set;
    }

    /// <summary>
    /// 맞춘 항목 이름 <paramref name="d"/> 가 고른 것 자체이거나 고른 폴더 아래인가(그 조상 — '/' 앞까지 — 가 고른 것). 결과는 예전의
    /// '고른 열쇠마다 같음·「열쇠/」로 시작함을 비교'와 같고, 걸리는 시간은 (항목 수 × 고른 수)가 아니라 (항목 수 × 깊이)다 —
    /// Ctrl+C·끌기로 수천 개를 고르면 예전 비교가 수천만 번 돌아 꺼내기 전에 오래 멈췄다(IJ3-01).
    /// </summary>
    internal static bool MatchesSelector(string d, IReadOnlySet<string> selectors)
    {
        if (selectors.Count == 0 || d.Length == 0) return false;
        if (selectors.Contains(d)) return true;
        for (int i = d.IndexOf('/'); i >= 0; i = d.IndexOf('/', i + 1))
            if (i > 0 && selectors.Contains(d[..i])) return true;
        return false;
    }

    /// <summary>
    /// 해제 한 번의 준비물(<see cref="PrepareExtract"/> 가 만듦). 서비스 객체는 여러 해제가 같이 쓰므로 호출마다 새로 만든다.
    /// </summary>
    protected sealed class ExtractPlan
    {
        public ExtractPlan(ReaderOptions options) => Options = options;

        /// <summary>엔진으로 열 때 쓸 옵션(암호·이름 판별).</summary>
        public ReaderOptions Options { get; }

        /// <summary>
        /// (항목 순번, 항목) → 풀린 내용 스트림. null 을 돌려주면(또는 이 값이 null 이면) 기본 <see cref="OpenEntryData"/>.
        /// ZIP 의 WinZip AES 항목을 일집이 직접 풀 때 쓴다(<see cref="ZipArchiveService"/>).
        /// </summary>
        public Func<int, IArchiveEntry, Stream?>? OpenOverride { get; init; }

        /// <summary>풀린 내용의 CRC-32 를 항목에 적힌 값과 맞춰 본다(B4 — ZIP 만, SharpCompress 가 보지 않음).</summary>
        public bool VerifyCrc { get; init; }

        /// <summary>
        /// 풀기 전에 암호를 충분히 확인했다(ZIP 판정 — <see cref="ZipPasswordProbeResult.Confirmed"/>). 그러면 암호 항목의 데이터·CRC·인증값 오류는
        /// 틀린 암호가 아니라 손상으로 알린다 — 화면이 암호를 다시 묻지 않고, 앞서 맞게 풀린 파일은 남는다.
        /// </summary>
        public bool PasswordConfirmed { get; init; }

        /// <summary>
        /// 이 계획으로 풀다가 암호 오류·손상 예외가 나면 이번에 만든 것을 모두 지우고 이 계획으로 처음부터 다시 푼다
        /// (ZIP 의 UTF-8·CP949 두 암호 후보가 모두 판정을 통과해 못 가린 경우 — B3).
        /// </summary>
        public ExtractPlan? Alternate { get; init; }
    }

    /// <summary>
    /// 해제 준비 — 대상 폴더에 아무것도 만들기 '전에' 부른다. 암호가 틀리면 여기서 예외를 던져 폴더도 안 생기게 할 수 있다(ZIP).
    /// 기본: 받은 암호 그대로, CRC 검사 없음.
    /// </summary>
    protected virtual ExtractPlan PrepareExtract(string archivePath, string? password, Func<string, bool> shouldExtract)
        => new(BuildReaderOptions(password));

    /// <summary>
    /// 엔진 예외 → 일집 암호 예외(아니면 null = 원래 예외 그대로). <paramref name="entry"/> 가 null 이면 열기·목록 단계.
    /// 형식별로 더 아는 것이 있으면 파생 클래스가 바꾼다(7z 헤더 암호 → <see cref="SevenZipArchiveService"/>).
    /// </summary>
    protected virtual Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
        => EngineErrorTranslator.IsBadSeek(ex)
            ? new CorruptArchiveException(ex, entryPath)   // 깨진 위치 값으로 엉뚱한 자리를 찾다 거절됨 — 손상(I4-1 검토)
            : EngineErrorTranslator.Translate(ex, new EngineErrorTranslator.Context(
                PasswordGiven: !string.IsNullOrEmpty(password),
                EntryEncrypted: entry is not null && IsEncrypted(entry),
                EntryPath: entryPath));

    /// <summary>
    /// 구조 검사 결과(W2-08 검증 지적 2차 — ZIP 의 목록 손상): <paramref name="Whole"/> = 압축 전체 문제(목록에서 파일이 빠짐 — 없으면 null),
    /// <paramref name="BadEntries"/> = 목록과 실제 내용이 맞지 않는 항목(엔진 항목 순번 — 풀거나 읽지 않는다).
    /// </summary>
    protected internal sealed record StructureProblems(CorruptArchiveException? Whole, IReadOnlySet<int> BadEntries)
    {
        public bool Any => Whole is not null || BadEntries.Count > 0;
    }

    /// <summary>
    /// 엔진이 말없이 넘기는 구조 손상을 따로 본다(기본: 보지 않음 — ZIP 만 <see cref="ZipArchiveService"/>). 엔진이 연 뒤 항목 수(<paramref name="engineEntryCount"/>)를 받아
    /// 풀기·검사가 부른다(목록 보기는 예전처럼 너그럽게 — 부르지 않음). 문제가 없으면 null.
    /// </summary>
    protected virtual StructureProblems? CheckStructure(string archivePath, int engineEntryCount) => null;

    /// <summary><see cref="CheckStructure"/> — 사용자 환경 문제(입출력)는 그대로 던지고, 그 밖의 읽기 실패는 '검사 못 함'(null)으로.</summary>
    private StructureProblems? CheckStructureSafe(string archivePath, int engineEntryCount)
    {
        try
        {
            var p = CheckStructure(archivePath, engineEntryCount);
            return p is { Any: true } ? p : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !ErrorCatalog.IsEnvironmentProblem(ex))
        {
            Logger.LogError($"구조 검사를 하지 못함(넘어감): {archivePath}", ex);
            return null;
        }
    }

    /// <summary>
    /// '암호가 맞지 않아요'로 끝나려는 오류가 사실은 목록 손상 때문인가(IJ3-02 — I2-8: 목록의 AES 정보·위치·크기 칸이 깨지면 맞는 암호로도 암호 판정이 틀려
    /// '암호가 맞지 않아요. 다시 넣어 주세요.'만 나오고 고치기를 권하지 않았다). 틀린 암호 예외가 아니면 그대로 돌려준다.
    /// <paramref name="structureChecked"/>(이미 구조를 봄 — <paramref name="known"/>)이면 압축 전체 문제(목록에서 파일이 빠짐)일 때만 그 손상으로 바꾼다
    /// (목록과 맞지 않는 항목은 풀지 않으므로 그 밖의 항목에서 난 암호 오류는 진짜 암호 문제). 아직 안 봤으면(풀기 전 암호 판정 단계) 목록 기록 차례로 보아
    /// 문제가 하나라도 있으면 손상으로 — 판정은 깨진 기록의 값(AES 세기·위치)으로 암호를 맞춰 보기 때문이다.
    /// </summary>
    private Exception PasswordOrDamage(Exception translated, string archivePath, StructureProblems? known, bool structureChecked)
    {
        if (translated is not WrongPasswordException wrong) return translated;
        var st = structureChecked ? known : CheckStructureSafe(archivePath, -1);
        CorruptArchiveException? damage = st?.Whole ?? (!structureChecked && st is { BadEntries.Count: > 0 } ? ZipListDamagedException.Mismatch(null) : null);
        // 손상은 보이지만(목록과 맞지 않는 항목) 이 항목의 암호 오류가 그 때문인지는 모름 — 틀린 암호와 손상을 가를 수 없어 '손상일 수도' 덧말을 붙인다
        // (I4-1 검토: 맨 앞 머리가 깨져 암호 판정을 못 한 한글 암호 ZIP 이 맞는 암호에도 덧말 없이 '암호가 맞지 않아요'였다)
        if (damage is null && st is { Any: true } && !wrong.MayBeCorrupt && !wrong.IsHeaderEncrypted)
            return new WrongPasswordException(inner: wrong, entryPath: wrong.EntryPath, mayBeCorrupt: true);
        if (damage is null) return translated;
        Logger.LogError($"암호 오류 대신 목록 손상으로 알림: {archivePath}", translated);
        return damage;
    }

    /// <summary>항목이 암호 항목인지(엔진이 모르면 false).</summary>
    protected static bool IsEncrypted(IArchiveEntry entry)
    {
        try { return entry.IsEncrypted; }
        catch { return false; }
    }

    /// <summary>
    /// I5-B(최종 감사 IX-F2): 내용 없는 링크 항목(tar 하드 링크 '1'·심볼릭 링크 '2' — 가리키는 곳만 있고 데이터 0바이트)인가. 일집은 링크를 되살리지 않고
    /// 빈 파일로 만든다(결정 기록 IX-5·B-041 '제외' — 알림만). <paramref name="target"/> = 가리키던 곳(압축 안 이름 또는 경로 글).
    /// 링크 대상이 없는 형식(ZIP·7z 등 — 엔진이 null·빈 글)은 늘 false.
    /// </summary>
    internal static bool IsEmptyLink(IEntry entry, out string target)
    {
        target = string.Empty;
        try
        {
            string? link = entry.LinkTarget;
            if (string.IsNullOrEmpty(link) || entry.IsDirectory || entry.Size > 0) return false;
            target = link.Replace('\\', '/');
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 목록에 보일 수정 시각을 현지 시각으로 맞춘다. 엔진은 형식에 따라 UTC(Kind=Utc — RAR5 의 유닉스 시각, gzip 머리 시각)로 주기도 하는데,
    /// 화면은 받은 값을 그대로 찍으므로 그대로 두면 그 형식만 9시간 이르게 보였다(통합 점검 2026-10-12). 현지·미지정 시각은 그대로.
    /// </summary>
    internal static DateTime? AsLocalTime(DateTime? t) =>
        t is { Kind: DateTimeKind.Utc } utc ? utc.ToLocalTime() : t;

    /// <summary>
    /// 암호 없이 목록을 읽으면 엔진이 '암호 없음'으로 멈추는가 = 파일 이름까지 암호가 걸린 압축(7z 헤더 암호·RAR 머리 암호)인가.
    /// 열기·목록 단계의 실패를 번역할 때만 부른다(성공 경로에서는 읽지 않음).
    /// </summary>
    protected static bool NeedsPasswordToList(string archivePath)
    {
        try
        {
            using var archive = OpenTyped(archivePath, new ReaderOptions());
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

    /// <summary>
    /// 해제 공통 구현. <paramref name="shouldExtract"/>로 항목을 필터링한다.
    /// 암호 오류(<see cref="ArchivePasswordException"/>, 엔진 예외는 <see cref="TranslateError"/> 로 바꿈)면 이번에 만든 파일·폴더를
    /// 모두 지운다(<see cref="ExtractionLedger"/>). 손상·CRC 오류·취소는 쓰다 만 그 파일만 지우고 앞서 끝난 파일은 남긴다(기존 계약).
    /// 계획에 다른 암호 후보(<see cref="ExtractPlan.Alternate"/>)가 있으면, 암호 오류·손상일 때 만든 것을 모두 지우고 그 후보로 한 번 더 푼다.
    /// </summary>
    private Task ExtractCore(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => ExtractCoreSync(archivePath, destinationFolder, shouldExtract, password, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 해제 본체(동기). 7z.dll 을 못 쓰는 PC 에서 SharpCompress 로 대신 푸는 서비스(<see cref="SevenZipEngineServiceBase"/>)·
    /// 자동 풀림 exe 의 ZIP(<see cref="SfxArchiveService"/>)도 부른다(W2-01).
    /// </summary>
    protected void ExtractCoreSync(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = PrepareChecked(archivePath, password, shouldExtract);

        var ledger = new ExtractionLedger();
        try
        {
            ExtractWithPlan(plan, ledger, archivePath, destinationFolder, shouldExtract, password, progress, cancellationToken);
        }
        catch (ArchiveException ex) when (plan.Alternate is not null && ex is ArchivePasswordException or CorruptArchiveException)
        {
            // 두 암호 후보가 모두 판정을 통과한 드문 경우(B3): 먼저 쓴 후보로 만든 것(맞게 풀린 파일까지)을 모두 지우고
            // 다른 후보로 처음부터 다시 푼다 — 같은 이름으로 다시 쓰게(아니면 '이름 (1)' 로 비켜 써짐). 이번에도 실패하면 그 예외 그대로.
            ledger.Rollback();
            ExtractWithPlan(plan.Alternate, new ExtractionLedger(), archivePath, destinationFolder, shouldExtract, password, progress, cancellationToken);
        }
    }

    /// <summary>
    /// 해제·시험 준비(<see cref="PrepareExtract"/>)를 하고 엔진 예외는 일집 예외로 바꾼다. 준비 단계의 암호 판정이 '틀린 암호'라고 하면
    /// 목록 손상 때문이 아닌지 본다(<see cref="PasswordOrDamage"/> — IJ3-02 I2-8).
    /// </summary>
    private ExtractPlan PrepareChecked(string archivePath, string? password, Func<string, bool> shouldExtract)
    {
        try
        {
            return PrepareExtract(archivePath, password, shouldExtract);
        }
        catch (Exception ex) when (ex is not ArchiveException && TranslateError(ex, archivePath, password, null, null) is { } translated)
        {
            throw PasswordOrDamage(translated, archivePath, null, structureChecked: false);
        }
        catch (WrongPasswordException ex)
        {
            var damage = PasswordOrDamage(ex, archivePath, null, structureChecked: false);
            if (ReferenceEquals(damage, ex)) throw;
            throw damage;
        }
    }

    /// <summary>계획 하나로 한 번 푼다. 예외는 일집 예외로 바꿔 던지고, 암호 오류면 <paramref name="ledger"/> 로 이번에 만든 것을 모두 지운다.</summary>
    private void ExtractWithPlan(
        ExtractPlan plan,
        ExtractionLedger ledger,
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        IArchiveEntry? current = null;   // 지금 풀고 있는 항목(오류 번역에 씀 — 열기 단계면 null)
        string? currentPath = null;
        StructureProblems? structure = null;
        bool structureChecked = false;
        try
        {
            ledger.EnsureDirectory(destinationFolder);

            var opened = OpenNamed(archivePath, plan.Options);
            using var archive = opened.Archive;
            var entries = opened.Entries;

            long totalBytes = 0;
            int totalFiles = 0, allFiles = 0;
            foreach (var e in entries)
            {
                if (e.IsDirectory) continue;
                allFiles++;
                string dp = EntryNames.Nfc(MapEntryKey(archivePath, e.Key));   // CustomDecoder가 이미 디코드 (이중 디코드 방지), 화면과 같은 NFC 이름(W2-09)
                if (!shouldExtract(dp)) continue;
                totalBytes += e.Size;
                totalFiles++;
            }

            // 엔진이 말없이 넘기는 목록 손상(ZIP — W2-08 검증 지적 2차): 목록과 맞지 않는 파일은 풀지 않고(다른 파일 내용이 그 이름으로 풀리지 않게),
            // 맞는 파일을 다 푼 뒤 손상으로 알린다(목록에서 빠진 파일은 전부 풀 때만 — 목록에서 고른 파일만 풀면 그 파일은 확인까지 끝남).
            structure = CheckStructureSafe(archivePath, entries.Count);
            structureChecked = true;
            string? skippedBad = null;
            bool anySkipped = false;

            long bytesProcessed = 0;
            int filesProcessed = 0;

            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                cancellationToken.ThrowIfCancellationRequested();

                // CustomDecoder가 이미 디코드 (이중 디코드 방지). 화면·디스크 이름은 NFC(W2-09, I2-10 — 맥 NFD 이름도 완성형으로 풀림).
                // 항목은 엔진 객체 그대로 쓰므로 이름만 바뀐다. NFC 로 같아지는 두 항목은 두 번째가 '이름 (1)'(OutputFiles — 이번 해제가 만든 파일과 겹침).
                string decodedPath = EntryNames.Nfc(MapEntryKey(archivePath, entry.Key));
                if (!shouldExtract(decodedPath)) continue;
                current = entry;
                currentPath = decodedPath;

                // 목록과 맞지 않는 '폴더'도 풀지 않고 손상으로 알린다 — 파일 이름의 끝 바이트가 '/' 로 깨지면 엔진은 그 파일을 폴더로 봐서
                // 예전엔 구조 검사 결과를 보기 전에 빈 폴더만 만들고 넘어가 그 파일이 말없이 빠진 채 종료 코드 0 이었다(I4-1 검증 지적)
                if (entry.IsDirectory && structure is not null && structure.BadEntries.Contains(index))
                {
                    skippedBad ??= decodedPath;
                    anySkipped = true;
                    current = null;
                    currentPath = null;
                    continue;
                }

                // Zip Slip 방어 + Windows 에서 그 이름 그대로는 못 쓰는 조각(':'·'?'·장치 이름·점만 있는 폴더)을 바꾼 출력 경로
                // (EntryPaths — 7z.dll·압축된 tar·EGG 경로와 같은 규칙, W2-01). 공격이면 '안전하지 않은 경로: …'(기존 문구).
                string fullOutputPath = EntryPaths.ResolveOutput(destinationFolder, decodedPath);

                if (entry.IsDirectory)
                {
                    ledger.EnsureDirectory(fullOutputPath);
                    continue;
                }

                // 암호 항목인데 암호를 안 줬으면 풀기 전에 멈춰 화면이 암호를 묻게 한다(형식과 상관없이 같은 길).
                // RAR4(RAR 3.x 암호)는 엔진이 암호 없이도 멈추지 않고 암호화된 바이트를 그대로 내보내 '완료'로 끝났다(통합 점검 2026-10-12).
                // 크기 0 항목은 풀 것이 없어 묻지 않는다 — SharpCompress 는 암호 없는 7z 의 빈 파일도 IsEncrypted = true 로 준다(실측).
                if (string.IsNullOrEmpty(plan.Options.Password) && entry.Size > 0 && IsEncrypted(entry))
                    throw new PasswordRequiredException(entryPath: decodedPath);

                if (structure is not null && structure.BadEntries.Contains(index))
                {
                    skippedBad ??= decodedPath;
                    anySkipped = true;
                    filesProcessed++;
                    current = null;
                    currentPath = null;
                    continue;
                }

                string? parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    ledger.EnsureDirectory(parent);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = decodedPath,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext" 식으로 자동 변경 → 원본 유실 방지(OutputFiles).
                // W2-05(I1-5): 화면이 같은 이름 처리 범위를 열었으면 그 처리기가 정한다 — null 이면 '건너뛰기'(이 파일은 쓰지 않음),
                // '덮어쓰기'면 같은 폴더의 숨김 임시 파일에 다 쓴 뒤 바꾼다(쓰다 실패하면 원래 파일 그대로).
                var target = OutputFiles.OpenTarget(fullOutputPath, entry.Size, AsLocalTime(entry.LastModifiedTime), totalFiles - filesProcessed - 1);
                if (target is null)
                {
                    filesProcessed++;
                    current = null;
                    currentPath = null;
                    continue;
                }
                var output = target.Stream;
                string writePath = target.WritePath;
                ledger.AddFile(writePath);
                try
                {
                    var crc = plan.VerifyCrc ? new Crc32() : null;
                    using (output)
                    using (var input = plan.OpenOverride?.Invoke(index, entry) ?? OpenEntryData(archivePath, entry))
                    {
                        var buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            crc?.Update(new ArraySegment<byte>(buffer, 0, read));
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
                    if (crc is not null) VerifyCrc(entry, decodedPath, (uint)crc.Value, plan.PasswordConfirmed);
                }
                catch
                {
                    // 취소/오류/CRC 불일치로 중단되면 절반만 기록된(또는 틀린 내용의) writePath(정상 이름이라 완전한 파일로 오인됨)를
                    // 지우고 예외를 그대로 전파한다(이미 완료된 앞 파일들은 보존 — 암호 오류면 바깥에서 장부로 모두 지움).
                    try { File.Delete(writePath); } catch { /* 정리 실패는 무시 */ }
                    ledger.ForgetFile(writePath);
                    throw;
                }

                if (entry.LastModifiedTime is DateTime dt)
                {
                    try { File.SetLastWriteTime(writePath, dt); }
                    catch { /* 일부 파일에서 실패 가능 */ }
                }

                // 덮어쓰기: 다 맞은 임시 파일로 원래 파일을 바꾼다(실패하면 임시 파일을 지우고 예외 — 원래 파일 그대로).
                // 바꾼 뒤의 경로는 원래 있던 파일이라 장부에 넣지 않는다(뒤에서 암호 오류로 되돌려도 사용자 파일을 지우지 않게).
                // I5-B: 다른 프로그램이 열어 둔 파일이라 못 바꾸면 Commit 이 그 파일만 건너뛴 것으로 적고(알림) null — 다음 파일로 간다.
                bool written = true;
                if (target.Overwrites)
                {
                    try { written = target.Commit() is not null; }
                    finally { ledger.ForgetFile(writePath); }
                }

                // I5-B(최종 감사 IX-F2): tar 링크 항목은 되살리지 않고 내용 없는 파일로 만든다(결정 기록 — 알림만). 말없이 넘기지 않게 알릴 것에 적는다
                // (결과 창·여러 개 풀기 요약 '링크 N개는 내용 없이 만들었어요', 창 없이 풀기는 표준 오류 한 줄 + 종료 코드 1).
                if (written && IsEmptyLink(entry, out var linkTarget))
                    ExtractionNotes.Current?.AddLink(decodedPath, linkTarget);

                filesProcessed++;
                current = null;
                currentPath = null;
            }

            if (anySkipped) throw ZipListDamagedException.Mismatch(skippedBad);
            if (structure?.Whole is { } whole && totalFiles == allFiles) throw whole;

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });
        }
        catch (Exception ex)
        {
            var translated = ex as ArchiveException ?? TranslateError(ex, archivePath, password, current, currentPath);
            // 판정이 암호를 이미 충분히 확인했으면 암호 항목의 데이터·인증값 오류(엔진 번역의 '틀린 암호, 손상일 수도')는 손상이다(B4) —
            // 다시 묻지 않고, 앞서 맞게 풀린 파일은 남긴다(깨진 그 파일은 위에서 이미 지움). 그 항목의 암호 확인값만 어긋난 것('틀린 암호')도 확인된 암호로는
            // 그 항목의 암호 머리가 깨진 것이다(I4-1 검토 — 맞는 암호에 '암호가 맞지 않아요'로 끝났다).
            if (plan.PasswordConfirmed && translated is WrongPasswordException { IsHeaderEncrypted: false } maybe)
                translated = new CorruptArchiveException(ex, maybe.EntryPath ?? currentPath);
            // 목록이 손상돼 암호 판정·풀기가 틀린 것이면 손상으로(IJ3-02 I2-8 — 맞는 암호로도 '암호가 맞지 않아요'였다)
            if (translated is not null) translated = PasswordOrDamage(translated, archivePath, structure, structureChecked);
            // 틀린 암호로 쓴 것(쓰레기 파일·빈 폴더)을 남기지 않는다. 같은 이름 창의 [취소](W2-05)도 이번에 만든 것을 모두 지운다.
            if (translated is ArchivePasswordException || ex is CollisionCancelledException)
                ledger.Rollback();
            if (translated is null || ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
    }

    /// <summary>
    /// 시험(W2-06 — 고친 압축을 바꿔 넣기 전 검사): 고른 항목을 끝까지 풀어 보기만 한다(디스크에 쓰지 않음). 해제와 같은 준비
    /// (<see cref="PrepareExtract"/> — 한글 암호 바이트·WinZip AES 직접 풀기·CRC 확인)를 쓰므로 해제가 알아챌 손상은 여기서도 알아챈다.
    /// 틀리면 해제와 같은 일집 예외. 확인한 파일 수를 돌려준다.
    /// </summary>
    /// <param name="include">시험할 항목(목록 이름 기준). 암호를 모를 때 암호 항목을 빼는 데 쓴다.</param>
    /// <param name="onTestedBytes">지금까지 풀어 본 바이트(풀린 크기, 누적)를 읽을 때마다 알린다(진행 창 — W2-06 검증 지적 2차: ZIP 검사 동안 막대가 멈춰 보임). 다른 후보 암호로 다시 시험하면 0 부터 다시 센다.</param>
    internal int TestEntriesSync(string archivePath, string? password, Func<string, bool> include, CancellationToken cancellationToken, Action<long>? onTestedBytes = null)
    {
        var plan = PrepareChecked(archivePath, password, include);
        try
        {
            return TestWithPlan(plan, archivePath, include, password, cancellationToken, onTestedBytes);
        }
        catch (ArchiveException ex) when (plan.Alternate is not null && ex is ArchivePasswordException or CorruptArchiveException)
        {
            return TestWithPlan(plan.Alternate, archivePath, include, password, cancellationToken, onTestedBytes);
        }
    }

    private int TestWithPlan(ExtractPlan plan, string archivePath, Func<string, bool> include, string? password, CancellationToken cancellationToken, Action<long>? onTestedBytes)
    {
        IArchiveEntry? current = null;
        string? currentPath = null;
        int checkedFiles = 0;
        StructureProblems? structure = null;
        bool structureChecked = false;
        try
        {
            var opened = OpenNamed(archivePath, plan.Options);
            using var archive = opened.Archive;
            var entries = opened.Entries;
            // 목록 손상(ZIP — W2-08 검증 지적 2차)도 시험 실패로
            structure = CheckStructureSafe(archivePath, entries.Count);
            structureChecked = true;
            if (structure?.Whole is { } whole) throw whole;
            var buffer = new byte[81920];
            long tested = 0;
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                cancellationToken.ThrowIfCancellationRequested();
                // 목록과 맞지 않는 항목은 폴더로 보여도 문제(이름 끝이 '/' 로 깨진 파일 — I4-1 검증 지적)
                bool bad = structure is not null && structure.BadEntries.Contains(index);
                if (entry.IsDirectory && !bad) continue;
                string path = EntryNames.Nfc(MapEntryKey(archivePath, entry.Key));
                if (!include(path)) continue;
                if (bad) throw ZipListDamagedException.Mismatch(path);
                current = entry;
                currentPath = path;
                if (string.IsNullOrEmpty(plan.Options.Password) && entry.Size > 0 && IsEncrypted(entry))
                    throw new PasswordRequiredException(entryPath: path);
                var crc = plan.VerifyCrc ? new Crc32() : null;
                using (var input = plan.OpenOverride?.Invoke(index, entry) ?? OpenEntryData(archivePath, entry))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        crc?.Update(new ArraySegment<byte>(buffer, 0, read));
                        if (onTestedBytes is not null)
                        {
                            tested += read;
                            onTestedBytes(tested);
                        }
                    }
                }
                if (crc is not null) VerifyCrc(entry, path, (uint)crc.Value, plan.PasswordConfirmed);
                checkedFiles++;
                current = null;
                currentPath = null;
            }
            return checkedFiles;
        }
        catch (Exception ex)
        {
            var translated = ex as ArchiveException ?? TranslateError(ex, archivePath, password, current, currentPath);
            if (plan.PasswordConfirmed && translated is WrongPasswordException { IsHeaderEncrypted: false } maybe)
                translated = new CorruptArchiveException(ex, maybe.EntryPath ?? currentPath);
            if (translated is not null) translated = PasswordOrDamage(translated, archivePath, structure, structureChecked);
            if (translated is null || ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
    }

    // ───── W2-08(압축 검사 — I2-7)에서 더한 것 ─────

    TestReport IArchiveTestable.TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        TestArchive(archivePath, password, progress, cancellationToken);

    /// <summary>
    /// 압축 검사(W2-08): 모든 항목을 끝까지 읽어 보기만 한다(디스크에 쓰지 않음 — 풀기와 같은 준비 <see cref="PrepareExtract"/>·CRC 확인).
    /// <see cref="TestEntriesSync"/>(첫 문제에서 멈춤)와 달리 항목 하나가 깨져도 다음 항목을 계속 보고 문제를 모아 돌려준다(ZIP 은 항목마다 따로 읽혀 나머지가 멀쩡할 수 있음).
    /// 열기부터 안 되면 전체 문제 하나. 암호 문제·취소·사용자 환경 문제는 예외. 형식마다 다르게 여는 파생 클래스(7z.dll·raw bzip2·자동 풀림 exe)가 바꾼다.
    /// </summary>
    internal virtual TestReport TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        TestArchiveCore(archivePath, password, progress, cancellationToken);

    /// <summary>SharpCompress 로 여는 검사 본체(7z.dll 을 못 쓸 때의 7z·RAR, 자동 풀림 ZIP 도 이 길).</summary>
    protected TestReport TestArchiveCore(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var plan = PrepareChecked(archivePath, password, _ => true);
        try
        {
            var first = TestReportWithPlan(plan, archivePath, password, progress, cancellationToken);
            if (first.Ok || plan.Alternate is null) return first;
            // 두 암호 후보가 모두 판정을 통과한 드문 경우(B3): 다른 후보로도 보고 문제가 적은 쪽을 믿는다
            try
            {
                var second = TestReportWithPlan(plan.Alternate, archivePath, password, progress, cancellationToken);
                return second.Problems.Count < first.Problems.Count ? second : first;
            }
            catch (ArchivePasswordException)
            {
                return first;
            }
        }
        catch (ArchivePasswordException) when (plan.Alternate is not null)
        {
            return TestReportWithPlan(plan.Alternate, archivePath, password, progress, cancellationToken);
        }
    }

    private TestReport TestReportWithPlan(ExtractPlan plan, string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        // 엔진 예외 → 일집 예외(풀기와 같은 번역). 판정이 암호를 이미 확인했으면 '틀린 암호, 손상일 수도'는 손상.
        Exception Translate(Exception ex, IArchiveEntry? entry, string? path)
        {
            var t = ex as ArchiveException ?? TranslateError(ex, archivePath, password, entry, path) ?? ex;
            if (plan.PasswordConfirmed && t is WrongPasswordException { IsHeaderEncrypted: false } maybe)
                t = new CorruptArchiveException(ex, maybe.EntryPath ?? path);
            return t;
        }

        IArchive? archive = null;
        var problemLog = new TestProblemLog(archivePath);   // I4-1: 항목 문제 로그는 처음 20개까지
        try
        {
            List<IArchiveEntry> entries;
            try
            {
                (archive, entries) = OpenNamed(archivePath, plan.Options);   // 목록·풀기와 같은 이름 방식(W2-09)
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var t = PasswordOrDamage(Translate(ex, null, null), archivePath, null, structureChecked: false);
                if (!ArchiveTestService.IsArchiveProblem(t)) Rethrow(t, ex);
                Logger.LogError($"압축 검사 — 열지 못함: {archivePath}", ex);
                return TestReport.ForArchive(t);
            }

            // 엔진이 말없이 넘기는 목록 손상(ZIP — W2-08 검증 지적 2차: 깨진 목록에서 엔진이 멈춰 '문제없어요. 파일 0개를 확인했어요.')
            var structure = CheckStructureSafe(archivePath, entries.Count);
            // 목록과 맞지 않는 항목은 엔진이 폴더로 봐도 파일 하나로 센다 — 이름 끝 바이트가 '/' 로 깨진 파일이면 폴더로 보여 검사가 건너뛰고
            // '문제없어요. 파일 N-1개'였다(I4-1 검증 지적)
            bool Bad(int i) => structure is not null && structure.BadEntries.Contains(i);

            long totalBytes = 0;
            int totalFiles = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.IsDirectory && !Bad(i)) continue;
                totalBytes += e.Size;
                totalFiles++;
            }

            var problems = new List<TestProblem>();
            var links = new List<LinkNote>();   // I5-B: 풀면 내용 없이 만들어지는 링크 항목(문제는 아님 — 알림)
            int checkedFiles = 0;
            long done = 0;
            bool passwordProven = false;
            WrongPasswordException? pendingWrong = null;
            var buffer = new byte[81920];
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsDirectory && !Bad(index)) continue;
                string path = EntryNames.Nfc(MapEntryKey(archivePath, entry.Key));   // 화면과 같은 NFC 이름(W2-09)
                if (Bad(index))
                {
                    // 목록이 가리키는 자리에 이 파일이 없다 — 읽지 않는다(다른 파일 내용을 이 파일로 확인하지 않게)
                    var mismatch = ZipListDamagedException.Mismatch(path);
                    problems.Add(new TestProblem(path, ZipListDamagedException.EntryReason, ErrorCatalog.DetailsOf(mismatch), ProblemKind.Corrupt));
                    done += Math.Max(0, entry.Size);
                    continue;
                }
                bool encrypted = IsEncrypted(entry);
                // 암호 항목인데 암호가 없으면 화면이 묻게(풀기와 같은 길)
                if (string.IsNullOrEmpty(plan.Options.Password) && entry.Size > 0 && encrypted)
                    throw new PasswordRequiredException(entryPath: path);
                progress?.Report(new ArchiveProgress { CurrentFile = path, BytesProcessed = done, TotalBytes = totalBytes, FilesProcessed = checkedFiles + problems.Count, TotalFiles = totalFiles });
                long before = done;
                try
                {
                    var crc = plan.VerifyCrc ? new Crc32() : null;
                    using (var input = plan.OpenOverride?.Invoke(index, entry) ?? OpenEntryData(archivePath, entry))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            crc?.Update(new ArraySegment<byte>(buffer, 0, read));
                            done += read;
                            progress?.Report(new ArchiveProgress { CurrentFile = path, BytesProcessed = Math.Min(done, totalBytes), TotalBytes = totalBytes, FilesProcessed = checkedFiles + problems.Count, TotalFiles = totalFiles });
                        }
                    }
                    if (crc is not null) VerifyCrc(entry, path, (uint)crc.Value, plan.PasswordConfirmed);
                    checkedFiles++;
                    if (encrypted && entry.Size > 0) passwordProven = true;
                    if (IsEmptyLink(entry, out var linkTarget)) links.Add(new LinkNote(path, linkTarget));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    done = before + Math.Max(0, entry.Size);
                    // 목록에서 파일이 빠질 만큼 목록이 깨졌으면 암호 오류도 그 손상 때문으로 본다(IJ3-02 I2-8)
                    var t = PasswordOrDamage(Translate(ex, entry, path), archivePath, structure, structureChecked: true);
                    if (t is WrongPasswordException { MayBeCorrupt: true } maybe)
                    {
                        // 틀린 암호와 손상을 가를 수 없다 — 같은 암호로 다른 암호 항목이 맞게 읽히면 손상, 아니면 끝에서 암호를 다시 묻는다
                        pendingWrong ??= maybe;
                        problems.Add(new TestProblem(path, ArchiveTestService.EntryReason(t), ErrorCatalog.DetailsOf(ex), ProblemKind.WrongPassword));
                        continue;
                    }
                    if (!ArchiveTestService.IsArchiveProblem(t)) Rethrow(t, ex);
                    problemLog.Entry(path, ex);
                    problems.Add(new TestProblem(path, ArchiveTestService.EntryReason(t), ErrorCatalog.DetailsOf(ex), ErrorCatalog.Describe(t).Kind));
                }
            }
            if (pendingWrong is not null && !passwordProven && !plan.PasswordConfirmed) throw pendingWrong;
            if (structure?.Whole is { } whole)
            {
                Logger.LogError($"압축 검사 — 목록 손상: {archivePath}", whole);
                problems.Add(new TestProblem(string.Empty, whole.Message, ErrorCatalog.DetailsOf(whole), ProblemKind.Corrupt));
            }
            progress?.Report(new ArchiveProgress { CurrentFile = string.Empty, BytesProcessed = totalBytes, TotalBytes = totalBytes, FilesProcessed = totalFiles, TotalFiles = totalFiles });
            return new TestReport { CheckedCount = checkedFiles, Problems = problems, Notes = TestReport.LinkNotes(links) };
        }
        finally
        {
            archive?.Dispose();
            problemLog.Finish();
        }
    }

    /// <summary>번역한 예외를 던진다(번역이 없었으면 원래 예외를 호출 위치 그대로).</summary>
    private static void Rethrow(Exception translated, Exception original)
    {
        if (ReferenceEquals(translated, original)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(original);
        throw translated;
    }

    /// <summary>
    /// B4: 풀린 내용의 CRC-32 를 항목에 적힌 값과 비교한다. WinZip AES 의 AE-2(암호 항목인데 CRC 칸이 0)는 CRC 를 적지 않으므로 건너뛴다
    /// (그 무결성은 AES 인증값이 맡음 — <see cref="WinZipAes"/>). 크기 0 항목은 CRC 0 과 비교된다(빈 내용의 CRC 가 0).
    /// 틀리면 손상. 단 암호 항목인데 판정이 암호를 충분히 확인하지 못했으면(<paramref name="passwordConfirmed"/> 아님 — ZipCrypto 항목 하나뿐 등)
    /// 1바이트 암호 검사를 우연히 통과한 틀린 암호일 수 있어 암호 예외(마지막 방어선).
    /// </summary>
    private static void VerifyCrc(IArchiveEntry entry, string path, uint actual, bool passwordConfirmed)
    {
        bool encrypted = IsEncrypted(entry);
        uint expected = unchecked((uint)entry.Crc);
        if (encrypted && expected == 0) return;
        if (actual == expected) return;
        if (encrypted && !passwordConfirmed) throw new WrongPasswordException(entryPath: path, mayBeCorrupt: true);
        throw new ChecksumMismatchException(path);
    }

    public abstract Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    // 압축할 원본 모으기(파일 평탄화 + 완전히 빈 폴더 + 못 들어간 폴더)는 Services/SourceCollector.cs(I0-2 — 하위 폴더 권한 오류로 전체가 실패하지 않게).
    // 파일이 있는 폴더는 파일 항목이, 하위 폴더가 있는 폴더는 그 하위 항목이 경로를 만들어 주므로 '완전히 빈' 폴더만 따로 기록하면 된다.

    /// <summary>
    /// 여러 파일을 담는 형식(ZIP·TAR)의 '못 읽는 원본 건너뛰기' 틀(I0-2). <paramref name="writeOnce"/> 는 '뺄 파일'(경로 → 이유)을 받아
    /// 그 파일들을 건너뛰며 압축 파일을 한 번 쓴다. 열 때(첫 블록까지) 못 읽는 파일은 writeOnce 가 스스로 건너뛴다.
    /// 열고 나서 읽는 도중에 막힌 파일(다른 프로그램이 파일의 뒤쪽 한 부분만 잠근 DB 등)은 항목 머리를 이미 써서 그 항목만 뺄 수 없으므로,
    /// writeOnce 가 <see cref="SourceUnreadableException"/> 을 던지면 쓰다 만 압축 파일을 지우고 그 파일을 뺀 채 처음부터 다시 쓴다
    /// (파일마다 한 번뿐이라 끝이 있다. 드문 경우라 앞서 쓴 만큼을 다시 쓰는 비용은 감수 — 진행 막대가 처음부터 다시 오른다).
    /// 담은 파일이 하나도 없으면 writeOnce 는 압축 파일을 만들지 않고 <see cref="NothingToCompressException"/> 을 던진다.
    /// </summary>
    protected static CompressResult RewriteWithoutUnreadable(string archivePath, Func<IReadOnlyDictionary<string, string>, CompressResult> writeOnce)
    {
        var dropped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            try
            {
                return writeOnce(dropped);
            }
            catch (SourceUnreadableException ex) when (!dropped.ContainsKey(ex.SourcePath))
            {
                dropped[ex.SourcePath] = ex.Reason;
                // 읽기 실패는 그 파일을 열어 항목 머리를 쓴 뒤에만 난다 → 이번 회차가 만든(또는 덮어쓰기 시작한) 압축 파일이다
                try { File.Delete(archivePath); } catch { /* 다음 회차가 덮어쓴다 */ }
            }
        }
    }

    /// <summary>
    /// 압축 파일을 처음 쓸 때 만드는 출력(I0-2) — 담을 파일이 하나도 없으면 아예 만들지 않아(빈 압축 파일 없음),
    /// 덮어쓰려던 기존 파일도 그대로 남는다. <see cref="Dispose"/> 는 실패 중에도 원래 예외를 가리지 않는다(닫다 난 오류는 삼킴).
    /// </summary>
    protected sealed class LazyArchiveOutput<TWriter> : IDisposable where TWriter : class, IDisposable
    {
        private readonly string _archivePath;
        private readonly Func<FileStream, TWriter> _createWriter;
        private FileStream? _file;
        private TWriter? _writer;

        public LazyArchiveOutput(string archivePath, Func<FileStream, TWriter> createWriter)
        {
            _archivePath = archivePath;
            _createWriter = createWriter;
        }

        /// <summary>만들었는가.</summary>
        public bool Created => _writer is not null;

        /// <summary>쓰기 도구(처음 부르면 압축 파일을 만든다).</summary>
        public TWriter Writer
        {
            get
            {
                if (_writer is not null) return _writer;
                string? dir = Path.GetDirectoryName(_archivePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                _file = File.Create(_archivePath);
                try
                {
                    _writer = _createWriter(_file);
                }
                catch
                {
                    _file.Dispose();
                    _file = null;
                    throw;
                }
                return _writer;
            }
        }

        /// <summary>성공해서 닫는다(닫다 난 오류는 그대로 던짐 — 마무리 쓰기 실패를 숨기지 않게).</summary>
        public void Complete()
        {
            _writer?.Dispose();
            _file?.Dispose();
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { /* 이미 실패 중 — 원래 예외를 가리지 않게 */ }
            try { _file?.Dispose(); } catch { /* 위와 같음 */ }
        }
    }

    /// <summary>입력 파일들의 총 바이트 합계 (진행률 계산용).</summary>
    protected static long SumFileSizes(IEnumerable<(string sourceFile, string entryName)> files)
    {
        long total = 0;
        foreach (var (sourceFile, _) in files)
            total += SafeLength(sourceFile);
        return total;
    }

    /// <summary>두 경로가 같은 파일인가(전체 경로를 대소문자 무시로 비교 — 압축 파일이 원본 폴더 안에 있을 때 자기 자신을 거르는 데 씀).</summary>
    protected static bool IsSameFile(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    /// <summary>파일 크기(못 읽으면 0 — 권한 등).</summary>
    protected static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// 엔진이 준 항목 이름(이미 CustomDecoder 로 디코드됨)을 화면·해제에 쓸 이름으로 바꾼다. 기본은 그대로.
    /// 목록·해제·선택 해제 비교가 모두 이 이름을 쓰므로, 파생 클래스가 '이름 없는 항목'(FNAME 없는 .gz) 등을 여기서 메운다.
    /// </summary>
    protected virtual string MapEntryKey(string archivePath, string? key) => key ?? string.Empty;

    /// <summary>목록·해제에서 압축 파일을 여는 곳. 기본은 <see cref="OpenTyped"/>. 형식 하나만 다루는 파생 클래스가 바꿀 수 있다.</summary>
    protected virtual IArchive OpenForRead(string archivePath, ReaderOptions options) => OpenTyped(archivePath, options);

    /// <summary>
    /// 목록·해제·시험이 압축 파일을 여는 길(W2-09): <see cref="OpenForRead"/> 로 열고 항목을 모두 읽은 뒤, 이름 글자 방식을 압축 파일 하나에 하나로 정한다
    /// (<see cref="OpenWithNameDetection"/> — 일본어·중국어 이름이면 그 방식으로 한 번 더 연다). 돌려준 압축은 호출자가 닫는다.
    /// </summary>
    protected (IArchive Archive, List<IArchiveEntry> Entries) OpenNamed(string archivePath, ReaderOptions options) =>
        OpenWithNameDetection(o => OpenForRead(archivePath, o), options);

    /// <summary>
    /// 이름 방식을 정하며 연다(W2-09, I2-10): 먼저 이름을 모으며 예전 규칙으로 열어 항목을 모두 읽고(<see cref="NameCollector"/>), 모은 날 이름으로 방식을 정한다
    /// (<see cref="ArchiveNameEncodingDetector"/>). 정한 방식으로 풀어도 이름이 하나도 안 바뀌면(한국어·UTF-8·ASCII 압축 — 거의 모두) 그대로 돌려주고,
    /// 바뀌면(Shift-JIS·GBK 압축 등) 처음 연 것을 닫고 정한 방식으로 다시 연다 — 열기는 두 번이지만 그런 압축은 드물다.
    /// <paramref name="options"/> 의 암호·암호 인코딩은 그대로 쓰고 이름 풀기만 바꾼다.
    /// </summary>
    internal static (IArchive Archive, List<IArchiveEntry> Entries) OpenWithNameDetection(Func<ReaderOptions, IArchive> open, ReaderOptions options)
    {
        var collector = new NameCollector();
        var archive = open(WithNameDecoder(options, collector.Decode));
        NameDecoder decided;
        try
        {
            var entries = archive.Entries.ToList();
            decided = collector.Decide();
            if (collector.AgreesWith(decided)) return (archive, entries);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
        archive.Dispose();

        var again = open(WithNameDecoder(options, decided.Decode));
        try
        {
            return (again, again.Entries.ToList());
        }
        catch
        {
            again.Dispose();
            throw;
        }
    }

    /// <summary>이름 풀기만 바꾼 옵션 사본(암호·암호 인코딩·기본 인코딩은 그대로 — 받은 옵션 객체는 건드리지 않음).</summary>
    private static ReaderOptions WithNameDecoder(ReaderOptions options, Func<byte[], int, int, EncodingType, string> decoder)
    {
        var src = options.ArchiveEncoding;
        return options with
        {
            ArchiveEncoding = new ArchiveEncoding
            {
                Default = src.Default,
                Password = src.Password,
                UTF8 = src.UTF8,
                Forced = src.Forced,
                CustomDecoder = decoder,
            },
        };
    }

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

    /// <summary>
    /// 엔진 옵션. <paramref name="passwordBytes"/> 를 주면 암호를 그 '바이트 그대로' 넘긴다(Latin-1 문자열 + Latin-1 암호 인코딩) —
    /// SharpCompress 는 암호 문자열을 늘 UTF-8 로 바꾸므로, 반디집의 CP949 한글 ZipCrypto 암호(B3)는 이렇게만 맞는다.
    /// (W2-01: ZIP 이름 대조 — 7z.dll 로 여는 .zipx 의 날 이름 바이트 읽기 — 도 같은 이름 규칙을 쓰려고 internal 로도 열었다)
    /// </summary>
    protected internal static ReaderOptions BuildReaderOptions(string? password, byte[]? passwordBytes = null) => new()
    {
        // ""(빈 암호)는 '암호 없음'과 같게 — 엔진에 빈 문자열을 넘기면 그것을 실제 암호로 쓰려 한다.
        Password = passwordBytes is not null ? Encoding.Latin1.GetString(passwordBytes)
                 : string.IsNullOrEmpty(password) ? null : password,
        ArchiveEncoding = passwordBytes is not null
            ? new ArchiveEncoding { Default = Cp437, CustomDecoder = DecodeName, Password = Encoding.Latin1 }
            : new ArchiveEncoding { Default = Cp437, CustomDecoder = DecodeName }
    };

    // CP437은 0x00~0xFF를 무손실 1:1 매핑 → CustomDecoder가 적용되지 않는
    // 경로에서도 원본 바이트가 보존되어 호출부의 KoreanFileNameDecoder.Decode로 복구 가능.
    private static Encoding Cp437 => Encoding.GetEncoding(437);

    // 파일명 원본 바이트를 직접 받아 UTF-8/CP949를 자동 판별 (가장 견고).
    // 네 번째 인자(EncodingType: UTF-8 플래그 등)는 무시 — 플래그와 무관하게 'strict UTF-8 먼저' 판별(기존 동작).
    private static string DecodeName(byte[] bytes, int index, int count, EncodingType _) =>
        KoreanFileNameDecoder.DecodeBytes(bytes, index, count);
}
