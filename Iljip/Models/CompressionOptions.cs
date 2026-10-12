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

    public static CompressionOptions Default => new();
}
