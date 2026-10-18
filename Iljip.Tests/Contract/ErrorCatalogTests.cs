using System.Text.RegularExpressions;
using Iljip.Services;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 — I2-8] 오류 원인 구분표(<see cref="ErrorCatalog"/>): 예외마다 종류(<see cref="ProblemKind"/>)·쉬운 한국어 문구가 정해져 있다.
/// 입출력 오류는 코드(HResult)로 가른다 — 공간 부족(0x80070070·0x80070027)·경로가 너무 김(0x800700CE)·권한 없음(0x80070005)·다른 프로그램이 씀(0x80070020·0x80070021).
/// 영어 원문(형식·엔진 문구·코드·호출 위치)은 '자세히'(<see cref="UserProblem.Details"/>)에만. 창 없이 풀기(재사용장치)의 한 줄 문구에는 '압축 아님' 표시 말이 없다.
/// </summary>
public class ErrorCatalogTests
{
    private const int DiskFull = unchecked((int)0x80070070);
    private const int HandleDiskFull = unchecked((int)0x80070027);
    private const int FilenameTooLong = unchecked((int)0x800700CE);
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    /// <summary>재사용장치가 '압축 파일 아님'으로 분류하는 말(재사용장치_processor.py 491~493행 — ReuseDeviceContractTests 와 같게).</summary>
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    private static IOException Io(string message, int hresult) => new(message, hresult);

    /// <summary>(이름, 예외, 기대 종류, 기대 문구 — null 이면 문구는 따로 보지 않음)</summary>
    public static TheoryData<string, Exception, ProblemKind, string?> Table => new()
    {
        // ───── 사용자 환경(입출력) — 코드로 가름 ─────
        { "공간 부족(0x80070070)", Io("There is not enough space on the disk. : 'D:\\풀 곳\\큰 파일.bin'", DiskFull), ProblemKind.DiskFull, "저장할 공간이 부족해요." },
        { "공간 부족(0x80070027)", Io("Reached the end of the file.", HandleDiskFull), ProblemKind.DiskFull, "저장할 공간이 부족해요." },
        { "경로가 너무 김(형식)", new PathTooLongException("The specified path, file name, or both are too long."), ProblemKind.PathTooLong, "경로가 너무 길어요. 더 짧은 위치를 골라 주세요." },
        { "경로가 너무 김(0x800700CE)", Io("The filename or extension is too long.", FilenameTooLong), ProblemKind.PathTooLong, "경로가 너무 길어요. 더 짧은 위치를 골라 주세요." },
        { "권한 없음(형식)", new UnauthorizedAccessException(@"Access to the path 'C:\Windows\보고서.xlsx' is denied."), ProblemKind.AccessDenied, @"이 위치에 쓸 권한이 없어요: C:\Windows\보고서.xlsx" },
        { "권한 없음(0x80070005)", Io("Access is denied.", AccessDenied), ProblemKind.AccessDenied, "이 위치에 쓸 권한이 없어요." },
        { "다른 프로그램이 씀(0x80070020)", Io(@"The process cannot access the file 'C:\받은 파일\견적서.xlsx' because it is being used by another process.", SharingViolation),
            ProblemKind.InUse, @"다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요. 잠시 뒤 다시 해 주세요. (파일: C:\받은 파일\견적서.xlsx)" },
        { "다른 프로그램이 잠금(0x80070021)", Io("The process cannot access the file because another process has locked a portion of the file.", LockViolation),
            ProblemKind.InUse, "다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요. 잠시 뒤 다시 해 주세요." },
        { "없는 파일", new FileNotFoundException(@"Could not find file 'C:\받은 파일\없음.zip'."), ProblemKind.NotFound, @"파일이나 폴더를 찾을 수 없어요: C:\받은 파일\없음.zip" },
        // 서비스가 이미 쉬운 한국어로 쓴 공간 부족 문구는 살린다(필요한 크기 안내)
        { "공간 부족(한국어 문구)", Io("저장할 공간이 부족해요. 풀려면 1.2GB 정도가 필요해요.", DiskFull), ProblemKind.DiskFull, "저장할 공간이 부족해요. 풀려면 1.2GB 정도가 필요해요." },

        // ───── 취소·모르는 예외 ─────
        { "취소", new OperationCanceledException(), ProblemKind.Cancelled, "취소했어요." },
        { "작업 취소", new TaskCanceledException(), ProblemKind.Cancelled, "취소했어요." },
        { "모르는 예외", new InvalidOperationException("boom: Sequence contains no elements"), ProblemKind.Unknown, "알 수 없는 문제가 생겼어요." },
        { "모르는 입출력 코드", Io("The semaphore timeout period has expired.", unchecked((int)0x80070079)), ProblemKind.Unknown, "알 수 없는 문제가 생겼어요." },

        // ───── 일집 예외(문구가 이미 한국어) ─────
        { "손상", new CorruptArchiveException(), ProblemKind.Corrupt, CorruptArchiveException.DefaultMessage },
        { "잘림", CorruptArchiveException.Truncated(), ProblemKind.Truncated, CorruptArchiveException.TruncatedMessage },
        { "검사값 불일치", new ChecksumMismatchException("자료/견적서.txt"), ProblemKind.ChecksumMismatch, "‘자료/견적서.txt’의 내용이 원래와 달라요(검사값 불일치). 압축 파일이 손상됐을 수 있어요." },
        { "암호 필요", new PasswordRequiredException(), ProblemKind.PasswordRequired, PasswordRequiredException.DefaultMessage },
        { "목록 암호", new ListPasswordRequiredException(), ProblemKind.ListPasswordRequired, null },
        { "암호 틀림", new WrongPasswordException(), ProblemKind.WrongPassword, WrongPasswordException.DefaultMessage },
        { "조각 없음", new VolumeMissingException("자료.7z.002"), ProblemKind.VolumeMissing, "'자료.7z.002' 조각이 없어요. 나눠진 조각을 모두 같은 폴더에 두고 다시 해 주세요." },
        { "조각 덜 받아짐", new VolumeIncompleteException("자료.7z.003"), ProblemKind.VolumeIncomplete, "'자료.7z.003' 조각이 덜 받아졌어요. 다시 받아 주세요." },
        { "형식 미지원", new UnsupportedFormatException(".zst"), ProblemKind.UnsupportedFormat, "일집이 열 수 없는 형식이에요(.zst)." },
        { "방식 미지원", new UnsupportedMethodException("PPMd"), ProblemKind.UnsupportedMethod, "이 압축 방식(PPMd)은 아직 풀 수 없어요." },
        { "7z.dll 없음", new EngineUnavailableException(new DllNotFoundException("7z.dll")), ProblemKind.EngineUnavailable, EngineUnavailableException.DefaultMessage },

        // ───── 엔진 예외가 그대로 올라온 경우 — 영어 원문 대신 손상·잘림·암호 ─────
        { "SharpCompress 머리 못 찾음", new SharpCompress.Common.ArchiveException("Failed to locate the Zip Header"), ProblemKind.Corrupt, ErrorCatalog.EngineDataMessage },
        { "SharpCompress TAR 잘림", new SharpCompress.Common.IncompleteArchiveException("Failed to read TAR header"), ProblemKind.Truncated, CorruptArchiveException.TruncatedMessage },
        { "SharpCompress 암호", new SharpCompress.Common.CryptographicException("The password did not match."), ProblemKind.WrongPassword, ErrorCatalog.EnginePasswordMessage },
        { "SharpZipLib ZIP", new ICSharpCode.SharpZipLib.Zip.ZipException("Cannot find central directory"), ProblemKind.Corrupt, ErrorCatalog.EngineDataMessage },
        { "스트림 끝", new EndOfStreamException("Unable to read beyond the end of the stream."), ProblemKind.Truncated, CorruptArchiveException.TruncatedMessage },
        { "데이터 오류", new InvalidDataException("Data error"), ProblemKind.Corrupt, ErrorCatalog.EngineDataMessage },
    };

    /// <summary>계약: 예외마다 정해진 종류·문구. 첫 화면 문구는 한국어(영어 원문 없음), 원문은 '자세히'에.</summary>
    [Theory]
    [MemberData(nameof(Table))]
    public void 예외마다_종류와_쉬운_한국어_문구(string name, Exception ex, ProblemKind kind, string? message)
    {
        var p = ErrorCatalog.Describe(ex);
        Assert.True(kind == p.Kind, $"{name}: 종류 {p.Kind}(기대 {kind})");
        if (message is not null) Assert.Equal(message, p.Message);
        Assert.True(ErrorCatalog.ContainsHangul(p.Message), $"{name}: 문구에 한글 없음 — {p.Message}");
        Assert.False(string.IsNullOrEmpty(p.Title), name);
        Assert.True(ErrorCatalog.ContainsHangul(p.Title), $"{name}: 제목에 한글 없음 — {p.Title}");
        // 첫 화면 글(무엇을 하다가 + 문구 + 안내)에 영어 예외 형식·원문이 없다 — 따옴표 밖 경로를 빼면 영어 낱말이 남지 않는다
        string shown = (p with { Heading = "압축을 풀지 못했어요." }).ToDisplayText();
        Assert.DoesNotContain("Exception", shown);
        Assert.DoesNotContain(ex.GetType().Name, shown);
        if (!ErrorCatalog.ContainsHangul(ex.Message) && ex.Message.Length > 0)
            Assert.DoesNotContain(ex.Message, shown);
        // 원문은 '자세히'에: 형식 전체 이름 + 원래 문구 + 코드
        Assert.Contains(ex.GetType().FullName!, p.Details);
        Assert.Contains(ex.Message, p.Details);
        Assert.Contains("HResult: 0x" + unchecked((uint)ex.HResult).ToString("X8"), p.Details);
    }

    /// <summary>계약: 권한 없음엔 '관리자 권한으로 다시 시도'를 고를 수 있다(실제 동작은 W2-11 — 그 전까지 화면이 버튼을 거름). 다른 종류엔 없다.</summary>
    [Fact]
    public void 권한_없음엔_관리자_권한_버튼()
    {
        foreach (var ex in new Exception[] { new UnauthorizedAccessException("Access to the path 'C:\\a.txt' is denied."), Io("Access is denied.", AccessDenied) })
        {
            var p = ErrorCatalog.Describe(ex, @"C:\받은 파일\자료.zip");
            Assert.Equal(ProblemKind.AccessDenied, p.Kind);
            var action = Assert.Single(p.Actions);
            Assert.Equal(ErrorCatalog.ElevateActionId, action.Id);
            Assert.Equal("관리자 권한으로 다시 시도", action.Label);
            Assert.Equal(ErrorCatalog.AccessDeniedAdvice, p.Advice);
        }
        Assert.Empty(ErrorCatalog.Describe(Io("x", DiskFull)).Actions);
        Assert.Empty(ErrorCatalog.Describe(new InvalidOperationException("x")).Actions);
    }

    /// <summary>계약: 다른 프로그램이 쓰면 '보안 프로그램이 검사 중일 수 있어요' 안내, 공간 부족·모르는 예외에도 안내가 있다.</summary>
    [Fact]
    public void 안내_문구()
    {
        Assert.Contains("보안 프로그램이 검사 중일 수 있어요", ErrorCatalog.Describe(Io("x", SharingViolation)).Advice);
        Assert.Contains("보안 프로그램이 검사 중일 수 있어요", ErrorCatalog.Describe(Io("x", LockViolation)).Advice);
        Assert.Equal(ErrorCatalog.DiskFullAdvice, ErrorCatalog.Describe(Io("x", DiskFull)).Advice);
        var unknown = ErrorCatalog.Describe(new InvalidOperationException("boom"));
        Assert.Contains("로그 폴더 열기", unknown.Advice);
        Assert.Contains("boom", unknown.Details);
        Assert.DoesNotContain("boom", unknown.ToDisplayText());
    }

    /// <summary>계약: 'ZIP 고치기 시도'는 ZIP 이 손상·잘림·검사값 불일치일 때만(7z·암호·조각 없음·공간 부족에는 없음).</summary>
    [Theory]
    [InlineData("손상 zip", @"C:\받은 파일\자료.zip", true)]
    [InlineData("잘림 zip", @"C:\받은 파일\자료.ZIP", true)]
    [InlineData("검사값 zip", "자료.zip", true)]
    [InlineData("손상 7z", @"C:\받은 파일\자료.7z", false)]
    [InlineData("이름 없음", null, false)]
    public void ZIP_고치기는_손상된_ZIP에만(string name, string? archive, bool expected)
    {
        Exception ex = name.StartsWith("잘림") ? CorruptArchiveException.Truncated()
            : name.StartsWith("검사값") ? new ChecksumMismatchException("a.txt")
            : new SharpCompress.Common.ArchiveException("Failed to locate the Zip Header");
        var p = ErrorCatalog.Describe(ex, archive);
        Assert.Equal(expected, p.Actions.Any(a => a.Id == ErrorCatalog.RepairZipActionId));
        if (expected) Assert.Equal("ZIP 고치기 시도", Assert.Single(p.Actions).Label);

        foreach (var other in new Exception[] { new WrongPasswordException(), new VolumeMissingException("자료.z01"), Io("x", DiskFull), new UnsupportedFormatException(".zip") })
            Assert.DoesNotContain(ErrorCatalog.Describe(other, @"C:\받은 파일\자료.zip").Actions, a => a.Id == ErrorCatalog.RepairZipActionId);
    }

    /// <summary>
    /// 계약(재사용장치): 창 없이 풀기의 stderr 한 줄(<see cref="ErrorCatalog.HeadlessMessage"/>)은 한국어 한 줄 — 줄바꿈 없음, '압축 아님' 표시 말 없음,
    /// 영어 원문 없음. (형식 미지원의 영어 'Unsupported archive type'(종료 코드 2)은 App 이 따로 쓴다 — ReuseDeviceContractTests)
    /// </summary>
    [Theory]
    [MemberData(nameof(Table))]
    public void 창없이_풀기_한줄_문구는_압축아님_표시가_없다(string name, Exception ex, ProblemKind _, string? _1)
    {
        string line = ErrorCatalog.HeadlessMessage(ex);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        foreach (var marker in NotArchiveMarkers)
            Assert.True(line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0, $"{name}: '{marker}' 가 들어감 — {line}");
        Assert.True(ErrorCatalog.ContainsHangul(line), $"{name}: 한글 없음 — {line}");
        Assert.DoesNotContain("Exception", line);
    }

    /// <summary>계약: 예전 이름(<see cref="ErrorMessages"/>)은 구분표를 그대로 쓴다 — 모르는 예외의 영어 원문은 화면에 나오지 않는다.</summary>
    [Fact]
    public void 예전_이름도_같은_문구()
    {
        var ex = new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
        Assert.Equal(ErrorCatalog.UnknownMessage, ErrorMessages.ToUserMessage(ex));
        Assert.Equal(ErrorCatalog.EngineDataMessage, ErrorMessages.ToPlainMessage(ex));
        Assert.Equal(CorruptArchiveException.DefaultMessage, ErrorMessages.ToPlainMessage(new CorruptArchiveException()));
        Assert.DoesNotMatch(new Regex("[A-Za-z]{4,}"), ErrorMessages.ToUserMessage(new SharpCompress.Common.ArchiveException("Failed to locate the Zip Header")));
    }

    /// <summary>계약: '자세히' 원문에는 안쪽 예외(차례로)와 호출 위치(위 다섯 줄까지)가 들어간다.</summary>
    [Fact]
    public void 자세히에는_안쪽_예외와_호출_위치()
    {
        Exception caught;
        try
        {
            try
            {
                throw new InvalidDataException("Bad state (invalid block type)");
            }
            catch (Exception inner)
            {
                throw new CorruptArchiveException(inner, "자료/견적서.txt");
            }
        }
        catch (Exception ex)
        {
            caught = ex;
        }
        var p = ErrorCatalog.Describe(caught);
        Assert.Contains("Iljip.Services.CorruptArchiveException", p.Details);
        Assert.Contains("<- System.IO.InvalidDataException: Bad state (invalid block type)", p.Details);
        Assert.Contains(nameof(자세히에는_안쪽_예외와_호출_위치), p.Details);
        Assert.True(p.Details.Split('\n').Count(l => l.TrimStart().StartsWith("at ") || l.TrimStart().StartsWith("위치: ")) <= 5, p.Details);
        Assert.DoesNotContain("invalid block", p.ToDisplayText());
    }
}
