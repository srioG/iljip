using System.Globalization;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>압축 옵션 창의 암호 두 칸 판정.</summary>
public enum PasswordEntryCheck
{
    /// <summary>두 칸이 같고 8자 이상.</summary>
    Ok,
    /// <summary>두 칸 다 비었음 = 암호 없이 압축.</summary>
    Empty,
    /// <summary>두 칸이 다름(막음).</summary>
    Mismatch,
    /// <summary>두 칸이 같지만 1~7자(안내만, 막지 않음).</summary>
    Short,
}

/// <summary>만들 압축 파일의 한글 암호가 다른 프로그램(반디집·알집)과 맞는가.</summary>
public enum PasswordCompat
{
    /// <summary>문제없음(ASCII 암호, 7z, 암호를 못 거는 형식, 빈 암호).</summary>
    Ok,
    /// <summary>ZIP + ZipCrypto + CP949 로 바꿀 수 있는 한글 암호 — CP949 바이트로 써서 반디집과 호환(양방향 실측). 작은 안내만.</summary>
    Cp949ZipCrypto,
    /// <summary>
    /// ZIP + AES + ASCII 아닌(CP949 로는 바꿀 수 있는) 암호 — 일집(SharpZipLib)은 UTF-8 로만 쓰고, 반디집·알집(한국어 Windows)은 CP949 로 읽어 안 풀림.
    /// 경고 창(ZipCrypto 로 바꾸면 CP949 로 써서 호환).
    /// </summary>
    NonAsciiAes,
    /// <summary>
    /// ZIP 인데 CP949 로 못 바꾸는 글자(이모지·일부 한자 등) — 암호 방식과 상관없이 UTF-8 로 써야 해 다른 프로그램에서 안 풀릴 수 있음.
    /// 경고 창(ZipCrypto 로 바꿔도 호환되지 않으므로 그 선택지는 없음).
    /// </summary>
    NotCp949Encodable,
}

/// <summary>
/// 압축 옵션 창의 암호 판정(순수 함수 — 창은 이 결과만 보고 동작한다, I0-3·B3).
/// 실측(반디집 7.46 bz.exe, 한국어 Windows): 일집 ZipCrypto+CP949 암호 → 반디집 통과, 일집 AES 한글 암호 → 반디집 'Invalid Password',
/// 7z 한글 암호(UTF-16) → 통과. 7z.dll 도 ZIP 의 ASCII 아닌 암호를 거부해 '반디집과 맞는 AES 한글 암호 ZIP' 은 만들 수 없다.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>권장 최소 길이(글자 수).</summary>
    public const int RecommendedLength = 8;

    public static PasswordEntryCheck CheckEntry(string? password, string? confirm)
    {
        password ??= string.Empty;
        confirm ??= string.Empty;
        if (password.Length == 0 && confirm.Length == 0) return PasswordEntryCheck.Empty;
        if (!string.Equals(password, confirm, StringComparison.Ordinal)) return PasswordEntryCheck.Mismatch;
        return LengthInCharacters(password) < RecommendedLength ? PasswordEntryCheck.Short : PasswordEntryCheck.Ok;
    }

    /// <param name="targetExtension">만들 파일의 확장자('.zip' 등).</param>
    public static PasswordCompat CheckCompat(string? password, string? targetExtension, ZipEncryption encryption)
    {
        if (string.IsNullOrEmpty(password) || IsAscii(password)) return PasswordCompat.Ok;
        if (!string.Equals(targetExtension, ".zip", StringComparison.OrdinalIgnoreCase)) return PasswordCompat.Ok;
        // CP949 로 못 바꾸는 글자가 있으면 ZipCrypto 로 바꿔도 반디집·알집과 맞출 수 없다 — 'ZipCrypto 로 만들기(호환)'를 권하지 않게 먼저 본다
        if (ZipPasswordProbe.TryCp949Bytes(password) is null) return PasswordCompat.NotCp949Encodable;
        return encryption == ZipEncryption.Aes256 ? PasswordCompat.NonAsciiAes : PasswordCompat.Cp949ZipCrypto;
    }

    /// <summary>사람이 보는 글자 수(이모지·결합 문자도 한 글자).</summary>
    public static int LengthInCharacters(string s) => new StringInfo(s).LengthInTextElements;

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
            if (c > 0x7F) return false;
        return true;
    }
}
