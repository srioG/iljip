namespace Iljip.Models;

public enum CompressionLevelOption
{
    /// <summary>저장만 (압축 안 함, 가장 빠름)</summary>
    Store,
    /// <summary>빠름</summary>
    Fastest,
    /// <summary>보통 (기본값)</summary>
    Normal,
    /// <summary>최대 압축 (가장 느림)</summary>
    Maximum
}

/// <summary>ZIP 암호 방식(I2-11). ★ 'ZipCrypto' 라는 이름은 이 enum 에만 쓸 것 — KnownGap 테스트가 리플렉션으로 이 이름의 값을 가진 속성을 찾는다.</summary>
public enum ZipEncryption
{
    /// <summary>WinZip AES-256(기본, 안전). 한글 암호는 UTF-8 로 쓰여 반디집·알집에서 안 풀릴 수 있다(화면이 경고).</summary>
    Aes256,
    /// <summary>전통 ZipCrypto(호환 — Windows 탐색기·오래된 프로그램). 보안은 약하다. 한글 암호는 CP949 로 써서 반디집과 호환.</summary>
    ZipCrypto,
}

public sealed class CompressionOptions
{
    public CompressionLevelOption Level { get; init; } = CompressionLevelOption.Normal;

    /// <summary>비밀번호. null/빈 문자열이면 암호화 안 함.</summary>
    public string? Password { get; init; }

    /// <summary>비밀번호 사용 여부의 편의 프로퍼티.</summary>
    public bool HasPassword => !string.IsNullOrEmpty(Password);

    /// <summary>ZIP 암호 방식. 암호가 있을 때만 의미가 있음. ZIP 만 씀. 기본 AES-256.</summary>
    public ZipEncryption Encryption { get; init; } = ZipEncryption.Aes256;

    /// <summary>
    /// 7Z 솔리드 압축(여러 파일을 한 덩어리로 — 더 작아지지만 일부만 꺼낼 때는 느림). 7Z 만 씀. 기본 켬(7-Zip·반디집 기본과 같음) — W2-02.
    /// </summary>
    public bool Solid { get; init; } = true;

    /// <summary>
    /// 7Z 파일 이름도 암호로 숨기기(헤더 암호 — 암호 없이는 목록도 안 보임). 7Z 이고 암호가 있을 때만 의미가 있음. 기본 끔 — W2-02.
    /// </summary>
    public bool EncryptFileNames { get; init; }

    /// <summary>
    /// 나눠 만들기(분할 압축) 조각 크기(바이트). null 이면 나누지 않음 — W2-03(I2-2 만들기). ZIP·7Z 만 씀(TAR·GZ·BZ2 는 무시).
    /// ZIP 은 PKZIP 나눔(이름.z01 … 이름.zip), 7Z 는 숫자 조각(이름.7z.001 …). 다 만든 결과가 이 크기 이하면 나누지 않고 보통 파일 하나로 둔다.
    /// 화면은 1MB 이상만 받는다(서비스는 1바이트 이상이면 받음 — 시험은 작은 값을 직접 넣음).
    /// </summary>
    public long? SplitBytes { get; init; }

    public static CompressionOptions Default => new();

    // ───── W2-07(쓸모없는 파일 빼기 I3-6)에서 더한 것 ─────

    /// <summary>
    /// 담지 않을 것(빼기 목록 — <see cref="Iljip.Services.PatternFileFilter"/>). null 이면 다 담는다(기본 — 서비스를 바로 부르는 쪽은 예전 그대로).
    /// 폴더 안에서 찾은 것에만 적용(사용자가 직접 고른 파일·폴더는 늘 담음). ZIP·7Z·TAR 만 씀(GZ·BZ2 는 고른 파일 하나뿐).
    /// 뺀 것은 <see cref="CompressResult.ExcludedPaths"/>.
    /// </summary>
    public Iljip.Services.IFileFilter? ExcludeFilter { get; init; }

    /// <summary>빼기 목록만 바꾼 사본(나머지 값은 그대로).</summary>
    public CompressionOptions WithExcludeFilter(Iljip.Services.IFileFilter? filter) => new()
    {
        Level = Level,
        Password = Password,
        Encryption = Encryption,
        Solid = Solid,
        EncryptFileNames = EncryptFileNames,
        SplitBytes = SplitBytes,
        ExcludeFilter = filter,
    };
}
