using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>알아서 풀기의 풀 곳 종류.</summary>
public enum SmartPlanKind
{
    /// <summary>압축 파일이 있는 폴더에 바로(맨 위가 하나라 폴더가 두 겹이 되지 않음).</summary>
    Here,
    /// <summary>압축 파일 옆 '이름' 폴더(있으면 '이름 (2)').</summary>
    NamedFolder,
}

/// <summary>알아서 풀기 계획. <see cref="TopName"/>·<see cref="TopIsFolder"/> 는 <see cref="SmartPlanKind.Here"/> 일 때 맨 위 하나.</summary>
public sealed record SmartExtractPlan(SmartPlanKind Kind, string? TopName = null, bool TopIsFolder = false);

/// <summary>
/// 알아서 풀기(W2-12 — 탐색기 우클릭 '알아서 풀기', '--extract-smart'): 압축 안의 맨 위가 하나(폴더 하나 또는 파일 하나)면 압축 파일이 있는 폴더에 바로 푼다
/// ('보고서.zip' 안에 '보고서\…' 폴더 하나뿐일 때 '보고서\보고서\…' 로 두 겹이 되지 않게). 여럿이면(또는 비었으면) '이름' 폴더(<see cref="ArchiveNaming.UniqueFolder"/>).
/// 바로 풀 때 같은 이름(맨 위 폴더가 이미 있음 등)은 W2-05 같은 이름 처리기가 맡는다(폴더는 합치고 겹치는 파일만 묻는다).
/// 순수 함수(목록만 본다) — 시험은 SmartExtractTests.
/// </summary>
public static class SmartExtractPlanner
{
    public static SmartExtractPlan Plan(IReadOnlyList<ArchiveEntry> entries, string archivePath)
    {
        // 맨 위 이름 → 폴더인가(아래에 항목이 있거나 폴더 항목). Windows 처럼 대소문자는 가리지 않는다.
        var tops = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var parts = (e.Path ?? string.Empty).Split('/', '\\').Where(p => p.Length > 0 && p != ".").ToList();
            if (parts.Count == 0) continue;
            bool folder = parts.Count > 1 || e.IsDirectory;
            tops[parts[0]] = tops.TryGetValue(parts[0], out bool was) ? was || folder : folder;
        }
        if (tops.Count != 1) return new SmartExtractPlan(SmartPlanKind.NamedFolder);

        var (name, isFolder) = tops.Single();
        // 바로 풀면 압축 파일 자신과 같은 이름이 되는 것('a.zip' 안의 'a.zip' 하나 등)·'..' 는 '이름' 폴더로 — 풀고 있는 파일을 덮어쓰지 않게
        if (name == ".." || string.Equals(name, Path.GetFileName(archivePath), StringComparison.OrdinalIgnoreCase))
            return new SmartExtractPlan(SmartPlanKind.NamedFolder);
        return new SmartExtractPlan(SmartPlanKind.Here, name, isFolder);
    }
}
