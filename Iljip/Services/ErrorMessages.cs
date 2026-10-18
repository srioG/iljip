namespace Iljip.Services;

/// <summary>
/// 예외 → 화면에 보일 쉬운 한국어 문구, 창 없이 풀기(--extract-to)의 종료 코드.
/// W2-08 부터 문구는 <see cref="ErrorCatalog"/> 한 곳에서 만든다 — 여기는 예전 이름을 지키는 얇은 겉(부르는 곳이 많아 이름을 그대로 둠).
/// 일집 자체 예외(<see cref="ArchiveException"/>)는 Message 가 이미 화면 문구다(검사값 불일치만 '검사값' 말로).
/// ★ 손상 문구에 '압축 파일이 아닌'을 넣지 말 것 — 재사용장치가 그 말로 '압축 아님'을 분류한다(재사용장치_processor.py 491행).
/// </summary>
public static class ErrorMessages
{
    /// <summary>경로 공격 거부 문구의 머리(서비스들이 InvalidDataException 에 쓰는 기존 계약).</summary>
    public const string UnsafePathPrefix = "안전하지 않은 경로: ";

    /// <summary>
    /// 화면 문구(<see cref="ErrorCatalog.Describe"/> 의 <see cref="UserProblem.Message"/>). W2-08 부터 모르는 오류도 영어 원문을 붙이지 않는다
    /// ('알 수 없는 문제가 생겼어요.' — 원문은 오류 창의 '자세히'·로그에만).
    /// </summary>
    public static string ToUserMessage(Exception ex) => ErrorCatalog.Describe(ex).Message;

    /// <summary>
    /// 엔진 영어 문구를 한 글자도 보이지 않는 쉬운 한국어(W2-05 — 탐색기 우클릭 풀기·여러 개 풀기의 오류 창·요약 이유).
    /// <see cref="ToUserMessage"/> 와 같고, 정말 모르는 오류만 <see cref="UnknownPlainMessage"/>('손상됐거나 끝까지 받아지지 않은 것 같아요')로 —
    /// 풀다가 난 모르는 오류는 대개 그 압축 파일 탓이라(예전과 같음). 날 문구는 부르는 쪽이 기록(로그)·요약 '자세히'에 남긴다.
    /// </summary>
    public static string ToPlainMessage(Exception ex)
    {
        var p = ErrorCatalog.Describe(ex);
        // 운영 체제가 알린 모르는 입출력 오류(네트워크 시간 초과 등)는 압축 파일 탓이 아니다 — '손상' 문구 대신 '알 수 없는 문제'(W2-08 검증 지적 2차)
        return p.Kind == ProblemKind.Unknown && !ErrorCatalog.IsEnvironmentProblem(ex) ? UnknownPlainMessage : p.Message;
    }

    /// <summary>모르는 오류의 쉬운 문구(★ '압축 파일이 아닌' 금지 — 재사용장치 분류 말).</summary>
    public const string UnknownPlainMessage = ErrorCatalog.EngineDataMessage;

    /// <summary>
    /// 압축(만들기) 실패의 쉬운 문구(W2-08 검증 지적 2차): <see cref="ToPlainMessage"/> 와 같되, 엔진이 낸 데이터 오류·모르는 오류는
    /// 풀기 문구('…원본을 다시 받아서 풀어 보세요')가 아니라 <see cref="CompressFailedMessage"/>. 사용자 환경 문제(공간·권한·잠금 — 엔진이 감싼 것도)·
    /// 일집 예외(원본 못 읽음 등)는 그 문구 그대로.
    /// </summary>
    public static string ToCompressMessage(Exception ex) => ErrorCatalog.DescribeCompressFailure(ex).Message;

    /// <summary>창 없이 풀기의 종료 코드: 형식 미지원 2, 그 밖의 실패 1(성공 0 은 호출자).</summary>
    public static int HeadlessExitCode(Exception ex) => ex is UnsupportedFormatException ? 2 : 1;

    /// <summary>
    /// "Access to the path 'X' is denied." 같은 문구에서 따옴표 안 경로를 꺼낸다 — 첫 따옴표부터 마지막 따옴표까지(경로 안의 작은따옴표 — 폴더 '26년 견적 — 에서
    /// 잘리지 않게, IJ3-02 (g)). .NET·운영 체제 문구는 경로를 하나만 싼다.
    /// </summary>
    internal static string? QuotedPath(string message)
    {
        int a = message.IndexOf('\'');
        int b = a < 0 ? -1 : message.LastIndexOf('\'');
        return b > a + 1 ? message[(a + 1)..b] : null;
    }

    /// <summary>
    /// 결과 창에 보일 항목 이름 — 손상돼 제어 문자가 섞인 이름만 '?' 로 바꾸고 길면 줄인다(I4-1 검증 지적: 깨진 이름의 이진 바이트가 수백 자 그대로 보였다).
    /// 멀쩡한 이름은 그대로.
    /// </summary>
    internal static string DisplayName(string name)
    {
        const int max = 60;
        if (!name.Any(char.IsControl)) return name;
        string s = new(name.Select(c => char.IsControl(c) ? '?' : c).ToArray());
        return s.Length > max ? s[..max] + "…" : s;
    }
}
