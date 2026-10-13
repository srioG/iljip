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
    /// <param name="services">셸·휴지통·설정(W2-05). null 이면 진짜 구현. 테스트는 가짜 묶음(TestSupport/TestServices)을 넣는다.</param>
    public MainViewModel(IUserInteraction? ui, ArchiveServiceLocator? locator = null, AppServices? services = null)
    {
        _ui = ui ?? new WpfUserInteraction();
        _locator = locator ?? new ArchiveServiceLocator();
        _services = services ?? AppServices.CreateDefault();
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
        OnPropertyChanged(nameof(CanEditArchive));   // W2-06
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
        // W2-05(I2-6): 여러 개를 고르면 '모두 풀기'(MainViewModel.CommandLine.cs)
        var picked = _ui.PickFiles("압축 파일 열기", BuildOpenFilter(), multiselect: true);
        if (picked is not { Count: > 0 }) return;
        if (picked.Count > 1)
        {
            await ExtractPickedArchivesAsync(picked);
            return;
        }
        await OpenArchiveAsync(picked[0]);
    }

    /// <summary>
    /// 압축 파일을 열어 목록을 채운다. 열었으면 true(못 열면 오류 창을 보이고 false — 보던 것은 그대로).
    /// 파일 이름까지 암호가 걸린 7z(헤더 암호 — 반디집 '파일 이름 암호화')는 목록부터 암호가 필요해 여기서 암호를 묻고 다시 연다(I1-7 의 화면 쪽).
    /// 틀리면 다시 묻고, 취소하면 아무것도 바꾸지 않는다(false). 맞은 암호는 같은 압축을 풀 때 다시 묻지 않게 기억한다.
    /// (W1-D 1차 검증: 우클릭 풀기를 '열기에 성공해야 푼다'로 바꾸며 헤더 암호 7z 를 어느 경로로도 못 풀게 된 회귀를 이것으로 고침)
    /// W2-02(I1-7): 목록까지 잠긴 7z·RAR(<see cref="ListPasswordRequiredException"/>)은 '파일 목록까지 암호로 잠긴…'(<see cref="PasswordPromptReason.ListLocked"/>)으로 묻고,
    /// 틀리면 '암호가 맞지 않아요' 경고 뒤 다시 묻는다(<see cref="AskListPassword"/>).
    /// </summary>
    public Task<bool> OpenArchiveAsync(string archivePath) => OpenArchiveAsync(archivePath, null);

    /// <param name="archivePath">열 압축 파일.</param>
    /// <param name="knownPassword">
    /// 이미 아는 암호(W2-02 — 방금 만든 7z 를 열 때 그 암호). 이것으로 먼저 열어 보고, 맞으면 기억해 풀 때도 묻지 않는다. 없으면 null.
    /// </param>
    public async Task<bool> OpenArchiveAsync(string archivePath, string? knownPassword)
    {
        // W2-01: 확장자로 먼저, 모르는 확장자면 내용(서명)으로 — 이름이 바뀐 압축(.bin 이 된 7z 등)·자동 풀림 exe 도 연다(B-086)
        var service = _locator.ResolveByContent(archivePath);
        if (service is null)
        {
            _ui.ShowWarning($"지원하지 않는 형식이에요:\n{Path.GetExtension(archivePath)}");
            return false;
        }

        // W2-06(I3-3): 다른 압축으로 바꾸기 전에 — 이 압축에서 연 파일을 고쳐 놓고 아직 넣지 않았으면 묻는다('나중에'의 다시 묻기)
        if (IsArchiveMode && !string.Equals(Path.GetFullPath(archivePath), CurrentArchivePath is { } cur ? Path.GetFullPath(cur) : null, StringComparison.OrdinalIgnoreCase))
            await AskPendingBeforeLeavingAsync();

        string? password = string.IsNullOrEmpty(knownPassword) ? null : knownPassword;
        StatusText = "압축 파일을 읽는 중...";
        while (true)
        {
            try
            {
                var entries = await service.ListEntriesAsync(archivePath, password);

                Entries.Clear();
                foreach (var e in entries)
                    Entries.Add(e);

                if (!string.Equals(CurrentArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
                    RetireWorkspace();   // W2-06: 앞 압축의 작업 폴더는 앱을 닫을 때까지 둔다(감시도 그대로)
                CurrentArchivePath = archivePath;
                _archivePassword = string.IsNullOrEmpty(password) ? null : password;
                BuildFolderTree();
                StatusText = $"{entries.Count}개 항목";
                return true;
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                // 암호는 [취소]할 때까지 다시 묻는다(풀 때와 같음). 취소하면 보던 압축·목록·기억한 암호는 그대로.
                var pw = AskListPassword(archivePath, ex, password);
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

    /// <summary>
    /// Staging 목록을 실제로 압축한다 — 한 화면 압축 창(W2-07, I2-12: 이름·위치·형식·수준·암호·나누기·각각 따로·날짜·빼기·시험·원본 지우기)을 띄우고
    /// 그 답대로 만든다(<see cref="CompressStagedAsync"/>, MainViewModel.Compress.cs). 예전 3단계(저장 창 → 옵션 창)는 이 창 하나로 바뀌었다.
    /// </summary>
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

        await CompressStagedAsync(sourcePaths);
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
    /// <remarks>
    /// W2-05: 같은 이름 파일은 설정(extract.onCollision — 기본 '묻기')대로(<see cref="ExtractionScope"/> — ‘남은 파일도 똑같이’ 답은 암호를 다시 물어 다시 풀 때도 유지).
    /// 전체를 다 풀었고 건너뛴 파일이 없으면 설정(extract.deleteArchiveAfter)에 따라 압축 파일을 휴지통으로(<see cref="RecycleArchiveAfterExtract"/>).
    /// </remarks>
    public async Task<bool> ExtractToFolderAsync(string archivePath, string destFolder, IReadOnlyCollection<string>? selectedPaths = null)
    {
        var service = _locator.ResolveByContent(archivePath);
        if (service is null) return false;

        bool sameArchive = string.Equals(archivePath, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        var resolver = CreateCollisionResolver();
        while (true)
        {
            var session = _ui.ShowProgress("압축 해제 중...");
            using var scope = ExtractionScope.Begin(resolver);
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

                session.Close();
                StatusText = $"압축 해제 완료: {destFolder}";
                if (sameArchive && !string.IsNullOrEmpty(password)) _archivePassword = password;
                // I2-5: 전체를 오류 없이 다 풀었고 건너뛴 파일이 없을 때만(선택 항목만 푼 것은 지우지 않음)
                if (selectedPaths is null && RecycleArchiveAfterExtract(archivePath, scope.Skipped) is { } note)
                    _ui.ShowWarning(note);
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
                // 이유 한 줄: 일집 암호 예외는 그 문구('암호가 걸린 압축 파일이에요…' / '파일 목록까지…' / '암호가 맞지 않아요…'), 엔진 예외는 예전 문구
                var (reason, message) = PasswordPrompt(ex, password);
                var pw = _ui.AskPassword(Path.GetFileName(archivePath), reason, message);
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

    /// <summary>
    /// 현재 상태(압축 보기/스테이징)를 비우고 빈 상태로. W2-06(I3-3): 압축을 닫기 전에 — 이 압축에서 연 파일을 고쳐 놓고 아직 넣지 않았으면 묻는다.
    /// </summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (IsArchiveMode) await AskPendingBeforeLeavingAsync();
        CloseView();
        StatusText = "준비됨";
    }

    /// <summary>보던 압축·모으던 목록을 모두 비운다(상태 줄은 그대로).</summary>
    private void CloseView()
    {
        RetireWorkspace();   // W2-06: 작업 폴더는 앱을 닫을 때까지 둔다
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
        // W2-06(I2-3): 고칠 수 있는 압축(ZIP·7z)을 보는 중이면 [이 압축 파일에 넣기]도
        var decision = DropPolicy.Decide(state, paths, IsArchiveFileByExtension, archiveEditable: CanEditArchive);
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
            case DropChoice.AddToArchive:
                await AddPathsToArchiveAsync(paths);   // 지금 보는 폴더에
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
