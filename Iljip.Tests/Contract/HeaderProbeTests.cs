using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-B, I1-7] 헤더(파일 이름) 암호 탐지와 숫자 분할 이어 붙이기.
/// 7z.dll 은 '헤더 암호인데 암호 없음/틀림'과 '손상'을 같은 오류로만 알려 주므로, 머리 바이트를 직접 읽어 구분한다.
/// RAR 는 실물 픽스처가 없어(반디집은 RAR 를 못 만듦) 규격대로 만든 합성 바이트로만 시험한다.
/// </summary>
public class HeaderProbeTests
{
    [Theory]
    [InlineData("7z_header_encrypted", true)]
    [InlineData("7z_header_encrypted_korean_pw", true)]
    [InlineData("7z_default", false)]
    [InlineData("py7z_pw_names_visible", false)]
    [InlineData("zip_default", false)]
    public void 헤더암호_7z를_머리에서_알아본다(string id, bool encrypted)
    {
        Assert.Equal(encrypted, ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(FixtureCatalog.Get(id).FullPath));
    }

    /// <summary>머리를 못 읽는 파일(잘림·빈 파일)은 헤더 암호가 아니라고 본다(손상은 7z.dll 이 알림).</summary>
    [Fact]
    public void 잘린_7z는_헤더암호가_아니다()
    {
        using var tmp = new TempDir("probe");
        string p = tmp.P("cut.7z");
        File.WriteAllBytes(p, File.ReadAllBytes(FixtureCatalog.Get("7z_header_encrypted").FullPath)[..20]);
        Assert.False(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(p));
        File.WriteAllBytes(p, Array.Empty<byte>());
        Assert.False(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(p));
    }

    private static readonly byte[] Rar5Sig = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
    private static readonly byte[] Rar4Sig = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };

    /// <summary>RAR5: 서명 뒤 첫 머리(CRC 4 + 크기 vint + 종류 vint …)의 종류 4 = 암호 머리, 종류 1 = 주 머리(플래그 0x0001 = 볼륨).</summary>
    [Fact]
    public void RAR5_머리를_읽는다()
    {
        var crc = new byte[] { 0, 0, 0, 0 };
        var enc = ArchiveHeaderProbe.ProbeRar(Rar5Sig.Concat(crc).Concat(new byte[] { 0x10, 0x04, 0x00 }).ToArray());
        Assert.Equal(new ArchiveHeaderProbe.RarInfo(true, true, true, false), enc);

        // 주 머리: 크기 3, 종류 1, 머리 플래그 0, 압축 플래그 0x0001(볼륨)
        var vol = ArchiveHeaderProbe.ProbeRar(Rar5Sig.Concat(crc).Concat(new byte[] { 0x03, 0x01, 0x00, 0x01 }).ToArray());
        Assert.Equal(new ArchiveHeaderProbe.RarInfo(true, true, false, true), vol);

        // 머리 플래그 0x0001(덧붙은 영역 크기 vint 있음) 뒤에 압축 플래그 0
        var plain = ArchiveHeaderProbe.ProbeRar(Rar5Sig.Concat(crc).Concat(new byte[] { 0x05, 0x01, 0x01, 0x07, 0x00 }).ToArray());
        Assert.Equal(new ArchiveHeaderProbe.RarInfo(true, true, false, false), plain);
    }

    /// <summary>RAR4: MAIN_HEAD(종류 0x73) 플래그 0x0080 = 헤더 암호, 0x0001 = 볼륨.</summary>
    [Theory]
    [InlineData(0x0080, true, false)]
    [InlineData(0x0001, false, true)]
    [InlineData(0x0081, true, true)]
    [InlineData(0x0000, false, false)]
    public void RAR4_주머리_플래그를_읽는다(int flags, bool headerEncrypted, bool volume)
    {
        var main = new byte[] { 0x00, 0x00, 0x73, (byte)(flags & 0xFF), (byte)(flags >> 8), 0x0D, 0x00, 0, 0, 0, 0, 0, 0 };
        var info = ArchiveHeaderProbe.ProbeRar(Rar4Sig.Concat(main).ToArray());
        Assert.Equal(new ArchiveHeaderProbe.RarInfo(true, false, headerEncrypted, volume), info);
    }

    [Fact]
    public void RAR_가_아니면_모두_거짓()
    {
        Assert.Equal(default, ArchiveHeaderProbe.ProbeRar("PK\x03\x04 not rar"u8));
        Assert.Equal(default, ArchiveHeaderProbe.ProbeRar(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>
    /// 여러 조각 RAR 의 빠진 조각 추정: 마지막 조각 끝 머리의 '다음 조각 있음' 표시를 보고 그다음 이름을 댄다.
    /// RAR5 끝 머리 = CRC 4 + 크기 03 + 종류 05 + 머리 플래그 04 + 끝 플래그(0x01 = 마지막 아님).
    /// </summary>
    [Fact]
    public void RAR5_조각이_빠지면_다음_조각_이름을_댄다()
    {
        using var tmp = new TempDir("rarvol");
        string dir = tmp.Dir("v");
        byte[] main = Rar5Sig.Concat(new byte[] { 0, 0, 0, 0, 0x03, 0x01, 0x00, 0x01 }).ToArray();
        byte[] notLast = { 0x11, 0x22, 0x33, 0x44, 0x03, 0x05, 0x04, 0x01 };
        byte[] last = { 0x11, 0x22, 0x33, 0x44, 0x03, 0x05, 0x04, 0x00 };
        File.WriteAllBytes(Path.Combine(dir, "자료.part1.rar"), main.Concat(new byte[100]).Concat(notLast).ToArray());
        File.WriteAllBytes(Path.Combine(dir, "자료.part2.rar"), main.Concat(new byte[100]).Concat(notLast).ToArray());
        Assert.Equal("자료.part3.rar", ArchiveHeaderProbe.MissingRarVolume(Path.Combine(dir, "자료.part1.rar")));

        File.WriteAllBytes(Path.Combine(dir, "자료.part3.rar"), main.Concat(new byte[100]).Concat(last).ToArray());
        Assert.Null(ArchiveHeaderProbe.MissingRarVolume(Path.Combine(dir, "자료.part1.rar")));   // 다 있음 → 손상으로 봄
    }

    /// <summary>RAR4 옛 이름 규칙(.rar → .r00 → .r01)과 ENDARC_HEAD(0x7B) 플래그 0x0001.</summary>
    [Fact]
    public void RAR4_옛_조각_이름도_댄다()
    {
        using var tmp = new TempDir("rarvol4");
        string dir = tmp.Dir("v");
        byte[] main = Rar4Sig.Concat(new byte[] { 0x00, 0x00, 0x73, 0x01, 0x00, 0x0D, 0x00, 0, 0, 0, 0, 0, 0 }).ToArray();
        byte[] endMore = { 0x00, 0x00, 0x7B, 0x01, 0x00, 0x07, 0x00 };
        File.WriteAllBytes(Path.Combine(dir, "옛.rar"), main.Concat(new byte[50]).Concat(endMore).ToArray());
        File.WriteAllBytes(Path.Combine(dir, "옛.r00"), main.Concat(new byte[50]).Concat(endMore).ToArray());
        Assert.Equal("옛.r01", ArchiveHeaderProbe.MissingRarVolume(Path.Combine(dir, "옛.rar")));
    }

    /// <summary>숫자 분할 이어 붙이기: 조각 경계를 넘는 읽기·위치 이동이 원래 파일과 같다.</summary>
    [Fact]
    public void 숫자분할_조각을_이어붙이면_원래_파일과_같다()
    {
        using var tmp = new TempDir("split");
        var data = new byte[10_000];
        new Random(4).NextBytes(data);
        int[] cuts = { 0, 3000, 3000, 7777, 10_000 };   // 길이 0 조각도 섞음
        for (int i = 0; i < cuts.Length - 1; i++)
            File.WriteAllBytes(tmp.P($"a.bin.{i + 1:000}"), data[cuts[i]..cuts[i + 1]]);

        Assert.True(SplitVolumeStream.IsNumberedFirstVolume(tmp.P("a.bin.001")));
        Assert.False(SplitVolumeStream.IsNumberedFirstVolume(tmp.P("a.bin.002")));
        Assert.False(SplitVolumeStream.IsNumberedFirstVolume(tmp.P("a.bin")));
        using var s = SplitVolumeStream.Open(tmp.P("a.bin.001"));
        Assert.Equal(4, s.Volumes.Count);
        Assert.Equal(data.Length, s.Length);
        Assert.Equal("a.bin.005", s.NextVolumeName);
        using var ms = new MemoryStream();
        s.CopyTo(ms, 777);
        Assert.Equal(data, ms.ToArray());

        s.Position = 2990;
        var buf = new byte[5000];
        int got = 0;
        while (got < buf.Length) got += s.Read(buf, got, buf.Length - got);
        Assert.Equal(data[2990..7990], buf);
        s.Seek(-10, SeekOrigin.End);
        Assert.Equal(10, s.Read(new byte[100], 0, 100));
    }
}
