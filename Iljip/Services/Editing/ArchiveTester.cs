using System.IO;
using SharpSevenZip;

namespace Iljip.Services.Editing;

/// <summary>고칠 수 있는 형식(W2-06).</summary>
public enum EditableFormat
{
    Zip,
    SevenZip,
}

/// <summary>
/// 고친 결과 검사(W2-06, I2-3) — 원본과 바꾸기 '전에' 임시 파일을 본다. 틀리면 예외를 던지고, 편집기는 임시 파일을 지우고 원본을 그대로 둔다.
/// 시험에서는 가짜(늘 실패)를 넣어 '검사가 실패하면 원본이 바이트 그대로'를 확인한다.
/// </summary>
public interface IArchiveTester
{
    /// <summary>
    /// <paramref name="archivePath"/>(임시 파일 — 확장자가 .tmp 라 형식은 <paramref name="format"/> 으로 준다)의 목록이 <paramref name="expectedKeys"/>
    /// (항목 키 — '/' 구분, 끝 '/' 없음, 순서 무관)와 같은지, 모든 항목의 확인값(CRC)이 맞는지 본다. 암호를 모르면(<paramref name="password"/> null)
    /// 암호 항목은 구조만 본다(ZIP 의 지우기·이름 바꾸기 — 암호 항목은 바이트 그대로 옮김).
    /// </summary>
    void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken);

    /// <summary>
    /// 위와 같고, 검사한 정도(0~1)를 <paramref name="onFraction"/> 로 알린다(진행 창 — 큰 7z 는 검사도 몇십 초). 기본은 알리지 않고 위를 부른다(시험용 가짜 검사기).
    /// </summary>
    void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken,
        Action<double>? onFraction) => Test(archivePath, format, expectedKeys, password, cancellationToken);
}

/// <summary>
/// 기본 검사기. ZIP: 끝 머리·중앙 디렉터리·모든 로컬 기록의 자리와 이름(<see cref="ZipRawRewriter.VerifyStructure"/>) + 목록 비교 +
/// 풀어 보며 CRC(풀기와 같은 길 — <see cref="SharpCompressArchiveServiceBase"/>; 암호 바이트 판정·AES 인증값 포함).
/// 7z: 7z.dll 로 열어 목록 비교 + 7z.dll 시험(Check — 모든 항목을 풀어 CRC 확인).
/// </summary>
public sealed class ArchiveTester : IArchiveTester
{
    public static readonly ArchiveTester Instance = new();

    /// <summary>검사가 틀렸을 때의 화면 문구(원본은 그대로).</summary>
    internal const string FailedMessage = "고친 압축 파일을 검사했더니 맞지 않는 곳이 있어서, 원래 압축 파일을 그대로 두었어요.";

    /// <summary>
    /// 검사가 틀렸는데 원래 압축 파일도 같은 검사에 틀릴 때(고치기 전부터 손상) — '고치다 생긴 문제'로 오해하지 않게 따로 알린다(W2-06 검증 지적).
    /// ★ '압축 파일이 아닌'을 넣지 말 것(재사용장치 분류 말 — <see cref="ErrorMessages"/>).
    /// </summary>
    internal const string OriginalDamagedMessage =
        "원래 압축 파일에 이미 손상된 곳이 있어서 고치지 않았어요(고치다 생긴 문제가 아니에요). 원래 압축 파일은 그대로예요. 풀어 보면 어느 파일이 손상됐는지 알 수 있어요.";

    public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken) =>
        Test(archivePath, format, expectedKeys, password, cancellationToken, null);

    public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken,
        Action<double>? onFraction)
    {
        try
        {
            if (format == EditableFormat.Zip) TestZip(archivePath, expectedKeys, password, cancellationToken, onFraction);
            else TestSevenZip(archivePath, expectedKeys, password, cancellationToken, onFraction);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArchiveEditException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new ArchiveEditException(FailedMessage, ex);
        }
    }

    /// <summary>
    /// 고친 결과의 검사(<paramref name="tester"/>)를 하고, 틀리면 원래 압축 파일도 진짜 검사기로 본다 — 원래도 틀리면 그 까닭(<see cref="OriginalProblem"/> —
    /// 손상이면 <see cref="OriginalDamagedMessage"/>, 일집이 못 푸는 방식이면 <see cref="UnreadableMessage"/>), 원래는 맞으면 고치다 생긴 문제
    /// (처음 오류 그대로 — 결과만 일집이 못 푸는 모양이면 <see cref="FailedMessage"/>). 원래 압축 검사는 실패했을 때만 한다(성공하면 비용 없음).
    /// </summary>
    internal static void TestOrBlameOriginal(
        IArchiveTester tester, string work, string original, EditableFormat format, IReadOnlyCollection<string> expectedKeys,
        IReadOnlyCollection<string> originalKeys, string? password, CancellationToken ct, Action<double>? onFraction)
    {
        try
        {
            tester.Test(work, format, expectedKeys, password, ct, onFraction);
        }
        catch (ArchiveEditException first) when (!ct.IsCancellationRequested)
        {
            if (OriginalProblem(original, format, originalKeys, password, ct) is { } why) throw new ArchiveEditException(why, first);
            if (first.Message == UnreadableMessage) throw new ArchiveEditException(FailedMessage, first);
            throw;
        }
    }

    /// <summary>원래 압축 파일이 진짜 검사기의 검사(목록 + 모든 항목 확인값)에 틀리는가 — 고치다 실패했을 때만 부른다. 취소는 그대로 던진다.</summary>
    internal static bool OriginalFails(string original, EditableFormat format, IReadOnlyCollection<string> originalKeys, string? password, CancellationToken ct) =>
        OriginalProblem(original, format, originalKeys, password, ct) is not null;

    /// <summary>
    /// 원래 압축 파일이 진짜 검사기의 검사에 틀리면 화면 문구 — 일집(풀기 엔진)이 못 푸는 방식이면 <see cref="UnreadableMessage"/>(손상이 아님 — W2-06 검증 지적 2차),
    /// 그 밖은 <see cref="OriginalDamagedMessage"/>. 맞으면 null. 고치다 실패했을 때만 부른다. 취소는 그대로 던진다.
    /// </summary>
    internal static string? OriginalProblem(string original, EditableFormat format, IReadOnlyCollection<string> originalKeys, string? password, CancellationToken ct)
    {
        try
        {
            Instance.Test(original, format, originalKeys, password, ct, null);
            return null;
        }
        catch (ArchiveEditException ex)
        {
            ct.ThrowIfCancellationRequested();
            return ex.Message == UnreadableMessage ? UnreadableMessage : OriginalDamagedMessage;
        }
    }

    /// <summary>
    /// ZIP 의 구조(머리·목록)는 맞는데 일집의 풀기 엔진(SharpCompress — 풀기와 같은 길)이 그 내용을 풀 수 없는 방식일 때(W2-06 검증 지적 2차 — 예: 한 번에 흘려 쓴
    /// ZipCrypto + 데이터 서술자 ZIP 은 SharpCompress 가 ZIP 으로 알아보지 못함, 일집이 모르는 압축 방식). 손상이 아니므로 '손상'이라고 하지 않는다.
    /// 일집 풀기도 같은 압축에서 실패하므로 '일집이 풀 수 없는 방식'이라고 알린다.
    /// </summary>
    internal const string UnreadableMessage =
        "이 압축 파일은 일집이 풀 수 없는 방식으로 만들어져 있어서 고치지 않았어요. 원래 압축 파일은 그대로예요. 다른 프로그램으로 풀어서 다시 압축하면 고칠 수 있어요.";

    /// <summary>
    /// ZIP: 구조(<see cref="ZipRawRewriter.VerifyStructure"/>) + 목록 비교 + 풀기와 같은 길로 모든 항목을 끝까지 풀어 CRC 확인. 풀어 본 바이트로 검사한 정도를 알린다
    /// (W2-06 검증 지적 2차 — 큰 ZIP 의 검사 동안 막대가 멈춰 보임). 구조가 맞는데 엔진이 그 ZIP 을 알아보지 못하거나 방식을 모르면 <see cref="UnreadableMessage"/>.
    /// </summary>
    private static void TestZip(string path, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken ct, Action<double>? onFraction)
    {
        ZipRawRewriter.Model model;
        using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
            model = ZipRawRewriter.VerifyStructure(s);
        CompareKeys(model.Entries.Select(e => e.Key), expectedKeys);

        var encrypted = model.Entries.Where(e => e.IsEncrypted && !e.IsDirectory).Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        bool hasPassword = !string.IsNullOrEmpty(password);
        bool Include(string name) => hasPassword || !encrypted.Contains(EditPlanner<object>.Normalize(name));

        // 구조는 맞는데 풀기 엔진이 ZIP 으로 알아보지 못하면(풀기도 'Cannot determine…' 으로 실패) 손상이 아니라 일집이 못 푸는 방식
        if (!SharpCompress.Archives.ArchiveFactory.IsArchive(path, out _))
            throw new ArchiveEditException(UnreadableMessage);

        long total = model.Entries.Where(e => !e.IsDirectory && (hasPassword || !e.IsEncrypted)).Sum(e => Math.Max(0, e.UncompressedSize));
        Action<long>? onTested = onFraction is null || total <= 0 ? null : tested => onFraction(Math.Clamp((double)tested / total, 0, 1));
        try
        {
            new ZipArchiveService().TestEntriesSync(path, hasPassword ? password : null, Include, ct, onTested);
        }
        catch (Exception ex) when (ex is NotSupportedException or UnsupportedMethodException or UnsupportedFormatException && !ct.IsCancellationRequested)
        {
            throw new ArchiveEditException(UnreadableMessage, ex);
        }
        onFraction?.Invoke(1);
    }

    /// <summary>
    /// 7z: 목록 비교 + 7z.dll 시험(Check). 7z.dll 에는 우리 읽기 스트림(<see cref="CancellableSeekableReadStream"/>)으로 넘겨, 큰 솔리드 7z 도
    /// 읽을 때마다 취소를 보고(20~80ms 안에 멈춤) 읽은 위치로 검사한 정도를 알린다(W2-06 검증 지적 — 경로로 넘기면 7z.dll 이 스스로 읽어 끝까지 못 멈춤).
    /// </summary>
    private static void TestSevenZip(string path, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken ct, Action<double>? onFraction)
    {
        SevenZipLibrary.ThrowIfUnavailable();
        ct.ThrowIfCancellationRequested();
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        // 검사한 정도 = 시험(Check)하는 동안 읽은 바이트 / 파일 크기(위치는 끝의 목록 머리로 뛰므로 쓰지 않음). 목록을 읽을 때는 알리지 않는다.
        long checkFrom = -1;
        CancellableSeekableReadStream? input = null;
        input = new CancellableSeekableReadStream(file, ct, onFraction is null ? null : (_, len) =>
        {
            if (checkFrom >= 0 && len > 0) onFraction(Math.Clamp((double)(input!.BytesRead - checkFrom) / len, 0, 1));
        });
        using var owned = input;
        if (input.Length == 0) throw new CorruptArchiveException();
        using var x = new SharpSevenZipExtractor(input, password ?? string.Empty, leaveOpen: true, InArchiveFormat.SevenZip);
        var names = x.ArchiveFileData.Select(f => EditPlanner<object>.Normalize(f.FileName ?? string.Empty)).ToList();
        CompareKeys(names, expectedKeys);
        ct.ThrowIfCancellationRequested();
        checkFrom = input.BytesRead;
        if (x.ArchiveFileData.Any(f => !f.IsDirectory) && !x.Check())
        {
            ct.ThrowIfCancellationRequested();   // 읽기에서 취소가 나면 Check 는 false 만 돌려준다
            throw new CorruptArchiveException();
        }
        onFraction?.Invoke(1);
    }

    /// <summary>두 목록이 (순서와 상관없이, 같은 이름 개수까지) 같은지. 다르면 예외(무엇이 다른지 로그용으로 담음).</summary>
    internal static void CompareKeys(IEnumerable<string> actual, IReadOnlyCollection<string> expected)
    {
        var a = actual.Select(EditPlanner<object>.Normalize).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var e = expected.Select(EditPlanner<object>.Normalize).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (a.SequenceEqual(e, StringComparer.Ordinal)) return;
        var missing = e.Except(a, StringComparer.Ordinal).Take(5);
        var extra = a.Except(e, StringComparer.Ordinal).Take(5);
        throw new InvalidDataException(
            $"고친 결과 목록이 계획과 다름(기대 {e.Count}개, 실제 {a.Count}개; 빠짐: {string.Join(", ", missing)}; 더 있음: {string.Join(", ", extra)})");
    }
}
