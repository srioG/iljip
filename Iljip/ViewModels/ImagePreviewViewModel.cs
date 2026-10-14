using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

/// <summary>미리 보기 칸의 상태.</summary>
public enum PreviewState
{
    /// <summary>아무것도 고르지 않음.</summary>
    Empty,
    /// <summary>꺼내 읽는 중.</summary>
    Loading,
    /// <summary>그림을 보이는 중.</summary>
    Image,
    /// <summary>그림 파일이 아님(미리 보기 꺼짐).</summary>
    NotImage,
    /// <summary>너무 커서 미리 보지 않음(자리표시).</summary>
    TooLarge,
    /// <summary>암호를 넣어야 볼 수 있음(단추).</summary>
    NeedsPassword,
    /// <summary>못 읽음(코덱 없음·손상 등).</summary>
    Error,
}

/// <summary>미리 보기가 화면(MainViewModel)에 바라는 것 — 시험에서도 진짜 압축으로 돌리려고 좁게 뺐다.</summary>
internal interface IPreviewHost
{
    /// <summary>
    /// 지금 보는 압축의 세대 번호(IJ3-01) — 다른 압축을 열거나, 같은 압축을 다시 열거나, 고친 뒤 목록을 다시 읽거나, 닫을 때마다 하나씩 는다.
    /// 미리 보기는 읽기를 시작할 때의 번호를 꺼내기에 넘기고, 끝났을 때 번호가 그대로일 때만 기억·표시한다(같은 경로라도 압축이 바뀌면 내용이 다름).
    /// </summary>
    int ArchiveGeneration { get; }

    /// <summary>지금 폴더의 항목(화면에 보이는 차례 — <see cref="EntrySortComparer"/>).</summary>
    IReadOnlyList<ArchiveEntry> FolderEntries { get; }

    /// <summary>이 항목을 꺼내려면 암호를 물어야 하는가(암호 항목인데 아는 암호가 없음).</summary>
    bool NeedsPassword(ArchiveEntry entry);

    /// <summary>
    /// 묻지 않고(아는 암호로) 꺼낸다. 실패는 예외. <paramref name="generation"/> 이 지금 세대가 아니면(그사이 압축이 바뀜) 꺼내지 않고
    /// <see cref="OperationCanceledException"/> — 새 압축에서 옛 항목을 꺼내거나, 옛 압축에서 새 항목을 꺼내지 않게.
    /// </summary>
    Task ExtractQuietAsync(int generation, ArchiveEntry entry, string dest, CancellationToken ct);

    /// <summary>
    /// 필요하면 암호를 묻고(틀리면 다시) 꺼낸다. 꺼냈으면 <see cref="UseOutcome.Done"/>, 암호 창을 닫았으면(또는 세대가 지났으면) <see cref="UseOutcome.Cancelled"/>,
    /// 그 밖의 실패(손상 등 — 오류 창은 이미 보임)면 <see cref="UseOutcome.Failed"/>.
    /// </summary>
    Task<UseOutcome> ExtractWithPromptAsync(int generation, ArchiveEntry entry, string dest);
}

/// <summary>고른 항목만 꺼내기(열기·복사·끌기 전 암호 확인·미리 보기 단추)의 결과.</summary>
internal enum UseOutcome
{
    /// <summary>꺼냄.</summary>
    Done,
    /// <summary>진행 창·암호 창에서 취소함.</summary>
    Cancelled,
    /// <summary>실패(오류 창을 이미 보임).</summary>
    Failed,
}

/// <summary>
/// 압축 속 그림 미리 보기(W2-04 — I3-7). 오른쪽 칸을 켜면 목록에서 고른 그림을 보여 준다(jpg·png·bmp·gif·tif·ico·webp).
/// <list type="bullet">
/// <item>이전/다음(←→)은 지금 폴더의 그림만, 목록 차례대로, 끝에서 처음으로 돈다.</item>
/// <item>확대: 맞춤 / 100% / ±25% 단계, 10~800%.</item>
/// <item>압축 크기(또는 원래 크기)가 50MB 를 넘으면 꺼내지 않고 '미리 보기에는 너무 커요'. 큰 그림은 읽는 쪽(<see cref="IImageDecoder"/>)이 줄여 읽고,
///   화소가 아주 많은 그림(파일은 작아도 — 그림 폭탄)은 읽지 않는다(<see cref="ImageTooLargeException"/> → 같은 문구).</item>
/// <item>최근 3장을 기억한다(합계 200MB 까지 — 넘으면 오래된 것부터 버림). 기억은 (압축 세대, 경로)로 한다 — 다른 압축을 열거나 고친 뒤 목록을 다시 읽으면
///   세대가 바뀌어(<see cref="IPreviewHost.ArchiveGeneration"/>) 옛 그림은 쓰지 않고, 그 전에 시작한 읽기의 결과는 기억도 표시도 하지 않는다
///   (같은 경로의 그림이라도 압축이 다르면 내용이 다름 — W2-04 1차 검증, IJ3-01 에서 세대를 화면 쪽 한 곳에서 세게 바꿈).</item>
/// <item>그림 읽기는 한 번에 하나만 — ←→ 를 누르고 있어도 읽는 그림이 겹쳐 메모리가 튀지 않게. 기다리는 동안 다른 것을 고르면 읽지 않고 그만둔다
///   (WIC 읽기는 시작하면 멈출 수 없어서).</item>
/// <item>꺼낸 임시 파일(<c>temp\preview\&lt;key&gt;\</c>)은 읽자마자 지운다.</item>
/// <item>암호를 모르면 묻지 않고 '암호를 넣으면 볼 수 있어요' 단추를 보인다(고를 때마다 암호 창이 뜨지 않게).</item>
/// </list>
/// </summary>
public sealed partial class ImagePreviewViewModel : ObservableObject
{
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp",
    };

    public const long MaxPreviewBytes = 50L * 1024 * 1024;
    public const int CacheCount = 3;
    public const long CacheBytes = 200L * 1024 * 1024;
    public const int MinZoom = 10, MaxZoom = 800, ZoomStep = 25;

    public const string NotImageMessage = "그림 파일을 고르면 여기에 보여요";
    public const string TooLargeMessage = "미리 보기에는 너무 커요";
    public const string PasswordMessage = "암호를 넣으면 볼 수 있어요";
    public const string ErrorMessage = "이 그림은 미리 볼 수 없어요";

    private readonly IPreviewHost _host;
    private readonly IImageDecoder _decoder;

    /// <summary>기억한 그림(최근 것이 앞) — (압축 세대, 경로)로 찾는다.</summary>
    private readonly LinkedList<(int Generation, string Key, DecodedImage Image)> _cache = new();

    /// <summary>그림 읽기는 한 번에 하나(<see cref="DecodeAsync"/>).</summary>
    private readonly SemaphoreSlim _decodeGate = new(1, 1);

    private CancellationTokenSource? _cts;
    private int _version;

    internal ImagePreviewViewModel(IPreviewHost host, IImageDecoder decoder)
    {
        _host = host;
        _decoder = decoder;
    }

    /// <summary>미리 보기 칸을 켰는가(설정 ui.previewVisible).</summary>
    [ObservableProperty]
    private bool _isVisible;

    /// <summary>미리 보는 항목.</summary>
    [ObservableProperty]
    private ArchiveEntry? _currentEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(IsPasswordNeeded), nameof(HasMessage))]
    private PreviewState _state = PreviewState.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = NotImageMessage;

    /// <summary>그림 원래 크기 안내('1920 × 1080').</summary>
    [ObservableProperty]
    private string _sizeText = string.Empty;

    /// <summary>맞춤(칸에 맞게)인가.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText), nameof(ZoomScale))]
    private bool _isFit = true;

    /// <summary>확대 비율(%). <see cref="IsFit"/> 이면 쓰지 않음.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText), nameof(ZoomScale))]
    private int _zoomPercent = 100;

    public bool HasImage => State == PreviewState.Image && Image is not null;
    public bool IsPasswordNeeded => State == PreviewState.NeedsPassword;
    public bool HasMessage => State != PreviewState.Image && !string.IsNullOrEmpty(Message);
    public string ZoomText => IsFit ? "맞춤" : $"{ZoomPercent}%";
    public double ZoomScale => ZoomPercent / 100.0;

    /// <summary>맞춤일 때 실제 비율(%) — 화면이 알려 준다. 맞춤에서 +/− 를 누르면 여기서부터 25% 단계로.</summary>
    public double FitPercent { get; set; } = 100;

    /// <summary>기억한 그림의 열쇠(최근 것이 앞) — 시험용.</summary>
    internal IReadOnlyList<string> CachedKeys => _cache.Select(c => c.Key).ToList();

    /// <summary>기억한 그림의 합계 바이트 — 시험용.</summary>
    internal long CachedBytes => _cache.Sum(c => c.Image.ByteSize);

    public static bool IsImage(ArchiveEntry? e) =>
        e is { IsDirectory: false } && ImageExtensions.Contains(Path.GetExtension(e.Name));

    /// <summary>너무 커서 미리 보지 않는가(압축 크기 또는 원래 크기가 50MB 초과).</summary>
    public static bool IsTooLarge(ArchiveEntry e) => e.Size > MaxPreviewBytes || e.CompressedSize > MaxPreviewBytes;

    /// <summary>지금 폴더의 그림 항목(목록 차례).</summary>
    public IReadOnlyList<ArchiveEntry> ImageEntries => _host.FolderEntries.Where(IsImage).ToList();

    /// <summary>
    /// 이 항목을 미리 본다. 그림이 아니면 꺼짐(<see cref="PreviewState.NotImage"/>), 크면 자리표시, 암호를 모르면 단추.
    /// <paramref name="askPassword"/> 면 필요할 때 암호를 묻는다(단추를 눌렀을 때).
    /// </summary>
    public async Task ShowAsync(ArchiveEntry? entry, bool askPassword = false)
    {
        int version = ++_version;
        int generation = _host.ArchiveGeneration;
        _cts?.Cancel();
        _cts = null;
        CurrentEntry = entry;

        if (entry is null)
        {
            SetMessage(PreviewState.Empty, NotImageMessage);
            return;
        }
        if (!IsImage(entry))
        {
            SetMessage(PreviewState.NotImage, NotImageMessage);
            return;
        }
        if (IsTooLarge(entry))
        {
            SetMessage(PreviewState.TooLarge, TooLargeMessage);
            return;
        }
        string key = KeyOf(entry);
        if (TryCache(generation, key) is { } cached)
        {
            SetImage(cached);
            return;
        }
        if (!askPassword && _host.NeedsPassword(entry))
        {
            SetMessage(PreviewState.NeedsPassword, PasswordMessage);
            return;
        }

        SetMessage(PreviewState.Loading, "불러오는 중…");
        var cts = new CancellationTokenSource();
        _cts = cts;
        string dest = Path.Combine(AppPaths.PreviewRoot, Guid.NewGuid().ToString("N"));
        DecodedImage? decoded = null;
        try
        {
            if (askPassword)
            {
                var outcome = await _host.ExtractWithPromptAsync(generation, entry, dest);
                if (outcome != UseOutcome.Done)
                {
                    // 암호 창을 닫았으면 단추를 다시, 그 밖의 실패(손상 등 — 오류 창은 이미 보임)는 '미리 볼 수 없어요'(암호 단추를 또 내밀지 않게)
                    if (version == _version && generation == _host.ArchiveGeneration)
                    {
                        if (outcome == UseOutcome.Cancelled) SetMessage(PreviewState.NeedsPassword, PasswordMessage);
                        else SetMessage(PreviewState.Error, ErrorMessage);
                    }
                    return;
                }
            }
            else
            {
                await _host.ExtractQuietAsync(generation, entry, dest, cts.Token);
            }
            string file = LazyEntryExtractor.FindExtracted(dest, DragPayloadBuilder.NormalizeKey(entry.Path))
                          ?? throw new FileNotFoundException("꺼낸 그림을 찾지 못했어요", entry.Path);
            decoded = await DecodeAsync(file, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;   // 다른 것을 골랐다(또는 압축이 바뀜)
        }
        catch (ArchivePasswordException)
        {
            if (version == _version && generation == _host.ArchiveGeneration) SetMessage(PreviewState.NeedsPassword, PasswordMessage);
            return;
        }
        catch (ImageTooLargeException ex)
        {
            // 파일은 작아도 화소가 아주 많은 그림 — 읽지 않음(메모리 보호, IJ3-01)
            Logger.LogError("미리 보기: 화소가 너무 많아 읽지 않음: " + entry.Path, ex);
            if (version == _version && generation == _host.ArchiveGeneration) SetMessage(PreviewState.TooLarge, TooLargeMessage);
            return;
        }
        catch (Exception ex)
        {
            Logger.LogError("미리 보기 실패: " + entry.Path, ex);
            if (version == _version && generation == _host.ArchiveGeneration) SetMessage(PreviewState.Error, ErrorMessage);
            return;
        }
        finally
        {
            TempFolders.DeleteBestEffort(dest);   // 읽은 뒤 바로 지운다(OnLoad 로 다 읽어 파일을 놓았음)
        }

        if (generation != _host.ArchiveGeneration)
        {
            // 그사이 압축이 바뀌었다(다른 압축·다시 열기·고친 뒤 다시 읽기) — 옛 압축의 그림은 기억하지도 보이지도 않는다(같은 경로라도 내용이 다름).
            // 화면이 아직 이 읽기를 기다리는 중이면(세대만 바뀌고 비우지 않은 경우) '불러오는 중'에 멈추지 않게 비운다.
            if (version == _version)
            {
                CurrentEntry = null;
                SetMessage(PreviewState.Empty, NotImageMessage);
            }
            return;
        }
        AddToCache(generation, key, decoded);
        if (version != _version) return;   // 그사이 다른 항목을 골랐다(기억만 해 둠)
        SetImage(decoded);
    }

    /// <summary>
    /// 그림 읽기 — 한 번에 하나만. 앞 읽기가 끝나기를 기다리는 동안 다른 것을 고르면(<paramref name="ct"/>) 읽지 않고 그만둔다.
    /// 시작한 읽기는 끝까지 돈다(WIC 는 도중에 멈출 수 없음) — 그래서 겹치지 않게 막는다.
    /// </summary>
    private async Task<DecodedImage> DecodeAsync(string file, CancellationToken ct)
    {
        await _decodeGate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await Task.Run(() => _decoder.Decode(file), ct);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    [RelayCommand]
    private Task Next() => Step(+1);

    [RelayCommand]
    private Task Prev() => Step(-1);

    /// <summary>이전/다음 그림 — 끝에서 처음으로 돈다. 지금 것이 그림이 아니면 첫(끝) 그림부터.</summary>
    private Task Step(int delta)
    {
        var list = ImageEntries;
        if (list.Count == 0) return Task.CompletedTask;
        int i = CurrentEntry is null ? -1 : IndexOf(list, CurrentEntry);
        int next = i < 0 ? (delta > 0 ? 0 : list.Count - 1) : ((i + delta) % list.Count + list.Count) % list.Count;
        return ShowAsync(list[next]);
    }

    /// <summary>'암호를 넣으면 볼 수 있어요' 단추.</summary>
    [RelayCommand]
    private Task EnterPassword() => ShowAsync(CurrentEntry, askPassword: true);

    [RelayCommand]
    private void ZoomIn()
    {
        double baseP = IsFit ? FitPercent : ZoomPercent;
        int next = (int)Math.Floor(baseP / ZoomStep) * ZoomStep + ZoomStep;
        SetZoom(next);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        double baseP = IsFit ? FitPercent : ZoomPercent;
        int next = (int)Math.Ceiling(baseP / ZoomStep) * ZoomStep - ZoomStep;
        SetZoom(next);
    }

    [RelayCommand]
    private void ZoomActual() => SetZoom(100);

    [RelayCommand]
    private void ZoomFit() => IsFit = true;

    [RelayCommand]
    private void Toggle() => IsVisible = !IsVisible;

    private void SetZoom(int percent)
    {
        ZoomPercent = Math.Clamp(percent, MinZoom, MaxZoom);
        IsFit = false;
    }

    /// <summary>다른 압축을 열거나 닫았다(화면이 세대를 올린 뒤 부름) — 기억한 그림을 버리고 비운다. 읽던 것은 끝나도 세대가 달라 버려진다.</summary>
    public void Reset()
    {
        ++_version;
        _cts?.Cancel();
        _cts = null;
        _cache.Clear();
        CurrentEntry = null;
        SetMessage(PreviewState.Empty, NotImageMessage);
    }

    private void SetMessage(PreviewState state, string message)
    {
        Image = null;
        SizeText = string.Empty;
        Message = message;
        State = state;
    }

    private void SetImage(DecodedImage img)
    {
        Image = img.Source;
        SizeText = $"{img.OriginalWidth} × {img.OriginalHeight}";
        Message = string.Empty;
        IsFit = true;
        State = PreviewState.Image;
    }

    private DecodedImage? TryCache(int generation, string key)
    {
        for (var node = _cache.First; node is not null; node = node.Next)
        {
            if (node.Value.Generation != generation || node.Value.Key != key) continue;
            _cache.Remove(node);
            _cache.AddFirst(node);
            return node.Value.Image;
        }
        return null;
    }

    private void AddToCache(int generation, string key, DecodedImage img)
    {
        // 옛 세대의 것은 다시 쓸 일이 없어 버린다(세대만 바뀌고 Reset 이 불리지 않은 경우에도 메모리를 잡고 있지 않게)
        for (var node = _cache.First; node is not null;)
        {
            var next = node.Next;
            if (node.Value.Generation != generation || node.Value.Key == key) _cache.Remove(node);
            node = next;
        }
        _cache.AddFirst((generation, key, img));
        // 3장, 합계 200MB — 넘으면 오래된 것부터(방금 넣은 것은 혼자 넘어도 둔다)
        while (_cache.Count > CacheCount || (_cache.Count > 1 && CachedBytes > CacheBytes))
            _cache.RemoveLast();
    }

    /// <summary>
    /// 기억(캐시)·자리 찾기 열쇠 = 압축 안 경로 그대로(대소문자를 가림). 리눅스·맥에서 만든 압축엔 대소문자만 다른 두 그림('A.png'·'a.png')이 있을 수 있는데,
    /// 예전엔 대문자로 바꿔 비교해 'A.png' 를 본 뒤 'a.png' 를 고르면 기억한 'A.png' 를 그대로 보였고 → 로 넘기면 'a.png' 에서 멈췄다(W2-04 2차 검증).
    /// </summary>
    private static string KeyOf(ArchiveEntry e) => DragPayloadBuilder.NormalizeKey(e.Path);

    /// <summary>목록에서 이 항목의 자리 — 같은 객체를 먼저, 없으면(목록을 다시 읽음) 경로가 대소문자까지 같은 것.</summary>
    private static int IndexOf(IReadOnlyList<ArchiveEntry> list, ArchiveEntry e)
    {
        for (int i = 0; i < list.Count; i++)
            if (ReferenceEquals(list[i], e)) return i;
        string k = KeyOf(e);
        for (int i = 0; i < list.Count; i++)
            if (string.Equals(KeyOf(list[i]), k, StringComparison.Ordinal)) return i;
        return -1;
    }
}
