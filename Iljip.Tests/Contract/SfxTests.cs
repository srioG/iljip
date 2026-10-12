using System.Buffers.Binary;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-B 2차 검증 지적] 자동 풀림 exe 는 ZIP·7z 방식만 알아봤다 — 국내에서 흔한 WinRAR 자동 풀림(RAR)과 알집 자동 풀림(EGG·ALZ)은
/// '지원하지 않는 형식이에요: .exe' 로 끝났다. SFX 모듈 뒤(PE 섹션이 끝나는 곳)에 붙은 압축을 서명 + 바로 뒤 머리 검증으로 찾아 연다.
/// 실물 WinRAR·알집 SFX 는 없어(이 PC 에 WinRAR·알집 없음) 진짜 PE 머리(섹션 표)를 가진 가짜 SFX 모듈 + 합성 압축으로 시험한다 —
/// 실물 확인은 MANUAL_CHECKLIST.
/// </summary>
public class SfxTests
{
    /// <summary>
    /// 섹션 하나짜리 PE 머리를 가진 가짜 SFX 모듈(<paramref name="size"/> 바이트, 마지막 섹션이 파일 <paramref name="size"/> 에서 끝남).
    /// 64KB 보다 크게 만들면 '앞 64KB 훑기'로는 못 찾고 'PE 섹션 끝(덧붙은 영역)'에서만 찾을 수 있다 — 실제 SFX 모듈 크기(수백 KB)를 흉내.
    /// 모듈 안 곳곳에 서명 글자만 흩어 둔다(검증 없이 서명만 찾으면 엉뚱한 자리를 고르게).
    /// </summary>
    internal static byte[] FakeSfxModule(int size)
    {
        var b = new byte[size];
        b[0] = (byte)'M';
        b[1] = (byte)'Z';
        const int pe = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0x3C), pe);
        "PE\0\0"u8.CopyTo(b.AsSpan(pe));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(pe + 4), 0x8664);   // x64
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(pe + 6), 1);        // 섹션 1개
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(pe + 20), 0);       // 선택 머리 없음
        int sct = pe + 24;
        ".text\0\0\0"u8.CopyTo(b.AsSpan(sct));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(sct + 16), (uint)(size - 0x400));   // 디스크 크기
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(sct + 20), 0x400);                  // 디스크 위치
        // 모듈 코드 속 '우연한' 서명 글자(뒤 머리가 맞지 않음)
        foreach (var (at, sig) in new (int, byte[])[]
                 {
                     (0x500, "Rar!\x1A\x07\x01\x00"u8.ToArray()), (0x600, "Rar!\x1A\x07\x00"u8.ToArray()),
                     (0x700, "EGGA"u8.ToArray()), (0x800, "ALZ\x01"u8.ToArray()), (0x900, new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }),
                 })
            sig.CopyTo(b, at);
        return b;
    }

    private static readonly RawRar.Item[] Items =
    {
        new("안내문.txt", Encoding.UTF8.GetBytes("자동 풀림 안내")),
        new("자료/표.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n")),
    };

    [Theory]
    [InlineData(100_000, false)]   // 실제 SFX 처럼 64KB 넘는 모듈 뒤(덧붙은 영역에서 찾음)
    [InlineData(4_096, true)]      // 작은 모듈(앞 64KB 안에서 찾음), RAR4
    public async Task WinRAR_자동풀림_exe도_열고_푼다(int moduleSize, bool rar4)
    {
        using var tmp = new TempDir("sfxr");
        byte[] rar = rar4
            ? RawRar.Rar4(new[] { ("readme.txt", Encoding.ASCII.GetBytes("rar4 sfx")), ("sub\\b.txt", Encoding.ASCII.GetBytes("bbb")) }).Bytes
            : RawRar.Rar5(Items).Bytes;
        string exe = tmp.P("설치.exe");
        File.WriteAllBytes(exe, FakeSfxModule(moduleSize).Concat(rar).ToArray());

        Assert.Equal(ArchiveKind.RarSfx, ArchiveSniffer.Detect(exe, out long offset));
        Assert.Equal(moduleSize, offset);
        var svc = Svc.For(exe);
        Assert.IsType<SfxArchiveService>(svc);
        var names = (await svc.ListEntriesAsync(exe)).Where(e => !e.IsDirectory).Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal).ToList();
        string[] expected = rar4 ? new[] { "readme.txt", "sub/b.txt" } : new[] { "안내문.txt", "자료/표.csv" };
        Assert.Equal(expected, names);
        string dest = tmp.P("out");
        await svc.ExtractAsync(exe, dest);
        Assert.Equal(expected, TreeAssert.Files(dest));
        string some = tmp.P("some");
        await svc.ExtractEntriesAsync(exe, some, new[] { expected[1] });
        Assert.Equal(new[] { expected[1] }, TreeAssert.Files(some));
    }

    [Fact]
    public async Task 알집_EGG_자동풀림_exe도_열고_푼다()
    {
        using var tmp = new TempDir("sfxe");
        string egg = tmp.P("inner.egg");
        RawEgg.Write(egg, Items.Select(i => new RawEgg.Item(i.Name, i.Data)).ToList());
        string exe = tmp.P("알집.exe");
        File.WriteAllBytes(exe, FakeSfxModule(100_000).Concat(File.ReadAllBytes(egg)).ToArray());

        Assert.Equal(ArchiveKind.EggSfx, ArchiveSniffer.Detect(exe, out long offset));
        Assert.Equal(100_000, offset);
        var svc = Svc.For(exe);
        Assert.Equal(new[] { "안내문.txt", "자료/표.csv" }, (await svc.ListEntriesAsync(exe)).Where(e => !e.IsDirectory).Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
        string dest = tmp.P("out");
        await svc.ExtractAsync(exe, dest);
        Assert.Equal(new[] { "안내문.txt", "자료/표.csv" }, TreeAssert.Files(dest));
        Assert.Equal("자동 풀림 안내", File.ReadAllText(Path.Combine(dest, "안내문.txt")));
    }

    /// <summary>ALZ 자동 풀림: 알집이 CP949 로 쓴 한글 이름도 안쪽 ALZ 위치부터 원본 바이트를 읽어 바르게 보인다(B10 우회가 SFX 에서도).</summary>
    [Fact]
    public async Task 알집_ALZ_자동풀림_exe도_한글_이름으로_열고_푼다()
    {
        using var tmp = new TempDir("sfxa");
        var cp949 = Encoding.GetEncoding(949);
        string alz = tmp.P("inner.alz");
        RawAlz.Write(alz, new RawAlz.Item(cp949.GetBytes("한글 파일.txt"), Encoding.UTF8.GetBytes("알집 내용")), new RawAlz.Item(Encoding.ASCII.GetBytes("plain.txt"), Encoding.ASCII.GetBytes("plain")));
        string exe = tmp.P("알집.exe");
        File.WriteAllBytes(exe, FakeSfxModule(100_000).Concat(File.ReadAllBytes(alz)).ToArray());

        Assert.Equal(ArchiveKind.AlzSfx, ArchiveSniffer.Detect(exe, out long offset));
        Assert.Equal(100_000, offset);
        var svc = Svc.For(exe);
        Assert.Equal(new[] { "plain.txt", "한글 파일.txt" }, (await svc.ListEntriesAsync(exe)).Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
        string dest = tmp.P("out");
        await svc.ExtractAsync(exe, dest);
        Assert.Equal(new[] { "plain.txt", "한글 파일.txt" }, TreeAssert.Files(dest));
    }

    /// <summary>
    /// 계약: '열기' 창에 '자동 풀림 압축 파일 (*.exe)' 칸이 있고, 거기서 고른 자동 풀림 exe 가 화면에 열린다
    /// (예전 '지원되는 압축 파일' 칸에는 .exe 가 없어 '모든 파일'로 바꿔야만 보였다 — W1-B 2차 검증 지적).
    /// </summary>
    [Fact]
    public async Task 열기_창에서_자동풀림_exe를_골라_연다()
    {
        using var tmp = new TempDir("sfxo");
        string exe = tmp.P("설치.exe");
        File.WriteAllBytes(exe, FakeSfxModule(100_000).Concat(RawRar.Rar5(Items).Bytes).ToArray());
        var ui = new FakeUserInteraction { FilesToPick = new[] { exe } };
        var vm = new Iljip.ViewModels.MainViewModel(ui);
        await vm.OpenCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(60));

        string filter = Assert.Single(ui.PickFilesFilters)!;
        var parts = filter.Split('|');
        int exeAt = Array.IndexOf(parts, "*.exe");
        Assert.True(exeAt > 0 && exeAt % 2 == 1, $"'*.exe' 칸이 없음: {filter}");
        Assert.Contains("자동 풀림", parts[exeAt - 1]);
        Assert.Equal("*.*", parts[^1]);   // '모든 파일' 은 그대로 마지막
        Assert.Equal(exe, vm.CurrentArchivePath);
        Assert.Contains(vm.Entries, e => e.Path == "안내문.txt");
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
    }

    /// <summary>모듈 코드 속 서명 글자만 있고 뒤에 압축이 없는 exe(보통 프로그램)는 SFX 로 보지 않는다(미지원 그대로).</summary>
    [Fact]
    public void 서명_글자만_있는_보통_exe는_압축이_아니다()
    {
        using var tmp = new TempDir("sfxn");
        string exe = tmp.P("보통.exe");
        File.WriteAllBytes(exe, FakeSfxModule(100_000));
        Assert.Equal(ArchiveKind.Unknown, ArchiveSniffer.Detect(exe));
        Assert.Null(Svc.Locator.Resolve(exe));
    }
}
