namespace Iljip.Services;

/// <summary>
/// 화면이 아닌 시스템 동작 묶음(설계 §5). <see cref="Iljip.ViewModels.MainViewModel"/> 생성자의 선택 인자 — null 이면 진짜 구현.
/// 시험은 가짜를 넣는다(Iljip.Tests/TestSupport/TestServices — 셸·휴지통·설정 모두 가짜, 진짜는 절대 부르지 않음).
/// W2-05 에서 처음 들임(모양은 W2-04 보관본과 같게 — 다시 합칠 때 겹치게). 뒤 묶음은 속성을 덧붙이기만 한다
/// (IClipboardService·IImageDecoder·IProcessLauncher·IRegistryRoot 등 — IFileChangeSource 는 W2-06, IClock·압축 시험기는 W2-07 이 더함).
/// </summary>
public sealed class AppServices
{
    /// <summary>파일·폴더 열기(ShellExecute·탐색기) — W2-05 는 우클릭으로 풀고 결과 폴더 열기(I1-6).</summary>
    public IShellLauncher Shell { get; init; } = new ShellLauncher();

    /// <summary>설정 저장소(settings.json).</summary>
    public ISettingsStore Settings { get; init; } = new JsonSettingsStore();

    /// <summary>휴지통(풀고 나서 압축 파일 지우기 — I2-5). 영구 삭제는 하지 않는다.</summary>
    public IRecycleBin RecycleBin { get; init; } = new RecycleBin();

    /// <summary>파일이 바뀌었다는 알림(W2-06 — 압축 속 파일을 고쳐 저장하면 '다시 넣을까요?', I3-3).</summary>
    public IFileChangeSource FileChanges { get; init; } = new FileChangeSource();

    /// <summary>지금 시각(W2-07 — 압축 파일 이름의 날짜 I3-5. 모양은 W2-04 보관본과 같게).</summary>
    public IClock Clock { get; init; } = SystemClock.Instance;

    /// <summary>방금 만든 압축 파일 시험(W2-07 — '다 만들고 시험하기'·원본 지우기 전 확인 I3-4. W2-08 이 키움).</summary>
    public ICreatedArchiveTester CompressTester { get; init; } = CreatedArchiveTester.Instance;

    /// <summary>진짜 구현만으로 된 묶음.</summary>
    public static AppServices CreateDefault() => new();
}
