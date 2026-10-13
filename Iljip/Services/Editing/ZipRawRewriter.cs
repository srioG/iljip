using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Iljip.Services.Editing;

/// <summary>ZIP 의 항목 하나(중앙 디렉터리 기록 + 이름·방식·암호 정보). 고치기의 '원시 복사' 단위.</summary>
internal sealed class ZipSourceEntry
{
    public required ZipDirectory.CentralRecord Cd { get; init; }
    public required byte[] NameBytes { get; init; }

    /// <summary>
    /// 목록에 보이는 이름('/' 구분, 끝 '/' 없음 — 목록·선택과 같은 규칙): 유니코드 경로 추가 필드(0x7075)가 있으면 그 이름, 없으면 이름 바이트를 한글 판별한 것.
    /// </summary>
    public required string Key { get; init; }

    public required bool IsDirectory { get; init; }
    public int Flags { get; init; }
    public int Method { get; init; }
    public uint Crc { get; init; }
    public ushort DosTime { get; init; }
    public ushort DosDate { get; init; }
    public int AesVersion { get; init; }
    public int AesStrength { get; init; }
    public int AesMethod { get; init; }

    public long CompressedSize => Cd.CompressedSize;
    public long UncompressedSize => Cd.UncompressedSize;
    public long LocalOffset => Cd.LocalOffset;
    public bool IsEncrypted => (Flags & 1) != 0;
    public bool IsAes => Method == 99;

    /// <summary>안쪽 압축 방식(AES 면 0x9901 의 방식).</summary>
    public int RealMethod => IsAes ? AesMethod : Method;

    /// <summary>DOS 시각(목록·같은 이름 질문용). 못 읽으면 null.</summary>
    public DateTime? Modified => ZipRawRewriter.FromDos(DosDate, DosTime);
}

/// <summary>
/// ZIP 원시 고쳐 쓰기(W2-06, I2-3) — 다시 압축하지 않는다.
/// <list type="bullet">
/// <item>읽기(<see cref="Read"/>): 끝 머리(ZIP64 포함)·중앙 디렉터리를 읽고 항목마다 로컬 기록의 자리(머리 + 이름 + 추가 필드 + 데이터 + 데이터 서술자)를 찾는다.
///   데이터 서술자(플래그 bit 3)는 서명 있음/없음 × 4/8바이트 크기 네 모양을 CRC·크기와 다음 기록 자리로 가려낸다.</item>
/// <item>고칠 수 없는 모양은 <see cref="ArchiveNotEditableException"/>: 나뉜 ZIP(디스크 번호 ≠ 0), 앞에 실행 파일 등이 붙은 ZIP(첫 4바이트가 ZIP 머리가 아님),
///   PKWARE 강한 암호(플래그 bit 6)·중앙 디렉터리 암호(bit 13).</item>
/// <item>쓰기(<see cref="Writer"/>): 남길 항목은 로컬 기록을 바이트 그대로 옮기고 새 위치로 중앙 디렉터리 기록을 고쳐 쓴다. 이름을 바꾼 항목은 이름 칸만 새로(UTF-8 + bit 11,
///   옛 유니코드 이름 추가 필드 0x7075 는 뺌) — 데이터·CRC·시각·AES 0x9901 은 그대로라 ZipCrypto 확인 바이트(CRC·시각에 묶임)도 그대로 맞는다.
///   손대지 않은 항목은 이름 바이트·플래그도 그대로(CP949 이름은 CP949 그대로). 압축 파일 주석도 그대로.</item>
/// <item>새 항목(넣기·새 폴더)은 머리를 새로 만든다(UTF-8 + bit 11, 데이터 서술자 없음, 큰 파일은 ZIP64). 암호 압축이면 원래 압축의 암호 바이트·방식
///   (ZipCrypto 또는 WinZip AES — 같은 세기·같은 AE 판)으로 데이터를 암호화해 넣는다(<see cref="ZipEntryCrypto"/>).</item>
/// </list>
/// </summary>
internal static class ZipRawRewriter
{
    internal const uint DescriptorSig = 0x08074b50;
    private const int Utf8Flag = 0x0800;
    private const int UnicodePathExtra = 0x7075;
    private const int Zip64Extra = 0x0001;
    private const int AesExtra = 0x9901;

    /// <summary>읽은 ZIP: 항목(중앙 디렉터리 순서)·주석·중앙 디렉터리 시작.</summary>
    internal sealed class Model
    {
        public required List<ZipSourceEntry> Entries { get; init; }
        public required byte[] Comment { get; init; }
        public required long CentralStart { get; init; }
        public required long Length { get; init; }
    }

    /// <summary>ZIP 을 읽는다. 고칠 수 없는 모양이면 <see cref="ArchiveNotEditableException"/>, 깨졌으면 <see cref="CorruptArchiveException"/>.</summary>
    public static Model Read(Stream s)
    {
        try
        {
            return ReadCore(s);
        }
        catch (InvalidDataException ex)
        {
            throw new CorruptArchiveException(ex);
        }
        catch (EndOfStreamException ex)
        {
            throw CorruptArchiveException.Truncated(ex);
        }
    }

    private static Model ReadCore(Stream s)
    {
        long len = s.Length;
        if (len < ZipDirectory.EndLength) throw new InvalidDataException("ZIP 이 너무 짧음");
        uint first = ZipDirectory.U32(ZipDirectory.ReadAt(s, 0, 4), 0);
        if (first != ZipDirectory.LocalHeaderSig && first != ZipDirectory.EndSig)
            throw new ArchiveNotEditableException();   // 앞에 다른 것이 붙음(자동 풀림 exe 등)·나눔 ZIP 의 첫 조각

        var end = ZipDirectory.FindEnd(s) ?? throw new InvalidDataException("ZIP 끝 머리를 찾지 못함");
        if (end.HasZip64Locator)
        {
            if (end.Zip64Disk != 0) throw new ArchiveNotEditableException();
            ZipDirectory.ApplyZip64(s, end, end.Zip64Offset);
        }
        if (end.ThisDisk != 0 || end.CdDisk != 0) throw new ArchiveNotEditableException();
        long cdStart = end.CdOffset;
        if (cdStart < 0 || end.CdSize < 0 || end.CdSize > int.MaxValue || cdStart + end.CdSize > end.DirectoryLimit)
            throw new InvalidDataException("ZIP 중앙 디렉터리 위치가 맞지 않음");
        byte[] cd = ZipDirectory.ReadAt(s, cdStart, (int)end.CdSize);
        var records = ZipDirectory.ParseCentral(cd, end.TotalEntries);

        var entries = new List<ZipSourceEntry>(records.Count);
        foreach (var r in records)
        {
            byte[] raw = r.Raw;
            int flags = ZipDirectory.U16(raw, 8);
            if ((flags & 0x40) != 0 || (flags & 0x2000) != 0) throw new ArchiveNotEditableException();   // PKWARE 강한 암호·중앙 디렉터리 암호
            if (r.DiskStart != 0) throw new ArchiveNotEditableException();
            if (r.LocalOffset < 0 || r.LocalOffset + ZipDirectory.LocalHeaderLength > cdStart)
                throw new InvalidDataException("ZIP 로컬 머리 위치가 맞지 않음");
            byte[] name = raw.AsSpan(ZipDirectory.CentralFixedLength, r.NameLength).ToArray();
            string decoded = KoreanFileNameDecoder.DecodeBytes(name, 0, name.Length);
            int aesVersion = 0, aesStrength = 0, aesMethod = 0;
            string? unicodePath = null;
            int extraAt = ZipDirectory.CentralFixedLength + r.NameLength;
            foreach (var (id, at, size) in ExtraBlocks(raw, extraAt, r.ExtraLength))
            {
                if (id == AesExtra && size >= 7)
                {
                    aesVersion = ZipDirectory.U16(raw, at);
                    aesStrength = raw[at + 4];
                    aesMethod = ZipDirectory.U16(raw, at + 5);
                }
                else if (id == UnicodePathExtra && unicodePath is null && size >= 5)
                {
                    // Info-ZIP 유니코드 경로(판 1 + 원래 이름의 CRC + UTF-8 이름). 목록(SharpCompress)이 이 이름을 쓰므로 키도 같게 —
                    // 반디집은 CP949 이름 + 0x7075 를 쓰는데, 맥 NFD 이름처럼 CP949 로 못 적는 이름이면 둘이 다르다(W2-06 검증 지적).
                    // SharpCompress 와 똑같이: 첫 0x7075 블록, 판·CRC 는 보지 않고 6번째 바이트부터 UTF-8.
                    unicodePath = Encoding.UTF8.GetString(raw, at + 5, size - 5);
                }
            }
            if (unicodePath is not null) decoded = unicodePath;
            bool isDir = decoded.EndsWith('/') || decoded.EndsWith('\\');
            entries.Add(new ZipSourceEntry
            {
                Cd = r,
                NameBytes = name,
                Key = EditPlanner<ZipSourceEntry>.Normalize(decoded),
                IsDirectory = isDir,
                Flags = flags,
                Method = ZipDirectory.U16(raw, 10),
                DosTime = ZipDirectory.U16(raw, 12),
                DosDate = ZipDirectory.U16(raw, 14),
                Crc = ZipDirectory.U32(raw, 16),
                AesVersion = aesVersion,
                AesStrength = aesStrength,
                AesMethod = aesMethod,
            });
        }
        return new Model { Entries = entries, Comment = end.Comment, CentralStart = cdStart, Length = len };
    }

    /// <summary>추가 필드 블록들(id, 데이터 위치, 데이터 크기). 모양이 어긋난 꼬리는 내지 않는다.</summary>
    internal static IEnumerable<(int Id, int At, int Size)> ExtraBlocks(byte[] b, int start, int length)
    {
        int end = start + length;
        int x = start;
        while (x + 4 <= end)
        {
            int id = ZipDirectory.U16(b, x), size = ZipDirectory.U16(b, x + 2);
            if (x + 4 + size > end) yield break;
            yield return (id, x + 4, size);
            x += 4 + size;
        }
    }

    /// <summary>추가 필드에서 <paramref name="drop"/> 블록을 뺀 바이트(어긋난 꼬리는 그대로 둔다).</summary>
    internal static byte[] FilterExtra(byte[] b, int start, int length, params int[] drop)
    {
        using var ms = new MemoryStream();
        int end = start + length;
        int x = start;
        while (x + 4 <= end)
        {
            int id = ZipDirectory.U16(b, x), size = ZipDirectory.U16(b, x + 2);
            if (x + 4 + size > end) break;
            if (Array.IndexOf(drop, id) < 0) ms.Write(b, x, 4 + size);
            x += 4 + size;
        }
        if (x < end) ms.Write(b, x, end - x);
        return ms.ToArray();
    }

    // ───────────────────────────── 로컬 기록의 자리 ─────────────────────────────

    /// <summary>로컬 기록 하나의 자리: 머리 길이(30 + 이름 + 추가 필드), 데이터 시작, 서술자까지 포함한 끝.</summary>
    internal readonly record struct Region(long Start, int HeaderLength, long DataStart, long DataEnd, long End, byte[] Header);

    /// <summary>
    /// 항목의 로컬 기록 자리를 찾는다. <paramref name="nextBoundary"/> = 이 기록 뒤에 오는 다음 로컬 머리(또는 중앙 디렉터리) 위치 —
    /// 데이터 서술자 모양을 가려내는 데 쓴다(서술자 뒤가 정확히 다음 기록이면 그 모양).
    /// </summary>
    public static Region Locate(Stream s, ZipSourceEntry e, long nextBoundary)
    {
        long start = e.LocalOffset;
        byte[] fixedPart = ZipDirectory.ReadAt(s, start, ZipDirectory.LocalHeaderLength);
        if (ZipDirectory.U32(fixedPart, 0) != ZipDirectory.LocalHeaderSig)
            throw new CorruptArchiveException(new InvalidDataException("ZIP 로컬 머리를 찾지 못함: " + e.Key), e.Key);
        int nameLen = ZipDirectory.U16(fixedPart, 26), extraLen = ZipDirectory.U16(fixedPart, 28);
        int headerLen = ZipDirectory.LocalHeaderLength + nameLen + extraLen;
        if (start + headerLen > nextBoundary)
            throw new CorruptArchiveException(new InvalidDataException("ZIP 로컬 머리가 다음 기록과 겹침: " + e.Key), e.Key);
        byte[] header = ZipDirectory.ReadAt(s, start, headerLen);
        long dataStart = start + headerLen;
        long dataEnd = dataStart + e.CompressedSize;
        if (dataEnd > nextBoundary)
            throw CorruptArchiveException.Truncated(new InvalidDataException("ZIP 항목 데이터가 다음 기록과 겹침: " + e.Key), e.Key);

        long end = dataEnd;
        int localFlags = ZipDirectory.U16(fixedPart, 6);
        if ((localFlags & 0x08) != 0 || (e.Flags & 0x08) != 0)
            end = dataEnd + DescriptorLength(s, e, dataEnd, nextBoundary);
        return new Region(start, headerLen, dataStart, dataEnd, end, header);
    }

    /// <summary>데이터 서술자 길이(12·16·20·24). 모양을 못 가리면 <see cref="CorruptArchiveException"/>.</summary>
    private static int DescriptorLength(Stream s, ZipSourceEntry e, long at, long nextBoundary)
    {
        int avail = (int)Math.Min(24, Math.Max(0, nextBoundary - at));
        byte[] b = avail > 0 ? ZipDirectory.ReadAt(s, at, avail) : Array.Empty<byte>();
        var candidates = new List<int>();
        bool Matches(int off, bool wide, int total)
        {
            if (total > avail) return false;
            if (ZipDirectory.U32(b, off) != e.Crc) return false;
            long c = wide ? (long)ZipDirectory.U64(b, off + 4) : ZipDirectory.U32(b, off + 4);
            long u = wide ? (long)ZipDirectory.U64(b, off + 12) : ZipDirectory.U32(b, off + 8);
            return c == e.CompressedSize && u == e.UncompressedSize;
        }
        bool sig = avail >= 4 && ZipDirectory.U32(b, 0) == DescriptorSig;
        if (sig && Matches(4, false, 16)) candidates.Add(16);
        if (sig && Matches(4, true, 24)) candidates.Add(24);
        if (Matches(0, false, 12)) candidates.Add(12);
        if (Matches(0, true, 20)) candidates.Add(20);
        if (candidates.Count == 0)
            throw new CorruptArchiveException(new InvalidDataException("ZIP 데이터 서술자를 찾지 못함: " + e.Key), e.Key);
        // 서술자 뒤가 정확히 다음 기록이면 그 모양(크기가 0 인 항목 등에서 둘 이상이 맞을 수 있음)
        foreach (int c in candidates)
            if (at + c == nextBoundary) return c;
        // 다음 기록과 사이에 틈이 있으면: 뒤에 ZIP 서명이 오는 모양
        foreach (int c in candidates)
        {
            if (at + c + 4 > nextBoundary) continue;
            uint after = ZipDirectory.U32(ZipDirectory.ReadAt(s, at + c, 4), 0);
            if (after is ZipDirectory.LocalHeaderSig or ZipDirectory.CentralSig) return c;
        }
        return candidates[0];
    }

    /// <summary>각 항목 뒤의 경계(다음 로컬 머리 위치 또는 중앙 디렉터리 시작)를 계산한다.</summary>
    public static Dictionary<ZipSourceEntry, long> Boundaries(Model m)
    {
        var offsets = m.Entries.Select(e => e.LocalOffset).Append(m.CentralStart).Distinct().OrderBy(o => o).ToArray();
        var map = new Dictionary<ZipSourceEntry, long>(ReferenceEqualityComparer.Instance);
        foreach (var e in m.Entries)
        {
            int i = Array.BinarySearch(offsets, e.LocalOffset);
            map[e] = i >= 0 && i + 1 < offsets.Length ? offsets[i + 1] : m.CentralStart;
        }
        return map;
    }

    /// <summary>
    /// 구조 검사(시험의 앞 단계 — 암호를 몰라 풀어 볼 수 없는 항목도): 모든 기록의 로컬 머리 서명·이름·데이터 자리가 맞는지.
    /// 틀리면 <see cref="CorruptArchiveException"/>. 읽은 모양을 돌려준다.
    /// </summary>
    public static Model VerifyStructure(Stream s)
    {
        var m = Read(s);
        var next = Boundaries(m);
        foreach (var e in m.Entries)
        {
            var r = Locate(s, e, next[e]);
            int nameLen = ZipDirectory.U16(r.Header, 26);
            if (nameLen != e.NameBytes.Length || !r.Header.AsSpan(ZipDirectory.LocalHeaderLength, nameLen).SequenceEqual(e.NameBytes))
                throw new CorruptArchiveException(new InvalidDataException("ZIP 로컬 이름이 중앙 디렉터리와 다름: " + e.Key), e.Key);
        }
        return m;
    }

    // ───────────────────────────── 쓰기 ─────────────────────────────

    /// <summary>새 ZIP 을 차례대로 쓴다(남길 항목 옮기기·이름 바꾸기·새 항목·끝에 중앙 디렉터리).</summary>
    internal sealed class Writer
    {
        private readonly Stream _out;
        private readonly CancellationToken _ct;
        private readonly Action<long>? _onBytes;
        private readonly List<byte[]> _central = new();
        private readonly byte[] _buffer = new byte[81920];

        public Writer(Stream output, CancellationToken ct, Action<long>? onBytes)
        {
            _out = output;
            _ct = ct;
            _onBytes = onBytes;
        }

        public int Count => _central.Count;

        /// <summary>원래 압축의 항목을 옮긴다. <paramref name="newName"/> 이 null 이면 로컬 기록·중앙 기록 모두 바이트 그대로(위치만 새로).</summary>
        public void CopyEntry(Stream src, ZipSourceEntry e, Region r, byte[]? newName)
        {
            long offset = _out.Position;
            if (newName is null)
            {
                Copy(src, r.Start, r.End - r.Start);
                _central.Add(e.Cd.Rewrite(0, offset));
                return;
            }

            // 로컬 머리: 이름 길이·이름·플래그(bit 11)만 새로, 옛 유니코드 이름 추가 필드(0x7075)는 뺀다. 나머지(시각·CRC·크기·AES)는 그대로.
            byte[] fixedPart = r.Header.AsSpan(0, ZipDirectory.LocalHeaderLength).ToArray();
            int oldName = ZipDirectory.U16(fixedPart, 26), oldExtra = ZipDirectory.U16(fixedPart, 28);
            byte[] extra = FilterExtra(r.Header, ZipDirectory.LocalHeaderLength + oldName, oldExtra, UnicodePathExtra);
            if (newName.Length > ushort.MaxValue || extra.Length > ushort.MaxValue) throw new ArchiveEditException("이름이 너무 길어요.");
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(6), (ushort)(ZipDirectory.U16(fixedPart, 6) | Utf8Flag));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(26), (ushort)newName.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(28), (ushort)extra.Length);
            _out.Write(fixedPart);
            _out.Write(newName);
            _out.Write(extra);
            Copy(src, r.DataStart, r.End - r.DataStart);   // 데이터 + 데이터 서술자 그대로

            _central.Add(RenamedCentral(e, newName).Rewrite(0, offset));
        }

        /// <summary>중앙 기록의 이름만 바꾼 것(플래그 bit 11, 0x7075 뺌).</summary>
        private static ZipDirectory.CentralRecord RenamedCentral(ZipSourceEntry e, byte[] newName)
        {
            var old = e.Cd;
            byte[] fixedPart = old.Raw.AsSpan(0, ZipDirectory.CentralFixedLength).ToArray();
            byte[] extra = FilterExtra(old.Raw, ZipDirectory.CentralFixedLength + old.NameLength, old.ExtraLength, UnicodePathExtra);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(8), (ushort)(ZipDirectory.U16(fixedPart, 8) | Utf8Flag));
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(28), (ushort)newName.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(fixedPart.AsSpan(30), (ushort)extra.Length);
            var raw = new byte[ZipDirectory.CentralFixedLength + newName.Length + extra.Length + old.CommentLength];
            fixedPart.CopyTo(raw, 0);
            newName.CopyTo(raw, ZipDirectory.CentralFixedLength);
            extra.CopyTo(raw, ZipDirectory.CentralFixedLength + newName.Length);
            old.Raw.AsSpan(ZipDirectory.CentralFixedLength + old.NameLength + old.ExtraLength, old.CommentLength)
                .CopyTo(raw.AsSpan(ZipDirectory.CentralFixedLength + newName.Length + extra.Length));
            return new ZipDirectory.CentralRecord
            {
                Raw = raw,
                NameLength = newName.Length,
                ExtraLength = extra.Length,
                CommentLength = old.CommentLength,
                CompressedSize = old.CompressedSize,
                UncompressedSize = old.UncompressedSize,
                LocalOffset = old.LocalOffset,
                DiskStart = old.DiskStart,
            };
        }

        /// <summary>
        /// 새 항목을 쓴다: 임시 ZIP(<paramref name="src"/>)의 항목 <paramref name="e"/> 의 압축된 데이터를 옮기며 필요하면 암호화한다.
        /// 이름은 <paramref name="name"/>(UTF-8 + bit 11), 데이터 서술자 없음.
        /// </summary>
        public void AddEntry(Stream src, ZipSourceEntry e, Region r, byte[] name, ZipEntryCrypto? crypto)
        {
            long offset = _out.Position;
            bool encrypt = crypto is not null && !e.IsDirectory;
            long payload = e.CompressedSize;
            long csize = encrypt ? payload + crypto!.Overhead : payload;
            long usize = e.UncompressedSize;
            bool aes = encrypt && crypto!.IsAes;
            uint crc = aes && crypto!.AesVersion == 2 ? 0 : e.Crc;
            int method = aes ? 99 : e.Method;
            int flags = (e.Flags & 0x0006) | Utf8Flag | (encrypt ? 1 : 0);
            bool zip64 = csize >= uint.MaxValue || usize >= uint.MaxValue;
            int versionNeeded = Math.Max(20, Math.Max(zip64 ? 45 : 0, aes ? 51 : 0));
            byte[] aesExtra = aes ? crypto!.AesExtraField(e.Method) : Array.Empty<byte>();

            // 임시 ZIP 의 추가 필드 중 ZIP64·AES·옛 유니코드 이름이 아닌 것(NTFS 시각 등)은 그대로 옮긴다
            int tmpName = ZipDirectory.U16(r.Header, 26), tmpExtra = ZipDirectory.U16(r.Header, 28);
            byte[] carriedLocal = FilterExtra(r.Header, ZipDirectory.LocalHeaderLength + tmpName, tmpExtra, Zip64Extra, AesExtra, UnicodePathExtra);
            using var localExtra = new MemoryStream();
            if (zip64)
            {
                localExtra.Write(BitConverter.GetBytes((ushort)Zip64Extra));
                localExtra.Write(BitConverter.GetBytes((ushort)16));
                localExtra.Write(BitConverter.GetBytes((ulong)usize));
                localExtra.Write(BitConverter.GetBytes((ulong)csize));
            }
            localExtra.Write(aesExtra);
            localExtra.Write(carriedLocal);
            if (name.Length > ushort.MaxValue || localExtra.Length > ushort.MaxValue) throw new ArchiveEditException("이름이 너무 길어요.");

            var h = new byte[ZipDirectory.LocalHeaderLength];
            BinaryPrimitives.WriteUInt32LittleEndian(h, ZipDirectory.LocalHeaderSig);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), (ushort)versionNeeded);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), (ushort)flags);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8), (ushort)method);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10), e.DosTime);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(12), e.DosDate);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(14), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(18), zip64 ? uint.MaxValue : (uint)csize);
            BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(22), zip64 ? uint.MaxValue : (uint)usize);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(26), (ushort)name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(28), (ushort)localExtra.Length);
            _out.Write(h);
            _out.Write(name);
            localExtra.Position = 0;
            localExtra.CopyTo(_out);

            if (encrypt) crypto!.EncryptCopy(src, r.DataStart, payload, e.Crc, _out, _buffer, _ct, _onBytes);
            else Copy(src, r.DataStart, payload);

            // 중앙 기록: 크기가 넘치면 칸을 0xFFFFFFFF 로 두고 Rewrite 가 ZIP64 추가 필드를 만든다(위치도)
            var cdOld = e.Cd;
            byte[] carriedCentral = FilterExtra(cdOld.Raw, ZipDirectory.CentralFixedLength + cdOld.NameLength, cdOld.ExtraLength, Zip64Extra, AesExtra, UnicodePathExtra);
            byte[] cdExtra = aesExtra.Concat(carriedCentral).ToArray();
            var c = new byte[ZipDirectory.CentralFixedLength + name.Length + cdExtra.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(c, ZipDirectory.CentralSig);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(4), ZipDirectory.U16(cdOld.Raw, 4));   // 만든 프로그램 판
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(6), (ushort)versionNeeded);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(8), (ushort)flags);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(10), (ushort)method);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(12), e.DosTime);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(14), e.DosDate);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(16), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(20), csize >= uint.MaxValue ? uint.MaxValue : (uint)csize);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(24), usize >= uint.MaxValue ? uint.MaxValue : (uint)usize);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(28), (ushort)name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(30), (ushort)cdExtra.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(36), ZipDirectory.U16(cdOld.Raw, 36));    // 내부 속성
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(38), ZipDirectory.U32(cdOld.Raw, 38));    // 외부 속성
            name.CopyTo(c, ZipDirectory.CentralFixedLength);
            cdExtra.CopyTo(c, ZipDirectory.CentralFixedLength + name.Length);
            var record = new ZipDirectory.CentralRecord
            {
                Raw = c,
                NameLength = name.Length,
                ExtraLength = cdExtra.Length,
                CommentLength = 0,
                CompressedSize = csize,
                UncompressedSize = usize,
                LocalOffset = 0,
                DiskStart = 0,
            };
            _central.Add(record.Rewrite(0, offset));
        }

        /// <summary>새 폴더 항목('경로/', 크기 0, 저장 방식, 폴더 속성).</summary>
        public void AddDirectory(string key, DateTime modified)
        {
            byte[] name = Encoding.UTF8.GetBytes(key.TrimEnd('/') + "/");
            if (name.Length > ushort.MaxValue) throw new ArchiveEditException("이름이 너무 길어요.");
            var (date, time) = ToDos(modified);
            long offset = _out.Position;
            var h = new byte[ZipDirectory.LocalHeaderLength];
            BinaryPrimitives.WriteUInt32LittleEndian(h, ZipDirectory.LocalHeaderSig);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(6), Utf8Flag);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10), time);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(12), date);
            BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(26), (ushort)name.Length);
            _out.Write(h);
            _out.Write(name);

            var c = new byte[ZipDirectory.CentralFixedLength + name.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(c, ZipDirectory.CentralSig);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(8), Utf8Flag);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(12), time);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(14), date);
            BinaryPrimitives.WriteUInt16LittleEndian(c.AsSpan(28), (ushort)name.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(38), 0x10);   // 폴더(MS-DOS 속성)
            name.CopyTo(c, ZipDirectory.CentralFixedLength);
            var record = new ZipDirectory.CentralRecord
            {
                Raw = c,
                NameLength = name.Length,
                ExtraLength = 0,
                CommentLength = 0,
                CompressedSize = 0,
                UncompressedSize = 0,
                LocalOffset = 0,
                DiskStart = 0,
            };
            _central.Add(record.Rewrite(0, offset));
        }

        /// <summary>중앙 디렉터리와 끝 머리(필요하면 ZIP64)를 쓴다.</summary>
        public void Finish(byte[] comment)
        {
            long cdOffset = _out.Position;
            long cdSize = 0;
            foreach (var rec in _central)
            {
                _out.Write(rec);
                cdSize += rec.Length;
            }
            bool zip64 = _central.Count >= ushort.MaxValue || cdOffset >= uint.MaxValue || cdSize >= uint.MaxValue;
            _out.Write(ZipDirectory.BuildEnd(zip64, 0, 0, _central.Count, _central.Count, cdSize, cdOffset, comment,
                zip64EndOffset: cdOffset + cdSize, totalDisks: 1));
        }

        private void Copy(Stream src, long from, long count)
        {
            src.Position = from;
            long left = count;
            while (left > 0)
            {
                _ct.ThrowIfCancellationRequested();
                int n = src.Read(_buffer, 0, (int)Math.Min(_buffer.Length, left));
                if (n <= 0) throw CorruptArchiveException.Truncated(new EndOfStreamException("ZIP 데이터가 중간에 끝남"));
                _out.Write(_buffer, 0, n);
                left -= n;
                _onBytes?.Invoke(n);
            }
        }
    }

    // ───────────────────────────── DOS 시각 ─────────────────────────────

    internal static DateTime? FromDos(ushort date, ushort time)
    {
        try
        {
            int year = 1980 + (date >> 9), month = (date >> 5) & 0x0F, day = date & 0x1F;
            int hour = time >> 11, minute = (time >> 5) & 0x3F, second = (time & 0x1F) * 2;
            if (month is < 1 or > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) return null;
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        }
        catch
        {
            return null;
        }
    }

    internal static (ushort Date, ushort Time) ToDos(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        if (t.Year > 2107) t = new DateTime(2107, 12, 31, 23, 59, 58);
        ushort date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        ushort time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
        return (date, time);
    }
}

/// <summary>
/// 새로 넣는 항목의 암호화(W2-06): 원래 압축과 같은 암호 '바이트'(반디집 CP949 한글 암호면 CP949 — <see cref="ZipPasswordProbe"/> 로 알아낸 것)와 같은 방식.
/// <list type="bullet">
/// <item>ZipCrypto: 12바이트 암호 머리(무작위 11 + CRC 윗바이트 — 데이터 서술자 없이 쓰므로 CRC 를 확인 바이트로) + 암호화한 데이터.</item>
/// <item>WinZip AES: salt + 확인값(PV) + AES-CTR 암호문 + HMAC-SHA1 앞 10바이트, 방식 99 + 0x9901(같은 세기·같은 AE 판 — AE-2 면 CRC 칸 0).
///   <see cref="WinZipAes"/> 의 풀기와 같은 규격(PBKDF2-HMAC-SHA1 1000회, 리틀 엔디언 카운터 1부터).</item>
/// </list>
/// </summary>
internal sealed class ZipEntryCrypto
{
    private readonly byte[] _password;

    private ZipEntryCrypto(byte[] password, bool isAes, int strength, int aesVersion)
    {
        _password = password;
        IsAes = isAes;
        AesStrength = strength;
        AesVersion = aesVersion;
    }

    public static ZipEntryCrypto ZipCrypto(byte[] passwordBytes) => new(passwordBytes, false, 0, 0);

    public static ZipEntryCrypto Aes(byte[] passwordBytes, int strength, int aesVersion) =>
        new(passwordBytes, true, strength is >= 1 and <= 3 ? strength : 3, aesVersion is 1 or 2 ? aesVersion : 2);

    public bool IsAes { get; }
    public int AesStrength { get; }
    public int AesVersion { get; }

    /// <summary>암호화로 늘어나는 바이트(ZipCrypto 12, AES salt + 2 + 10).</summary>
    public long Overhead => IsAes ? WinZipAes.SaltLength(AesStrength) + 2 + WinZipAes.AuthCodeLength : 12;

    /// <summary>0x9901 추가 필드(판·'AE'·세기·안쪽 방식).</summary>
    public byte[] AesExtraField(int innerMethod)
    {
        var b = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(b, 0x9901);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(4), (ushort)AesVersion);
        b[6] = (byte)'A';
        b[7] = (byte)'E';
        b[8] = (byte)AesStrength;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(9), (ushort)innerMethod);
        return b;
    }

    /// <summary><paramref name="src"/> 의 <paramref name="from"/> 부터 <paramref name="count"/> 바이트(압축된 데이터)를 암호화해 <paramref name="output"/> 에 쓴다.</summary>
    public void EncryptCopy(Stream src, long from, long count, uint crc, Stream output, byte[] buffer, CancellationToken ct, Action<long>? onBytes)
    {
        src.Position = from;
        if (IsAes) EncryptAes(src, count, output, buffer, ct, onBytes);
        else EncryptZipCrypto(src, count, crc, output, buffer, ct, onBytes);
    }

    private void EncryptZipCrypto(Stream src, long count, uint crc, Stream output, byte[] buffer, CancellationToken ct, Action<long>? onBytes)
    {
        var keys = new ZipCryptoKeys(_password);
        var head = RandomNumberGenerator.GetBytes(12);
        head[11] = (byte)(crc >> 24);
        keys.Encrypt(head);
        output.Write(head);
        long left = count;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw CorruptArchiveException.Truncated(new EndOfStreamException("ZIP 데이터가 중간에 끝남"));
            keys.Encrypt(buffer.AsSpan(0, n));
            output.Write(buffer, 0, n);
            left -= n;
            onBytes?.Invoke(n);
        }
    }

    private void EncryptAes(Stream src, long count, Stream output, byte[] buffer, CancellationToken ct, Action<long>? onBytes)
    {
        int keyLen = WinZipAes.KeyLength(AesStrength);
        byte[] salt = RandomNumberGenerator.GetBytes(keyLen / 2);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(_password, salt, 1000, HashAlgorithmName.SHA1, keyLen * 2 + 2);
        output.Write(salt);
        output.Write(derived, keyLen * 2, 2);   // 확인값(PV)
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = derived[..keyLen];
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, derived[keyLen..(keyLen * 2)]);
        var counter = new byte[16];
        var block = new byte[16];
        var stream = new byte[16];
        int pos = 16;
        long left = count;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            int n = src.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw CorruptArchiveException.Truncated(new EndOfStreamException("ZIP 데이터가 중간에 끝남"));
            for (int i = 0; i < n; i++)
            {
                if (pos == 16)
                {
                    for (int k = 0; k < 16 && ++counter[k] == 0; k++) { }
                    Buffer.BlockCopy(counter, 0, block, 0, 16);
                    aes.EncryptEcb(block, stream, PaddingMode.None);
                    pos = 0;
                }
                buffer[i] ^= stream[pos++];
            }
            hmac.AppendData(buffer, 0, n);
            output.Write(buffer, 0, n);
            left -= n;
            onBytes?.Invoke(n);
        }
        output.Write(hmac.GetHashAndReset(), 0, WinZipAes.AuthCodeLength);
    }

    /// <summary>전통 ZipCrypto 키 상태(<see cref="ZipPasswordProbe.ZipCryptoCheck"/> 와 같은 계산).</summary>
    private sealed class ZipCryptoKeys
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            uint c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        private uint _k0 = 0x12345678, _k1 = 0x23456789, _k2 = 0x34567890;

        public ZipCryptoKeys(byte[] password)
        {
            foreach (byte b in password) Update(b);
        }

        public void Encrypt(Span<byte> data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                uint t = (_k2 | 2) & 0xFFFF;
                byte plain = data[i];
                data[i] = (byte)(plain ^ (byte)((t * (t ^ 1)) >> 8));
                Update(plain);
            }
        }

        private void Update(byte c)
        {
            _k0 = Table[(_k0 ^ c) & 0xFF] ^ (_k0 >> 8);
            _k1 = unchecked((_k1 + (_k0 & 0xFF)) * 134775813 + 1);
            _k2 = Table[(_k2 ^ (byte)(_k1 >> 24)) & 0xFF] ^ (_k2 >> 8);
        }
    }
}
