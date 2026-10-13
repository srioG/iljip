namespace Iljip.Services;

/// <summary>
/// 화면이 아닌 시스템 동작 묶음(설계 §5). <see cref="Iljip.ViewModels.MainViewModel"/> 생성자의 선택 인자 — null 이면 진짜 구현.
/// 시험은 가짜를 넣는다(Iljip.Tests/TestSupport/TestServices — 셸·휴지통·설정 모두 가짜, 진짜는 절대 부르지 않음).
/// W2-05 에서 처음 들임(모양은 W2-04 보관본과 같게 — 다시 합칠 때 겹치게). 뒤 묶음은 속성을 덧붙이기만 한다
/// (IClipboardService·IImageDecoder·IClock·IProcessLauncher·IFileChangeSource·IRegistryRoot 등).
/// </summary>
public sealed class AppServices
{
    /// <summary>파일·폴더 열기(ShellExecute·탐색기) — W2-05 는 우클릭으로 풀고 결과 폴더 열기(I1-6).</summary>
    public IShellLauncher Shell { get; init; } = new ShellLauncher();

    /// <summary>설정 저장소(settings.json).</summary>
    public ISettingsStore Settings { get; init; } = new JsonSettingsStore();

    /// <summary>휴지통(풀고 나서 압축 파일 지우기 — I2-5). 영구 삭제는 하지 않는다.</summary>
    public IRecycleBin RecycleBin { get; init; } = new RecycleBin();

    /// <summary>진짜 구현만으로 된 묶음.</summary>
    public static AppServices CreateDefault() => new();
}
