using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;
using Iljip.Views;

namespace Iljip.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ArchiveServiceLocator _locator;
    private readonly IUserInteraction _ui;

    /// <summary>
    /// 지금 열린 압축에 맞았던 암호 — 열 때 받은 것(파일 이름까지 암호가 걸린 7z, I1-7)이나 풀 때 맞은 것.
    /// 같은 압축을 다시 풀 때 묻지 않으려고 기억한다. 다른 압축을 열거나 비우면 지운다(W1-B 보관본과 같은 모양).
    /// </summary>
    private string? _archivePassword;

    /// <summary>XAML(MainWindow 의 DataContext)용 — 실제 WPF 창을 쓴다.</summary>
    public MainViewModel() : this(null, null)
    {
    }

    /// <param name="ui">사용자에게 묻고 알리는 창. null 이면 실제 WPF 창(<see cref="WpfUserInteraction"/>). 테스트는 가짜를 넣는다.</param>
    /// <param name="locator">형식 라우터. null 이면 기본.</param>
    public MainViewModel(IUserInteraction? ui, ArchiveServiceLocator? locator = null)
    {
        _ui = ui ?? new WpfUserInteraction();
        _locator = locator ?? new ArchiveServiceLocator();
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsStagingMode));
            OnPropertyChanged(nameof(IsEmptyState));
            OnPropertyChanged(nameof(HasAnyContent));
        };
    }

    /// <summary>현재 열려 있는 압축 파일 경로 (null이면 빈 상태 또는 Staging)</summary>
    [ObservableProperty]
    private string? _currentArchivePath;

    public string WindowTitle =>
        IsArchiveMode
            ? $"{Path.GetFileName(CurrentArchivePath!)} - 일집"
            : IsStagingMode
                ? $"새 압축 ({Entries.Count}개 항목) - 일집"
                : "일집";

    partial void OnCurrentArchivePathChanged(string? value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HasArchiveOpen));
        OnPropertyChanged(nameof(IsArchiveMode));
        OnPropertyChanged(nameof(IsStagingMode));
        OnPropertyChanged(nameof(IsEmptyState));
        OnPropertyChanged(nameof(HasAnyContent));
    }

    // ===== 모드 판별 =====
    public bool HasArchiveOpen => !string.IsNullOrEmpty(CurrentArchivePath);
    public bool IsArchiveMode => HasArchiveOpen;
    public bool IsStagingMode => !HasArchiveOpen && Entries.Count > 0;
    public bool IsEmptyState => !HasArchiveOpen && Entries.Count == 0;
    public bool HasAnyContent => HasArchiveOpen || Entries.Count > 0;

    /// <summary>현재 압축 파일의 전체 항목 목록 또는 압축 대기 중인 파일 목록 (원본).</summary>
    public ObservableCollection<ArchiveEntry> Entries { get; } = new();

    /// <summary>좌측 폴더 트리 (아카이브 모드에서만 채워짐).</summary>
    public ObservableCollection<ArchiveTreeNode> FolderTree { get; } = new();

    /// <summary>우측 목록에 실제로 표시되는 항목. 아카이브 모드: 선택 폴더의 직속 / 그 외: Entries 전체. 늘 <see cref="SortMember"/> 순.</summary>
    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = new();

    /// <summary>트리에서 선택된 폴더 노드.</summary>
    [ObservableProperty]
    private ArchiveTreeNode? _selectedFolder;

    partial void OnSelectedFolderChanged(ArchiveTreeNode? value) => RefreshVisibleEntries();

    [ObservableProperty]
    private string _statusText = "준비됨";

    // ===== 정렬 (I0-5) =====

    /// <summary>목록 정렬 기준 열(화면 DataGrid 의 SortMemberPath). 기본은 이름 — 예전 목록 순서 그대로.</summary>
    [ObservableProperty]
    private string _sortMember = EntrySortComparer.Name;

    /// <summary>내림차순인가.</summary>
    [ObservableProperty]
    private bool _sortDescending;

    /// <summary>
    /// 열 머리를 눌렀을 때: 같은 열이면 방향을 뒤집고, 다른 열이면 그 열 오름차순. 폴더는 늘 먼저(<see cref="EntrySortComparer"/>).
    /// 정렬은 여기(VM)가 맡는다 — 폴더를 옮겨 다니거나 다른 압축을 열어도 유지되고, 항목이 많아도 한 번 정렬(O(N log N))로 끝난다.
    /// </summary>
    public void SortBy(string? member)
    {
        if (!EntrySortComparer.IsSortable(member)) return;
        if (member == SortMember)
            SortDescending = !SortDescending;
        else
        {
            SortMember = member!;
            SortDescending = false;
        }
        RefreshVisibleEntries();
    }

    // ===== Commands =====

    [RelayCommand]
    private async Task OpenAsync()
    {
        var picked = _ui.PickFiles("압축 파일 열기", BuildOpenFilter(), multiselect: false);
        if (picked is not { Count: > 0 }) return;
        await OpenArchiveAsync(picked[0]);
    }

    /// <summary>
    /// 압축 파일을 열어 목록을 채운다. 열었으면 true(못 열면 오류 창을 보이고 false — 보던 것은 그대로).
    /// 파일 이름까지 암호가 걸린 7z(헤더 암호 — 반디집 '파일 이름 암호화')는 목록부터 암호가 필요해 여기서 암호를 묻고 다시 연다(I1-7 의 화면 쪽).
    /// 틀리면 다시 묻고, 취소하면 아무것도 바꾸지 않는다(false). 맞은 암호는 같은 압축을 풀 때 다시 묻지 않게 기억한다.
    /// (W1-D 1차 검증: 우클릭 풀기를 '열기에 성공해야 푼다'로 바꾸며 헤더 암호 7z 를 어느 경로로도 못 풀게 된 회귀를 이것으로 고침)
    /// </summary>
    public async Task<bool> OpenArchiveAsync(string archivePath)
    {
        // W2-01: 확장자로 먼저, 모르는 확장자면 내용(서명)으로 — 이름이 바뀐 압축(.bin 이 된 7z 등)·자동 풀림 exe 도 연다(B-086)
        var service = _locator.ResolveByContent(archivePath);
        if (service is null)
        {
            _ui.ShowWarning($"지원하지 않는 형식이에요:\n{Path.GetExtension(archivePath)}");
            return false;
        }

        string? password = null;
        StatusText = "압축 파일을 읽는 중...";
        while (true)
        {
            try
            {
                var entries = await service.ListEntriesAsync(archivePath, password);

                Entries.Clear();
                foreach (var e in entries)
                    Entries.Add(e);

                CurrentArchivePath = archivePath;
                _archivePassword = string.IsNullOrEmpty(password) ? null : password;
                BuildFolderTree();
                StatusText = $"{entries.Count}개 항목";
                return true;
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                // 암호는 [취소]할 때까지 다시 묻는다(풀 때와 같음)
                var reason = string.IsNullOrEmpty(password) ? PasswordPromptReason.Required : PasswordPromptReason.Wrong;
                var pw = _ui.AskPassword(Path.GetFileName(archivePath), reason, PasswordPromptMessage(ex, reason));
                if (pw is null)
                {
                    StatusText = "열기를 취소했어요";
                    return false;
                }
                password = pw;
            }
            catch (Exception ex)
            {
                Logger.LogError($"열기 실패: {archivePath}", ex);
                _ui.ShowError($"파일을 열 수 없어요:\n\n{ErrorMessages.ToUserMessage(ex)}");
                StatusText = "오류 발생";
                return false;
            }
        }
    }

    /// <summary>암호 창 문구: 일집 암호 예외면 그 문구('암호가 걸린 압축 파일이에요…'·'파일 이름까지…'·'암호가 맞지 않아요…'), 엔진 예외면 예전 문구.</summary>
    private static string PasswordPromptMessage(Exception ex, PasswordPromptReason reason) =>
        ex is ArchivePasswordException
            ? ex.Message
            : reason == PasswordPromptReason.Required
                ? "이 압축 파일은 비밀번호가 필요해요."
                : "비밀번호가 틀린 것 같아요. 다시 입력해주세요.";

    /// <summary>파일/폴더 선택 다이얼로그를 띄워 Staging에 추가.</summary>
    [RelayCommand]
    private void AddFiles()
    {
        var picked = _ui.PickFiles("압축할 파일 선택 (여러 개 가능)", null, multiselect: true);
        if (picked is not { Count: > 0 }) return;
        AddToStaging(picked);
    }

    /// <summary>폴더를 Staging에 추가.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var folder = _ui.PickFolder("압축할 폴더 선택");
        if (folder is null) return;
        AddToStaging(new[] { folder });
    }

    /// <summary>
    /// 임시 압축 목록에 파일/폴더 경로들을 추가. 압축 파일을 보는 중이면 아무것도 하지 않는다 —
    /// 보던 압축을 닫고 새 목록을 만드는 것은 사용자가 그렇게 고른 경우에만(<see cref="StartNewStaging"/>, I0-4).
    /// (예전엔 여기서 열린 압축을 말없이 닫아, 압축을 보다가 파일을 끌어 놓으면 보던 압축이 사라졌다)
    /// </summary>
    public void AddToStaging(IEnumerable<string> paths)
    {
        if (IsArchiveMode) return;

        var existingSources = Entries
            .Where(e => !string.IsNullOrEmpty(e.SourcePath))
            .Select(e => e.SourcePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var p in paths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (existingSources.Contains(p)) continue;

            ArchiveEntry? entry = null;
            try
            {
                if (File.Exists(p))
                {
                    var fi = new FileInfo(p);
                    entry = new ArchiveEntry
                    {
                        Path = fi.Name,
                        Size = fi.Length,
                        IsDirectory = false,
                        LastModified = fi.LastWriteTime,
                        SourcePath = p
                    };
                }
                else if (Directory.Exists(p))
                {
                    var di = new DirectoryInfo(p);
                    entry = new ArchiveEntry
                    {
                        Path = di.Name,
                        Size = 0, // 폴더 크기는 계산 비용이 커서 압축 시 누적
                        IsDirectory = true,
                        LastModified = di.LastWriteTime,
                        SourcePath = p
                    };
                }
            }
            catch
            {
                // 권한 등 — 무시
            }

            if (entry is not null)
            {
                Entries.Add(entry);
                existingSources.Add(p);
                added++;
            }
        }

        if (added > 0)
        {
            StatusText = $"압축 대기 {Entries.Count}개 항목 — 압축 버튼을 눌러주세요";
            OnPropertyChanged(nameof(WindowTitle));
        }

        RefreshVisibleEntries();
    }

    /// <summary>보던 압축(또는 모으던 목록)을 닫고 새 압축할 목록을 만든다 — 사용자가 고른 경우에만 부른다(I0-4).</summary>
    public void StartNewStaging(IEnumerable<string> paths)
    {
        CloseView();
        AddToStaging(paths);
    }

    /// <summary>Staging 목록을 실제로 압축한다.</summary>
    [RelayCommand]
    private async Task CompressAsync()
    {
        if (!IsStagingMode)
        {
            _ui.ShowInfo("압축할 파일을 먼저 드래그하거나 [추가] 버튼으로 넣어주세요.");
            return;
        }

        var sourcePaths = Entries
            .Select(e => e.SourcePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (sourcePaths.Count == 0) return;

        // 출력 이름 제안
        string defaultDir = Path.GetDirectoryName(sourcePaths[0]) ?? Environment.CurrentDirectory;
        string defaultName;
        if (sourcePaths.Count == 1)
        {
            defaultName = SuggestFileBaseName(sourcePaths[0]) + ".zip";
        }
        else
        {
            defaultName = SanitizeFileName(new DirectoryInfo(defaultDir).Name) + ".zip";
        }

        string? savePath = _ui.AskSavePath("압축 파일로 저장", _locator.BuildSaveFilter(), defaultName, defaultDir, ".zip");
        if (savePath is null) return;

        // 만들 형식은 확장자로만 고른다(덮어쓸 기존 파일의 내용은 보지 않음)
        var service = _locator.ResolveByExtension(savePath) ?? _locator.GetDefaultForCompression();

        var compressionOptions = _ui.AskCompressionOptions(Path.GetExtension(savePath));
        if (compressionOptions is null) return;

        // 실패 정리 때 '이번에 만들거나 고친 파일'만 지우려고 시작 전 모습을 적어 둔다 — 원본을 못 읽어(SourceUnreadable 등)
        // 압축 파일을 열기도 전에 끝났는데 덮어쓰려던 기존 파일을 지우는 일이 없게.
        var before = OutputStamp.Of(savePath);
        var session = _ui.ShowProgress("압축 중...");
        CompressResult result;
        try
        {
            result = await service.CompressAsync(
                sourcePaths,
                savePath,
                options: compressionOptions,
                progress: session.Progress,
                cancellationToken: session.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusText = "압축 취소됨";
            DeletePartialOutput(savePath, before);
            return;
        }
        catch (NothingToCompressException ex)
        {
            // 하나도 못 담았다(I0-2) — 서비스가 압축 파일을 만들지 않았다. 빈 압축을 남기지 않고 무엇을 못 읽었는지 알린다.
            DeletePartialOutput(savePath, before);
            session.Close();
            _ui.ShowError(NothingCompressedMessage(ex.Skipped));
            StatusText = "오류 발생";
            return;
        }
        catch (Exception ex)
        {
            // 비취소 실패(I/O·디스크풀·소스 변경 등)로도 부분/손상 아카이브가 디스크에 남아 정상
            // 파일로 오인될 수 있다. 취소 경로와 동일하게 부분 산출물을 정리한 뒤 오류를 알린다.
            DeletePartialOutput(savePath, before);
            session.Close();
            Logger.LogError($"압축 실패: {savePath}", ex);
            _ui.ShowError($"압축 실패:\n\n{ErrorMessages.ToUserMessage(ex)}");
            StatusText = "오류 발생";
            return;
        }
        finally
        {
            session.Close();
        }

        // 건너뛴 파일이 있어도 성공이다 — 결과물은 지우지 않고, 연 뒤에 무엇이 빠졌는지 알린다(I0-2)
        StatusText = $"압축 완료: {savePath}";

        // 결과 압축 파일을 자동으로 열어줌 (Staging 비워지고 Archive 모드로 전환)
        await OpenArchiveAsync(savePath);

        if (result.Skipped.Count > 0)
            _ui.ShowInfo(SkippedMessage(result.Skipped));
    }

    /// <summary>건너뛴 원본 안내 문구(최대 10줄, 나머지는 '외 N개').</summary>
    internal static string SkippedMessage(IReadOnlyList<SkippedSource> skipped) =>
        SkippedLines($"압축을 마쳤어요. 건너뛴 파일 {skipped.Count}개:", skipped);

    /// <summary>하나도 못 담아 압축 파일을 만들지 않았을 때의 오류 문구(I0-2).</summary>
    internal static string NothingCompressedMessage(IReadOnlyList<SkippedSource> skipped) =>
        SkippedLines($"{NothingToCompressException.DefaultMessage}\n건너뛴 파일 {skipped.Count}개:", skipped)
        + "\n\n다른 프로그램이 쓰고 있으면 닫고 다시 해 주세요.";

    private static string SkippedLines(string header, IReadOnlyList<SkippedSource> skipped)
    {
        const int MaxLines = 10;
        var lines = new List<string> { header };
        foreach (var s in skipped.Take(MaxLines))
        {
            string name = Path.GetFileName(s.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = s.Path;
            bool isFolder = Directory.Exists(s.Path);
            lines.Add(isFolder ? $"- {name} 폴더 ({s.Reason})" : $"- {name} ({s.Reason})");
        }
        if (skipped.Count > MaxLines)
            lines.Add($"외 {skipped.Count - MaxLines}개");
        return string.Join("\n", lines);
    }

    /// <summary>압축 실패·취소 때 부분 산출물 지우기 — 시작 전과 달라졌을 때만(새로 생겼거나 고쳐 쓰였을 때).</summary>
    private static void DeletePartialOutput(string path, OutputStamp before)
    {
        if (OutputStamp.Of(path) == before) return;   // 손대지 않은 기존 파일(또는 처음부터 없음)
        try { File.Delete(path); } catch { /* 정리 실패는 무시 */ }
    }

    /// <summary>파일이 있는지·크기·수정 시각(바뀌었는지만 본다).</summary>
    private readonly record struct OutputStamp(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static OutputStamp Of(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists ? new OutputStamp(true, fi.Length, fi.LastWriteTimeUtc) : default;
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>
    /// 단일 소스 경로에서 SaveFileDialog 기본 파일명(확장자 제외)을 안전하게 만든다.
    /// 확장자만 있는 파일(.gitignore)·드라이브 루트(C:\)·금지문자 포함 경로에서도
    /// 비정상 기본명이 만들어지지 않도록 단계적으로 폴백한다.
    /// </summary>
    private static string SuggestFileBaseName(string path)
    {
        // 1) 확장자를 뗀 파일명
        string name = Path.GetFileNameWithoutExtension(path);
        // 2) 비면(점으로 시작하는 파일 등) 확장자 포함 파일명
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(path);
        // 3) 그래도 비면(드라이브 루트/구분자로 끝나는 경로) 디렉터리명
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = new DirectoryInfo(path).Name; } catch { /* 잘못된 경로 */ }
        }
        return SanitizeFileName(name);
    }

    /// <summary>파일명에서 금지문자를 제거하고, 결과가 비면 "archive"로 대체한다.</summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "archive";

        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), string.Empty);

        // Windows는 끝에 마침표/공백을 허용하지 않음
        name = name.Trim().TrimEnd('.').Trim();
        return string.IsNullOrWhiteSpace(name) ? "archive" : name;
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (!IsArchiveMode || string.IsNullOrEmpty(CurrentArchivePath))
        {
            _ui.ShowInfo("먼저 압축 파일을 열어주세요.");
            return;
        }

        var folder = _ui.PickFolder("압축을 풀 폴더를 선택하세요");
        if (folder is null) return;

        await ExtractToFolderAsync(CurrentArchivePath, folder);
    }

    /// <summary>
    /// 지정한 폴더로 압축 해제. UI 진행률 + 비밀번호 재시도 포함.
    /// CLI / 셸 확장 / 기존 ExtractCommand 모두에서 호출.
    /// 암호는 사용자가 [취소]할 때까지 몇 번이고 다시 묻는다(반디집과 같음 — B-067). 예전엔 암호 창을 세 번 띄우고도 세 번째로 넣은 암호는
    /// 써 보지 않은 채 '3회 모두 일치하지 않아'로 끝났다(W1-D 1차 검증). 이 압축을 열 때·앞서 풀 때 맞은 암호가 있으면 그것부터 쓴다.
    /// </summary>
    /// <param name="selectedPaths">지정하면 해당 항목들만 해제. null이면 전체.</param>
    /// <returns>다 풀었으면 true. 취소·실패(오류 창을 이미 보임)·암호 창 취소면 false.</returns>
    public async Task<bool> ExtractToFolderAsync(string archivePath, string destFolder, IReadOnlyCollection<string>? selectedPaths = null)
    {
        var service = _locator.ResolveByContent(archivePath);
        if (service is null) return false;

        bool sameArchive = string.Equals(archivePath, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        while (true)
        {
            var session = _ui.ShowProgress("압축 해제 중...");
            try
            {
                if (selectedPaths is null)
                    await service.ExtractAsync(
                        archivePath,
                        destFolder,
                        password: password,
                        progress: session.Progress,
                        cancellationToken: session.CancellationToken);
                else
                    await service.ExtractEntriesAsync(
                        archivePath,
                        destFolder,
                        selectedPaths,
                        password: password,
                        progress: session.Progress,
                        cancellationToken: session.CancellationToken);

                StatusText = $"압축 해제 완료: {destFolder}";
                if (sameArchive && !string.IsNullOrEmpty(password)) _archivePassword = password;
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusText = "압축 해제 취소됨";
                return false;
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                // 비밀번호 프롬프트 뒤에 진행 창이 남지 않도록 먼저 닫는다(finally 가 다시 닫아도 안전).
                session.Close();
                // 이유 한 줄: 일집 암호 예외는 그 문구('암호가 걸린 압축 파일이에요…' / '암호가 맞지 않아요…'), 엔진 예외는 예전 문구
                var reason = string.IsNullOrEmpty(password) ? PasswordPromptReason.Required : PasswordPromptReason.Wrong;
                var pw = _ui.AskPassword(Path.GetFileName(archivePath), reason, PasswordPromptMessage(ex, reason));
                if (pw is null)
                {
                    StatusText = "압축 해제 취소됨";
                    return false;
                }
                password = pw;
            }
            catch (Exception ex)
            {
                session.Close();
                Logger.LogError($"압축 해제 실패: {archivePath}", ex);
                _ui.ShowError($"압축 해제 실패:\n\n{ErrorMessages.ToUserMessage(ex)}");
                StatusText = "오류 발생";
                return false;
            }
            finally
            {
                session.Close();
            }
        }
    }

    /// <summary>목록에서 선택한 항목들만 해제. 폴더 선택 시 그 하위 전체 포함.</summary>
    public async Task ExtractSelectedAsync(IEnumerable<ArchiveEntry> selected)
    {
        if (!IsArchiveMode || string.IsNullOrEmpty(CurrentArchivePath))
            return;

        var paths = selected
            .Select(e => e.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0) return;

        var folder = _ui.PickFolder("선택한 항목을 풀 폴더를 선택하세요");
        if (folder is null) return;

        await ExtractToFolderAsync(CurrentArchivePath, folder, paths);
    }

    /// <summary>우측 목록에서 폴더를 더블클릭했을 때 해당 폴더로 이동.</summary>
    public void NavigateToFolder(string folderFullPath)
    {
        var node = FindNode(FolderTree, folderFullPath);
        if (node is not null)
            SelectedFolder = node; // → RefreshVisibleEntries
    }

    private static ArchiveTreeNode? FindNode(IEnumerable<ArchiveTreeNode> nodes, string fullPath)
    {
        foreach (var n in nodes)
        {
            if (string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return n;
            var found = FindNode(n.Children, fullPath);
            if (found is not null) return found;
        }
        return null;
    }

    /// <summary>Entries(전체 평면 목록)로부터 폴더 트리를 구성한다.</summary>
    private void BuildFolderTree()
    {
        FolderTree.Clear();

        string rootName = string.IsNullOrEmpty(CurrentArchivePath)
            ? "/"
            : Path.GetFileName(CurrentArchivePath);

        var root = new ArchiveTreeNode
        {
            Name = rootName,
            FullPath = string.Empty,
            IsExpanded = true
        };
        var map = new Dictionary<string, ArchiveTreeNode>(StringComparer.OrdinalIgnoreCase)
        {
            [string.Empty] = root
        };

        foreach (var entry in Entries)
        {
            string normalized = entry.Path.Replace('\\', '/');
            if (entry.IsDirectory)
            {
                EnsureFolder(map, root, normalized.TrimEnd('/'));
            }
            else
            {
                var folder = EnsureFolder(map, root, GetDirectory(normalized));
                folder.Files.Add(entry);
            }
        }

        FolderTree.Add(root);
        SelectedFolder = root; // → RefreshVisibleEntries
    }

    /// <summary>
    /// 선택 폴더 기준으로 우측 표시 목록을 갱신. 아카이브 모드가 아니면 Entries 전체.
    /// 정렬은 <see cref="SortMember"/>·<see cref="SortDescending"/>(기본 이름 오름차순, 폴더 먼저 — 예전 순서와 같음).
    /// </summary>
    private void RefreshVisibleEntries()
    {
        VisibleEntries.Clear();

        IEnumerable<ArchiveEntry> items;
        if (IsArchiveMode && SelectedFolder is not null)
        {
            items = SelectedFolder.Children
                .Select(child => new ArchiveEntry
                {
                    Path = child.FullPath,
                    IsDirectory = true,
                    Size = -1,        // 폴더는 크기 칸 비움 (BytesToHuman이 음수를 빈 문자열로 처리)
                    CompressedSize = -1
                })
                .Concat(SelectedFolder.Files);
        }
        else
        {
            items = Entries;
        }

        // OrderBy 는 안정 정렬(같은 값끼리 원래 차례 유지) — 비교기도 값이 같으면 이름으로 차례를 정한다
        foreach (var e in items.OrderBy(e => e, new EntrySortComparer(SortMember, SortDescending)).ToList())
            VisibleEntries.Add(e);
    }

    private static string GetDirectory(string path)
    {
        int i = path.LastIndexOf('/');
        return i < 0 ? string.Empty : path.Substring(0, i);
    }

    private static ArchiveTreeNode EnsureFolder(
        Dictionary<string, ArchiveTreeNode> map, ArchiveTreeNode root, string dir)
    {
        dir = dir.Trim('/');
        if (string.IsNullOrEmpty(dir)) return root;
        if (map.TryGetValue(dir, out var existing)) return existing;

        string cur = string.Empty;
        var parent = root;
        foreach (var part in dir.Split('/'))
        {
            if (string.IsNullOrEmpty(part)) continue;
            cur = string.IsNullOrEmpty(cur) ? part : cur + "/" + part;
            if (!map.TryGetValue(cur, out var node))
            {
                node = new ArchiveTreeNode { Name = part, FullPath = cur };
                parent.Children.Add(node);
                map[cur] = node;
            }
            parent = node;
        }
        return parent;
    }

    /// <summary>현재 상태(압축 보기/스테이징)를 비우고 빈 상태로.</summary>
    [RelayCommand]
    private void Clear()
    {
        CloseView();
        StatusText = "준비됨";
    }

    /// <summary>보던 압축·모으던 목록을 모두 비운다(상태 줄은 그대로).</summary>
    private void CloseView()
    {
        Entries.Clear();
        CurrentArchivePath = null;
        _archivePassword = null;
        FolderTree.Clear();
        SelectedFolder = null;
        VisibleEntries.Clear();
    }

    /// <summary>
    /// 스테이징(압축 대기) 목록에서 선택한 항목들만 제거.
    /// 압축 파일을 열어 보는 중(Archive 모드)에는 동작하지 않는다 — 압축 내용은 여기서 못 지움.
    /// </summary>
    public void RemoveEntries(IEnumerable<ArchiveEntry> toRemove)
    {
        if (!IsStagingMode) return;

        var targets = toRemove as IReadOnlyList<ArchiveEntry> ?? toRemove.ToList();
        if (targets.Count == 0) return;

        foreach (var e in targets)
            Entries.Remove(e);

        RefreshVisibleEntries();

        StatusText = Entries.Count > 0
            ? $"압축 대기 {Entries.Count}개 항목 — 압축 버튼을 눌러주세요"
            : "준비됨";
    }

    [RelayCommand]
    private void CloseWindow()
    {
        Application.Current?.MainWindow?.Close();
    }

    /// <summary>
    /// 끌어 놓기(와 키워드 없이 경로만 준 명령줄) 처리 — <see cref="DropPolicy"/>(I0-4):
    /// 빈 창이면 압축 파일 1개는 열고 그 밖은 압축할 목록에. 목록을 모으는 중이면 일반 파일은 더하고, 압축 파일 1개는 묻는다.
    /// 압축을 보는 중이면 늘 묻는다(보던 압축을 말없이 닫지 않음).
    /// 압축 파일인지는 확장자로만 본다 — 내용으로 보면 .docx·.xlsx(속은 ZIP)를 압축하려고 끌어 놓아도 '열기'가 돼 버린다.
    /// </summary>
    public async Task HandleDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        var state = IsArchiveMode ? ViewState.Archive : IsStagingMode ? ViewState.Staging : ViewState.Empty;
        var decision = DropPolicy.Decide(state, paths, IsArchiveFileByExtension);
        var choice = decision.Kind switch
        {
            DropDecisionKind.OpenDirect => DropChoice.OpenArchive,
            DropDecisionKind.StageDirect => DropChoice.AddToStaging,
            _ => AskDropChoice(decision, state),
        };

        switch (choice)
        {
            case DropChoice.OpenArchive:
                await OpenArchiveAsync(paths[0]);   // 열리면 목록이 바뀌고, 못 열면(오류 창) 보던 것·모으던 목록은 그대로
                break;
            case DropChoice.AddToStaging:
                AddToStaging(paths);
                break;
            case DropChoice.NewStaging:
                StartNewStaging(paths);
                break;
            default:
                break;   // 취소 — 아무것도 바꾸지 않는다
        }
    }

    private DropChoice AskDropChoice(DropDecision decision, ViewState state)
    {
        var labels = decision.Options.Select(o => DropPolicy.Label(o, state)).ToList();
        // 기본 버튼(Enter)은 [취소] — Enter 한 번에 보던 압축이 닫히거나 모으던 목록이 비워지지 않게
        int index = _ui.Choose(decision.Message ?? string.Empty, labels, decision.DefaultIndex);
        return index >= 0 && index < decision.Options.Count ? decision.Options[index] : DropChoice.Cancel;
    }

    private bool IsArchiveFileByExtension(string path) => File.Exists(path) && _locator.ResolveByExtension(path) is not null;

    /// <summary>
    /// 명령줄 인자를 받아 적절한 동작 수행. 셸 확장이 호출하는 진입점. 해석은 <see cref="CommandLineParser"/>(순수 함수).
    /// 인자 형식:
    ///   --open &lt;archive&gt;               압축 파일을 열기만 함
    ///   --extract &lt;archive&gt;            폴더 선택 다이얼로그로 압축 해제
    ///   --extract-here &lt;archive&gt;       압축 파일이 있는 폴더에 바로 해제(반디집 '여기에 풀기')
    ///   --extract-to-folder &lt;archive&gt;  '부모\이름' 폴더에 해제. 그 이름이 이미 있으면 '이름 (2)' …(합치지 않음)
    ///   --extract-to &lt;archive&gt; &lt;dst&gt;   지정 폴더로 해제
    ///   --compress &lt;p1&gt; &lt;p2&gt; ...       인자들을 Staging에 추가 (압축 다이얼로그 자동 진행)
    ///   &lt;path&gt;...                       경로만 주어지면 드롭과 동일한 동작
    /// (W1-D 전에는 '--extract-here' 가 '이름' 폴더에 풀었다 — 그 동작은 '--extract-to-folder' 로 옮김)
    /// </summary>
    public async Task ProcessCommandLineAsync(string[] args)
    {
        var cmd = CommandLineParser.Parse(args);
        if (cmd.Verb == CommandVerb.None) return;

        // 한 번에 하나씩: 여러 압축을 골라 우클릭하면 파일마다 Iljip.exe 가 떠 단일 인스턴스 파이프로 거의 동시에 들어온다
        // (App.OnSecondInstanceLaunched 는 async void). 겹쳐 돌면 진행·암호 창이 섞이고 같은 '이름 (2)' 폴더를 둘이 고를 수 있어 차례로 처리한다.
        await _commandGate.WaitAsync();
        try
        {
            switch (cmd.Verb)
            {
                case CommandVerb.Open when File.Exists(cmd.Archive):
                    await OpenArchiveAsync(cmd.Archive!);
                    break;

                // 해제 명령은 열기가 실제로 된 경우에만 이어서 푼다 — 열지 못했으면(오류 창을 이미 보임) 해제 오류 창을 또 띄우지 않는다.
                // 파일 이름까지 암호가 걸린 7z 는 열기가 암호를 묻는다(I1-7) — 맞으면 그 암호로 풀고(다시 묻지 않음), 취소하면 풀지 않는다.
                case CommandVerb.Extract when File.Exists(cmd.Archive):
                    if (await OpenArchiveAsync(cmd.Archive!))
                        await ExtractAsync(); // 폴더 선택 다이얼로그 + 해제 완료까지 대기(fire-and-forget 제거)
                    break;

                case CommandVerb.ExtractHere when File.Exists(cmd.Archive):
                    {
                        var ap = Path.GetFullPath(cmd.Archive!);
                        var parent = Path.GetDirectoryName(ap) ?? Environment.CurrentDirectory;
                        if (await OpenArchiveAsync(ap))
                            await ExtractToFolderAsync(ap, parent);   // 같은 이름 파일은 '이름 (1).ext' 로 비켜 씀(기존 규칙)
                        break;
                    }

                case CommandVerb.ExtractToFolder when File.Exists(cmd.Archive):
                    {
                        var ap = Path.GetFullPath(cmd.Archive!);
                        var parent = Path.GetDirectoryName(ap) ?? Environment.CurrentDirectory;
                        if (await OpenArchiveAsync(ap))
                        {
                            // 열기(오류 창) 뒤에 정한다 — 그사이 생긴 폴더와도 겹치지 않게
                            var dest = ArchiveNaming.UniqueFolder(parent, ArchiveNaming.FolderNameFor(ap));
                            // 취소·손상으로 멈췄는데 새 폴더에 파일이 하나도 안 남았으면(빈 하위 폴더뿐) 그 폴더를 지운다 — 다음에 풀 때 번호가 밀리지 않게.
                            // 앞서 다 풀린 파일이 있으면 남긴다(기존 계약). 암호 오류·암호 창 취소는 서비스가 이미 모두 되돌렸다.
                            if (!await ExtractToFolderAsync(ap, dest))
                                ArchiveNaming.RemoveFolderIfNoFiles(dest);
                        }
                        break;
                    }

                case CommandVerb.ExtractTo when File.Exists(cmd.Archive):
                    if (await OpenArchiveAsync(cmd.Archive!))
                        await ExtractToFolderAsync(cmd.Archive!, cmd.Dest!);
                    break;

                case CommandVerb.Compress:
                    // 셸 '일집으로 압축'은 사용자가 고른 것 — 압축을 보는 중이면 그것을 닫고 새 목록을 만든다
                    if (IsArchiveMode) StartNewStaging(cmd.Paths);
                    else AddToStaging(cmd.Paths);
                    // 사용자 의도가 명확하므로 압축 다이얼로그를 바로 띄움(끝날 때까지 기다림)
                    if (IsStagingMode && CompressCommand.CanExecute(null))
                        await CompressCommand.ExecuteAsync(null);
                    break;

                case CommandVerb.Paths:
                    {
                        // 키워드가 아니면 모두 경로로 간주 (드롭과 동일 처리)
                        var pathLike = cmd.Paths.Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
                        if (pathLike.Count > 0)
                            await HandleDroppedPathsAsync(pathLike);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("명령 처리 중 오류", ex);
            _ui.ShowError($"명령 처리 중 오류:\n\n{ErrorMessages.ToUserMessage(ex)}");
        }
        finally
        {
            _commandGate.Release();
        }
    }

    /// <summary>명령줄 처리를 한 번에 하나씩(<see cref="ProcessCommandLineAsync"/>).</summary>
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    /// <summary>
    /// 해제 실패가 '암호 문제'(암호 창을 다시 띄울 일)인가. 서비스가 일집 예외로 바꿔 던지는 것은 타입으로만 판정한다(B5) —
    /// 암호 예외면 예, 그 밖의 일집 예외(손상·CRC 오류 등)는 문구에 '암호'가 들어 있어도 아니오.
    /// 엔진 예외가 그대로 올라온 경우(번역이 없는 경로)만 예전 문구 휴리스틱을 쓴다.
    /// </summary>
    private static bool IsPasswordRelated(Exception ex)
    {
        if (ex is ArchivePasswordException) return true;
        if (ex is ArchiveException) return false;

        var name = ex.GetType().Name;
        var msg = ex.Message ?? string.Empty;
        return ex is System.Security.Cryptography.CryptographicException
            || name.Contains("Crypto", StringComparison.OrdinalIgnoreCase)
            // EggDotNet은 암호화 egg/alz에 비번이 없거나 틀리면 DecryptFailedException("Decryption of entry failed")을
            // 던진다 — 타입/메시지에 Crypto·password가 없어 위 조건에 안 걸리므로 decrypt를 명시적으로 잡는다.
            || name.Contains("Decrypt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("decrypt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("암호", StringComparison.Ordinal);
    }

    /// <summary>
    /// 열기 창 필터. 자동 풀림 exe(W2-01)는 확장자로 고르지 않아(보통 실행 파일과 구분은 내용으로) '지원되는 압축 파일' 에 넣지 않고 따로 한 줄.
    /// </summary>
    private string BuildOpenFilter()
    {
        var exts = _locator.AllSupportedExtensions.Select(e => $"*{e}").ToList();
        string allPattern = string.Join(";", exts);
        return $"지원되는 압축 파일 ({allPattern})|{allPattern}|자동 풀림 압축 파일 (*.exe)|*.exe|모든 파일 (*.*)|*.*";
    }
}
