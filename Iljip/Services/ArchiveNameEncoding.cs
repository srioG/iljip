using System.Text;
using System.Text.Unicode;
using SharpCompress.Common;

namespace Iljip.Services;

/// <summary>압축 파일 하나의 이름 글자 방식(W2-09, I2-10 — <see cref="ArchiveNameEncodingDetector"/> 가 정함).</summary>
public enum NameEncoding
{
    /// <summary>정하지 않음 — 이름마다 예전 규칙(<see cref="KoreanFileNameDecoder.DecodeBytes"/>: UTF-8 → 한글이 보이면 CP949 → CP437). ASCII 뿐인 압축도 여기.</summary>
    Auto,
    /// <summary>UTF-8(표시가 없어도 모든 이름이 UTF-8 로 엄격하게 풀림). 푸는 결과는 <see cref="Auto"/> 와 같다.</summary>
    Utf8,
    /// <summary>CP949(한국어 Windows — 옛 알집·탐색기·반디집 기본).</summary>
    Korean,
    /// <summary>CP932(Shift-JIS — 일본어 Windows).</summary>
    Japanese,
    /// <summary>CP936(GBK — 중국어 간체 Windows).</summary>
    Chinese,
}

/// <summary>
/// 압축 파일 하나의 이름 글자 방식을 정한다(W2-09, I2-10 — KoreanFileNameDecoder 를 넓힘). UTF-8 표시가 없는 '날 이름 바이트' 전체를 보고
/// 압축 파일 하나에 하나만 정한다(항목마다 따로 섞지 않음). 차례(한국어 우선):
/// <list type="number">
/// <item>ASCII·UTF-8 로 엄격하게 풀리지 않는 이름(옛 방식 이름)이 없으면 정하지 않는다(<see cref="NameEncoding.Utf8"/>·<see cref="NameEncoding.Auto"/> — 지금 동작).</item>
/// <item>옛 방식 이름이 모두 CP949 로 엄격하게 풀리고 한글로 보이면 CP949(지금 동작과 같음). '한글로 보임'은 KS X 1001 완성형 음절이 하나 이상이고
///   ① 한자·확장 음절 없이 한글뿐이면(짧은 이름은 늘, 긴 이름은 자주 쓰는 음절이 30% 이상) 또는 ② 자주 쓰는 음절이 절반 이상이고 한글 수가 한자·확장 음절 수 이상
///   또는 ③(W2-09 검증 지적 — '株式會社 일진 見積書' 처럼 한자가 더 많은 이름) 확장 음절이 없고 자주 쓰는 음절이 2자 이상·한글의 80% 이상
///   (GBK 간체를 CP949 로 읽은 한글은 자주 쓰는 음절이 25% 안팎이라 둘 다 자주 쓰는 음절일 확률이 낮다 — 실험에서 중국어 압축의 한국어 오판이 1%p 안팎 늚).
///   ★ 설계의 '한글 음절이 하나 이상'만으로는 안 된다: Shift-JIS 가나·한자는 CP949 의 확장 음절(옛 알집이 못 쓰던 8,822자)로, GBK 간체는 완성형 한글과 한자가 섞인
///   엉뚱한 글자로 다 풀린다(실측 — 'テスト資料' → '깻깥깸랍됨', '测试文件' → '꿎桿匡숭'). 진짜 한국어 이름은 완성형 음절, 그것도 자주 쓰는 음절이 대부분이라 그것으로 가른다.</item>
/// <item>모두 CP932 로 엄격하게 풀리고 가나·한자가 80% 이상이며(반각 가나는 넣지 않음), 가나가 있거나 GBK 로는 흔한 한자(GB2312)로 안 보이면 CP932.</item>
/// <item>모두 CP936(GBK)로 엄격하게 풀리고 흔한 한자(GB2312 한자 구역)가 60% 이상이면 CP936. 단(W2-09 검증 지적 — 한자가 많은 한국어 이름이 중국어로 읽힘)
///   GBK 로 읽은 글자에 사용자 정의 글자(PUA)가 하나라도 있거나 GB2312 2급 한자(머리 D8~F7 — 중국어 이름에는 드묾, 실측 0.1~0.3%)가 한자의 25%를 넘으면 중국어가 아니다.
///   KS X 1001 한자는 읽는 소리 차례라 '사~힐' 소리 한자(약 4분의 3)가 GBK 로는 2급 한자·사용자 정의 구역에 떨어진다('會' → PUA, '株式社' → 2급).</item>
/// <item>중국어·일본어가 아니고 모두 CP949 완성형(한글·한자, 확장 음절 없음)으로 풀리면 CP949 — '株式會社 일진 見積書'처럼 한자가 한글보다 많아 2번에서 못 정한 한국어 이름.
///   예전 규칙도 한글이 든 이름은 CP949 로 풀었고, 한자뿐인 이름('會議錄')은 CP437 로 깨졌는데 이제 한자로 바르게 보인다.</item>
/// <item>그 밖은 정하지 않는다 — 이름마다 예전 규칙(CP949 로 못 푸는 것은 CP437 그대로 보존).</item>
/// </list>
/// '엄격'은 <see cref="DecoderFallback.ExceptionFallback"/>. 예외 흐름 비용(8a04404 교훈)을 줄이려고 바이트 모양을 먼저 보고, 모양이 맞을 때만 실제로 풀어 본다.
/// 짧은 이름 1~2개는 CP949 와 GBK 둘 다 그럴듯해 한국어 쪽으로 기운다(설계 위험 — 중국어 압축이 한글처럼 보일 수 있음). 이름이 많을수록 정확하다.
/// </summary>
public static class ArchiveNameEncodingDetector
{
    private static readonly Encoding StrictCp949 = Strict(949);
    private static readonly Encoding StrictCp932 = Strict(932);
    private static readonly Encoding StrictCp936 = Strict(936);

    private static Encoding Strict(int codePage) =>
        Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>
    /// 이름들(UTF-8 표시가 '없는' 것만 — 표시가 있는 이름은 늘 UTF-8)의 날 바이트로 정한다. ASCII 뿐이면 <see cref="NameEncoding.Auto"/>,
    /// 모두 UTF-8 이면 <see cref="NameEncoding.Utf8"/>.
    /// </summary>
    public static NameEncoding Detect(IEnumerable<byte[]> rawNames)
    {
        bool anyNonAscii = false;
        var legacy = new List<byte[]>();
        foreach (var b in rawNames)
        {
            if (b is null || IsAscii(b)) continue;
            anyNonAscii = true;
            if (!Utf8.IsValid(b)) legacy.Add(b);
        }
        if (legacy.Count == 0) return anyNonAscii ? NameEncoding.Utf8 : NameEncoding.Auto;
        return DetectLegacy(legacy);
    }

    /// <summary>옛 방식 이름인가 — ASCII 가 아닌 바이트가 있고 UTF-8 로 엄격하게 풀리지 않음.</summary>
    internal static bool IsLegacy(ReadOnlySpan<byte> b) => !IsAscii(b) && !Utf8.IsValid(b);

    private static bool IsAscii(ReadOnlySpan<byte> b) => System.Text.Ascii.IsValid(b);

    /// <summary>옛 방식 이름들(<see cref="IsLegacy"/>)로 정한다. 없으면 <see cref="NameEncoding.Auto"/>.</summary>
    internal static NameEncoding DetectLegacy(IReadOnlyCollection<byte[]> legacy)
    {
        if (legacy.Count == 0) return NameEncoding.Auto;
        var ko = ScanCp949(legacy);
        if (ko is { LooksKorean: true }) return NameEncoding.Korean;   // 한국어 우선 — 맞으면 다른 것은 보지도 않는다
        var zh = ScanCp936(legacy);
        if (ScanCp932(legacy) is { } ja && ja.LooksJapanese(zh)) return NameEncoding.Japanese;
        if (zh is { LooksChinese: true }) return NameEncoding.Chinese;
        // 한자가 많은 한국어 이름(W2-09 검증 지적): 중국어·일본어가 아니면 CP949 완성형으로 다 풀리는 것을 한국어로
        if (ko is { Extended: 0 } k && k.Hangul + k.Hanja > 0) return NameEncoding.Korean;
        return NameEncoding.Auto;
    }

    /// <summary>정한 방식의 엄격한 인코딩(정하지 않음·UTF-8 이면 null).</summary>
    internal static Encoding? StrictEncodingOf(NameEncoding e) => e switch
    {
        NameEncoding.Korean => StrictCp949,
        NameEncoding.Japanese => StrictCp932,
        NameEncoding.Chinese => StrictCp936,
        _ => null,
    };

    /// <summary>엄격하게 풀어 본다(못 풀면 null). 바이트 모양은 호출자가 먼저 봤다 — 여기서 예외는 드물다.</summary>
    internal static string? TryStrict(Encoding strict, ReadOnlySpan<byte> b)
    {
        try { return strict.GetString(b); }
        catch (DecoderFallbackException) { return null; }
    }

    // ───────────────────────────── CP949(UHC) ─────────────────────────────

    private sealed class KoreanStats
    {
        public int Hangul;        // KS X 1001 완성형 음절(머리 0xB0~0xC8)
        public int CommonHangul;  // 그중 자주 쓰는 음절
        public int Hanja;         // KS X 1001 한자(머리 0xCA~0xFD)
        public int Extended;      // UHC 확장 음절(완성형 밖 8,822자 — 꼬리가 0xA1 아래)

        public bool LooksKorean =>
            Hangul > 0 && (
                Hanja + Extended == 0 && (Hangul <= 4 || CommonHangul * 10 >= Hangul * 3)
                || CommonHangul * 2 >= Hangul && Hangul >= Hanja + Extended
                || Extended == 0 && CommonHangul >= 2 && CommonHangul * 10 >= Hangul * 8);
    }

    /// <summary>모두 CP949 로 엄격하게 풀리면 통계, 하나라도 안 풀리면 null.</summary>
    private static KoreanStats? ScanCp949(IEnumerable<byte[]> names)
    {
        var st = new KoreanStats();
        foreach (var b in names)
        {
            if (!Cp949Shape(b)) return null;
            string? s = TryStrict(StrictCp949, b);
            if (s is null) return null;
            int j = 0;   // CP949 는 바이트 단위(1바이트 또는 2바이트) 하나가 글자 하나 — 풀린 글자와 나란히 걷는다
            for (int i = 0; i < b.Length; i++, j++)
            {
                byte c = b[i];
                if (c < 0x80) continue;
                byte t = b[++i];
                if (c <= 0xA0 || t < 0xA1) st.Extended++;   // UHC 확장 구역(머리 81~A0 전부, 머리 A1~C6 의 꼬리 A1 아래)
                else if (c >= 0xB0 && c <= 0xC8)
                {
                    st.Hangul++;
                    if (j < s.Length && CommonSyllables.Contains(s[j])) st.CommonHangul++;
                }
                else if (c >= 0xCA && c <= 0xFD) st.Hanja++;
            }
        }
        return st;
    }

    /// <summary>CP949(UHC) 바이트 모양: 머리 0x81~0xC6 꼬리 41~5A·61~7A·81~FE(머리 A1 부터는 꼬리 A1 위가 완성형), 머리 0xC7~0xFE 꼬리 A1~FE.</summary>
    private static bool Cp949Shape(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            byte c = b[i];
            if (c < 0x80) continue;
            if (c == 0x80 || c == 0xFF || i + 1 >= b.Length) return false;
            byte t = b[++i];
            bool ok = c <= 0xC6
                ? t is >= 0x41 and <= 0x5A or >= 0x61 and <= 0x7A or >= 0x81 and <= 0xFE
                : t is >= 0xA1 and <= 0xFE;
            if (!ok) return false;
        }
        return true;
    }

    // ───────────────────────────── CP932(Shift-JIS) ─────────────────────────────

    private sealed class JapaneseStats
    {
        public int Kana;      // 히라가나·가타카나(전각)
        public int Kanji;     // 한자
        public int HalfKana;  // 반각 가타카나(드물어 일본어 근거로 치지 않음)
        public int Other;     // 그 밖의 비 ASCII(기호·전각 영숫자)

        public bool LooksJapanese(ChineseStats? zh)
        {
            int jp = Kana + Kanji;
            if (jp == 0 || jp * 5 < (jp + HalfKana + Other) * 4) return false;
            return Kana > 0 || zh is null || !zh.MostlyCommonHanzi;
        }
    }

    private static JapaneseStats? ScanCp932(IEnumerable<byte[]> names)
    {
        var st = new JapaneseStats();
        foreach (var b in names)
        {
            if (!Cp932Shape(b)) return null;
            string? s = TryStrict(StrictCp932, b);
            if (s is null) return null;
            foreach (char ch in s)
            {
                if (ch < 0x80) continue;
                if (ch is >= 'ぁ' and <= 'ヿ') st.Kana++;
                else if (IsCjkIdeograph(ch)) st.Kanji++;
                else if (ch is >= '｡' and <= 'ﾟ') st.HalfKana++;
                else st.Other++;
            }
        }
        return st;
    }

    /// <summary>Shift-JIS 바이트 모양: 한 바이트 00~7F·A1~DF(반각 가나), 머리 81~9F·E0~FC 꼬리 40~7E·80~FC.</summary>
    private static bool Cp932Shape(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            byte c = b[i];
            if (c < 0x80 || c is >= 0xA1 and <= 0xDF) continue;
            if (!(c is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC) || i + 1 >= b.Length) return false;
            byte t = b[++i];
            if (!(t is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFC)) return false;
        }
        return true;
    }

    // ───────────────────────────── CP936(GBK) ─────────────────────────────

    private sealed class ChineseStats
    {
        public int Common;    // GB2312 한자 구역(머리 B0~F7, 꼬리 A1~FE)
        public int Rare;      // 그중 2급 한자(머리 D8~F7 — 부수 차례의 드문 글자. 중국어 이름에는 거의 없고, 한국어 한자 '사~힐' 소리 대부분이 GBK 로는 여기)
        public int Symbols;   // GB2312 기호 구역(머리 A1~A9)
        public int Extended;  // GBK 확장(그 밖 — 드문 한자). 사용자 정의 구역 바이트도 여기 센다(아래 Private 와 따로 — 비율 계산은 예전 그대로)
        public int Private;   // 풀린 글자 중 사용자 정의 글자(U+E000~F8FF — GBK 사용자 정의 구역). 진짜 중국어 이름에는 없다

        /// <summary>두 바이트 글자의 80% 이상이 흔한 한자.</summary>
        public bool MostlyCommonHanzi => Common > 0 && Common * 5 >= (Common + Extended) * 4;

        /// <summary>흔한 한자가 60% 이상이고, 사용자 정의 글자가 없고, 2급 한자가 한자의 25% 이하(W2-09 검증 지적 — 한자가 많은 한국어 이름을 중국어로 보지 않게).</summary>
        public bool LooksChinese => Private == 0 && Common > 0 && Common * 5 >= (Common + Extended + Symbols) * 3 && Rare * 4 <= Common;
    }

    private static ChineseStats? ScanCp936(IEnumerable<byte[]> names)
    {
        var st = new ChineseStats();
        foreach (var b in names)
        {
            if (!Cp936Shape(b)) return null;
            string? s = TryStrict(StrictCp936, b);
            if (s is null) return null;
            foreach (char ch in s)
                if (ch is >= '' and <= '') st.Private++;
            for (int i = 0; i < b.Length; i++)
            {
                byte c = b[i];
                if (c <= 0x80) continue;
                byte t = b[++i];
                if (c >= 0xB0 && c <= 0xF7 && t >= 0xA1)
                {
                    st.Common++;
                    if (c >= 0xD8) st.Rare++;
                }
                else if (c >= 0xA1 && c <= 0xA9 && t >= 0xA1) st.Symbols++;
                else st.Extended++;
            }
        }
        return st;
    }

    /// <summary>GBK 바이트 모양: 한 바이트 00~80, 머리 81~FE 꼬리 40~7E·80~FE.</summary>
    private static bool Cp936Shape(byte[] b)
    {
        for (int i = 0; i < b.Length; i++)
        {
            byte c = b[i];
            if (c <= 0x80) continue;
            if (c == 0xFF || i + 1 >= b.Length) return false;
            byte t = b[++i];
            if (!(t is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFE)) return false;
        }
        return true;
    }

    private static bool IsCjkIdeograph(char ch) =>
        ch is >= '一' and <= '鿿' or >= '㐀' and <= '䶿' or >= '豈' and <= '﫿' or '々' or '〆';

    /// <summary>
    /// 자주 쓰는 한글 음절(파일 이름에 흔한 말 — 문서·업무·생활). 완성형 2,350자 중 약 600자. 진짜 한국어 이름은 거의 이 안에서 나오고(실측 90% 넘음),
    /// GBK 간체를 CP949 로 잘못 읽은 글자는 완성형 안에서 고르게 흩어져 20% 안팎만 걸린다 — 그 차이로 가른다.
    /// 마지막 줄(W2-09 검증 지적)은 이 PC 의 실제 이름 5만여 개에서 자주 나왔는데 빠져 있던 음절('새 폴더'·'샘플'·'스캔'·'텍스트' 등).
    /// </summary>
    private static readonly HashSet<char> CommonSyllables = new(
        "가각간갈감갑강같개객거건걸검겁게겨격견결겸경계고곡곤골곳공과관광괴교구국군굴궁권귀규균그극근글금급기긴길김까꺼께꼬꾸끄끝" +
        "나낙난날남납낭내냉너널넘네녀년념녕노녹논놀농높뇌누눈뉴느는늘능니님다닥단달담답당대댁더덕던데도독돈돌동돼되된될두둘뒤드득든들듯등디따딸때또뜻" +
        "라락란람랍랑래랭략량러럼레려력련렬령례로록론롭료루류륙률르른를름릉리린림립마막만많말망매맥머먹멀메며면멸명모목몰몸못몽묘무묵문물므미민밀" +
        "바박반받발밝방배백뱀버번벌범법벽변별병보복본볼봉부북분불붕비빈빌빛사삭산살삼상새색생서석선설섬섭성세센셀셋소속손솔송쇄수숙순술숨쉬스슬습승시식신실심십싸쌍써쓰씨" +
        "아악안알암압앙애액야약양어억언얼엄업없에엑엔여역연열염엽영예오옥온올완왕외요욕용우운울움웅원월위유육윤율으은을음응의이익인일임입있잉" +
        "자작잔잘잠잡장재쟁저적전절점접정제져조족존졸종좋좌죄주죽준줄중즉즐증지직진질집징짜째쪽차착찬찰참창채책처척천철첨첩청체초촉촌총촬최추축춘출충취측층치칙친칠침" +
        "카칼커컴케켜코콘쿠크큰키타탁탄탈탐탑탕태택터테텔토톤통퇴투특튼트파판팔패퍼페펴편평폐포폭표푸품풍프피필핑하학한할함합항해핵행향허헌험혁현혈협형혜호혹혼홍화확환활황회획효후훈휘휴흉흐흑흔흡흥희흰히힘" +
        "갔겠넣놓닭떡뜨띠렌렇롯링맞맛멘몇뭐밥밭벤봄뷰빠뻐뽑뿐샘샵셔셨쇼숍슈쉽쌀썼쏘쓴씩앞얘얻었엄엘옷와왜웃웨윈잎잖젊젤짐짝찍쯤챙쳐춤칭캐캠컵켓콜쿄큐킨킹탕텐톡튀틀팀팅펜폰폼풀퓨픽햄헬헤혀홈휠" +
        "폴플클캔및워텍딩런"
    );
}

/// <summary>
/// 정한 방식(<see cref="NameEncoding"/>)으로 이름 바이트를 푸는 것(W2-09). SharpCompress <c>ArchiveEncoding.CustomDecoder</c> 에 그대로 연결된다.
/// - UTF-8 표시가 있는 이름·정하지 않음(<see cref="NameEncoding.Auto"/>·<see cref="NameEncoding.Utf8"/>)은 예전 규칙(<see cref="KoreanFileNameDecoder.DecodeBytes"/>).
/// - 정했으면: ASCII·UTF-8 로 엄격하게 풀리는 이름은 UTF-8(예전과 같음), 정한 방식으로 엄격하게 풀리면 그것, 둘 다 아니면 예전 규칙(그 이름만 — CP437 보존 등).
/// </summary>
public sealed class NameDecoder
{
    /// <summary>정하지 않음(이름마다 예전 규칙).</summary>
    public static readonly NameDecoder PerName = new(NameEncoding.Auto);

    public NameDecoder(NameEncoding encoding) => Encoding = encoding;

    public NameEncoding Encoding { get; }

    /// <summary>날 이름 바이트들로 방식을 정한 푸는 것.</summary>
    public static NameDecoder For(IEnumerable<byte[]> rawNames) => new(ArchiveNameEncodingDetector.Detect(rawNames));

    public string Decode(byte[] bytes, int index, int count, bool utf8Flagged = false)
    {
        if (bytes is null || count <= 0) return string.Empty;
        var strict = utf8Flagged ? null : ArchiveNameEncodingDetector.StrictEncodingOf(Encoding);
        if (strict is null) return KoreanFileNameDecoder.DecodeBytes(bytes, index, count);
        var span = new ReadOnlySpan<byte>(bytes, index, count);
        if (Utf8.IsValid(span)) return System.Text.Encoding.UTF8.GetString(span);
        return ArchiveNameEncodingDetector.TryStrict(strict, span) ?? KoreanFileNameDecoder.DecodeBytes(bytes, index, count);
    }

    /// <summary>SharpCompress <c>CustomDecoder</c> 모양.</summary>
    public string Decode(byte[] bytes, int index, int count, EncodingType type) => Decode(bytes, index, count, type == EncodingType.UTF8);
}

/// <summary>
/// SharpCompress 로 처음 열 때 이름 바이트를 모으는 것(W2-09). 여는 동안은 예전 규칙으로 풀어 주고, UTF-8 표시가 없는 옛 방식 이름만 따로 적어 둔다.
/// 다 연 뒤 <see cref="Decide"/> 로 방식을 정하고, <see cref="AgreesWith"/> 가 참이면(정한 방식으로 풀어도 예전 규칙과 글자가 같음 — 한국어·UTF-8 압축 대부분)
/// 그대로 쓰고, 아니면(일본어·중국어 압축 등) 정한 방식으로 한 번 더 연다(<see cref="SharpCompressArchiveServiceBase.OpenWithNameDetection"/>).
/// ★ SharpCompress 는 ZIP 의 항목 주석·압축 파일 주석, TAR 의 링크 이름도 같은 길로 풀어서 그것도 같이 모인다(ZIP 원시 읽기 <see cref="ZipEntryNames"/> 도 같게 본다).
/// </summary>
internal sealed class NameCollector
{
    private readonly List<byte[]> _legacy = new();
    private bool _frozen;

    public string Decode(byte[] bytes, int index, int count, EncodingType type)
    {
        if (!_frozen && type != EncodingType.UTF8 && bytes is not null && count > 0)
        {
            var span = new ReadOnlySpan<byte>(bytes, index, count);
            if (ArchiveNameEncodingDetector.IsLegacy(span))
                lock (_legacy) _legacy.Add(span.ToArray());
        }
        return KoreanFileNameDecoder.DecodeBytes(bytes!, index, count);
    }

    /// <summary>모은 이름으로 방식을 정한다(이후로는 모으지 않음).</summary>
    public NameDecoder Decide()
    {
        lock (_legacy)
        {
            _frozen = true;
            return new NameDecoder(ArchiveNameEncodingDetector.DetectLegacy(_legacy));
        }
    }

    /// <summary>모은 모든 이름이 <paramref name="decoder"/> 로 풀어도 예전 규칙과 같은가(같으면 다시 열 필요 없음).</summary>
    public bool AgreesWith(NameDecoder decoder)
    {
        if (decoder.Encoding is NameEncoding.Auto or NameEncoding.Utf8) return true;
        lock (_legacy)
        {
            foreach (var raw in _legacy)
                if (!string.Equals(decoder.Decode(raw, 0, raw.Length), KoreanFileNameDecoder.DecodeBytes(raw, 0, raw.Length), StringComparison.Ordinal))
                    return false;
        }
        return true;
    }
}
