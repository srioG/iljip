using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Iljip.Models;
using Iljip.Services.Editing;

namespace Iljip.Services;

/// <summary>못 살린 파일 하나 — 이름(목록 쪽 표시 이름, 읽을 수 없으면 '(이름을 읽을 수 없는 파일 — 목록 k번째)')과 짧은 이유(있을 때만).</summary>
public sealed record RepairLoss(string Name, string? Reason = null)
{
    /// <summary>결과 창 한 줄('이름 — 이유'). 손상돼 제어 문자가 섞인 이름은 '?' 로 바꾸고 줄여 보인다.</summary>
    public string Display => Reason is null ? ErrorMessages.DisplayName(Name) : $"{ErrorMessages.DisplayName(Name)} — {Reason}";
}

/// <summary>
/// 깨진 ZIP 고치기 결과(I3-12 — I4-1 '정직한 보수 모드').
/// 목록 모드(끝의 파일 목록을 믿을 수 있음)는 원래 개수 N 을 목록이 정한다: '전체 N개 중 M개를 살렸어요.'(모든 기록을 검사값으로 확인했을 때만 'N개를 모두 살렸어요.').
/// 훑기 모드(목록 없음·못 믿음)는 '원래 몇 개였는지 확실하지 않아요 — M개를 살렸어요.'.
/// M(<see cref="Salvaged"/>)은 데이터 검사값(CRC·크기, AES 는 인증값)으로 확인한 파일만 — 확인하지 못하고 옮긴 파일(<see cref="Unverified"/>)·
/// 못 살린 파일(<see cref="LostItems"/>)·믿기 어려워 넣지 않은 조각 수(<see cref="UnconfirmedPieces"/>)는 따로 알린다.
/// </summary>
public sealed class RepairReport
{
    /// <summary>원래 파일 수를 아는가(목록 모드).</summary>
    public bool CountKnown { get; init; }

    /// <summary>원래 파일 수 N — 목록 모드에서 폴더가 아닌 목록 기록 수(이름을 못 읽는 기록 포함). 훑기 모드면 0.</summary>
    public int OriginalCount { get; init; }

    /// <summary>살린 파일(M) — 검사값으로 내용을 확인해 새 ZIP 에 담은 것만.</summary>
    public IReadOnlyList<string> Salvaged { get; init; } = Array.Empty<string>();

    /// <summary>내용을 확인하지 못하고 그대로 옮긴 파일(U — 암호를 넣지 않음·일집이 확인할 수 없는 압축 방식). '살렸어요' 수에 넣지 않는다.</summary>
    public IReadOnlyList<string> Unverified { get; init; } = Array.Empty<string>();

    /// <summary>못 살린 파일(L)과 짧은 이유.</summary>
    public IReadOnlyList<RepairLoss> LostItems { get; init; } = Array.Empty<RepairLoss>();

    /// <summary>못 살린 파일 이름.</summary>
    public IReadOnlyList<string> Lost => LostItems.Select(l => l.Name).ToList();

    /// <summary>훑기 모드에서 찾았지만 믿기 어려워 넣지 않은 파일 머리 수(K).</summary>
    public int UnconfirmedPieces { get; init; }

    /// <summary>만든 새 ZIP(담은 파일이 없으면 null — 만들지 않음).</summary>
    public string? OutputPath { get; init; }

    /// <summary>살핀 조각 수 — 나눠진 ZIP('이름.z01 … 이름.zip'·'이름.zip.001 …')을 이어서 살폈으면 2 이상.</summary>
    public int VolumeCount { get; init; } = 1;

    /// <summary>'모두 살렸어요' — 목록 모드이고 목록의 모든 파일 기록을 검사값으로 확인해 살렸을 때만.</summary>
    public bool IsComplete => CountKnown && OriginalCount > 0 && Salvaged.Count == OriginalCount && Unverified.Count == 0 && LostItems.Count == 0;

    /// <summary>훑기 모드의 덧말.</summary>
    public const string UncertainNote = "압축 파일 끝의 파일 목록이 없거나 믿을 수 없어서, 앞에서부터 차례로 확인되는 파일만 살렸어요.";

    /// <summary>믿기 어려워 넣지 않은 조각을 알리는 말.</summary>
    public static string PiecesNote(int count) => $"목록에 없는 조각 {count}개 — 믿기 어려워 넣지 않았어요.";

    /// <summary>한 줄 요약.</summary>
    public string Headline
    {
        get
        {
            if (Salvaged.Count == 0 && Unverified.Count == 0)
                return CountKnown && OriginalCount > 0 ? $"전체 {OriginalCount}개 중 하나도 살리지 못했어요." : "살릴 수 있는 파일을 찾지 못했어요.";
            if (!CountKnown) return $"원래 몇 개였는지 확실하지 않아요 — {Salvaged.Count}개를 살렸어요.";
            return IsComplete ? $"{OriginalCount}개를 모두 살렸어요." : $"전체 {OriginalCount}개 중 {Salvaged.Count}개를 살렸어요.";
        }
    }

    /// <summary>결과 창 글: 요약, (훑기 모드 덧말), 못 살린 파일, 확인하지 못한 파일, 조각 수, (나눠진 ZIP 이면 조각 수), 새 파일 위치(원래 파일은 그대로).</summary>
    public string ToDisplayText()
    {
        const int max = 20;
        var lines = new List<string> { Headline };
        if (!CountKnown && Salvaged.Count + Unverified.Count > 0) lines.Add(UncertainNote);
        if (LostItems.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("못 살린 파일:");
            foreach (var l in LostItems.Take(max)) lines.Add($"- {l.Display}");
            if (LostItems.Count > max) lines.Add($"외 {LostItems.Count - max}개");
        }
        if (Unverified.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"내용을 확인하지 못하고 그대로 옮긴 파일 {Unverified.Count}개(암호를 넣지 않았거나 일집이 확인할 수 없는 압축 방식 — 살린 수에는 넣지 않았어요):");
            foreach (var n in Unverified.Take(max)) lines.Add($"- {n}");
            if (Unverified.Count > max) lines.Add($"외 {Unverified.Count - max}개");
        }
        if (UnconfirmedPieces > 0)
        {
            lines.Add(string.Empty);
            lines.Add(PiecesNote(UnconfirmedPieces));
        }
        if (VolumeCount > 1)
        {
            lines.Add(string.Empty);
            lines.Add($"나눠진 조각 {VolumeCount}개를 이어서 살폈어요.");
        }
        lines.Add(string.Empty);
        lines.Add(OutputPath is not null
            ? $"살린 파일은 새 ZIP 에 담았어요: {OutputPath}\n원래 파일은 그대로예요."
            : "새 ZIP 은 만들지 않았어요. 원래 파일은 그대로예요.");
        return string.Join("\n", lines);
    }
}

/// <summary>깨진 ZIP 고치기(I3-12). 시험은 진짜(<see cref="ZipRepairer"/>)를 그대로 쓴다 — 임시 폴더의 파일만 다룬다.</summary>
public interface IZipRepairer
{
    /// <summary>
    /// <paramref name="sourcePath"/>(원본 — 건드리지 않음)에서 살릴 수 있는 항목만 <paramref name="destPath"/> 새 ZIP 으로 옮긴다.
    /// 암호를 주면 암호 항목도 내용까지 확인하고, 주지 않으면 그대로 옮긴다('확인 못 함'). 준 암호가 어느 암호 항목과도 맞지 않으면 <see cref="WrongPasswordException"/>.
    /// 나눠진 ZIP 의 조각을 고르면 세트를 이어서 살핀다 — 조각이 없으면 <see cref="VolumeMissingException"/>.
    /// </summary>
    Task<RepairReport> RepairAsync(string sourcePath, string destPath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken);

    /// <summary>암호 항목이 있어 보이는가(고치기 전에 암호를 물을지 — 머리만 훑음, 못 읽으면 false). 작업 스레드에서 돌고 진행·취소가 된다.</summary>
    Task<bool> HasEncryptedEntriesAsync(string sourcePath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// 깨진 ZIP 고치기 — I4-1 정직한 보수 모드(2·3차의 바이트 조합별 추측을 버리고 구조를 바꿈). 원칙 셋:
/// <b>원래 개수는 목록이 정하고, 살렸다는 증거는 데이터 검사값이며, 훑기는 맨 앞에서 이어지고 끝이 데이터로 확인된 사슬만 따라간다.</b>
/// <list type="bullet">
/// <item>목록 모드(<see cref="CentralDirectoryTrust"/>): 끝 머리 후보의 목록을 앞에서부터 읽어 끝 머리 바로 앞에서 정확히 끝나고, 항목 수·목록 크기·목록 위치 칸 가운데
///   둘 이상이 맞고, (위치가 밀렸으면 'MZ' 로 시작하고) 가장 앞 항목보다 앞에 머리 서명 비슷한 자리가 없을 때. 기록마다 가리키는 로컬 머리와 이름 바이트·UTF-8 표시·
///   유니코드 이름(0x7075)이 같아야 하고(다르면 '이름 정보가 서로 달라요' — 어느 쪽이 맞는지 가르지 않음), 목록 값·로컬 값 두 증인 가운데 데이터 CRC 로 확인되는 쪽으로 살린다.
///   데이터는 다음 맞는 항목 자리·목록 시작을 넘지 않는다. 로컬 머리를 훑지 않는다(목록에 없는 머리 — 저장해 담은 문서 속 부품 — 는 보지도 세지도 않음).</item>
/// <item>훑기 모드(<see cref="ScanChains"/>): 파일 맨 앞이 머리일 때만 앞 사슬을 시작하고, 끝을 데이터로 믿을 수 있을 때만(확인됨·서술자 맞음·크기 0 + 바로 뒤 서명·
///   확인 못 함 + 바로 뒤 서명 — 검사값이 압축 바이트를 모두 덮어 끝이 정확하면 뒤가 깨져도 그 항목까지) 다음 머리로 간다. 멈춘 뒤 다시 찾은 머리는 읽히는 목록 기록이
///   그 자리·이름을 가리킬 때만 넣고, 나머지는 수(K)만 알린다. 맨 앞 머리의 목록 암호 표시(bit 13 — 다른 표시가 깨져 켜진 것이 아닐 때)는 '고칠 수 없어요'.</item>
/// <item>확인 못 한 항목은 '살렸어요' 에 넣지 않고, 크기 0 으로 '살리기'는 두 증인이 모두 0 이고 끝이 기록 경계에 정확히 닿을 때만. 깨진 데이터·머리는 고치지 않는다.</item>
/// <item>쓰기(<see cref="RepairWriter"/>): 확인한 값으로 로컬 머리·목록 기록을 새로 쓰고 압축 데이터만 그대로 옮긴다. 숨김 임시 파일 → 다시 읽어 확인 → 바꿔 넣기.
///   원본(나눠진 ZIP 이면 그 조각들)과 같은 경로는 거절. 네트워크·장치 입출력 오류는 '못 살린 파일'로 세지 않고 고치기 전체를 멈춘다.</item>
/// </list>
/// </summary>
public sealed class ZipRepairer : IZipRepairer
{
    public static readonly ZipRepairer Instance = new();

    private const uint LocalSig = ZipDirectory.LocalHeaderSig;
    private const uint CentralSig = ZipDirectory.CentralSig;
    private const uint EndSig = ZipDirectory.EndSig;
    private const uint Zip64EndSig = ZipDirectory.Zip64EndSig;
    private const uint DescriptorSig = ZipRawRewriter.DescriptorSig;
    private const int AesExtraId = 0x9901;
    private const int UnicodePathExtraId = 0x7075;
    private const int Utf8Flag = 0x0800;

    /// <summary>새 ZIP 에 옮기는 표시(암호·압축 선택·서술자·강한 암호·UTF-8) — 목록 암호(bit 13)·패치 데이터 같은 깨진 표시는 옮기지 않는다.</summary>
    private const int KeptFlags = 0x0001 | 0x0002 | 0x0004 | 0x0008 | 0x0040 | Utf8Flag;

    /// <summary>
    /// 새 머리에 옮길 표시(UTF-8·서술자 표시는 따로 정함). 강한 암호 표시(bit 6)는 암호 표시(bit 0)가 있을 때만 — 암호 없는 항목에 깨져 켜진 bit 6 을
    /// 옮기면 다른 프로그램(Python zipfile 등)이 '강한 암호'로 보고 살린 파일을 거절했다(I4-1 검증 지적).
    /// </summary>
    private static int KeptBits(int flags)
    {
        int kept = flags & KeptFlags & ~(Utf8Flag | 0x08);
        return (kept & 0x01) != 0 ? kept : kept & ~0x40;
    }

    /// <summary>원래 목록을 읽을 크기 한도(그보다 크면 목록을 믿지 않는다).</summary>
    private const long MaxCentralBytes = 256L * 1024 * 1024;

    /// <summary>숫자 분할 조각('이름.zip.001', '이름.002' …).</summary>
    private static readonly Regex NumberedVolume = new(@"^(?<base>.+)\.(?<n>\d{3,})$", RegexOptions.CultureInvariant);

    /// <summary>PKZIP 나눔의 앞 조각('이름.z01' …).</summary>
    private static readonly Regex SpanPiece = new(@"\.z\d{2,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>제안하는 결과 이름('이름_복구.zip' — 나눠진 ZIP 의 조각이면 세트 이름으로: '자료.zip.001'·'자료.z01' → '자료_복구.zip').</summary>
    public static string SuggestedName(string sourcePath)
    {
        string name = Path.GetFileName(sourcePath);
        var m = NumberedVolume.Match(name);
        if (m.Success && m.Groups["base"].Value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name = m.Groups["base"].Value;
        else if (SpanPiece.IsMatch(name)) name = Path.GetFileNameWithoutExtension(name) + ".zip";
        return $"{Path.GetFileNameWithoutExtension(name)}_복구.zip";
    }

    /// <summary>
    /// 고칠 수 있는 ZIP 이름인가 — '.zip', 나눠진 ZIP 의 조각('자료.zip.001' 같은 숫자 분할, '자료.z01' 같은 PKZIP 나눔). 문제 창·검사 결과의 'ZIP 고치기 시도'를
    /// 이것으로 정한다(IJ3-02 4차 검증: 예전엔 '.zip' 으로 끝날 때만 권해 나눠진 ZIP 은 고치기 버튼이 없었다).
    /// </summary>
    public static bool IsRepairableName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        string name = Path.GetFileName(path);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || SpanPiece.IsMatch(name)) return true;
        var m = NumberedVolume.Match(name);
        return m.Success && m.Groups["base"].Value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>원본과 같은 경로에 저장하려 할 때의 문구.</summary>
    public const string SameAsSourceMessage = "고친 ZIP 은 원래 파일과 다른 이름으로 저장해야 해요. 다른 이름을 골라 주세요.";

    /// <summary>못 살린 파일의 이유 — 암호 확인값이 맞지 않음.</summary>
    internal const string PasswordMismatchReason = "암호가 맞지 않아요";

    /// <summary>같은 암호로 다른 파일은 확인됐는데 이 파일만 암호 확인값이 맞지 않을 때의 이유(암호 머리가 깨졌을 수 있음).</summary>
    internal const string PasswordMismatchMaybeDamaged = "암호가 맞지 않아요(같은 암호로 다른 파일은 확인돼서, 이 파일이 손상됐을 수 있어요)";

    /// <summary>파일 목록까지 암호로 잠근 ZIP(PKWARE 목록 암호 — 플래그 bit 13)일 때의 문구(I4-1 cut 11).</summary>
    public const string ListEncryptedMessage = "파일 목록까지 암호로 잠겨 있어 고칠 수 없어요.";

    public Task<RepairReport> RepairAsync(string sourcePath, string destPath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => Repair(sourcePath, destPath, password, progress, cancellationToken), cancellationToken);

    public Task<bool> HasEncryptedEntriesAsync(string sourcePath, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        Task.Run(() => HasEncryptedEntries(sourcePath, progress, cancellationToken), cancellationToken);

    /// <summary>암호 항목이 있어 보이는가(동기 — 시험·작업 스레드용).</summary>
    public bool HasEncryptedEntries(string sourcePath) => HasEncryptedEntries(sourcePath, null, CancellationToken.None);

    private static bool HasEncryptedEntries(string sourcePath, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var source = OpenSource(Path.GetFullPath(sourcePath));
            var src = source.Stream;
            var scan = new Scanner(src);
            long len = src.Length, pos = 0, reported = -1;
            string name = Path.GetFileName(sourcePath);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (progress is not null && pos - reported >= 1 << 20)
                {
                    reported = pos;
                    progress.Report(new ArchiveProgress { CurrentFile = name, BytesProcessed = Math.Min(pos, len), TotalBytes = len });
                }
                long at = scan.FindNext(pos, len, LocalSig, ct);
                if (at < 0) return false;
                var h = Plausible(src, at, len);
                if (h is not null && h.Encrypted && !IsDirName(h.Name)) return true;
                pos = h is not null && !h.Bit3 && !h.CSizeFull && h.DataStart + h.CSize <= len ? Math.Max(at + 4, h.DataStart + h.CSize) : at + 4;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    // ───────────────────────────── 원본(한 파일 또는 나눠진 조각) ─────────────────────────────

    /// <summary>고칠 원본: 한 파일, 또는 나눠진 ZIP 의 조각을 차례로 이어 붙인 것(읽기만).</summary>
    private sealed class Source : IDisposable
    {
        public required Stream Stream { get; init; }
        public required IReadOnlyList<string> Volumes { get; init; }

        /// <summary>PKZIP 나눔이면 조각(디스크)마다 이어 붙인 스트림 안 시작 위치. 한 파일·숫자 분할이면 null(목록의 위치가 그대로 맞음 — 디스크 번호는 보지 않음).</summary>
        public long[]? DiskStarts { get; init; }

        /// <summary>목록의 (디스크 번호, 위치) → 이어 붙인 스트림 안 위치(모르면 -1).</summary>
        public long Map(long disk, long offset)
        {
            if (offset < 0) return -1;
            if (DiskStarts is null) return offset;
            return disk >= 0 && disk < DiskStarts.Length ? DiskStarts[disk] + offset : -1;
        }

        public void Dispose() => Stream.Dispose();
    }

    /// <summary>
    /// 원본을 연다. 숫자 분할('자료.zip.001' — 어느 조각을 골라도 001 부터)은 바이트로 자른 것이라 이어 붙이면 위치가 그대로 맞는다.
    /// PKZIP 나눔('자료.z01 … 자료.zip' — 어느 조각을 골라도)은 끝 머리가 알려 주는 조각 수만큼(끝 머리가 깨졌으면 '.z01' 부터 있는 데까지) 이어 붙이고
    /// 조각마다 시작 위치를 기억한다(목록의 위치는 조각 안 위치). 조각이 없으면 그 이름으로 <see cref="VolumeMissingException"/>.
    /// </summary>
    private static Source OpenSource(string fullPath)
    {
        string dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string name = Path.GetFileName(fullPath);

        var m = NumberedVolume.Match(name);
        string first = m.Success ? Path.Combine(dir, m.Groups["base"].Value + "." + "1".PadLeft(m.Groups["n"].Value.Length, '0')) : string.Empty;
        if (m.Success && (m.Groups["base"].Value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || File.Exists(first)))
        {
            if (!File.Exists(first)) throw new VolumeMissingException(Path.GetFileName(first));
            var split = SplitVolumeStream.Open(first);
            // 가운데 조각이 빠졌으면(뒤 번호 조각은 있음) 그 이름으로 알린다 — 앞 조각만 훑고 뒤의 파일을 말없이 빼지 않게
            string? gap = MissingMiddleVolume(dir, m.Groups["base"].Value, m.Groups["n"].Value.Length, split.Volumes.Count);
            if (gap is not null)
            {
                split.Dispose();
                throw new VolumeMissingException(gap);
            }
            return new Source { Stream = split, Volumes = split.Volumes };
        }

        string? last = SpanPiece.IsMatch(name) ? Path.Combine(dir, Path.GetFileNameWithoutExtension(name) + ".zip")
            : name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? fullPath
            : null;
        if (last is not null)
        {
            bool pickedPiece = !ReferenceEquals(last, fullPath);
            if (pickedPiece && !File.Exists(last)) throw new VolumeMissingException(Path.GetFileName(last));
            var (endReadable, disks, cdDisk, cdInLast) = SpanInfo(last);
            // 끝 머리의 '이 조각 번호' 칸이 깨져 조각 수가 틀려도: 앞 조각이 빠짐없이 있고 그 뒤 번호 조각은 없으며, 목록이 마지막 조각에서 시작하고
            // 그 '목록 시작 조각' 칸이 있는 조각 수와 맞으면 있는 조각으로 본다. 정말 빠진 조각은 목록 시작 조각 칸과 어긋나 그대로 알림.
            int present = 0;
            while (present < 998 && File.Exists(SpannedZipStream.PrecedingVolumePath(last, present + 1))) present++;
            if (present > 0 && disks != present + 1 && cdInLast && cdDisk == present
                && !Enumerable.Range(present + 2, 16).Any(k => File.Exists(SpannedZipStream.PrecedingVolumePath(last, k))))
                disks = present + 1;
            // 끝 머리가 조각이 여럿이라고 해도 앞 조각이 하나도 없으면 한 파일로 본다. 앞 조각이 일부만 있으면 빠진 조각을 알린다.
            bool spanned = pickedPiece
                || (disks >= 2 || !endReadable) && File.Exists(SpannedZipStream.PrecedingVolumePath(last, 1))
                || disks >= 2 && File.Exists(SpannedZipStream.PrecedingVolumePath(last, (int)disks - 1));
            if (spanned)
            {
                var paths = new List<string>();
                for (int i = 1; i < 1000 && (disks < 2 || i < disks); i++)
                {
                    string p = SpannedZipStream.PrecedingVolumePath(last, i);
                    if (!File.Exists(p))
                    {
                        // 조각 수를 알거나(끝 머리), 뒤 번호 조각이 있으면 가운데가 빠진 것
                        if (disks >= 2 || Enumerable.Range(i + 1, 16).Any(k => File.Exists(SpannedZipStream.PrecedingVolumePath(last, k))))
                            throw new VolumeMissingException(Path.GetFileName(p));
                        break;
                    }
                    paths.Add(p);
                }
                paths.Add(last);
                var parts = SplitVolumeStream.OpenParts(paths);
                var starts = new long[parts.PartCount];
                for (int i = 0; i < starts.Length; i++) starts[i] = parts.PartStart(i);
                return new Source { Stream = parts, Volumes = paths, DiskStarts = starts };
            }
        }
        return new Source
        {
            Stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16),
            Volumes = new[] { fullPath },
        };
    }

    /// <summary>숫자 분할 조각을 <paramref name="opened"/>개까지 이어 열었는데 그보다 뒤 번호 조각이 있으면 빠진 조각 이름(없으면 null).</summary>
    private static string? MissingMiddleVolume(string dir, string baseName, int digits, int opened)
    {
        var numbered = new Regex("^" + Regex.Escape(baseName) + @"\.(?<n>\d{" + digits + ",})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        bool later = Directory.EnumerateFiles(string.IsNullOrEmpty(dir) ? "." : dir, baseName + ".*")
            .Select(p => numbered.Match(Path.GetFileName(p)))
            .Any(x => x.Success && int.TryParse(x.Groups["n"].Value, out int n) && n > opened + 1);
        return later ? $"{baseName}.{(opened + 1).ToString().PadLeft(digits, '0')}" : null;
    }

    /// <summary>
    /// '.zip' 끝 머리를 읽을 수 있는가, 그것이 알려 주는 조각 수(읽을 수 없거나 말이 안 되면 0), 목록이 시작하는 조각 번호,
    /// 목록 위치 칸이 이 파일 안의 목록 기록('PK\x01\x02')을 가리키는가.
    /// </summary>
    private static (bool Readable, long Disks, long CdDisk, bool CdInLast) SpanInfo(string zipPath)
    {
        using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 12);
        var end = ZipDirectory.FindEnd(fs);
        if (end is null) return (false, 0, -1, false);
        long disks = end.TotalDisks;
        bool cdInLast = end.CdOffset + 4 <= end.EndPosition && SignatureAt(fs, end.CdOffset, fs.Length) == CentralSig;
        return (true, disks is >= 2 and < 1000 ? disks : 0, end.CdDisk, cdInLast);
    }

    // ───────────────────────────── 본체 ─────────────────────────────

    private RepairReport Repair(string sourcePath, string destPath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        string srcFull = Path.GetFullPath(sourcePath);
        string destFull = Path.GetFullPath(destPath);
        if (string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
            throw new ArchiveEditException(SameAsSourceMessage);

        using var source = OpenSource(srcFull);
        if (source.Volumes.Any(v => string.Equals(Path.GetFullPath(v), destFull, StringComparison.OrdinalIgnoreCase)))
            throw new ArchiveEditException(SameAsSourceMessage);

        var run = new Run(source, srcFull, PasswordCandidates(password), progress, ct);
        var list = CentralDirectoryTrust.Lists(source, ct, trustedOnly: true).FirstOrDefault();
        if (list is not null) run.RepairListed(list);
        else run.ScanChains(CentralDirectoryTrust.Lists(source, ct, trustedOnly: false));

        // 준 암호가 어느 암호 항목과도 맞지 않으면 다시 묻게(틀린 암호와 손상을 가를 수 없음 — '손상일 수도' 덧말)
        if (run.Candidates.Count > 0 && run.EncryptedSeen > 0 && run.PasswordMatched == 0 && run.AnyMismatch)
            throw new WrongPasswordException(mayBeCorrupt: true);
        // 같은 암호로 다른 파일은 확인됐으면 그 파일만 암호가 안 맞는 것은 손상일 수 있다(I4-1 검증 지적: 암호 머리가 깨진 파일에 '암호가 맞지 않아요'만)
        var lost = run.PasswordMatched > 0
            ? run.Lost.Select(l => l.Reason == PasswordMismatchReason ? l with { Reason = PasswordMismatchMaybeDamaged } : l).ToList()
            : run.Lost;

        string? output = null;
        if (run.Kept.Any(k => !k.IsDirectory))
        {
            RepairWriter.Write(source.Stream, destFull, run.Kept, ct);
            output = destFull;
        }

        var files = run.Kept.Where(k => !k.IsDirectory).ToList();
        var report = new RepairReport
        {
            CountKnown = list is not null,
            OriginalCount = list is not null ? run.OriginalCount : 0,
            Salvaged = files.Where(f => f.Verified).Select(f => f.Name).ToList(),
            Unverified = files.Where(f => !f.Verified).Select(f => f.Name).ToList(),
            LostItems = lost,
            UnconfirmedPieces = list is null ? run.Pieces : 0,
            OutputPath = output,
            VolumeCount = source.Volumes.Count,
        };
        // 로그는 고치기 한 번에 한 기록(요약 + 못 살린 이름 처음 50개) — 시도마다 남기지 않는다(I4-1: 로그 상한)
        var note = new StringBuilder($"ZIP 고치기: {srcFull} — {report.Headline} (확인 못 함 {report.Unverified.Count}, 못 살림 {report.LostItems.Count}, 조각 {report.UnconfirmedPieces})");
        foreach (var l in report.LostItems.Take(50)) note.Append("\n  못 살림: ").Append(l.Display);
        if (report.LostItems.Count > 50) note.Append($"\n  외 {report.LostItems.Count - 50}개");
        Logger.LogNote(note.ToString());
        return report;
    }

    // ───────────────────────────── 읽은 값 ─────────────────────────────

    /// <summary>로컬 머리 하나(읽은 그대로 — ZIP64 추가 필드까지 반영한 크기).</summary>
    private sealed class LocalHead
    {
        public long Start;
        public long DataStart;
        public int Version;
        public int Flags;
        public int Method;
        public ushort Time;
        public ushort Date;
        public uint Crc;
        public long CSize;
        public long USize;
        public bool CSizeFull;   // 칸이 0xFFFFFFFF 인데 ZIP64 값이 없음 — 크기를 모름
        public bool USizeFull;
        public byte[] Name = Array.Empty<byte>();
        public byte[]? Aes;      // 0x9901 내용 7바이트
        public byte[]? Unicode;  // 0x7075 내용(판 1 + CRC 4 + UTF-8 이름)
        public bool HasZip64;
        public bool ExtraWhole = true;   // 추가 필드가 블록(머리 4바이트 + 크기)으로 남김없이 나뉜다 — 아니면 추가 필드 길이 칸이 깨졌을 수 있음
        public bool Bit3 => (Flags & 0x08) != 0;
        public bool Encrypted => (Flags & 0x01) != 0;
        public bool AllZero => Crc == 0 && CSize == 0 && USize == 0 && !CSizeFull && !USizeFull;
    }

    /// <summary>확인에 쓰는 한 벌의 값(목록 쪽이나 로컬 쪽 증인).</summary>
    private sealed record Values(int Flags, int Method, uint Crc, long CSize, long USize, byte[]? Aes, ushort Time)
    {
        public bool Encrypted => (Flags & 0x01) != 0;
        public bool IsAes => Method == 99;
        public bool Same(Values o) => Flags == o.Flags && Method == o.Method && Crc == o.Crc && CSize == o.CSize && USize == o.USize && SameBytes(Aes, o.Aes);
    }

    /// <summary>새 ZIP 에 넣을 항목(확인한 값 또는 두 증인이 같은 값).</summary>
    private sealed class Kept
    {
        public required string Name { get; init; }
        public required byte[] NameBytes { get; init; }
        public required bool IsDirectory { get; init; }
        public required bool Verified { get; init; }
        public long DataStart { get; init; }
        public int Flags { get; init; }          // bit 3 은 KeepDescriptor 일 때만 남긴다
        public int Method { get; init; }
        public ushort Time { get; init; }
        public ushort Date { get; init; }
        public uint Crc { get; init; }
        public long CSize { get; init; }
        public long USize { get; init; }
        public byte[]? Aes { get; init; }
        public byte[]? Unicode { get; init; }   // 이름 바이트와 CRC 가 맞고 모양도 맞는 0x7075 내용만
        public int VersionNeeded { get; init; } = 20;
        public int MadeBy { get; init; } = 20;
        public ushort InternalAttr { get; init; }
        public uint ExternalAttr { get; init; }
        public bool KeepDescriptor { get; init; } // ZipCrypto + bit 3 — 암호 확인 바이트가 시각에서 오므로 bit 3·서술자를 그대로
    }

    private enum Verdict { Ok, Bad, CannotCheck, PasswordMismatch }

    // ───────────────────────────── 목록을 믿는 조건 ─────────────────────────────

    /// <summary>끝 머리 후보 하나에서 읽은 목록(중앙 디렉터리) — 그 기록들과 시작 위치, 로컬 위치에 더할 값.</summary>
    private sealed class CentralList
    {
        public required List<ZipDirectory.CentralRecord> Records { get; init; }
        public required long Start { get; init; }
        public required long Delta { get; init; }

        /// <summary>기록이 가리키는 로컬 머리 위치(이어 붙인 스트림 안 — 모르면 -1).</summary>
        public long Position(Source src, ZipDirectory.CentralRecord r)
        {
            long m = src.Map(r.DiskStart, r.LocalOffset);
            return m < 0 ? -1 : m + Delta;
        }
    }

    /// <summary>
    /// 목록을 믿는 조건(I4-1 ②): 파일 끝 64KB+22 안의 'PK\5\6' 후보를 끝에서부터(주석·디스크 칸은 보지 않음 — ZIP64 면 'PK\6\6' 레코드의 값).
    /// 목록 시작은 위치 칸이 'PK\1\2' 를 가리키면 그곳(delta 0), 아니면 '끝 머리(또는 ZIP64 끝 레코드) − 목록 크기'(delta = 그 값 − 위치 칸).
    /// 믿으려면 a) 기록들이 끝 머리 바로 앞에서 정확히 끝나고 b) 항목 수·목록 크기·목록 위치 칸 가운데 둘 이상이 읽은 것과 맞고 c) delta≠0 이면 파일 맨 앞이 'MZ',
    /// d) 목록이 가리키는 가장 앞 항목보다 앞에 로컬 머리 서명 4바이트 중 3바이트 이상 맞는 자리('P' 로 시작)가 없어야 한다.
    /// 훑기 모드는 정확히 끝나지 않아도 읽히는 만큼의 기록(c·d 는 지킴)을 다시 찾은 사슬의 증거로만 쓴다.
    /// </summary>
    private static class CentralDirectoryTrust
    {
        public static List<CentralList> Lists(Source source, CancellationToken ct, bool trustedOnly)
        {
            var result = new List<CentralList>();
            var s = source.Stream;
            long len = s.Length;
            if (len < ZipDirectory.EndLength) return result;
            int tailLen = (int)Math.Min(len, ZipDirectory.EndLength + 0xFFFF);
            long tailStart = len - tailLen;
            byte[] tail = ZipDirectory.ReadAt(s, tailStart, tailLen);
            for (int i = tailLen - ZipDirectory.EndLength; i >= 0; i--)
            {
                if (ZipDirectory.U32(tail, i) != EndSig) continue;
                ct.ThrowIfCancellationRequested();
                try
                {
                    AddCandidates(source, tailStart + i, tail.AsSpan(i, ZipDirectory.EndLength).ToArray(), trustedOnly, result);
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException or OverflowException)
                {
                    // 이 후보는 못 읽음 — 다음 후보
                }
                if (trustedOnly && result.Count > 0) return result;
            }
            return result;
        }

        private static void AddCandidates(Source source, long e, byte[] end, bool trustedOnly, List<CentralList> result)
        {
            var s = source.Stream;
            long len = s.Length;
            long cdDisk = ZipDirectory.U16(end, 6), total = ZipDirectory.U16(end, 10), size = ZipDirectory.U32(end, 12), off = ZipDirectory.U32(end, 16);
            long limit = e;
            if (e >= ZipDirectory.Zip64LocatorLength && SignatureAt(s, e - ZipDirectory.Zip64LocatorLength, len) == ZipDirectory.Zip64LocatorSig)
            {
                long z = -1, beforeLocator = e - ZipDirectory.Zip64LocatorLength;
                if (beforeLocator - ZipDirectory.Zip64EndLength >= 0 && SignatureAt(s, beforeLocator - ZipDirectory.Zip64EndLength, len) == Zip64EndSig)
                    z = beforeLocator - ZipDirectory.Zip64EndLength;
                else
                {
                    byte[] loc = ZipDirectory.ReadAt(s, beforeLocator, ZipDirectory.Zip64LocatorLength);
                    long p = source.Map(ZipDirectory.U32(loc, 4), Clamp(ZipDirectory.U64(loc, 8)));
                    if (p >= 0 && p + ZipDirectory.Zip64EndLength <= beforeLocator && SignatureAt(s, p, len) == Zip64EndSig) z = p;
                }
                if (z < 0) return;   // ZIP64 위치 표는 있는데 ZIP64 끝 레코드가 없음 — 이 후보는 믿지 않는다
                byte[] zr = ZipDirectory.ReadAt(s, z, ZipDirectory.Zip64EndLength);
                cdDisk = ZipDirectory.U32(zr, 20);
                total = Clamp(ZipDirectory.U64(zr, 32));
                size = Clamp(ZipDirectory.U64(zr, 40));
                off = Clamp(ZipDirectory.U64(zr, 48));
                limit = z;
            }

            long fieldPos = source.Map(cdDisk, off);
            var starts = new List<(long Start, long Delta)>();
            if (fieldPos >= 0 && fieldPos + 4 <= limit && SignatureAt(s, fieldPos, len) == CentralSig) starts.Add((fieldPos, 0));
            long byEnd = limit - size;
            if (size >= 0 && byEnd >= 0 && byEnd != fieldPos && byEnd + 4 <= limit && SignatureAt(s, byEnd, len) == CentralSig)
                starts.Add((byEnd, source.DiskStarts is null && fieldPos >= 0 ? byEnd - fieldPos : 0));

            foreach (var (start, delta) in starts)
            {
                if (limit - start > MaxCentralBytes) continue;
                if (delta != 0 && !StartsWithMz(s)) continue;
                byte[] cd = ZipDirectory.ReadAt(s, start, checked((int)(limit - start)));
                var records = ZipDirectory.ParseCentralPrefix(cd, out int consumed);
                if (records.Count == 0) continue;
                var list = new CentralList { Records = records, Start = start, Delta = delta };
                long first = records.Select(r => list.Position(source, r)).Where(p => p >= 0).DefaultIfEmpty(-1).Min();
                if (first > 0 && NearLocalSignatureBefore(s, first)) continue;
                int agree = (records.Count == total ? 1 : 0) + (consumed == size ? 1 : 0) + (start == fieldPos ? 1 : 0);
                // 기록 하나의 이름·추가 필드·주석 길이 칸이 커져 뒤 기록들을 삼키고도 끝 머리에 정확히 닿는 모양(그 칸 안에 목록 서명이 있음 — 항목 수 칸만 다름)은 믿지 않는다
                bool swallowed = records.Any(r => r.Raw.AsSpan(ZipDirectory.CentralFixedLength).IndexOf("PK\x01\x02"u8) >= 0);
                bool trusted = consumed == cd.Length && agree >= 2 && !swallowed;
                if (trustedOnly && !trusted) continue;
                result.Add(list);
                if (trustedOnly) return;
            }
        }

        private static long Clamp(ulong v) => (long)Math.Min(v, long.MaxValue / 4);

        private static bool StartsWithMz(Stream s) => s.Length >= 2 && ZipDirectory.ReadAt(s, 0, 2) is [(byte)'M', (byte)'Z'];

        /// <summary>
        /// <paramref name="limit"/> 앞에서 시작하는 자리 가운데 'P' 로 시작하고 로컬 머리 서명 4바이트 중 3바이트 이상 맞는 자리가 있는가
        /// (서명 4바이트는 <paramref name="limit"/> 을 넘어가도 된다 — 가리키는 자리가 진짜 머리보다 1~3바이트 뒤로 밀린 모양).
        /// </summary>
        private static bool NearLocalSignatureBefore(Stream s, long limit)
        {
            byte[] sig = { 0x50, 0x4B, 0x03, 0x04 };
            var buf = new byte[1 << 20];
            long end = Math.Min(s.Length, limit + 3);
            for (long at = 0; at < limit && at + 3 < end; at += buf.Length - 3)
            {
                int want = (int)Math.Min(buf.Length, end - at);
                s.Position = at;
                int got = 0;
                while (got < want)
                {
                    int n = s.Read(buf, got, want - got);
                    if (n <= 0) break;
                    got += n;
                }
                for (int i = 0; i + 3 < got && at + i < limit; i++)
                {
                    if (buf[i] != 0x50) continue;
                    int same = 1 + (buf[i + 1] == sig[1] ? 1 : 0) + (buf[i + 2] == sig[2] ? 1 : 0) + (buf[i + 3] == sig[3] ? 1 : 0);
                    if (same >= 3) return true;
                }
                if (got < want) break;
            }
            return false;
        }
    }

    // ───────────────────────────── 고치기 한 번 ─────────────────────────────

    /// <summary>고치기 한 번의 상태: 원본, 암호 후보, 넣을 항목·못 살린 것·조각 수, 암호 맞춤 횟수.</summary>
    private sealed class Run
    {
        private readonly Source _source;
        private readonly Stream _src;
        private readonly long _len;
        private readonly Scanner _scan;
        private readonly string _fileName;
        private readonly IProgress<ArchiveProgress>? _progress;
        private readonly CancellationToken _ct;

        public Run(Source source, string srcPath, List<byte[]> candidates, IProgress<ArchiveProgress>? progress, CancellationToken ct)
        {
            _source = source;
            _src = source.Stream;
            _len = _src.Length;
            _scan = new Scanner(_src);
            _fileName = Path.GetFileName(srcPath);
            Candidates = candidates;
            _progress = progress;
            _ct = ct;
        }

        public List<byte[]> Candidates { get; }
        public List<Kept> Kept { get; } = new();
        public List<RepairLoss> Lost { get; } = new();
        public int OriginalCount { get; private set; }
        public int Pieces { get; private set; }
        public int EncryptedSeen { get; private set; }
        public int PasswordMatched { get; private set; }
        public bool AnyMismatch { get; private set; }

        private void Report(long at, string? current = null) =>
            _progress?.Report(new ArchiveProgress { CurrentFile = current ?? _fileName, BytesProcessed = Math.Clamp(at, 0, _len), TotalBytes = _len });

        // ───── 목록 모드 ─────

        /// <summary>목록 기록 하나와 그것이 가리키는 로컬 머리(맞을 때만).</summary>
        private sealed class Listed
        {
            public required ZipDirectory.CentralRecord R { get; init; }
            public required int Index { get; init; }
            public required byte[] NameBytes { get; init; }
            public required string Display { get; init; }
            public required bool Readable { get; init; }
            public required long Position { get; init; }
            public int Flags => ZipDirectory.U16(R.Raw, 8);
            public int Method => ZipDirectory.U16(R.Raw, 10);
            public ushort Time => ZipDirectory.U16(R.Raw, 12);
            public ushort Date => ZipDirectory.U16(R.Raw, 14);
            public uint Crc => ZipDirectory.U32(R.Raw, 16);
            public bool AllZero => Crc == 0 && R.CompressedSize == 0 && R.UncompressedSize == 0;
            /// <summary>내용이 없다고 적힌 기록(CRC·원래 크기 0 — 압축 크기는 보지 않음: 폴더를 Deflate 빈 흐름 2바이트로 쓰는 프로그램이 있다).</summary>
            public bool NoContent => Crc == 0 && R.UncompressedSize == 0;
            public byte[]? Aes => ZipArchiveService.AesBlockOf(R.Raw, ZipDirectory.CentralFixedLength + R.NameLength, R.ExtraLength);
            public byte[]? Unicode => ExtraPayload(R.Raw, ZipDirectory.CentralFixedLength + R.NameLength, R.ExtraLength, UnicodePathExtraId, 5);
            public bool IsFolder { get; set; }
            public bool FolderHeaderOk { get; set; }
            public bool CountAsLost { get; set; }   // 폴더인지 빈 파일인지 가를 수 없는 기록 — 쓰지 않고 '못 살린 파일'로 센다
            public LocalHead? Local { get; set; }
            public string? Mismatch { get; set; }
        }

        /// <summary>
        /// 목록 모드: 원래 수 N 은 폴더가 아닌 목록 기록 수(이름을 못 읽는 기록, 목록·로컬 이름이 끝 글자만 달라 폴더인지 빈 파일인지 가를 수 없는 기록 포함).
        /// 기록마다 가리키는 로컬 머리와 맞추고(이름 바이트·UTF-8 표시·0x7075),
        /// 두 증인(목록 값·로컬 값) 가운데 데이터 CRC 로 확인되는 쪽으로 살린다. 데이터는 다음 맞는 항목 자리·목록 시작을 넘지 않는다. 로컬 머리를 훑지 않는다.
        /// </summary>
        public void RepairListed(CentralList list)
        {
            var items = new List<Listed>(list.Records.Count);
            for (int k = 0; k < list.Records.Count; k++)
            {
                var r = list.Records[k];
                byte[] nameBytes = r.Raw.AsSpan(ZipDirectory.CentralFixedLength, r.NameLength).ToArray();
                bool readable = NameReadable(nameBytes, ZipDirectory.U16(r.Raw, 8));
                string display = readable ? DisplayName(nameBytes, ZipDirectory.U16(r.Raw, 8), ValidUnicode(ExtraPayload(r.Raw, ZipDirectory.CentralFixedLength + r.NameLength, r.ExtraLength, UnicodePathExtraId, 5), nameBytes))
                    : $"(이름을 읽을 수 없는 파일 — 목록 {k + 1}번째)";
                items.Add(new Listed { R = r, Index = k, NameBytes = nameBytes, Display = display, Readable = readable, Position = list.Position(_source, r) });
            }

            // 폴더 가르기와 로컬 머리 맞추기
            var used = new HashSet<long>();
            foreach (var it in items)
            {
                _ct.ThrowIfCancellationRequested();
                bool dirName = IsDirName(it.NameBytes);
                var raw = it.Position >= 0 ? ReadLocal(_src, it.Position, _len, null, allowNearSignature: true) : null;
                // 머리의 이름 길이 칸이 깨져 끝이 파일 밖으로 나가면 목록의 이름 길이로 한 번 더(이름이 같은지는 MatchLocal 이 본다)
                if (raw is null && it.Position >= 0) raw = ReadLocal(_src, it.Position, _len, it.NameBytes.Length, allowNearSignature: true);
                // 목록·로컬 이름이 어긋난 빈 기록이 폴더인지 빈 파일인지는 이름으로 가를 수 없다(목록 이름의 끝 바이트가 '/' 로 깨진 빈 파일 ↔ 로컬 이름의 끝 글자가 깨진 폴더).
                // 목록 기록의 속성 칸으로 가른다: 폴더 속성이면 폴더(쓰지 않고 세지도 않음 — cut 8: 폴더만 빠짐), 파일 속성이면 '못 살린 파일'로 센다,
                // 속성이 없으면(.NET 이 만든 ZIP 등) 끝 글자만 다를 때 '못 살린 파일'로 센다(cut 8: 하나 더 셀 수 있음).
                // I4-1 검증 지적: 예전엔 세지 않아 빈 파일이 빠진 채 'N-1개를 모두 살렸어요'.
                int kind = AttributeKind(it.R);
                // 폴더는 이름 끝이 '/'·'\' 이고 내용이 없다고 적힌 기록 — 압축 크기는 보지 않는다. Java ZipOutputStream·SharpZipLib·Python(ZIP_DEFLATED)은
                // 폴더 기록을 방식 8·압축 크기 2(빈 Deflate 흐름)로 쓴다(I4-1 2차 검증 지적: 예전엔 압축 크기까지 0 이어야 폴더로 보아 그런 폴더를
                // 원래 수 N 과 '살렸어요' 에 넣었다 — 파일 6개인데 '7개를 모두 살렸어요').
                if (dirName && it.NoContent) it.IsFolder = true;
                else if (it.NoContent && raw is not null && raw.Crc == 0 && raw.USize == 0 && !raw.USizeFull && IsDirName(raw.Name)
                         && LastByteOnlyDiffers(raw.Name, it.NameBytes))
                {
                    // 목록 이름의 끝 글자만 깨진 폴더이거나, 로컬 이름의 끝 글자가 '/' 로 깨진 빈 파일 — 쓰지 않는다
                    it.IsFolder = true;
                    it.Mismatch = "이름 정보가 서로 달라요";
                    it.CountAsLost = kind != FolderAttr;
                    continue;
                }
                if (it.IsFolder)
                {
                    if (raw is not null && !SameName(raw.Name, it.NameBytes))
                    {
                        it.Mismatch = "이름 정보가 서로 달라요";
                        it.CountAsLost = kind == FileAttr || kind == UnknownAttr && LastByteOnlyDiffers(raw.Name, it.NameBytes);
                    }
                    it.FolderHeaderOk = raw is not null && it.Mismatch is null && used.Add(it.Position);
                    continue;
                }
                it.Local = MatchLocal(it, raw, out string? why);
                if (it.Local is not null && !used.Add(it.Position))
                {
                    it.Local = null;
                    why = "목록 기록이 다른 파일과 같은 자리를 가리켜요";
                }
                it.Mismatch = why;
            }

            // 데이터가 넘지 않을 경계: 머리가 맞는 항목(폴더 포함)의 자리와 목록 시작
            var bounds = items.Where(i => i.Local is not null || i.FolderHeaderOk).Select(i => i.Position).Append(list.Start).Distinct().OrderBy(p => p).ToArray();
            var listed = items.Where(i => i.Position >= 0).Select(i => i.Position).Append(list.Start).ToHashSet();

            foreach (var it in items)
            {
                _ct.ThrowIfCancellationRequested();
                Report(Math.Max(it.Position, 0), it.Display);
                if (it.IsFolder)
                {
                    if (it.CountAsLost)
                    {
                        OriginalCount++;
                        Lost.Add(new RepairLoss(it.Display, it.Mismatch));
                    }
                    else if (it.Mismatch is null && it.Readable) Kept.Add(FolderFrom(it));
                    continue;
                }
                OriginalCount++;
                if (it.Local is not { } h)
                {
                    Lost.Add(new RepairLoss(it.Display, it.Mismatch ?? "파일 머리를 찾지 못했어요"));
                    continue;
                }
                int i = Array.BinarySearch(bounds, it.Position);
                long limit = i >= 0 && i + 1 < bounds.Length ? bounds[i + 1] : _len;
                int before = Kept.Count;
                ResolveListed(it, h, limit, p => listed.Contains(p));
                if (Kept.Count > before && IsDirName(it.NameBytes)) KeepFolderNamed(it);
            }
        }

        /// <summary>
        /// 이름 끝이 '/'·'\' 인데 목록 값으로는 폴더가 아니던 기록을 살린 뒤: 확인한 내용이 비어 있으면(목록의 CRC·크기 칸이 깨진 폴더) 폴더로 바꿔 넣고
        /// 원래 수에서 뺀다. 내용이 있으면 '못 살린 파일' — 그 이름 그대로 쓰면 일집·7-Zip 이 폴더로 봐서 내용이 말없이 사라진다(I4-1 2차 검증 지적).
        /// </summary>
        private void KeepFolderNamed(Listed it)
        {
            var k = Kept[^1];
            Kept.RemoveAt(Kept.Count - 1);
            if (k.Verified && k.Crc == 0 && k.USize == 0)
            {
                OriginalCount--;
                if (it.Readable) Kept.Add(FolderFrom(it));
                return;
            }
            Lost.Add(new RepairLoss(it.Display, "이름이 깨졌어요"));
        }

        /// <summary>
        /// 기록이 가리키는 자리의 로컬 머리가 같은 항목인가 — 서명(한 바이트만 다른 것도), 이름 바이트('\'·'/' 같게, 머리의 이름 길이 칸이 다르면 목록의 이름 길이로 한 번 더),
        /// UTF-8 표시(bit 11), 유니코드 이름(0x7075: 둘 다 있으면 같은 바이트, 하나만 있으면 그 CRC 가 이름 바이트와 맞아야). 아니면 null 과 이유.
        /// </summary>
        private LocalHead? MatchLocal(Listed it, LocalHead? h, out string? why)
        {
            why = "파일 머리를 찾지 못했어요";
            if (it.Position < 0 || h is null) return null;
            if (!SameName(h.Name, it.NameBytes))
            {
                var again = h.Name.Length != it.NameBytes.Length ? ReadLocal(_src, it.Position, _len, it.NameBytes.Length, allowNearSignature: true) : null;
                if (again is null || !SameName(again.Name, it.NameBytes))
                {
                    why = "이름 정보가 서로 달라요";
                    return null;
                }
                h = again;
            }
            why = "이름 정보가 서로 달라요";
            if ((h.Flags & Utf8Flag) != (it.Flags & Utf8Flag)) return null;
            byte[]? cu = it.Unicode, lu = h.Unicode;
            if (cu is not null && lu is not null ? !cu.AsSpan().SequenceEqual(lu)
                : (cu ?? lu) is { } one && ZipDirectory.U32(one, 1) != ArchiveSniffer.Crc32Of(it.NameBytes)) return null;
            why = null;
            return h;
        }

        /// <summary>목록 모드 파일 하나: 두 증인(목록·로컬) 값으로 확인해 살리거나, 둘이 모두 같으면 확인 못 함으로 옮기거나, 못 살린 것으로 적는다.</summary>
        private void ResolveListed(Listed it, LocalHead h, long limit, Func<long, bool> listedAt)
        {
            var cd = new Values(it.Flags, it.Method, it.Crc, it.R.CompressedSize, it.R.UncompressedSize, it.Aes ?? h.Aes, h.Time);
            var cands = new List<Values> { cd };
            void Add(Values v)
            {
                if (!cands.Any(c => c.Same(v) && c.Time == v.Time)) cands.Add(v);
            }
            if (it.Aes is not null && h.Aes is not null) Add(cd with { Aes = h.Aes });
            if (!h.Bit3 && !h.CSizeFull && !h.USizeFull)
            {
                var local = new Values(h.Flags, h.Method, h.Crc, h.CSize, h.USize, h.Aes ?? it.Aes, h.Time);
                Add(local);
                if (it.Aes is not null && h.Aes is not null) Add(local with { Aes = it.Aes });
            }

            bool bit3 = h.Bit3 || (it.Flags & 0x08) != 0;
            // ZipCrypto + 서술자 방식은 암호 확인 바이트가 시각 칸에서 온다 — 로컬 시각이 깨졌으면 목록의 시각도 증인으로(쓸 때 확인한 시각으로 씀)
            if (bit3 && it.Time != h.Time)
                foreach (var c in cands.Where(c => c.Encrypted && !c.IsAes).ToList()) Add(c with { Time = it.Time });
            bool encrypted = cands.Any(c => c.Encrypted);
            if (encrypted && Candidates.Count > 0) EncryptedSeen++;
            string? reason = null;
            bool cannot = false, mismatch = false;
            foreach (var c in cands)
            {
                if (!AllowedMethod(c.Method)) { reason ??= "알 수 없는 압축 방식이에요"; continue; }
                if (c.CSize < 0 || c.USize < 0) continue;
                long dataEnd = h.DataStart + c.CSize;
                if (dataEnd > _len) { reason ??= "끝이 잘렸어요"; continue; }
                if (dataEnd > limit) { reason ??= "데이터가 다음 파일과 겹쳐요"; continue; }
                if (c.CSize == 0)
                {
                    // 크기 0 은 두 증인이 모두 0 이고 데이터(+서술자) 끝이 다음 목록 항목·목록 시작에 정확히 닿을 때만(목록 칸이 0 으로 깨져 빈 파일로 '확인'되지 않게)
                    // 암호 항목은 빈 파일도 암호 머리(12바이트 이상)가 있으니 크기 0 은 깨진 것
                    if (c.Crc == 0 && c.USize == 0 && !c.Encrypted && it.AllZero && ZeroWitness(h, listedAt))
                    {
                        Kept.Add(FileFrom(it, h, c, verified: true, timeCheck: false));
                        return;
                    }
                    continue;
                }
                switch (Verify(c, h.DataStart, bit3, out _, out bool timeCheck))
                {
                    case Verdict.Ok:
                        if (c.Encrypted) PasswordMatched++;
                        Kept.Add(FileFrom(it, h, c, verified: true, timeCheck));
                        return;
                    case Verdict.CannotCheck:
                        cannot = true;
                        break;
                    case Verdict.PasswordMismatch:
                        mismatch = true;
                        reason ??= PasswordMismatchReason;
                        break;
                    default:
                        reason ??= "내용이 원래와 달라요";
                        break;
                }
            }
            if (cannot)
            {
                if (WitnessesAgree(it, h, limit))
                {
                    // 확인하지 못했으니 서술자 방식(bit 3)은 두 증인이 적은 그대로 — 암호를 넣으면 같은 확인 바이트로 풀린다
                    Kept.Add(FileFrom(it, h, cd, verified: false, timeCheck: h.Bit3));
                    return;
                }
                reason ??= "목록과 파일 머리의 정보가 서로 달라 확인하지 못했어요";
            }
            if (mismatch) AnyMismatch = true;
            Lost.Add(new RepairLoss(it.Display, reason ?? "내용이 원래와 달라요"));
        }

        /// <summary>크기 0 의 두 번째 증인: 로컬 머리(서술자 방식이면 그 자리의 서술자)도 CRC·크기 0 이고 그 끝이 목록 항목 자리·목록 시작에 정확히 닿는가.</summary>
        private bool ZeroWitness(LocalHead h, Func<long, bool> listedAt)
        {
            if (!h.Bit3) return h.AllZero && listedAt(h.DataStart);
            if (h.DataStart + 12 > _len) return false;
            byte[] d = ZipDirectory.ReadAt(_src, h.DataStart, (int)Math.Min(24, _len - h.DataStart));
            bool signed = ZipDirectory.U32(d, 0) == DescriptorSig;
            foreach (int n in signed ? new[] { 16, 24 } : new[] { 12, 20 })
            {
                if (n > d.Length) continue;
                if (d.AsSpan(signed ? 4 : 0, n - (signed ? 4 : 0)).IndexOfAnyExcept((byte)0) < 0 && listedAt(h.DataStart + n)) return true;
            }
            return false;
        }

        /// <summary>
        /// 확인할 수 없는 항목(암호를 안 넣음·LZMA 등)을 그대로 옮겨도 되는가 — 목록과 로컬이 방식·플래그·CRC·크기(서술자면 서술자 값)·AES 정보까지 같고,
        /// 데이터가 다음 항목 자리를 넘지 않을 때만.
        /// </summary>
        private bool WitnessesAgree(Listed it, LocalHead h, long limit)
        {
            long csize = it.R.CompressedSize, usize = it.R.UncompressedSize;
            if (it.Method != h.Method || it.Flags != h.Flags || !SameBytes(it.Aes, h.Aes) || !AllowedMethod(it.Method)) return false;
            if (it.Method == 99 && (it.Aes is null || !ZipArchiveService.PlausibleAes(it.Aes))) return false;
            if (csize <= 0 || h.DataStart + csize > Math.Min(limit, _len)) return false;
            if (!h.Bit3) return !h.CSizeFull && !h.USizeFull && h.Crc == it.Crc && h.CSize == csize && h.USize == usize;
            return DescriptorMatches(h.DataStart + csize, it.Crc, csize, usize, limit);
        }

        /// <summary><paramref name="at"/> 에 이 값들의 데이터 서술자(서명 있음·없음 × 4·8바이트 크기)가 있고 <paramref name="limit"/> 안에 드는가.</summary>
        private bool DescriptorMatches(long at, uint crc, long csize, long usize, long limit)
        {
            if (at + 12 > _len) return false;
            byte[] b = ZipDirectory.ReadAt(_src, at, (int)Math.Min(24, _len - at));
            bool Fits(int off, bool wide, int total) =>
                at + total <= limit && total <= b.Length && ZipDirectory.U32(b, off) == crc
                && (wide ? (long)ZipDirectory.U64(b, off + 4) == csize && (long)ZipDirectory.U64(b, off + 12) == usize
                         : ZipDirectory.U32(b, off + 4) == csize && ZipDirectory.U32(b, off + 8) == usize);
            bool signed = ZipDirectory.U32(b, 0) == DescriptorSig;
            return signed && (Fits(4, false, 16) || Fits(4, true, 24)) || Fits(0, false, 12) || Fits(0, true, 20);
        }

        /// <param name="timeCheck">ZipCrypto 의 암호 확인 바이트가 시각에서 왔는가(그러면 bit 3·서술자를 남겨야 다른 프로그램도 같은 바이트로 확인한다).</param>
        private Kept FileFrom(Listed it, LocalHead h, Values v, bool verified, bool timeCheck)
        {
            // 목록 쪽 0x7075 를 먼저(보고 이름 it.Display 도 그것으로 지었음) — 목록에 없을 때만 로컬 것을 쓰고 보고 이름도 그것으로 다시 짓는다
            byte[]? listedUnicode = ValidUnicode(it.Unicode, it.NameBytes);
            byte[]? unicode = listedUnicode ?? (it.Unicode is null ? ValidUnicode(h.Unicode, it.NameBytes) : null);
            string name = unicode is not null && listedUnicode is null && it.Readable ? DisplayName(it.NameBytes, it.Flags, unicode) : it.Display;
            int flags = KeptBits(v.Flags) | NameFlag(it.NameBytes, it.Flags, unicode);
            bool keepDesc = v.Encrypted && !v.IsAes && timeCheck;
            bool cdTime = v.Time != h.Time;   // 목록의 시각으로 확인됨
            var (madeBy, attr) = Attributes(it.R, isDirectory: false);
            return new Kept
            {
                Name = name, NameBytes = it.NameBytes, IsDirectory = false, Verified = verified,
                DataStart = h.DataStart, Flags = keepDesc ? flags | 0x08 : flags, Method = v.Method,
                Time = cdTime ? it.Time : h.Time, Date = cdTime ? it.Date : h.Date,
                Crc = v.Crc, CSize = v.CSize, USize = v.USize, Aes = v.IsAes ? v.Aes : null,
                Unicode = unicode, VersionNeeded = NeededVersion(v),
                MadeBy = madeBy, ExternalAttr = attr, InternalAttr = ZipDirectory.U16(it.R.Raw, 36), KeepDescriptor = keepDesc,
            };
        }

        private Kept FolderFrom(Listed it)
        {
            var (madeBy, attr) = Attributes(it.R, isDirectory: true);
            var raw = it.Position >= 0 ? ReadLocal(_src, it.Position, _len, null, allowNearSignature: false) : null;
            return new Kept
            {
                Name = it.Display, NameBytes = it.NameBytes, IsDirectory = true, Verified = true,
                Flags = NameFlag(it.NameBytes, it.Flags, ValidUnicode(it.Unicode, it.NameBytes)),
                Time = raw?.Time ?? it.Time, Date = raw?.Date ?? it.Date,
                Unicode = ValidUnicode(it.Unicode, it.NameBytes), MadeBy = madeBy, ExternalAttr = attr, InternalAttr = ZipDirectory.U16(it.R.Raw, 36),
            };
        }

        private const int FolderAttr = 1, FileAttr = -1, UnknownAttr = 0;

        /// <summary>
        /// 목록 기록의 외부 속성이 말하는 종류: 폴더(DOS 0x10·유닉스 폴더 모드), 파일(DOS 보관 0x20·유닉스 일반 파일 모드), 모름(속성 0 — .NET·탐색기 압축 등).
        /// </summary>
        private static int AttributeKind(ZipDirectory.CentralRecord r)
        {
            uint attr = ZipDirectory.U32(r.Raw, 38);
            uint mode = (attr >> 16) & 0xF000;
            if ((attr & 0x10) != 0 || mode == 0x4000) return FolderAttr;
            if ((attr & 0x20) != 0 || mode == 0x8000) return FileAttr;
            return UnknownAttr;
        }

        /// <summary>목록 기록의 만든 프로그램·외부 속성 — 폴더 비트가 이름과 어긋나면 DOS 속성(파일 0x20·폴더 0x10)으로.</summary>
        private static (int MadeBy, uint Attr) Attributes(ZipDirectory.CentralRecord r, bool isDirectory)
        {
            int madeBy = ZipDirectory.U16(r.Raw, 4);
            uint attr = ZipDirectory.U32(r.Raw, 38);
            bool unix = madeBy >> 8 == 3;
            bool dirAttr = (attr & 0x10) != 0 || unix && ((attr >> 16) & 0xF000) == 0x4000;
            // 폴더 표시가 이름과 다르거나 '볼륨 이름'(0x08 — 파일·폴더에는 없음) 표시가 켜졌으면 깨진 칸 — 기본값으로(그대로 두면 SharpZipLib 등이 그 파일을 파일로 보지 않고 건너뜀)
            if (isDirectory != dirAttr || (attr & 0x08) != 0) return (20, isDirectory ? 0x10u : 0x20u);
            return (madeBy, attr);
        }

        // ───── 훑기 모드 ─────

        /// <summary>
        /// 훑기 모드: 파일 맨 앞(나눠진 ZIP 이면 조각 표시 4바이트 뒤)이 말이 되는 로컬 머리일 때만 앞 사슬을 시작하고, 끝을 믿을 수 있는 동안만 따라간다.
        /// 멈춘 뒤 다시 찾은 머리는 읽히는 목록 기록(<paramref name="partial"/>)이 그 자리·이름을 가리킬 때만 사슬로 따라가고, 나머지 말이 되는 머리는 수(K)만 센다.
        /// </summary>
        public void ScanChains(List<CentralList> partial)
        {
            var pointers = new Dictionary<long, List<byte[]>>();
            foreach (var l in partial)
                foreach (var r in l.Records)
                {
                    long p = l.Position(_source, r);
                    if (p < 0) continue;
                    if (!pointers.TryGetValue(p, out var names)) pointers[p] = names = new List<byte[]>();
                    names.Add(r.Raw.AsSpan(ZipDirectory.CentralFixedLength, r.NameLength).ToArray());
                }

            long anchor = _len >= 4 && SignatureAt(_src, 0, _len) is ZipDirectory.SpanSig or ZipDirectory.TempSpanSig ? 4 : 0;
            var first = Plausible(_src, anchor, _len);
            if (first is not null && LooksListEncrypted(first.Flags)) throw new ArchiveEditException(ListEncryptedMessage);
            long q = first is not null ? Follow(anchor) : anchor;
            while (true)
            {
                q = _scan.FindNext(q, _len, LocalSig, _ct);
                if (q < 0) break;
                Report(q);
                var h = Plausible(_src, q, _len);
                if (h is not null && pointers.TryGetValue(q, out var names) && names.Any(n => SameName(n, h.Name)))
                {
                    q = Follow(q);
                    continue;
                }
                if (h is not null) Pieces++;
                q += 4;
            }
        }

        /// <summary>사슬 하나를 따라가고, 멈추면 다시 훑기 시작할 자리를 돌려준다.</summary>
        private long Follow(long p)
        {
            while (true)
            {
                _ct.ThrowIfCancellationRequested();
                Report(p);
                if (p >= _len) return _len;
                var h = Plausible(_src, p, _len);
                if (h is null)
                {
                    // 사슬이 목록·끝 머리에 닿음(정상 끝), 또는 읽을 수 없는 머리 — 그 머리는 조각 하나로 센다
                    if (SignatureAt(_src, p, _len) == LocalSig)
                    {
                        Pieces++;
                        return p + 4;
                    }
                    return p;
                }
                long next = ScanItem(h, out long resume);
                if (next < 0) return resume;
                p = next;
            }
        }

        /// <summary>
        /// 훑기 모드의 항목 하나. 끝을 믿을 수 있으면 넣고(또는 이름이 깨졌으면 못 살린 것으로 적고) 다음 머리 자리를, 아니면 못 살린 것으로 적고 -1 과
        /// 다시 훑기 시작할 자리(<paramref name="resume"/>)를 돌려준다. 끝을 믿는 조건(I4-1 ④): (1) 확인됨 + 데이터 1바이트 이상 + 끝 바로 뒤가 서명·파일 끝
        /// (Deflate 는 압축 바이트를 거의 다 썼는지도), (2) 서술자 방식 + 서술자가 맞고 그 뒤가 서명·파일 끝, (3) 머리 CRC·크기 0 + 바로 뒤 서명·파일 끝
        /// (이름 끝 '/' 면 폴더, 압축·문서 이름이면 믿지 않음), (4) 확인 못 함(암호이거나 방식이 0 이 아님) + 끝 뒤 서명·파일 끝.
        /// 끝 뒤가 서명이 아니어도(뒤 머리가 깨짐) 검사값이 압축 바이트를 모두 덮어 끝이 정확히 확인되면(저장·Deflate 다 씀·AES·서술자 크기 맞음 + CRC) 살리되 사슬은 거기서 멈춘다.
        /// 서명 앞부분(1~3바이트)에서 파일이 끝나도 경계로 본다(받다 만 파일).
        /// </summary>
        private long ScanItem(LocalHead h, out long resume)
        {
            resume = h.DataStart;
            string name = ScanName(h, out bool nameOk, out byte[]? unicode);
            bool dirName = IsDirName(h.Name);
            // 목록까지 암호(bit 13)는 맨 앞 머리에서만 알린다(ScanChains) — 사슬 가운데 머리의 그 표시는 머리가 깨진 것
            if ((h.Flags & 0x2000) != 0) return LoseScan(name, "파일 머리가 깨졌어요");

            // 데이터 시작 자리(머리 + 이름 길이 + 추가 필드 길이)를 믿을 수 있는가 — 이름이 깨졌으면 이름 길이 칸이, 추가 필드가 블록으로 나뉘지 않으면
            // 추가 필드 길이 칸이 깨졌을 수 있다. 그런 항목은 데이터 1바이트 이상을 검사값으로 확인했을 때만 끝을 믿는다(데이터가 없는 폴더·빈 파일,
            // 확인 못 한 항목은 사슬을 멈춤). I4-1 검증 지적: 빈 파일 머리의 이름 길이 칸이 커져 데이터 시작이 뒤 저장 문서 속 부품 머리에 정확히 닿자
            // '크기 0 + 바로 뒤 서명'으로 끝을 믿고 사슬이 문서 안으로 들어가 안쪽 부품(word/document.xml)을 바깥 파일로 살렸다.
            bool startTrusted = nameOk && h.ExtraWhole;
            string unanchored = nameOk ? "파일 머리가 깨졌어요" : "이름이 깨졌어요";
            if (!startTrusted) resume = h.Start + 4;   // 다시 훑기는 이 머리 서명 바로 뒤부터(깨진 이름·추가 필드 안에 든 머리도 조각으로 세게)

            if (dirName && h.AllZero)
            {
                if (!startTrusted) return LoseUnanchored(h, name, unanchored, out resume);
                // 폴더(데이터 없음): 바로 뒤가 서명·파일 끝이면 넣고 이어 간다. 서술자 방식이면 빈 서술자까지(아래). 뒤가 깨졌으면 폴더만 넣고
                // 그 뒤는 믿지 않는다 — 폴더를 '못 살린 파일'로 세지 않는다.
                if (AtBoundary(h.DataStart))
                {
                    if (nameOk) Kept.Add(ScanFolder(h, name, unicode));
                    return h.DataStart;
                }
                if (!h.Bit3)
                {
                    if (nameOk) Kept.Add(ScanFolder(h, name, unicode));
                    return -1;
                }
            }
            if (!AllowedMethod(h.Method)) return LoseScan(name, "알 수 없는 압축 방식이에요");

            if (h.AllZero && !h.Bit3 && AtBoundary(h.DataStart))
            {
                if (!startTrusted) return LoseUnanchored(h, name, unanchored, out resume);
                // 압축·문서 이름이거나 암호 항목(빈 파일도 암호 머리가 있음)이면 크기 칸이 0 으로 깨진 것일 수 있다
                if (IsContainerName(name) || h.Encrypted) return LoseScan(name, "크기 정보를 믿을 수 없어요");
                KeepScanned(h, name, nameOk, unicode, new Values(h.Flags, h.Method, 0, 0, 0, h.Aes, h.Time), verified: true, timeCheck: false);
                return h.DataStart;
            }

            if (h.Bit3 || h.AllZero)
            {
                bool mismatch = false;
                foreach (var d in FindDescriptor(_scan, _src, h.DataStart, _len, h.HasZip64, _ct))
                {
                    bool descBoundary = AtBoundary(d.End);
                    bool empty = d.Crc == 0 && d.CSize == 0 && d.USize == 0;
                    if (dirName && empty)
                    {
                        if (!descBoundary) continue;
                        if (!startTrusted) return LoseUnanchored(h, name, unanchored, out resume);
                        Kept.Add(ScanFolder(h, name, unicode));
                        return d.End;
                    }
                    if (!h.Bit3 && d.CSize == 0) continue;
                    if (!descBoundary && d.CSize == 0) continue;
                    if (h.Encrypted && d.CSize == 0) continue;   // 암호 항목은 빈 파일도 암호 머리가 있다
                    var v = new Values(h.Flags, h.Method, d.Crc, d.CSize, d.USize, h.Aes, h.Time);
                    bool timeCheck = false;
                    var verdict = d.CSize == 0 ? (empty ? Verdict.Ok : Verdict.Bad) : Verify(v, h.DataStart, h.Bit3, out _, out timeCheck);
                    mismatch |= verdict == Verdict.PasswordMismatch;
                    // 서술자 뒤가 서명이 아니면(뒤 머리가 깨짐) 검사값으로 확인됐을 때만 — 서술자의 크기가 데이터 길이와 맞고 CRC 가 맞아 끝이 정확하다(사슬은 거기서 멈춤)
                    if (!descBoundary && verdict != Verdict.Ok) continue;
                    if (Trusted(h, v, verdict, d.CSize))
                    {
                        CountPassword(v, verdict == Verdict.Ok, false);
                        // 데이터로 확인하지 못한 끝(빈 파일·확인 못 함)은 데이터 시작 자리를 믿을 수 있을 때만
                        if (!startTrusted && (verdict != Verdict.Ok || d.CSize == 0)) return LoseUnanchored(h, name, unanchored, out resume);
                        KeepScanned(h, name, nameOk, unicode, v, verdict == Verdict.Ok, verdict == Verdict.Ok ? timeCheck : h.Bit3);
                        return d.End;
                    }
                }
                CountPassword(new Values(h.Flags, h.Method, 0, 1, 0, h.Aes, h.Time), false, mismatch);
                if (dirName && h.AllZero)
                {
                    if (nameOk) Kept.Add(ScanFolder(h, name, unicode));   // 서술자 없는 폴더 — 폴더만 넣고 뒤는 믿지 않음
                    return -1;
                }
                return LoseScan(name, mismatch ? PasswordMismatchReason : "파일의 끝을 찾지 못했어요");
            }

            if (h.CSizeFull || h.USizeFull) return LoseScan(name, "크기 정보를 믿을 수 없어요");
            long end = h.DataStart + h.CSize;
            if (end > _len) return LoseScan(name, "끝이 잘렸어요");
            bool boundary = AtBoundary(end);
            var vals = new Values(h.Flags, h.Method, h.Crc, h.CSize, h.USize, h.Aes, h.Time);
            // 끝 뒤가 서명이 아니면(뒤 머리가 깨졌거나 크기 칸이 깨짐) 검사값이 압축 바이트를 모두 덮는 방식(저장·Deflate 는 다 썼는지까지·AES 인증값)만 확인해 본다 —
            // 확인되면 끝이 정확하므로 살리고 사슬은 거기서 멈춘다(그 뒤 머리는 다시 찾은 것으로만)
            bool exactCheck = !h.Encrypted && h.Method is 0 or 8 || vals.IsAes || h.Encrypted && h.Method == 0;
            if (!boundary && !exactCheck) return LoseScan(name, "파일의 끝이 맞지 않아요");
            if (vals.IsAes && (vals.Aes is null || !ZipArchiveService.PlausibleAes(vals.Aes)))
            {
                if (boundary) resume = end;   // 끝 뒤는 기록 경계 — AES 정보만 깨진 것으로 보고 그 뒤부터 다시 훑는다
                return LoseScan(name, "암호 정보가 깨졌어요");
            }
            long consumed = h.CSize;
            var result = h.CSize == 0 ? Verdict.Bad
                : h.Method == 8 && !h.Encrypted ? VerifyDeflateExact(h.DataStart, vals, out consumed)
                : Verify(vals, h.DataStart, false, out _, out _);
            CountPassword(vals, result == Verdict.Ok, result == Verdict.PasswordMismatch);
            // Deflate 가 압축 바이트를 다 쓰지 않고 끝났으면 머리의 압축 크기가 실제보다 크다(뒤 항목을 삼켰을 수 있음) — 끝을 믿지 않는다
            if (result == Verdict.Ok && consumed < h.CSize) return LoseScan(name, "파일의 끝이 맞지 않아요");
            // 암호 데이터(무작위로 보임)·원문을 그대로 담지 않는 방식 안에 말이 되는 머리가 있으면 압축 크기가 뒤 항목을 삼킨 것(암호 없이 옮길 때도)
            if (result is Verdict.Ok or Verdict.CannotCheck && (h.Encrypted || h.Method is 12 or 14 or 98) && HeaderInside(h.DataStart, end))
                return LoseScan(name, "파일의 끝이 맞지 않아요");
            if (!boundary && result != Verdict.Ok) return LoseScan(name, "파일의 끝이 맞지 않아요");
            if (Trusted(h, vals, result, h.CSize))
            {
                if (result != Verdict.Ok && !startTrusted) return LoseUnanchored(h, name, unanchored, out resume);   // 확인 못 한 끝 — 데이터 시작 자리를 믿을 수 있을 때만
                KeepScanned(h, name, nameOk, unicode, vals, result == Verdict.Ok, timeCheck: false);
                return end;
            }
            resume = end;   // 끝 뒤는 기록 경계 — 데이터만 깨진 것으로 보고 그 뒤부터 다시 훑는다
            return LoseScan(name, result == Verdict.PasswordMismatch ? PasswordMismatchReason : "내용이 원래와 달라요");
        }

        /// <summary>끝을 믿을 수 있는 결과인가: 확인됨(데이터 1바이트 이상 — 크기 0 은 서술자의 빈 파일만), 또는 확인 못 함이고 암호이거나 방식이 0 이 아님.</summary>
        private bool Trusted(LocalHead h, Values v, Verdict verdict, long csize)
        {
            if (verdict == Verdict.Ok) return csize > 0 || h.Bit3;
            if (verdict != Verdict.CannotCheck || !(v.Encrypted || v.Method != 0)) return false;
            return !v.IsAes || v.Aes is not null && ZipArchiveService.PlausibleAes(v.Aes);
        }

        /// <summary>
        /// 목록까지 암호(PKWARE 강한 암호의 목록 암호 — bit 13)로 보이는 머리 표시: bit 13 이 켜지고 쓰지 않는 표시(bit 7~10·12·14·15)는 꺼져 있다 —
        /// 머리 바이트가 깨져 여러 표시가 한꺼번에 켜진 것과 가른다.
        /// </summary>
        private static bool LooksListEncrypted(int flags) => (flags & 0x2000) != 0 && (flags & 0xD780) == 0;

        /// <summary>[<paramref name="from"/>, <paramref name="to"/>) 안에 말이 되는 로컬 머리가 통째로 있는가.</summary>
        private bool HeaderInside(long from, long to)
        {
            for (long q = from; ; q += 4)
            {
                q = _scan.FindNext(q, to, LocalSig, _ct);
                if (q < 0 || q + 30 > to) return false;
                if (Plausible(_src, q, _len) is { } x && x.DataStart <= to) return true;
            }
        }

        private long LoseScan(string name, string reason)
        {
            Lost.Add(new RepairLoss(name, reason));
            return -1;
        }

        /// <summary>데이터 시작 자리를 믿을 수 없는 항목: 못 살린 것으로 적고 사슬을 멈춘다 — 다시 훑기는 이 머리 서명 바로 뒤부터.</summary>
        private long LoseUnanchored(LocalHead h, string name, string reason, out long resume)
        {
            resume = h.Start + 4;
            return LoseScan(name, reason);
        }

        /// <summary>암호 항목 하나를 셈(암호를 줬을 때만): 본 수, 맞은 수, 암호 확인값이 안 맞았는지.</summary>
        private void CountPassword(Values v, bool ok, bool mismatch)
        {
            if (!v.Encrypted || Candidates.Count == 0) return;
            EncryptedSeen++;
            if (ok) PasswordMatched++;
            if (mismatch) AnyMismatch = true;
        }

        /// <param name="timeCheck">ZipCrypto 의 암호 확인 바이트가 시각에서 왔는가(그러면 bit 3·서술자를 남긴다 — 확인하지 못했으면 머리에 적힌 그대로).</param>
        private void KeepScanned(LocalHead h, string name, bool nameOk, byte[]? unicode, Values v, bool verified, bool timeCheck)
        {
            if (!nameOk)
            {
                Lost.Add(new RepairLoss(name, "이름이 깨졌어요"));
                return;
            }
            if (IsDirName(h.Name))
            {
                // 이름 끝이 '/'·'\': 내용이 없으면(CRC·원래 크기 0 — 빈 Deflate 흐름 2바이트로 쓴 폴더 포함) 폴더로 넣는다(파일 수에 넣지 않음).
                // 내용이 있으면 이름 끝 글자가 깨진 파일 — 서술자 방식이라 머리 칸이 0 이어서 이름 검사(ScanName)를 지나왔어도, 그 이름 그대로 쓰면
                // 다른 프로그램이 폴더로 봐서 내용이 사라진다(I4-1 2차 검증 지적: '5개를 살렸어요'에 넣었는데 고친 ZIP 에서는 파일 4개)
                if (v.Crc == 0 && v.USize == 0) Kept.Add(ScanFolder(h, name, unicode));
                else Lost.Add(new RepairLoss(name, "이름이 깨졌어요"));
                return;
            }
            int flags = KeptBits(v.Flags) | NameFlag(h.Name, h.Flags, unicode);
            bool keepDesc = v.Encrypted && !v.IsAes && timeCheck;
            Kept.Add(new Kept
            {
                Name = name, NameBytes = h.Name, IsDirectory = false, Verified = verified, DataStart = h.DataStart,
                Flags = keepDesc ? flags | 0x08 : flags, Method = v.Method, Time = h.Time, Date = h.Date,
                Crc = v.Crc, CSize = v.CSize, USize = v.USize, Aes = v.IsAes ? v.Aes : null, Unicode = unicode,
                VersionNeeded = NeededVersion(v), ExternalAttr = 0x20, KeepDescriptor = keepDesc,
            });
        }

        private static Kept ScanFolder(LocalHead h, string name, byte[]? unicode) => new()
        {
            Name = name, NameBytes = h.Name, IsDirectory = true, Verified = true, Flags = NameFlag(h.Name, h.Flags, unicode),
            Time = h.Time, Date = h.Date, Unicode = unicode, ExternalAttr = 0x10,
        };

        /// <summary>
        /// 훑기 모드의 이름(로컬 머리 하나뿐): 제어 문자, UTF-8 표시인데 UTF-8 이 아님, 표시가 없는데 UTF-8·CP949 로도 안 읽힘, 파일인데 끝이 '/'·'\',
        /// 로컬 0x7075 의 CRC 가 이름 바이트와 다름 — 하나라도 걸리면 '이름이 깨졌어요'. 0x7075 는 CRC 가 맞고 모양이 맞을 때만 쓴다.
        /// </summary>
        private static string ScanName(LocalHead h, out bool ok, out byte[]? unicode)
        {
            ok = NameReadable(h.Name, h.Flags);
            unicode = null;
            if (h.Unicode is { } u)
            {
                if (ZipDirectory.U32(u, 1) != ArchiveSniffer.Crc32Of(h.Name)) ok = false;
                else unicode = ValidUnicode(u, h.Name);
            }
            // 파일인데 이름 끝이 '/'·'\' — 내용이 없다고 적힌 것(CRC·원래 크기 0)만 폴더로 본다(압축 크기 2 의 빈 Deflate 폴더도 — 내용은 KeepScanned 가 다시 봄)
            if (IsDirName(h.Name) && !(h.Crc == 0 && h.USize == 0 && !h.USizeFull)) ok = false;
            return DisplayName(h.Name, h.Flags, unicode);
        }

        /// <summary><paramref name="p"/> 가 기록 경계인가 — 로컬 머리·목록·ZIP64 끝·끝 머리 서명이 있거나 파일 끝.</summary>
        private bool AtBoundary(long p) => p == _len || p >= 0 && p < _len && (SignatureAt(_src, p, _len) is LocalSig or CentralSig or Zip64EndSig or EndSig || CutSignature(p));

        /// <summary><paramref name="p"/> 부터 파일 끝까지 1~3바이트가 서명의 앞부분('P'·'PK'·'PK\x03' 등) — 받다 만 파일이 다음 기록 머리 몇 바이트에서 끊긴 모양.</summary>
        private bool CutSignature(long p)
        {
            long n = _len - p;
            if (n is < 1 or > 3) return false;
            byte[] tail = ZipDirectory.ReadAt(_src, p, (int)n);
            Span<byte> sig = stackalloc byte[4];
            foreach (uint s in new[] { LocalSig, CentralSig, Zip64EndSig, EndSig })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(sig, s);
                if (sig[..(int)n].SequenceEqual(tail)) return true;
            }
            return false;
        }

        // ───── 확인 ─────

        /// <summary>데이터를 풀어 원래 크기·CRC(암호면 암호로 풀어서, AES 는 인증값까지)를 본다. 네트워크·장치 오류는 그대로 던진다.</summary>
        /// <param name="pulled">해제기가 데이터에서 읽어 간 바이트 수(모르면 크기).</param>
        /// <param name="timeCheck">ZipCrypto 의 암호 확인 바이트가 (CRC 가 아니라) 시각 윗바이트와 맞았는가.</param>
        private Verdict Verify(Values v, long dataStart, bool bit3, out long pulled, out bool timeCheck)
        {
            pulled = v.CSize;
            timeCheck = false;
            try
            {
                if (!v.Encrypted)
                {
                    var window = new Window(_src, dataStart, v.CSize);
                    var r = VerifyPlain(window, v.Method, v.Crc, v.USize, _ct);
                    pulled = window.Pulled;
                    return r;
                }
                if ((v.Flags & 0x40) != 0 || Candidates.Count == 0) return Verdict.CannotCheck;   // PKWARE 강한 암호·암호를 모름
                return v.IsAes ? VerifyAes(v, dataStart) : VerifyZipCrypto(v, dataStart, bit3 || (v.Flags & 0x08) != 0, out timeCheck);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not (UnauthorizedAccessException or PathTooLongException) && !ErrorCatalog.IsEnvironmentProblem(ex))
            {
                return Verdict.Bad;
            }
        }

        /// <summary>
        /// 훑기 모드의 Deflate 확인: 풀어 크기·CRC 를 보고, 압축 바이트를 몇 개 썼는지(<paramref name="consumed"/>)도 센다 — 남는 바이트가 있으면 머리의 압축 크기가
        /// 실제보다 커진 것(Deflate 는 뒤의 남는 바이트를 무시해 확인을 통과하므로 끝을 믿을 수 없다).
        /// </summary>
        private Verdict VerifyDeflateExact(long dataStart, Values v, out long consumed)
        {
            consumed = v.CSize;
            try
            {
                var inflater = new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(noHeader: true);
                using var s = new ICSharpCode.SharpZipLib.Zip.Compression.Streams.InflaterInputStream(new Window(_src, dataStart, v.CSize), inflater) { IsStreamOwner = false };
                var (got, count) = Measure(s, v.USize, _ct);
                consumed = inflater.TotalIn;
                return got == v.Crc && count == v.USize && inflater.IsFinished ? Verdict.Ok : Verdict.Bad;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not (UnauthorizedAccessException or PathTooLongException) && !ErrorCatalog.IsEnvironmentProblem(ex))
            {
                return Verdict.Bad;
            }
        }

        /// <param name="timeCheck">확인 바이트가 CRC 윗바이트가 아니라 시각 윗바이트와 맞았는가 — 그러면 고친 ZIP 에도 bit 3(서술자 방식)을 남겨야
        /// 다른 프로그램이 같은 바이트(시각)로 확인한다. 둘 다 맞으면 false(bit 3 없이 CRC 로 확인됨).</param>
        private Verdict VerifyZipCrypto(Values v, long dataStart, bool bit3, out bool timeCheck)
        {
            timeCheck = false;
            if (v.Method is not (0 or 8 or 12)) return Verdict.CannotCheck;
            if (v.CSize < 12) return Verdict.Bad;
            byte[] head = ZipDirectory.ReadAt(_src, dataStart, 12);
            // 확인 바이트: CRC 윗바이트, 데이터 서술자 항목은 DOS 시각 윗바이트(만든 프로그램에 따라 둘 다 받음)
            byte byCrc = (byte)(v.Crc >> 24), byTime = (byte)(v.Time >> 8);
            bool anyPassed = false;
            foreach (var pw in Candidates)
            {
                var keys = new ZipCryptoKeys(pw);
                byte last = 0;
                foreach (byte x in head) last = keys.Decrypt(x);
                if (last != byCrc && !(bit3 && last == byTime)) continue;
                anyPassed = true;
                var payload = new ZipCryptoReader(new Window(_src, dataStart + 12, v.CSize - 12), keys);
                if (VerifyPlain(payload, v.Method, v.Crc, v.USize, _ct) == Verdict.Ok)
                {
                    timeCheck = last != byCrc;
                    return Verdict.Ok;
                }
            }
            return anyPassed ? Verdict.Bad : Verdict.PasswordMismatch;
        }

        private Verdict VerifyAes(Values v, long dataStart)
        {
            if (v.Aes is not { } a || !ZipArchiveService.PlausibleAes(a)) return Verdict.Bad;
            var e = new ZipCentralEntry(0, string.Empty, v.Flags, v.Method, v.Time, v.Crc, v.CSize, v.USize, 0,
                ZipDirectory.U16(a, 0), a[4], ZipDirectory.U16(a, 5));
            if (!WinZipAes.CanDecompress(e)) return Verdict.CannotCheck;
            bool anyPassed = false;
            foreach (var pw in Candidates)
            {
                Stream s;
                try
                {
                    // 원본(이어 붙인 조각일 수 있음)에서 — 닫지 않는 겉으로 싸서. 데이터 시작은 읽은 머리대로
                    s = WinZipAes.OpenEntry(new KeepOpenStream(_src), e, pw, dataStart);
                }
                catch (WrongPasswordException)
                {
                    continue;
                }
                anyPassed = true;
                using (s)
                {
                    var (got, count) = Measure(s, v.USize, _ct);
                    // AE-2 는 CRC 칸이 0(무결성은 인증값 HMAC — 끝까지 읽을 때 확인됨). AE-2 인데 CRC 칸이 0 이 아니면 그 값도 맞아야 한다
                    bool crcOk = e.AesVersion == 2 && v.Crc == 0 || got == v.Crc;
                    if (crcOk && count == v.USize) return Verdict.Ok;
                }
            }
            return anyPassed ? Verdict.Bad : Verdict.PasswordMismatch;
        }
    }

    // ───────────────────────────── 쓰기 ─────────────────────────────

    /// <summary>새 ZIP 쓰기: 확인한 값으로 로컬 머리·목록 기록을 새로 쓰고 압축 데이터만 그대로 옮긴다.</summary>
    private static class RepairWriter
    {
        public static void Write(Stream src, string destFull, List<Kept> items, CancellationToken ct)
        {
            string dir = Path.GetDirectoryName(destFull) ?? throw new IOException("저장할 폴더를 알 수 없어요: " + destFull);
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, $".~{Path.GetFileName(destFull)}.{Guid.NewGuid().ToString("N")[..8]}.tmp");
            try
            {
                using (var outFs = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
                {
                    File.SetAttributes(tmp, File.GetAttributes(tmp) | FileAttributes.Hidden);
                    var writer = new ZipRawRewriter.Writer(outFs, ct, null);
                    foreach (var k in items)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = new ZipSourceEntry { Cd = Central(k), NameBytes = k.NameBytes, Key = k.Name, IsDirectory = k.IsDirectory };
                        writer.CopyEntryWithHeader(src, entry, LocalHeader(k), k.DataStart, k.IsDirectory ? 0 : k.CSize, k.KeepDescriptor ? Descriptor(k) : null);
                    }
                    writer.Finish(Array.Empty<byte>());
                    outFs.Flush(true);

                    // 다 쓴 결과의 목록을 다시 읽어 본다(기록 수·이름이 맞아야)
                    var end = ZipDirectory.FindEnd(outFs) ?? throw new InvalidDataException("고친 ZIP 의 끝 머리를 찾지 못함");
                    if (end.HasZip64Locator) ZipDirectory.ApplyZip64(outFs, end, end.Zip64Offset);
                    byte[] cdBytes = ZipDirectory.ReadAt(outFs, end.CdOffset, checked((int)end.CdSize));
                    var back = ZipDirectory.ParseCentral(cdBytes, end.TotalEntries);
                    if (back.Count != items.Count) throw new InvalidDataException("고친 ZIP 의 목록 수가 맞지 않음");
                    for (int i = 0; i < back.Count; i++)
                        if (!back[i].Raw.AsSpan(ZipDirectory.CentralFixedLength, back[i].NameLength).SequenceEqual(items[i].NameBytes))
                            throw new InvalidDataException("고친 ZIP 의 이름이 맞지 않음");
                }
                File.SetAttributes(tmp, File.GetAttributes(tmp) & ~FileAttributes.Hidden);
                try
                {
                    File.Move(tmp, destFull, overwrite: true);
                }
                catch (UnauthorizedAccessException) when (InUseInstead(destFull) is { } inUse)
                {
                    // 저장 위치의 파일을 다른 프로그램이 열어 두면 바꿔 넣기가 '권한 없음'(0x80070005)으로 실패한다 — 원인은 '다른 프로그램이 씀'
                    throw inUse;
                }
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* 정리 실패는 무시 */ }
                throw;
            }
        }

        private static bool Zip64(Kept k) => k.CSize >= uint.MaxValue || k.USize >= uint.MaxValue;

        /// <summary>온전한 추가 필드 블록만: 확인한(또는 두 증인이 같은) AES 정보, 유효한 유니코드 이름.</summary>
        private static byte[] Extra(Kept k)
        {
            using var ms = new MemoryStream();
            if (k.Aes is { } a) Block(ms, AesExtraId, a);
            if (k.Unicode is { } u) Block(ms, UnicodePathExtraId, u);
            return ms.ToArray();
        }

        private static void Block(MemoryStream ms, int id, byte[] payload)
        {
            ms.Write(BitConverter.GetBytes((ushort)id));
            ms.Write(BitConverter.GetBytes((ushort)payload.Length));
            ms.Write(payload);
        }

        /// <summary>
        /// 새 로컬 머리 — 확인한 CRC·크기를 적는다. ZipCrypto + bit 3(서술자) 항목도 머리에 값을 적고 서술자도 쓴다(SharpZipLib 가 되감을 수 있는 곳에 쓰는 모양):
        /// 머리 값이 0 인 흘려 쓴 암호 항목은 흘려 읽는 프로그램이 데이터 끝을 몰라 못 푸는 일이 있어서(일집 풀기 엔진 포함 — I4-1 검토에서 찾음).
        /// </summary>
        private static byte[] LocalHeader(Kept k)
        {
            bool zip64 = Zip64(k);
            using var extra = new MemoryStream();
            if (zip64)
            {
                extra.Write(BitConverter.GetBytes((ushort)0x0001));
                extra.Write(BitConverter.GetBytes((ushort)16));
                extra.Write(BitConverter.GetBytes((ulong)k.USize));
                extra.Write(BitConverter.GetBytes((ulong)k.CSize));
            }
            extra.Write(Extra(k));
            byte[] ex = extra.ToArray();
            var h = new byte[ZipDirectory.LocalHeaderLength + k.NameBytes.Length + ex.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(h, LocalSig);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), (ushort)k.VersionNeeded);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), (ushort)k.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8), (ushort)k.Method);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10), k.Time);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(12), k.Date);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(14), k.Crc);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(18), zip64 ? uint.MaxValue : (uint)k.CSize);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(22), zip64 ? uint.MaxValue : (uint)k.USize);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(26), (ushort)k.NameBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(28), (ushort)ex.Length);
            k.NameBytes.CopyTo(h, ZipDirectory.LocalHeaderLength);
            ex.CopyTo(h, ZipDirectory.LocalHeaderLength + k.NameBytes.Length);
            return h;
        }

        /// <summary>ZipCrypto + bit 3 항목의 데이터 서술자(서명 있음 — 16, 크면 24바이트) — 확인한 값으로.</summary>
        private static byte[] Descriptor(Kept k)
        {
            bool wide = Zip64(k);
            var d = new byte[wide ? 24 : 16];
            BinaryPrimitives.WriteUInt32LittleEndian(d, DescriptorSig);
            BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4), k.Crc);
            if (wide)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(8), (ulong)k.CSize);
                BinaryPrimitives.WriteUInt64LittleEndian(d.AsSpan(16), (ulong)k.USize);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(8), (uint)k.CSize);
                BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(12), (uint)k.USize);
            }
            return d;
        }

        /// <summary>새 목록 기록(시각은 로컬, 속성은 목록 쪽). 4GB 넘는 크기는 칸을 가득 두고 <see cref="ZipDirectory.CentralRecord.Rewrite"/> 가 ZIP64 를 붙인다.</summary>
        private static ZipDirectory.CentralRecord Central(Kept k)
        {
            byte[] extra = Extra(k);
            var c = new byte[ZipDirectory.CentralFixedLength + k.NameBytes.Length + extra.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(c, CentralSig);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(4), (ushort)k.MadeBy);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(6), (ushort)k.VersionNeeded);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(8), (ushort)k.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(10), (ushort)k.Method);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(12), k.Time);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(14), k.Date);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(16), k.Crc);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(20), k.CSize >= uint.MaxValue ? uint.MaxValue : (uint)k.CSize);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(24), k.USize >= uint.MaxValue ? uint.MaxValue : (uint)k.USize);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(28), (ushort)k.NameBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(30), (ushort)extra.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(36), k.InternalAttr);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(38), k.ExternalAttr);
            k.NameBytes.CopyTo(c, ZipDirectory.CentralFixedLength);
            extra.CopyTo(c, ZipDirectory.CentralFixedLength + k.NameBytes.Length);
            return new ZipDirectory.CentralRecord
            {
                Raw = c,
                NameLength = k.NameBytes.Length,
                ExtraLength = extra.Length,
                CommentLength = 0,
                CompressedSize = k.CSize,
                UncompressedSize = k.USize,
                LocalOffset = 0,
                DiskStart = 0,
            };
        }

        /// <summary>그 파일을 다른 프로그램이 열어 두었으면 그 '다른 프로그램이 씀' 예외(0x80070020·0x80070021), 아니면 null.</summary>
        private static IOException? InUseInstead(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return null;
            }
            catch (IOException io) when (io.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021))
            {
                return io;
            }
            catch
            {
                return null;
            }
        }
    }

    // ───────────────────────────── 머리·이름 도우미 ─────────────────────────────

    /// <summary>
    /// <paramref name="at"/> 의 로컬 머리를 읽는다(서명이 맞아야 — <paramref name="allowNearSignature"/> 면 한 바이트만 다른 것도). 이름 길이는 머리 칸 대신
    /// <paramref name="nameLength"/> 로 줄 수 있다(목록 모드 — 같은 항목의 목록 이름 길이로 한 번 더 읽어 볼 때). 읽을 수 없으면 null.
    /// </summary>
    private static LocalHead? ReadLocal(Stream s, long at, long len, int? nameLength, bool allowNearSignature)
    {
        if (at < 0 || at + ZipDirectory.LocalHeaderLength > len) return null;
        byte[] f = ZipDirectory.ReadAt(s, at, ZipDirectory.LocalHeaderLength);
        uint sig = ZipDirectory.U32(f, 0);
        if (sig != LocalSig && !(allowNearSignature && BitDiff(sig, LocalSig) <= 1)) return null;
        int nameLen = nameLength ?? ZipDirectory.U16(f, 26), extraLen = ZipDirectory.U16(f, 28);
        long dataStart = at + ZipDirectory.LocalHeaderLength + nameLen + extraLen;
        if (dataStart > len) return null;
        byte[] name = ZipDirectory.ReadAt(s, at + ZipDirectory.LocalHeaderLength, nameLen);
        byte[] extra = extraLen > 0 ? ZipDirectory.ReadAt(s, at + ZipDirectory.LocalHeaderLength + nameLen, extraLen) : Array.Empty<byte>();
        long csize = ZipDirectory.U32(f, 18), usize = ZipDirectory.U32(f, 22);
        bool csFull = csize == uint.MaxValue, usFull = usize == uint.MaxValue, zip64 = false;
        foreach (var (id, dat, size) in ZipRawRewriter.ExtraBlocks(extra, 0, extra.Length))
        {
            if (id != 0x0001) continue;
            // ZIP64: 로컬 머리는 원래 크기, 압축 크기 차례(가득 찬 칸만)
            zip64 = true;
            int q = dat, qEnd = dat + size;
            if (usFull && q + 8 <= qEnd) { usize = (long)Math.Min(ZipDirectory.U64(extra, q), long.MaxValue / 4); usFull = false; q += 8; }
            if (csFull && q + 8 <= qEnd) { csize = (long)Math.Min(ZipDirectory.U64(extra, q), long.MaxValue / 4); csFull = false; }
            break;
        }
        return new LocalHead
        {
            Start = at,
            DataStart = dataStart,
            Version = ZipDirectory.U16(f, 4) & 0xFF,
            Flags = ZipDirectory.U16(f, 6),
            Method = ZipDirectory.U16(f, 8),
            Time = ZipDirectory.U16(f, 10),
            Date = ZipDirectory.U16(f, 12),
            Crc = ZipDirectory.U32(f, 14),
            CSize = csize,
            USize = usize,
            CSizeFull = csFull,
            USizeFull = usFull,
            Name = name,
            Aes = ExtraPayload(extra, 0, extra.Length, AesExtraId, 7) is { } a ? a[..7] : null,
            Unicode = ExtraPayload(extra, 0, extra.Length, UnicodePathExtraId, 5),
            HasZip64 = zip64,
            ExtraWhole = ExtraFillsExactly(extra),
        };
    }

    /// <summary>추가 필드가 블록(머리 4바이트 + 크기)으로 남김없이 나뉘는가(길이 0 도 참).</summary>
    private static bool ExtraFillsExactly(byte[] extra)
    {
        int x = 0;
        foreach (var (_, dat, size) in ZipRawRewriter.ExtraBlocks(extra, 0, extra.Length)) x = dat + size;
        return x == extra.Length;
    }

    /// <summary>훑기용: 서명이 정확하고 말이 되는 로컬 머리(판 63 이하, 이름 1~4096바이트, 아는 압축 방식, 데이터 시작이 파일 안). 아니면 null.</summary>
    private static LocalHead? Plausible(Stream s, long at, long len)
    {
        var h = ReadLocal(s, at, len, null, allowNearSignature: false);
        return h is not null && h.Version <= 63 && h.Name.Length is >= 1 and <= 4096 && IsKnownMethod(h.Method) ? h : null;
    }

    /// <summary>추가 필드에서 <paramref name="id"/> 블록(크기 <paramref name="minSize"/> 이상)의 내용 — 블록들이 어긋나기 전까지만 본다. 없으면 null.</summary>
    private static byte[]? ExtraPayload(byte[] b, int at, int length, int id, int minSize)
    {
        foreach (var (bid, dat, size) in ZipRawRewriter.ExtraBlocks(b, at, length))
            if (bid == id && size >= minSize) return b.AsSpan(dat, size).ToArray();
        return null;
    }

    /// <summary>
    /// 유니코드 이름(0x7075 내용)이 이름 바이트와 CRC 가 맞고 모양(폴더/파일·제어 문자 없음)도 맞고, 글자도 이름 바이트와 맞으면 그 내용, 아니면 null(쓰지 않음).
    /// CRC 는 이름 바이트만 덮으므로 0x7075 의 글자가 깨져도(예: '견적서'→'견적쐜') 걸러지지 않는다 — 그래서 이름 바이트를 만든 코드 페이지로 다시 적어 맞춰 본다(<see cref="SpelledAsName"/>).
    /// </summary>
    private static byte[]? ValidUnicode(byte[]? u, byte[] nameBytes)
    {
        if (u is null || u.Length < 6 || ZipDirectory.U32(u, 1) != ArchiveSniffer.Crc32Of(nameBytes)) return null;
        string name;
        try
        {
            name = new UTF8Encoding(false, true).GetString(u, 5, u.Length - 5);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        if (name.Any(ch => ch < 0x20 || ch == '�')) return null;
        if ((name.EndsWith('/') || name.EndsWith('\\')) != IsDirName(nameBytes)) return null;
        return SpelledAsName(name, nameBytes) ? u : null;
    }

    /// <summary>0x7075 이름과 이름 바이트를 맞춰 볼 코드 페이지 — 이름 바이트는 만든 PC 의 코드 페이지(한국어 Windows 는 949)로 적힌다. 없는 글자는 '?' 로.</summary>
    private static readonly Lazy<Encoding[]> NameCodePages = new(() =>
    {
        var list = new List<Encoding>();
        foreach (int cp in new[] { 949, 932, 936, 950, 437, 850, 866, 874, 1250, 1251, 1252, 1253, 1254, 1255, 1256, 1257, 1258 })
        {
            try { list.Add(Encoding.GetEncoding(cp, new EncoderReplacementFallback("?"), DecoderFallback.ExceptionFallback)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { }
        }
        return list.ToArray();
    });

    /// <summary>
    /// 0x7075 이름 <paramref name="name"/> 이 이름 바이트와 같은 이름인가 — 이름 바이트가 UTF-8 로 같은 글자이거나, 위 코드 페이지 하나로 글자마다 다시 적어 바이트가 같으면 참.
    /// 그 코드 페이지에 없는 글자는 만든 프로그램이 '?'·'_'·비슷한 영문자 하나(이모지 같은 두 칸 글자는 '??' 도)로 적으므로 그 자리의 영문·기호 바이트 하나를 받는다. '/'·'\' 는 같게 본다.
    /// 거짓이면 0x7075 를 쓰지 않는다(이름 바이트는 CRC 로 확인됐으니 그쪽 이름이 맞다).
    /// </summary>
    private static bool SpelledAsName(string name, byte[] nameBytes)
    {
        static string Norm(string s) => s.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        if (System.Text.Unicode.Utf8.IsValid(nameBytes) && Norm(Encoding.UTF8.GetString(nameBytes)) == Norm(name)) return true;
        foreach (var enc in NameCodePages.Value)
            if (SpelledIn(enc)) return true;
        return false;

        bool SpelledIn(Encoding enc)
        {
            int p = 0;
            for (int i = 0; i < name.Length;)
            {
                int n = char.IsSurrogatePair(name, i) ? 2 : 1;
                byte[] b = enc.GetBytes(name, i, n);
                bool missing = b.AsSpan().IndexOfAnyExcept((byte)'?') < 0 && !(n == 1 && name[i] == '?');   // 코드 페이지에 없는 글자
                // 맥에서 온 NFD 한글(조합형 낱자 U+1100~)은 반디집 등이 CP949 이름 바이트에 호환 낱자(ㅁㅐㄱ)로 적는다 — 그 바이트와 맞춰 본다
                // (I4-1 2차 검증 지적: 맞춰 보지 못해 0x7075 를 버리고 '맥_샘플' 을 'ㅁㅐㄱ_ㅅㅐㅁㅍㅡㄹ' 로 바꿔 살렸다)
                if (missing && n == 1 && CompatibilityJamo(name[i]) is { } jamo
                    && enc.GetBytes(jamo.ToString()) is var jb && jb.AsSpan().IndexOfAnyExcept((byte)'?') >= 0)
                {
                    b = jb;
                    missing = false;
                }
                i += n;
                if (missing)
                {
                    if (p >= nameBytes.Length || nameBytes[p] is < 0x20 or > 0x7E) return false;
                    p++;
                    if (n == 2 && p < nameBytes.Length && nameBytes[p - 1] == (byte)'?' && nameBytes[p] == (byte)'?') p++;
                    continue;
                }
                if (p + b.Length > nameBytes.Length) return false;
                for (int k = 0; k < b.Length; k++, p++)
                    if (b[k] != nameBytes[p] && !(b[k] is (byte)'/' or (byte)'\\' && nameBytes[p] is (byte)'/' or (byte)'\\')) return false;
            }
            return p == nameBytes.Length;
        }
    }

    /// <summary>조합형 한글 낱자(첫소리 U+1100~·가운뎃소리 U+1161~·끝소리 U+11A8~ 의 현대 낱자)에 맞는 호환 낱자(U+3131~). 아니면 null.</summary>
    private static char? CompatibilityJamo(char c)
    {
        const string lead = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        const string tail = "ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ";
        if (c is >= 'ᄀ' and <= 'ᄒ') return lead[c - 0x1100];
        if (c is >= 'ᅡ' and <= 'ᅵ') return (char)(0x314F + (c - 0x1161));
        if (c is >= 'ᆨ' and <= 'ᇂ') return tail[c - 0x11A8];
        return null;
    }

    /// <summary>보여 줄 이름: 쓸 수 있는 유니코드 이름이 있으면 그것, 아니면 이름 바이트(UTF-8 표시·올바른 UTF-8 이면 UTF-8, 아니면 한글 판별) — '\' 는 '/' 로.</summary>
    private static string DisplayName(byte[] nameBytes, int flags, byte[]? unicode)
    {
        string name = unicode is not null ? Encoding.UTF8.GetString(unicode, 5, unicode.Length - 5)
            : (flags & Utf8Flag) != 0 || System.Text.Unicode.Utf8.IsValid(nameBytes) ? Encoding.UTF8.GetString(nameBytes)
            : KoreanFileNameDecoder.DecodeBytes(nameBytes, 0, nameBytes.Length);
        return name.Replace('\\', '/');
    }

    /// <summary>
    /// 새 ZIP 의 UTF-8 표시(bit 11): 원래 켜져 있었거나, 쓸 유니코드 이름(0x7075)이 없고 이름이 한글 등(ASCII 밖)인 올바른 UTF-8 이면 켠다 —
    /// <see cref="DisplayName"/> 이 UTF-8 로 읽은 이름을 다른 프로그램도 같게 읽게(표시가 깨져 꺼진 머리에서 살린 파일이 CP949 로 읽혀 이름이 깨졌다 — I4-1 검토).
    /// </summary>
    private static int NameFlag(byte[] nameBytes, int flags, byte[]? unicode) =>
        (flags & Utf8Flag) != 0 || unicode is null && nameBytes.Any(b => b >= 0x80) && System.Text.Unicode.Utf8.IsValid(nameBytes) ? Utf8Flag : 0;

    private static readonly Lazy<Encoding> StrictCp949 = new(() => Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));

    /// <summary>이름 바이트가 글자로 읽히는가(제어 문자 없음, UTF-8 표시면 올바른 UTF-8, 아니면 UTF-8 이거나 CP949).</summary>
    private static bool NameReadable(byte[] name, int flags)
    {
        if (name.Length == 0) return false;
        foreach (byte b in name)
            if (b < 0x20 || b == 0x7F) return false;
        if (System.Text.Unicode.Utf8.IsValid(name)) return true;
        if ((flags & Utf8Flag) != 0) return false;
        try
        {
            StrictCp949.Value.GetString(name);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsDirName(byte[] name) => name.Length > 0 && name[^1] is 0x2F or 0x5C;

    /// <summary>압축·문서 형식 이름 — 훑기 모드에서 크기 0 으로 적혀 있으면 머리 칸이 0 으로 깨진 저장 문서일 수 있어 끝을 믿지 않는다(I4-1 cut 9).</summary>
    private static bool IsContainerName(string name)
    {
        string n = name.ToLowerInvariant();
        if (Regex.IsMatch(n, @"\.(z\d{2,}|zip\.\d{3,})$")) return true;
        return n.EndsWith(".zip") || n.EndsWith(".docx") || n.EndsWith(".xlsx") || n.EndsWith(".pptx") || n.EndsWith(".hwpx")
               || n.EndsWith(".odt") || n.EndsWith(".ods") || n.EndsWith(".odp") || n.EndsWith(".jar") || n.EndsWith(".apk")
               || n.EndsWith(".epub") || n.EndsWith(".7z") || n.EndsWith(".egg") || n.EndsWith(".alz");
    }

    /// <summary>고치기가 받는 압축 방식 번호(저장·Deflate·Deflate64·BZip2·LZMA·Zstd·XZ·PPMd·AES). 그 밖은 '못 살린 파일'.</summary>
    private static bool AllowedMethod(int method) => method is 0 or 8 or 9 or 12 or 14 or 93 or 95 or 98 or 99;

    /// <summary>머리로 받을 만한 방식 번호(훑기에서 말이 되는 머리를 고를 때 — 받는 것보다 넓음).</summary>
    private static bool IsKnownMethod(int method) =>
        method is 0 or 1 or 6 or 8 or 9 or 12 or 14 or 18 or 19 or 93 or 94 or 95 or 96 or 97 or 98 or 99;

    /// <summary>
    /// 새 머리의 '필요한 판' — 방식·암호·크기에서 정한다. 원래 머리의 판 칸은 옮기지 않는다(그 바이트가 깨져 29 같은 값이 되면 다른 프로그램이
    /// '이 판은 풀 수 없음'으로 거절해, 확인해 살린 파일이 고친 ZIP 에서 안 열린다 — I4-1 검토에서 찾음).
    /// </summary>
    private static int NeededVersion(Values v)
    {
        int need = v.IsAes ? 51
            : v.Method switch { 9 => 21, 12 => 46, 14 or 93 or 95 or 98 => 63, _ => 20 };
        if (v.CSize >= uint.MaxValue || v.USize >= uint.MaxValue) need = Math.Max(need, 45);
        return need;
    }

    /// <summary>
    /// 두 이름이 길이가 같고 마지막 바이트만 다른가 — 목록 이름의 끝 글자('/')만 깨진 폴더를 가를 때. 길이가 다르면 아님(머리의 이름 길이 칸이 깨져
    /// 뒤 머리의 폴더 이름까지 읽힌 빈 파일을 폴더로 잘못 세지 않게).
    /// </summary>
    private static bool LastByteOnlyDiffers(byte[] a, byte[] b) =>
        a.Length == b.Length && a.Length > 0 && a.AsSpan(0, a.Length - 1).SequenceEqual(b.AsSpan(0, b.Length - 1));

    /// <summary>두 이름 바이트가 같은가('\'·'/' 는 같게 봄).</summary>
    private static bool SameName(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            byte x = a[i] == 0x5C ? (byte)0x2F : a[i], y = b[i] == 0x5C ? (byte)0x2F : b[i];
            if (x != y) return false;
        }
        return true;
    }

    private static bool SameBytes(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);

    private static int BitDiff(uint a, uint b)
    {
        int diff = 0;
        for (int k = 0; k < 4; k++)
            if ((byte)(a >> (8 * k)) != (byte)(b >> (8 * k))) diff++;
        return diff;
    }

    /// <summary><paramref name="at"/> 의 4바이트(서명 자리) — 파일 밖이면 0.</summary>
    private static uint SignatureAt(Stream s, long at, long len) =>
        at < 0 || at + 4 > len ? 0 : ZipDirectory.U32(ZipDirectory.ReadAt(s, at, 4), 0);

    // ───────────────────────────── 확인 도우미 ─────────────────────────────

    /// <summary>
    /// 풀어 보며 CRC-32·크기를 본다(저장·Deflate·BZip2). 그 밖의 방식은 확인할 수 없다.
    /// Deflate 는 압축 흐름이 끝 표시까지 제대로 끝나야 한다 — .NET 의 Deflate 는 끝 몇 바이트가 깨진 흐름도 말없이 받아 주어(내용·CRC 는 맞음),
    /// 그런 항목을 '살렸어요'로 옮기면 다른 프로그램(SharpZipLib 등)이 '데이터가 갑자기 끝남'으로 못 여는 ZIP 이 됐다(I4-1 검토).
    /// </summary>
    private static Verdict VerifyPlain(Stream data, int method, uint crc, long usize, CancellationToken ct)
    {
        if (method == 8)
        {
            var inflater = new ICSharpCode.SharpZipLib.Zip.Compression.Inflater(noHeader: true);
            using var inflate = new ICSharpCode.SharpZipLib.Zip.Compression.Streams.InflaterInputStream(data, inflater) { IsStreamOwner = false };
            var (got, count) = Measure(inflate, usize, ct);
            return got == crc && count == usize && inflater.IsFinished ? Verdict.Ok : Verdict.Bad;
        }
        Stream? plain = method switch
        {
            0 => data,
            12 => new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(data) { IsStreamOwner = false },
            _ => null,
        };
        if (plain is null) return Verdict.CannotCheck;
        using (plain)
        {
            var (got, count) = Measure(plain, usize, ct);
            return got == crc && count == usize ? Verdict.Ok : Verdict.Bad;
        }
    }

    /// <summary>끝까지 읽으며 CRC-32 와 길이를 센다(적힌 크기보다 길게 나오면 거기서 멈춤 — 깨진 데이터가 끝없이 풀리지 않게).</summary>
    private static (uint Crc, long Count) Measure(Stream s, long expected, CancellationToken ct)
    {
        var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
        var buf = new byte[81920];
        long count = 0;
        int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            crc.Update(new ArraySegment<byte>(buf, 0, n));
            count += n;
            if (count > expected) break;
        }
        return ((uint)crc.Value, count);
    }

    /// <summary>암호 바이트 후보: UTF-8, CP949(다르면) — 반디집·알집의 한글 암호는 CP949(B3).</summary>
    private static List<byte[]> PasswordCandidates(string? password)
    {
        var list = new List<byte[]>();
        if (string.IsNullOrEmpty(password)) return list;
        list.Add(Encoding.UTF8.GetBytes(password));
        if (ZipPasswordProbe.TryCp949Bytes(password) is { } cp && !cp.AsSpan().SequenceEqual(list[0])) list.Add(cp);
        return list;
    }

    /// <summary>한 항목의 데이터 서술자 후보: 데이터 끝·서술자 길이·적힌 CRC·크기.</summary>
    private readonly record struct Bounds(long DataEnd, int DescLen, uint Crc, long CSize, long USize)
    {
        public long End => DataEnd + DescLen;
    }

    /// <summary>
    /// 데이터 서술자로 끝나는 항목의 경계 후보: 데이터 시작부터 훑어 '데이터 길이 = 서술자의 압축 크기'가 맞는 첫 자리의 모양들.
    /// 서명 있는 서술자('PK\x07\x08' + CRC + 크기 4/8바이트)이거나, 서명 없는 서술자 바로 뒤에 다음 머리·목록·끝 머리(또는 파일 끝)가 오는 모양.
    /// 한 자리에서 4바이트·8바이트 크기가 둘 다 맞으면 둘 다 — <paramref name="zip64First"/>(로컬 머리에 ZIP64 추가 필드가 있음)이면 8바이트 쪽을 먼저. 못 찾으면 빈 목록.
    /// </summary>
    private static List<Bounds> FindDescriptor(Scanner scan, Stream s, long dataStart, long len, bool zip64First, CancellationToken ct)
    {
        var found = new List<Bounds>();
        void Add(bool wide, long dataEnd, int descLen, byte[] b, int at)
        {
            if (wide) found.Add(new Bounds(dataEnd, descLen, ZipDirectory.U32(b, at), (long)Math.Min(ZipDirectory.U64(b, at + 4), long.MaxValue / 4), (long)Math.Min(ZipDirectory.U64(b, at + 12), long.MaxValue / 4)));
            else found.Add(new Bounds(dataEnd, descLen, ZipDirectory.U32(b, at), ZipDirectory.U32(b, at + 4), ZipDirectory.U32(b, at + 8)));
        }
        void Signed(long q)
        {
            if (q + 16 > len) return;
            byte[] b = ZipDirectory.ReadAt(s, q, (int)Math.Min(24, len - q));
            bool narrow = ZipDirectory.U32(b, 8) == q - dataStart;
            bool wide = b.Length >= 24 && (long)ZipDirectory.U64(b, 8) == q - dataStart;
            if (zip64First && wide) Add(true, q, 24, b, 4);
            if (narrow) Add(false, q, 16, b, 4);
            if (!zip64First && wide) Add(true, q, 24, b, 4);
        }
        void Unsigned(long q)   // 다음 기록이 q 에서 시작
        {
            bool narrow = false, wide = false;
            byte[]? nb = null, wb = null;
            if (q - 12 >= dataStart)
            {
                nb = ZipDirectory.ReadAt(s, q - 12, 12);
                narrow = ZipDirectory.U32(nb, 4) == q - 12 - dataStart;
            }
            if (q - 20 >= dataStart)
            {
                wb = ZipDirectory.ReadAt(s, q - 20, 20);
                wide = (long)ZipDirectory.U64(wb, 4) == q - 20 - dataStart;
            }
            if (zip64First && wide) Add(true, q - 20, 20, wb!, 0);
            if (narrow) Add(false, q - 12, 12, nb!, 0);
            if (!zip64First && wide) Add(true, q - 20, 20, wb!, 0);
        }

        long pos = dataStart;
        while (true)
        {
            long q = scan.Find(pos, len, "PK"u8, ct);
            if (q < 0 || q + 4 > len) break;
            pos = q + 1;
            uint sig = scan.U32At(q);
            if (sig == DescriptorSig) Signed(q);
            else if (sig is LocalSig or CentralSig or EndSig or Zip64EndSig) Unsigned(q);
            if (found.Count > 0) return found;
        }
        Unsigned(len);   // 마지막 항목 뒤에 아무것도 없을 때(목록이 통째로 없어짐)
        return found;
    }

    // ───────────────────────────── 읽기 도우미 ─────────────────────────────

    /// <summary>
    /// 서명 찾기용 읽기 창: 마지막으로 읽은 1MB 구간을 들고 있다가, 앞으로 나아가며 찾는 호출은 대부분 그 안에서 끝난다.
    /// 원본 스트림의 위치는 읽을 때마다 정해 주므로 다른 읽기와 섞여도 된다.
    /// </summary>
    private sealed class Scanner
    {
        private readonly Stream _s;
        private readonly long _len;
        private readonly byte[] _buf = new byte[1 << 20];
        private long _start;
        private int _count;   // _buf[0.._count) = 파일의 [_start, _start + _count)

        public Scanner(Stream s)
        {
            _s = s;
            _len = s.Length;
        }

        /// <summary>[from, limit) 안에서 4바이트 서명이 시작하는 첫 자리(서명이 limit 안에 다 들어야). 없으면 -1.</summary>
        public long FindNext(long from, long limit, uint sig, CancellationToken ct)
        {
            Span<byte> pattern = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(pattern, sig);
            return Find(from, limit, pattern, ct);
        }

        /// <summary>[from, limit) 안에서 <paramref name="pattern"/> 이 시작하는 첫 자리(패턴이 limit 안에 다 들어야). 없으면 -1.</summary>
        public long Find(long from, long limit, ReadOnlySpan<byte> pattern, CancellationToken ct)
        {
            int w = pattern.Length;
            limit = Math.Min(limit, _len);
            long pos = Math.Max(0, from);
            while (pos + w <= limit)
            {
                ct.ThrowIfCancellationRequested();
                if (pos < _start || pos + w > _start + _count) Load(pos);
                int off = (int)(pos - _start);
                int avail = (int)Math.Min(_count - off, limit - pos);
                if (avail < w) return -1;
                int i = _buf.AsSpan(off, avail).IndexOf(pattern);
                if (i >= 0) return pos + i;
                pos += avail - (w - 1);
            }
            return -1;
        }

        /// <summary><paramref name="at"/> 의 4바이트(파일 밖이면 0).</summary>
        public uint U32At(long at)
        {
            if (at < 0 || at + 4 > _len) return 0;
            if (at < _start || at + 4 > _start + _count) Load(at);
            return _count - (at - _start) >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(_buf.AsSpan((int)(at - _start), 4)) : 0;
        }

        private void Load(long pos)
        {
            _s.Position = pos;
            int want = (int)Math.Min(_buf.Length, _len - pos);
            int got = 0;
            while (got < want)
            {
                int n = _s.Read(_buf, got, want - got);
                if (n <= 0) break;
                got += n;
            }
            _start = pos;
            _count = got;
        }
    }

    /// <summary>원본의 한 구간만 읽는 스트림(원본 스트림을 닫지 않음, 위치는 스스로 기억).</summary>
    private sealed class Window : Stream
    {
        private readonly Stream _s;
        private readonly long _start;
        private readonly long _length;
        private long _pos;

        public Window(Stream s, long start, long length)
        {
            _s = s;
            _start = start;
            _length = Math.Max(0, length);
        }

        /// <summary>지금까지 읽어 간 바이트 수(해제기가 데이터를 어디까지 읽었는지).</summary>
        public long Pulled => _pos;

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = _length - _pos;
            if (left <= 0 || count == 0) return 0;
            _s.Position = _start + _pos;
            int n = _s.Read(buffer, offset, (int)Math.Min(count, left));
            if (n <= 0) throw new EndOfStreamException("ZIP 데이터가 중간에 끝남");
            _pos += n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>원본 스트림을 그대로 읽고 위치를 옮기되 닫지는 않는 겉(넘겨받아 닫는 <see cref="WinZipAes"/> 에 원본을 줄 때).</summary>
    private sealed class KeepOpenStream(Stream inner) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>전통 ZipCrypto 키(풀기 — 12바이트 머리 다음 데이터를 같은 키 상태로 이어 푼다).</summary>
    private sealed class ZipCryptoKeys
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private uint _k0 = 0x12345678, _k1 = 0x23456789, _k2 = 0x34567890;

        public ZipCryptoKeys(byte[] password)
        {
            foreach (byte b in password) Update(b);
        }

        public byte Decrypt(byte cipher)
        {
            uint t = (_k2 | 2) & 0xFFFF;
            byte plain = (byte)(cipher ^ (byte)((t * (t ^ 1)) >> 8));
            Update(plain);
            return plain;
        }

        private void Update(byte c)
        {
            _k0 = Table[(_k0 ^ c) & 0xFF] ^ (_k0 >> 8);
            _k1 = unchecked((_k1 + (_k0 & 0xFF)) * 134775813 + 1);
            _k2 = Table[(_k2 ^ (byte)(_k1 >> 24)) & 0xFF] ^ (_k2 >> 8);
        }
    }

    /// <summary>ZipCrypto 로 풀며 읽는 스트림.</summary>
    private sealed class ZipCryptoReader(Stream inner, ZipCryptoKeys keys) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            for (int i = 0; i < n; i++) buffer[offset + i] = keys.Decrypt(buffer[offset + i]);
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
