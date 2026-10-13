using Iljip.Models;

namespace Iljip.Views;

/// <summary>
/// 압축 옵션 창(<see cref="CompressionOptionsDialog"/>)의 칸 보이기·결과 만들기 규칙(W2-02) — 순수 함수라 창을 띄우지 않고 시험한다.
/// ZIP: 암호 + 암호 방식(AES-256·ZipCrypto) + 한글 암호 호환 경고. 7Z: 암호(AES-256, UTF-16 이라 한글 암호도 반디집·7-Zip 과 호환 — 경고 없음)
/// + 솔리드 + 파일 이름도 암호로 숨기기(암호가 있을 때만). 그 밖(TAR·GZ·BZ2): 암호 칸을 끔.
/// W2-03: ZIP·7Z 에 '나누기'(분할 압축 — 나누지 않음·10MB·20MB(메일 첨부용)·직접 입력 MB, 1MB 이상) 칸. TAR·GZ·BZ2 는 숨기고 값도 버린다.
/// </summary>
internal static class CompressionOptionsRules
{
    /// <summary>창의 칸 모양.</summary>
    /// <param name="PasswordEnabled">암호 칸을 쓸 수 있는가.</param>
    /// <param name="ShowZipEncryption">ZIP 암호 방식 칸을 보이는가.</param>
    /// <param name="ShowSevenZipOptions">7Z 전용 칸(솔리드·파일 이름 암호화)을 보이는가.</param>
    /// <param name="Hint">아래 안내 문구.</param>
    internal sealed record Layout(bool PasswordEnabled, bool ShowZipEncryption, bool ShowSevenZipOptions, string Hint)
    {
        /// <summary>'나누기'(분할 압축) 칸을 보이는가 — ZIP·7Z 만(W2-03). TAR·GZ·BZ2 는 숨김.</summary>
        public bool ShowSplit { get; init; }
    }

    /// <summary>수준 칸의 글자(차례 = <see cref="LevelOf"/> 의 순번).</summary>
    internal static readonly string[] LevelLabels = { "저장만 (압축 안 함, 가장 빠름)", "빠름", "보통 (권장)", "최대 (가장 느림)" };

    public static bool IsZip(string? targetExtension) => string.Equals(targetExtension, ".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsSevenZip(string? targetExtension) => string.Equals(targetExtension, ".7z", StringComparison.OrdinalIgnoreCase);

    /// <summary>자동 풀림 EXE(W2-10 — 안은 ZIP).</summary>
    public static bool IsSfx(string? targetExtension) => string.Equals(targetExtension, ".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>ZIP 암호 방식(AES-256·ZipCrypto)을 고르는 형식인가 — ZIP 과 자동 풀림 EXE.</summary>
    public static bool UsesZipEncryption(string? targetExtension) => IsZip(targetExtension) || IsSfx(targetExtension);

    /// <summary>
    /// 자동 풀림 EXE 를 고르면 형식 칸 바로 아래에 늘 보이는 안내(W2-10 — 회사 메일은 exe 첨부를 막는 경우가 많음).
    /// 만들기 직전에도 한 번 더 묻는다(<c>MainViewModel</c> — ☰ ▸ 설정에서 끌 수 있음).
    /// </summary>
    internal const string SfxMailNotice =
        "회사 메일은 실행 파일(.exe)을 막는 경우가 많아요. 받는 사람이 못 받을 수 있어요. 메일로 보낼 거면 ZIP을 권해요.";

    public static Layout LayoutFor(string? targetExtension)
    {
        if (IsZip(targetExtension))
            return new Layout(true, true, false, "* AES-256 에 한글 암호를 쓰면 반디집·알집에서 안 풀릴 수 있어요 — 영문·숫자를 권장해요.") { ShowSplit = true };
        if (IsSfx(targetExtension))
            return new Layout(true, true, false,
                "* 받는 사람 PC 에 일집·반디집이 없어도 두 번 눌러 풀 수 있어요(Windows 10·11). 한글 이름·한글 암호도 이 exe 로는 풀려요.\n" +
                "* 처음 받은 PC 에서는 'Windows의 PC 보호' 창이나 백신 경고가 뜰 수 있어요(서명 없는 새 실행 파일이라서). 회사 보안 설정에 따라 실행이 아예 막히기도 해요.\n" +
                "* 4GB 가 넘는 exe 는 Windows 에서 실행되지 않아 만들지 않아요 — 큰 자료는 ZIP으로 보내 주세요.");
        if (IsSevenZip(targetExtension))
            return new Layout(true, false, true,
                "* 7Z 는 AES-256 으로 암호를 걸어요. 한글 암호도 반디집·7-Zip 에서 풀려요.\n" +
                "* '파일 이름도 암호로 숨기기'를 켜면 암호 없이는 목록도 안 보여요.") { ShowSplit = true };
        return new Layout(false, false, false,
            $"* {targetExtension} 포맷은 비밀번호를 지원하지 않아요.\n  ZIP이나 7Z를 쓰면 암호를 걸 수 있어요.");
    }

    // ───── 나누기(분할 압축, W2-03) ─────

    /// <summary>1MB(나누기 크기의 단위 — 1,048,576바이트).</summary>
    internal const long MegaByte = 1024L * 1024;

    /// <summary>'나누기' 칸의 글자(차례 = <see cref="SplitPresetBytes"/>). 마지막은 직접 입력(MB).</summary>
    internal static readonly string[] SplitLabels = { "나누지 않음", "10MB", "20MB (메일 첨부용)", "직접 입력 (MB)" };

    /// <summary>'나누기' 칸 순번별 크기(null = 나누지 않음, 직접 입력은 칸의 값).</summary>
    internal static readonly long?[] SplitPresetBytes = { null, 10 * MegaByte, 20 * MegaByte, null };

    /// <summary>'직접 입력' 의 순번.</summary>
    internal const int SplitCustomIndex = 3;

    /// <summary>직접 입력의 가장 작은 값·가장 큰 값(MB). 큰 쪽은 USB(FAT32) 한 파일 한도 4GB 아래.</summary>
    internal const int SplitMinMegabytes = 1;
    internal const int SplitMaxMegabytes = 4095;

    internal const string SplitTooSmallMessage = "1MB 이상으로 넣어 주세요.";
    internal const string SplitTooLargeMessage = "4095MB 이하로 넣어 주세요.";
    internal const string SplitNotNumberMessage = "나눌 크기를 숫자(MB)로 넣어 주세요.";

    /// <summary>
    /// '나누기' 칸 아래 안내. 메일은 첨부를 인코딩하면서 약 1.37배 커지므로 정확한 한도 대신 '한도보다 작게'만 말한다(설계 W2-03 위험).
    /// Windows 탐색기는 나뉜 압축을 열지 못한다(ZIP .z01·7Z .001 모두).
    /// </summary>
    internal const string SplitHint =
        "* 메일로 보낼 때는 메일 첨부 한도보다 작게 고르세요.\n" +
        "* 받는 사람은 조각을 모두 같은 폴더에 두고 열어야 해요. Windows 탐색기로는 열리지 않아요(반디집·알집·7-Zip 으로 열어요).";

    /// <summary>
    /// '나누기' 칸에서 고른 값 → 바이트(null = 나누지 않음). 직접 입력(<see cref="SplitCustomIndex"/>)은 MB 숫자(소수 가능)를 받아
    /// 1MB 미만·4095MB 초과·숫자가 아니면 false 와 쉬운 안내(<paramref name="error"/>). 다른 순번은 늘 true.
    /// </summary>
    public static bool TryGetSplitBytes(int index, string? customMegabytes, out long? bytes, out string? error)
    {
        bytes = null;
        error = null;
        if (index != SplitCustomIndex)
        {
            bytes = index >= 0 && index < SplitPresetBytes.Length ? SplitPresetBytes[index] : null;
            return true;
        }
        string text = (customMegabytes ?? string.Empty).Trim();
        if (text.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out decimal mb)
            && !decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out mb))
        {
            error = SplitNotNumberMessage;
            return false;
        }
        if (mb < SplitMinMegabytes)
        {
            error = SplitTooSmallMessage;
            return false;
        }
        if (mb > SplitMaxMegabytes)
        {
            error = SplitTooLargeMessage;
            return false;
        }
        bytes = (long)Math.Round(mb * MegaByte);
        return true;
    }

    /// <summary>'파일 이름도 암호로 숨기기'를 고를 수 있는가 — 7Z 이고 암호를 넣었을 때만.</summary>
    public static bool CanEncryptFileNames(string? targetExtension, string? password) =>
        IsSevenZip(targetExtension) && !string.IsNullOrEmpty(password);

    /// <summary>
    /// 한글 암호 호환 경고(<see cref="Iljip.Services.PasswordPolicy.CheckCompat"/>)를 볼 형식인가 — ZIP 만(7Z 암호는 UTF-16 이라 반디집과 호환).
    /// 자동 풀림 EXE(W2-10)는 받는 사람이 그 exe 로 풀고, 스텁이 암호의 UTF-8·CP949 바이트를 둘 다 맞춰 보므로 경고하지 않는다.
    /// (만들기 직전 메일 질문에서 [ZIP으로 만들기]를 고르면 보통 ZIP 이 되므로 그때 <c>MainViewModel.ConfirmZipPasswordCompat</c> 가 같은 판정으로 묻는다.)
    /// </summary>
    public static bool ChecksPasswordCompat(string? targetExtension) => IsZip(targetExtension);

    public static CompressionLevelOption LevelOf(int index) => index switch
    {
        0 => CompressionLevelOption.Store,
        1 => CompressionLevelOption.Fastest,
        2 => CompressionLevelOption.Normal,
        3 => CompressionLevelOption.Maximum,
        _ => CompressionLevelOption.Normal,
    };

    /// <summary>
    /// 창에서 고른 값으로 옵션을 만든다. 형식에 맞지 않는 값은 버린다(TAR 의 암호, ZIP 의 솔리드, 암호 없는 파일 이름 암호화, TAR·GZ 의 나누기 등).
    /// </summary>
    /// <param name="splitBytes">나누기 크기(<see cref="TryGetSplitBytes"/> 의 값, null = 나누지 않음) — W2-03.</param>
    public static CompressionOptions Build(int levelIndex, string? password, string? targetExtension, ZipEncryption encryption, bool solid, bool encryptFileNames,
                                           long? splitBytes = null)
    {
        var layout = LayoutFor(targetExtension);
        bool hasPassword = layout.PasswordEnabled && !string.IsNullOrEmpty(password);
        bool sevenZip = IsSevenZip(targetExtension);
        return new CompressionOptions
        {
            Level = LevelOf(levelIndex),
            Password = hasPassword ? password : null,
            Encryption = UsesZipEncryption(targetExtension) ? encryption : ZipEncryption.Aes256,
            Solid = !sevenZip || solid,
            EncryptFileNames = sevenZip && hasPassword && encryptFileNames,
            SplitBytes = layout.ShowSplit && splitBytes > 0 ? splitBytes : null,
        };
    }
}
