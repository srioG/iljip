using Iljip.Models;

namespace Iljip.Views;

/// <summary>
/// 압축 옵션 창(<see cref="CompressionOptionsDialog"/>)의 칸 보이기·결과 만들기 규칙(W2-02) — 순수 함수라 창을 띄우지 않고 시험한다.
/// ZIP: 암호 + 암호 방식(AES-256·ZipCrypto) + 한글 암호 호환 경고. 7Z: 암호(AES-256, UTF-16 이라 한글 암호도 반디집·7-Zip 과 호환 — 경고 없음)
/// + 솔리드 + 파일 이름도 암호로 숨기기(암호가 있을 때만). 그 밖(TAR·GZ·BZ2): 암호 칸을 끔.
/// </summary>
internal static class CompressionOptionsRules
{
    /// <summary>창의 칸 모양.</summary>
    /// <param name="PasswordEnabled">암호 칸을 쓸 수 있는가.</param>
    /// <param name="ShowZipEncryption">ZIP 암호 방식 칸을 보이는가.</param>
    /// <param name="ShowSevenZipOptions">7Z 전용 칸(솔리드·파일 이름 암호화)을 보이는가.</param>
    /// <param name="Hint">아래 안내 문구.</param>
    internal sealed record Layout(bool PasswordEnabled, bool ShowZipEncryption, bool ShowSevenZipOptions, string Hint);

    /// <summary>수준 칸의 글자(차례 = <see cref="LevelOf"/> 의 순번).</summary>
    internal static readonly string[] LevelLabels = { "저장만 (압축 안 함, 가장 빠름)", "빠름", "보통 (권장)", "최대 (가장 느림)" };

    public static bool IsZip(string? targetExtension) => string.Equals(targetExtension, ".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsSevenZip(string? targetExtension) => string.Equals(targetExtension, ".7z", StringComparison.OrdinalIgnoreCase);

    public static Layout LayoutFor(string? targetExtension)
    {
        if (IsZip(targetExtension))
            return new Layout(true, true, false, "* AES-256 에 한글 암호를 쓰면 반디집·알집에서 안 풀릴 수 있어요 — 영문·숫자를 권장해요.");
        if (IsSevenZip(targetExtension))
            return new Layout(true, false, true,
                "* 7Z 는 AES-256 으로 암호를 걸어요. 한글 암호도 반디집·7-Zip 에서 풀려요.\n" +
                "* '파일 이름도 암호로 숨기기'를 켜면 암호 없이는 목록도 안 보여요.");
        return new Layout(false, false, false,
            $"* {targetExtension} 포맷은 비밀번호를 지원하지 않아요.\n  ZIP이나 7Z를 쓰면 암호를 걸 수 있어요.");
    }

    /// <summary>'파일 이름도 암호로 숨기기'를 고를 수 있는가 — 7Z 이고 암호를 넣었을 때만.</summary>
    public static bool CanEncryptFileNames(string? targetExtension, string? password) =>
        IsSevenZip(targetExtension) && !string.IsNullOrEmpty(password);

    /// <summary>한글 암호 호환 경고(<see cref="Iljip.Services.PasswordPolicy.CheckCompat"/>)를 볼 형식인가 — ZIP 만(7Z 암호는 UTF-16 이라 반디집과 호환).</summary>
    public static bool ChecksPasswordCompat(string? targetExtension) => IsZip(targetExtension);

    public static CompressionLevelOption LevelOf(int index) => index switch
    {
        0 => CompressionLevelOption.Store,
        1 => CompressionLevelOption.Fastest,
        2 => CompressionLevelOption.Normal,
        3 => CompressionLevelOption.Maximum,
        _ => CompressionLevelOption.Normal,
    };

    /// <summary>창에서 고른 값으로 옵션을 만든다. 형식에 맞지 않는 값은 버린다(TAR 의 암호, ZIP 의 솔리드, 암호 없는 파일 이름 암호화 등).</summary>
    public static CompressionOptions Build(int levelIndex, string? password, string? targetExtension, ZipEncryption encryption, bool solid, bool encryptFileNames)
    {
        bool hasPassword = LayoutFor(targetExtension).PasswordEnabled && !string.IsNullOrEmpty(password);
        bool sevenZip = IsSevenZip(targetExtension);
        return new CompressionOptions
        {
            Level = LevelOf(levelIndex),
            Password = hasPassword ? password : null,
            Encryption = IsZip(targetExtension) ? encryption : ZipEncryption.Aes256,
            Solid = !sevenZip || solid,
            EncryptFileNames = sevenZip && hasPassword && encryptFileNames,
        };
    }
}
