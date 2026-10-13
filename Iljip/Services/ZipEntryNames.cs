using System.Text;

namespace Iljip.Services;

/// <summary>
/// ZIP 중앙 디렉터리를 직접 읽는 곳(암호 판정 <see cref="ZipPasswordProbe"/>·압축 고치기 <see cref="Editing.ZipRawRewriter"/>)의 이름 풀기(W2-09).
/// 목록·풀기(SharpCompress + <see cref="NameCollector"/>)와 글자 하나까지 같은 이름이 나오게 같은 규칙을 쓴다:
/// <list type="bullet">
/// <item>방식 정하기(<see cref="ArchiveNameEncodingDetector"/>)에 넣는 것 = UTF-8 표시(bit 11)가 없는 항목의 이름·주석 + 압축 파일 주석(SharpCompress 가 같은 길로 푸는 것 전부).</item>
/// <item>항목 이름 = 유니코드 경로 추가 필드(0x7075)가 있으면 첫 블록의 UTF-8(판·CRC 는 보지 않음 — SharpCompress 와 같음), 없으면 정한 방식으로 푼 것.</item>
/// </list>
/// 돌려주는 이름은 엔진 이름 그대로(NFD 일 수 있음) — 화면·비교용 NFC 는 쓰는 쪽이 한다.
/// </summary>
internal static class ZipEntryNames
{
    internal const int UnicodePathExtra = 0x7075;

    /// <summary>중앙 디렉터리 기록 하나의 이름 재료.</summary>
    internal readonly record struct Raw(byte[] Name, bool Utf8Flag, string? UnicodePath, byte[]? Comment);

    /// <summary>
    /// 기록 하나에서 이름 재료를 꺼낸다(<paramref name="cd"/> = 중앙 디렉터리 바이트, <paramref name="p"/> = 기록 시작).
    /// 고정 46바이트 뒤 이름·추가 필드·주석 순서. 길이는 호출자가 이미 확인했다.
    /// </summary>
    internal static Raw Read(byte[] cd, int p, int flags, int nameLen, int extraLen, int commentLen)
    {
        int nameAt = p + 46, extraAt = nameAt + nameLen, commentAt = extraAt + extraLen;
        byte[] name = cd.AsSpan(nameAt, nameLen).ToArray();
        byte[]? comment = commentLen > 0 ? cd.AsSpan(commentAt, commentLen).ToArray() : null;
        return new Raw(name, (flags & 0x0800) != 0, UnicodePathOf(cd, extraAt, extraLen), comment);
    }

    /// <summary>추가 필드의 첫 0x7075(크기 5 이상) 블록의 UTF-8 이름(6번째 바이트부터). 없으면 null.</summary>
    internal static string? UnicodePathOf(byte[] b, int extraAt, int extraLen)
    {
        int end = extraAt + extraLen;
        for (int x = extraAt; x + 4 <= end;)
        {
            int id = b[x] | b[x + 1] << 8, size = b[x + 2] | b[x + 3] << 8;
            if (x + 4 + size > end) break;
            if (id == UnicodePathExtra && size >= 5) return Encoding.UTF8.GetString(b, x + 4 + 5, size - 5);
            x += 4 + size;
        }
        return null;
    }

    /// <summary>방식을 정하고 이름들을 푼다(<paramref name="names"/> 순서 그대로).</summary>
    internal static List<string> Decode(IReadOnlyList<Raw> names, byte[]? archiveComment)
    {
        var decoder = DecoderFor(names, archiveComment);
        var result = new List<string>(names.Count);
        foreach (var n in names)
            result.Add(n.UnicodePath ?? decoder.Decode(n.Name, 0, n.Name.Length, n.Utf8Flag));
        return result;
    }

    /// <summary>이 ZIP 의 이름 방식(UTF-8 표시가 없는 이름·주석 + 압축 파일 주석으로 정함).</summary>
    internal static NameDecoder DecoderFor(IReadOnlyList<Raw> names, byte[]? archiveComment)
    {
        var candidates = new List<byte[]>();
        foreach (var n in names)
        {
            if (n.Utf8Flag) continue;
            if (n.Name.Length > 0) candidates.Add(n.Name);
            if (n.Comment is { Length: > 0 } c) candidates.Add(c);
        }
        if (archiveComment is { Length: > 0 }) candidates.Add(archiveComment);
        return NameDecoder.For(candidates);
    }
}
