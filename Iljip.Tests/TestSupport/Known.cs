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
    // I1-7(헤더 암호 7z/RAR 를 열 때 암호를 안 물음): 7z 는 W1-D 1차 검증에서 화면(MainViewModel.OpenArchiveAsync 가 암호 예외면 묻고 다시 엶)을
    // 고쳐 Skip 사유가 없다 — 가짜 암호 창으로 CommandLineTests.헤더암호_7z…(열기·우클릭 풀기·틀린 암호·다른 압축을 열면 잊기)가 지킨다.
    // 서비스 쪽(암호 주면 목록 OK, 안 주면 암호 예외)은 PasswordTests 가 고정. RAR 머리 암호는 통합 점검(2026-10-12)에서 합성 RAR4·RAR5
    // (TestSupport/RawRar.cs, 반디집 bz.exe 로 작성기 확인)로 서비스·화면 모두 확인 — RarTests·FormatFlowTests. 진짜 WinRAR 산출물은 MANUAL_CHECKLIST 6절.
    // W2-01(7z.dll 해제 엔진)에서 I2-9(zipx·tgz·tar.gz·txz·tbz2·xz·iso·lzh·자동 풀림 exe)와 I2-2 의 '열기·풀기'(.7z.001·.zip.001)를 메워
    // 그 Skip 을 풀었다(I2_9 사유 삭제). I2-2 는 '분할 만들기' 쪽만 남았다(W2-03 몫 — 쓰는 곳이 없으면 W2-03 이 지움).
    // W2-02 에서 I2-1(7z 만들기)을 메워 KnownGapTests.압축_7z_왕복이_원본과_같다 의 Skip 을 풀었다(I2_1 사유 삭제).
    // 같은 묶음에서 I1-7 의 남은 몫(목록까지 잠긴 압축 전용 예외 ListPasswordRequiredException·열 때 '암호가 맞지 않아요' 경고)도 메움 — ListPasswordFlowTests.
    // W2-03 에서 I2-2 의 '나눠 만들기'(7Z .7z.001 …·ZIP PKZIP 나눔 .z01 … .zip)를 메웠다 — 쓰는 Skip 이 없어 I2_2 사유를 지웠다(새 계약은 Contract/SplitCreateTests.cs).
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

    /// <summary>숫자 분할(I2-2 의 풀기, W2-01 에서 고침).</summary>
    public static readonly string[] NumberedSplit = { "7z_split", "zip_numbered_split" };

    /// <summary>예전 미등록·미지원 형식(I2-9, W2-01 에서 고침). 여러 파일이 든 묶음 형식만.</summary>
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
