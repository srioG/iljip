using System.IO;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// ALZ(옛 알집 형식)의 항목 머리 '원본 바이트'(이름·속성)를 직접 읽는 최소 스캐너(B10 우회 + 폴더 판별).
///
/// EggDotNet 은 ALZ 이름을 UTF-8 로만 읽어, 알집이 시스템 코드 페이지(한국어 Windows = CP949)로 쓴 한글 이름이
/// U+FFFD(�)로 깨진다(되돌릴 수 없음). 또 ALZ 의 파일 속성(폴더 표시 0x10)은 읽지 않고 버린다(GetExtraAttributes 가 늘 0).
/// 원본 바이트를 얻을 공개 API 가 없어 파일을 처음부터 직접 훑는다.
/// 구조(unalz 기준):
/// <code>
///   'ALZ\1' + 4바이트
///   항목마다: 'BLZ\1' 이름길이(2) 속성(1) DOS시각(4) 설명자(1) 미상(1)
///             [설명자 윗 4비트 = 크기 칸 바이트 수 n &gt; 0 이면] 방식(1) 미상(1) CRC(4) 압축크기(n) 원래크기(n)
///             이름 · [암호 항목(설명자 bit0)이면 12바이트 암호 머리] · 데이터(압축크기)
///   'CLZ\1' [4바이트 칸 …] 'CLZ\2' (끝 — EggDotNet 도 'CLZ\2' 를 끝으로 봄)
/// </code>
/// 모르는 서명·말이 안 되는 크기 칸이면 null 을 돌려 호출자가 EggDotNet 값을 그대로 쓰게 한다 — 최악이라도 지금과 같다.
/// 파일이 중간에 끊긴 경우(받다 만 파일)는 끊기기 전까지 '온전히 읽은' 항목만 돌려준다(<see cref="AlzScan.Complete"/> = false).
/// </summary>
internal static class AlzNameScanner
{
    private const uint AlzMagic = 0x015A4C41;   // 'ALZ\1'
    private const uint EntryMagic = 0x015A4C42; // 'BLZ\1'
    private const uint EndMagic = 0x015A4C43;   // 'CLZ\1'
    private const int EncryptHeaderLength = 12;
    private const byte DirectoryAttribute = 0x10;   // DOS FILE_ATTRIBUTE_DIRECTORY

    /// <summary>항목 하나의 머리 원본: 이름 바이트와 속성 바이트.</summary>
    internal sealed record AlzRawEntry(byte[] Name, byte Attributes)
    {
        public bool IsDirectory => (Attributes & DirectoryAttribute) != 0;
    }

    /// <summary>훑은 결과. <see cref="Complete"/> 는 끝 표시('CLZ')까지 읽었는지 — false 면 파일이 중간에 끊겼다.</summary>
    internal sealed record AlzScan(IReadOnlyList<AlzRawEntry> Entries, bool Complete);

    /// <summary>
    /// ALZ 파일의 항목 머리를 파일 순서대로 읽는다. ALZ 가 아니거나(EGG 등) 구조가 기대와 다르면 null.
    /// 파일이 끊겼으면 이름까지 온전히 읽은 항목만 담고 Complete=false.
    /// </summary>
    /// <param name="start">ALZ 가 시작하는 위치(자동 풀림 exe 안이면 그 위치, 보통 0).</param>
    internal static AlzScan? TryScan(string alzPath, long start = 0)
    {
        try
        {
            // 분할 ALZ(.a00, .a01 …)는 이름이 조각에 걸칠 수 있어 다루지 않는다(EggDotNet 도 분할 ALZ 미지원).
            string? dir = Path.GetDirectoryName(Path.GetFullPath(alzPath));
            string stem = Path.GetFileNameWithoutExtension(alzPath);
            if (dir is not null && File.Exists(Path.Combine(dir, stem + ".a00")))
                return null;

            using var fs = new FileStream(alzPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new BinaryReader(fs);
            long length = fs.Length;
            if (start < 0 || start > length) return null;
            fs.Position = start;
            if (length - start < 8 || r.ReadUInt32() != AlzMagic) return null;
            r.ReadUInt32();   // 버전·ID

            var entries = new List<AlzRawEntry>();
            AlzScan Cut() => new(entries, Complete: false);
            while (true)
            {
                if (fs.Position + 4 > length) return Cut();           // 끝 표시 없이 끝남 = 잘림
                uint sig = r.ReadUInt32();
                if (sig == EndMagic) return EndMarkFollows(fs, r, length) ? new AlzScan(entries, Complete: true) : Cut();
                if (sig != EntryMagic) return null;                    // 모르는 서명

                if (fs.Position + 9 > length) return Cut();           // 항목 머리 중간에서 끊김
                int nameLength = r.ReadUInt16();
                byte attributes = r.ReadByte();
                r.ReadUInt32();                                        // DOS 시각
                byte descriptor = r.ReadByte();
                r.ReadByte();                                          // 미상

                int sizeBytes = descriptor >> 4;
                long compressedSize = 0;
                if (sizeBytes > 0)
                {
                    if (sizeBytes is not (1 or 2 or 4 or 8)) return null;
                    if (fs.Position + 6 + 2L * sizeBytes > length) return Cut();
                    r.ReadByte();                                      // 방식
                    r.ReadByte();                                      // 미상
                    r.ReadUInt32();                                    // CRC
                    compressedSize = ReadSize(r, sizeBytes);
                    ReadSize(r, sizeBytes);                            // 원래 크기
                    if (compressedSize < 0) return null;
                }

                if (nameLength <= 0) return null;
                if (fs.Position + nameLength > length) return Cut();  // 이름 중간에서 끊김 — 이 항목은 버림
                entries.Add(new AlzRawEntry(r.ReadBytes(nameLength), attributes));

                long skip = compressedSize + ((descriptor & 0x01) != 0 ? EncryptHeaderLength : 0);
                if (fs.Position + skip > length) return Cut();        // 데이터가 잘림(이름은 온전함)
                fs.Seek(skip, SeekOrigin.Current);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 'CLZ\1' 뒤에 마지막 끝 표시 'CLZ\2'(0x025A4C43)가 있는가(IJ3-02 (a) — 예전: 'CLZ\1' 만 보고 온전하다고 봐, 'CLZ\2' 안·앞에서 잘린 ALZ 를 '문제없어요').
    /// unalz·EggDotNet 처럼 4바이트씩 넘기며 찾는다(사이에 낀 칸이 있을 수 있음). 찾으면 true, 찾기 전에 파일이 끝나면 false(잘림).
    /// <see cref="EndSearchWords"/> 칸 안에 없고 뒤가 더 있으면 모르는 꼬리 — 온전하다고 본다(예전과 같게, 잘못 '잘림'이라 하지 않게).
    /// </summary>
    private static bool EndMarkFollows(FileStream fs, BinaryReader r, long length)
    {
        for (int i = 0; i < EndSearchWords; i++)
        {
            if (fs.Position + 4 > length) return false;
            if (r.ReadUInt32() == FinalMagic) return true;
        }
        return true;
    }

    private const uint FinalMagic = 0x025A4C43;   // 'CLZ\2'
    private const int EndSearchWords = 16;

    /// <summary>ALZ 파일의 항목 이름 원본 바이트를 파일 순서대로. 끝까지 온전한 ALZ 가 아니면(잘림 포함) null.</summary>
    internal static IReadOnlyList<byte[]>? TryReadNames(string alzPath) =>
        TryScan(alzPath) is { Complete: true } scan ? scan.Entries.Select(e => e.Name).ToList() : null;

    /// <summary>
    /// EggDotNet 이 준 이름(<paramref name="libraryNames"/>, 순서대로)과 스캐너가 읽은 항목 머리를 짝짓는다.
    /// 다음을 모두 만족할 때만 짝지은 목록을 돌려주고, 아니면 null(= EggDotNet 값 그대로 쓸 것):
    ///   - ALZ 이고 스캐너가 읽은 항목 수가 EggDotNet 과 같음(끊긴 파일이어도 EggDotNet 이 보여 주는 항목 수와 같으면 됨 —
    ///     EggDotNet 도 이름까지 온전한 항목만 보여 준다)
    ///   - 항목마다 '같은 항목'임이 확인됨: 원본 바이트를 UTF-8 로 읽은 결과가 EggDotNet 이름과 같거나,
    ///     적어도 ASCII 부분(손실 문자 자리를 뺀 나머지)이 같음
    /// </summary>
    internal static IReadOnlyList<AlzRawEntry>? TryMatch(string alzPath, IReadOnlyList<string> libraryNames, long start = 0)
    {
        var scan = TryScan(alzPath, start);
        if (scan is null || scan.Entries.Count != libraryNames.Count) return null;
        for (int i = 0; i < scan.Entries.Count; i++)
            if (!SameEntry(scan.Entries[i].Name, libraryNames[i])) return null;
        return scan.Entries;
    }

    /// <summary>
    /// 깨진 이름(U+FFFD)이 하나라도 있으면 원본 바이트로 다시 푼 이름 목록을, 고칠 게 없거나 짝이 확인되지 않으면 null.
    /// </summary>
    internal static IReadOnlyList<string>? TryRecoverNames(string alzPath, IReadOnlyList<string> libraryNames)
    {
        if (!libraryNames.Any(n => n.Contains('�'))) return null;
        return TryMatch(alzPath, libraryNames)?.Select(e => DecodeName(e.Name)).ToList();
    }

    /// <summary>
    /// ALZ 이름 바이트 → 문자열. ALZ 이름은 알집이 시스템 코드 페이지(한국어 Windows = CP949)로 쓰므로:
    /// 올바른 UTF-8 이면 UTF-8(ASCII 포함), 아니면 CP949 로 '빈틈없이' 풀리면 CP949 — 한글이 없는 한자·기호 이름('漢字①.txt')도 맞게.
    /// 둘 다 아니면 CP437(바이트 1:1 보존) — <see cref="KoreanFileNameDecoder.DecodeBytes"/> 와 같은 마지막 단계.
    /// (ZIP 용 DecodeBytes 는 CP949 결과에 한글이 있어야 채택해서, 한자·기호만 있는 ALZ 이름이 CP437 로 깨졌다)
    /// '빈틈없이' = 끊긴 2바이트 글자가 없고(예외), 풀린 글자에 제어 문자(U+0080~U+009F)나 사용자 정의 영역(U+E000~U+F8FF)이 없음 —
    /// .NET CP949 는 홑바이트 0x80·0xFF 와 사용자 정의 영역을 이런 글자로 '풀어' 버려서, CP949 가 아닌 이름을 걸러 내려면 따로 봐야 한다.
    /// </summary>
    internal static string DecodeName(byte[] raw)
    {
        if (System.Text.Unicode.Utf8.IsValid(raw))
            return Encoding.UTF8.GetString(raw);
        try
        {
            string cp949 = StrictCp949.GetString(raw);
            if (!cp949.Any(c => c is >= '\u0080' and <= '\u009F' or >= '' and <= ''))
                return cp949;
        }
        catch (DecoderFallbackException)
        {
            // 끊긴 2바이트 글자 — CP949 가 아님
        }
        return KoreanFileNameDecoder.DecodeBytes(raw, 0, raw.Length);
    }

    private static Encoding? _strictCp949;

    /// <summary>틀린 바이트를 '?' 로 바꾸지 않고 예외를 내는 CP949(처음 쓸 때 만듦 — 코드 페이지 등록은 App.OnStartup).</summary>
    private static Encoding StrictCp949 =>
        _strictCp949 ??= Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static bool SameEntry(byte[] raw, string libraryName)
    {
        if (Encoding.UTF8.GetString(raw) == libraryName) return true;
        var asciiRaw = raw.Where(b => b < 0x80).Select(b => (char)b);
        var asciiLib = libraryName.Where(c => c < 0x80);
        return asciiRaw.SequenceEqual(asciiLib);
    }

    private static long ReadSize(BinaryReader r, int bytes) => bytes switch
    {
        1 => r.ReadByte(),
        2 => r.ReadUInt16(),
        4 => r.ReadUInt32(),
        _ => r.ReadInt64(),
    };
}
