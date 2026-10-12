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
            try
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
            }
            catch (Exception ex) when (TranslateError(ex, archivePath, password, null, null) is { } translated)
            {
                // 헤더 암호 7z 의 '암호 없음'·'틀린 암호'를 화면이 타입으로 알아보게(B5)
                throw translated;
            }
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
        => EngineErrorTranslator.Translate(ex, new EngineErrorTranslator.Context(
            PasswordGiven: !string.IsNullOrEmpty(password),
            EntryEncrypted: entry is not null && IsEncrypted(entry),
            EntryPath: entryPath));

    /// <summary>항목이 암호 항목인지(엔진이 모르면 false).</summary>
    protected static bool IsEncrypted(IArchiveEntry entry)
    {
        try { return entry.IsEncrypted; }
        catch { return false; }
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
        return Task.Run(() =>
        {
            ExtractPlan plan;
            try
            {
                plan = PrepareExtract(archivePath, password, shouldExtract);
            }
            catch (Exception ex) when (ex is not ArchiveException && TranslateError(ex, archivePath, password, null, null) is { } translated)
            {
                throw translated;
            }

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
        }, cancellationToken);
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
        try
        {
            ledger.EnsureDirectory(destinationFolder);

            using var archive = OpenForRead(archivePath, plan.Options);
            var entries = archive.Entries.ToList();

            long totalBytes = 0;
            int totalFiles = 0;
            foreach (var e in entries)
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

            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                cancellationToken.ThrowIfCancellationRequested();

                string decodedPath = MapEntryKey(archivePath, entry.Key);   // CustomDecoder가 이미 디코드 (이중 디코드 방지)
                if (!shouldExtract(decodedPath)) continue;
                current = entry;
                currentPath = decodedPath;

                string outputPath = Path.Combine(destinationFolder, decodedPath);
                string fullOutputPath = Path.GetFullPath(outputPath);

                // Zip Slip 방어
                if (!fullOutputPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"안전하지 않은 경로: {decodedPath}");

                if (entry.IsDirectory)
                {
                    ledger.EnsureDirectory(fullOutputPath);
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
                var output = OutputFiles.CreateNew(fullOutputPath, out string writePath);
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

                filesProcessed++;
                current = null;
                currentPath = null;
            }

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
            // 다시 묻지 않고, 앞서 맞게 풀린 파일은 남긴다(깨진 그 파일은 위에서 이미 지움).
            if (plan.PasswordConfirmed && translated is WrongPasswordException { MayBeCorrupt: true } maybe)
                translated = new CorruptArchiveException(ex, maybe.EntryPath ?? currentPath);
            if (translated is ArchivePasswordException)
                ledger.Rollback();   // 틀린 암호로 쓴 것(쓰레기 파일·빈 폴더)을 남기지 않는다
            if (translated is null || ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
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
    /// </summary>
    protected static ReaderOptions BuildReaderOptions(string? password, byte[]? passwordBytes = null) => new()
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
