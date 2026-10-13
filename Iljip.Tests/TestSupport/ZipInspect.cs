using System.Buffers.Binary;
using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 시험용 ZIP 원시 읽기(본체 코드와 따로 — W2-06 압축 고치기 시험이 '손대지 않은 항목은 바이트 그대로'를 확인할 때).
/// 중앙 디렉터리를 읽고 항목마다 로컬 머리 뒤 압축된 데이터(크기 = 중앙 디렉터리의 압축 크기)를 꺼낸다. ZIP64 크기·위치도 읽는다.
/// </summary>
public static class ZipInspect
{
    public sealed record Entry(
        byte[] NameBytes, string Name, int Flags, int Method, uint Crc, ushort DosTime, ushort DosDate,
        long CompressedSize, long UncompressedSize, byte[] Data, int AesVersion, int AesStrength, int AesMethod, byte[] LocalNameBytes)
    {
        public bool Utf8 => (Flags & 0x0800) != 0;
        public bool Encrypted => (Flags & 1) != 0;
        public bool IsDir => Name.EndsWith('/');

        /// <summary>'이 항목을 고치지 않았다'의 비교 값: 이름 바이트·플래그·방식·CRC·시각·압축된 데이터.</summary>
        public string Fingerprint =>
            $"{Convert.ToHexString(NameBytes)}|{Flags}|{Method}|{Crc}|{DosTime}|{DosDate}|{CompressedSize}|{UncompressedSize}|{TreeAssert.Sha256(Data)}";
    }

    public static List<Entry> Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int eocd = -1;
        for (int i = b.Length - 22; i >= Math.Max(0, b.Length - 22 - 0xFFFF); i--)
            if (U32(b, i) == 0x06054b50) { eocd = i; break; }
        Assert.True(eocd >= 0, "끝 머리 없음: " + path);
        long count = U16(b, eocd + 10);
        long cdOffset = U32(b, eocd + 16);
        if (eocd >= 20 && U32(b, eocd - 20) == 0x07064b50)
        {
            long z64 = (long)U64(b, eocd - 20 + 8);
            Assert.Equal(0x06064b50u, U32(b, (int)z64));
            count = (long)U64(b, (int)z64 + 32);
            cdOffset = (long)U64(b, (int)z64 + 48);
        }
        var list = new List<Entry>();
        int p = (int)cdOffset;
        for (long n = 0; n < count; n++)
        {
            Assert.Equal(0x02014b50u, U32(b, p));
            int flags = U16(b, p + 8), method = U16(b, p + 10);
            ushort time = U16(b, p + 12), date = U16(b, p + 14);
            uint crc = U32(b, p + 16);
            long csize = U32(b, p + 20), usize = U32(b, p + 24);
            int nameLen = U16(b, p + 28), extraLen = U16(b, p + 30), commentLen = U16(b, p + 32);
            long offset = U32(b, p + 42);
            byte[] name = b.AsSpan(p + 46, nameLen).ToArray();
            int aesV = 0, aesS = 0, aesM = 0;
            for (int x = p + 46 + nameLen; x + 4 <= p + 46 + nameLen + extraLen;)
            {
                int id = U16(b, x), size = U16(b, x + 2), d = x + 4;
                if (id == 0x0001)
                {
                    int q = d;
                    if (usize == uint.MaxValue) { usize = (long)U64(b, q); q += 8; }
                    if (csize == uint.MaxValue) { csize = (long)U64(b, q); q += 8; }
                    if (offset == uint.MaxValue) { offset = (long)U64(b, q); }
                }
                else if (id == 0x9901)
                {
                    aesV = U16(b, d);
                    aesS = b[d + 4];
                    aesM = U16(b, d + 5);
                }
                x = d + size;
            }
            Assert.Equal(0x04034b50u, U32(b, (int)offset));
            int lName = U16(b, (int)offset + 26), lExtra = U16(b, (int)offset + 28);
            byte[] localName = b.AsSpan((int)offset + 30, lName).ToArray();
            long dataStart = offset + 30 + lName + lExtra;
            byte[] data = b.AsSpan((int)dataStart, (int)csize).ToArray();
            string decoded = (flags & 0x0800) != 0 ? Encoding.UTF8.GetString(name) : Iljip.Services.KoreanFileNameDecoder.DecodeBytes(name, 0, name.Length);
            list.Add(new Entry(name, decoded, flags, method, crc, time, date, csize, usize, data, aesV, aesS, aesM, localName));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return list;
    }

    /// <summary>
    /// 데이터 서술자(플래그 bit 3) ZIP: 로컬 머리의 CRC·크기 칸은 0, 데이터 뒤에 서술자(서명 있음/없음 번갈아). Store 방식, UTF-8 이름.
    /// 스트리밍으로 만든 ZIP(일부 휴대폰·웹 서비스·Java)의 모양.
    /// </summary>
    public static void WriteWithDataDescriptors(string path, params (string Name, byte[] Data)[] items)
    {
        const ushort dosTime = (3 << 11) | (4 << 5) | (6 / 2);
        const ushort dosDate = ((2026 - 1980) << 9) | (1 << 5) | 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var central = new List<(byte[] Name, uint Crc, int Size, long Offset)>();
        int i = 0;
        foreach (var (nameText, data) in items)
        {
            byte[] name = Encoding.UTF8.GetBytes(nameText);
            uint crc = Crc32.Compute(data);
            central.Add((name, crc, data.Length, ms.Position));
            w.Write(0x04034b50u); w.Write((ushort)20); w.Write((ushort)0x0808); w.Write((ushort)0);
            w.Write(dosTime); w.Write(dosDate);
            w.Write(0u); w.Write(0u); w.Write(0u);
            w.Write((ushort)name.Length); w.Write((ushort)0); w.Write(name); w.Write(data);
            if (i++ % 2 == 0) w.Write(0x08074b50u);   // 서명 있는 서술자와 없는 서술자를 섞는다
            w.Write(crc); w.Write((uint)data.Length); w.Write((uint)data.Length);
        }
        long cdStart = ms.Position;
        foreach (var c in central)
        {
            w.Write(0x02014b50u); w.Write((ushort)20); w.Write((ushort)20); w.Write((ushort)0x0808); w.Write((ushort)0);
            w.Write(dosTime); w.Write(dosDate);
            w.Write(c.Crc); w.Write((uint)c.Size); w.Write((uint)c.Size);
            w.Write((ushort)c.Name.Length); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0);
            w.Write(0u); w.Write((uint)c.Offset); w.Write(c.Name);
        }
        long cdEnd = ms.Position;
        w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)items.Length); w.Write((ushort)items.Length);
        w.Write((uint)(cdEnd - cdStart)); w.Write((uint)cdStart); w.Write((ushort)0);
        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    private static ulong U64(byte[] b, int at) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at));
}
