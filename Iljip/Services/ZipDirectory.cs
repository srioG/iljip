using System.Buffers.Binary;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// ZIP 끝 머리(EOCD·ZIP64 끝 머리·위치 표)와 중앙 디렉터리 기록을 읽고 다시 쓰는 도우미(W2-03 — 나눔 ZIP 쓰기 <see cref="ZipSpanWriter"/>·
/// 읽기 <see cref="SpannedZipStream"/> 가 같이 씀). 중앙 디렉터리 기록은 '시작 디스크 번호'와 '로컬 머리 위치'만 바꿔 다시 쓴다 —
/// 나머지 바이트(이름·플래그·암호 표시·AES 추가 필드 0x9901·주석)는 그대로. 크기·위치가 ZIP64 추가 필드(0x0001)에 들어 있으면 그 칸을 새로 만든다
/// (칸의 차례가 '가득 찬(0xFFFFFFFF) 원래 칸'에 따라 정해지므로, 위치 칸이 바뀌면 추가 필드를 통째로 다시 써야 한다).
/// 잘못된 모양이면 <see cref="InvalidDataException"/>.
/// </summary>
internal static class ZipDirectory
{
    internal const uint LocalHeaderSig = 0x04034b50;
    internal const uint CentralSig = 0x02014b50;
    internal const uint EndSig = 0x06054b50;
    internal const uint Zip64EndSig = 0x06064b50;
    internal const uint Zip64LocatorSig = 0x07064b50;

    /// <summary>PKZIP 나눔의 첫 조각 맨 앞 4바이트(<c>50 4B 07 08</c> — 데이터 서술자 서명과 같은 값). 바로 뒤에 첫 파일의 로컬 머리가 온다.</summary>
    internal const uint SpanSig = 0x08074b50;

    /// <summary>나누려다 한 조각에 다 들어간 '임시 나눔' 표시(<c>PK00</c>) — 읽을 때만 나눔 표시와 같게 본다.</summary>
    internal const uint TempSpanSig = 0x30304b50;

    internal const int LocalHeaderLength = 30;
    internal const int CentralFixedLength = 46;
    internal const int EndLength = 22;
    internal const int Zip64EndLength = 56;
    internal const int Zip64LocatorLength = 20;

    /// <summary>끝 머리에서 읽은 값. ZIP64 위치 표가 있으면 <see cref="ApplyZip64"/> 로 ZIP64 끝 머리의 값으로 바꾼다.</summary>
    internal sealed class End
    {
        /// <summary>끝 머리(EOCD) 위치(읽은 스트림 안).</summary>
        public long EndPosition { get; init; }
        public uint ThisDisk { get; set; }
        public uint CdDisk { get; set; }
        public long EntriesOnDisk { get; set; }
        public long TotalEntries { get; set; }
        public long CdSize { get; set; }
        /// <summary>중앙 디렉터리가 시작하는 디스크(<see cref="CdDisk"/>) 안의 위치.</summary>
        public long CdOffset { get; set; }
        public byte[] Comment { get; init; } = Array.Empty<byte>();

        /// <summary>끝 머리 바로 앞에 ZIP64 위치 표가 있는가.</summary>
        public bool HasZip64Locator { get; init; }
        /// <summary>ZIP64 끝 머리가 있는 디스크와 그 안의 위치(위치 표 값).</summary>
        public uint Zip64Disk { get; init; }
        public long Zip64Offset { get; init; }
        /// <summary>위치 표의 '전체 디스크 수'.</summary>
        public uint LocatorTotalDisks { get; init; }
        /// <summary>읽은 ZIP64 끝 머리의 위치(읽은 스트림 안). 안 읽었으면 -1.</summary>
        public long Zip64Position { get; set; } = -1;

        /// <summary>조각(디스크) 수 — 위치 표가 있으면 그 값, 없으면 '이 디스크 번호 + 1'. 보통 ZIP 은 1.</summary>
        public long TotalDisks => HasZip64Locator ? LocatorTotalDisks : (long)ThisDisk + 1;

        /// <summary>중앙 디렉터리가 끝나야 하는 곳(ZIP64 끝 머리 또는 끝 머리 위치).</summary>
        public long DirectoryLimit => Zip64Position >= 0 ? Zip64Position : HasZip64Locator ? EndPosition - Zip64LocatorLength : EndPosition;
    }

    /// <summary>
    /// 스트림 끝(주석 최대 64KB)에서 끝 머리를 찾는다. 없으면 null. ZIP64 끝 머리는 읽지 않는다(<see cref="ApplyZip64"/>).
    /// <paramref name="lenient"/> 면 주석 길이 칸이 파일 끝을 넘는(그 칸이 깨진) 끝 머리도 받는다 — 맨 뒤의 끝 머리 서명(주석은 파일 끝까지).
    /// 압축 엔진(SharpCompress)은 그 칸을 보지 않고 연다 — 목록 구조 검사가 엔진과 같은 끝 머리를 보게(IJ3-02 5차).
    /// </summary>
    internal static End? FindEnd(Stream s, bool lenient = false)
    {
        long len = s.Length;
        if (len < EndLength) return null;
        int tailLen = (int)Math.Min(len, EndLength + 0xFFFF);
        long tailStart = len - tailLen;
        byte[] tail = ReadAt(s, tailStart, tailLen);
        int at = -1, loose = -1;
        for (int i = tailLen - EndLength; i >= 0; i--)
        {
            if (U32(tail, i) != EndSig) continue;
            if (loose < 0) loose = i;
            if (i + EndLength + U16(tail, i + 20) <= tailLen) { at = i; break; }
        }
        if (at < 0 && lenient) at = loose;
        if (at < 0) return null;
        long endPos = tailStart + at;
        int commentLen = Math.Min(U16(tail, at + 20), tailLen - at - EndLength);

        bool hasLocator = false;
        uint z64Disk = 0, totalDisks = 0;
        long z64Off = 0;
        if (endPos >= Zip64LocatorLength)
        {
            byte[] loc = ReadAt(s, endPos - Zip64LocatorLength, Zip64LocatorLength);
            if (U32(loc, 0) == Zip64LocatorSig)
            {
                hasLocator = true;
                z64Disk = U32(loc, 4);
                z64Off = (long)Math.Min(U64(loc, 8), long.MaxValue);
                totalDisks = U32(loc, 16);
            }
        }
        return new End
        {
            EndPosition = endPos,
            ThisDisk = U16(tail, at + 4),
            CdDisk = U16(tail, at + 6),
            EntriesOnDisk = U16(tail, at + 8),
            TotalEntries = U16(tail, at + 10),
            CdSize = U32(tail, at + 12),
            CdOffset = U32(tail, at + 16),
            Comment = tail.AsSpan(at + EndLength, commentLen).ToArray(),
            HasZip64Locator = hasLocator,
            Zip64Disk = z64Disk,
            Zip64Offset = z64Off,
            LocatorTotalDisks = totalDisks,
        };
    }

    /// <summary>ZIP64 끝 머리(<paramref name="position"/> — 읽는 스트림 안 위치)의 값으로 바꾼다. 모양이 틀리면 <see cref="InvalidDataException"/>.</summary>
    internal static void ApplyZip64(Stream s, End end, long position)
    {
        if (position < 0 || position + Zip64EndLength > end.EndPosition - Zip64LocatorLength)
            throw new InvalidDataException("ZIP64 끝 머리 위치가 맞지 않음");
        byte[] z = ReadAt(s, position, Zip64EndLength);
        if (U32(z, 0) != Zip64EndSig) throw new InvalidDataException("ZIP64 끝 머리를 찾지 못함");
        end.ThisDisk = U32(z, 16);
        end.CdDisk = U32(z, 20);
        end.EntriesOnDisk = ToLong(U64(z, 24));
        end.TotalEntries = ToLong(U64(z, 32));
        end.CdSize = ToLong(U64(z, 40));
        end.CdOffset = ToLong(U64(z, 48));
        end.Zip64Position = position;
    }

    /// <summary>중앙 디렉터리 기록 하나(읽은 바이트 그대로 + ZIP64 까지 반영한 크기·위치·디스크).</summary>
    internal sealed class CentralRecord
    {
        public required byte[] Raw { get; init; }
        public int NameLength { get; init; }
        public int ExtraLength { get; init; }
        public int CommentLength { get; init; }
        public long CompressedSize { get; init; }
        public long UncompressedSize { get; init; }
        public long LocalOffset { get; init; }
        public long DiskStart { get; init; }

        /// <summary>
        /// 시작 디스크와 로컬 머리 위치만 바꾼 기록. 원래 가득 찬(ZIP64) 크기 칸은 그대로 ZIP64 추가 필드에 두고,
        /// 새 위치가 4GB 이상이면 위치도 ZIP64 칸으로. 다른 추가 필드(AES 0x9901 등)는 그대로 뒤에 붙인다.
        /// </summary>
        public byte[] Rewrite(long disk, long offset)
        {
            var fixedPart = Raw.AsSpan(0, CentralFixedLength).ToArray();
            bool usize64 = U32(fixedPart, 24) == uint.MaxValue;
            bool csize64 = U32(fixedPart, 20) == uint.MaxValue;
            bool offset64 = offset >= uint.MaxValue;
            bool disk64 = disk >= ushort.MaxValue;
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(34), disk64 ? ushort.MaxValue : (ushort)disk);
            BinaryPrimitives.WriteUInt32LittleEndian(fixedPart.AsSpan(42), offset64 ? uint.MaxValue : (uint)offset);

            using var extra = new MemoryStream();
            var z64 = new List<byte>();
            if (usize64) z64.AddRange(BitConverter.GetBytes((ulong)UncompressedSize));
            if (csize64) z64.AddRange(BitConverter.GetBytes((ulong)CompressedSize));
            if (offset64) z64.AddRange(BitConverter.GetBytes((ulong)offset));
            if (disk64) z64.AddRange(BitConverter.GetBytes((uint)disk));
            if (z64.Count > 0)
            {
                extra.Write(BitConverter.GetBytes((ushort)0x0001));
                extra.Write(BitConverter.GetBytes((ushort)z64.Count));
                extra.Write(z64.ToArray());
            }
            int extraAt = CentralFixedLength + NameLength;
            int extraEnd = extraAt + ExtraLength;
            int x = extraAt;
            while (x + 4 <= extraEnd)
            {
                int id = U16(Raw, x), size = U16(Raw, x + 2);
                if (x + 4 + size > extraEnd) break;
                if (id != 0x0001) extra.Write(Raw, x, 4 + size);
                x += 4 + size;
            }
            if (x < extraEnd) extra.Write(Raw, x, extraEnd - x);   // 모양이 어긋난 꼬리 바이트는 그대로
            if (extra.Length > ushort.MaxValue) throw new InvalidDataException("ZIP 추가 필드가 너무 김");
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(30), (ushort)extra.Length);

            var result = new byte[CentralFixedLength + NameLength + extra.Length + CommentLength];
            fixedPart.CopyTo(result, 0);
            Raw.AsSpan(CentralFixedLength, NameLength).CopyTo(result.AsSpan(CentralFixedLength));
            extra.ToArray().CopyTo(result, CentralFixedLength + NameLength);
            Raw.AsSpan(extraEnd, CommentLength).CopyTo(result.AsSpan(CentralFixedLength + NameLength + (int)extra.Length));
            return result;
        }
    }

    /// <summary>중앙 디렉터리 바이트에서 기록 <paramref name="count"/> 개를 읽는다.</summary>
    internal static List<CentralRecord> ParseCentral(byte[] cd, long count)
    {
        if (count < 0 || count > cd.Length / CentralFixedLength + 1) throw new InvalidDataException("ZIP 항목 수가 맞지 않음");
        var list = new List<CentralRecord>((int)count);
        int p = 0;
        for (long n = 0; n < count; n++)
            list.Add(TryParseRecord(cd, ref p) ?? throw new InvalidDataException("ZIP 중앙 디렉터리 기록이 깨짐"));
        return list;
    }

    /// <summary>
    /// 중앙 디렉터리 바이트를 앞에서부터 읽을 수 있는 데까지 읽는다(W2-08 검증 지적 2차 — 깨진 목록의 앞부분이라도 쓰려고: ZIP 고치기·압축 검사).
    /// 서명이 틀리거나 길이가 넘치는 기록을 만나면 거기서 멈춘다(예외 없음). <paramref name="consumed"/> = 읽은 기록들이 차지한 바이트 수.
    /// </summary>
    internal static List<CentralRecord> ParseCentralPrefix(byte[] cd, out int consumed)
    {
        var list = new List<CentralRecord>();
        int p = 0;
        try
        {
            while (TryParseRecord(cd, ref p) is { } r) list.Add(r);
        }
        catch (InvalidDataException)
        {
            // ZIP64 값이 터무니없이 큰 기록 — 거기서 멈춘다
        }
        consumed = p;
        return list;
    }

    /// <summary><paramref name="p"/> 자리의 기록 하나(읽으면 <paramref name="p"/> 를 다음 기록으로 옮김). 서명이 틀리거나 넘치면 null.</summary>
    private static CentralRecord? TryParseRecord(byte[] cd, ref int p)
    {
        if (p + CentralFixedLength > cd.Length || U32(cd, p) != CentralSig) return null;
        long csize = U32(cd, p + 20), usize = U32(cd, p + 24), offset = U32(cd, p + 42), disk = U16(cd, p + 34);
        int nameLen = U16(cd, p + 28), extraLen = U16(cd, p + 30), commentLen = U16(cd, p + 32);
        int extraAt = p + CentralFixedLength + nameLen, next = extraAt + extraLen + commentLen;
        if (next > cd.Length) return null;
        for (int x = extraAt; x + 4 <= extraAt + extraLen;)
        {
            int id = U16(cd, x), size = U16(cd, x + 2), d = x + 4;
            if (d + size > extraAt + extraLen) break;
            if (id == 0x0001)
            {
                // ZIP64: 원래 칸이 가득(0xFFFFFFFF/0xFFFF)인 값만 이 차례로 들어 있다
                int q = d, qEnd = d + size;
                if (usize == uint.MaxValue && q + 8 <= qEnd) { usize = ToLong(U64(cd, q)); q += 8; }
                if (csize == uint.MaxValue && q + 8 <= qEnd) { csize = ToLong(U64(cd, q)); q += 8; }
                if (offset == uint.MaxValue && q + 8 <= qEnd) { offset = ToLong(U64(cd, q)); q += 8; }
                if (disk == ushort.MaxValue && q + 4 <= qEnd) disk = U32(cd, q);
            }
            x = d + size;
        }
        var record = new CentralRecord
        {
            Raw = cd.AsSpan(p, next - p).ToArray(),
            NameLength = nameLen,
            ExtraLength = extraLen,
            CommentLength = commentLen,
            CompressedSize = csize,
            UncompressedSize = usize,
            LocalOffset = offset,
            DiskStart = disk,
        };
        p = next;
        return record;
    }

    /// <summary>
    /// 끝 머리(필요하면 ZIP64 끝 머리 + 위치 표 앞에) 바이트. <paramref name="zip64EndOffset"/> 는 ZIP64 끝 머리가 놓일 그 디스크 안 위치.
    /// ZIP64 를 쓰면 넘치는 칸만 0xFFFF·0xFFFFFFFF 로 두고 진짜 값은 ZIP64 끝 머리에.
    /// </summary>
    internal static byte[] BuildEnd(bool zip64, long thisDisk, long cdDisk, long entriesOnDisk, long totalEntries,
                                    long cdSize, long cdOffset, byte[] comment, long zip64EndOffset, long totalDisks)
    {
        using var ms = new MemoryStream();
        void W16(long v) => ms.Write(BitConverter.GetBytes((ushort)v));
        void W32(long v) => ms.Write(BitConverter.GetBytes((uint)v));
        void W64(long v) => ms.Write(BitConverter.GetBytes((ulong)v));
        if (zip64)
        {
            W32(Zip64EndSig);
            W64(Zip64EndLength - 12);
            W16(45);
            W16(45);
            W32(thisDisk);
            W32(cdDisk);
            W64(entriesOnDisk);
            W64(totalEntries);
            W64(cdSize);
            W64(cdOffset);
            W32(Zip64LocatorSig);
            W32(thisDisk);
            W64(zip64EndOffset);
            W32(totalDisks);
        }
        W32(EndSig);
        W16(Math.Min(thisDisk, ushort.MaxValue));
        W16(Math.Min(cdDisk, ushort.MaxValue));
        W16(entriesOnDisk >= ushort.MaxValue ? ushort.MaxValue : entriesOnDisk);
        W16(totalEntries >= ushort.MaxValue ? ushort.MaxValue : totalEntries);
        W32(cdSize >= uint.MaxValue ? uint.MaxValue : cdSize);
        W32(cdOffset >= uint.MaxValue ? uint.MaxValue : cdOffset);
        W16(comment.Length);
        ms.Write(comment);
        return ms.ToArray();
    }

    /// <summary>끝 머리 바이트 길이(<see cref="BuildEnd"/> 와 같음).</summary>
    internal static int EndBytesLength(bool zip64, int commentLength) =>
        (zip64 ? Zip64EndLength + Zip64LocatorLength : 0) + EndLength + commentLength;

    internal static byte[] ReadAt(Stream s, long position, int count)
    {
        var b = new byte[count];
        s.Position = position;
        s.ReadExactly(b);
        return b;
    }

    internal static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    internal static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
    internal static ulong U64(byte[] b, int at) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(at));

    private static long ToLong(ulong v) => v > long.MaxValue ? throw new InvalidDataException("ZIP 크기 값이 너무 큼") : (long)v;
}
