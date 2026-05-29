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

public sealed class CompressionOptions
{
    public CompressionLevelOption Level { get; init; } = CompressionLevelOption.Normal;

    /// <summary>비밀번호. null/빈 문자열이면 암호화 안 함.</summary>
    public string? Password { get; init; }

    /// <summary>비밀번호 사용 여부의 편의 프로퍼티.</summary>
    public bool HasPassword => !string.IsNullOrEmpty(Password);

    public static CompressionOptions Default => new();
}
