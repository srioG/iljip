namespace Iljip.Tests.TestSupport;

/// <summary>
/// Skip 사유 모음. 두 종류다.
///   - "known gap Ix-y: …" : 기능 대조표(D:\Python\_기능대조_알PDF_반디집.md)의 알려진 빈틈. 엔진 교체 후 Skip 만 지우면 통과해야 함.
///   - "bug Bn: …"         : 이번 테스트 작성 중 발견한 본체 버그(본 코드 동결이라 고치지 않고 보고만 함).
/// 테스트 본문은 '고쳐졌을 때의 올바른 기대'로 쓰여 있다.
/// </summary>
public static class Known
{
    // ── 알려진 빈틈 ──────────────────────────────────────────────
    // I1-7(헤더 암호 7z/RAR 를 열 때 암호를 안 물음)은 자동 테스트가 없다 — 빈틈이 화면(MainViewModel.OpenArchiveAsync 가
    // 암호 없이 ListEntriesAsync 만 부르고 재시도 안 함)에 있어 창 없이 시험할 수 없다. MANUAL_CHECKLIST 2절로 사람이 확인.
    // 서비스 쪽(암호 주면 목록 OK, 안 주면 암호 예외)은 PasswordTests 가 고정. RAR 헤더 암호는 픽스처가 없어 미검증.
    // README 의 IUserInteraction 주입이 생기면 가짜 암호 창으로 '열기 → 암호 요청 → 15개 목록'을 시험할 것.
    public const string I2_1 = "known gap I2-1: 7z 압축 생성 불가 — SevenZipArchiveService.CompressAsync 가 NotSupportedException";
    public const string I2_2 = "known gap I2-2: .7z.001/.zip.001 숫자 분할 확장자 미등록 — Resolve 가 null (SharpCompress 로 강제로 열면 열리긴 함)";
    public const string I2_9 = "known gap I2-9: 미등록 형식(.zipx/.tgz/.tar.gz/.txz/.xz/.tbz2/.iso/.lzh/.exe SFX) — Resolve 가 null 이거나 .tar.gz 는 GZ 처리기가 이름 빈 항목 하나만 봄";
    public const string I2_10 = "known gap I2-10: 맥 ZIP 의 NFD(자모 분리) 한글 이름을 NFC 로 바꾸지 않음 — 목록·풀린 파일 이름이 자모 분리 그대로";
    public const string I2_11 = "known gap I2-11: ZIP 암호 방식 선택 불가(AES-256 고정) — CompressionOptions 에 ZipCrypto 선택이 없음";
    public const string TarEmptyDir = "known gap TAR-빈폴더: TarArchiveService 가 빈 폴더를 담지 않음(코드 NOTE(잔존)에 명시된 한계, 대조표 ID 없음)";

    // ── 이번에 발견한 버그 ──────────────────────────────────────
    public const string B1 = "bug B1: 7-Zip/반디집이 만든 7z 에 빈(0바이트) 파일이 있으면 ExtractAsync 가 InvalidOperationException('File does not have a stream.') 로 실패 — 빈 파일은 안 생기고 오류 창이 뜸";
    public const string B2 = "bug B2: 원본 이름(FNAME)이 없는 .gz(반디집 산출물·일집 자신이 만든 .gz 모두)는 목록 이름이 빈 문자열이고 ExtractAsync 가 InvalidDataException('안전하지 않은 경로: ') — 일집 GZ 왕복 불가";
    public const string B3 = "bug B3: 반디집(한국어 Windows)이 한글 암호로 만든 ZIP 은 암호 바이트가 CP949 인데 SharpCompress 는 UTF-8 로 바꿔 비교 — ZipCrypto 'The password did not match.', AES 'bad password' 로 못 풂";
    public const string B4 = "bug B4: ZIP 해제 때 CRC-32 를 검사하지 않음 — Store 방식 데이터 1바이트가 깨진 ZIP 을 오류 없이 '틀린 내용'으로 풀어 냄. " +
        "ZipCrypto 1바이트 암호 검사를 우연히 통과한 틀린 암호(약 1/256)도 첫 파일을 쓰레기 내용으로 '풀어' 남긴 뒤 다음 파일에서 암호 불일치로 멈춤";
    public const string B5 = "bug B5: 틀린 암호인데 MainViewModel.IsPasswordRelated 가 암호 문제로 못 알아보는 예외가 있음 — 7z 틀린 암호(DataErrorException 'Data Error'), ZipCrypto 1바이트 검사를 우연히 통과한 틀린 암호(약 1/256, ZlibException 'Bad state') → 다시 묻지 않고 오류 창으로 끝남";
    public const string B6 = "bug B6: TarArchiveService.CompressAsync 는 파일과 파일 사이에서만 취소를 확인 — 큰 파일을 쓰는 동안 누른 취소는 그 파일이 끝날 때까지 안 먹히고, 마지막 파일이면 아예 무시된 채 '완료'로 끝남";
    public const string B8 = "bug B8: 손상된 7z·egg·alz 를 열다 실패하면(목록·해제 모두) SharpCompress/EggDotNet 이 연 파일 핸들을 닫지 않아 GC 가 돌 때까지 원본이 잠김 — 손상 파일을 바로 지우거나 옮기면 '다른 프로세스가 사용 중'";
    public const string B7 ="bug B7: ThrottledArchiveProgress 가 _lastReportMs=long.MinValue 로 시작해 'now - long.MinValue' 가 오버플로(음수) → 중간 진행률을 하나도 통과시키지 않음 — 압축·해제 진행 창이 처음 상태로 멈춰 있다가 끝에서만 100%";
    public const string B9 =
        "bug B9: 크기 0 항목(폴더 항목·빈 파일)이 든 EGG — EggDotNet 0.3.0 은 그런 항목에 블록 머리가 없어 CompressedLength·Open() 이 NullReferenceException. " +
        "EggAlzArchiveService.ListEntriesAsync 가 모든 항목의 CompressedLength 를 읽어 목록부터 실패(파일을 아예 못 엶), 빈 파일은 해제도 실패. " +
        "(테스트 EGG 는 RawEgg 로 만든 것 — 진짜 알집이 빈 파일·빈 폴더를 이 모양으로 쓰는지는 알집이 없어 미확인)";
    public const string B10 =
        "bug B10: ALZ 의 CP949 한글 이름이 깨짐 — EggDotNet 0.3.0 은 ALZ 이름을 UTF-8 로만 읽어 CP949 바이트가 U+FFFD(�)로 바뀜(복구 불가). " +
        "알집은 ALZ 이름을 시스템 코드 페이지(CP949)로 씀(unalz 기준; 서비스 주석도 'ALZ는 CP949' 라고 가정) — 진짜 알집 ALZ 로는 미확인";
}

/// <summary>
/// 테스트 종류 표시(xUnit Trait). 엔진을 바꾼 직후에는 '현재 동작 기록' 테스트가 무더기로 실패하는 게 정상이라,
/// 계약(엔진과 무관하게 지켜야 할 것)만 먼저 보려면: <c>dotnet test ... --filter "Kind!=Record"</c>
/// </summary>
public static class Kinds
{
    public const string Key = "Kind";
    /// <summary>지금 엔진(SharpCompress 등)의 예외 타입·문구·목록 모양·방식 번호 같은 '현재 동작 기록'. 엔진이 바뀌면 새 값으로 고쳐도 된다.</summary>
    public const string Record = "Record";
}

/// <summary>
/// manifest.json 의 모든 픽스처가 어느 테스트 묶음에 속하는지 적는 곳.
/// FixtureCoverageTests 가 '빠진 픽스처 없음'을 검사한다 — 픽스처를 새로 만들면 여기에 넣을 것.
/// </summary>
public static class FixtureSets
{
    /// <summary>목록이 반디집 기대값과 정확히 같아야 하는 것(지금 통과).</summary>
    public static readonly string[] ListOk =
    {
        "zip_default", "zip_cp949", "zip_utf8", "zip_store_l0", "zip_storeroot_no",
        "zip_zipcrypto", "zip_zipcrypto_korean_pw", "zip_aes256", "zip_aes256_korean_pw", "zip_split",
        "7z_default", "7z_header_encrypted", "7z_header_encrypted_korean_pw", "py7z_pw_names_visible",
        "tar_default", "tar_utf8",
    };

    /// <summary>전체 해제 결과가 반디집 기대값과 정확히 같아야 하는 것(지금 통과).</summary>
    public static readonly string[] ExtractOk =
    {
        "zip_default", "zip_cp949", "zip_utf8", "zip_store_l0", "zip_storeroot_no",
        "zip_zipcrypto", "zip_aes256", "zip_split", "py7z_pw_names_visible",
        "tar_default", "tar_utf8",
    };

    /// <summary>반디집 7z — 목록은 맞고, 빈 파일을 뺀 선택 해제는 맞지만 전체 해제는 B1.</summary>
    public static readonly string[] SevenZipEmptyFileBug = { "7z_default", "7z_header_encrypted", "7z_header_encrypted_korean_pw" };

    /// <summary>한글 암호 ZIP — 목록은 맞지만 해제는 B3.</summary>
    public static readonly string[] KoreanPasswordZip = { "zip_zipcrypto_korean_pw", "zip_aes256_korean_pw" };

    /// <summary>NFD 이름 ZIP — 내용은 맞게 풀리지만 이름이 NFD 그대로(I2-10).</summary>
    public static readonly string[] Nfd = { "nfd_bandizip_default", "nfd_bandizip_utf8", "nfd_python_efs", "nfd_python_noflag" };

    /// <summary>숫자 분할(I2-2).</summary>
    public static readonly string[] NumberedSplit = { "7z_split", "zip_numbered_split" };

    /// <summary>미등록·미지원 형식(I2-9). 여러 파일이 든 묶음 형식만.</summary>
    public static readonly string[] UnsupportedMulti =
    {
        "zipx_default", "zipx_aes", "tgz_default", "targz_default", "txz_two_step", "tbz2_python", "iso_default", "lzh_default",
    };

    /// <summary>개별 테스트에서 따로 다루는 것.</summary>
    public static readonly string[] Individually = { "gz_single", "xz_single", "bz2_single_python", "sfx_default" };

    public static IEnumerable<string> AllCovered =>
        ListOk.Concat(ExtractOk).Concat(SevenZipEmptyFileBug).Concat(KoreanPasswordZip).Concat(Nfd)
              .Concat(NumberedSplit).Concat(UnsupportedMulti).Concat(Individually).Distinct();

    public static TheoryData<string> Data(string[] ids)
    {
        var d = new TheoryData<string>();
        foreach (var id in ids) d.Add(id);
        return d;
    }
}

/// <summary>git 제외(local/) 픽스처처럼 '없을 수도 있는' 파일이 없으면 자동으로 건너뛰는 Fact.</summary>
public sealed class FixtureFactAttribute : FactAttribute
{
    public FixtureFactAttribute(string relativePath)
    {
        try
        {
            if (!File.Exists(FixtureCatalog.PathOf(relativePath)))
                Skip = $"픽스처 파일 없음(로컬 전용, git 제외): {relativePath} — make_fixtures.py 로 다시 만들 수 있음(반디집 필요)";
        }
        catch (Exception ex)
        {
            Skip = "픽스처 폴더를 찾지 못함: " + ex.Message;
        }
    }
}
