using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// '인터넷에서 받은 파일' 표식(NTFS 대체 데이터 스트림 <c>:Zone.Identifier</c> — MOTW)을 압축 파일에서 꺼낸 파일로 옮긴다(W2-04 — I1-3).
/// 메일·웹에서 받은 압축 속 문서를 일집에서 바로 열어도 오피스가 '제한된 보기'로 열게(보호 유지). 열기·미리 보기·Ctrl+C 는 꺼낸 파일마다 붙인다
/// (Ctrl+C 는 붙여넣을 때 탐색기가 파일째 복사해 표식도 따라감). 끌어내기는 탐색기가 내용 스트림으로 새 파일을 만들어 임시 파일의 표식이 옮겨지지 않으므로,
/// 끌기 데이터에 셸 형식 'ZoneIdentifier'(영역 번호 DWORD — <see cref="ParseZoneId"/>)를 함께 실어 탐색기가 붙이게 한다(실제 반영은 손 점검 MANUAL_CHECKLIST 20절).
/// 압축 파일에 표식이 없으면 아무것도 하지 않는다. NTFS 가 아니거나(USB 의 exFAT 등) 실패하면 조용히 넘어가고 로그만 남긴다.
/// IJ3-03(사용자 결정 I2-15): 일집 창·탐색기 우클릭으로 '압축 풀기'한 파일에도 붙인다 — 이번 풀기가 만든(덮어쓴) 파일만(<see cref="MarkExtractedAsync"/> —
/// 풀 곳에 원래 있던 파일·건너뛴 파일은 건드리지 않음). 내용은 원본 그대로 복사한다(ZoneId·ReferrerUrl·HostUrl 그대로 — 64KB 넘는 비정상 표식만 영역 번호로 줄임).
/// 설정 extract.keepInternetMark(기본 켬)로 끌 수 있고, 끄면 열기·끌기·Ctrl+C 에도 붙이지 않는다.
/// ★ 창 없이 풀기(--extract-to — 재사용장치가 엑셀로 자동 처리하는 첨부)에는 설정과 상관없이 붙이지 않는다: 압축 서비스는 표식을 모르고
/// (7z.dll 도 PropagateMarkOfTheWeb = false), 붙이는 일은 화면(MainViewModel·BatchExtractor)만 한다(ReuseDeviceContractTests).
/// 표식을 쓰면 NTFS 가 파일의 수정 시각을 '지금'으로 바꾸므로 원래 수정 시각으로 되돌리고, 읽기 전용 파일은 잠깐 풀었다가 되돌린다.
/// </summary>
public static class ZoneIdentifier
{
    public const string StreamName = ":Zone.Identifier";

    /// <summary>풀기 진행 창의 단계 알림(다 푼 뒤 표식을 붙이는 동안).</summary>
    internal const string StageMarking = "인터넷에서 받은 파일 표시를 붙이는 중";

    /// <summary>
    /// 표식 내용에서 영역 번호(<c>ZoneId=3</c> → 3 — 0 내 컴퓨터, 1 회사 망, 2 신뢰, 3 인터넷, 4 제한)를 읽는다. 없거나 못 읽으면 null.
    /// </summary>
    public static int? ParseZoneId(byte[] data)
    {
        string text;
        try
        {
            text = System.Text.Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (!line.StartsWith("ZoneId", StringComparison.OrdinalIgnoreCase)) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            if (int.TryParse(line[(eq + 1)..].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int zone) && zone >= 0)
                return zone;
        }
        return null;
    }

    /// <summary>
    /// 이보다 큰 표식은 그대로 옮기지 않는다(IJ3-03). Windows·브라우저가 붙이는 표식은 1KB 도 안 된다 — 누군가 일부러 키운 표식을 풀린 파일마다
    /// 복사해 디스크를 채우지 않게, 그때는 영역 번호만 담은 작은 표식으로 바꿔 보호(제한된 보기)는 그대로 둔다.
    /// </summary>
    internal const int MaxMarkBytes = 64 * 1024;

    /// <summary>
    /// 압축 파일의 표식 내용(없거나 못 읽으면 null). 보통은 바이트 그대로(ZoneId·ReferrerUrl·HostUrl), <see cref="MaxMarkBytes"/> 보다 크면
    /// <c>[ZoneTransfer] ZoneId=n</c> 만(영역을 못 읽으면 3 = 인터넷).
    /// </summary>
    public static byte[]? Read(string archivePath)
    {
        try
        {
            // File.Exists 는 대체 스트림 경로를 믿을 수 없어 바로 열고 '없음' 예외로 가른다
            using var fs = new FileStream(archivePath + StreamName, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length <= MaxMarkBytes)
            {
                var all = new byte[fs.Length];
                fs.ReadExactly(all);
                return all;
            }
            var head = new byte[MaxMarkBytes];
            int n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            int zone = ParseZoneId(head[..n]) ?? 3;
            Logger.LogError($"인터넷 표식이 너무 커서({fs.Length:N0}바이트) 영역 번호({zone})만 옮김: " + archivePath, new InvalidDataException("Zone.Identifier too large"));
            return System.Text.Encoding.ASCII.GetBytes($"[ZoneTransfer]\r\nZoneId={zone}\r\n");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("인터넷 표식(Zone.Identifier)을 읽지 못함: " + archivePath, ex);
            return null;
        }
    }

    /// <summary>설정이 켜져 있으면(<paramref name="enabled"/>) 압축 파일의 표식 내용, 아니면 null(읽지도 않음 — IJ3-03).</summary>
    public static byte[]? ReadIfEnabled(string archivePath, bool enabled) => enabled ? Read(archivePath) : null;

    /// <summary>
    /// <paramref name="archivePath"/> 에 표식이 있으면 <paramref name="extractedPath"/>(파일, 또는 폴더면 그 안의 모든 파일)에 같은 내용을 쓴다.
    /// 붙인 파일 수를 돌려준다(표식이 없으면 0).
    /// </summary>
    public static int Copy(string archivePath, string extractedPath)
    {
        var data = Read(archivePath);
        return data is null ? 0 : Apply(data, extractedPath);
    }

    /// <summary>
    /// 표식 내용을 파일(또는 폴더 안 모든 파일)에 쓴다. 실패는 로그만. <paramref name="ct"/> 는 파일마다 본다(IJ3-01 — Ctrl+C 로 큰 폴더를 복사할 때 진행 창 [취소]).
    /// </summary>
    public static int Apply(byte[] data, string path, CancellationToken ct = default)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList()
                : File.Exists(path) ? new[] { path } : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.LogError("인터넷 표식을 붙일 파일을 찾지 못함: " + path, ex);
            return 0;
        }
        return ApplyToFiles(data, files, ct);
    }

    /// <summary>표식 내용을 주어진 파일들에 쓴다(없는 파일은 건너뜀). 붙인 수를 돌려준다. 실패는 로그만.</summary>
    public static int ApplyToFiles(byte[] data, IEnumerable<string> files, CancellationToken ct = default)
    {
        int count = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            if (TryMark(f, data)) count++;
        }
        return count;
    }

    /// <summary>
    /// 파일 하나에 표식을 쓴다. NTFS 는 대체 스트림을 써도 파일의 수정 시각을 '지금'으로 바꾸므로(IJ3-03 실측) 원래 수정 시각으로 되돌린다
    /// (압축에 적힌 시각을 지키게 — 예전 열기·Ctrl+C 도 이것으로 고쳐짐). 읽기 전용 파일은 쓰는 동안만 읽기 전용을 풀었다가 되돌린다.
    /// </summary>
    private static bool TryMark(string file, byte[] data)
    {
        DateTime? lastWriteUtc = null;
        FileAttributes? readOnlyAttributes = null;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return false;
            lastWriteUtc = info.LastWriteTimeUtc;
            var attributes = info.Attributes;
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                readOnlyAttributes = attributes;
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
            File.WriteAllBytes(file + StreamName, data);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("인터넷 표식(Zone.Identifier)을 붙이지 못함: " + file, ex);
            return false;
        }
        finally
        {
            try
            {
                if (lastWriteUtc is { } t) File.SetLastWriteTimeUtc(file, t);
            }
            catch (Exception ex)
            {
                Logger.LogError("인터넷 표식을 붙인 뒤 수정 시각을 되돌리지 못함: " + file, ex);
            }
            try
            {
                if (readOnlyAttributes is { } a) File.SetAttributes(file, a);
            }
            catch (Exception ex)
            {
                Logger.LogError("인터넷 표식을 붙인 뒤 읽기 전용을 되돌리지 못함: " + file, ex);
            }
        }
    }

    /// <summary>
    /// 풀기 한 번(<paramref name="scope"/> — 화면이 연 같은 이름 처리 범위)이 만든(덮어쓴) 파일 중 지금 있는 것에 표식을 붙인다(IJ3-03 — 일집 창·우클릭 풀기).
    /// <paramref name="zone"/> 은 풀기 '전에' 읽어 둔 압축의 표식(다 풀고 압축을 휴지통으로 보내도 붙이게 — 설정이 꺼졌거나 표식이 없으면 null → 아무것도 안 함).
    /// 다 풀었을 때뿐 아니라 손상·취소로 멈춰 앞서 다 풀린 파일이 남았을 때도 부른다(남은 파일도 인터넷에서 온 내용). 암호 오류로 되돌린 파일은 이미 없다.
    /// 풀 곳에 원래 있던 파일·건너뛴 파일은 범위에 없으므로 건드리지 않는다. 뒤 스레드에서 붙이고(파일이 많아도 화면이 굳지 않게), 진행 창에는 단계 알림.
    /// 취소는 받지 않는다(멈추면 표식 없는 파일이 남음 — 파일마다 작은 쓰기라 금방 끝남). 실패해도 예외를 던지지 않는다(로그만). 붙인 파일 수를 돌려준다.
    /// </summary>
    public static async Task<int> MarkExtractedAsync(byte[]? zone, ExtractionScope scope, IProgress<ArchiveProgress>? progress = null)
    {
        if (zone is null) return 0;
        try
        {
            var files = scope.CreatedFiles;
            if (files.Count == 0) return 0;
            progress?.Report(new ArchiveProgress { CurrentFile = StageMarking, IsStage = true });
            return await Task.Run(() => ApplyToFiles(zone, files));
        }
        catch (Exception ex)
        {
            Logger.LogError("풀린 파일에 인터넷 표식을 붙이지 못함", ex);
            return 0;
        }
    }
}
