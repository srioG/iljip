using System.Text;

namespace Iljip.Services;

/// <summary>
/// 압축 안 항목 이름의 유니코드 모양 맞추기(W2-09, I2-10). 맥에서 만든 압축은 한글 이름을 자모로 나눠(NFD — 'ㅂㅗㄱㅗㅅㅓ') 적는다.
/// 화면에 보이는 이름과 디스크에 쓰는 이름은 모두 NFC(완성형 — '보고서')로 바꾼다(반디집과 같음).
/// 엔진 쪽 항목(SharpCompress 의 항목 객체·7z.dll 의 번호·EggDotNet 의 항목)은 그대로 두고 이름만 바꾸므로, 고른 항목 찾기는 양쪽을 NFC 로 맞춰 비교한다
/// (<see cref="SharpCompressArchiveServiceBase.EntrySelector"/> — 목록의 NFC 이름으로 골라도, 원래 NFD 이름으로 골라도 같은 항목).
/// NFC 로 같아지는 두 항목(같은 이름의 NFD·NFC)은 목록에 둘 다 보이고, 풀면 두 번째가 '이름 (1)'(<see cref="OutputFiles"/> 규칙).
/// </summary>
public static class EntryNames
{
    /// <summary>NFC(완성형)로. 이미 NFC(ASCII 포함)면 그대로 돌려준다. 짝 없는 서로게이트처럼 정규화할 수 없는 이름도 그대로.</summary>
    public static string Nfc(string? name)
    {
        if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
        try
        {
            return name.IsNormalized(NormalizationForm.FormC) ? name : name.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return name;
        }
    }

    /// <summary>선택·비교용 키: '\' → '/', 앞뒤 '/' 뗌, NFC.</summary>
    public static string SelectorKey(string? path) => Nfc((path ?? string.Empty).Replace('\\', '/').Trim('/'));
}
