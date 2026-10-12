using System.IO;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 7z.dll 이 임시 폴더에 푼 결과를 항목마다 맞춰 본다(W2-01 — 1차 W1-B 의 '길이만 비교'를 대신함).
/// <list type="bullet">
/// <item>고른 파일마다: 파일이 있는가(크기 0 항목은 엔진이 안 만들었어도 됨), 크기가 목록의 크기와 같은가.</item>
/// <item>LZH 는 늘 CRC-16/ARC(다항식 0xA001 — 7z.dll 이 주는 LZH 확인값, 실측)를 다시 계산한다. 반디집이 만든 정상 LZH 에서 7z.dll 이
///   실패를 알리는 일이 있어(거짓 오류, lzh_default 실측) 그것을 가려 낸다.</item>
/// <item>그 밖의 형식은 7z.dll 이 그 항목을 실패로 알렸을 때만 다시 계산해 거짓 오류인지 가린다: 7z·ZIP(ZIPX)·RAR4·암호 없는 RAR5 = CRC32
///   (암호 RAR5 의 확인값은 암호에 묶인 값이라 비교할 수 없다). ISO·UDF 는 크기만 본다(정상 이미지에도 오류 표시가 뜸 — 실측).
///   CAB·WIM·XZ 는 7z.dll 의 실패를 그대로 믿는다.</item>
/// </list>
/// 모든 항목이 맞으면 7z.dll 의 실패 보고는 로그만 남기고 성공으로 친다. 하나라도 틀리면 호출자(<see cref="SevenZipEngine"/>)가 손상으로 알린다
/// (틀린 파일은 절대 옮기지 않음 — 확인값 오류만 main 의 ZIP·RAR 계약대로 그 앞에서 맞게 풀린 파일을 남긴다).
/// </summary>
internal static class ExtractionVerifier
{
    internal enum Problem
    {
        /// <summary>문제없음(7z.dll 이 끝냈고 크기·확인값도 맞음, 또는 거짓 오류를 확인값으로 가려 냄).</summary>
        None,
        /// <summary>7z.dll 이 그 항목을 끝내지 못했다(확인값으로도 살릴 수 없음).</summary>
        EngineFailed,
        /// <summary>7z.dll 은 끝났다고 했는데 파일이 없거나 크기가 다르다.</summary>
        SizeMismatch,
        /// <summary>다시 계산한 확인값이 다르다(LZH 는 늘, 암호 없는 ZIP·RAR 은 7z.dll 이 실패로 알린 항목의 크기는 맞을 때).</summary>
        ChecksumMismatch,
    }

    /// <param name="format">실제로 연 형식(대신 형식으로 열었을 수 있음).</param>
    /// <param name="stagedPath">임시 폴더 안의 그 항목 파일(없을 수 있음).</param>
    /// <param name="finished">7z.dll 이 그 항목을 성공으로 끝냈다고 알렸는가(FileExtractionFinished).</param>
    public static Problem Check(InArchiveFormat format, SevenZipEngine.Item item, string stagedPath, bool finished)
    {
        long len = File.Exists(stagedPath) ? new FileInfo(stagedPath).Length : -1;
        if (len < 0)
        {
            // 크기 0 항목은 7z.dll 이 파일을 만들지 않을 수 있다(옮길 때 빈 파일을 만든다). LZH 의 빈 -lh7- 항목은 실패로 알려 와도 맞다(실측).
            if (item.Size == 0 && (finished || format == InArchiveFormat.Lzh)) return Problem.None;
            return finished ? Problem.SizeMismatch : Problem.EngineFailed;
        }
        // xz 는 7z.dll 이 풀린 크기를 모를 때 0 으로 준다(끝의 색인을 못 읽은 경우 — 실측) → 그때는 7z.dll 의 '끝남'(xz 자체 확인값 검사 포함)을 믿는다
        bool sizeUnknown = format == InArchiveFormat.XZ && item.Size == 0;
        if (len != item.Size && !(sizeUnknown && finished)) return finished ? Problem.SizeMismatch : Problem.EngineFailed;

        if (format == InArchiveFormat.Lzh)
            return Crc16Arc(stagedPath) == (item.Crc & 0xFFFF) ? Problem.None : finished ? Problem.ChecksumMismatch : Problem.EngineFailed;

        if (finished) return Problem.None;

        // 여기부터는 7z.dll 이 실패로 알린 항목 — 크기는 맞다. 확인값으로 거짓 오류인지 가린다.
        switch (format)
        {
            case InArchiveFormat.Iso or InArchiveFormat.Udf:
                return Problem.None;
            case InArchiveFormat.SevenZip or InArchiveFormat.Zip or InArchiveFormat.Rar4:
            case InArchiveFormat.Rar when !item.Encrypted:
                // 확인값이 없는 항목(크기 > 0 인데 0 — ZIP AES 의 AE-2 등)은 가릴 수 없어 실패를 믿는다
                if (item.Crc == 0 && item.Size > 0) return Problem.EngineFailed;
                if (Crc32Of(stagedPath) == item.Crc) return Problem.None;
                // 암호 없는 ZIP·RAR 항목이 끝까지 나왔는데 확인값만 다르다 = 그 파일의 데이터 손상(main 의 ZIP·RAR 'CRC 오류' 계약과 같게).
                // 암호 항목은 틀린 암호일 수 있어, 7z 는 기록된 문구(손상)대로 엔진 실패로 둔다.
                return !item.Encrypted && format is InArchiveFormat.Zip or InArchiveFormat.Rar4 or InArchiveFormat.Rar
                    ? Problem.ChecksumMismatch
                    : Problem.EngineFailed;
            default:
                return Problem.EngineFailed;
        }
    }

    /// <summary>CRC-16/ARC(반사, 다항식 0xA001, 시작값 0) — LZH 의 확인값.</summary>
    internal static uint Crc16Arc(string path)
    {
        uint crc = 0;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        var buf = new byte[81920];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                crc ^= buf[i];
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1;
            }
        }
        return crc;
    }

    internal static uint Crc32Of(string path)
    {
        var c = new ICSharpCode.SharpZipLib.Checksum.Crc32();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        var buf = new byte[81920];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            c.Update(new ArraySegment<byte>(buf, 0, n));
        return (uint)c.Value;
    }
}
