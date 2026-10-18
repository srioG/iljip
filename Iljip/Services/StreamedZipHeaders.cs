using System.IO;

namespace Iljip.Services;

/// <summary>
/// 흘려 쓴 암호 ZIP(되감지 못하는 스트림에 쓴 것 — SharpZipLib 를 쓰기 전용 스트림에, zip4j 같은 Java·서버 쪽 흘려 쓰기)을 풀기 엔진(SharpCompress)이 읽게 하는 보정.
/// <para>
/// 그런 ZIP 의 암호 항목(ZipCrypto·WinZip AES)은 로컬 머리에 데이터 서술자 표시(bit 3)가 있고 압축 크기 칸이 0 이다(ZIP64 추가 필드가 있어도 0) —
/// 진짜 크기·CRC 는 데이터 뒤 서술자와 목록(중앙 디렉터리)에 있다. SharpCompress 0.50.5 는 로컬 머리를 읽을 때 '암호 + bit 3 + 압축 크기 0' 이면
/// 목록으로 여는(되감을 수 있는) 길에서도 NotSupportedException('…non-seekable Zip Streams with encrypted data…')을 던진다 — 바로 뒤에서 크기·CRC 를
/// 목록 값으로 덮어쓰는데도. 첫 항목이 그 모양이면 형식 알아보기(<c>ArchiveFactory.IsArchive</c>)부터 실패해 온전한 원본을 맞는 암호로 풀어도
/// 'Cannot determine compressed stream type'(화면: '파일이 손상됐거나 끝까지 받아지지 않은 것 같아요')이 났다(I4-1 남김 — 일집 암호 판정은 맞게 됨).
/// </para>
/// <para>
/// 그래서 목록이 가리키는 로컬 머리 중 그 모양인 것의 압축 크기 칸만 목록의 값으로 바꿔 보이는 읽기 전용 스트림을 엔진에 준다(파일은 그대로).
/// bit 3 항목은 엔진이 크기·CRC 를 목록 값으로 쓰고 ZipCrypto 확인 바이트도 수정 시각으로 맞춰 보므로, 바꾼 칸은 그 검사를 지나가는 데만 쓰인다
/// (내용 확인은 그대로 — 풀린 내용의 CRC·AES 인증값). 목록을 못 읽거나 그 모양이 없으면 아무것도 바꾸지 않는다(예전과 같은 길).
/// </para>
/// </summary>
internal static class StreamedZipHeaders
{
    /// <summary>목록을 이만큼까지만 읽는다(더 크면 보정하지 않음 — 엔진에 그대로).</summary>
    private const long MaxDirectoryBytes = 256L * 1024 * 1024;

    /// <summary>
    /// 흘려 쓴 암호 항목이 있으면 그 로컬 머리의 압축 크기 칸을 목록 값으로 덮어 보이는 스트림(<paramref name="zip"/> 을 소유 — 닫으면 같이 닫힘). 없으면 null(<paramref name="zip"/> 은 그대로).
    /// </summary>
    internal static Stream? TryWrap(Stream zip)
    {
        var patches = FindPatches(zip);
        zip.Position = 0;
        return patches.Count == 0 ? null : new PatchedReadStream(zip, patches);
    }

    /// <summary>
    /// 풀기 엔진이 이 파일을 압축으로 알아보는가 — 풀기와 같은 모양으로 본다(흘려 쓴 암호 항목이 있으면 <see cref="TryWrap"/> 로 고친 모양).
    /// 압축 고치기의 검사(<see cref="Editing.ArchiveTester"/>)가 '일집이 풀 수 없는 방식'을 가를 때 쓴다(예전엔 파일 그대로 물어 이 모양을 못 푸는 방식으로 봄).
    /// </summary>
    internal static bool EngineRecognizes(string path)
    {
        if (SharpCompress.Archives.ArchiveFactory.IsArchive(path, out _)) return true;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var view = TryWrap(s);
        return view is not null && SharpCompress.Archives.Zip.ZipArchive.IsZipFile(view);
    }

    /// <summary>
    /// 바꿀 칸(로컬 머리 안 압축 크기 칸의 위치, 넣을 4바이트). 목록(끝 머리·ZIP64 끝 머리 — 디스크 0 하나)을 읽어 '암호 + bit 3' 기록마다 그 로컬 머리를 보고,
    /// 로컬 머리도 '암호 + bit 3' 이고 엔진이 읽을 압축 크기(칸, 칸이 0xFFFFFFFF 면 ZIP64 추가 필드의 값)가 0 인데 목록의 압축 크기는 0 보다 크면 고친다.
    /// 넣는 값은 목록의 압축 크기(4GB 이상이면 0xFFFFFFFE — 엔진은 bit 3 항목의 크기를 목록 값으로 바꿔 쓰므로 0 만 아니면 됨).
    /// 읽다 실패하거나 모양이 맞지 않으면 빈 목록(예외 없음 — 그때는 엔진이 예전처럼 알린다).
    /// </summary>
    internal static List<(long Position, byte[] Bytes)> FindPatches(Stream zip)
    {
        var patches = new List<(long, byte[])>();
        try
        {
            var end = ZipDirectory.FindEnd(zip);
            if (end is null) return patches;
            if (end.HasZip64Locator)
            {
                if (end.Zip64Disk != 0) return patches;
                ZipDirectory.ApplyZip64(zip, end, end.Zip64Offset);
            }
            if (end.ThisDisk != 0 || end.CdDisk != 0) return patches;   // 나눔 조각(나눔 세트는 보통 ZIP 처럼 보이게 한 스트림으로 온다)
            long cdStart = end.CdOffset, cdEnd = end.CdOffset + end.CdSize;
            if (cdStart < 0 || end.CdSize <= 0 || end.CdSize > MaxDirectoryBytes || cdEnd > end.DirectoryLimit) return patches;

            byte[] cd = ZipDirectory.ReadAt(zip, cdStart, (int)end.CdSize);
            if (!AnyEncryptedWithDescriptor(cd)) return patches;   // 거의 모든 ZIP — 기록마다 나눠 담지 않고 끝
            foreach (var r in ZipDirectory.ParseCentral(cd, end.TotalEntries))
            {
                if ((ZipDirectory.U16(r.Raw, 8) & 0x09) != 0x09 || r.CompressedSize <= 0) continue;   // 암호 + 서술자, 내용이 있는 것만
                long at = r.LocalOffset;
                if (at < 0 || at + ZipDirectory.LocalHeaderLength > cdStart) continue;
                byte[] h = ZipDirectory.ReadAt(zip, at, ZipDirectory.LocalHeaderLength);
                if (ZipDirectory.U32(h, 0) != ZipDirectory.LocalHeaderSig || (ZipDirectory.U16(h, 6) & 0x09) != 0x09) continue;
                if (LocalCompressedSize(zip, at, h, cdStart) != 0) continue;
                patches.Add((at + 18, BitConverter.GetBytes((uint)Math.Min(r.CompressedSize, uint.MaxValue - 1))));
            }
        }
        catch (Exception)
        {
            patches.Clear();   // 보정은 덤이다 — 못 하면 엔진에 그대로(읽기 실패의 진짜 이유는 엔진이 다시 알린다)
        }
        return patches;
    }

    /// <summary>목록 바이트에 '암호 + 서술자'(플래그 bit 0·3) 기록이 하나라도 있는가(기록 머리만 따라가며 플래그 칸을 본다 — 모양이 어긋나면 거기서 멈춤).</summary>
    private static bool AnyEncryptedWithDescriptor(byte[] cd)
    {
        for (int p = 0; p + ZipDirectory.CentralFixedLength <= cd.Length && ZipDirectory.U32(cd, p) == ZipDirectory.CentralSig;
             p += ZipDirectory.CentralFixedLength + ZipDirectory.U16(cd, p + 28) + ZipDirectory.U16(cd, p + 30) + ZipDirectory.U16(cd, p + 32))
            if ((ZipDirectory.U16(cd, p + 8) & 0x09) == 0x09) return true;
        return false;
    }

    /// <summary>
    /// 엔진이 로컬 머리에서 읽을 압축 크기: 칸 값, 칸이 0xFFFFFFFF 면 ZIP64 추가 필드(0x0001)의 값(크기 칸이 0xFFFFFFFF 면 그 앞에 원래 크기 8바이트).
    /// ZIP64 값을 못 찾으면 -1(고치지 않음).
    /// </summary>
    private static long LocalCompressedSize(Stream zip, long at, byte[] h, long limit)
    {
        long field = ZipDirectory.U32(h, 18);
        if (field != uint.MaxValue) return field;
        int nameLen = ZipDirectory.U16(h, 26), extraLen = ZipDirectory.U16(h, 28);
        long extraAt = at + ZipDirectory.LocalHeaderLength + nameLen;
        if (extraLen == 0 || extraAt + extraLen > limit) return -1;
        byte[] extra = ZipDirectory.ReadAt(zip, extraAt, extraLen);
        for (int x = 0; x + 4 <= extra.Length;)
        {
            int id = ZipDirectory.U16(extra, x), size = ZipDirectory.U16(extra, x + 2), d = x + 4;
            if (d + size > extra.Length) break;
            if (id == 0x0001)
            {
                int q = d + (ZipDirectory.U32(h, 22) == uint.MaxValue ? 8 : 0);
                return q + 8 <= d + size ? (long)Math.Min(ZipDirectory.U64(extra, q), long.MaxValue) : -1;
            }
            x = d + size;
        }
        return -1;
    }

    /// <summary>
    /// 안쪽 스트림을 읽으며 정해진 칸(4바이트, 서로 겹치지 않음)만 다른 바이트로 덮어 보이는 읽기 전용 스트림(안쪽 스트림을 소유).
    /// 칸은 위치 차례로 두고 읽을 때마다 이분 탐색으로 그 범위에 걸친 것만 본다(항목이 많은 ZIP 에서도 읽기마다 칸 전체를 훑지 않게).
    /// </summary>
    private sealed class PatchedReadStream : Stream
    {
        private const int Width = 4;
        private readonly Stream _inner;
        private readonly long[] _positions;
        private readonly byte[][] _bytes;

        public PatchedReadStream(Stream inner, List<(long Position, byte[] Bytes)> patches)
        {
            _inner = inner;
            var sorted = patches.OrderBy(p => p.Position).ToList();
            _positions = sorted.Select(p => p.Position).ToArray();
            _bytes = sorted.Select(p => p.Bytes).ToArray();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long start = _inner.Position;
            int n = _inner.Read(buffer);
            if (n <= 0) return n;
            // 이 읽기 범위 [start, start+n) 에 걸칠 수 있는 첫 칸(칸 시작이 start - Width 보다 뒤)부터
            int i = Array.BinarySearch(_positions, start - Width + 1);
            if (i < 0) i = ~i;
            for (; i < _positions.Length && _positions[i] < start + n; i++)
            {
                long pos = _positions[i];
                long from = Math.Max(pos, start), to = Math.Min(pos + Width, start + n);
                for (long p = from; p < to; p++) buffer[(int)(p - start)] = _bytes[i][p - pos];
            }
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
