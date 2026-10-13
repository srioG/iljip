using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Iljip.Models;
using Iljip.Services;
using Iljip.Views;

namespace Iljip.ViewModels;

/// <summary>
/// 풀기 창(<see cref="ExtractOptionsDialog"/>, W2-09 — I2-13)의 값과 규칙 — 순수 VM 이라 창을 띄우지 않고 시험한다.
/// 위에서 아래로: 압축 파일 → 풀 곳(최근 푼 곳 목록 + [찾아보기]) → '압축파일명으로 폴더 만들기' → 풀리는 곳 미리 보기
/// → '다 풀고 압축 파일을 휴지통으로'(고른 항목만 풀 때는 숨김) → '다 풀면 폴더 열기'.
/// 처음 값은 부르는 쪽이 설정(extract.*)으로 채운 <see cref="ExtractTargetRequest"/>. 기억(설정 저장)은 이 VM 이 아니라 풀기가 끝난 뒤 화면(MainViewModel)이 한 번.
/// </summary>
public sealed partial class ExtractOptionsViewModel : ObservableObject
{
    private readonly ExtractTargetRequest _request;

    public ExtractOptionsViewModel(ExtractTargetRequest request)
    {
        _request = request;
        RecentFolders = request.RecentFolders ?? Array.Empty<string>();
        // 풀 곳의 처음 값: 압축 파일이 있는 폴더(없으면 가장 최근에 푼 곳)
        _folder = !string.IsNullOrWhiteSpace(request.InitialFolder) ? request.InitialFolder : RecentFolders.FirstOrDefault() ?? string.Empty;
        _makeFolder = request.MakeFolder;
        _deleteArchiveAfter = !request.SelectedOnly && request.DeleteArchiveAfter;
        _openFolderAfter = request.OpenFolderAfter;
    }

    /// <summary>최근에 푼 곳(풀 곳 칸의 목록 — 가장 최근이 맨 위).</summary>
    public IReadOnlyList<string> RecentFolders { get; }

    /// <summary>풀 곳(고른 폴더).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private string _folder;

    /// <summary>압축파일명으로 폴더 만들기.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    private bool _makeFolder;

    /// <summary>다 풀고 압축 파일을 휴지통으로.</summary>
    [ObservableProperty]
    private bool _deleteArchiveAfter;

    /// <summary>다 풀면 폴더 열기.</summary>
    [ObservableProperty]
    private bool _openFolderAfter;

    private bool IsSingle => _request.Archives.Count == 1;

    /// <summary>창 위 한 줄: '압축 파일: 보고서.zip' / '압축 파일 3개'.</summary>
    public string ArchiveText => IsSingle
        ? "압축 파일: " + Path.GetFileName(_request.Archives[0])
        : $"압축 파일 {_request.Archives.Count}개";

    /// <summary>폴더 만들기 칸 글자: 하나면 만들 폴더 이름을 보여 준다.</summary>
    public string MakeFolderLabel => IsSingle
        ? $"압축파일명으로 폴더 만들기 (‘{ArchiveNaming.FolderNameFor(_request.Archives[0])}’ 폴더)"
        : "압축파일명으로 폴더 만들기 (압축 파일마다 따로)";

    /// <summary>'다 풀고 압축 파일을 휴지통으로' 칸을 보일지(고른 항목만 풀 때는 숨김).</summary>
    public bool ShowDeleteOption => !_request.SelectedOnly;

    /// <summary>
    /// 풀리는 곳 미리 보기: 'D:\받은 파일\보고서' — 폴더를 만들면 그 이름(이미 있으면 '보고서 (2)'), 여럿이면 '…\(압축 파일 이름)'.
    /// 풀 곳이 비었거나 올바르지 않으면 빈 글자. 한 글자 칠 때마다 다시 그리므로 네트워크 폴더(NAS)는 디스크를 보지 않는다
    /// (W2-09 검증 지적 — 끊긴 NAS 면 칠 때마다 창이 굳음): 있는지·'(2)'가 붙을지는 [풀기] 때 정한다.
    /// </summary>
    public string PreviewText
    {
        get
        {
            string folder = CleanFolder();
            if (CheckPath(folder) is not null) return string.Empty;
            bool network = FolderProbe.IsNetwork(folder);
            if (!network && !Directory.Exists(folder)) return string.Empty;
            string target;
            if (!MakeFolder) target = folder;
            else if (!IsSingle) target = Path.Combine(folder, "(압축 파일 이름)");
            else if (network) target = Path.Combine(folder, ArchiveNaming.FolderNameFor(_request.Archives[0]));
            else target = new ExtractTarget(folder, true, false, false).DestinationFor(_request.Archives[0]);
            return "풀리는 곳: " + target;
        }
    }

    private string CleanFolder() => ExtractSettings.TrimFolder(Folder);

    /// <summary>[풀기] 전에 볼 것 — 문제가 있으면 쉬운 한국어 안내, 없으면 null.</summary>
    public string? Validate() => CheckFolder(CleanFolder());

    /// <summary>풀 곳 확인: 경로 모양(<see cref="CheckPath"/>) + 있는지(네트워크 폴더는 <see cref="FolderProbe.NetworkTimeout"/> 까지만 기다림).</summary>
    private static string? CheckFolder(string folder)
    {
        if (CheckPath(folder) is { } problem) return problem;
        return FolderProbe.Exists(folder) switch
        {
            true => null,
            false => $"‘{folder}’ 폴더가 없어요. [찾아보기]로 골라 주세요.",
            null => $"‘{folder}’ 폴더에 닿지 않아요. 네트워크(NAS·공유 폴더) 연결을 확인하고 다시 하거나, 다른 폴더를 골라 주세요.",
        };
    }

    /// <summary>
    /// 디스크를 보지 않는 확인: 비었나·경로 모양이 맞나·일집 임시 폴더인가(W2-09 검증 지적 — 일집을 닫으면 지워지는 곳이라 풀지 않음).
    /// </summary>
    private static string? CheckPath(string folder)
    {
        if (folder.Length == 0) return "풀 곳(폴더)을 골라 주세요.";
        bool valid;
        try
        {
            valid = folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder);
        }
        catch
        {
            valid = false;
        }
        if (!valid) return "풀 곳(폴더) 경로가 올바르지 않아요. [찾아보기]로 골라 주세요.";
        if (TempLocations.IsIljipTemp(folder)) return TempFolderMessage;
        return null;
    }

    /// <summary>일집 임시 폴더를 골랐을 때의 안내.</summary>
    internal const string TempFolderMessage = "일집 임시 폴더에는 풀 수 없어요. 일집을 닫으면 지워지는 곳이에요. [찾아보기]로 다른 폴더를 골라 주세요.";

    /// <summary>창의 답([풀기]). 문제가 있으면 null(<see cref="Validate"/> 의 안내를 창이 보여 줌).</summary>
    public ExtractTarget? BuildResult() => TryBuildResult(out var result) is null ? result : null;

    /// <summary>[풀기]: 문제가 있으면 그 안내(답 없음), 없으면 null 과 답. 폴더 확인은 한 번만 한다(네트워크 폴더는 기다릴 수 있어서).</summary>
    public string? TryBuildResult(out ExtractTarget? result)
    {
        string folder = CleanFolder();
        result = null;
        if (CheckFolder(folder) is { } problem) return problem;
        result = new ExtractTarget(folder, MakeFolder, ShowDeleteOption && DeleteArchiveAfter, OpenFolderAfter);
        return null;
    }
}
