using System.IO;
using ICSharpCode.SharpZipLib.Checksum;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>압축 검사에서 찾은 문제 하나. <see cref="Entry"/> 가 빈 문자열이면 압축 파일 전체의 문제(열기·구조·조각), 아니면 그 항목.</summary>
/// <param name="Entry">문제 항목('/' 구분) — 전체 문제면 빈 문자열.</param>
/// <param name="Reason">쉬운 한국어 이유(영어 없음).</param>
/// <param name="Detail">'자세히' 아래 원문(예외 형식·엔진 문구). 없으면 null.</param>
/// <param name="Kind">문제 종류(W2-08 검증 지적 — 'ZIP 고치기 시도'는 손상·잘림·검사값 불일치일 때만 권한다. 풀 수 없는 방식·조각 빠짐에는 권하지 않음).</param>
public sealed record TestProblem(string Entry, string Reason, string? Detail = null, ProblemKind Kind = ProblemKind.Corrupt);

/// <summary>
/// 압축 검사 결과(W2-08 — I2-7). <see cref="Ok"/> 면 '문제없어요. 파일 128개를 확인했어요.', 아니면 문제 항목과 이유.
/// </summary>
public sealed class TestReport
{
    /// <summary>문제없이 확인한 파일 수(폴더 빼고).</summary>
    public int CheckedCount { get; init; }

    /// <summary>찾은 문제(없으면 비어 있음).</summary>
    public IReadOnlyList<TestProblem> Problems { get; init; } = Array.Empty<TestProblem>();

    /// <summary>문제가 하나도 없는가.</summary>
    public bool Ok => Problems.Count == 0;

    /// <summary>항목 하나하나의 문제 수(전체 문제는 빼고).</summary>
    public int ProblemEntryCount => Problems.Count(p => p.Entry.Length > 0);

    /// <summary>
    /// 한 줄 요약: '문제없어요. 파일 128개를 확인했어요.' / '문제가 있어요. 파일 10개 중 2개에 문제가 있어요.' / '문제가 있어요.'
    /// 압축 파일 전체의 문제(잘림·솔리드라 뒤를 못 봄 등)도 있으면 전체 파일 수를 알 수 없어 '확인한 파일 3개 중 1개'(W2-08 검증 지적 — 잘린 EGG 가
    /// '파일 3개 중'으로 원래 4개인 것처럼 안 보이게).
    /// </summary>
    public string Headline
    {
        get
        {
            if (Ok) return $"문제없어요. 파일 {CheckedCount}개를 확인했어요.";
            int bad = ProblemEntryCount;
            if (bad == 0) return "문제가 있어요.";
            return Problems.Any(p => p.Entry.Length == 0)
                ? $"문제가 있어요. 확인한 파일 {CheckedCount + bad}개 중 {bad}개에 문제가 있어요."
                : $"문제가 있어요. 파일 {CheckedCount + bad}개 중 {bad}개에 문제가 있어요.";
        }
    }

    /// <summary>'ZIP 고치기 시도'를 권할 만한가 — 손상·잘림·검사값 불일치·암호가 틀렸거나 손상(가를 수 없음)이 하나라도 있을 때.</summary>
    public bool HasDamage => Problems.Any(p => p.Kind is ProblemKind.Corrupt or ProblemKind.Truncated or ProblemKind.ChecksumMismatch or ProblemKind.WrongPassword);

    /// <summary>첫 화면 글(요약 + '이름 — 이유' + 전체 문제). '자세히'(원문)는 넣지 않는다.</summary>
    public string ToDisplayText()
    {
        var lines = new List<string> { Headline };
        if (Ok) return Headline;
        var items = Problems.Where(p => p.Entry.Length > 0).ToList();
        if (items.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("문제가 있는 파일:");
            foreach (var p in items.Take(MaxLines)) lines.Add($"- {ErrorMessages.DisplayName(p.Entry)} — {p.Reason}");
            if (items.Count > MaxLines) lines.Add($"외 {items.Count - MaxLines}개");
        }
        foreach (var p in Problems.Where(p => p.Entry.Length == 0))
        {
            lines.Add(string.Empty);
            lines.Add(p.Reason);
        }
        if (items.Count == 0 && CheckedCount > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"그 밖의 파일 {CheckedCount}개는 문제없었어요.");
        }
        return string.Join("\n", lines);
    }

    /// <summary>'자세히' 아래 글(문제마다 원문). 없으면 빈 문자열.</summary>
    public string DetailText() =>
        string.Join("\n\n", Problems.Where(p => !string.IsNullOrEmpty(p.Detail)).Select(p => p.Entry.Length > 0 ? $"[{p.Entry}]\n{p.Detail}" : p.Detail!));

    /// <summary>예외 문구용 한 문단(만든 압축의 시험 실패 — <see cref="ArchiveTestFailedException"/>).</summary>
    internal string ProblemSummary()
    {
        var items = Problems.Where(p => p.Entry.Length > 0).ToList();
        var whole = Problems.Where(p => p.Entry.Length == 0).Select(p => p.Reason).ToList();
        var parts = new List<string>();
        if (items.Count > 0)
        {
            string names = string.Join(", ", items.Take(3).Select(p => $"‘{ErrorMessages.DisplayName(p.Entry)}’({p.Reason})"));
            parts.Add(items.Count > 3 ? $"파일 {items.Count}개에 문제가 있어요: {names} 외 {items.Count - 3}개." : $"파일 {items.Count}개에 문제가 있어요: {names}.");
        }
        parts.AddRange(whole);
        return parts.Count > 0 ? string.Join(" ", parts) : CorruptArchiveException.DefaultMessage;
    }

    private const int MaxLines = 20;

    /// <summary>압축 파일 전체의 문제 하나로 끝난 결과(앞서 확인한 파일 수 포함).</summary>
    internal static TestReport ForArchive(Exception ex, int checkedCount = 0)
    {
        var p = ErrorCatalog.Describe(ex);
        return new TestReport
        {
            CheckedCount = checkedCount,
            Problems = new[] { new TestProblem(string.Empty, p.Message, ErrorCatalog.DetailsOf(ex), p.Kind) },
        };
    }
}

/// <summary>
/// 압축 검사(W2-08 — I2-7 '시험하기'): 풀지 않고(디스크에 쓰지 않음) 모든 항목을 끝까지 읽어 확인값(CRC 등)·크기를 본다. 진행·취소가 된다.
/// 화면(☰ ▸ 시험하기·목록 오른쪽 클릭·'--test')과 방금 만든 압축의 시험(<see cref="CreatedArchiveTester"/>)이 같이 쓴다.
/// 시험은 <see cref="ArchiveTestService"/>(진짜)를 그대로 쓴다 — 바깥(진행 창·암호 창)만 가짜.
/// </summary>
public interface IArchiveTestService
{
    /// <summary>
    /// 검사한다. 문제(손상·잘림·검사값 불일치·조각 없음 등)는 결과(<see cref="TestReport.Problems"/>)로 돌려준다.
    /// 암호가 필요하거나 틀리면 일집 암호 예외(<see cref="ArchivePasswordException"/> — 화면이 암호를 묻고 다시 부름),
    /// 취소면 <see cref="OperationCanceledException"/>, 형식 미지원·7z.dll 없음·사용자 환경 문제(권한·다른 프로그램·없는 파일)는 그 예외.
    /// </summary>
    Task<TestReport> TestAsync(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>형식별 검사(서비스가 구현 — 디스크에 쓰지 않음). 문제는 결과로, 암호·취소·환경 문제는 예외로.</summary>
internal interface IArchiveTestable
{
    TestReport TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// 기본 압축 검사. 처리기는 열기와 같게 고른다(확장자로, 모르면 내용으로 — 자동 풀림 exe·이름이 바뀐 압축도). 형식마다:
/// <list type="bullet">
/// <item>ZIP·TAR·GZ·BZ2(SharpCompress): 풀기와 같은 준비(한글 암호 바이트·WinZip AES·CRC 확인)로 항목을 끝까지 읽기만 한다. 항목 하나가 깨져도 다음 항목을 계속 본다.</item>
/// <item>7z·RAR·ZIPX·CAB·LZH·ISO·WIM·XZ·자동 풀림 7z/RAR(7z.dll): 7z.dll 시험(Check)으로 모두 읽고, 실패라고 한 항목은 솔리드가 아니면 그 항목만
///   다시 꺼내(쓰지 않고 길이·확인값만 셈) 7z.dll 의 거짓 오류(LZH·ISO 실측)를 가린다. 솔리드면 '손상된 부분이 있어요'.</item>
/// <item>압축된 TAR: gzip·bzip2 는 바깥 압축을 흘려 풀며 안쪽 TAR 를 읽는다(디스크에 쓰지 않음 — W2-08 검증 지적). xz 는 목록용 임시 파일(일집 임시 폴더 —
///   목록 보기와 같은 것, 압축 파일 옆이 아님)로 풀고 안쪽 TAR 를 검사한다. 목록 보기로 이미 풀어 둔 것이 있으면 그것을 쓴다.</item>
/// <item>EGG·ALZ: 항목마다 끝까지 풀어 크기·CRC 를 본다. 끝 표시까지 구조를 따라가 보고 잘렸으면(EggDotNet 이 목록에서 말없이 뺀 뒤쪽 항목) 전체 문제로 더한다.</item>
/// </list>
/// </summary>
public sealed class ArchiveTestService : IArchiveTestService
{
    public static readonly ArchiveTestService Instance = new();

    private readonly ArchiveServiceLocator _locator;

    public ArchiveTestService(ArchiveServiceLocator? locator = null) => _locator = locator ?? new ArchiveServiceLocator();

    public Task<TestReport> TestAsync(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Test(archivePath, password, progress, cancellationToken), cancellationToken);

    /// <summary>검사 본체(동기). <paramref name="service"/> 를 주면 그 처리기로(만든 압축 — 마지막 확장자로 고른 처리기).</summary>
    internal TestReport Test(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken,
        IArchiveService? service = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 나눔 ZIP 의 앞 조각('이름.z01')을 고르면 열기처럼 마지막 조각(.zip)으로 본다 — 예전엔 조각 하나만 보고 멀쩡한 세트를 '손상'이라 했다(I4-1 검증 지적)
        if (service is null) archivePath = ArchiveVolumes.OpenTargetFor(archivePath);
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("압축 파일을 찾을 수 없어요.", archivePath); // I4-1(A2): 화면·로그에 영어 문구가 나가지 않게
        service ??= _locator.ResolveByContent(archivePath) ?? throw new UnsupportedFormatException(Path.GetExtension(archivePath));
        if (service is not IArchiveTestable testable) throw new UnsupportedFormatException(Path.GetExtension(archivePath));
        string? pw = string.IsNullOrEmpty(password) ? null : password;
        try
        {
            var report = testable.TestArchive(archivePath, pw, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return report;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsArchiveProblem(ex))
        {
            Logger.LogError($"압축 검사에서 문제: {archivePath}", ex);
            return TestReport.ForArchive(ex);
        }
    }

    /// <summary>
    /// 압축 파일 자체의 문제(결과에 적을 것)인가 — 손상·잘림·검사값·조각·풀 수 없는 방식·모르는 엔진 오류.
    /// 암호(다시 물음)·취소·형식 미지원·7z.dll 없음·사용자 환경(권한·다른 프로그램·공간·없는 파일, 그리고 운영 체제가 알린 그 밖의 입출력 오류 —
    /// 네트워크가 끊김·USB 를 뺌 등, W2-08 검증 지적)은 아니다(예외 그대로 — 화면이 '시험하지 못했어요'로 알리고 고치기를 권하지 않음).
    /// </summary>
    internal static bool IsArchiveProblem(Exception ex) =>
        ex is not OperationCanceledException
        && !ErrorCatalog.IsEnvironmentProblem(ex)
        && ErrorCatalog.Describe(ex).Kind is ProblemKind.Corrupt or ProblemKind.Truncated or ProblemKind.ChecksumMismatch
            or ProblemKind.VolumeMissing or ProblemKind.VolumeIncomplete or ProblemKind.UnsupportedMethod or ProblemKind.UnsafePath
            or ProblemKind.Other or ProblemKind.Unknown;

    /// <summary>항목 하나의 문제 이유(항목 이름은 따로 보이므로 빼고 짧게).</summary>
    internal static string EntryReason(Exception ex)
    {
        var p = ErrorCatalog.Describe(ex);
        return p.Kind switch
        {
            ProblemKind.ChecksumMismatch => "내용이 원래와 달라요(검사값 불일치)",
            ProblemKind.Truncated => "끝부분이 잘려 있어 끝까지 읽지 못했어요",
            ProblemKind.Corrupt => "손상돼서 끝까지 읽지 못했어요",
            ProblemKind.WrongPassword => "암호가 틀렸거나 손상돼서 읽지 못했어요",
            ProblemKind.UnsupportedMethod => ErrorCatalog.UnsupportedMethodName(ex) is { } m
                ? $"이 압축 방식({m})은 일집이 아직 풀 수 없어 확인하지 못했어요"
                : UnsupportedMethodReason,
            _ => p.Message,
        };
    }

    /// <summary>일집이 풀 수 없는 압축 방식인 항목의 이유(손상이 아님 — 고치기를 권하지 않는다).</summary>
    internal const string UnsupportedMethodReason = "이 압축 방식은 일집이 아직 풀 수 없어 확인하지 못했어요";

    /// <summary>솔리드 압축(7z 등)이 깨졌을 때의 이유 — 어느 파일이 망가졌는지 하나하나 가려낼 수 없다.</summary>
    internal const string SolidDamagedMessage = "손상된 부분이 있어요. 여러 파일을 한 덩어리로 압축한 파일(솔리드)이라 어느 파일이 망가졌는지 하나하나 가려낼 수는 없어요.";
}

/// <summary>
/// 쓰지 않고 길이·확인값(CRC32, 필요하면 LZH 의 CRC-16)만 세는 쓰기 스트림(압축 검사 — 7z.dll 이 실패라고 한 항목을 다시 꺼내 볼 때).
/// 쓸 때마다 취소를 본다(7z.dll 이 HRESULT 로 받아 곧 멈춘다).
/// </summary>
internal sealed class MeasuringStream : Stream
{
    private readonly CancellationToken _ct;
    private readonly bool _crc16;
    private readonly Crc32 _crc = new();
    private uint _crc16Value;

    public MeasuringStream(CancellationToken ct, bool crc16 = false)
    {
        _ct = ct;
        _crc16 = crc16;
    }

    public long Written { get; private set; }
    public uint Crc32Value => (uint)_crc.Value;
    public uint Crc16Value => _crc16Value;

    public override void Write(byte[] buffer, int offset, int count)
    {
        _ct.ThrowIfCancellationRequested();
        _crc.Update(new ArraySegment<byte>(buffer, offset, count));
        if (_crc16)
        {
            uint c = _crc16Value;
            for (int i = 0; i < count; i++)
            {
                c ^= buffer[offset + i];
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xA001 : c >> 1;
            }
            _crc16Value = c;
        }
        Written += count;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Written;
    public override long Position { get => Written; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// I4-1: 검사 한 번의 '항목 문제' 로그 상한 — 처음 <see cref="MaxDetailed"/>개만 스택까지 남기고, 그 뒤는 끝에 '그 밖 N개 항목 문제' 한 줄
/// (예전에는 깨진 압축 하나가 항목마다 스택 전체를 남겨 하루 로그가 수백 MB 가 됐다).
/// </summary>
internal sealed class TestProblemLog
{
    public const int MaxDetailed = 20;
    private readonly string _archivePath;
    private int _count;

    public TestProblemLog(string archivePath) => _archivePath = archivePath;

    /// <summary>지금까지 센 항목 문제 수.</summary>
    public int Count => _count;

    public void Entry(string path, Exception ex)
    {
        _count++;
        if (_count <= MaxDetailed) Logger.LogError($"압축 검사 — 항목 문제: {_archivePath} / {path}", ex);
    }

    /// <summary>검사가 끝날 때(성공·실패 모두) 한 번 — 상한을 넘었으면 남은 수만 한 줄.</summary>
    public void Finish()
    {
        if (_count > MaxDetailed) Logger.LogNote($"압축 검사 — 그 밖 {_count - MaxDetailed}개 항목 문제(기록 생략): {_archivePath}");
    }
}
