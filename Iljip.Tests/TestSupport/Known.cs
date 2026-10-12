namespace Iljip.Tests.TestSupport;

/// <summary>
/// Skip 사유 모음. 두 종류다.
///   - "known gap Ix-y: …" : 기능 대조표(D:\Python\_기능대조_알PDF_반디집.md)의 알려진 빈틈. 엔진 교체 후 Skip 만 지우면 통과해야 함.
///   - "bug Bn: …"         : 테스트 작성 중 발견한 본체 버그 중 아직 안 고친 것(고친 것은 Contract/FixedBugTests.cs).
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

    // ── 이번에 발견한 버그 ─────────────────────────
    // 모두 고쳤다(남은 Bn Skip 없음). B1·B2·B6·B7·B8·B9·B10 과 TAR-빈폴더는 W1-A(SharpCompress 0.50.5 업그레이드 + 서비스 우회),
    // B3·B4·B5 와 I2-11 은 W1-C(ZIP 암호 판정·CRC 검사·일집 암호 예외·ZipCrypto 선택) — 근거 테스트는 Contract/FixedBugTests.cs 로 옮겨
    // 기대를 뒤집었고, Skip 테스트는 Skip 을 지웠다. 새 버그를 찾으면 여기에 "bug Bn: …" 사유를 다시 둘 것.
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

    /// <summary>반디집 7z(일반·헤더 암호·한글 암호) — 빈 파일을 뺀 선택 해제 + 전체 해제(예전 B1, 고침)를 따로 시험.</summary>
    public static readonly string[] SevenZipBandizip = { "7z_default", "7z_header_encrypted", "7z_header_encrypted_korean_pw" };

    /// <summary>한글 암호(CP949 바이트) ZIP — 예전 B3, W1-C 에서 해제도 고침(BandizipFixtureTests.반디집_한글암호_ZIP이_풀린다).</summary>
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
        ListOk.Concat(ExtractOk).Concat(SevenZipBandizip).Concat(KoreanPasswordZip).Concat(Nfd)
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
