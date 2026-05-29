using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;
using Iljip.Views;
using Microsoft.Win32;

namespace Iljip.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ArchiveServiceLocator _locator;

    public MainViewModel()
    {
        _locator = new ArchiveServiceLocator();
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

    /// <summary>우측 목록에 실제로 표시되는 항목. 아카이브 모드: 선택 폴더의 직속 / 그 외: Entries 전체.</summary>
    public ObservableCollection<ArchiveEntry> VisibleEntries { get; } = new();

    /// <summary>트리에서 선택된 폴더 노드.</summary>
    [ObservableProperty]
    private ArchiveTreeNode? _selectedFolder;

    partial void OnSelectedFolderChanged(ArchiveTreeNode? value) => RefreshVisibleEntries();

    [ObservableProperty]
    private string _statusText = "준비됨";

    // ===== Commands =====

    [RelayCommand]
    private async Task OpenAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "압축 파일 열기",
            Filter = BuildOpenFilter(),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        await OpenArchiveAsync(dlg.FileName);
    }

    public async Task OpenArchiveAsync(string archivePath)
    {
        var service = _locator.Resolve(archivePath);
        if (service is null)
        {
            MessageBox.Show(
                $"지원하지 않는 형식이에요:\n{Path.GetExtension(archivePath)}",
                "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText = "압축 파일을 읽는 중...";
            var entries = await service.ListEntriesAsync(archivePath);

            Entries.Clear();
            foreach (var e in entries)
                Entries.Add(e);

            CurrentArchivePath = archivePath;
            BuildFolderTree();
            StatusText = $"{entries.Count}개 항목";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"파일을 열 수 없어요:\n\n{ex.Message}",
                "일집", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "오류 발생";
        }
    }

    /// <summary>파일/폴더 선택 다이얼로그를 띄워 Staging에 추가.</summary>
    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Title = "압축할 파일 선택 (여러 개 가능)",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        AddToStaging(dlg.FileNames);
    }

    /// <summary>폴더를 Staging에 추가.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "압축할 폴더 선택"
        };
        if (dlg.ShowDialog() != true) return;
        AddToStaging(new[] { dlg.FolderName });
    }

    /// <summary>임시 압축 목록에 파일/폴더 경로들을 추가.</summary>
    public void AddToStaging(IEnumerable<string> paths)
    {
        // Archive 모드 → 새 Staging으로 전환 (열린 압축 닫음)
        if (IsArchiveMode)
        {
            Entries.Clear();
            CurrentArchivePath = null;
            FolderTree.Clear();
            SelectedFolder = null;
        }

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

    /// <summary>Staging 목록을 실제로 압축한다.</summary>
    [RelayCommand]
    private async Task CompressAsync()
    {
        if (!IsStagingMode)
        {
            MessageBox.Show("압축할 파일을 먼저 드래그하거나 [추가] 버튼으로 넣어주세요.", "일집");
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
            defaultName = Path.GetFileNameWithoutExtension(sourcePaths[0]) + ".zip";
            if (string.IsNullOrEmpty(defaultName) || defaultName == ".zip")
                defaultName = new DirectoryInfo(sourcePaths[0]).Name + ".zip";
        }
        else
        {
            string parentName = new DirectoryInfo(defaultDir).Name;
            defaultName = string.IsNullOrEmpty(parentName) ? "archive.zip" : parentName + ".zip";
        }

        var dlgSave = new SaveFileDialog
        {
            Title = "압축 파일로 저장",
            Filter = _locator.BuildSaveFilter(),
            FileName = defaultName,
            InitialDirectory = defaultDir,
            DefaultExt = ".zip"
        };
        if (dlgSave.ShowDialog() != true) return;

        var service = _locator.Resolve(dlgSave.FileName) ?? _locator.GetDefaultForCompression();

        var optDlg = new CompressionOptionsDialog(Path.GetExtension(dlgSave.FileName))
        {
            Owner = Application.Current.MainWindow
        };
        if (optDlg.ShowDialog() != true) return;
        var compressionOptions = optDlg.Result ?? CompressionOptions.Default;

        var progressDlg = new ProgressDialog
        {
            Owner = Application.Current.MainWindow,
            Title = "압축 중..."
        };
        var progress = new ThrottledArchiveProgress(p => progressDlg.UpdateProgress(p));
        progressDlg.Show();

        try
        {
            await service.CompressAsync(
                sourcePaths,
                dlgSave.FileName,
                options: compressionOptions,
                progress: progress,
                cancellationToken: progressDlg.CancellationToken);

            StatusText = $"압축 완료: {dlgSave.FileName}";

            // 결과 압축 파일을 자동으로 열어줌 (Staging 비워지고 Archive 모드로 전환)
            await OpenArchiveAsync(dlgSave.FileName);
        }
        catch (OperationCanceledException)
        {
            StatusText = "압축 취소됨";
            try { File.Delete(dlgSave.FileName); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"압축 실패:\n\n{ex.Message}",
                "일집", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "오류 발생";
        }
        finally
        {
            progressDlg.Close();
        }
    }

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (!IsArchiveMode || string.IsNullOrEmpty(CurrentArchivePath))
        {
            MessageBox.Show("먼저 압축 파일을 열어주세요.", "일집");
            return;
        }

        var dlg = new OpenFolderDialog
        {
            Title = "압축을 풀 폴더를 선택하세요"
        };
        if (dlg.ShowDialog() != true) return;

        await ExtractToFolderAsync(CurrentArchivePath, dlg.FolderName);
    }

    /// <summary>
    /// 지정한 폴더로 압축 해제. UI 진행률 + 비밀번호 재시도 포함.
    /// CLI / 셸 확장 / 기존 ExtractCommand 모두에서 호출.
    /// </summary>
    /// <param name="selectedPaths">지정하면 해당 항목들만 해제. null이면 전체.</param>
    public async Task ExtractToFolderAsync(string archivePath, string destFolder, IReadOnlyCollection<string>? selectedPaths = null)
    {
        var service = _locator.Resolve(archivePath);
        if (service is null) return;

        string? password = null;
        bool extracted = false;
        for (int attempt = 0; attempt < 3 && !extracted; attempt++)
        {
            var progressDlg = new ProgressDialog
            {
                Owner = Application.Current.MainWindow,
                Title = "압축 해제 중..."
            };
            var progress = new ThrottledArchiveProgress(p => progressDlg.UpdateProgress(p));
            progressDlg.Show();

            try
            {
                if (selectedPaths is null)
                    await service.ExtractAsync(
                        archivePath,
                        destFolder,
                        password: password,
                        progress: progress,
                        cancellationToken: progressDlg.CancellationToken);
                else
                    await service.ExtractEntriesAsync(
                        archivePath,
                        destFolder,
                        selectedPaths,
                        password: password,
                        progress: progress,
                        cancellationToken: progressDlg.CancellationToken);

                StatusText = $"압축 해제 완료: {destFolder}";
                extracted = true;
            }
            catch (OperationCanceledException)
            {
                StatusText = "압축 해제 취소됨";
                return;
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                progressDlg.Close();
                var pw = new PasswordPromptDialog(
                    attempt == 0
                        ? "이 압축 파일은 비밀번호가 필요해요."
                        : "비밀번호가 틀린 것 같아요. 다시 입력해주세요.")
                {
                    Owner = Application.Current.MainWindow
                };
                if (pw.ShowDialog() != true)
                {
                    StatusText = "압축 해제 취소됨";
                    return;
                }
                password = pw.Password;
                continue;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"압축 해제 실패:\n\n{ex.Message}",
                    "일집", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "오류 발생";
                return;
            }
            finally
            {
                progressDlg.Close();
            }
        }

        if (!extracted)
        {
            MessageBox.Show("비밀번호가 3회 모두 일치하지 않아 해제에 실패했어요.",
                "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            StatusText = "비밀번호 불일치";
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

        var dlg = new OpenFolderDialog { Title = "선택한 항목을 풀 폴더를 선택하세요" };
        if (dlg.ShowDialog() != true) return;

        await ExtractToFolderAsync(CurrentArchivePath, dlg.FolderName, paths);
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
            IsRoot = true,
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

    /// <summary>선택 폴더 기준으로 우측 표시 목록을 갱신. 아카이브 모드가 아니면 Entries 전체.</summary>
    private void RefreshVisibleEntries()
    {
        VisibleEntries.Clear();

        if (IsArchiveMode && SelectedFolder is not null)
        {
            foreach (var child in SelectedFolder.Children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                VisibleEntries.Add(new ArchiveEntry
                {
                    Path = child.FullPath,
                    IsDirectory = true,
                    Size = -1,        // 폴더는 크기 칸 비움 (BytesToHuman이 음수를 빈 문자열로 처리)
                    CompressedSize = -1
                });
            }
            foreach (var file in SelectedFolder.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                VisibleEntries.Add(file);
        }
        else
        {
            foreach (var e in Entries)
                VisibleEntries.Add(e);
        }
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
        Entries.Clear();
        CurrentArchivePath = null;
        FolderTree.Clear();
        SelectedFolder = null;
        VisibleEntries.Clear();
        StatusText = "준비됨";
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
        Application.Current.MainWindow?.Close();
    }

    /// <summary>드래그 앤 드롭 처리.</summary>
    /// - 단일 압축 파일 → 열기
    /// - 그 외(일반 파일/폴더, 또는 여러 파일) → Staging에 추가
    public async Task HandleDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;

        if (paths.Count == 1 && File.Exists(paths[0]) && _locator.Resolve(paths[0]) is not null)
        {
            await OpenArchiveAsync(paths[0]);
            return;
        }

        AddToStaging(paths);
    }

    /// <summary>
    /// 명령줄 인자를 받아 적절한 동작 수행. 셸 확장이 호출하는 진입점.
    /// 인자 형식:
    ///   --open &lt;archive&gt;             압축 파일을 열기만 함
    ///   --extract &lt;archive&gt;          폴더 선택 다이얼로그로 압축 해제
    ///   --extract-here &lt;archive&gt;     압축 파일과 같은 위치의 동명 폴더에 해제
    ///   --extract-to &lt;archive&gt; &lt;dst&gt; 지정 폴더로 해제
    ///   --compress &lt;p1&gt; &lt;p2&gt; ...     인자들을 Staging에 추가 (압축 다이얼로그 자동 진행)
    ///   &lt;path&gt;...                     경로만 주어지면 드롭과 동일한 동작
    /// </summary>
    public async Task ProcessCommandLineAsync(string[] args)
    {
        if (args.Length == 0) return;

        try
        {
            string verb = args[0];

            switch (verb)
            {
                case "--open" when args.Length >= 2 && File.Exists(args[1]):
                    await OpenArchiveAsync(args[1]);
                    break;

                case "--extract" when args.Length >= 2 && File.Exists(args[1]):
                    await OpenArchiveAsync(args[1]);
                    ExtractCommand.Execute(null); // 폴더 선택 다이얼로그
                    break;

                case "--extract-here" when args.Length >= 2 && File.Exists(args[1]):
                    {
                        var ap = args[1];
                        var parent = Path.GetDirectoryName(ap) ?? Environment.CurrentDirectory;
                        var sub = Path.GetFileNameWithoutExtension(ap);
                        var dest = Path.Combine(parent, sub);
                        await OpenArchiveAsync(ap);
                        await ExtractToFolderAsync(ap, dest);
                        break;
                    }

                case "--extract-to" when args.Length >= 3 && File.Exists(args[1]):
                    await OpenArchiveAsync(args[1]);
                    await ExtractToFolderAsync(args[1], args[2]);
                    break;

                case "--compress":
                    if (args.Length >= 2)
                    {
                        AddToStaging(args.Skip(1));
                        // 사용자 의도가 명확하므로 압축 다이얼로그를 바로 띄움
                        if (CompressCommand.CanExecute(null))
                            CompressCommand.Execute(null);
                    }
                    break;

                default:
                    // 키워드가 아니면 모두 경로로 간주 (드롭과 동일 처리)
                    var pathLike = args.Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
                    if (pathLike.Count > 0)
                        await HandleDroppedPathsAsync(pathLike);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"명령 처리 중 오류:\n\n{ex.Message}",
                "일집", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static bool IsPasswordRelated(Exception ex)
    {
        var name = ex.GetType().Name;
        var msg = ex.Message ?? string.Empty;
        return ex is System.Security.Cryptography.CryptographicException
            || name.Contains("Crypto", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("암호", StringComparison.Ordinal);
    }

    private string BuildOpenFilter()
    {
        var exts = _locator.AllSupportedExtensions.Select(e => $"*{e}").ToList();
        string allPattern = string.Join(";", exts);
        return $"지원되는 압축 파일 ({allPattern})|{allPattern}|모든 파일 (*.*)|*.*";
    }
}
