using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Iljip.Services;
using Microsoft.Win32.SafeHandles;

namespace Iljip.SfxStub;

/// <summary>같은 이름 파일이 있을 때의 답.</summary>
internal enum CollisionChoice
{
    Overwrite,
    Skip,
    Cancel,
}

/// <summary>풀 곳에 원래 있던 폴더 안의 같은 이름(<c>CommitLedger.Existing</c>).</summary>
internal enum ExistingEntry
{
    /// <summary>없음(또는 모름 — 옮겨 보고 가림).</summary>
    None,
    File,
    Directory,
}

/// <summary>같은 이름 파일이 있을 때 묻는 곳(무인 모드는 늘 덮어쓰기, 화면 모드는 창).</summary>
internal interface ICollisionHandler
{
    /// <param name="targetPath">이미 있는 파일.</param>
    /// <param name="incoming">새로 풀 항목.</param>
    /// <param name="remaining">이 뒤에 남은 항목 수.</param>
    CollisionChoice Resolve(string targetPath, ZipItem incoming, int remaining);
}

/// <summary>무인 모드: 같은 이름은 늘 덮어쓴다(7-Zip 자동 풀림의 -y 와 같음 — 도움말에 적음).</summary>
internal sealed class OverwriteAll : ICollisionHandler
{
    public static readonly OverwriteAll Instance = new OverwriteAll();

    public CollisionChoice Resolve(string targetPath, ZipItem incoming, int remaining) => CollisionChoice.Overwrite;
}

/// <summary>진행 상황(화면 모드의 진행 막대).</summary>
internal sealed class ExtractProgress
{
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public int FilesDone { get; set; }
    public int FilesTotal { get; set; }
    public string Current { get; set; } = string.Empty;
    /// <summary>파일을 다 풀고 제자리로 옮기는 중.</summary>
    public bool Finishing { get; set; }
    /// <summary>같은 풀 곳에 다른 자동 풀림이 마무리하는 중이라 기다림(그쪽이 끝나면 이어서 — W2-10 검증 지적 1차).</summary>
    public bool Waiting { get; set; }
}

/// <summary>풀기 결과.</summary>
internal sealed class ExtractOutcome
{
    public int Extracted { get; set; }
    public int Skipped { get; set; }
    public int Overwritten { get; set; }
    /// <summary>지난번에 강제로 끊긴 풀기의 흔적을 정리하며 원래 파일을 보이는 폴더로 옮겼으면 그 안내(아니면 null).</summary>
    public string? Note { get; set; }
}

/// <summary>
/// 자동 풀림 데이터 풀기(일집 본체의 '전부 아니면 전무' 계약과 같은 원칙 — CorruptArchiveTests):
/// <list type="number">
/// <item>암호: 아무것도 만들기 전에 확인값(AES 의 PV·ZipCrypto 의 머리 1바이트)으로 가린다 — 없으면 '암호 필요', 틀리면 '맞지 않아요'(대상 폴더도 안 만듦).</item>
/// <item>이름: 모든 항목 이름을 먼저 검사한다 — 풀 폴더 밖·드라이브·'..'·장치 이름(CON 등)·못 쓰는 글자가 하나라도 있으면 아무것도 풀지 않는다.</item>
/// <item>풀기: 대상 폴더 안 숨김 임시 폴더(<c>.일집-임시-8글자</c> — 같은 볼륨)에 다 풀며 크기·CRC-32(AES 는 인증값까지)를 맞춰 본다.
///   임시 폴더 안의 주인 표시(<see cref="SfxStaging.MarkerFileName"/>)를 끝날 때까지 열어 둔다 — 강제 종료로 남은 임시 폴더는 다음 풀기가 바로 알아보고 정리한다
///   (<see cref="SfxStaging.CleanupStale"/> — 비켜 둔 사용자 원래 파일은 지우지 않고 보이는 폴더 '덮어쓰기 전 원래 파일 (…)' 로 옮김).</item>
/// <item>옮기기: 다 맞을 때만 제자리로 옮긴다(같은 이름은 묻거나 덮어씀 — 덮어쓸 파일은 임시 폴더로 비켜 뒀다가 끝나면 지움).
///   같은 풀 곳의 옮기기·되돌리기는 한 번에 하나씩(<see cref="SfxStaging.DestinationMutexName"/> — 두 번 눌러 둘이 같은 곳에 풀 때 서로의 결과를 지우지 않게).
///   옮기다 실패하면 옮긴 것을 지우고 비켜 둔 원래 파일을 되돌린다(못 되돌린 것은 보이는 폴더로 옮기고 알림). 실패·취소면 임시 폴더와 이번에 만든 빈 폴더(대상 폴더 포함)를 지운다.</item>
/// </list>
/// </summary>
internal static class SfxExtractor
{
    /// <summary>같은 풀 곳의 다른 자동 풀림이 마무리를 끝내기를 기다리는 최대 시간(그쪽 창이 '같은 이름' 질문에 머물러 있을 수 있음).</summary>
    internal static readonly TimeSpan DestinationWaitLimit = TimeSpan.FromMinutes(10);

    private const int MoveRetries = 3;
    private const int MoveRetryDelayMs = 200;

    // ───────────── 암호 ─────────────

    /// <summary>
    /// 암호 바이트를 정한다(암호 항목이 없으면 null). 넣은 암호의 UTF-8·CP949 바이트 중 암호 항목(최대 16개)의 확인값을 모두 통과하는 것.
    /// (일집은 AES 암호를 UTF-8 로, ZipCrypto 암호는 CP949 로 쓸 수 있으면 CP949 로 쓴다 — 본체 ZipArchiveService)
    /// </summary>
    public static byte[]? ResolvePassword(SfxPayload payload, string? password, bool silent)
    {
        var encrypted = payload.Items.Where(i => !i.IsDirectory && i.IsEncrypted).ToList();
        if (encrypted.Count == 0) return null;
        foreach (var e in encrypted)
        {
            if ((e.Flags & 0x0040) != 0) throw new SfxException(SfxErrorKind.Unsupported, SfxMessages.Unsupported("강한 암호화"));
            if (e.IsAes && (e.AesStrength < 1 || e.AesStrength > 3)) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
        }
        if (string.IsNullOrEmpty(password))
            throw new SfxException(SfxErrorKind.PasswordRequired, silent ? SfxMessages.PasswordRequiredSilent : SfxMessages.PasswordRequired);

        bool anyAes = encrypted.Any(e => e.IsAes);
        var candidates = new List<byte[]>();
        void Add(byte[]? b)
        {
            if (b is not null && !candidates.Any(c => c.SequenceEqual(b))) candidates.Add(b);
        }
        byte[] utf8 = new UTF8Encoding(false).GetBytes(password);
        byte[]? cp949 = TryCp949(password!);
        if (anyAes) { Add(utf8); Add(cp949); }
        else { Add(cp949); Add(utf8); }

        var checks = encrypted.Take(16).Select(e => BuildCheck(payload, e)).ToList();
        foreach (var candidate in candidates)
            if (checks.All(c => c(candidate))) return candidate;
        throw new SfxException(SfxErrorKind.WrongPassword, SfxMessages.WrongPassword);
    }

    private static byte[]? TryCp949(string password)
    {
        try
        {
            var enc = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return enc.GetBytes(password);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<byte[], bool> BuildCheck(SfxPayload payload, ZipItem e)
    {
        long dataStart = payload.LocalDataStart(e, out ushort localTime);
        if (e.IsAes)
        {
            int saltLen = WinZipAesKeys.SaltLength(e.AesStrength);
            if (e.CompressedSize < saltLen + 2 + WinZipAesKeys.AuthCodeLength) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
            byte[] head = payload.ReadRange(dataStart, saltLen + 2);
            byte[] salt = new byte[saltLen];
            Buffer.BlockCopy(head, 0, salt, 0, saltLen);
            byte pv0 = head[saltLen], pv1 = head[saltLen + 1];
            int strength = e.AesStrength;
            return pw => WinZipAesKeys.PasswordMatches(pw, salt, pv0, pv1, strength);
        }
        if (e.CompressedSize < ZipCryptoKeys.HeaderLength) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
        byte[] h12 = payload.ReadRange(dataStart, ZipCryptoKeys.HeaderLength);
        byte[] accepted = AcceptedCheckBytes(e, localTime);
        return pw => ZipCryptoKeys.CheckHeader(pw, h12, accepted, out _);
    }

    /// <summary>ZipCrypto 머리 마지막 바이트로 받아들일 값: CRC 윗바이트, 데이터 설명자(bit 3)면 DOS 시각 윗바이트도.</summary>
    private static byte[] AcceptedCheckBytes(ZipItem e, ushort localTime)
    {
        var set = new List<byte> { (byte)(e.Crc >> 24) };
        if ((e.Flags & 0x0008) != 0)
        {
            set.Add((byte)(localTime >> 8));
            set.Add((byte)(e.DosTime >> 8));
        }
        return set.Distinct().ToArray();
    }

    // ───────────── 이름 ─────────────

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars();

    private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// 항목 이름 → 풀 폴더 기준 상대 경로('\' 구분). 안전하지 않으면 null: 비었음, '/'·'\' 로 시작(뿌리), 빈 조각('a//b'), '.'·'..',
    /// 못 쓰는 글자(':' 포함 — 드라이브·대체 데이터 스트림), 끝 점·공백(Windows 가 떼어 다른 이름이 됨), 장치 이름(CON·NUL·COM1 …).
    /// </summary>
    internal static string? SafeRelativePath(string name)
    {
        string n = (name ?? string.Empty).Replace('\\', '/');
        if (n.EndsWith("/", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 1);
        if (n.Length == 0 || n.StartsWith("/", StringComparison.Ordinal)) return null;
        string[] parts = n.Split('/');
        foreach (string part in parts)
        {
            if (part.Length == 0 || part == "." || part == "..") return null;
            if (part.IndexOfAny(InvalidNameChars) >= 0) return null;
            if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal)) return null;
            int dot = part.IndexOf('.');
            string stem = (dot >= 0 ? part.Substring(0, dot) : part).TrimEnd(' ');
            if (ReservedNames.Contains(stem)) return null;
        }
        return string.Join("\\", parts);
    }

    /// <summary>
    /// 풀 계획: (항목, 상대 경로). 안전하지 않은 이름·겹치는 이름(파일과 폴더가 같은 이름 등)이 하나라도 있으면 실패(아무것도 만들기 전).
    /// 이름은 붙은 글자(NFC)로 푼다(맥에서 온 자모 분리 이름). 단 NFC 로 바꿔서 같은 이름이 둘이 될 때 — 같은 폴더에 NFC·NFD 두 모양이 다 있던 원본
    /// (NTFS 는 둘을 다른 이름으로 둠 — 일집이 그런 폴더로 만든 exe) — 는 바꾸지 않고 압축에 적힌 그대로 푼다(원본과 같은 두 파일).
    /// 예전엔 그런 exe 를 통째로 '같은 이름이 두 번… 손상'으로 거절했다(W2-10 검증 지적 2차). 바꾸지 않아도 겹치면 그때는 손상.
    /// </summary>
    internal static List<PlannedItem> Plan(SfxPayload payload)
    {
        var plan = Plan(payload, useRawNames: false, out var duplicate);
        if (plan is not null) return plan;
        if (payload.Items.Any(i => !string.Equals(i.RawName, i.Name, StringComparison.Ordinal))
            && Plan(payload, useRawNames: true, out _) is { } raw)
            return raw;
        throw duplicate!;
    }

    /// <summary>
    /// <see cref="Plan(SfxPayload)"/> 의 한 번: 겹치는 이름이면 null 과 그 오류(<paramref name="duplicate"/>). 안전하지 않은 이름·못 푸는 방식은 바로 던진다.
    /// </summary>
    private static List<PlannedItem>? Plan(SfxPayload payload, bool useRawNames, out SfxException? duplicate)
    {
        duplicate = null;
        var plan = new List<PlannedItem>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in payload.Items)
        {
            string name = useRawNames ? item.RawName : item.Name;
            string rel = SafeRelativePath(name) ?? throw new SfxException(SfxErrorKind.UnsafePath, SfxMessages.UnsafePath(item.Name));
            if (!item.IsDirectory && item.ContentMethod != 0 && item.ContentMethod != 8)
                throw new SfxException(SfxErrorKind.Unsupported, SfxMessages.Unsupported("방식 " + item.ContentMethod));

            // 조상 폴더가 파일 이름과 겹치면 안 된다
            string[] parts = rel.Split('\\');
            string prefix = string.Empty;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                prefix = prefix.Length == 0 ? parts[i] : prefix + "\\" + parts[i];
                if (files.Contains(prefix))
                {
                    duplicate = new SfxException(SfxErrorKind.Damaged, SfxMessages.DuplicateName(prefix.Replace('\\', '/')));
                    return null;
                }
                dirs.Add(prefix);
            }
            if (item.IsDirectory ? files.Contains(rel) : files.Contains(rel) || dirs.Contains(rel))
            {
                duplicate = new SfxException(SfxErrorKind.Damaged, SfxMessages.DuplicateName(item.Name));
                return null;
            }
            if (item.IsDirectory) dirs.Add(rel);
            else files.Add(rel);
            plan.Add(new PlannedItem(item, rel));
        }
        return plan;
    }

    internal sealed class PlannedItem
    {
        public PlannedItem(ZipItem item, string rel)
        {
            Item = item;
            Rel = rel;
        }

        public ZipItem Item { get; }
        public string Rel { get; }
    }

    // ───────────── 풀기 ─────────────

    /// <summary>
    /// <paramref name="destination"/> 에 모두 푼다(없으면 만든다). 암호는 <see cref="ResolvePassword"/> 로 정한 바이트.
    /// 실패하면 <see cref="SfxException"/>·IO 예외·<see cref="OperationCanceledException"/> — 그때 대상에는 이번에 만든 것이 남지 않는다.
    /// </summary>
    public static ExtractOutcome Extract(SfxPayload payload, byte[]? passwordBytes, string destination, ICollisionHandler collisions,
        Action<ExtractProgress>? progress, CancellationToken ct)
    {
        var plan = Plan(payload);
        if (passwordBytes is null && plan.Any(p => !p.Item.IsDirectory && p.Item.IsEncrypted))
            throw new SfxException(SfxErrorKind.PasswordRequired, SfxMessages.PasswordRequired);

        string dest = Path.GetFullPath(destination);
        var report = new ExtractProgress { BytesTotal = plan.Where(p => !p.Item.IsDirectory).Sum(p => p.Item.UncompressedSize), FilesTotal = plan.Count(p => !p.Item.IsDirectory) };
        var createdDestDirs = CreateDestination(dest);
        Staging? staging = null;
        DestinationLock? destLock = null;
        string? earlierNote = null;
        var ledger = new CommitLedger();
        try
        {
            // 0) 지난번에 강제로 끊긴 풀기의 흔적(주인 없는 임시 폴더)을 정리한다 — 비켜 둔 원래 파일은 지우지 않고 보이는 폴더로(W2-10 검증 지적 1차)
            var stale = SfxStaging.CleanupStale(dest, DateTime.Now, deleteAttempts: MoveRetries + 1);
            if (stale.Moved > 0) earlierNote = SfxMessages.RescuedFromEarlier(stale.Moved, stale.FolderName!);
            staging = Staging.Create(dest);
            string stagingRoot = staging.Root;
            // 풀린 것은 임시 폴더 안 한 칸 더 아래(주인 표시·'.덮어쓴 파일' 과 압축 속 이름이 겹치지 않게)
            string contentRoot = Path.Combine(stagingRoot, Staging.ContentFolderName);

            // 1) 임시 폴더에 다 풀며 확인(만든 폴더는 기억 — 파일마다 폴더를 다시 보지 않게)
            var stagingDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var buffer = new byte[81920];
            Directory.CreateDirectory(contentRoot);
            foreach (var p in plan)
            {
                ct.ThrowIfCancellationRequested();
                string path = Inside(contentRoot, p.Rel, p.Item.Name);
                if (p.Item.IsDirectory)
                {
                    if (stagingDirs.Add(path)) Directory.CreateDirectory(path);
                    continue;
                }
                string parent = Path.GetDirectoryName(path)!;
                if (stagingDirs.Add(parent)) Directory.CreateDirectory(parent);
                report.Current = p.Item.Name.TrimEnd('/');
                progress?.Invoke(report);
                ExtractOne(payload, p.Item, passwordBytes, path, buffer, n =>
                {
                    report.BytesDone += n;
                    progress?.Invoke(report);
                }, ct);
                report.FilesDone++;
            }

            // 2) 다 맞으면 제자리로 — 같은 풀 곳의 다른 자동 풀림이 옮기는 중이면 끝나기를 기다린다(서로의 결과를 되돌리며 지우지 않게)
            report.Finishing = true;
            progress?.Invoke(report);
            destLock = DestinationLock.Acquire(dest, ct, () =>
            {
                report.Waiting = true;
                progress?.Invoke(report);
            });
            if (report.Waiting)
            {
                report.Waiting = false;
                progress?.Invoke(report);
            }
            var outcome = new ExtractOutcome();
            ledger.BackupRoot = Path.Combine(stagingRoot, SfxStaging.BackupFolderName);
            // 풀 곳에 아직 없는 폴더는 통째로 한 번에 옮긴다(그 안 파일마다 옮기지 않음 — 회사 PC 는 파일 하나 옮기기가 수 ms 라 작은 파일이 많으면 느렸음,
            // W2-10 검증 지적 1차). 없는 폴더라 같은 이름을 만날 일이 없고, 되돌릴 때는 그 폴더째 임시 폴더로 되돌린다.
            var wholeMoved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < plan.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = plan[i];
                string target = Inside(dest, p.Rel, p.Item.Name);
                if (MovedWithFolder(p, dest, contentRoot, ledger, wholeMoved))
                {
                    if (!p.Item.IsDirectory) outcome.Extracted++;
                    continue;
                }
                if (p.Item.IsDirectory)
                {
                    ledger.EnsureDirectory(target, dest);
                    continue;
                }
                string parent = Path.GetDirectoryName(target)!;
                ledger.EnsureDirectory(parent, dest);
                string staged = Path.Combine(contentRoot, p.Rel);
                // 풀 곳에 원래 있던 폴더면 그 폴더를 처음 볼 때 한 번 훑어 둔 이름으로 같은 이름을 먼저 가린다(실패할 옮기기를 해 보지 않게 —
                // 회사 PC 는 실패하는 옮기기·'있나?' 하나에도 1~2ms 라 같은 이름 수천 개를 덮어쓰면 느렸음, W2-10 검증 지적 2차).
                // 모르는 이름(대부분)은 먼저 옮겨 본다 — 그 자리에 무엇이 있으면 그때 가린다(보고 나서 옮기기 사이의 틈이 없음).
                var existing = ledger.Existing(parent, Path.GetFileName(target));
                if (existing == ExistingEntry.Directory)
                    throw new SfxException(SfxErrorKind.Io, SfxMessages.FileBlockedByFolder(p.Rel.Replace('\\', '/')));
                if (existing == ExistingEntry.None)
                {
                    if (ledger.TryMoveNew(staged, target))
                    {
                        outcome.Extracted++;
                        continue;
                    }
                    if (Directory.Exists(target)) throw new SfxException(SfxErrorKind.Io, SfxMessages.FileBlockedByFolder(p.Rel.Replace('\\', '/')));
                }
                switch (collisions.Resolve(target, p.Item, plan.Count - i - 1))
                {
                    case CollisionChoice.Cancel:
                        throw new OperationCanceledException();
                    case CollisionChoice.Skip:
                        outcome.Skipped++;
                        continue;
                    default:
                        ledger.Replace(staged, target, p.Rel);
                        outcome.Overwritten++;
                        outcome.Extracted++;
                        continue;
                }
            }
            destLock.Dispose();
            destLock = null;

            // 다 됐다 — 임시 폴더(덮어쓴 원래 파일 포함)를 지운다(주인 표시는 마지막에 닫음). 지우지 못해도 결과는 그대로(숨김 폴더가 남을 뿐 — 다음 풀기가 정리)
            ledger.CloseList();
            staging.Delete();
            outcome.Note = earlierNote;
            return outcome;
        }
        catch (Exception ex)
        {
            // 덮어쓰려고 비켜 둔 원래 파일을 하나라도 못 되돌렸으면 숨은 임시 폴더에 두지 않고 보이는 폴더로 옮겨 알린다
            // (예전: 숨은 '.덮어쓴 파일' 에 말없이 남겼다가 하루 뒤 같은 곳에 풀면 지웠음 — W2-10 검증 지적 1차). 못 옮긴 것이 있으면 임시 폴더를 남긴다.
            ledger.CloseList();   // 목록을 읽을 수 있게(보이는 폴더로 옮길 때 원래 이름)
            bool restored = ledger.Rollback();
            string? note = null;
            bool keepStaging = false;
            if (!restored && staging is not null)
            {
                var rescue = new SfxStaging.RescueSession(dest, DateTime.Now);
                keepStaging = !rescue.Rescue(staging.Root);
                rescue.Finish();
                note = SfxMessages.NotRestored(rescue.Moved, rescue.FolderName, rescue.Left, Path.GetFileName(staging.Root));
            }
            destLock?.Dispose();
            if (staging is not null)
            {
                if (keepStaging) staging.ReleaseMarker();
                else staging.Delete();
            }
            RemoveIfEmpty(createdDestDirs);
            string? extra = Join(earlierNote, note);
            if (extra is not null) throw new SfxException(SfxMessages.KindOf(ex), SfxMessages.For(ex) + " " + extra, ex);
            throw;
        }
    }

    private static string? Join(string? a, string? b) =>
        a is null ? b : b is null ? a : a + " " + b;

    /// <summary>
    /// 항목이 이미 '통째로 옮긴 폴더' 안이면 true. 아니면 그 항목의 위 폴더(폴더 항목이면 자신까지) 중 풀 곳에 아직 없는 가장 위의 것을 임시 폴더에서 통째로 옮겨 보고
    /// 옮겼으면 true(그 안의 다른 항목도 함께 옮겨짐). 위 폴더가 모두 있거나 통째로 옮기지 못하면 false — 부르는 쪽이 하나씩 옮긴다.
    /// 폴더 자리에 같은 이름 파일이 있으면 '폴더를 만들 수 없어요'.
    /// </summary>
    private static bool MovedWithFolder(PlannedItem p, string dest, string contentRoot, CommitLedger ledger, HashSet<string> wholeMoved)
    {
        string[] parts = p.Rel.Split('\\');
        int depth = p.Item.IsDirectory ? parts.Length : parts.Length - 1;
        string prefix = string.Empty;
        for (int k = 0; k < depth; k++)
        {
            prefix = k == 0 ? parts[0] : prefix + "\\" + parts[k];
            if (wholeMoved.Contains(prefix)) return true;
            string target = Path.Combine(dest, prefix);
            if (ledger.IsKnownDirectory(target)) continue;
            if (Directory.Exists(target))
            {
                ledger.RememberDirectory(target);
                continue;
            }
            if (File.Exists(target)) throw new SfxException(SfxErrorKind.Io, SfxMessages.FolderBlockedByFile(parts[k]));
            if (!ledger.TryMoveDirectory(Path.Combine(contentRoot, prefix), target)) return false;
            wholeMoved.Add(prefix);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 항목 하나를 <paramref name="outPath"/> 에 풀고 크기·CRC-32(AES 는 인증값까지)를 맞춰 본다. 틀리면 <see cref="SfxException"/>
    /// (파일은 부르는 쪽이 임시 폴더째 지움). 수정 시각은 닫기 전에 열린 핸들로 맞춘다(파일을 다시 열지 않게 — 작은 파일이 많을 때 느렸음, W2-10 검증 지적 1차).
    /// </summary>
    private static void ExtractOne(SfxPayload payload, ZipItem item, byte[]? pw, string outPath, byte[] buffer, Action<long> onBytes, CancellationToken ct)
    {
        long dataStart = payload.LocalDataStart(item, out ushort localTime);
        Stream raw = payload.OpenRange(dataStart, item.CompressedSize);
        Stream source = raw;
        AesEntryStream? aes = null;
        bool checkCrc = true;
        bool zipCrypto = false;
        try
        {
            if (item.IsEncrypted)
            {
                if (pw is null) throw new SfxException(SfxErrorKind.PasswordRequired, SfxMessages.PasswordRequired);
                if (item.IsAes)
                {
                    int saltLen = WinZipAesKeys.SaltLength(item.AesStrength);
                    long cipherLen = item.CompressedSize - saltLen - 2 - WinZipAesKeys.AuthCodeLength;
                    if (cipherLen < 0) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
                    byte[] head = ReadExactly(raw, saltLen + 2);
                    byte[] salt = new byte[saltLen];
                    Buffer.BlockCopy(head, 0, salt, 0, saltLen);
                    byte[] derived = WinZipAesKeys.Derive(pw, salt, item.AesStrength);
                    if (!WinZipAesKeys.VerifierMatches(derived, head[saltLen], head[saltLen + 1]))
                        throw new SfxException(SfxErrorKind.WrongPassword, SfxMessages.WrongPassword);
                    aes = new AesEntryStream(raw, cipherLen, new WinZipAesCipher(derived, item.AesStrength), item.Name);
                    source = aes;
                    // AE-2 는 CRC 칸이 0(인증값이 무결성 확인) — AE-1 이거나 CRC 가 적혀 있으면 CRC 도 본다
                    checkCrc = item.AesVendorVersion != 2 || item.Crc != 0;
                }
                else
                {
                    byte[] head = ReadExactly(raw, ZipCryptoKeys.HeaderLength);
                    if (!ZipCryptoKeys.CheckHeader(pw, head, AcceptedCheckBytes(item, localTime), out var keys))
                        throw new SfxException(SfxErrorKind.WrongPassword, SfxMessages.WrongPassword);
                    source = new ZipCryptoStream(raw, keys);
                    zipCrypto = true;
                }
            }

            Stream data = item.ContentMethod switch
            {
                0 => source,
                8 => new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true),
                _ => throw new SfxException(SfxErrorKind.Unsupported, SfxMessages.Unsupported("방식 " + item.ContentMethod)),
            };

            uint crc = 0;
            long written = 0;
            bool timeSet = false;
            try
            {
                using var output = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
                int n;
                while ((n = data.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    written += n;
                    if (written > item.UncompressedSize) throw Mismatch(item, zipCrypto);
                    crc = ZipCrc32.Append(crc, buffer, 0, n);
                    output.Write(buffer, 0, n);
                    onBytes(n);
                }
                output.Flush();
                if (item.LastWriteTime is DateTime lt) timeSet = TrySetLastWriteTime(output.SafeFileHandle, lt);
            }
            catch (InvalidDataException ex)
            {
                throw Mismatch(item, zipCrypto, ex);
            }
            finally
            {
                if (!ReferenceEquals(data, source)) data.Dispose();
            }

            aes?.Finish();   // 남은 암호문까지 읽어 인증값(HMAC)을 맞춰 본다
            if (written != item.UncompressedSize || (checkCrc && crc != item.Crc)) throw Mismatch(item, zipCrypto);

            if (!timeSet && item.LastWriteTime is DateTime t)
            {
                try { File.SetLastWriteTime(outPath, t); } catch (Exception) { /* 시각은 못 맞춰도 내용은 그대로 */ }
            }
        }
        finally
        {
            source.Dispose();
            raw.Dispose();
        }
    }

    /// <summary>열린 파일의 수정 시각을 맞춘다(지역 시각). 시각을 직접 정한 핸들은 닫을 때 Windows 가 그 시각을 덮어쓰지 않는다. 실패하면 false.</summary>
    private static bool TrySetLastWriteTime(SafeFileHandle handle, DateTime local)
    {
        try
        {
            long ft = local.ToFileTime();
            return SetFileTime(handle, IntPtr.Zero, IntPtr.Zero, ref ft);
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileTime(SafeFileHandle hFile, IntPtr lpCreationTime, IntPtr lpLastAccessTime, ref long lpLastWriteTime);

    /// <summary>내용이 원래와 다름 — ZipCrypto 는 틀린 암호가 머리 검사를 우연히 통과했을 수 있어(256분의 1) '암호가 틀렸거나 손상'.</summary>
    private static SfxException Mismatch(ZipItem item, bool zipCrypto, Exception? inner = null) =>
        zipCrypto
            ? new SfxException(SfxErrorKind.WrongPassword, SfxMessages.WrongPasswordOrDamaged, inner)
            : new SfxException(SfxErrorKind.Damaged, SfxMessages.Checksum(item.Name.TrimEnd('/')), inner);

    private static byte[] ReadExactly(Stream s, int count)
    {
        var b = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = s.Read(b, got, count - got);
            if (n <= 0) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
            got += n;
        }
        return b;
    }

    // ───────────── 폴더 ─────────────

    /// <summary>뿌리 아래 상대 경로의 전체 경로 — 뿌리 밖이면 안전하지 않은 경로로 멈춘다(이름 검사를 지났더라도 한 번 더).</summary>
    private static string Inside(string root, string rel, string name)
    {
        string full = Path.GetFullPath(Path.Combine(root, rel));
        string r = root.TrimEnd('\\') + "\\";
        if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new SfxException(SfxErrorKind.UnsafePath, SfxMessages.UnsafePath(name));
        return full;
    }

    /// <summary>대상 폴더(와 없는 위 폴더들)를 만든다. 만든 것(위에서 아래로)을 돌려준다 — 실패하면 비어 있는 것만 지운다.</summary>
    private static List<string> CreateDestination(string dest)
    {
        var toCreate = new Stack<string>();
        string? cur = dest;
        while (cur is not null && !Directory.Exists(cur))
        {
            if (File.Exists(cur)) throw new SfxException(SfxErrorKind.Io, SfxMessages.FolderBlockedByFile(Path.GetFileName(cur)));
            toCreate.Push(cur);
            cur = Path.GetDirectoryName(cur);
        }
        if (cur is null) throw new SfxException(SfxErrorKind.Io, "풀 위치를 찾을 수 없어요. 다른 폴더를 골라 주세요.");
        var created = new List<string>();
        try
        {
            while (toCreate.Count > 0)
            {
                string d = toCreate.Pop();
                Directory.CreateDirectory(d);
                created.Add(d);
            }
        }
        catch
        {
            RemoveIfEmpty(created);
            throw;
        }
        return created;
    }

    /// <summary>
    /// 대상 안 숨김 임시 폴더(이번 풀기의 것). 만들자마자 주인 표시(<see cref="SfxStaging.MarkerFileName"/>)를 독점으로 열어 두고 끝날 때 닫는다 —
    /// 그 사이 다른 풀기·일집 본체는 이 폴더를 정리하지 않고, 이 프로세스가 강제로 죽으면 Windows 가 닫아 줘 다음 풀기가 바로 정리한다.
    /// 표시를 못 만들면(드묾) 표시 없이 쓴다(그때는 예전처럼 하루 지난 뒤에 정리됨).
    /// </summary>
    private sealed class Staging
    {
        /// <summary>풀린 것을 두는 칸(임시 폴더 안 — 주인 표시·'.덮어쓴 파일' 과 압축 속 이름이 겹치지 않게).</summary>
        public const string ContentFolderName = "내용";

        private FileStream? _marker;

        private Staging(string root, FileStream? marker)
        {
            Root = root;
            _marker = marker;
        }

        public string Root { get; }

        public static Staging Create(string dest)
        {
            for (int i = 0; i < 10; i++)
            {
                string p = Path.Combine(dest, SfxStaging.Prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
                if (Directory.Exists(p) || File.Exists(p)) continue;
                var di = Directory.CreateDirectory(p);
                string markerPath = Path.Combine(p, SfxStaging.MarkerFileName);
                FileStream? marker = null;
                try
                {
                    Retry(() => marker = new FileStream(markerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
                }
                catch (Exception)
                {
                    // 표시 없이 — 하루 규칙으로만 정리됨(열지 못한 표시 파일이 남아 '주인 없음'으로 보이지 않게 지움)
                    try { File.Delete(markerPath); } catch (Exception) { /* 그대로 */ }
                }
                try { di.Attributes |= FileAttributes.Hidden; } catch (Exception) { /* 숨김 실패는 무시 */ }
                return new Staging(p, marker);
            }
            throw new IOException("임시 폴더를 만들지 못했어요: " + dest);
        }

        /// <summary>주인 표시를 닫는다(폴더는 남김 — 다음 풀기가 주인 없음으로 보고 정리).</summary>
        public void ReleaseMarker()
        {
            _marker?.Dispose();
            _marker = null;
        }

        /// <summary>
        /// 폴더째 지운다 — 주인 표시는 쥔 채 나머지(덮어쓴 원래 파일 등)를 먼저 지우고 표시는 마지막에 닫는다(<see cref="SfxStaging.DeleteOwnedTree"/>).
        /// 예전엔 표시를 먼저 닫아, 지우는 사이 같은 곳에 막 시작한 다른 풀기가 '주인 없음'으로 보고 방금 일부러 덮어쓴 원래 파일을
        /// '덮어쓰기 전 원래 파일 (…)' 폴더로 옮기며 '지난번에 끝까지 못 푼 흔적'이라고 잘못 알렸다(W2-10 검증 지적 2차).
        /// 잠기면 몇 번 다시 — 못 지워도 그만(다음 풀기가 정리).
        /// </summary>
        public void Delete()
        {
            var marker = _marker;
            _marker = null;
            SfxStaging.DeleteOwnedTree(Root, marker, MoveRetries + 1);
        }
    }

    /// <summary>
    /// 같은 풀 곳의 '제자리로 옮기기·되돌리기'를 한 번에 하나씩 하게 하는 잠금(같은 로그인 세션의 이름 있는 뮤텍스 — <see cref="SfxStaging.DestinationMutexName"/>).
    /// 두 번 눌러 둘이 같은 곳에 풀면 나중 것은 먼저 것이 끝나기를 기다렸다가 이어서 한다(먼저 것의 결과는 '같은 이름'으로 만남 — 서로의 결과를 되돌리며 지우지 않음).
    /// 앞의 주인이 강제로 죽었으면(버려진 뮤텍스) 그대로 넘겨받는다. 잡은 스레드에서 놓는다(<see cref="Extract"/> 는 한 스레드에서 돈다).
    /// </summary>
    private sealed class DestinationLock : IDisposable
    {
        private Mutex? _mutex;

        private DestinationLock(Mutex? mutex) => _mutex = mutex;

        public static DestinationLock Acquire(string dest, CancellationToken ct, Action onWaiting)
        {
            Mutex m;
            try
            {
                m = new Mutex(false, SfxStaging.DestinationMutexName(dest));
            }
            catch (Exception)
            {
                return new DestinationLock(null);   // 잠금을 못 만들면(드묾) 예전처럼 바로
            }
            var waited = Stopwatch.StartNew();
            bool reported = false;
            while (true)
            {
                bool got;
                try
                {
                    got = m.WaitOne(200);
                }
                catch (AbandonedMutexException)
                {
                    got = true;   // 앞의 주인이 강제로 끝남 — 이제 우리 것
                }
                if (got) return new DestinationLock(m);
                if (ct.IsCancellationRequested || waited.Elapsed > DestinationWaitLimit)
                {
                    m.Dispose();
                    ct.ThrowIfCancellationRequested();
                    throw new SfxException(SfxErrorKind.Io, SfxMessages.DestinationBusy);
                }
                if (!reported)
                {
                    reported = true;
                    onWaiting();
                }
            }
        }

        public void Dispose()
        {
            if (_mutex is null) return;
            try { _mutex.ReleaseMutex(); } catch (Exception) { /* 이미 놓음 */ }
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private static void RemoveIfEmpty(List<string> createdTopDown)
    {
        for (int i = createdTopDown.Count - 1; i >= 0; i--)
        {
            try
            {
                if (Directory.Exists(createdTopDown[i]) && !Directory.EnumerateFileSystemEntries(createdTopDown[i]).Any())
                    Directory.Delete(createdTopDown[i]);
            }
            catch (Exception) { /* 남겨 둠 */ }
        }
    }

    /// <summary>잠김(공유·잠금 위반, 접근 거부)이면 200ms 간격으로 3번 다시(회사 PC 의 DLP·백신이 막 만든 파일을 잠깐 잡음).</summary>
    internal static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < MoveRetries && IsTransient(ex))
            {
                Thread.Sleep(MoveRetryDelayMs);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException
        || (ex is IOException && (ex.HResult == unchecked((int)0x80070020) || ex.HResult == unchecked((int)0x80070021)));

    /// <summary>제자리로 옮긴 기록 — 실패하면 거꾸로 되돌린다. 다 쓰면(성공·실패 모두) <see cref="CloseList"/>.</summary>
    private sealed class CommitLedger
    {
        private readonly List<string> _createdDirs = new List<string>();
        private readonly List<string> _moved = new List<string>();
        private readonly List<KeyValuePair<string, string>> _backups = new List<KeyValuePair<string, string>>();
        private readonly HashSet<string> _knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<KeyValuePair<string, string>> _movedDirs = new List<KeyValuePair<string, string>>();
        private int _backupCount;
        private SfxStaging.BackupList? _list;

        /// <summary>덮어쓸 원래 파일을 비켜 둘 곳(임시 폴더 안).</summary>
        public string BackupRoot { get; set; } = string.Empty;

        /// <summary>원래 자리 목록(목록.txt)을 닫는다 — 목록을 읽거나(보이는 폴더로 옮기기) 임시 폴더를 지우기 전에.</summary>
        public void CloseList()
        {
            _list?.Dispose();
            _list = null;
        }

        public bool IsKnownDirectory(string dir) => _knownDirs.Contains(dir);

        /// <summary>풀 곳에 원래 있던 폴더로 기억한다(그 안의 같은 이름은 <see cref="Existing"/> 가 한 번 훑어 가림).</summary>
        public void RememberDirectory(string dir)
        {
            _knownDirs.Add(dir);
            _preExisting.Add(dir);
        }

        // 풀 곳에 원래 있던 폴더와 그 안의 이름(대소문자까지 같은 것 — 처음 볼 때 한 번 훑음)
        private readonly HashSet<string> _preExisting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, bool>> _names = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 풀 곳에 원래 있던 폴더 <paramref name="dir"/> 안에 이름이 (대소문자까지) 같은 파일·폴더가 있었는가 — 그 폴더를 처음 볼 때 한 번 훑어 둔다
        /// (훑기는 한 번이라 빠르다 — 파일마다 옮겨 보거나 '있나?'를 묻지 않게). 이번에 만든 폴더·훑지 못한 폴더·대소문자만 다른 이름은 None
        /// (부르는 쪽이 옮겨 보고 가림 — 예전 길이라 결과는 같음).
        /// </summary>
        public ExistingEntry Existing(string dir, string name)
        {
            if (!_preExisting.Contains(dir)) return ExistingEntry.None;
            if (!_names.TryGetValue(dir, out var names))
            {
                names = new Dictionary<string, bool>(StringComparer.Ordinal);
                try
                {
                    foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                        names[e.Name] = (e.Attributes & FileAttributes.Directory) != 0;
                }
                catch (Exception)
                {
                    names.Clear();   // 못 훑으면 모르는 것으로(옮겨 보고 가림)
                }
                _names[dir] = names;
            }
            return names.TryGetValue(name, out bool isDir)
                ? isDir ? ExistingEntry.Directory : ExistingEntry.File
                : ExistingEntry.None;
        }

        /// <summary>
        /// 임시 폴더의 폴더를 풀 곳(아직 없는 자리)으로 통째로 옮긴다(같은 볼륨 — 이름만 바뀜). 그 사이 같은 이름이 생겼거나 안의 파일이 잠겨 못 옮기면 false(하나씩 옮김).
        /// 되돌릴 때는 통째로 임시 폴더로 되돌린다(그 폴더는 이번에 처음 생긴 자리라 사용자 파일이 없음).
        /// </summary>
        public bool TryMoveDirectory(string from, string to)
        {
            try
            {
                Retry(() => Directory.Move(from, to));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
            _movedDirs.Add(new KeyValuePair<string, string>(from, to));
            _knownDirs.Add(to);
            return true;
        }

        /// <summary>
        /// 폴더가 없으면 만든다(위 폴더부터, <paramref name="stopAt"/> 까지). 같은 이름 파일이 있으면 실패.
        /// 있다고 확인한 폴더는 기억한다(같은 폴더의 파일마다 다시 보지 않게 — 작은 파일이 많을 때 느렸음).
        /// </summary>
        public void EnsureDirectory(string dir, string stopAt)
        {
            if (_knownDirs.Contains(dir)) return;
            if (Directory.Exists(dir))
            {
                RememberDirectory(dir);
                return;
            }
            var toCreate = new Stack<string>();
            string? cur = dir;
            while (cur is not null && !Directory.Exists(cur))
            {
                if (File.Exists(cur)) throw new SfxException(SfxErrorKind.Io, SfxMessages.FolderBlockedByFile(Path.GetFileName(cur)));
                toCreate.Push(cur);
                if (string.Equals(cur.TrimEnd('\\'), stopAt.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) break;
                cur = Path.GetDirectoryName(cur);
            }
            while (toCreate.Count > 0)
            {
                string d = toCreate.Pop();
                Directory.CreateDirectory(d);
                _createdDirs.Add(d);
                _knownDirs.Add(d);
            }
        }

        /// <summary>
        /// 새 파일을 제자리로 옮긴다(덮어쓰지 않음). 그 자리에 파일·폴더가 이미 있으면 false(부르는 쪽이 같은 이름 처리 — 먼저 '있나?'를 보지 않아 파일마다 두 번 덜 봄,
        /// 보고 나서 옮기기 사이에 다른 프로그램이 같은 이름을 만들어도 덮지 않고 같은 이름으로 다룸). 잠김은 200ms 간격으로 3번 다시.
        /// </summary>
        public bool TryMoveNew(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(from, to);
                    _moved.Add(to);
                    return true;
                }
                catch (IOException ex) when (IsAlreadyExists(ex))
                {
                    return false;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (File.Exists(to) || Directory.Exists(to)) return false;
                    if (attempt < MoveRetries && IsTransient(ex))
                    {
                        Thread.Sleep(MoveRetryDelayMs);
                        continue;
                    }
                    throw;
                }
            }
        }

        private static bool IsAlreadyExists(IOException ex) =>
            ex.HResult == unchecked((int)0x800700B7) || ex.HResult == unchecked((int)0x80070050);   // ERROR_ALREADY_EXISTS·ERROR_FILE_EXISTS

        /// <summary>
        /// 원래 파일을 비켜 두고(되돌릴 수 있게) 새 파일을 그 자리로. 비키기 <b>전에</b> 원래 자리(<paramref name="rel"/>)를 목록에 적는다 —
        /// 그 사이에 강제로 죽어도 다음 풀기가 원래 이름으로 보이는 폴더에 옮겨 줄 수 있게(<see cref="SfxStaging.RescueSession"/>).
        /// 비켜 둘 폴더는 처음 한 번만 만들고 목록 파일은 열어 둔 채 쓴다(파일마다 열고 닫지 않음 — W2-10 검증 지적 2차).
        /// </summary>
        public void Replace(string from, string to, string rel)
        {
            if (_list is null)
            {
                Directory.CreateDirectory(BackupRoot);
                _list = new SfxStaging.BackupList(BackupRoot);
            }
            int number = _backupCount++;
            string backup = Path.Combine(BackupRoot, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _list.Add(number, rel);
            Retry(() => File.Move(to, backup));
            _backups.Add(new KeyValuePair<string, string>(backup, to));
            Retry(() => File.Move(from, to));
            _moved.Add(to);
        }

        /// <summary>거꾸로 되돌린다. 비켜 둔 원래 파일을 모두 제자리로 돌려놨으면 true(하나라도 못 했으면 false — 임시 폴더를 지우면 안 됨).</summary>
        public bool Rollback()
        {
            bool restoredAll = true;
            // 통째로 옮긴 폴더는 통째로 임시 폴더로(안 되면 지움 — 이번에 처음 생긴 자리)
            for (int i = _movedDirs.Count - 1; i >= 0; i--)
            {
                var m = _movedDirs[i];
                try { Retry(() => Directory.Move(m.Value, m.Key)); }
                catch (Exception) { SfxStaging.DeleteTree(m.Value, MoveRetries + 1); }
            }
            for (int i = _moved.Count - 1; i >= 0; i--)
            {
                try
                {
                    File.SetAttributes(_moved[i], FileAttributes.Normal);
                    File.Delete(_moved[i]);
                }
                catch (Exception) { /* 되돌리기 실패는 남겨 둠 */ }
            }
            for (int i = _backups.Count - 1; i >= 0; i--)
            {
                var b = _backups[i];
                try { Retry(() => File.Move(b.Key, b.Value)); }
                catch (Exception) { restoredAll = false; }
            }
            for (int i = _createdDirs.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (Directory.Exists(_createdDirs[i]) && !Directory.EnumerateFileSystemEntries(_createdDirs[i]).Any())
                        Directory.Delete(_createdDirs[i]);
                }
                catch (Exception) { /* 남겨 둠 */ }
            }
            return restoredAll;
        }
    }
}

/// <summary>ZipCrypto 로 암호화된 데이터를 읽으며 푼다.</summary>
internal sealed class ZipCryptoStream : Stream
{
    private readonly Stream _inner;
    private readonly ZipCryptoKeys _keys;

    public ZipCryptoStream(Stream inner, ZipCryptoKeys keys)
    {
        _inner = inner;
        _keys = keys;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _inner.Read(buffer, offset, count);
        if (n > 0) _keys.Decrypt(buffer, offset, n);
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

/// <summary>WinZip AES 암호문을 읽으며 풀고, <see cref="Finish"/> 에서 남은 암호문까지 읽어 인증값(HMAC 앞 10바이트)을 맞춰 본다.</summary>
internal sealed class AesEntryStream : Stream
{
    private readonly Stream _inner;
    private readonly WinZipAesCipher _cipher;
    private readonly string _name;
    private long _remaining;
    private bool _finished;

    public AesEntryStream(Stream inner, long cipherLength, WinZipAesCipher cipher, string name)
    {
        _inner = inner;
        _remaining = cipherLength;
        _cipher = cipher;
        _name = name;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0 || count <= 0) return 0;
        int n = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
        if (n <= 0) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
        _remaining -= n;
        _cipher.Decrypt(buffer, offset, n);
        return n;
    }

    /// <summary>남은 암호문을 HMAC 에 넣고 뒤의 인증값과 맞춰 본다. 틀리면 '내용이 원래와 달라요'.</summary>
    public void Finish()
    {
        if (_finished) return;
        var buf = new byte[81920];
        while (_remaining > 0)
        {
            int n = _inner.Read(buf, 0, (int)Math.Min(buf.Length, _remaining));
            if (n <= 0) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
            _cipher.AppendCiphertext(buf, 0, n);
            _remaining -= n;
        }
        var stored = new byte[WinZipAesKeys.AuthCodeLength];
        int got = 0;
        while (got < stored.Length)
        {
            int n = _inner.Read(stored, got, stored.Length - got);
            if (n <= 0) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
            got += n;
        }
        if (!_cipher.AuthCodeMatches(stored, 0)) throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Checksum(_name.TrimEnd('/')));
        _finished = true;
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

    protected override void Dispose(bool disposing)
    {
        if (disposing) _cipher.Dispose();
        base.Dispose(disposing);
    }
}
