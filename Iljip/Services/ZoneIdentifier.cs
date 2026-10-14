using System.IO;

namespace Iljip.Services;

/// <summary>
/// '인터넷에서 받은 파일' 표식(NTFS 대체 데이터 스트림 <c>:Zone.Identifier</c> — MOTW)을 압축 파일에서 꺼낸 파일로 옮긴다(W2-04 — I1-3).
/// 메일·웹에서 받은 압축 속 문서를 일집에서 바로 열어도 오피스가 '제한된 보기'로 열게(보호 유지). 열기·미리 보기·Ctrl+C 는 꺼낸 파일마다 붙인다
/// (Ctrl+C 는 붙여넣을 때 탐색기가 파일째 복사해 표식도 따라감). 끌어내기는 탐색기가 내용 스트림으로 새 파일을 만들어 임시 파일의 표식이 옮겨지지 않으므로,
/// 끌기 데이터에 셸 형식 'ZoneIdentifier'(영역 번호 DWORD — <see cref="ParseZoneId"/>)를 함께 실어 탐색기가 붙이게 한다(실제 반영은 손 점검 MANUAL_CHECKLIST 20절).
/// 압축 파일에 표식이 없으면 아무것도 하지 않는다. NTFS 가 아니거나(USB 의 exFAT 등) 실패하면 조용히 넘어가고 로그만 남긴다.
/// (보통 '압축 풀기'에는 아직 옮기지 않는다 — 재사용장치 엑셀 COM 영향 확인이 먼저라 HANDOFF '보고 안건')
/// </summary>
public static class ZoneIdentifier
{
    public const string StreamName = ":Zone.Identifier";

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

    /// <summary>압축 파일의 표식 내용(없거나 못 읽으면 null).</summary>
    public static byte[]? Read(string archivePath)
    {
        try
        {
            // File.Exists 는 대체 스트림 경로를 믿을 수 없어 바로 읽고 '없음' 예외로 가른다
            return File.ReadAllBytes(archivePath + StreamName);
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
        int count = 0;
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
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.WriteAllBytes(f + StreamName, data);
                count++;
            }
            catch (Exception ex)
            {
                Logger.LogError("인터넷 표식(Zone.Identifier)을 붙이지 못함: " + f, ex);
            }
        }
        return count;
    }
}
