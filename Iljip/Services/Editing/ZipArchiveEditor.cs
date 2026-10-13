using System.IO;
using System.Text;
using Iljip.Models;

namespace Iljip.Services.Editing;

/// <summary>
/// ZIP 고치기(W2-06, I2-3) — 다시 압축하지 않는 원시 복사(<see cref="ZipRawRewriter"/>).
/// <list type="bullet">
/// <item>지우기: 그 항목의 기록만 빼고 나머지 기록은 바이트 그대로 옮긴다(압축된 데이터·CRC·시각·암호·CP949 이름 그대로).</item>
/// <item>이름 바꾸기: 이름 칸만 새로(UTF-8 + bit 11) — 데이터는 그대로라 암호를 몰라도 된다. 폴더면 그 아래 전부.</item>
/// <item>새 폴더: '경로/' 항목(크기 0).</item>
/// <item>넣기: 넣을 파일만 담은 임시 ZIP 을 원래 압축과 같은 수준으로 만들고(<see cref="ZipArchiveService.WriteCollected"/> — 암호 없음, 잠긴 원본 건너뜀),
///   그 압축된 데이터를 옮겨 담는다. 원래 압축에 암호 항목이 있으면 같은 암호 바이트(반디집 CP949 한글 암호 포함 — <see cref="ZipPasswordProbe"/>)와
///   같은 방식(ZipCrypto 또는 같은 세기의 AES)으로 암호화해 넣는다 — 이때 암호가 없으면 <see cref="PasswordRequiredException"/>, 틀리면 <see cref="WrongPasswordException"/>.</item>
/// </list>
/// 결과는 원본 옆 숨김 임시 파일에 쓰고 검사(<see cref="IArchiveTester"/>)가 맞을 때만 바꾼다(<see cref="ArchiveSwap"/>).
/// 고칠 수 있는 것: 확장자 .zip 이고 나뉘지 않은 ZIP(.z01 나눔·.zip.001 숫자 분할·앞에 실행 파일이 붙은 ZIP·PKWARE 강한 암호는 <see cref="ArchiveNotEditableException"/>).
/// </summary>
public sealed class ZipArchiveEditor : IArchiveEditor
{
    private readonly IArchiveTester _tester;

    public ZipArchiveEditor(IArchiveTester? tester = null) => _tester = tester ?? ArchiveTester.Instance;

    /// <summary>시험용: 결과를 쓰는 스트림을 감싼다(쓰기 실패 주입). 실제 동작에서는 null.</summary>
    internal Func<Stream, Stream>? OutputWrapper { get; init; }

    public bool CanEdit(string path) => IsEditablePath(path);

    /// <summary>확장자 .zip 이고 나뉜 ZIP(.zip.001 숫자 분할·.z01 나눔의 마지막 조각)이 아니다.</summary>
    internal static bool IsEditablePath(string path)
    {
        if (!path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            return !SplitVolumeStream.IsNumberedFirstVolume(path) && !SpannedZipStream.IsLastVolume(path);
        }
        catch
        {
            return false;
        }
    }

    public Task<EditResult> EditAsync(
        string archivePath, IReadOnlyList<EditOp> ops, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        // 같은 이름 처리기는 부른 쪽의 범위(AsyncLocal)에서 — Task.Run 으로도 흐른다
        var resolver = ExtractionScope.Current?.Resolver;
        return Task.Run(() => Edit(Path.GetFullPath(archivePath), ops, password, resolver, progress, cancellationToken), cancellationToken);
    }

    private EditResult Edit(
        string archivePath, IReadOnlyList<EditOp> ops, string? password, ICollisionResolver? resolver,
        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (!IsEditablePath(archivePath)) throw new ArchiveNotEditableException();
        ArchiveSwap.EnsureWritable(archivePath);
        ct.ThrowIfCancellationRequested();
        var stamp = ArchiveSwap.Stamp(archivePath);   // 바꾸기 직전에 같은지 본다(그 사이 다른 곳에서 바뀌었으면 덮어쓰지 않음)

        ZipRawRewriter.Model model;
        using (var s = OpenSource(archivePath))
            model = ZipRawRewriter.Read(s);

        // ── 계획 ──
        var planner = new EditPlanner<ZipSourceEntry>(model.Entries.Select(e => new EditPlanner<ZipSourceEntry>.Item
        {
            Source = e,
            OriginalKey = e.Key,
            IsDirectory = e.IsDirectory,
            Size = e.UncompressedSize,
            Modified = e.Modified,
        }));
        var files = new List<EditSupport.PlannedSource>();
        var skipped = new List<SkippedSource>();
        int tempCounter = 0;
        bool anyAdd = false;
        foreach (var op in ops)
        {
            ct.ThrowIfCancellationRequested();
            switch (op)
            {
                case EditOp.Delete d: planner.Delete(d.EntryKeys); break;
                case EditOp.Rename r: planner.Rename(r.EntryKey, r.NewName); break;
                case EditOp.NewFolder n: planner.NewFolder(n.Path); break;
                case EditOp.Add a:
                    anyAdd = true;
                    EditSupport.PlanAdd(planner, a, resolver, () => (++tempCounter).ToString("D7", System.Globalization.CultureInfo.InvariantCulture), files, skipped);
                    break;
            }
        }
        var addedFiles = planner.AddedFiles.ToList();
        if (anyAdd && addedFiles.Count == 0 && planner.CollisionSkipped == 0 && skipped.Count > 0 && !planner.HasChanges)
            throw new ArchiveEditException(EditSupport.NothingToAddMessage);
        if (!planner.HasChanges)
            return new EditResult { CollisionSkipped = planner.CollisionSkipped, Skipped = skipped };

        // ── 암호(넣을 파일이 있고 원래 압축에 암호 항목이 있을 때, 또는 암호를 받았을 때 확인) ──
        var encrypted = model.Entries.Where(e => e.IsEncrypted && !e.IsDirectory).ToList();
        ZipEntryCrypto? crypto = null;
        string? testPassword = null;
        if (encrypted.Count > 0 && (addedFiles.Count > 0 || !string.IsNullOrEmpty(password)))
        {
            if (string.IsNullOrEmpty(password)) throw new PasswordRequiredException();
            byte[] bytes = PasswordBytesFor(archivePath, password, encrypted);
            testPassword = password;
            var aes = encrypted.FirstOrDefault(e => e.IsAes);
            crypto = aes is not null ? ZipEntryCrypto.Aes(bytes, aes.AesStrength, aes.AesVersion) : ZipEntryCrypto.ZipCrypto(bytes);
        }

        // ── 쓰기 ──
        var tracker = new EditSupport.Tracker(progress);
        var keptItems = planner.Kept.ToList();
        long addBytes = addedFiles.Sum(a => a.Size);
        long copyBytes = keptItems.Sum(i => i.Source.CompressedSize);
        // 검사(모든 항목을 풀어 CRC 확인 — 큰 ZIP 은 다시 쓰기보다 오래 걸림)도 막대에 넣는다(W2-06 검증 지적 2차 — 예전엔 검사 내내 100% 에 멈춰 보임).
        // 무게는 풀어 볼 크기(남는 파일 — 암호를 모르면 암호 항목은 빼고 — + 넣을 파일), 진행은 검사기가 풀어 본 바이트의 비율.
        long testBytes = Math.Max(1, keptItems.Where(i => !i.IsDirectory && (testPassword is not null || !i.Source.IsEncrypted))
            .Sum(i => Math.Max(0, i.Source.UncompressedSize)) + addBytes);
        tracker.SetTotals(addBytes + copyBytes + addBytes + testBytes, addedFiles.Count);

        string temp = ArchiveSwap.TempPath(archivePath);
        string? addsTemp = null;
        bool committed = false;
        try
        {
            // 1) 넣을 파일만 담은 임시 ZIP(원래 압축과 같은 수준, 암호 없음 — 암호는 옮겨 담을 때)
            Dictionary<string, ZipSourceEntry>? addEntries = null;
            if (files.Count > 0)
            {
                addsTemp = ArchiveSwap.TempPath(archivePath);
                var set = new SourceSet();
                foreach (var f in files)
                    if (addedFiles.Any(a => a.TempKey == f.TempKey)) set.Files.Add((f.SourcePath, f.TempKey));
                var names = addedFiles.ToDictionary(a => a.TempKey, a => a.Key, StringComparer.Ordinal);
                try
                {
                    var written = ZipArchiveService.WriteCollected(set, addsTemp, new CompressionOptions { Level = GuessLevel(model.Entries) },
                        tracker.CompressPhase(names), ct);
                    skipped.AddRange(written.Skipped);
                }
                catch (NothingToCompressException ex)
                {
                    skipped.AddRange(ex.Skipped);
                    foreach (var a in addedFiles) a.Removed = true;
                    if (!planner.HasChanges) throw new ArchiveEditException(EditSupport.NothingToAddMessage, ex);
                }
                tracker.EndPhase(addBytes);
                if (File.Exists(addsTemp))
                {
                    using var s = new FileStream(addsTemp, FileMode.Open, FileAccess.Read, FileShare.Read);
                    addEntries = ZipRawRewriter.Read(s).Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Key, StringComparer.Ordinal);
                }
                // 읽다 막혀 빠진 원본은 계획에서도 뺀다
                foreach (var a in addedFiles)
                    if (addEntries is null || !addEntries.ContainsKey(a.TempKey)) a.Removed = true;
                addedFiles = planner.AddedFiles.ToList();
            }

            // 2) 새 압축: 남길 항목(원래 차례) → 새 폴더 → 새 파일 → 중앙 디렉터리
            tracker.Stage(EditSupport.StageRewriting);
            using (var src = OpenSource(archivePath))
            using (var addSrc = addsTemp is not null && File.Exists(addsTemp) ? new FileStream(addsTemp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16) : null)
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
            {
                try { File.SetAttributes(temp, File.GetAttributes(temp) | FileAttributes.Hidden); } catch { /* 숨김 실패는 무시 */ }
                var output = OutputWrapper?.Invoke(fs) ?? fs;
                var writer = new ZipRawRewriter.Writer(output, ct, tracker.OnBytes);
                var bounds = ZipRawRewriter.Boundaries(model);
                foreach (var it in keptItems)
                {
                    var e = it.Source;
                    var region = ZipRawRewriter.Locate(src, e, bounds[e]);
                    byte[]? newName = it.Renamed ? Encoding.UTF8.GetBytes(it.IsDirectory ? it.Key + "/" : it.Key) : null;
                    writer.CopyEntry(src, e, region, newName);
                }
                var now = DateTime.Now;
                foreach (var dir in planner.AddedDirectories)
                    writer.AddDirectory(dir, now);
                if (addSrc is not null && addEntries is not null && addedFiles.Count > 0)
                {
                    var addModel = ZipRawRewriter.Read(addSrc);
                    var addBounds = ZipRawRewriter.Boundaries(addModel);
                    var byKey = addModel.Entries.Where(e => !e.IsDirectory).ToDictionary(e => e.Key, StringComparer.Ordinal);
                    foreach (var a in addedFiles)
                    {
                        var te = byKey[a.TempKey];
                        var region = ZipRawRewriter.Locate(addSrc, te, addBounds[te]);
                        writer.AddEntry(addSrc, te, region, Encoding.UTF8.GetBytes(a.Key), crypto);
                    }
                }
                writer.Finish(model.Comment);
                output.Flush();
                fs.Flush(flushToDisk: true);
            }
            if (addsTemp is not null) ArchiveSwap.DeleteQuietly(addsTemp);
            tracker.EndPhase(copyBytes + addBytes);

            // 3) 검사 → 바꾸기
            ct.ThrowIfCancellationRequested();
            tracker.Stage(EditSupport.StageTesting);
            var expected = keptItems.Select(i => i.Key)
                .Concat(planner.AddedDirectories)
                .Concat(addedFiles.Select(a => a.Key))
                .ToList();
            // 검사가 틀리면 원래 압축도 보아 '원래부터 손상'(고치다 생긴 문제가 아님)을 가린다
            ArchiveTester.TestOrBlameOriginal(_tester, temp, archivePath, EditableFormat.Zip, expected, model.Entries.Select(e => e.Key).ToList(),
                testPassword, ct, f => tracker.Within(testBytes, f));
            ct.ThrowIfCancellationRequested();
            ArchiveSwap.Commit(temp, archivePath, stamp);
            committed = true;
        }
        finally
        {
            if (!committed) ArchiveSwap.DeleteQuietly(temp);
            if (addsTemp is not null) ArchiveSwap.DeleteQuietly(addsTemp);
        }
        tracker.Final();
        return new EditResult { AddedFiles = addedFiles.Count, CollisionSkipped = planner.CollisionSkipped, Skipped = skipped };
    }

    /// <summary>원본 읽기(다른 프로그램이 읽는 중이어도 — 바꾸기는 나중에 따로).</summary>
    private static FileStream OpenSource(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex))
            {
                throw new ArchiveEditException(ArchiveSwap.LockedMessage, ex);
            }
        }
    }

    /// <summary>
    /// 새 항목을 암호화할 암호 바이트 — 원래 압축의 암호 항목에 맞는 쪽(UTF-8 또는 반디집 CP949). 어느 쪽도 맞지 않으면 <see cref="WrongPasswordException"/>.
    /// 판정할 수 없으면(드묾) ZipCrypto 는 CP949(반디집·알집·일집 ZipCrypto 와 같음), AES 는 UTF-8(일집·7-Zip AES 와 같음).
    /// </summary>
    private static byte[] PasswordBytesFor(string archivePath, string password, List<ZipSourceEntry> encrypted)
    {
        var probe = ZipPasswordProbe.Probe(archivePath, password);
        switch (probe.Match)
        {
            case PasswordBytes.Utf8 or PasswordBytes.Cp949 when probe.MatchedPasswordBytes is { } matched:
                return matched;
            case PasswordBytes.None:
                throw new WrongPasswordException();
        }
        if (encrypted.Any(e => e.IsAes)) return Encoding.UTF8.GetBytes(password);
        return ZipPasswordProbe.TryCp949Bytes(password) ?? Encoding.UTF8.GetBytes(password);
    }

    /// <summary>
    /// 원래 압축의 수준 짐작(넣을 파일을 같은 수준으로): 내용 있는 파일이 모두 '저장'이면 저장만, Deflate 플래그(bit 1·2)가 최대면 최대, 빠름이면 빠름, 아니면 보통.
    /// </summary>
    internal static CompressionLevelOption GuessLevel(IEnumerable<ZipSourceEntry> entries)
    {
        var files = entries.Where(e => !e.IsDirectory && e.UncompressedSize > 0).ToList();
        if (files.Count == 0) return CompressionLevelOption.Normal;
        if (files.All(e => e.RealMethod == 0)) return CompressionLevelOption.Store;
        var deflated = files.FirstOrDefault(e => e.RealMethod == 8);
        if (deflated is null) return CompressionLevelOption.Normal;
        return ((deflated.Flags >> 1) & 3) switch
        {
            1 => CompressionLevelOption.Maximum,
            2 or 3 => CompressionLevelOption.Fastest,
            _ => CompressionLevelOption.Normal,
        };
    }
}
