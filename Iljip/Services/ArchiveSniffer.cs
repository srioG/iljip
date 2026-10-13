using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Iljip.Services;

/// <summary>내용(서명)으로 알아본 압축 종류.</summary>
public enum ArchiveKind
{
    Unknown, Zip, SevenZip, Rar, Tar, TarGz, TarBz2, TarXz, GZip, BZip2, Xz, Iso, Cab, Lzh, Wim, Egg, Alz, ZipSfx, SevenZipSfx, Zstd,

    /// <summary>UDF 가 든 디스크 이미지(UDF 만 있거나 ISO9660+UDF 다리 — DVD·Windows 설치 ISO 등). 확장자는 보통 .iso.</summary>
    Udf,

    /// <summary>자동 풀림 exe 안의 RAR(WinRAR SFX)·EGG·ALZ(알집 SFX) — W1-B 2차 검증 지적.</summary>
    RarSfx, EggSfx, AlzSfx,
}

/// <summary>
/// 확장자가 아니라 파일 앞부분의 서명으로 압축 종류를 알아본다(확장자가 바뀐 압축 — B-086).
/// 앞쪽 64KB 와 꼭 필요한 위치(ISO 0x8001·SFX 뒤쪽 ZIP 끝 머리)만 읽고, 예외는 내지 않고 <see cref="ArchiveKind.Unknown"/> 을 돌려준다.
/// ★ 일집 안에서 '열기·풀기' 직전에만 쓴다(<see cref="ArchiveServiceLocator.ResolveByContent"/> — 확장자로 못 고른 파일,
///   <see cref="ArchiveServiceLocator.Resolve"/> — .gz·.bz2·.xz 의 안쪽 tar). 탐색기 우클릭(C++ 셸 확장)과 드롭 분류·저장 대상은
///   파일을 읽지 않는다(<see cref="ArchiveServiceLocator.ResolveByExtension"/>).
/// </summary>
public static class ArchiveSniffer
{
    private const int HeadSize = 64 * 1024;
    /// <summary>gz·bz2·xz 안쪽 512바이트를 풀 때 읽을 최대 입력(bzip2 첫 블록은 900KB 까지 될 수 있음).</summary>
    private const int InnerProbeInputLimit = 2 * 1024 * 1024;

    private static readonly byte[] SevenZipSig = { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C };
    private static readonly byte[] Rar4Sig = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
    private static readonly byte[] Rar5Sig = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
    private static readonly byte[] XzSig = { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 };
    private static readonly byte[] ZstdSig = { 0x28, 0xB5, 0x2F, 0xFD };
    private static readonly byte[] AlzSig = { 0x41, 0x4C, 0x5A, 0x01 };

    public static ArchiveKind Detect(string path) => Detect(path, out _);

    /// <param name="embeddedOffset">
    /// SFX 의 안쪽 압축 기준점(아니면 0): 7z SFX 는 7z 서명 위치, ZIP SFX 는 ZIP 안 위치값의 기준점(반디집처럼 파일 처음 기준이면 0).
    /// </param>
    /// <param name="peekInner">gz·bz2·xz 의 안쪽이 tar 인지까지 볼지(false 면 바깥 종류만 — TarGz 등을 돌려주지 않음).</param>
    public static ArchiveKind Detect(string path, out long embeddedOffset, bool peekInner = true)
    {
        embeddedOffset = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            return Detect(fs, out embeddedOffset, peekInner);
        }
        catch
        {
            return ArchiveKind.Unknown;
        }
    }

    /// <summary>
    /// 확장자가 단일 스트림 압축(.gz·.bz2·.xz)인 파일의 안쪽이 tar 인지(서명이 그 확장자의 형식과 맞을 때만 본다) — 라우팅 표의
    /// '.gz·.bz2(안이 tar, 내용 판별) → 압축된 tar 처리기'(<see cref="ArchiveServiceLocator.Resolve"/>). 못 읽으면 false.
    /// </summary>
    public static bool SingleStreamInnerIsTar(string path, string extension)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            var head = ReadAt(fs, 0, 6);
            ArchiveKind outer;
            if (extension.Equals(".gz", StringComparison.OrdinalIgnoreCase) && head.Length >= 3 && head[0] == 0x1F && head[1] == 0x8B && head[2] == 0x08)
                outer = ArchiveKind.GZip;
            else if (extension.Equals(".bz2", StringComparison.OrdinalIgnoreCase) && head.Length >= 4 && head[0] == 0x42 && head[1] == 0x5A && head[2] == 0x68)
                outer = ArchiveKind.BZip2;
            else if (extension.Equals(".xz", StringComparison.OrdinalIgnoreCase) && head.AsSpan().StartsWith(XzSig))
                outer = ArchiveKind.Xz;
            else
                return false;
            return InnerIsTar(fs, outer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>읽기·찾기가 되는 스트림(처음 위치 0)으로 알아본다. 스트림 위치는 바뀐다.</summary>
    internal static ArchiveKind Detect(Stream s, out long embeddedOffset, bool peekInner = true)
    {
        embeddedOffset = 0;
        try
        {
            long len = s.Length;
            if (len == 0) return ArchiveKind.Unknown;
            var head = ReadAt(s, 0, (int)Math.Min(HeadSize, len));
            ReadOnlySpan<byte> h = head;

            if (h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B &&
                ((h[2] == 0x03 && h[3] == 0x04) || (h[2] == 0x05 && h[3] == 0x06) || (h[2] == 0x07 && h[3] == 0x08)))
                return ArchiveKind.Zip;
            if (h.StartsWith(SevenZipSig)) return ArchiveKind.SevenZip;
            if (h.StartsWith(Rar4Sig) || h.StartsWith(Rar5Sig)) return ArchiveKind.Rar;
            if (h.Length >= 3 && h[0] == 0x1F && h[1] == 0x8B && h[2] == 0x08)
                return peekInner && InnerIsTar(s, ArchiveKind.GZip) ? ArchiveKind.TarGz : ArchiveKind.GZip;
            if (h.Length >= 4 && h[0] == 0x42 && h[1] == 0x5A && h[2] == 0x68 && h[3] is >= (byte)'1' and <= (byte)'9')
                return peekInner && InnerIsTar(s, ArchiveKind.BZip2) ? ArchiveKind.TarBz2 : ArchiveKind.BZip2;
            if (h.StartsWith(XzSig))
                return peekInner && InnerIsTar(s, ArchiveKind.Xz) ? ArchiveKind.TarXz : ArchiveKind.Xz;
            if (h.Length >= 512 && h.Slice(257, 5).SequenceEqual("ustar"u8)) return ArchiveKind.Tar;
            if (h.StartsWith("EGGA"u8)) return ArchiveKind.Egg;
            if (h.StartsWith(AlzSig)) return ArchiveKind.Alz;
            if (h.StartsWith("MSCF"u8)) return ArchiveKind.Cab;
            if (h.StartsWith("MSWIM\0\0\0"u8)) return ArchiveKind.Wim;
            if (h.StartsWith(ZstdSig)) return ArchiveKind.Zstd;
            if (h.Length >= 7 && h[2] == (byte)'-' && h[3] == (byte)'l' && h[4] == (byte)'h' && h[6] == (byte)'-') return ArchiveKind.Lzh;
            if (IsDiscImage(h, out bool udf)) return udf ? ArchiveKind.Udf : ArchiveKind.Iso;
            if (h.Length >= 2 && h[0] == (byte)'M' && h[1] == (byte)'Z')
                return DetectSfx(s, head, out embeddedOffset);
            return ArchiveKind.Unknown;
        }
        catch
        {
            embeddedOffset = 0;
            return ArchiveKind.Unknown;
        }
    }

    /// <summary>
    /// 디스크 이미지인지: 16번째 2048바이트 구역(32KB)부터 이어지는 '볼륨 인식 순서'를 읽는다 — ISO9660 'CD001'(+ 'BOOT2'·'CDW02'),
    /// UDF 'BEA01'·'NSR02'/'NSR03'·'TEA01'. NSR 이 있으면 UDF(7-Zip·반디집처럼 UDF 를 먼저 연다 — Joliet 없는 다리 이미지의
    /// ISO9660 쪽은 이름이 8.3 대문자로 깨지고, Windows 설치 ISO 의 ISO9660 쪽엔 안내문 하나만 있다).
    /// </summary>
    private static bool IsDiscImage(ReadOnlySpan<byte> h, out bool udf)
    {
        udf = false;
        bool iso = false, bea = false;
        // 앞 64KB 안의 구역 머리를 모두 본다(구역이 4096바이트인 UDF 도 2048 배수 자리에 있다)
        for (int at = 16 * 2048 + 1; h.Length >= at + 5; at += 2048)
        {
            var id = h.Slice(at, 5);
            if (id.SequenceEqual("CD001"u8)) iso = true;
            else if (id.SequenceEqual("BEA01"u8)) bea = true;
            else if (id.SequenceEqual("NSR02"u8) || id.SequenceEqual("NSR03"u8)) udf = true;
        }
        udf &= bea;   // NSR 은 확장 영역 시작(BEA01) 뒤에 온다 — 우연히 같은 글자가 있는 파일을 거른다
        return iso || udf;
    }

    /// <summary>
    /// tar 머리인지: 257 에 'ustar' 가 있거나(POSIX·GNU), 서명 없는 옛 v7 tar 는 머리 확인값(148~155, 그 칸은 공백으로 보고 더함)이 맞는지.
    /// </summary>
    internal static bool IsTarHeader(ReadOnlySpan<byte> b)
    {
        if (b.Length < 512) return false;
        if (b.Slice(257, 5).SequenceEqual("ustar"u8)) return true;
        if (b[0] == 0) return false;   // 이름 없음 = tar 끝 표시이거나 tar 아님
        long stored = 0;
        bool any = false;
        foreach (byte c in b.Slice(148, 8))
        {
            if (c is >= (byte)'0' and <= (byte)'7') { stored = stored * 8 + (c - '0'); any = true; }
            else if (c is 0 or (byte)' ') { if (any) break; }
            else return false;
        }
        if (!any) return false;
        long sum = 0;
        for (int i = 0; i < 512; i++) sum += i is >= 148 and < 156 ? (byte)' ' : b[i];
        return sum == stored;
    }

    /// <summary>
    /// gz·bz2·xz 의 안쪽 512바이트를 풀어 tar 머리인지 본다(gz·bz2 는 읽는 입력을 <see cref="InnerProbeInputLimit"/> 까지).
    /// ★ xz 는 7z.dll 로만 본다(<see cref="XzInnerIsTar"/>) — SharpCompress 0.50.5 의 XZ 해제기는 블록 꼬리가 잘린 xz 에서 Read 안쪽을 끝없이 돌고
    ///   (취소도 안 먹음 — 1차 W1-B 검증 지적 (b)), 입력을 2MB 로 자른 감싸개가 바로 그 '잘린 파일'을 흉내 내 같은 멈춤이 났다.
    /// </summary>
    private static bool InnerIsTar(Stream s, ArchiveKind outer)
    {
        if (outer == ArchiveKind.Xz) return XzInnerIsTar(s);
        try
        {
            s.Position = 0;
            using var limited = new LimitedReadStream(s, InnerProbeInputLimit);
            using Stream dec = outer switch
            {
                ArchiveKind.GZip => new GZipStream(limited, CompressionMode.Decompress, leaveOpen: true),
                ArchiveKind.BZip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(limited, SharpCompress.Compressors.CompressionMode.Decompress, false),
                _ => throw new ArgumentOutOfRangeException(nameof(outer)),
            };
            var buf = new byte[512];
            int got = 0;
            while (got < buf.Length)
            {
                int n = dec.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
            return got == 512 && IsTarHeader(buf);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// xz 안쪽 첫 512바이트를 7z.dll 로 받아 tar 머리인지 본다. 512바이트를 받으면 출력 스트림이 멈춤 예외를 던져 7z.dll 이 곧 멈춘다
    /// (출력 쓰기에서 난 예외는 7z.dll 이 HRESULT 로 받아 1초 안에 멈춤 — 실측). 7z.dll 을 못 쓰면 false(.txz·.tar.xz 는 확장자로 정해짐).
    /// </summary>
    private static bool XzInnerIsTar(Stream s)
    {
        if (!SevenZipLibrary.IsAvailable) return false;
        var head = new StopAfterStream(512);
        try
        {
            s.Position = 0;
            using var ex = new SharpSevenZip.SharpSevenZipExtractor(s, leaveOpen: true, SharpSevenZip.InArchiveFormat.XZ);
            ex.ExtractFile(0, head);
        }
        catch
        {
            // 512바이트에서 멈춘 것(정상)·잘린 xz·손상 — 받은 만큼으로 판단
        }
        return head.Count >= 512 && IsTarHeader(head.Buffer);
    }

    /// <summary>처음 <c>limit</c> 바이트만 모으고, 다 모이면 예외를 던져 쓰는 쪽(7z.dll)을 멈추게 하는 출력 스트림.</summary>
    private sealed class StopAfterStream : Stream
    {
        private readonly int _limit;
        public byte[] Buffer { get; }
        public int Count { get; private set; }

        public StopAfterStream(int limit)
        {
            _limit = limit;
            Buffer = new byte[limit];
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int take = Math.Min(count, _limit - Count);
            if (take > 0)
            {
                Array.Copy(buffer, offset, Buffer, Count, take);
                Count += take;
            }
            if (Count >= _limit) throw new OperationCanceledException("앞부분만 봄");
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) { }
    }

    /// <summary>
    /// 'MZ' 실행 파일 안의 자동 풀림(SFX) 압축 찾기.
    /// ZIP: 파일 끝의 ZIP 끝 머리(EOCD) → 중앙 디렉터리 첫 항목 → 그 로컬 머리가 실제로 있는지(절대·상대 위치 둘 다) 확인.
    /// 7z·RAR·EGG·ALZ: PE 섹션이 끝나는 곳(덧붙은 영역 — SFX 모듈 뒤에 압축을 붙이는 자리) 또는 앞 64KB 에서 서명을 찾고
    /// 바로 뒤 머리를 검증한다(7z 시작 머리 CRC, RAR 주 머리 CRC, EGG 머리 끝 표시, ALZ 첫 항목 서명) — SFX 모듈 코드 속 우연한 같은
    /// 글자를 거른다. 같은 영역에서 여럿이면 앞에 있는 것. 없으면 보통 exe(Unknown).
    /// ★ RAR·EGG·ALZ SFX 는 실물 픽스처가 없어(WinRAR·알집 없음) 규격대로 만든 합성 exe 로만 시험했다 — MANUAL_CHECKLIST.
    /// </summary>
    private static ArchiveKind DetectSfx(Stream s, byte[] head, out long offset)
    {
        offset = 0;
        long len = s.Length;
        // 일집이 만든 자동 풀림 exe(W2-10): 꼬리 'ILJIPSFX' 가 ZIP 데이터 시작을 정확히 적어 둔다 — 끝 머리 찾기보다 먼저(꼬리 24바이트 속
        // 우연한 끝 머리 서명에 흔들리지 않게)
        if (IljipSfxData(s) is { } own)
        {
            offset = own.Start;
            return ArchiveKind.ZipSfx;
        }
        long zipBase = FindZipBase(s, len);
        if (zipBase >= 0)
        {
            offset = zipBase;
            return ArchiveKind.ZipSfx;
        }

        long overlay = PeOverlayOffset(head);
        // 덧붙은 영역이 ZIP 로컬 머리로 바로 시작하는데 끝 머리(EOCD)·일집 꼬리가 없으면 끝이 잘린(덜 받아진) ZIP 자동 풀림이다 — '압축 아님'이 아니라
        // ZIP 자동 풀림으로 보고, 여는 쪽(SfxArchiveService)이 '끝부분이 잘려 있어요'로 알린다(W2-10 검증 지적 2차: 끝이 잘린 일집 자동 풀림이
        // 'Unsupported archive type: .exe'·지원하지 않는 형식이었음). 7z·RAR 등을 찾기 전에 본다 — 잘린 ZIP 안에 저장만 한 .7z 가 있어도 그 7z 를 압축 자체로 잘못 열지 않게.
        if (OverlayStartsZip(s, overlay, len))
        {
            offset = overlay;
            return ArchiveKind.ZipSfx;
        }
        if (overlay > 0 && overlay < len)
        {
            var win = ReadAt(s, overlay, (int)Math.Min(HeadSize, len - overlay));
            var (kind, at) = FirstEmbedded(win, from: 0);
            if (kind != ArchiveKind.Unknown) { offset = overlay + at; return kind; }
        }
        var (headKind, headAt) = FirstEmbedded(head, from: 1);   // 0 이면 'MZ' 가 아니므로 1 부터
        if (headKind != ArchiveKind.Unknown) { offset = headAt; return headKind; }
        return ArchiveKind.Unknown;
    }

    /// <summary>
    /// 끝이 잘린 ZIP 자동 풀림인가: 'MZ' 실행 파일인데 일집 꼬리도 ZIP 끝 머리(EOCD)도 없고, PE 섹션이 끝나는 곳(덧붙은 영역)이 ZIP 로컬 머리로 시작한다
    /// (<see cref="Detect(Stream, out long, bool)"/> 는 이것도 <see cref="ArchiveKind.ZipSfx"/> 로 본다 — W2-10 검증 지적 2차).
    /// 여는 쪽이 엔진에 맡기기 전에 '끝부분이 잘려 있어요'로 알리게(엔진은 'Failed to locate the Zip Header' 같은 영어로 끝남). 못 읽으면 false.
    /// </summary>
    internal static bool IsTruncatedZipSfx(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096);
            long len = s.Length;
            var head = ReadAt(s, 0, (int)Math.Min(HeadSize, len));
            if (head.Length < 2 || head[0] != (byte)'M' || head[1] != (byte)'Z') return false;
            if (IljipSfxData(s) is not null || FindZipBase(s, len) >= 0) return false;
            return OverlayStartsZip(s, PeOverlayOffset(head), len);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>덧붙은 영역(<paramref name="overlay"/>)이 ZIP 로컬 머리로 시작하는가.</summary>
    private static bool OverlayStartsZip(Stream s, long overlay, long len) =>
        overlay > 0 && overlay + 30 <= len && IsZipLocalHeader(ReadAt(s, overlay, 30));

    /// <summary>
    /// ZIP 로컬 머리(30바이트)다운가: 서명 'PK\3\4' + 풀 때 필요한 판(63 이하 — 6.3 까지) + 압축 방식이 아는 값(저장·deflate·deflate64·bzip2·lzma·zstd·xz·ppmd·AES 99)
    /// + 이름 길이 1 이상. exe 의 서명 표 등 우연히 같은 글자로 시작하는 것을 거른다.
    /// </summary>
    private static bool IsZipLocalHeader(byte[] h)
    {
        if (h.Length < 30 || BinaryPrimitives.ReadUInt32LittleEndian(h) != 0x04034B50) return false;
        int version = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(4, 2)) & 0xFF;
        int method = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(8, 2));
        int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(26, 2));
        return version <= 63 && nameLength > 0 && method is 0 or 8 or 9 or 12 or 14 or 93 or 95 or 98 or 99;
    }

    /// <summary>영역 안에서 검증을 통과한 첫 압축(7z·RAR·EGG·ALZ)과 그 위치. 없으면 (Unknown, -1).</summary>
    private static (ArchiveKind Kind, int At) FirstEmbedded(ReadOnlySpan<byte> b, int from)
    {
        var found = new (ArchiveKind Kind, int At)[]
        {
            (ArchiveKind.SevenZipSfx, IndexOfValid(b, SevenZipSig, from, IsValid7zAt)),
            (ArchiveKind.RarSfx, Math.Min(Pos(IndexOfValid(b, Rar5Sig, from, IsValidRar5At)), Pos(IndexOfValid(b, Rar4Sig, from, IsValidRar4At)))),
            (ArchiveKind.EggSfx, IndexOfValid(b, "EGGA"u8, from, IsValidEggAt)),
            (ArchiveKind.AlzSfx, IndexOfValid(b, AlzSig, from, IsValidAlzAt)),
        };
        var best = (Kind: ArchiveKind.Unknown, At: -1);
        foreach (var f in found)
        {
            int at = f.At == int.MaxValue ? -1 : f.At;
            if (at >= 0 && (best.At < 0 || at < best.At)) best = (f.Kind, at);
        }
        return best;

        static int Pos(int i) => i < 0 ? int.MaxValue : i;
    }

    private delegate bool Validator(ReadOnlySpan<byte> b, int at);

    /// <summary>서명이 있고 <paramref name="valid"/> 를 통과한 첫 위치(없으면 -1).</summary>
    private static int IndexOfValid(ReadOnlySpan<byte> b, ReadOnlySpan<byte> sig, int from, Validator valid)
    {
        while (from < b.Length)
        {
            int i = b[from..].IndexOf(sig);
            if (i < 0) return -1;
            i += from;
            if (valid(b, i)) return i;
            from = i + 1;
        }
        return -1;
    }

    /// <summary>7z: 서명 + 시작 머리 CRC(뒤 20바이트의 CRC32)가 맞음.</summary>
    private static bool IsValid7zAt(ReadOnlySpan<byte> b, int i)
    {
        if (i + 32 > b.Length || b[i + 6] != 0) return false;
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(i + 8, 4));
        return crc == Crc32Of(b.Slice(i + 12, 20));
    }

    /// <summary>RAR5: 서명 뒤 첫 머리(CRC32 4 + 크기 vint + 본문)의 CRC 가 맞고 종류가 1(주 머리) 또는 4(암호 머리).</summary>
    private static bool IsValidRar5At(ReadOnlySpan<byte> b, int i)
    {
        int p = i + 8;
        if (p + 6 > b.Length) return false;
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p, 4));
        int q = p + 4;
        if (!ReadVint(b, ref q, out ulong size) || size == 0 || size > 0x10000 || q + (long)size > b.Length) return false;
        int body = q;
        if (crc != Crc32Of(b.Slice(p + 4, q - (p + 4) + (int)size))) return false;
        return ReadVint(b, ref body, out ulong type) && type is 1 or 4;
    }

    /// <summary>RAR4: 서명 뒤 주 머리(MAIN_HEAD 0x73)의 CRC(종류부터 머리 끝까지 CRC32 의 아래 16비트)가 맞음.</summary>
    private static bool IsValidRar4At(ReadOnlySpan<byte> b, int i)
    {
        int m = i + 7;
        if (m + 7 > b.Length || b[m + 2] != 0x73) return false;
        int size = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(m + 5, 2));
        if (size < 7 || m + size > b.Length) return false;
        ushort crc = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(m, 2));
        return crc == (ushort)(Crc32Of(b.Slice(m + 2, size - 2)) & 0xFFFF);
    }

    /// <summary>EGG: 'EGGA' + 판(2) + 머리 ID(4) + 예약(4) 뒤에 머리 끝 표시(0x08E28222)·분할 머리·솔리드 머리 중 하나.</summary>
    private static bool IsValidEggAt(ReadOnlySpan<byte> b, int i)
    {
        if (i + 18 > b.Length) return false;
        uint next = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(i + 14, 4));
        return next is 0x08E28222u or 0x24F5A262u or 0x24E5A060u;
    }

    /// <summary>ALZ: 'ALZ\1' + 4바이트 뒤에 첫 항목('BLZ\1') 또는 끝('CLZ\1').</summary>
    private static bool IsValidAlzAt(ReadOnlySpan<byte> b, int i)
    {
        if (i + 12 > b.Length) return false;
        uint next = BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(i + 8, 4));
        return next is 0x015A4C42u or 0x015A4C43u;
    }

    private static bool ReadVint(ReadOnlySpan<byte> b, ref int p, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && p < b.Length; shift += 7)
        {
            byte c = b[p++];
            value |= (ulong)(c & 0x7F) << shift;
            if ((c & 0x80) == 0) return true;
        }
        return false;
    }

    internal static uint Crc32Of(ReadOnlySpan<byte> data)
    {
        var c = new ICSharpCode.SharpZipLib.Checksum.Crc32();
        c.Update(new ArraySegment<byte>(data.ToArray()));
        return (uint)c.Value;
    }

    /// <summary>PE 머리에서 마지막 섹션이 끝나는 파일 위치(덧붙은 영역 시작). 못 읽으면 0.</summary>
    private static long PeOverlayOffset(ReadOnlySpan<byte> h)
    {
        if (h.Length < 0x40) return 0;
        int pe = BinaryPrimitives.ReadInt32LittleEndian(h.Slice(0x3C, 4));
        if (pe <= 0 || pe + 24 > h.Length || !h.Slice(pe, 4).SequenceEqual("PE\0\0"u8)) return 0;
        int sections = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(pe + 6, 2));
        int optSize = BinaryPrimitives.ReadUInt16LittleEndian(h.Slice(pe + 20, 2));
        int table = pe + 24 + optSize;
        long end = 0;
        for (int k = 0; k < sections; k++)
        {
            int sct = table + 40 * k;
            if (sct + 40 > h.Length) return 0;
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(sct + 16, 4));
            uint rawPtr = BinaryPrimitives.ReadUInt32LittleEndian(h.Slice(sct + 20, 4));
            end = Math.Max(end, (long)rawPtr + rawSize);
        }
        return end;
    }

    /// <summary>
    /// ZIP 끝 머리 → 중앙 디렉터리 첫 항목 → 그 로컬 머리가 실제로 있는지 확인하고, ZIP 안 위치값의 기준점을 돌려준다:
    /// 위치값이 파일 처음 기준(반디집 SFX)이면 0, 앞에 붙인 exe 만큼 밀린 상대값이면 그 길이. 없으면 -1.
    /// (그 기준점부터 보이는 스트림을 열면 보통 ZIP 처럼 읽힌다 — <see cref="OffsetStream"/>)
    /// </summary>
    /// <summary>
    /// 일집 자동 풀림 exe(W2-10)의 ZIP 데이터 시작·길이: 꼬리(<see cref="SfxBuilder.TryReadTrailer"/>)가 맞고 그 자리가 ZIP 머리(로컬 머리·빈 ZIP 의 끝 머리)로
    /// 시작할 때만. 아니면 null. 스트림 위치는 바뀐다.
    /// </summary>
    internal static (long Start, long Length)? IljipSfxData(Stream s)
    {
        if (!SfxBuilder.TryReadTrailer(s, out long start, out long length)) return null;
        var sig = ReadAt(s, start, 4);
        if (sig.Length < 4) return null;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(sig);
        return v is 0x04034B50 or 0x06054B50 ? (start, length) : null;
    }

    private static long FindZipBase(Stream s, long len)
    {
        int tailLen = (int)Math.Min(len, 65535 + 22);
        long tailStart = len - tailLen;
        var tail = ReadAt(s, tailStart, tailLen);
        for (int i = tail.Length - 22; i >= 0; i--)
        {
            if (tail[i] != 0x50 || tail[i + 1] != 0x4B || tail[i + 2] != 0x05 || tail[i + 3] != 0x06) continue;
            long eocd = tailStart + i;
            long cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12, 4));
            long cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16, 4));
            long cdActual = eocd - cdSize;
            if (cdActual < 0) continue;
            var cd = ReadAt(s, cdActual, 46);
            if (cd.Length < 46 || BinaryPrimitives.ReadUInt32LittleEndian(cd) != 0x02014B50) continue;
            long local = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(42, 4));
            // 절대 위치(반디집 SFX 처럼 파일 처음 기준) 또는 압축 시작 기준(앞에 붙인 exe 만큼 밀린 것)
            foreach (long zipBase in new[] { 0L, cdActual - cdOffset })
            {
                long cand = zipBase + local;
                if (zipBase < 0 || cand <= 0 || cand + 4 > len) continue;
                var sig = ReadAt(s, cand, 4);
                if (sig.Length == 4 && BinaryPrimitives.ReadUInt32LittleEndian(sig) == 0x04034B50) return zipBase;
            }
        }
        return -1;
    }

    private static byte[] ReadAt(Stream s, long pos, int count)
    {
        if (count <= 0) return Array.Empty<byte>();
        s.Position = pos;
        var buf = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) break;
            got += n;
        }
        return got == count ? buf : buf[..got];
    }

    /// <summary>안쪽 tar 머리를 볼 때 입력을 정해진 양까지만 읽게 하는 감싸개(큰 파일을 끝까지 읽지 않게).</summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _left;

        public LimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _left = limit;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_left <= 0) return 0;
            int n = _inner.Read(buffer, offset, (int)Math.Min(count, _left));
            _left -= n;
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
