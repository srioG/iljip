using System.Text;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [흘려 쓴 암호 ZIP] SharpZipLib 의 ZipOutputStream 을 되감지 못하는(쓰기 전용) 스트림에 암호로 쓴 ZIP — Java·서버가 흘려 쓴 암호 ZIP 모양:
/// 항목마다 암호 + 데이터 서술자(bit 3), 로컬 머리의 크기 칸 0(ZIP64 추가 필드가 있어도 0), 진짜 크기·CRC 는 서술자와 목록에.
/// <para>
/// 예전(I4-1 남김): 온전한 원본인데도 맞는 암호로 못 풀었다 — '파일이 손상됐거나 끝까지 받아지지 않은 것 같아요'(안쪽 'Cannot determine compressed stream type').
/// 원인은 풀기 엔진(SharpCompress 0.50.5)이 '암호 + bit 3 + 로컬 압축 크기 0' 로컬 머리를 목록으로 여는 길에서도 NotSupportedException 으로 거절하는 것
/// (첫 항목이 그 모양이면 형식 알아보기부터 실패). 일집의 암호 판정(<see cref="ZipPasswordProbe"/>)은 맞게 했다.
/// 고침: 그 칸만 목록 값으로 덮어 보이는 스트림으로 연다(<see cref="StreamedZipHeaders"/>, 파일은 그대로) — 목록·풀기·시험(검사)·압축 고치기 뒤 검사가 같은 길.
/// </para>
/// </summary>
public class StreamedEncryptedZipTests
{
    private const string Password = "pass-1234";

    /// <summary>되감지 못하는 쓰기 전용 스트림(SharpZipLib 가 데이터 서술자를 쓰고 로컬 머리의 크기를 비워 두게).</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    /// <summary>재료의 모양.</summary>
    public enum Shape
    {
        /// <summary>재현 재료 그대로(ZipCrypto·Deflate 6·ZIP64 추가 필드 — ZipRepairHonestTests 의 흘려 쓴 ZipCrypto 와 같음).</summary>
        ZipCrypto,
        /// <summary>수준 0(저장에 가까운 Deflate).</summary>
        ZipCryptoLevel0,
        /// <summary>ZIP64 끔 — 로컬 머리에 ZIP64 추가 필드 없이 크기 칸 0, 목록도 4바이트 크기.</summary>
        ZipCryptoNoZip64,
        /// <summary>빈 파일·빈 폴더가 섞임(빈 파일도 암호 머리 12바이트).</summary>
        ZipCryptoEmptyAndFolder,
        /// <summary>한글 암호를 CP949 로(반디집·알집과 같은 암호 바이트).</summary>
        ZipCryptoKoreanCp949,
        /// <summary>한글 암호를 UTF-8 로(Java·서버가 흔히 쓰는 암호 바이트).</summary>
        ZipCryptoKoreanUtf8,
        /// <summary>WinZip AES-256 — 같은 원인(로컬 압축 크기 0 + 암호 + bit 3)으로 형식 알아보기부터 실패하던 모양.</summary>
        Aes256,
        /// <summary>로컬 머리의 크기 칸이 0xFFFFFFFF 이고 ZIP64 추가 필드의 값이 0(다른 흘려 쓰기 도구의 모양).</summary>
        ZipCryptoLocalZip64Marker,
    }

    /// <summary>ZipCrypto 암호를 바이트로 바꾸는 방식(한글 CP949 재료만 CP949, 나머지는 UTF-8 — ASCII 암호는 둘이 같음).</summary>
    private static SharpZip.StringCodec CodecOf(Shape shape) =>
        SharpZip.StringCodec.Default.WithZipCryptoEncoding(shape == Shape.ZipCryptoKoreanCp949 ? Encoding.GetEncoding(949) : Encoding.UTF8);

    internal static string PasswordOf(Shape shape) => shape is Shape.ZipCryptoKoreanCp949 or Shape.ZipCryptoKoreanUtf8 ? "흘림암호가1" : Password;

    /// <summary>흘려 쓴 암호 ZIP 바이트와 그 안의 파일(이름 → 내용, 폴더는 null).</summary>
    internal static (byte[] Zip, Dictionary<string, byte[]?> Items) Build(Shape shape)
    {
        string password = PasswordOf(shape);
        var items = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        for (int i = 0; i < 3; i++)
            items[$"흘림/암호{i}.txt"] = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 암호 데이터 ", 500)));
        if (shape == Shape.ZipCryptoEmptyAndFolder)
        {
            items["흘림/빈 파일.txt"] = Array.Empty<byte>();
            items["흘림/빈 폴더/"] = null;
        }

        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(new ForwardOnly(ms), CodecOf(shape)) { IsStreamOwner = false, Password = password })
        {
            zos.SetLevel(shape == Shape.ZipCryptoLevel0 ? 0 : 6);
            if (shape == Shape.ZipCryptoNoZip64) zos.UseZip64 = SharpZip.UseZip64.Off;
            foreach (var (name, data) in items)
            {
                zos.PutNextEntry(new SharpZip.ZipEntry(name)
                {
                    IsUnicodeText = true,
                    DateTime = new DateTime(2026, 10, 1, 9, 0, 0),
                    AESKeySize = shape == Shape.Aes256 ? 256 : 0,
                });
                if (data is { Length: > 0 }) zos.Write(data, 0, data.Length);
                zos.CloseEntry();
            }
            zos.Finish();
        }
        byte[] zip = ms.ToArray();
        if (shape == Shape.ZipCryptoLocalZip64Marker)
        {
            // 로컬 머리의 크기 칸을 0xFFFFFFFF 로(ZIP64 추가 필드 0x0001 의 원래 크기·압축 크기 값은 0 그대로)
            foreach (long at in LocalHeaderOffsets(zip))
            {
                Assert.Equal(0x0001, U16(zip, (int)at + 30 + U16(zip, (int)at + 26)));   // 재료 확인: 로컬 머리 첫 추가 필드가 ZIP64
                BitConverter.GetBytes(uint.MaxValue).CopyTo(zip, (int)at + 18);
                BitConverter.GetBytes(uint.MaxValue).CopyTo(zip, (int)at + 22);
            }
        }
        return (zip, items);
    }

    /// <summary>목록 기록이 가리키는 로컬 머리 위치(목록 차례).</summary>
    private static List<long> LocalHeaderOffsets(byte[] zip)
    {
        int eocd = zip.Length - 22;
        Assert.Equal(0x06054b50u, BitConverter.ToUInt32(zip, eocd));
        int count = U16(zip, eocd + 10), p = (int)BitConverter.ToUInt32(zip, eocd + 16);
        var list = new List<long>();
        for (int n = 0; n < count; n++)
        {
            Assert.Equal(0x02014b50u, BitConverter.ToUInt32(zip, p));
            int nameLen = U16(zip, p + 28), extraLen = U16(zip, p + 30), commentLen = U16(zip, p + 32);
            list.Add(BitConverter.ToUInt32(zip, p + 42));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return list;
    }

    private static int U16(byte[] b, int at) => BitConverter.ToUInt16(b, at);

    private static string Write(TempDir t, string name, byte[] zip)
    {
        string path = t.P(name);
        File.WriteAllBytes(path, zip);
        return path;
    }

    private static List<ExpectedEntry> Expected(Dictionary<string, byte[]?> items)
    {
        var list = new List<ExpectedEntry>();
        var dirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, data) in items)
        {
            string path = name.TrimEnd('/');
            for (int i = path.IndexOf('/'); i > 0; i = path.IndexOf('/', i + 1)) dirs.Add(path[..i]);
            if (data is null) dirs.Add(path);
            else list.Add(new ExpectedEntry(path, data.Length, TreeAssert.Sha256(data), false));
        }
        list.AddRange(dirs.Select(d => new ExpectedEntry(d, 0, null, true)));
        return list;
    }

    /// <summary>재료 확인: 흘려 쓴 암호 모양이고, 엔진은 파일 그대로는 알아보지 못하며, 만든 쪽(SharpZipLib)은 맞는 암호로 원래대로 푼다(원본이 온전함).</summary>
    private static void AssertMaterial(string zip, Shape shape, Dictionary<string, byte[]?> items)
    {
        byte[] b = File.ReadAllBytes(zip);
        foreach (long at in LocalHeaderOffsets(b))
        {
            int flags = U16(b, (int)at + 6);
            string name = Encoding.UTF8.GetString(b, (int)at + 30, U16(b, (int)at + 26));
            if (name.EndsWith('/')) continue;
            Assert.True((flags & 0x09) == 0x09, $"암호 + 서술자 항목이 아님: {name} 0x{flags:X}");
            uint csize = BitConverter.ToUInt32(b, (int)at + 18);
            Assert.True(csize == (shape == Shape.ZipCryptoLocalZip64Marker ? uint.MaxValue : 0u), $"로컬 머리 압축 크기 칸: {name} {csize}");
        }
        Assert.False(SharpCompress.Archives.ArchiveFactory.IsArchive(zip, out _), "재료 확인: 엔진이 파일 그대로는 ZIP 으로 알아보지 못해야 함");

        using var zf = new SharpZip.ZipFile(zip, CodecOf(shape)) { Password = PasswordOf(shape) };
        int files = 0;
        foreach (SharpZip.ZipEntry e in zf)
        {
            if (!e.IsFile) continue;
            using var s = zf.GetInputStream(e);
            using var m = new MemoryStream();
            s.CopyTo(m);
            Assert.Equal(TreeAssert.Sha256(items[e.Name]!), TreeAssert.Sha256(m.ToArray()));
            files++;
        }
        Assert.Equal(items.Count(kv => kv.Value is not null), files);
    }

    // ───────────── 풀기·목록 ─────────────

    /// <summary>
    /// 계약: 흘려 쓴 암호 ZIP(재현 재료 — 파일 3개, 그 변형)을 일집 풀기(내용으로 고른 처리기 → <see cref="ZipArchiveService"/>)가 맞는 암호로 모두 원래대로 푼다
    /// (예전: 'Cannot determine compressed stream type' → '파일이 손상됐거나…'). 목록은 암호 없이도 보이고 파일마다 암호 표시.
    /// </summary>
    [Theory]
    [InlineData(Shape.ZipCrypto)]
    [InlineData(Shape.ZipCryptoLevel0)]
    [InlineData(Shape.ZipCryptoNoZip64)]
    [InlineData(Shape.ZipCryptoEmptyAndFolder)]
    [InlineData(Shape.ZipCryptoKoreanCp949)]
    [InlineData(Shape.ZipCryptoKoreanUtf8)]
    [InlineData(Shape.Aes256)]
    [InlineData(Shape.ZipCryptoLocalZip64Marker)]
    public async Task 흘려_쓴_암호_ZIP을_맞는_암호로_원래대로_푼다(Shape shape)
    {
        using var t = new TempDir("szext");
        var (bytes, items) = Build(shape);
        string zip = Write(t, "흘린 암호.zip", bytes);
        AssertMaterial(zip, shape, items);

        var svc = new ArchiveServiceLocator().ResolveByContent(zip);
        Assert.IsType<ZipArchiveService>(svc);
        string dest = t.Dir("풀기");
        await svc.ExtractAsync(zip, dest, PasswordOf(shape));
        TreeAssert.ExtractedTreeEquals(dest, Expected(items), context: shape.ToString());

        var listed = await svc.ListEntriesAsync(zip);
        Assert.Equal(items.Keys.Select(k => k.TrimEnd('/')).OrderBy(k => k, StringComparer.Ordinal),
            listed.Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(listed.Where(e => !e.IsDirectory), e => Assert.True(e.IsEncrypted, e.Path));
        Assert.Equal(TreeAssert.Sha256(bytes), TreeAssert.Sha256(zip));   // 원본은 그대로(보정은 읽을 때만)
    }

    /// <summary>계약: 고른 파일만 풀기(선택 해제)도 같은 길 — 고른 하나만 나온다.</summary>
    [Fact]
    public async Task 흘려_쓴_암호_ZIP에서_고른_파일만_푼다()
    {
        using var t = new TempDir("szsel");
        var (bytes, items) = Build(Shape.ZipCrypto);
        string zip = Write(t, "흘린 암호.zip", bytes);
        string dest = t.Dir("풀기");
        await Svc.For(zip).ExtractEntriesAsync(zip, dest, new[] { "흘림/암호1.txt" }, Password);
        Assert.Equal(new[] { "흘림/암호1.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(items["흘림/암호1.txt"]!), TreeAssert.Sha256(Path.Combine(dest, "흘림", "암호1.txt")));
    }

    /// <summary>
    /// 계약: 틀린 암호는 '암호가 맞지 않아요'(화면이 다시 물음), 암호 없이는 '암호가 걸린…' — 손상으로 보이지 않는다. 틀린 암호로 풀면 아무것도 남기지 않는다.
    /// </summary>
    [Theory]
    [InlineData(Shape.ZipCrypto)]
    [InlineData(Shape.ZipCryptoKoreanCp949)]
    [InlineData(Shape.Aes256)]
    public async Task 흘려_쓴_암호_ZIP의_틀린_암호와_암호_없음은_암호_문제로_알린다(Shape shape)
    {
        using var t = new TempDir("szpw");
        string zip = Write(t, "흘린 암호.zip", Build(shape).Zip);
        var svc = Svc.For(zip);

        string wrongDest = t.P("틀림");
        var wrong = await Svc.Catch(() => svc.ExtractAsync(zip, wrongDest, "틀린-암호"));
        Assert.IsType<WrongPasswordException>(wrong);
        Assert.True(Internals.IsPasswordRelated(wrong!));
        TreeAssert.NoFiles(wrongDest);

        var none = await Svc.Catch(() => svc.ExtractAsync(zip, t.P("없음"), null));
        Assert.IsType<PasswordRequiredException>(none);
        Assert.True(Internals.IsPasswordRelated(none!));
    }

    /// <summary>
    /// 계약: 보정은 손상 확인을 끄지 않는다 — 둘째 파일의 암호화된 데이터 한가운데 바이트가 바뀌면 그 파일은 손상(검사값 불일치 등)으로 알리고(틀린 암호가 아님 — 판정이 암호를 확인함),
    /// 앞서 맞게 풀린 첫 파일은 남고 깨진 파일은 남기지 않는다(기존 해제 계약).
    /// </summary>
    [Fact]
    public async Task 흘려_쓴_암호_ZIP의_데이터_손상은_손상으로_알린다()
    {
        using var t = new TempDir("szbad");
        var (bytes, items) = Build(Shape.ZipCrypto);
        long second = LocalHeaderOffsets(bytes)[1];
        int dataStart = (int)second + 30 + U16(bytes, (int)second + 26) + U16(bytes, (int)second + 28);
        int csize = (int)(LocalHeaderOffsets(bytes)[2] - 24 - dataStart);   // 다음 머리 앞의 ZIP64 서술자(24바이트) 앞까지가 데이터
        bytes[dataStart + 12 + (csize - 12) / 2] ^= 0x5A;
        string zip = Write(t, "깨진 흘림.zip", bytes);

        string dest = t.Dir("풀기");
        var ex = await Svc.Catch(() => Svc.For(zip).ExtractAsync(zip, dest, Password));
        Assert.IsAssignableFrom<CorruptArchiveException>(ex);
        Assert.False(Internals.IsPasswordRelated(ex!));
        Assert.Equal(new[] { "흘림/암호0.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(items["흘림/암호0.txt"]!), TreeAssert.Sha256(Path.Combine(dest, "흘림", "암호0.txt")));
    }

    // ───────────── 시험(검사)·압축 고치기 ─────────────

    /// <summary>
    /// 계약: 풀지 않고 끝까지 읽어 보는 시험(<see cref="SharpCompressArchiveServiceBase.TestEntriesSync"/> — 압축 고치기 뒤 검사·다 만들고 시험하기와 같은 길)도
    /// 맞는 암호로 모든 파일을 확인하고, 틀린 암호는 암호 문제로 알린다.
    /// </summary>
    [Theory]
    [InlineData(Shape.ZipCrypto)]
    [InlineData(Shape.ZipCryptoEmptyAndFolder)]
    [InlineData(Shape.Aes256)]
    public void 흘려_쓴_암호_ZIP을_맞는_암호로_시험한다(Shape shape)
    {
        using var t = new TempDir("sztest");
        var (bytes, items) = Build(shape);
        string zip = Write(t, "흘린 암호.zip", bytes);
        Assert.Equal(items.Count(kv => kv.Value is not null), new ZipArchiveService().TestEntriesSync(zip, PasswordOf(shape), _ => true, CancellationToken.None));
        Assert.ThrowsAny<ArchivePasswordException>(() => new ZipArchiveService().TestEntriesSync(zip, "틀린-암호", _ => true, CancellationToken.None));
    }

    /// <summary>
    /// 계약: 흘려 쓴 암호 ZIP 도 압축 고치기(지우기)가 된다 — 고친 결과 검사가 풀기와 같은 모양으로 엔진에 묻는다(예전: '일집이 풀 수 없는 방식…고치지 않았어요').
    /// 암호를 알든 모르든(모르면 암호 항목은 바이트 그대로 옮기고 구조만 봄) 남은 파일은 맞는 암호로 원래대로 풀린다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 흘려_쓴_암호_ZIP도_압축_고치기로_파일을_지운다(bool withPassword)
    {
        using var t = new TempDir("szedit");
        var (bytes, items) = Build(Shape.ZipCrypto);
        string zip = Write(t, "흘린 암호.zip", bytes);

        await new ZipArchiveEditor().EditAsync(zip, new EditOp[] { new EditOp.Delete(new[] { "흘림/암호1.txt" }) },
            withPassword ? Password : null, null, CancellationToken.None);

        items.Remove("흘림/암호1.txt");
        string dest = t.Dir("풀기");
        await Svc.For(zip).ExtractAsync(zip, dest, Password);
        TreeAssert.ExtractedTreeEquals(dest, Expected(items));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    // ───────────── 보정의 범위 ─────────────

    /// <summary>
    /// 계약(보정의 범위): 바꾸는 칸은 흘려 쓴 암호 항목의 로컬 머리 압축 크기 칸뿐(그 항목 수만큼, 로컬 머리 + 18). 되감으며 쓴 암호 ZIP·흘려 쓴 암호 없는 ZIP
    /// (엔진이 원래 읽음)·ZIP 이 아닌 것·잘린 ZIP 은 아무것도 바꾸지 않는다(예외 없음 — 예전과 같은 길).
    /// </summary>
    [Fact]
    public void 보정은_흘려_쓴_암호_항목의_크기_칸만_바꾼다()
    {
        var (streamed, _) = Build(Shape.ZipCrypto);
        var patches = Patches(streamed);
        var offsets = LocalHeaderOffsets(streamed);
        Assert.Equal(offsets.Select(o => o + 18), patches.Select(p => p.Position));
        Assert.All(patches, p => Assert.Equal(4, p.Bytes.Length));

        Assert.Empty(Patches(Seekable(Password)));     // 되감으며 쓴 암호 ZIP(로컬 머리에 크기가 있음)
        Assert.Empty(Patches(Streamed(null)));         // 흘려 썼지만 암호 없음(서술자만 — 엔진이 원래 읽음)
        Assert.Empty(Patches(Encoding.UTF8.GetBytes("ZIP 이 아님")));
        Assert.Empty(Patches(streamed[..(streamed.Length / 2)]));   // 목록이 없는(잘린) ZIP — 엔진에 그대로
    }

    private static List<(long Position, byte[] Bytes)> Patches(byte[] zip)
    {
        using var ms = new MemoryStream(zip, writable: false);
        return StreamedZipHeaders.FindPatches(ms);
    }

    private static byte[] Seekable(string? password)
    {
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(ms) { IsStreamOwner = false, Password = password })
            Put(zos);
        return ms.ToArray();
    }

    private static byte[] Streamed(string? password)
    {
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(new ForwardOnly(ms)) { IsStreamOwner = false, Password = password })
            Put(zos);
        return ms.ToArray();
    }

    private static void Put(SharpZip.ZipOutputStream zos)
    {
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("데이터 ", 300)));
        zos.PutNextEntry(new SharpZip.ZipEntry("가.txt") { IsUnicodeText = true });
        zos.Write(data, 0, data.Length);
        zos.CloseEntry();
        zos.Finish();
    }
}
