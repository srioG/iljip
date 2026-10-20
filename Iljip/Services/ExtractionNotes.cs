using System.IO;

namespace Iljip.Services;

/// <summary>링크 항목 하나(tar 하드·심볼릭 링크) — 압축 안 이름('/' 구분)과 링크가 가리키던 곳(원래 파일).</summary>
public sealed record LinkNote(string EntryPath, string LinkTarget);

/// <summary>풀고 있는 압축 파일과 이름이 같아 비켜 푼 항목 — 쓰려던 경로와 실제로 쓴 경로.</summary>
public sealed record RenamedNote(string DesiredPath, string WrittenPath);

/// <summary>
/// 풀기 한 번에서 사람에게 알릴 것(I5-B — 최종 감사 IA-SELFNAME·IA-OPENFILE-OVERWRITE·IX-F2). 실패는 아니지만 말없이 넘기면 안 되는 일:
/// <list type="bullet">
/// <item>링크 항목(tar 하드·심볼릭 링크)을 내용 없는(0바이트) 파일로 만든 것 — 링크를 되살리지는 않는다(결정 기록 IX-5·B-041 '제외', 알림만).</item>
/// <item>풀고 있는 압축 파일(과 그 조각)과 이름이 같은 항목을 '이름 (1)'로 비켜 푼 것 — 압축 파일 자신을 덮지 않게(<see cref="ExtractionScope"/>).</item>
/// <item>덮어쓰기로 정했는데 다른 프로그램(엑셀 등)이 열어 둔 파일이라 바꾸지 못하고 건너뛴 것 — 원래 파일 그대로, 나머지는 계속 푼다.</item>
/// </list>
/// 해제 서비스·<see cref="OutputFiles"/> 가 <see cref="Current"/> 에 적고, 화면(결과 창·여러 개 풀기 요약)·창 없이 풀기(표준 오류 한 줄)가 읽는다.
/// 화면 흐름은 <see cref="ExtractionScope"/> 가 하나씩 갖고(<see cref="ExtractionScope.Notes"/>), 창 없이 풀기는 같은 이름 범위 없이 이것만 연다(<see cref="Begin"/>).
/// 아무도 열지 않았으면(서비스를 바로 부르는 시험 등) 적지 않는다.
/// </summary>
public sealed class ExtractionNotes : IDisposable
{
    private static readonly AsyncLocal<ExtractionNotes?> CurrentValue = new();

    private readonly object _gate = new();
    private readonly List<LinkNote> _links = new();
    private readonly List<RenamedNote> _selfRenamed = new();
    private readonly List<string> _inUse = new();
    private readonly ExtractionNotes? _previous;
    private readonly bool _pushed;
    private bool _disposed;

    /// <summary>범위(<see cref="ExtractionScope"/>)가 갖는 모음 — 이 흐름의 '지금 모음'으로 올리지 않는다(범위를 거쳐 찾음).</summary>
    internal ExtractionNotes()
    {
    }

    private ExtractionNotes(ExtractionNotes? previous)
    {
        _previous = previous;
        _pushed = true;
    }

    /// <summary>
    /// 이 비동기 흐름의 모음 — 따로 연 것(창 없이 풀기), 없으면 같은 이름 범위의 것(화면), 둘 다 없으면 null(적지 않음).
    /// </summary>
    internal static ExtractionNotes? Current => CurrentValue.Value ?? ExtractionScope.Current?.Notes;

    /// <summary>모음을 연다(이 흐름과 여기서 시작하는 작업에 걸림 — 창 없이 풀기). using 으로 닫는다.</summary>
    public static ExtractionNotes Begin()
    {
        var notes = new ExtractionNotes(CurrentValue.Value);
        CurrentValue.Value = notes;
        return notes;
    }

    /// <summary>내용 없이 만든 링크 항목.</summary>
    public IReadOnlyList<LinkNote> Links
    {
        get { lock (_gate) return _links.ToList(); }
    }

    /// <summary>풀고 있는 압축 파일과 이름이 같아 비켜 푼 항목.</summary>
    public IReadOnlyList<RenamedNote> SelfRenamed
    {
        get { lock (_gate) return _selfRenamed.ToList(); }
    }

    /// <summary>다른 프로그램이 쓰는 중이라 바꾸지 못하고 건너뛴 파일(전체 경로 — 원래 파일 그대로).</summary>
    public IReadOnlyList<string> InUse
    {
        get { lock (_gate) return _inUse.ToList(); }
    }

    /// <summary>알릴 것이 하나라도 있는가.</summary>
    public bool HasAny
    {
        get { lock (_gate) return _links.Count + _selfRenamed.Count + _inUse.Count > 0; }
    }

    internal void AddLink(string entryPath, string linkTarget)
    {
        lock (_gate) _links.Add(new LinkNote(entryPath, linkTarget));
    }

    internal void AddSelfRenamed(string desiredPath, string writtenPath)
    {
        lock (_gate) _selfRenamed.Add(new RenamedNote(desiredPath, writtenPath));
    }

    internal void AddInUse(string path)
    {
        lock (_gate)
            if (!_inUse.Contains(path, StringComparer.OrdinalIgnoreCase)) _inUse.Add(path);
    }

    /// <summary>목록에 보일 줄 수(넘으면 '외 N개').</summary>
    private const int MaxLines = 10;

    /// <summary>
    /// 결과 창·여러 개 풀기 요약에 보일 글(쉬운 한국어 — 알릴 것이 없으면 null). 예:
    /// '다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개(원래 파일 그대로예요 — 그 프로그램을 닫고 다시 풀어 주세요):\n- 견적서 1.xlsx'.
    /// </summary>
    public string? ToMessage()
    {
        List<LinkNote> links;
        List<RenamedNote> renamed;
        List<string> inUse;
        lock (_gate)
        {
            links = _links.ToList();
            renamed = _selfRenamed.ToList();
            inUse = _inUse.ToList();
        }
        if (links.Count + renamed.Count + inUse.Count == 0) return null;

        var blocks = new List<string>();
        if (renamed.Count > 0)
            blocks.Add(Block(
                $"풀고 있는 압축 파일과 이름이 같은 파일 {renamed.Count}개는 그 압축 파일을 덮지 않게 이름을 바꿔 풀었어요(원래 압축 파일은 그대로예요):",
                renamed.Select(r => $"- {Path.GetFileName(r.DesiredPath)} → {Path.GetFileName(r.WrittenPath)}").ToList()));
        if (inUse.Count > 0)
            blocks.Add(Block(
                $"다른 프로그램이 쓰는 중이라 못 바꾼 파일 {inUse.Count}개(원래 파일 그대로예요 — 그 프로그램을 닫고 다시 풀어 주세요):",
                inUse.Select(p => $"- {Path.GetFileName(p)}").ToList()));
        if (links.Count > 0)
            blocks.Add(Block(
                $"링크 {links.Count}개는 내용 없이(0바이트) 만들었어요. 일집은 링크를 되살리지 않아요:",
                links.Select(l => $"- {l.EntryPath} (원래 파일: {l.LinkTarget})").ToList()));
        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// 창 없이 풀기(재사용장치)의 표준 오류 한 줄 — 링크를 내용 없이 만들었을 때만(그때 종료 코드 1 — 재사용장치는 반디집으로 다시 풂). 없으면 null.
    /// '압축 아님' 표지('압축 파일이 아닌' 등)는 넣지 않는다.
    /// </summary>
    public string? HeadlessLine()
    {
        var links = Links;
        if (links.Count == 0) return null;
        string shown = string.Join(", ", links.Take(3).Select(l => $"{l.EntryPath} → {l.LinkTarget}"));
        string more = links.Count > 3 ? $" 외 {links.Count - 3}개" : string.Empty;
        return $"링크 {links.Count}개는 내용 없이(0바이트) 만들었어요({shown}{more}). 일집은 링크를 되살리지 않아요.";
    }

    private static string Block(string head, IReadOnlyList<string> lines)
    {
        var all = new List<string> { head };
        all.AddRange(lines.Take(MaxLines));
        if (lines.Count > MaxLines) all.Add($"외 {lines.Count - MaxLines}개");
        return string.Join("\n", all);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pushed && ReferenceEquals(CurrentValue.Value, this))
            CurrentValue.Value = _previous;
    }
}
