using System.IO.Compression;
using System.Text;
using Iljip.Services;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// [I4-1 【5】] 깨진 ZIP 고치기 판정 오라클 — 원래 파일(이름 → 내용)과 고치기 결과(보고 + 새 ZIP)를 견주어 '거짓 성공'이 없는지 본다.
/// 바이트 조합마다 기대를 적지 않고 아래 불변식으로 판정한다(2·3차에서 조합별 기대가 맴돌던 것을 끊으려고).
/// <list type="number">
/// <item>새 ZIP 의 파일 = 살린 것(M) + 확인 못 한 것(U) — 이름까지 같은 묶음. 담은 것이 없으면 새 ZIP 도 없다.</item>
/// <item>살린 파일은 원래 파일과 이름·내용이 같다(0바이트·틀린 내용·안쪽 부품은 여기서 걸림). 훑기 모드에서 글자로 읽히게 깨진 이름(cut 7)만 <see cref="Expect.NameMayDiffer"/> 로 —
///   그때도 내용은 아직 짝짓지 않은 원래 파일 하나와 같아야 한다.</item>
/// <item>확인 못 한 파일은 원래 이름(이름이 달라도 되는 훑기 경우 빼고) — 내용은 확인하지 못했으니 보지 않는다.</item>
/// <item>목록 모드: 원래 파일 수 ≤ N ≤ 원래 파일 수 + 폴더 수(cut 8 — 끝 글자만 깨진 폴더 기록), M + U + L = N.</item>
/// <item>'모두 살렸어요'(<see cref="RepairReport.IsComplete"/>)면 원래 파일이 모두 같은 이름·내용으로 새 ZIP 에 있다. 훑기 모드 요약에는 '모두'가 없다.</item>
/// <item>꼭 살려야 하는 파일(<see cref="Expect.MustSalvage"/>)은 살린 것(M)에 있다 — 경우마다 '덜 살려도 되는 것'의 한계를 지킨다.</item>
/// </list>
/// </summary>
public static class RepairOracle
{
    /// <summary>원래 압축의 파일(NFC 이름 → sha256)과 폴더 수.</summary>
    public sealed record Original(IReadOnlyDictionary<string, string> Files, int Folders);

    /// <summary>경우마다 다른 기대.</summary>
    public sealed record Expect
    {
        /// <summary>목록 모드여야(true)·훑기 모드여야(false)·어느 쪽이든(null).</summary>
        public bool? ListMode { get; init; }

        /// <summary>꼭 살려야 하는 원래 파일 이름(NFC). null 이면 따지지 않는다.</summary>
        public IReadOnlyCollection<string>? MustSalvage { get; init; }

        /// <summary>훑기 모드에서 이름이 글자로 읽히게 깨진 것을 받는다(내용은 원래 파일 하나와 같아야).</summary>
        public bool NameMayDiffer { get; init; }

        /// <summary>새 ZIP 을 일집 검사로도 본다(살린 파일에 문제가 없어야 — 확인 못 한 파일만 문제일 수 있음).</summary>
        public bool TestOutput { get; init; }
    }

    public static Original Of(FixtureInfo fx) =>
        new(fx.ExpectedFiles.ToDictionary(e => Nfc(e.Path), e => e.Sha256!, StringComparer.Ordinal), fx.Expected.Count(e => e.IsDir));

    public static Original Of(IReadOnlyDictionary<string, byte[]> files, int folders = 0) =>
        new(files.ToDictionary(kv => Nfc(kv.Key), kv => TreeAssert.Sha256(kv.Value), StringComparer.Ordinal), folders);

    public static string Nfc(string s) => s.Normalize(NormalizationForm.FormC);

    /// <summary>마지막 <see cref="ReadOutputAsync"/> 에서 암호로 풀다 난 오류(실패 설명에 붙임 — 없으면 null).</summary>
    private static readonly AsyncLocal<System.Runtime.CompilerServices.StrongBox<string?>?> ReadError = new();

    /// <summary>
    /// 새 ZIP 의 파일 항목(차례대로 — 같은 이름이 둘이어도 따로) NFC 이름과 sha256(읽지 못한 항목은 null). 암호가 없으면 System.IO.Compression 으로 항목마다
    /// (안 되는 항목만 null), 암호가 있으면 일집으로 풀어서(실패하면 이름만 — 내용 null).
    /// </summary>
    public static async Task<List<(string Name, string? Sha)>> ReadOutputAsync(string zip, string? password, string workDir)
    {
        var got = new List<(string Name, string? Sha)>();
        if (password is not null)
        {
            string dest = Path.Combine(workDir, "풀림_" + Guid.NewGuid().ToString("N")[..6]);
            try
            {
                var svc = new ArchiveServiceLocator().ResolveByContent(zip) ?? throw new UnsupportedFormatException(".zip");
                await svc.ExtractAsync(zip, dest, password).WaitAsync(TimeSpan.FromSeconds(120));
                foreach (var (name, sha) in TreeAssert.Snapshot(dest, nfc: true))
                    if (sha is not null) got.Add((name, sha));
                return got;
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                got.Clear();   // 이름만(내용은 null)
                if (ReadError.Value is { } box) box.Value = $"{ex.GetType().Name}: {ErrorCatalog.Describe(ex).Message} / {ex.Message}";
            }
        }
        using var z = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read, leaveOpen: false, Encoding.Latin1);
        foreach (var e in z.Entries)
        {
            string name = Nfc(NameOf(e.FullName));
            if (name.EndsWith('/')) continue;
            if (password is not null)
            {
                // 통째로 풀기가 한 항목(확인 못 한 것 등)에서 멈췄으면 항목마다 따로 — 풀리는 항목은 내용까지
                got.Add((name, await ReadOneAsync(zip, name, password, workDir)));
                continue;
            }
            try
            {
                using var s = e.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                got.Add((name, TreeAssert.Sha256(m.ToArray())));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                got.Add((name, null));
            }
        }
        return got;
    }

    /// <summary>항목 하나만 일집으로 풀어 sha256(못 풀면 null).</summary>
    private static async Task<string?> ReadOneAsync(string zip, string name, string password, string workDir)
    {
        string dest = Path.Combine(workDir, "하나_" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var svc = new ArchiveServiceLocator().ResolveByContent(zip) ?? throw new UnsupportedFormatException(".zip");
            await svc.ExtractEntriesAsync(zip, dest, new[] { name }, password).WaitAsync(TimeSpan.FromSeconds(120));
            var files = TreeAssert.Snapshot(dest, nfc: true).Where(kv => kv.Value is not null).ToList();
            return files.Count == 1 && files[0].Key == name ? files[0].Value : null;
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            return null;
        }
    }

    private static string NameOf(string read)
    {
        if (read.Any(c => c > 0xFF)) return read;   // UTF-8 표시가 있어 UTF-8 로 읽힘
        byte[] raw = Encoding.Latin1.GetBytes(read);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(raw); }
    }

    /// <summary>불변식을 본다. 문제가 있으면 한 줄 설명, 없으면 null.</summary>
    public static string? Check(Original o, RepairReport r, IReadOnlyList<(string Name, string? Sha)> output, Expect e)
    {
        var salvaged = r.Salvaged.Select(Nfc).ToList();
        var unverified = r.Unverified.Select(Nfc).ToList();
        string summary = $"'{r.Headline}' 살림[{string.Join(", ", salvaged)}] 확인 못 함[{string.Join(", ", unverified)}] 못 살림[{string.Join(", ", r.LostItems.Select(l => l.Display))}] 조각 {r.UnconfirmedPieces}";

        if (e.ListMode is { } lm && r.CountKnown != lm) return $"{(lm ? "목록 모드여야 하는데 훑기 모드" : "훑기 모드여야 하는데 목록 모드")} — {summary}";
        // 1. 새 ZIP = 살린 것 + 확인 못 한 것(이름 묶음 — 같은 이름이 둘이어도 수까지)
        var claimed = salvaged.Concat(unverified).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var inZip = output.Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (!claimed.SequenceEqual(inZip, StringComparer.Ordinal))
            return $"새 ZIP 의 파일 [{string.Join(", ", inZip)}] 과 보고가 다름 — {summary}";
        if ((r.OutputPath is null) != (claimed.Count == 0)) return $"새 ZIP 이 {(r.OutputPath is null ? "없는데" : "있는데")} 담은 파일 {claimed.Count}개 — {summary}";

        // 2. 살린 파일 = 원래 이름·내용. 살린 이름마다 새 ZIP 의 같은 이름 항목 하나씩(먼저 이름·내용이 맞는 것부터 짝짓고, 남은 것은 훑기 모드의 '이름이 달라도 됨'일 때만 내용으로)
        var pool = output.ToList();
        var salvagedEntries = new List<(string Name, string? Sha)>();
        foreach (var name in salvaged)
        {
            int k = pool.FindIndex(x => x.Name == name && x.Sha is not null && o.Files.TryGetValue(name, out var w) && w == x.Sha);
            if (k < 0) k = pool.FindIndex(x => x.Name == name);
            if (k < 0) return $"살렸다는 '{name}' 이 새 ZIP 에 없음 — {summary}";
            salvagedEntries.Add(pool[k]);
            pool.RemoveAt(k);
        }
        var unmatched = new HashSet<string>(o.Files.Keys, StringComparer.Ordinal);
        var rest = new List<(string Name, string? Sha)>();
        foreach (var (name, sha) in salvagedEntries)
        {
            if (sha is null) return $"살렸다는 '{name}' 을 새 ZIP 에서 읽을 수 없음{(ReadError.Value?.Value is { } re ? $"(풀기 오류 {re})" : "")} — {summary}";
            if (o.Files.TryGetValue(name, out var want) && want == sha && unmatched.Remove(name)) continue;
            rest.Add((name, sha));
        }
        foreach (var (name, sha) in rest)
        {
            string? same = !r.CountKnown && e.NameMayDiffer ? unmatched.FirstOrDefault(k => o.Files[k] == sha) : null;
            if (same is not null)
            {
                unmatched.Remove(same);
                continue;
            }
            return o.Files.ContainsKey(name)
                ? $"살렸다는 '{name}' 의 내용이 원래와 다름(0바이트·틀린 내용) — {summary}"
                : $"원래 없던 파일을 살림 '{name}'(안쪽 부품·틀린 이름) — {summary}";
        }
        // 3. 확인 못 한 파일은 원래 이름
        if (r.CountKnown || !e.NameMayDiffer)
            foreach (var name in unverified)
                if (!o.Files.ContainsKey(name)) return $"원래 없던 파일을 확인 못 함으로 옮김 '{name}' — {summary}";

        // 4. 목록 모드의 수
        if (r.CountKnown)
        {
            if (r.OriginalCount < o.Files.Count || r.OriginalCount > o.Files.Count + o.Folders)
                return $"원래 수 N={r.OriginalCount} 이 원래 파일 {o.Files.Count}개(폴더 {o.Folders})와 안 맞음 — {summary}";
            if (salvaged.Count + unverified.Count + r.LostItems.Count != r.OriginalCount)
                return $"M+U+L ≠ N({r.OriginalCount}) — {summary}";
        }
        else if (r.OriginalCount != 0) return $"훑기 모드인데 원래 수 {r.OriginalCount} — {summary}";

        // 5. '모두'
        bool all = r.Headline.Contains("모두", StringComparison.Ordinal);
        if (all != r.IsComplete) return $"요약의 '모두'와 IsComplete({r.IsComplete})가 다름 — {summary}";
        if (r.IsComplete && (unmatched.Count > 0 || salvaged.Count != o.Files.Count))
            return $"원래 파일 [{string.Join(", ", unmatched)}] 을 살리지 않고 '모두' — {summary}";
        if (!r.CountKnown && claimed.Count > 0 && !r.Headline.StartsWith("원래 몇 개였는지 확실하지 않아요", StringComparison.Ordinal))
            return $"훑기 모드인데 요약이 '{r.Headline}'";

        // 6. 꼭 살려야 하는 파일
        if (e.MustSalvage is { } must)
            foreach (var name in must.Select(Nfc))
                if (unmatched.Contains(name))
                    return $"멀쩡한 '{name}' 을 살리지 못함 — {summary}";
        return null;
    }

    /// <summary>
    /// <paramref name="src"/> 를 고쳐 <see cref="Check"/> 로 본다(원본·결과는 지우지 않음). 예외는 설명으로. <see cref="Expect.TestOutput"/> 면 새 ZIP 을 일집 검사로도 —
    /// 살린 파일에 문제가 있으면 실패(확인 못 한 파일의 문제는 받음).
    /// </summary>
    public static async Task<string?> RepairAndCheckAsync(Original o, string src, string dest, string? password, Expect e, string workDir) =>
        (await RepairCheckAsync(o, src, dest, password, e, workDir)).Failure;

    /// <summary><see cref="RepairAndCheckAsync"/> 와 같고 보고(예외로 끝났으면 null)·예외도 돌려준다 — 경우마다 더 볼 것이 있는 시험용.</summary>
    public static async Task<(string? Failure, RepairReport? Report, Exception? Error)> RepairCheckAsync(Original o, string src, string dest, string? password, Expect e, string workDir)
    {
        RepairReport r;
        try
        {
            r = await ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(120));
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            return ($"예외 {ex.GetType().Name} {ErrorCatalog.Describe(ex).Message}", null, ex);
        }
        List<(string Name, string? Sha)> output;
        ReadError.Value = new System.Runtime.CompilerServices.StrongBox<string?>();
        try
        {
            output = r.OutputPath is null ? new List<(string Name, string? Sha)>() : await ReadOutputAsync(r.OutputPath, password, workDir);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return ($"고친 ZIP 을 다른 프로그램(.NET)이 읽지 못함 {ex.GetType().Name}: {ex.Message} — {r.Headline}", r, null);
        }
        if (Check(o, r, output, e) is { } f) return (f, r, null);
        if (e.TestOutput && r.OutputPath is not null)
        {
            try
            {
                var tr = await ArchiveTestService.Instance.TestAsync(r.OutputPath, password, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(120));
                var unverified = r.Unverified.Select(Nfc).ToHashSet(StringComparer.Ordinal);
                foreach (var p in tr.Problems)
                    if (!unverified.Contains(Nfc(p.Entry))) return ($"고친 ZIP 을 일집으로 검사하면 '{tr.Headline}' ({p.Entry}: {p.Reason}) — {r.Headline}", r, null);
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                if (r.Unverified.Count == 0) return ($"고친 ZIP 검사 예외 {ex.GetType().Name} {ErrorCatalog.Describe(ex).Message} — {r.Headline}", r, null);
            }
        }
        return (null, r, null);
    }
}
