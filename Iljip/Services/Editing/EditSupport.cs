using System.IO;
using Iljip.Models;

namespace Iljip.Services.Editing;

/// <summary>두 편집기(ZIP·7z)가 같이 쓰는 것: 진행 창 문구, '넣기' 계획, 진행률 묶기.</summary>
internal static class EditSupport
{
    /// <summary>원래 항목을 새 압축으로 옮기는 동안(7z 는 7z.dll 이 다시 쓰는 동안)의 진행 창 문구.</summary>
    internal const string StageRewriting = "다시 정리하는 중";

    /// <summary>다 쓴 결과를 검사하는 동안의 진행 창 문구.</summary>
    internal const string StageTesting = "검사하는 중";

    /// <summary>7z: 원본을 작업 사본으로 복사하는 동안의 진행 창 문구.</summary>
    internal const string StagePreparing = "준비하는 중";

    /// <summary>넣을 수 있는 파일이 하나도 없을 때(모두 다른 프로그램이 잠금·없어짐).</summary>
    internal const string NothingToAddMessage = "넣을 수 있는 파일이 하나도 없어서 압축 파일을 그대로 두었어요.";

    /// <summary>넣을 파일 하나(원본 경로 → 임시 이름).</summary>
    internal sealed record PlannedSource(string SourcePath, string TempKey);

    /// <summary><see cref="EditOp.Add.ReplaceExisting"/> 의 같은 이름 처리(늘 덮어쓰기).</summary>
    private static readonly ICollisionResolver ReplaceResolver = new FixedResolver(CollisionChoice.Overwrite);

    /// <summary>
    /// '넣기' 하나를 계획에 더한다: 원본을 모으고(<see cref="SourceCollector"/> — 폴더는 그 이름째, 못 들어간 폴더·없어진 원본은 건너뜀으로),
    /// 파일마다 자리를 정한다(같은 이름은 처리기에 묻고, 범위가 없으면 '이름 (1)'). 빈 폴더도 넣는다.
    /// 넣기로 한 파일은 <paramref name="files"/> 에 (원본, 임시 이름)으로 더한다.
    /// </summary>
    public static void PlanAdd<T>(
        EditPlanner<T> planner, EditOp.Add add, ICollisionResolver? resolver, Func<string> nextTempKey,
        List<PlannedSource> files, List<SkippedSource> skipped, List<(string Key, string SourceDir)>? folders = null)
    {
        var set = SourceCollector.Collect(add.Sources);
        skipped.AddRange(set.Skipped);
        if (add.ReplaceExisting) resolver = ReplaceResolver;   // 다시 넣기: 같은 이름 파일은 묻지 않고 바꿔 넣음(없으면 새로)
        string folder = EditPlanner<T>.Normalize(add.TargetFolder);
        bool single = set.Files.Count == 1 && set.EmptyDirectories.Count == 0 && !string.IsNullOrEmpty(add.AsName);
        // 다시 넣기(바꿔 넣기)의 이름은 압축에 있던 원래 이름 — Windows 에 못 쓰는 이름(':' 든 이름·끝 점)도 그대로 둔다(W2-06 검증 지적 2차)
        string? problem = !single ? null
            : add.ReplaceExisting ? EditPlanner<T>.ValidateExistingName(add.AsName) : EditPlanner<T>.ValidateName(add.AsName);
        if (problem is not null) throw new ArchiveEditException(problem);
        // 넣는 폴더와 같은 이름의 '파일'이 이미 있으면 그 폴더째 '이름 (1)' 로 비켜 넣는다(이 넣기 안에서는 늘 같은 이름으로)
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int remaining = set.Files.Count;
        foreach (var (source, entryName) in set.Files)
        {
            remaining--;
            string rel = single ? add.AsName! : entryName;
            string desired = planner.AvoidFiles(folder, rel, lastIsFolder: false, moved);
            string temp = nextTempKey();
            if (planner.AddFile(source, temp, desired, SafeLength(source), SafeTime(source), remaining, resolver))
                files.Add(new PlannedSource(source, temp));
        }
        foreach (var (dir, entryName) in set.EmptyDirectories)
        {
            string key = planner.AvoidFiles(folder, entryName, lastIsFolder: true, moved);
            planner.AddEmptyDirectory(key);
            folders?.Add((EditPlanner<T>.Normalize(key), dir));
        }
    }

    public static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    public static DateTime? SafeTime(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch { return null; }
    }

    /// <summary>
    /// 고치기 진행률 묶기: 앞 단계(새 파일 압축 — 원본 바이트)와 뒤 단계(옮겨 쓰기)를 한 막대로. 줄어들지 않게, 총량을 넘지 않게.
    /// 앞 단계가 알려 오는 임시 이름은 화면 이름으로 바꾼다.
    /// </summary>
    internal sealed class Tracker
    {
        private readonly IProgress<ArchiveProgress>? _progress;
        private readonly object _gate = new();
        private long _total;
        private int _totalFiles;
        private long _done;
        private long _base;
        private int _files;
        private long _shown;
        private string _current = string.Empty;

        public Tracker(IProgress<ArchiveProgress>? progress) => _progress = progress;

        /// <summary>총량을 정하고 처음부터 센다(7z 는 읽다 막힌 원본을 빼고 다시 할 때 또 부른다 — 막대는 줄지 않고 그 자리에서 기다림).</summary>
        public void SetTotals(long totalBytes, int totalFiles)
        {
            lock (_gate)
            {
                _total = Math.Max(0, totalBytes);
                _totalFiles = Math.Max(0, totalFiles);
                _base = 0;
                _done = 0;
                _files = 0;
                Report();
            }
        }

        /// <summary>앞 단계(새 파일 압축)의 진행을 받는 어댑터 — 임시 이름 → 화면 이름.</summary>
        public IProgress<ArchiveProgress> CompressPhase(IReadOnlyDictionary<string, string> displayNames) =>
            new Adapter(this, displayNames);

        /// <summary>앞 단계를 마쳤다 — 이후 바이트는 <paramref name="phaseBytes"/> 위에 더한다.</summary>
        public void EndPhase(long phaseBytes)
        {
            lock (_gate)
            {
                _base += Math.Max(0, phaseBytes);
                _done = _base;
                Report();
            }
        }

        public void Stage(string text)
        {
            lock (_gate)
            {
                _current = text;
                Report();
            }
        }

        public void OnBytes(long n)
        {
            lock (_gate)
            {
                _done += n;
                Report();
            }
        }

        /// <summary>지금 단계(크기 <paramref name="phaseWeight"/>)를 <paramref name="fraction"/>(0~1)만큼 했다 — 다음 단계는 <see cref="EndPhase"/> 뒤에.</summary>
        public void Within(long phaseWeight, double fraction)
        {
            lock (_gate)
            {
                _done = _base + (long)(Math.Max(0, phaseWeight) * Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1));
                Report();
            }
        }

        public void Final()
        {
            lock (_gate)
            {
                _progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    BytesProcessed = _total,
                    TotalBytes = _total,
                    FilesProcessed = _totalFiles,
                    TotalFiles = _totalFiles,
                });
            }
        }

        private void Report()
        {
            if (_progress is null) return;
            _shown = Math.Min(_total, Math.Max(_shown, _done));
            _progress.Report(new ArchiveProgress
            {
                CurrentFile = _current,
                BytesProcessed = _shown,
                TotalBytes = _total,
                FilesProcessed = Math.Min(_files, _totalFiles),
                TotalFiles = _totalFiles,
            });
        }

        private sealed class Adapter : IProgress<ArchiveProgress>
        {
            private readonly Tracker _owner;
            private readonly IReadOnlyDictionary<string, string> _names;

            public Adapter(Tracker owner, IReadOnlyDictionary<string, string> names)
            {
                _owner = owner;
                _names = names;
            }

            public void Report(ArchiveProgress value)
            {
                lock (_owner._gate)
                {
                    _owner._done = _owner._base + value.BytesProcessed;
                    _owner._files = value.FilesProcessed;
                    if (!string.IsNullOrEmpty(value.CurrentFile))
                        _owner._current = _names.TryGetValue(value.CurrentFile, out var shown) ? shown : value.CurrentFile;
                    _owner.Report();
                }
            }
        }
    }
}
