using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace Iljip.SfxStub;

/// <summary>ZIP 중앙 디렉터리 항목 하나(필요한 칸만).</summary>
internal sealed class ZipItem
{
    /// <summary>이름(맥에서 온 자모 분리 한글(NFD)은 붙은 글자(NFC)로 — 화면·풀 경로).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 압축에 적힌 그대로의 이름(NFC 로 바꾸기 전). 같은 폴더에 두 모양(NFC·NFD)의 같은 이름이 다 있던 원본은 NFC 로 바꾸면 같은 이름이 둘이 되므로
    /// 그때만 이 이름으로 푼다(<see cref="SfxExtractor.Plan"/> — W2-10 검증 지적 2차).
    /// </summary>
    public string RawName { get; set; } = string.Empty;

    public int Flags { get; set; }
    public int Method { get; set; }
    public ushort DosTime { get; set; }
    public ushort DosDate { get; set; }
    public uint Crc { get; set; }
    public long CompressedSize { get; set; }
    public long UncompressedSize { get; set; }
    public long LocalHeaderOffset { get; set; }
    public bool IsDirectory { get; set; }

    /// <summary>WinZip AES 추가 필드(0x9901)의 판(1 = AE-1, 2 = AE-2), 세기(1·2·3), 안쪽 압축 방식. AES 가 아니면 0.</summary>
    public int AesVendorVersion { get; set; }
    public int AesStrength { get; set; }
    public int AesMethod { get; set; }

    public bool IsEncrypted => (Flags & 0x0001) != 0;

    public bool IsAes => Method == 99;

    /// <summary>풀린 내용의 압축 방식(AES 면 안쪽 방식).</summary>
    public int ContentMethod => IsAes ? AesMethod : Method;

    /// <summary>DOS 날짜·시각(지역 시각). 이상하면 null.</summary>
    public DateTime? LastWriteTime
    {
        get
        {
            try
            {
                int year = 1980 + (DosDate >> 9), month = (DosDate >> 5) & 0x0F, day = DosDate & 0x1F;
                int hour = DosTime >> 11, minute = (DosTime >> 5) & 0x3F, second = (DosTime & 0x1F) * 2;
                return new DateTime(year, month, day, hour, minute, Math.Min(second, 59), DateTimeKind.Local);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// 자동 풀림 exe 에 붙은 데이터: [스텁 exe] + [ZIP] + [꼬리 24바이트 = 'ILJIPSFX' + 데이터 시작 위치(8, 리틀 엔디언) + 데이터 길이(8)].
/// 열 때 꼬리와 ZIP 목록(중앙 디렉터리 — ZIP64 포함)을 확인한다: 꼬리가 없거나 위치가 파일 길이와 맞지 않으면(끝이 잘림·덧붙여짐) 손상으로 알린다.
/// 나중에 코드 서명을 해서 끝에 서명 표가 붙은 것은 받는다(<see cref="Iljip.Services.SfxTrailer"/> — 본체와 같은 규칙).
/// ZIP 안 위치값은 ZIP 시작(=데이터 시작) 기준이다(일집이 ZIP 을 바이트 그대로 붙임 — 다른 ZIP 도구도 '앞에 붙은 exe' 로 알아보고 연다).
/// </summary>
internal sealed class SfxPayload : IDisposable
{
    private const uint LocalSig = 0x04034b50, CentralSig = 0x02014b50, EndSig = 0x06054b50, End64Sig = 0x06064b50, End64LocatorSig = 0x07064b50;

    private readonly FileStream _file;

    public string ExePath { get; }

    /// <summary>ZIP 데이터가 시작하는 파일 위치.</summary>
    public long DataStart { get; }

    /// <summary>ZIP 데이터 길이.</summary>
    public long DataLength { get; }

    /// <summary>중앙 디렉터리 위치(ZIP 기준) — 항목 데이터는 이보다 앞에 있어야 한다.</summary>
    public long CentralDirectoryOffset { get; }

    public IReadOnlyList<ZipItem> Items { get; }

    private SfxPayload(FileStream file, string exePath, long dataStart, long dataLength, long cdOffset, IReadOnlyList<ZipItem> items)
    {
        _file = file;
        ExePath = exePath;
        DataStart = dataStart;
        DataLength = dataLength;
        CentralDirectoryOffset = cdOffset;
        Items = items;
    }

    /// <summary>파일 수·풀린 크기 합계(폴더 빼고).</summary>
    public int FileCount
    {
        get
        {
            int n = 0;
            foreach (var i in Items) if (!i.IsDirectory) n++;
            return n;
        }
    }

    public long TotalBytes
    {
        get
        {
            long n = 0;
            foreach (var i in Items) if (!i.IsDirectory) n += i.UncompressedSize;
            return n;
        }
    }

    public bool AnyEncrypted
    {
        get
        {
            foreach (var i in Items) if (!i.IsDirectory && i.IsEncrypted) return true;
            return false;
        }
    }

    /// <summary>지금 실행 중인 exe 자신의 경로.</summary>
    public static string SelfPath()
    {
        string? p = null;
        try { p = Assembly.GetEntryAssembly()?.Location; } catch { /* 아래로 */ }
        if (string.IsNullOrEmpty(p))
        {
            using var me = System.Diagnostics.Process.GetCurrentProcess();
            p = me.MainModule?.FileName;
        }
        return p ?? throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);
    }

    /// <summary>자동 풀림 exe 를 열고 꼬리·ZIP 목록을 확인한다. 이상하면 <see cref="SfxException"/>(손상).</summary>
    public static SfxPayload Open(string exePath)
    {
        FileStream fs;
        try
        {
            fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            throw new SfxException(SfxErrorKind.Io, SfxMessages.FromIo(ex), ex);
        }
        try
        {
            // 꼬리는 파일 끝에(나중에 코드 서명을 했으면 서명 표 바로 앞에) — 본체와 같은 규칙(공유 소스 SfxTrailerCore.cs, W2-10 검증 지적 2차)
            if (!Iljip.Services.SfxTrailer.TryLocate(fs, out long start, out long dataLen)) throw Damaged();
            uint first = BitConverter.ToUInt32(ReadAt(fs, start, 4), 0);
            if (first != LocalSig && first != EndSig) throw Damaged();

            var (cdOffset, items) = ReadDirectory(fs, start, dataLen);
            return new SfxPayload(fs, exePath, start, dataLen, cdOffset, items);
        }
        catch (SfxException)
        {
            fs.Dispose();
            throw;
        }
        catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is InvalidDataException || ex is OverflowException)
        {
            fs.Dispose();
            throw new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged, ex);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    private static SfxException Damaged() => new SfxException(SfxErrorKind.Damaged, SfxMessages.Damaged);

    /// <summary>ZIP 끝 머리(EOCD — ZIP64 면 ZIP64 끝 머리)로 중앙 디렉터리를 찾아 항목을 읽는다.</summary>
    private static DirectoryResult ReadDirectory(FileStream fs, long start, long dataLen)
    {
        int tailLen = (int)Math.Min(dataLen, 65535 + 22);
        byte[] tail = ReadAt(fs, start + dataLen - tailLen, tailLen);
        int eocd = -1;
        for (int i = tailLen - 22; i >= 0; i--)
        {
            if (BitConverter.ToUInt32(tail, i) != EndSig) continue;
            int commentLen = BitConverter.ToUInt16(tail, i + 20);
            if (i + 22 + commentLen == tailLen) { eocd = i; break; }   // 끝 머리 + 주석이 데이터 끝과 딱 맞아야(뒤에 다른 것이 붙지 않음)
        }
        if (eocd < 0) throw Damaged();
        long eocdPos = dataLen - tailLen + eocd;   // ZIP 기준

        long entries = BitConverter.ToUInt16(tail, eocd + 10);
        long cdSize = BitConverter.ToUInt32(tail, eocd + 12);
        long cdOffset = BitConverter.ToUInt32(tail, eocd + 16);
        long cdLimit = eocdPos;
        if (entries == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
        {
            // ZIP64: 끝 머리 바로 앞 20바이트가 ZIP64 위치 표시
            if (eocdPos < 20) throw Damaged();
            byte[] loc = ReadAt(fs, start + eocdPos - 20, 20);
            if (BitConverter.ToUInt32(loc, 0) != End64LocatorSig) throw Damaged();
            long e64 = BitConverter.ToInt64(loc, 8);
            if (e64 < 0 || e64 + 56 > eocdPos - 20) throw Damaged();
            byte[] rec = ReadAt(fs, start + e64, 56);
            if (BitConverter.ToUInt32(rec, 0) != End64Sig) throw Damaged();
            entries = BitConverter.ToInt64(rec, 32);
            cdSize = BitConverter.ToInt64(rec, 40);
            cdOffset = BitConverter.ToInt64(rec, 48);
            cdLimit = e64;
        }
        if (entries < 0 || cdSize < 0 || cdOffset < 0 || cdOffset + cdSize > cdLimit || cdSize > int.MaxValue) throw Damaged();

        byte[] cd = ReadAt(fs, start + cdOffset, (int)cdSize);
        var items = new List<ZipItem>();
        int p = 0;
        Encoding? cp949 = null;
        for (long n = 0; n < entries; n++)
        {
            if (p + 46 > cd.Length || BitConverter.ToUInt32(cd, p) != CentralSig) throw Damaged();
            var item = new ZipItem
            {
                Flags = BitConverter.ToUInt16(cd, p + 8),
                Method = BitConverter.ToUInt16(cd, p + 10),
                DosTime = BitConverter.ToUInt16(cd, p + 12),
                DosDate = BitConverter.ToUInt16(cd, p + 14),
                Crc = BitConverter.ToUInt32(cd, p + 16),
                CompressedSize = BitConverter.ToUInt32(cd, p + 20),
                UncompressedSize = BitConverter.ToUInt32(cd, p + 24),
                LocalHeaderOffset = BitConverter.ToUInt32(cd, p + 42),
            };
            int nameLen = BitConverter.ToUInt16(cd, p + 28), extraLen = BitConverter.ToUInt16(cd, p + 30), commentLen = BitConverter.ToUInt16(cd, p + 32);
            if (p + 46 + nameLen + extraLen + commentLen > cd.Length) throw Damaged();
            byte[] rawName = new byte[nameLen];
            Buffer.BlockCopy(cd, p + 46, rawName, 0, nameLen);
            ReadExtra(cd, p + 46 + nameLen, extraLen, item);

            string name;
            if ((item.Flags & 0x0800) != 0)
            {
                name = new UTF8Encoding(false, true).GetString(rawName);
            }
            else
            {
                // 일집이 만든 ZIP 은 늘 UTF-8 표시가 있다. 표시가 없으면 한국어 Windows 의 옛 방식(CP949)으로 읽는다
                try { cp949 ??= Encoding.GetEncoding(949); }
                catch (Exception) { cp949 = Encoding.Default; }
                name = cp949.GetString(rawName);
            }
            item.RawName = name;
            name = name.Normalize(NormalizationForm.FormC);
            item.Name = name;
            item.IsDirectory = name.EndsWith("/", StringComparison.Ordinal) || name.EndsWith("\\", StringComparison.Ordinal);
            if (item.CompressedSize < 0 || item.UncompressedSize < 0 || item.LocalHeaderOffset < 0 || item.LocalHeaderOffset >= cdOffset)
                throw Damaged();
            items.Add(item);
            p += 46 + nameLen + extraLen + commentLen;
        }
        return new DirectoryResult(cdOffset, items);
    }

    /// <summary>추가 필드: ZIP64(0x0001 — 32비트 칸이 0xFFFFFFFF 인 것만 차례로), WinZip AES(0x9901).</summary>
    private static void ReadExtra(byte[] b, int at, int length, ZipItem item)
    {
        int end = at + length;
        while (at + 4 <= end)
        {
            int id = BitConverter.ToUInt16(b, at), size = BitConverter.ToUInt16(b, at + 2);
            int data = at + 4;
            if (data + size > end) break;
            if (id == 0x0001)
            {
                int q = data;
                if (item.UncompressedSize == 0xFFFFFFFF && q + 8 <= data + size) { item.UncompressedSize = BitConverter.ToInt64(b, q); q += 8; }
                if (item.CompressedSize == 0xFFFFFFFF && q + 8 <= data + size) { item.CompressedSize = BitConverter.ToInt64(b, q); q += 8; }
                if (item.LocalHeaderOffset == 0xFFFFFFFF && q + 8 <= data + size) { item.LocalHeaderOffset = BitConverter.ToInt64(b, q); }
            }
            else if (id == 0x9901 && size >= 7)
            {
                item.AesVendorVersion = BitConverter.ToUInt16(b, data);
                item.AesStrength = b[data + 4];
                item.AesMethod = BitConverter.ToUInt16(b, data + 5);
            }
            at = data + size;
        }
    }

    /// <summary>항목 데이터가 시작하는 위치(ZIP 기준)와 로컬 머리의 DOS 시각. 로컬 머리가 없으면 손상.</summary>
    public long LocalDataStart(ZipItem item, out ushort localTime)
    {
        byte[] h = ReadRange(item.LocalHeaderOffset, 30);
        if (BitConverter.ToUInt32(h, 0) != LocalSig) throw Damaged();
        int nameLen = BitConverter.ToUInt16(h, 26), extraLen = BitConverter.ToUInt16(h, 28);
        localTime = BitConverter.ToUInt16(h, 10);
        long dataStart = item.LocalHeaderOffset + 30 + nameLen + extraLen;
        if (dataStart + item.CompressedSize > CentralDirectoryOffset) throw Damaged();
        return dataStart;
    }

    /// <summary>ZIP 기준 위치의 바이트들(데이터 범위를 넘으면 손상).</summary>
    public byte[] ReadRange(long zipOffset, int count)
    {
        if (zipOffset < 0 || zipOffset + count > DataLength) throw Damaged();
        return ReadAt(_file, DataStart + zipOffset, count);
    }

    /// <summary>ZIP 기준 [위치, 위치+길이) 를 읽는 스트림(파일을 같이 쓰므로 한 번에 하나만).</summary>
    public Stream OpenRange(long zipOffset, long length)
    {
        if (zipOffset < 0 || length < 0 || zipOffset + length > DataLength) throw Damaged();
        return new RangeStream(_file, DataStart + zipOffset, length);
    }

    private static byte[] ReadAt(Stream s, long pos, int count)
    {
        var buf = new byte[count];
        s.Position = pos;
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) throw Damaged();
            got += n;
        }
        return buf;
    }

    public void Dispose() => _file.Dispose();

    private readonly struct DirectoryResult
    {
        public DirectoryResult(long cdOffset, List<ZipItem> items)
        {
            CdOffset = cdOffset;
            Items = items;
        }

        public long CdOffset { get; }
        public List<ZipItem> Items { get; }

        public void Deconstruct(out long cdOffset, out List<ZipItem> items)
        {
            cdOffset = CdOffset;
            items = Items;
        }
    }
}

/// <summary>파일의 한 구간만 읽는 스트림(읽을 때마다 자리를 잡는다 — 같은 파일을 차례로 여러 구간에 씀).</summary>
internal sealed class RangeStream : Stream
{
    private readonly Stream _base;
    private readonly long _start;
    private readonly long _length;
    private long _pos;

    public RangeStream(Stream @base, long start, long length)
    {
        _base = @base;
        _start = start;
        _length = length;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_pos >= _length || count <= 0) return 0;
        _base.Position = _start + _pos;
        int n = _base.Read(buffer, offset, (int)Math.Min(count, _length - _pos));
        if (n > 0) _pos += n;
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
