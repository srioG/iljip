using System.IO;
using System.Text.RegularExpressions;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>'다른 이름으로 저장'에서 할 일(W2-11, I3-15).</summary>
public enum SaveAsKind
{
    /// <summary>못 함(<see cref="SaveAsPlan.Problem"/> 에 까닭).</summary>
    Refuse,
    /// <summary>같은 형식 — 파일(나뉜 압축이면 조각 전부)을 새 이름으로 복사.</summary>
    Copy,
    /// <summary>다른 형식(ZIP↔7Z 등) — 임시 폴더에 다 풀고 새 형식으로 다시 압축.</summary>
    Convert,
}

/// <summary>
/// 할 일 계획. <see cref="Pairs"/> 는 복사할 (원래 조각, 새 조각) — 첫 짝이 연 파일의 새 이름(다 저장하고 열 파일).
/// <see cref="Target"/> 은 다 저장하고 열 파일, <see cref="Format"/> 은 변환할 형식(<see cref="SaveAsKind.Convert"/> 일 때만).
/// </summary>
public sealed record SaveAsPlan(SaveAsKind Kind, string Target, IReadOnlyList<(string From, string To)> Pairs, CompressionFormat? Format, string? Problem)
{
    public static SaveAsPlan Refuse(string problem) => new(SaveAsKind.Refuse, string.Empty, Array.Empty<(string, string)>(), null, problem);
}

/// <summary>
/// 연 압축의 '다른 이름으로 저장'(W2-11, I3-15) — 이름 규칙과 복사(순수 규칙 + 파일 복사, 화면은 MainViewModel.Start.cs).
/// <list type="bullet">
/// <item>처음 이름은 같은 형식의 '이름 - 복사본'(나뉜 압축은 연 조각의 꼴 — '견적 - 복사본.7z.001', '자료 - 복사본.zip').</item>
/// <item>고른 이름의 형식 꼬리(<see cref="SuffixOf"/> — '.zip'·'.7z.001'·'.tar.gz'·'.part1.rar')가 연 파일과 같으면 복사: 나뉜 압축은 조각 전부를
///   같은 꼬리로(이름 부분만 바꿔) 복사한다. 그 이름의 조각이 하나라도 이미 있으면 하지 않는다(섞인 조각 세트가 생기지 않게 — 다른 이름을 고르게).</item>
/// <item>다르고 '.zip'·'.7z'(7z.dll 이 있을 때)면 변환. 그 밖은 하지 않는다.</item>
/// <item>복사는 새 파일 옆 숨김 임시 파일(<c>.~이름.8글자.tmp</c>)에 다 쓴 뒤에만 이름을 바꾼다 — 실패·취소면 임시 파일을 지우고 덮어쓰려던 파일은 그대로.</item>
/// </list>
/// </summary>
public static class ArchiveSaveAs
{
    /// <summary>처음 이름의 덧붙임.</summary>
    public const string CopySuffix = " - 복사본";

    private static readonly Regex Numbered = new(@"^(?<base>.*)\.(?<n>\d{3,})$", RegexOptions.CultureInvariant);

    /// <summary>
    /// 파일 이름의 형식 꼬리: 이름 부분(<see cref="ArchiveNaming.FolderNameFor"/> — '.001'·'.partN'·'.volN'·'.tar.gz' 등을 뗀 것)을 뺀 나머지.
    /// 예) '견적.7z.001' → '.7z.001', '자료.zip' → '.zip', 'x.tar.gz' → '.tar.gz', 'x.part1.rar' → '.part1.rar', 확장자 없음 → ''.
    /// </summary>
    public static string SuffixOf(string fileName)
    {
        string name = Path.GetFileName(fileName ?? string.Empty);
        string stem = ArchiveNaming.FolderNameFor(name);
        return name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ? name[stem.Length..] : Path.GetExtension(name);
    }

    /// <summary>처음 이름: '이름 - 복사본' + 연 파일의 꼬리.</summary>
    public static string SuggestName(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        string suffix = SuffixOf(name);
        string stem = name[..^suffix.Length];
        return stem + CopySuffix + suffix;
    }

    /// <summary>저장 창의 형식 칸: 같은 형식(꼬리) · ZIP · 7Z(쓸 수 있을 때) · 모든 파일.</summary>
    public static string Filter(string archivePath, bool sevenZipAvailable)
    {
        string suffix = SuffixOf(archivePath);
        var parts = new List<string>();
        if (suffix.Length > 0) parts.Add($"같은 형식 (*{suffix})|*{suffix}");
        if (!suffix.Equals(".zip", StringComparison.OrdinalIgnoreCase)) parts.Add("ZIP 파일로 바꿔 저장 (*.zip)|*.zip");
        if (sevenZipAvailable && !suffix.Equals(".7z", StringComparison.OrdinalIgnoreCase)) parts.Add("7Z 파일로 바꿔 저장 (*.7z)|*.7z");
        parts.Add("모든 파일 (*.*)|*.*");
        return string.Join("|", parts);
    }

    /// <summary>연 파일 <paramref name="archivePath"/> 를 <paramref name="chosenPath"/> 로 저장하는 계획.</summary>
    public static SaveAsPlan Plan(string archivePath, string chosenPath, bool sevenZipAvailable)
    {
        string source = Path.GetFullPath(archivePath);
        string target = Path.GetFullPath(chosenPath);
        if (Same(source, target))
            return SaveAsPlan.Refuse("지금 열린 파일과 같은 곳·같은 이름이에요. 다른 이름이나 다른 폴더를 골라 주세요.");

        string srcSuffix = SuffixOf(source);
        string dstSuffix = SuffixOf(target);
        if (string.Equals(srcSuffix, dstSuffix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = ArchiveVolumes.AllParts(source);
            if (parts.Count <= 1)
                return new SaveAsPlan(SaveAsKind.Copy, target, new[] { (source, target) }, null, null);

            // 나뉜 압축: 조각마다 이름 부분만 바꾼다('견적.7z.002' → '새이름.7z.002')
            string srcStem = Path.GetFileName(source)[..^srcSuffix.Length];
            string dstStem = Path.GetFileName(target)[..^dstSuffix.Length];
            string dir = Path.GetDirectoryName(target) ?? string.Empty;
            var pairs = new List<(string, string)>();
            foreach (var part in parts)
            {
                string partName = Path.GetFileName(part);
                if (!partName.StartsWith(srcStem, StringComparison.OrdinalIgnoreCase))
                    return SaveAsPlan.Refuse("나뉜 조각의 이름이 서로 달라 새 이름으로 바꿀 수 없어요. 탐색기에서 조각을 모두 복사해 주세요.");
                string to = Path.Combine(dir, dstStem + partName[srcStem.Length..]);
                if (parts.Any(p => Same(p, to)))
                    return SaveAsPlan.Refuse("지금 열린 조각과 같은 이름이 생겨요. 다른 이름이나 다른 폴더를 골라 주세요.");
                if (File.Exists(to) || Directory.Exists(to))
                    return SaveAsPlan.Refuse(TakenMessage(to));
                pairs.Add((part, to));
            }
            // 숫자 조각('…7z.001' …)은 그 뒤 번호가 이미 있으면 읽는 프로그램이 이어 붙인다 — 섞이지 않게 하지 않는다
            if (NextNumbered(pairs[^1].Item2) is { } next && (File.Exists(next) || Directory.Exists(next)))
                return SaveAsPlan.Refuse(TakenMessage(next));
            // 연 파일의 새 이름을 맨 앞에(다 저장하고 열 파일)
            int opened = pairs.FindIndex(p => Same(p.Item1, source));
            if (opened > 0)
            {
                var first = pairs[opened];
                pairs.RemoveAt(opened);
                pairs.Insert(0, first);
            }
            return new SaveAsPlan(SaveAsKind.Copy, target, pairs, null, null);
        }

        string ext = Path.GetExtension(target);
        if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return new SaveAsPlan(SaveAsKind.Convert, target, Array.Empty<(string, string)>(), CompressionFormat.Zip, null);
        if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) && sevenZipAvailable)
            return new SaveAsPlan(SaveAsKind.Convert, target, Array.Empty<(string, string)>(), CompressionFormat.SevenZip, null);
        string shown = dstSuffix.Length > 0 ? dstSuffix : "확장자 없음";
        string same = srcSuffix.Length > 0 ? $"같은 형식({srcSuffix})이나 " : string.Empty;
        return SaveAsPlan.Refuse($"‘{shown}’ 형식으로는 저장할 수 없어요. {same}{(sevenZipAvailable ? "ZIP·7Z" : "ZIP")} 로 저장해 주세요.");
    }

    private static string TakenMessage(string path) =>
        $"‘{Path.GetFileName(path)}’ 조각이 이미 있어요. 나뉜 압축은 조각이 섞이지 않게 덮어쓰지 않아요 — 다른 이름을 골라 주세요.";

    /// <summary>'이름.7z.003' → '이름.7z.004'(숫자 조각이 아니면 null).</summary>
    private static string? NextNumbered(string path)
    {
        var m = Numbered.Match(Path.GetFileName(path));
        if (!m.Success) return null;
        string n = m.Groups["n"].Value;
        if (!long.TryParse(n, out long v)) return null;
        return Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, $"{m.Groups["base"].Value}.{(v + 1).ToString().PadLeft(n.Length, '0')}");
    }

    /// <summary>
    /// 복사: 짝마다 새 파일 옆 숨김 임시 파일에 다 쓴 뒤(수정 시각도 원본대로), 모두 끝나면 이름을 바꾼다. 하나짜리는 있던 파일을 바꾼다(저장 창이 이미 물음),
    /// 여럿(나뉜 압축)은 있던 것이 없음을 <see cref="Plan"/> 이 확인했다. 실패·취소면 임시 파일과 이번에 놓은 조각을 지우고 예외를 그대로 던진다.
    /// </summary>
    public static async Task CopyAsync(IReadOnlyList<(string From, string To)> pairs, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        long total = 0;
        foreach (var (from, _) in pairs) total += new FileInfo(from).Length;
        var temps = new List<string>();
        var placed = new List<string>();
        try
        {
            long done = 0;
            int index = 0;
            foreach (var (from, to) in pairs)
            {
                ct.ThrowIfCancellationRequested();
                string tmp = OutputFiles.TempSibling(to);
                temps.Add(tmp);
                index++;
                string name = Path.GetFileName(to);
                long before = done;
                await Task.Run(() => CopyOne(from, tmp, ct, n =>
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = name,
                        BytesProcessed = before + n,
                        TotalBytes = total,
                        FilesProcessed = index - 1,
                        TotalFiles = pairs.Count,
                    })), ct);
                done += new FileInfo(tmp).Length;
            }
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < pairs.Count; i++)
            {
                string to = pairs[i].To;
                if (pairs.Count == 1) OutputFiles.ReplaceExisting(temps[i], to);
                else MoveNoOverwrite(temps[i], to);
                File.SetAttributes(to, File.GetAttributes(to) & ~FileAttributes.Hidden);
                placed.Add(to);
            }
            progress?.Report(new ArchiveProgress { CurrentFile = Path.GetFileName(pairs[0].To), BytesProcessed = total, TotalBytes = total, FilesProcessed = pairs.Count, TotalFiles = pairs.Count });
        }
        catch
        {
            foreach (var t in temps) TryDelete(t);
            // 여럿이면 이번에 놓은 조각도 지운다(반쪽 세트를 남기지 않게 — 모두 새로 만든 이름). 하나짜리는 놓았으면 끝난 것.
            if (pairs.Count > 1)
                foreach (var p in placed) TryDelete(p);
            throw;
        }
    }

    /// <summary>파일 하나를 임시 파일로(원본은 다른 프로그램이 열어 둬도 읽음 — 읽기 공유). 다 쓰면 원본의 수정 시각으로.</summary>
    private static void CopyOne(string from, string tmp, CancellationToken ct, Action<long> report)
    {
        using (var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan))
        using (var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
        {
            try
            {
                File.SetAttributes(tmp, File.GetAttributes(tmp) | FileAttributes.Hidden);
            }
            catch
            {
                // 숨기지 못해도 복사는 한다
            }
            var buffer = new byte[1 << 20];
            long copied = 0;
            var last = DateTime.MinValue;
            int read;
            while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                dst.Write(buffer, 0, read);
                copied += read;
                if ((DateTime.UtcNow - last).TotalMilliseconds >= 100)
                {
                    last = DateTime.UtcNow;
                    report(copied);
                }
            }
            dst.Flush(flushToDisk: true);
            report(copied);
        }
        File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(from));
    }

    /// <summary>덮어쓰지 않고 옮긴다(잠깐 잡힌 것은 200ms 간격으로 3번 다시).</summary>
    private static void MoveNoOverwrite(string from, string to)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: false);
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch
        {
            // 남으면 다음에 같은 이름을 쓰지 않을 뿐(임시 이름은 늘 새로)
        }
    }

    private static bool Same(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
