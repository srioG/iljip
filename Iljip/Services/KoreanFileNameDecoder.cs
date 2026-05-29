using System.Text;

namespace Iljip.Services;

/// <summary>
/// ZIP 파일의 한글 파일명 인코딩 자동 판별/디코딩.
/// 구버전 알집/탐색기로 만든 ZIP은 EUC-KR(CP949)로 인코딩된 경우가 많고,
/// 최신 알집/반디집/macOS는 UTF-8 + EFS(General Purpose Bit 11) 플래그를 사용한다.
///
/// 가장 견고한 방식은 <see cref="DecodeBytes"/>로 원본 바이트를 직접 받아 판별하는 것이며,
/// SharpCompress의 <c>ArchiveEncoding.CustomDecoder</c>에 연결해 사용한다.
/// (문자열만 받는 <see cref="Decode"/>는 fallback 경로 대비용으로 유지.)
/// </summary>
public static class KoreanFileNameDecoder
{
    private static readonly Encoding Cp437 = Encoding.GetEncoding(437);
    private static readonly Encoding Cp949;

    // 잘못된 바이트를 만나면 예외를 던지는 엄격한 UTF-8 디코더
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static KoreanFileNameDecoder()
    {
        // System.Text.Encoding.CodePages 패키지 등록은 App.OnStartup 에서 처리
        Cp949 = Encoding.GetEncoding(949);
    }

    /// <summary>
    /// 압축 항목 파일명의 원본 바이트를 받아 올바른 문자열로 디코딩한다.
    /// SharpCompress <c>ArchiveEncoding.CustomDecoder</c>(byte[], int, int) 시그니처에 직접 연결된다.
    ///   1) UTF-8로 엄격 디코딩 시도 — 성공하면 최신 UTF-8 ZIP (한글/이모지 등 정상)
    ///   2) 실패하면 CP949(EUC-KR)로 디코딩 — 구형 알집/탐색기 ZIP
    ///   3) 그래도 실패하면 CP437로 무손실 보존 (원본 바이트 유지)
    /// </summary>
    public static string DecodeBytes(byte[] bytes, int index, int count)
    {
        if (bytes is null || count <= 0)
            return string.Empty;

        // 1) UTF-8 (strict). 유효한 UTF-8이면 그대로 채택.
        try
        {
            return StrictUtf8.GetString(bytes, index, count);
        }
        catch (DecoderFallbackException)
        {
            // 유효한 UTF-8이 아님 → 레거시 인코딩 시도
        }

        // 2) CP949 (EUC-KR). 한글이 감지되면 채택.
        try
        {
            string cp949 = Cp949.GetString(bytes, index, count);
            if (ContainsKorean(cp949))
                return cp949;
        }
        catch
        {
            // 무시하고 다음 단계로
        }

        // 3) CP437 — 0x00~0xFF를 무손실 1:1 매핑하므로 원본 보존에 안전.
        return Cp437.GetString(bytes, index, count);
    }

    /// <summary>
    /// (Fallback) SharpCompress가 CP437로 잘못 해석한 문자열을 받아 CP949(EUC-KR)로 재해석.
    /// CustomDecoder가 적용되지 않는 경로를 대비한 belt-and-suspenders.
    /// 이미 올바른 한글 문자열이면 round-trip이 깨져 ContainsKorean=false → 원본 그대로 반환(무해).
    /// </summary>
    public static string Decode(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
            return rawName;

        bool hasNonAscii = false;
        foreach (char c in rawName)
        {
            if (c > 0x7F) { hasNonAscii = true; break; }
        }
        if (!hasNonAscii)
            return rawName;

        try
        {
            byte[] bytes = Cp437.GetBytes(rawName);
            string decoded = Cp949.GetString(bytes);
            if (ContainsKorean(decoded))
                return decoded;
        }
        catch
        {
            // 변환 실패 시 원본 유지
        }

        return rawName;
    }

    private static bool ContainsKorean(string text)
    {
        foreach (char c in text)
        {
            // 한글 음절(가-힣) + 한글 자모 영역
            if ((c >= 0xAC00 && c <= 0xD7A3) ||
                (c >= 0x1100 && c <= 0x11FF) ||
                (c >= 0x3130 && c <= 0x318F))
            {
                return true;
            }
        }
        return false;
    }
}
