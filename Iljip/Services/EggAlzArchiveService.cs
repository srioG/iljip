using System.IO;
using EggDotNet;
using ICSharpCode.SharpZipLib.Checksum;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 이스트소프트 EGG/ALZ 해제 전용 서비스 (EggDotNet 기반).
///
/// SharpCompress는 egg/alz를 못 풀기 때문에 <see cref="SharpCompressArchiveServiceBase"/>를
/// 상속하지 않고 <see cref="IArchiveService"/>를 직접 구현한다. 따라서 베이스가 주던 안전장치
/// (Zip Slip 방어·동명파일 비파괴 자동개명·진행률 보고)를 여기서 직접 갖춘다.
///
/// EggDotNet은 store/deflate/bzip2/lzma + 이스트소프트 독자 AZO + 분할볼륨(.volN.egg)을 모두
/// 해제한다(자체 테스트 픽스처로 CRC 일치 검증 완료). 압축(생성)은 지원하지 않는다(반디집/알집 영역).
/// EGG 파일명은 유니코드라 EggDotNet이 디코드한 FullName을 그대로 쓴다(이중 디코드 금지).
/// ALZ 이름은 CP949인데 EggDotNet이 UTF-8로만 읽어 한글이 깨지므로(B10), <see cref="AlzNameScanner"/>가
/// 원본 바이트를 다시 읽어 확인된 경우에만 이름을 바꿔 쓴다(<see cref="NamedEntries"/>).
/// EggDotNet 0.4.0 우회: 열다 실패하면 연 파일을 직접 닫는다(B8), 크기 0 항목(폴더·빈 파일)은
/// 블록 머리가 없어 CompressedLength·Open()이 NullReferenceException이라 따로 처리한다(B9),
/// 풀린 내용의 크기·CRC 를 직접 맞춰 본다(EggDotNet 은 확인하지 않아 잘린 파일도 '완료'로 끝났음),
/// 폴더는 이름 끝 '/' 뿐 아니라 폴더 속성으로도 알아본다(ALZ 속성은 EggDotNet 이 버려서 스캐너가 읽음).
/// </summary>
public sealed class EggAlzArchiveService : IArchiveService
{
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".egg", ".alz" };

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            try
            {
                using var archive = OpenArchive(archivePath, password);
                var entries = new List<ArchiveEntry>();
                foreach (var n in NamedEntries(archive, archivePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var e = n.Entry;
                    entries.Add(new ArchiveEntry
                    {
                        Path = n.Path,
                        Size = e.UncompressedLength,
                        CompressedSize = SafeLen(e, n.IsDirectory, () => e.CompressedLength),   // B9: 크기 0 항목은 NRE → 0
                        IsDirectory = n.IsDirectory,
                        LastModified = e.LastWriteTime
                    });
                }
                return entries;
            }
            catch (Exception ex) when (Translate(ex, password, null, null) is { } translated)
            {
                throw translated;
            }
        }, cancellationToken);
    }

    public Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExtractCore(archivePath, destinationFolder, _ => true, password, progress, cancellationToken);

    public Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // W2-09: 고른 이름·항목 이름 모두 NFC 로 맞춰 비교(맥 ZIP 의 NFD 이름을 화면 이름으로 골라도, 원래 이름으로 골라도 찾음)
        var selectors = entryPaths
            .Select(EntryNames.SelectorKey)
            .Where(p => p.Length > 0)
            .ToList();

        bool ShouldExtract(string decodedPath)
        {
            string d = EntryNames.Nfc(decodedPath.Replace('\\', '/').TrimEnd('/'));
            foreach (var s in selectors)
            {
                if (d.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
                if (d.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        return ExtractCore(archivePath, destinationFolder, ShouldExtract, password, progress, cancellationToken);
    }

    private static Task ExtractCore(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var ledger = new ExtractionLedger();
            EggArchiveEntry? current = null;   // 지금 풀고 있는 항목(오류 번역에 씀 — 열기 단계면 null)
            string? currentPath = null;
            try
            {
                ledger.EnsureDirectory(destinationFolder);
                using var archive = OpenArchive(archivePath, password);
                // 이름(ALZ 는 CP949 복구 이름)과 항목을 한 번에 짝지어 둔다 — 화면이 넘긴 '복구된 이름'으로 고른 항목을 다시 찾기 위함.
                var named = NamedEntries(archive, archivePath);

                // 진행률 총량 선계산 (디렉터리·필터제외·암호화 제외분은 빼고 파일만)
                long totalBytes = 0;
                int totalFiles = 0;
                foreach (var n in named)
                {
                    if (n.IsDirectory) continue;
                    if (!shouldExtract(n.Path)) continue;
                    totalBytes += n.Entry.UncompressedLength;
                    totalFiles++;
                }

                long bytesProcessed = 0;
                int filesProcessed = 0;

                foreach (var (entry, decodedPath, isDirectory) in named)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (decodedPath.Length == 0) continue;
                    if (!shouldExtract(decodedPath)) continue;
                    current = entry;
                    currentPath = decodedPath;

                    // Zip Slip 방어(대상 폴더 밖이면 '안전하지 않은 경로: …') + Windows 에서 그 이름 그대로는 못 쓰는 조각(':'·'?'·장치 이름)을
                    // 바꾼 출력 경로(EntryPaths — 모든 해제 경로가 같은 규칙, W2-01)
                    string fullOutputPath = EntryPaths.ResolveOutput(destinationFolder, decodedPath);

                    if (isDirectory)
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

                    // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext" 식으로 → 원본 유실 방지(OutputFiles).
                    // W2-05(I1-5): 같은 이름 처리 범위가 있으면 그 처리기가 정한다(null = 건너뛰기, 덮어쓰기는 임시 파일에 다 쓴 뒤 바꿈).
                    var target = OutputFiles.OpenTarget(fullOutputPath, entry.UncompressedLength, entry.LastWriteTime, totalFiles - filesProcessed - 1);
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

                    // entry.Open()이 store/deflate/bzip2/lzma/AZO·암호화를 알아서 해제해 평문 스트림을 준다.
                    // 암호화인데 비번이 없으면 여기서 예외 → 해제 전체 실패(부분 추출로 위장하지 않음).
                    try
                    {
                        using (output)
                        {
                            // B9: 크기 0 항목은 EggDotNet 에 블록 머리가 없어 Open()이 NullReferenceException —
                            // 열지 않고 빈 파일만 둔다(읽을 내용도, 맞춰 볼 CRC 도 없음).
                            if (entry.UncompressedLength != 0)
                            {
                                using var input = entry.Open();
                                var crc = new Crc32();
                                long written = 0;
                                var buffer = new byte[81920];
                                int read;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    output.Write(buffer, 0, read);
                                    crc.Update(new ArraySegment<byte>(buffer, 0, read));
                                    written += read;
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
                                VerifyWritten(archive, entry, decodedPath, written, (uint)crc.Value);
                            }
                        }
                    }
                    catch
                    {
                        // 취소/오류/확인값 불일치로 중단되면 절반만 기록된 writePath(정상 이름이라 완전한 파일로 오인됨)를
                        // 지우고 예외를 그대로 전파한다(이미 완료된 앞 파일들은 보존 — 암호 오류면 바깥에서 장부로 모두 지움).
                        try { File.Delete(writePath); } catch { /* 정리 실패는 무시 */ }
                        ledger.ForgetFile(writePath);
                        throw;
                    }

                    if (entry.LastWriteTime is { } lwt)
                    {
                        try { File.SetLastWriteTime(writePath, lwt); }
                        catch { /* 일부 값/파일에서 실패 가능 — 무시 */ }
                    }

                    if (target.Overwrites)
                    {
                        // 덮어쓰기: 다 맞은 임시 파일로 원래 파일을 바꾼다. 바꾼 경로(원래 있던 파일)는 장부에 넣지 않는다.
                        try { target.Commit(); }
                        finally { ledger.ForgetFile(writePath); }
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
                var translated = ex as ArchiveException ?? Translate(ex, password, current, currentPath);
                // 틀린 암호로 쓴 것(쓰레기 파일·빈 폴더)을 남기지 않는다. 같은 이름 창의 [취소](W2-05)도 이번에 만든 것을 모두 지운다.
                if (translated is ArchivePasswordException || ex is CollisionCancelledException)
                    ledger.Rollback();
                if (translated is null || ReferenceEquals(translated, ex)) throw;
                throw translated;
            }
        }, cancellationToken);
    }

    /// <summary>EggDotNet 예외 → 일집 암호 예외(아니면 null). DecryptFailedException 은 암호가 없으면 '필요', 있으면 '틀림'.</summary>
    private static ArchivePasswordException? Translate(Exception ex, string? password, EggArchiveEntry? entry, string? entryPath)
    {
        bool encrypted;
        try { encrypted = entry?.IsEncrypted == true; }
        catch { encrypted = false; }
        return EngineErrorTranslator.Translate(ex, new EngineErrorTranslator.Context(
            PasswordGiven: !string.IsNullOrEmpty(password), EntryEncrypted: encrypted, EntryPath: entryPath));
    }

    /// <summary>
    /// 풀어 쓴 내용이 압축 파일에 적힌 것과 같은지 본다 — EggDotNet 은 확인하지 않아, 끝까지 받지 못한(잘린) egg/alz 도
    /// 짧은 파일을 남기고 '완료'로 끝났다. 크기는 늘 보고, CRC 는 적혀 있을 때만(0 이 아님) 본다.
    /// 솔리드 EGG 는 항목마다 CRC 가 없어(첫 항목에 묶음 전체 CRC, 나머지는 0) 크기만 본다.
    /// </summary>
    private static void VerifyWritten(EggArchive archive, EggArchiveEntry entry, string path, long written, uint crc)
    {
        long expected = entry.UncompressedLength;
        if (written != expected)
            throw DamagedEntry(path, entry.IsEncrypted, $"크기가 맞지 않아요 — {written:N0} / {expected:N0}바이트");
        if (!archive.IsSolid && entry.Crc32 != 0 && crc != entry.Crc32)
            throw DamagedEntry(path, entry.IsEncrypted, "내용 확인값(CRC)이 맞지 않아요");
    }

    /// <summary>
    /// 확인 실패를 알리는 예외. 화면에 '압축 해제 실패:' 뒤에 그대로 보인다.
    /// 암호 걸린 항목이면 <see cref="WrongPasswordException"/>(손상일 수도 있다는 말 포함) — 화면이 암호를 다시 묻고, 해제는 이번에 만든 것을
    /// 모두 지운다(EGG 의 옛 ZIP 방식 암호는 틀린 암호도 1바이트 검사를 우연히 통과할 수 있어, 그때는 내용 확인값만 틀린다 — ZIP 의 B4 와 같은 규칙).
    /// 자세한 이유는 InnerException(로그용). 암호가 없는 항목은 손상 문구(InvalidDataException — '암호'라는 말 없음, 암호 창 안 뜸).
    /// </summary>
    internal static Exception DamagedEntry(string path, bool encrypted, string why)
    {
        var detail = new InvalidDataException(encrypted
            ? $"'{path}' 을(를) 풀다가 멈췄어요({why}). 암호가 틀렸거나 압축 파일이 손상된 것 같아요."
            : $"'{path}' 을(를) 풀다가 멈췄어요({why}). 압축 파일이 손상되었거나 끝까지 받아지지 않은 것 같아요. 원본 파일을 다시 받아 보세요.");
        return encrypted ? new WrongPasswordException(inner: detail, entryPath: path, mayBeCorrupt: true) : detail;
    }

    /// <summary>
    /// EGG/ALZ 아카이브를 연다(암호 유무와 관계없이 한 경로).
    ///
    /// <see cref="EggFile.Open(string)"/>은 쓰지 않는다 — 열다 실패하면(손상·형식 아님·분할 조각 없음) 자기가 연
    /// 파일 핸들을 닫지 않아 GC 전까지 원본이 잠긴다(B8, 0.4.0 도 같음). 그래서 원본과 분할 조각 스트림을 직접 열어
    /// 모아 두고, 생성자가 던지면 모두 닫는다. 성공하면 ownStream:true 라 EggArchive(=using) 폐기 때 같이 닫힌다
    /// (쓰지 않은 형제 파일은 EggDotNet 이 바로 닫음).
    ///
    /// 분할볼륨(.volN.egg) 콜백은 같은 폴더의 다른 .egg 파일만 후보로 연다(<see cref="SplitVolumeCandidates"/>).
    /// 암호 콜백도 항상 넣는다 — 넣지 않으면 EggDotNet 기본 콜백이 콘솔에서 암호를 읽으려 한다.
    /// </summary>
    private static EggArchive OpenArchive(string archivePath, string? password)
    {
        var opened = new List<Stream>();
        var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        opened.Add(file);
        // 알집 자동 풀림 exe(SfxArchiveService 가 넘김, W2-01): EGG/ALZ 서명 위치부터 보이는 스트림으로(분할 조각은 찾지 않음)
        long offset = EmbeddedOffset(archivePath);
        Stream stream = offset > 0 ? new OffsetStream(file, offset) : file;
        try
        {
            Callbacks.SplitFileReceiverCallback splitCallback = st =>
            {
                var streams = new List<Stream>();
                if (st is FileStream fs)
                {
                    foreach (var sibling in SplitVolumeCandidates(fs.Name))
                    {
                        FileStream s;
                        // 다른 프로그램이 잠근 형제 파일은 분할 조각일 리 없으니 건너뛴다(예전엔 여기서 전체가 실패).
                        try { s = new FileStream(sibling, FileMode.Open, FileAccess.Read, FileShare.Read); }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        opened.Add(s);
                        streams.Add(s);
                    }
                }
                return streams;
            };
            // 암호가 없으면 빈 암호 → EggDotNet 이 바로 DecryptFailedException.
            // Retry=false: 비번이 틀리면 즉시 DecryptFailedException → 상위 재시도 루프가 다시 프롬프트.
            Callbacks.FileDecryptPasswordCallback passwordCallback =
                (_, opts) => { opts.Password = password ?? string.Empty; opts.Retry = false; };
            return new EggArchive(stream, ownStream: true, splitCallback, passwordCallback);
        }
        catch (Exception ex)
        {
            foreach (var s in opened)
            {
                try { s.Dispose(); } catch { /* 닫기 실패는 무시 */ }
            }
            // W2-01: 형식을 못 알아봄(깨진 머리)·분할 조각 없음은 일집 예외로(화면·창 없이 풀기 문구가 형식과 무관하게 같은 쉬운 한국어).
            // 그 밖의 예외(암호 등)는 그대로 — 호출자가 번역한다.
            if (ex is EggDotNet.Exceptions.UnknownEggException) throw new CorruptArchiveException(ex);
            if (ex is EggDotNet.Exceptions.MissingVolumeException) throw new VolumeMissingException(MissingVolumeName(archivePath), ex);
            throw;
        }
    }

    /// <summary>
    /// 자동 풀림 exe('MZ' 로 시작)이면 안쪽 EGG/ALZ 의 시작 위치, 아니면 0(W2-01). 보통 .egg/.alz 는 첫 두 바이트만 읽고 끝난다.
    /// </summary>
    internal static long EmbeddedOffset(string archivePath)
    {
        try
        {
            using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') return 0;
            }
            var kind = ArchiveSniffer.Detect(archivePath, out long offset);
            return kind is ArchiveKind.EggSfx or ArchiveKind.AlzSfx ? offset : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex VolName =
        new(@"^(?<base>.*)\.vol(?<n>\d+)\.egg$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 분할 EGG 의 빠진 조각 이름 추정(알집 규칙 '이름.volN.egg'): vol1 부터 처음으로 없는 번호. 이름이 규칙과 다르면 '이름.vol2.egg'.
    /// (EggDotNet 은 조각을 머리 ID 로 찾아 어느 이름이 빠졌는지 알려 주지 않는다)
    /// </summary>
    internal static string MissingVolumeName(string archivePath)
    {
        string dir = Path.GetDirectoryName(archivePath) ?? string.Empty;
        string name = Path.GetFileName(archivePath);
        var m = VolName.Match(name);
        if (!m.Success) return Path.GetFileNameWithoutExtension(name) + ".vol2.egg";
        string b = m.Groups["base"].Value;
        for (int k = 1; k < 10000; k++)
        {
            string cand = $"{b}.vol{k}.egg";
            if (!File.Exists(Path.Combine(dir, cand))) return cand;
        }
        return name;
    }

    /// <summary>
    /// 분할 EGG 의 다른 조각 후보: 첫 조각과 같은 폴더에 있는 다른 '.egg' 파일(알집 분할 조각 이름은 '이름.volN.egg').
    /// EggDotNet 기본 구현은 폴더의 '모든' 파일을 열어 머리를 읽는다 — 파일이 수천 개인 폴더에서 느리고, OneDrive 폴더에서는
    /// '온라인 전용' 파일을 내려받게 만들 수 있어 .egg 만 연다(진짜 조각인지는 EggDotNet 이 머리 ID 로 가린다).
    /// </summary>
    internal static IEnumerable<string> SplitVolumeCandidates(string firstVolumePath)
    {
        string? dir = Path.GetDirectoryName(firstVolumePath);
        if (string.IsNullOrEmpty(dir)) yield break;
        foreach (var sibling in Directory.EnumerateFiles(dir))
        {
            if (string.Equals(sibling, firstVolumePath, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(Path.GetExtension(sibling), ".egg", StringComparison.OrdinalIgnoreCase)) continue;
            yield return sibling;
        }
    }

    /// <summary>항목과 '화면·해제에 쓸 이름'('\'→'/', 폴더는 끝에 '/')·폴더 여부를 짝지은 것.</summary>
    private readonly record struct NamedEntry(EggArchiveEntry Entry, string Path, bool IsDirectory);

    /// <summary>
    /// 항목마다 화면·해제에 쓸 이름과 폴더 여부를 정한다.
    /// - 이름: ALZ 에서 EggDotNet 이름이 깨졌으면(U+FFFD) <see cref="AlzNameScanner"/>가 원본 바이트로 다시 푼 이름(확인이 안 되면 EggDotNet 이름 그대로).
    /// - 폴더: 이름 끝이 '/' 이거나, 크기 0 이면서 폴더 속성(EGG: 윈도 속성 0x80·POSIX 디렉터리, ALZ: 스캐너가 읽은 속성 0x10)이 있는 항목.
    ///   예전엔 끝 '/' 만 봐서, 끝 구분자 없이 '문서' 로 적힌 폴더 항목이 0바이트 '파일'로 풀리고 그 아래 항목에서 해제가 실패했다.
    ///   폴더인데 끝 '/' 가 없으면 붙여서 다른 폴더 항목과 같은 모양으로 맞춘다.
    /// </summary>
    private static List<NamedEntry> NamedEntries(EggArchive archive, string archivePath)
    {
        var entries = archive.Entries.ToList();
        IReadOnlyList<string> names = entries.Select(e => e.FullName ?? e.Name ?? string.Empty).ToList();

        // ALZ 이면(EGG 는 바로 null) 원본 머리와 짝지어 깨진 이름·폴더 속성을 보완한다.
        var alz = AlzNameScanner.TryMatch(archivePath, names, EmbeddedOffset(archivePath));
        if (alz is not null && names.Any(n => n.Contains('�')))
            names = alz.Select(r => AlzNameScanner.DecodeName(r.Name)).ToList();

        var result = new List<NamedEntry>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string path = EntryNames.Nfc(names[i].Replace('\\', '/'));
            bool dirAttribute = alz is not null ? alz[i].IsDirectory : HasEggDirectoryAttribute(e);
            bool isDirectory = path.EndsWith("/", StringComparison.Ordinal) || (dirAttribute && IsZeroSize(e) && path.Length > 0);
            if (isDirectory && !path.EndsWith("/", StringComparison.Ordinal)) path += "/";
            result.Add(new NamedEntry(e, path, isDirectory));
        }
        return result;
    }

    /// <summary>EGG 항목의 폴더 속성(윈도 정보 0x80, POSIX 형식 비트 = 디렉터리). 정보가 없거나 읽다 실패하면 false.</summary>
    private static bool HasEggDirectoryAttribute(EggArchiveEntry e)
    {
        try
        {
            long a = e.GetExtraAttributes(ExtraAttributeType.FileAttibutes);
            return e.EntryInfoType switch
            {
                EntryInfoType.Windows => (a & (long)WindowsFileAttributes.Directory) != 0,
                EntryInfoType.Posix => (a & 0xF000) == (long)PosixFileAttributes.Directory,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// B9 우회: EggDotNet 0.4.0 은 크기 0 항목(폴더·빈 파일)에 블록 머리가 없어 CompressedLength 등이 NullReferenceException.
    /// 그 경우에만 0 으로 본다. 크기가 있는 항목의 NRE 는 다른 손상이므로 가리지 않고 그대로 던진다.
    /// </summary>
    private static long SafeLen(EggArchiveEntry e, bool isDirectory, Func<long> get)
    {
        try
        {
            return get();
        }
        catch (NullReferenceException) when (isDirectory || IsZeroSize(e))
        {
            return 0;
        }
    }

    private static bool IsZeroSize(EggArchiveEntry e)
    {
        try { return e.UncompressedLength == 0; }
        catch (NullReferenceException) { return false; }
    }

    public Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "EGG/ALZ 압축(생성)은 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }
}
