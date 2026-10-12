using System.IO.Compression;
using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>CRC-32 (ZIP·GZIP 공용, 다항식 0xEDB88320).</summary>
public static class Crc32
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Compute(byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>CRC 레지스터에 한 바이트를 넣는다(전·후 반전 없음 — ZipCrypto 키 갱신용).</summary>
    public static uint Update(uint crc, byte b) => Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
}

/// <summary>
/// ZipCrypto(전통 PKWARE 암호)의 '1바이트 암호 검사'를 우연히 통과하는 틀린 암호 찾기(B5 재현용).
/// ZipCrypto 는 12바이트 암호 머리의 마지막 바이트 하나(CRC 윗바이트, 데이터 설명자가 있으면 시각 윗바이트)로만 암호를 확인하므로
/// 틀린 암호도 약 1/256 확률로 통과한다. 픽스처를 다시 만들면(암호 머리 난수가 바뀜) 그런 암호도 바뀌므로 고정값 대신 매번 계산한다.
/// 해제 코드가 처음 여는 '첫 번째 파일 항목'(중앙 디렉터리 순서, 크기 &gt; 0)을 기준으로 한다.
/// </summary>
public static class ZipCryptoLucky
{
    /// <summary>
    /// 1바이트 검사를 통과하는 틀린 암호들 중, 지정 압축 파일을 일집으로 풀었을 때 <paramref name="outcome"/> 을 만족하는 첫 암호.
    /// (통과한 뒤에 무슨 일이 나는지는 쓰레기 Deflate 스트림이 어떻게 깨지느냐에 달려 암호마다 다르다 —
    /// 곧바로 ZlibException 이 나기도 하고, 쓰레기 내용을 '정상'처럼 풀어 쓴 뒤 다음 항목에서 '암호 불일치'로 멈추기도 한다)
    /// </summary>
    public static async Task<(string Password, Exception? Ex, string Dest)?> FindAsync(
        string zipPath, string correctPassword, TempDir tmp, Func<Exception?, string, bool> outcome, int maxCandidates = 200)
    {
        int n = 0;
        foreach (var pw in WrongPasswordsPassingCheck(zipPath, correctPassword).Take(maxCandidates))
        {
            string dest = tmp.P($"lucky_{n++}");
            var ex = await Svc.Catch(() => Svc.For(zipPath).ExtractAsync(zipPath, dest, pw));
            if (outcome(ex, dest)) return (pw, ex, dest);
        }
        return null;
    }

    public static string FindWrongPasswordPassingCheck(string zipPath, string correctPassword, string prefix = "w") =>
        WrongPasswordsPassingCheck(zipPath, correctPassword, prefix).First();

    public static IEnumerable<string> WrongPasswordsPassingCheck(string zipPath, string correctPassword, string prefix = "w")
    {
        var b = File.ReadAllBytes(zipPath);
        int eocd = -1;
        for (int i = b.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(b, i) == 0x06054b50) { eocd = i; break; }
        int count = BitConverter.ToUInt16(b, eocd + 10);
        int p = (int)BitConverter.ToUInt32(b, eocd + 16);
        for (int n = 0; n < count; n++)
        {
            int flags = BitConverter.ToUInt16(b, p + 8);
            ushort time = BitConverter.ToUInt16(b, p + 12);
            uint crc = BitConverter.ToUInt32(b, p + 16);
            uint usize = BitConverter.ToUInt32(b, p + 24);
            int nameLen = BitConverter.ToUInt16(b, p + 28), extraLen = BitConverter.ToUInt16(b, p + 30), commentLen = BitConverter.ToUInt16(b, p + 32);
            int lho = (int)BitConverter.ToUInt32(b, p + 42);
            p += 46 + nameLen + extraLen + commentLen;
            if ((flags & 1) == 0 || usize == 0) continue;

            int data = lho + 30 + BitConverter.ToUInt16(b, lho + 26) + BitConverter.ToUInt16(b, lho + 28);
            byte[] head = b[data..(data + 12)];
            byte check = (flags & 0x08) != 0 ? (byte)(time >> 8) : (byte)(crc >> 24);
            for (int i = 0; i < 10_000_000; i++)
            {
                string pw = prefix + i;
                if (pw != correctPassword && Passes(pw, head, check)) yield return pw;
            }
            yield break;
        }
        throw new InvalidDataException("ZipCrypto 항목이 없음: " + zipPath);
    }

    private static bool Passes(string password, byte[] head, byte check)
    {
        uint k0 = 0x12345678, k1 = 0x23456789, k2 = 0x34567890;
        void Upd(byte c)
        {
            k0 = Crc32.Update(k0, c);
            k1 = unchecked((k1 + (k0 & 0xFF)) * 134775813 + 1);
            k2 = Crc32.Update(k2, (byte)(k1 >> 24));
        }
        foreach (byte c in Encoding.UTF8.GetBytes(password)) Upd(c);
        byte last = 0;
        foreach (byte e in head)
        {
            uint t = (k2 | 2) & 0xFFFF;
            byte d = (byte)(e ^ (byte)((t * (t ^ 1)) >> 8));
            Upd(d);
            last = d;
        }
        return last == check;
    }
}

/// <summary>
/// 이름 바이트를 마음대로 넣을 수 있는 최소 ZIP 작성기(Store 방식만).
/// zip-slip 공격 이름('../evil.txt'), CP949 원시 이름, EFS 플래그 유무 등을 테스트 안에서 직접 만든다.
/// (일반 라이브러리는 이런 '나쁜' 이름을 막거나 고쳐 버려서 직접 쓴다.)
/// </summary>
public static class RawZip
{
    /// <param name="Compressed">null 이면 Store(방식 0)로 Data 를 그대로 쓴다. 주면 방식 8(Deflate)로 이 바이트를 데이터로 쓴다
    /// (CRC·원본 크기는 Data 기준) — 일부러 망가뜨린 Deflate 스트림을 넣을 때 쓴다.</param>
    public sealed record Item(byte[] Name, byte[] Data, bool Utf8Flag = true, byte[]? Compressed = null);

    public static Item Utf8(string name, string text) => new(Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(text), true);

    public static void Write(string path, params Item[] items)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var offsets = new List<long>();
        const ushort dosTime = (3 << 11) | (4 << 5) | (6 / 2);            // 03:04:06
        const ushort dosDate = ((2026 - 1980) << 9) | (1 << 5) | 2;       // 2026-01-02
        foreach (var it in items)
        {
            offsets.Add(ms.Position);
            byte[] payload = it.Compressed ?? it.Data;
            ushort method = (ushort)(it.Compressed is null ? 0 : 8);
            w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)(it.Utf8Flag ? 0x0800 : 0)); w.Write(method);
            w.Write(dosTime); w.Write(dosDate);
            w.Write(Crc32.Compute(it.Data)); w.Write((uint)payload.Length); w.Write((uint)it.Data.Length);
            w.Write((ushort)it.Name.Length); w.Write((ushort)0); w.Write(it.Name); w.Write(payload);
        }
        long cdStart = ms.Position;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            byte[] payload = it.Compressed ?? it.Data;
            ushort method = (ushort)(it.Compressed is null ? 0 : 8);
            w.Write(0x02014b50u); w.Write((ushort)20); w.Write((ushort)20); w.Write((ushort)(it.Utf8Flag ? 0x0800 : 0)); w.Write(method);
            w.Write(dosTime); w.Write(dosDate);
            w.Write(Crc32.Compute(it.Data)); w.Write((uint)payload.Length); w.Write((uint)it.Data.Length);
            w.Write((ushort)it.Name.Length); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u); w.Write((uint)offsets[i]); w.Write(it.Name);
        }
        long cdEnd = ms.Position;
        w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)items.Length); w.Write((ushort)items.Length);
        w.Write((uint)(cdEnd - cdStart)); w.Write((uint)cdStart); w.Write((ushort)0);
        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }
}

/// <summary>이름을 마음대로 넣을 수 있는 최소 ustar TAR 작성기(이름은 UTF-8). 기본은 일반 파일('0').</summary>
public static class RawTar
{
    /// <param name="Type">ustar 형식 문자: '0' 일반 파일, '1' 하드 링크, '2' 심볼릭 링크, '5' 폴더.</param>
    public sealed record Entry(string Name, byte[] Data, char Type = '0', string LinkName = "");

    public static void Write(string path, params (string Name, byte[] Data)[] items) =>
        Write(path, items.Select(i => new Entry(i.Name, i.Data)).ToArray());

    public static void Write(string path, params Entry[] items)
    {
        using var fs = File.Create(path);
        foreach (var (name, data, type, linkName) in items)
        {
            var h = new byte[512];
            byte[] nb = Encoding.UTF8.GetBytes(name);
            byte[] prefix = Array.Empty<byte>();
            if (nb.Length > 100)
            {
                // ustar: 앞부분은 prefix(155B), 마지막 '/' 뒤는 name(100B)
                int cut = name.LastIndexOf('/');
                if (cut <= 0) throw new ArgumentException("이름이 너무 김: " + name);
                prefix = Encoding.UTF8.GetBytes(name[..cut]);
                nb = Encoding.UTF8.GetBytes(name[(cut + 1)..]);
                if (prefix.Length > 155 || nb.Length > 100) throw new ArgumentException("이름이 너무 김: " + name);
            }
            Array.Copy(nb, 0, h, 0, nb.Length);
            Octal(h, 100, 8, Convert.ToInt32("644", 8));
            Octal(h, 108, 8, 0);
            Octal(h, 116, 8, 0);
            Octal(h, 124, 12, data.Length);
            Octal(h, 136, 12, FixtureCatalog.FixedMtime.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++) h[i] = 0x20;
            h[156] = (byte)type;
            byte[] lb = Encoding.UTF8.GetBytes(linkName);
            if (lb.Length > 100) throw new ArgumentException("링크 이름이 너무 김: " + linkName);
            Array.Copy(lb, 0, h, 157, lb.Length);
            Encoding.ASCII.GetBytes("ustar\0").CopyTo(h, 257);
            Encoding.ASCII.GetBytes("00").CopyTo(h, 263);
            Array.Copy(prefix, 0, h, 345, prefix.Length);
            int sum = h.Sum(x => x);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0')).CopyTo(h, 148);
            h[154] = 0; h[155] = 0x20;
            fs.Write(h);
            fs.Write(data);
            fs.Write(new byte[(512 - data.Length % 512) % 512]);
        }
        fs.Write(new byte[1024]);
    }

    private static void Octal(byte[] h, int off, int len, long v)
    {
        string s = Convert.ToString(v, 8).PadLeft(len - 1, '0');
        Encoding.ASCII.GetBytes(s).CopyTo(h, off);
        h[off + len - 1] = 0;
    }
}

/// <summary>원본 이름(FNAME) 칸을 넣을지 고를 수 있는 최소 GZIP 작성기.</summary>
public static class RawGz
{
    /// <param name="fnameBytes">null 이면 FNAME 없음(반디집·일집 방식), 있으면 그 바이트 그대로(리눅스 gzip 방식).</param>
    public static void Write(string path, byte[]? fnameBytes, byte[] data)
    {
        using var fs = File.Create(path);
        byte flags = (byte)(fnameBytes is null ? 0 : 0x08);
        uint mtime = (uint)FixtureCatalog.FixedMtime.ToUnixTimeSeconds();
        fs.Write(new byte[] { 0x1f, 0x8b, 8, flags });
        fs.Write(BitConverter.GetBytes(mtime));
        fs.Write(new byte[] { 0, 0xff });
        if (fnameBytes is not null) { fs.Write(fnameBytes); fs.WriteByte(0); }
        using (var ds = new DeflateStream(fs, CompressionLevel.Optimal, leaveOpen: true)) ds.Write(data);
        fs.Write(BitConverter.GetBytes(Crc32.Compute(data)));
        fs.Write(BitConverter.GetBytes((uint)data.Length));
    }
}

/// <summary>
/// '저장 블록(BTYPE=00)'만으로 된 Deflate 스트림 작성기. 블록 경계가 정확히 정해져 있어
/// "앞 블록들은 멀쩡하고 N번째 블록 머리만 깨진" 스트림을 만들 수 있다 — 해제가 '쓰는 도중'에 실패하는 경우 재현용.
/// </summary>
public static class DeflateStored
{
    public const int BlockSize = 65535;

    /// <param name="corruptBlock">0부터 센 블록 번호. 이 블록의 NLEN(LEN 의 보수)을 틀리게 써서 해제기가
    /// 'invalid stored block lengths' 로 멈추게 한다. -1 이면 멀쩡한 스트림.</param>
    public static byte[] Encode(byte[] data, int corruptBlock = -1)
    {
        using var ms = new MemoryStream();
        int blocks = Math.Max(1, (data.Length + BlockSize - 1) / BlockSize);
        for (int b = 0; b < blocks; b++)
        {
            int off = b * BlockSize;
            int len = Math.Min(BlockSize, data.Length - off);
            ms.WriteByte((byte)(b == blocks - 1 ? 1 : 0));       // BFINAL + BTYPE=00, 나머지 비트는 바이트 경계 맞춤
            ushort nlen = (ushort)~len;
            if (b == corruptBlock) nlen ^= 0x00FF;
            ms.Write(BitConverter.GetBytes((ushort)len));
            ms.Write(BitConverter.GetBytes(nlen));
            ms.Write(data, off, len);
        }
        return ms.ToArray();
    }
}

/// <summary>
/// 최소 EGG 작성기(이스트소프트 알집 형식, Store 방식만, 암호·솔리드 없음, 이름 UTF-8, 분할은 <see cref="WriteSplit"/>).
/// 반디집은 EGG 를 만들 수 없고 이 PC 에는 알집도 없어서, EGG 규격과 EggDotNet(0.3.0·0.4.0) 파서가 읽는 구조대로 직접 쓴다:
/// <code>
///   'EGGA'(0x41474745) 버전 0x0100 · 머리 ID · 예약 → 끝(0x08E28222)
///   항목마다: 파일 머리(0x0A8590E3: ID·원본 크기) · 파일 이름(0x0A8591AC: 플래그·길이·UTF-8) ·
///             윈도 파일 정보(0x2C86950B: FILETIME·속성) · 끝(0x08E28222) ·
///             [크기 &gt; 0 이면] 블록 머리(0x02B50C13: 방식 0·원본/압축 크기·CRC32·끝 0x08E28222) + 데이터
///   보관 끝(0x08E28222)
/// </code>
/// 크기 0 인 항목(폴더·빈 파일)에는 블록을 쓰지 않는다 — EggDotNet 파서는 크기 0 이면 블록을 찾지 않고,
/// 블록이 있으면 다음 항목 머리로 잘못 읽는다. 진짜 알집 산출물이 빈 파일을 이렇게 쓰는지는 확인하지 못했다(알집 없음).
/// 폴더는 이름 끝 '/' 와 윈도 속성 0x80 으로 나타낸다(일집 EggAlzArchiveService 는 둘 중 하나만 있어도 폴더로 본다 —
/// 끝 '/' 없이 속성만 있는 폴더는 <see cref="Item.DirSlash"/> = false).
/// </summary>
public static class RawEgg
{
    /// <param name="Data">null 이면 폴더(윈도 속성 0x80).</param>
    /// <param name="DirSlash">폴더 이름 끝에 '/' 를 붙일지. false 면 이름 그대로 — 폴더 속성으로만 폴더임을 알 수 있는 항목 재현용.</param>
    public sealed record Item(string Name, byte[]? Data, bool DirSlash = true)
    {
        public bool IsDir => Data is null;
    }

    public static void Write(string path, IEnumerable<Item> items, DateTime? mtimeUtc = null)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(0x41474745u); w.Write((ushort)0x0100); w.Write(0x1A2B3C4Du); w.Write(0u);
        w.Write(0x08E28222u);
        w.Write(Body(items, mtimeUtc));
    }

    /// <summary>
    /// 분할 EGG(.vol1.egg, .vol2.egg …): 머리 뒤의 본문(항목들 + 보관 끝)을 <paramref name="parts"/> 조각으로 나눠,
    /// 조각마다 '분할 머리'(0x24F5A262: 앞·다음 조각 ID)를 단 EGG 머리를 붙인다(EggDotNet 이 조각 본문을 이어 붙여 읽는 구조).
    /// 돌려주는 목록의 첫 번째가 첫 조각.
    /// </summary>
    public static List<string> WriteSplit(string basePath, IEnumerable<Item> items, int parts, DateTime? mtimeUtc = null)
    {
        byte[] body = Body(items, mtimeUtc);
        int chunk = (body.Length + parts - 1) / parts;
        var paths = new List<string>();
        for (int i = 0; i < parts; i++)
        {
            string path = $"{basePath}.vol{i + 1}.egg";
            uint id = 0x5000u + (uint)i;
            uint prev = i == 0 ? 0 : id - 1;
            uint next = i == parts - 1 ? 0 : id + 1;
            int from = Math.Min(body.Length, i * chunk), to = Math.Min(body.Length, (i + 1) * chunk);
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                WriteVolumeHeader(w, id, prev, next);
                w.Write(body, from, to - from);
            }
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>항목 없이 '분할 머리'가 달린 EGG 머리만 쓴다(조각이 빠진 분할 EGG 재현용).</summary>
    public static void WriteVolumeHeaderOnly(string path, uint headerId, uint previousId, uint nextId)
    {
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        WriteVolumeHeader(w, headerId, previousId, nextId);
        w.Write(0x08E28222u);
    }

    private static void WriteVolumeHeader(BinaryWriter w, uint headerId, uint previousId, uint nextId)
    {
        w.Write(0x41474745u); w.Write((ushort)0x0100); w.Write(headerId); w.Write(0u);
        w.Write(0x24F5A262u); w.Write((byte)0); w.Write((ushort)8); w.Write(previousId); w.Write(nextId);
        w.Write(0x08E28222u);
    }

    /// <summary>EGG 머리 뒤에 오는 본문: 항목들 + 보관 끝(0x08E28222).</summary>
    private static byte[] Body(IEnumerable<Item> items, DateTime? mtimeUtc)
    {
        long fileTime = (mtimeUtc ?? FixtureCatalog.FixedMtimeUtc).ToFileTimeUtc();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        uint id = 0;
        foreach (var it in items)
        {
            byte[] data = it.Data ?? Array.Empty<byte>();
            string name = it.IsDir && it.DirSlash && !it.Name.EndsWith('/') ? it.Name + "/" : it.Name;
            byte[] nb = Encoding.UTF8.GetBytes(name);
            if (nb.Length > 256) throw new ArgumentException("EGG 이름이 너무 김(EggDotNet 한도 256B): " + name);

            w.Write(0x0A8590E3u); w.Write(id++); w.Write((long)data.Length);
            w.Write(0x0A8591ACu); w.Write((byte)0); w.Write((ushort)nb.Length); w.Write(nb);
            w.Write(0x2C86950Bu); w.Write((byte)0); w.Write((ushort)9); w.Write(fileTime); w.Write((byte)(it.IsDir ? 0x80 : 0x20));
            w.Write(0x08E28222u);
            if (data.Length > 0)
            {
                w.Write(0x02B50C13u); w.Write((byte)0); w.Write((byte)0);
                w.Write((uint)data.Length); w.Write((uint)data.Length); w.Write(Crc32.Compute(data));
                w.Write(0x08E28222u);
                w.Write(data);
            }
        }
        w.Write(0x08E28222u);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>폴더 하나를 통째로 EGG 항목으로(부모 기준 상대경로, 폴더 항목 포함 — 알집처럼).</summary>
    public static List<Item> FromFolder(string root)
    {
        string parent = Path.GetDirectoryName(root)!;
        var list = new List<Item> { new(Path.GetFileName(root) + "/", null) };
        foreach (var p in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(parent, p).Replace('\\', '/');
            list.Add(File.Exists(p) ? new Item(rel, File.ReadAllBytes(p)) : new Item(rel + "/", null));
        }
        return list;
    }
}

/// <summary>
/// 최소 ALZ 작성기(Store 방식만, 암호 없음). unalz·EggDotNet(0.3.0·0.4.0) 이 읽는 구조:
/// <code>
///   'ALZ\1'(0x015A4C41) 버전 10 · ID
///   항목마다: 'BLZ\1'(0x015A4C42) 이름 길이(2) · 속성(1) · DOS 시각(4) · 플래그(2, 낮은 바이트 윗 4비트 = 크기 칸 바이트 수)
///             · [플래그 ≠ 0 이면] 방식(2) · CRC32(4) · 압축 크기 · 원본 크기 · 이름 · 데이터
///   'CLZ\1'(0x015A4C43) 'CLZ\2'(0x025A4C43)
/// </code>
/// 알집이 만드는 ALZ 의 이름은 시스템 코드 페이지(한국어 Windows = CP949)다(unalz 기준). 이름 바이트는 호출자가 고른다.
/// </summary>
public static class RawAlz
{
    public sealed record Item(byte[] Name, byte[]? Data)
    {
        public bool IsDir => Data is null;
    }

    public static void Write(string path, params Item[] items)
    {
        const uint dosDate = ((2026u - 1980u) << 9) | (1u << 5) | 2u;      // 2026-01-02
        const uint dosTime = (3u << 11) | (4u << 5) | (6u / 2u);           // 03:04:06
        const uint dos = (dosDate << 16) | dosTime;
        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);
        w.Write(0x015A4C41u); w.Write((ushort)10); w.Write((ushort)0);
        foreach (var it in items)
        {
            byte[] data = it.Data ?? Array.Empty<byte>();
            w.Write(0x015A4C42u);
            w.Write((ushort)it.Name.Length); w.Write((byte)(it.IsDir ? 0x10 : 0x20)); w.Write(dos);
            if (data.Length == 0)
            {
                w.Write((ushort)0);           // 크기 정보 없음
            }
            else
            {
                w.Write((ushort)0x0040);      // 크기 칸 4바이트
                w.Write((ushort)0);           // 방식 0 = 저장
                w.Write(Crc32.Compute(data)); w.Write((uint)data.Length); w.Write((uint)data.Length);
            }
            w.Write(it.Name);
            w.Write(data);
        }
        w.Write(0x015A4C43u); w.Write(0x025A4C43u);
    }
}
