using System.Diagnostics;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-2] 압축할 원본 열기(<see cref="SourceFileOpener"/>). 다른 프로그램(아웃룩 PST·로그 등)이 쓰기로 열어 두었어도 공유를 허락했으면 읽고,
/// 공유를 아예 막았으면 '다른 프로그램이 쓰고 있어요'로 실패한다(예외 없음). 같은 프로세스 안에서 FileStream 으로 잠가 흉내 낸다
/// (Windows 의 공유 모드는 핸들마다 걸려 같은 프로세스에도 똑같이 적용된다).
/// </summary>
public class SourceFileOpenerTests
{
    [Fact]
    public void 쓰기_공유로_열린_파일은_읽힌다()
    {
        using var tmp = new TempDir("sfo");
        string p = tmp.P("메일함.pst");
        File.WriteAllText(p, "쓰는 중인 파일 내용");
        using var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        // 예전 방식(File.OpenRead = FileShare.Read)은 쓰기로 열린 파일을 못 연다 — 그래서 압축 전체가 실패했다(실측 근거)
        Assert.Throws<IOException>(() => File.OpenRead(p).Dispose());

        Assert.True(SourceFileOpener.TryOpenRead(p, out var s, out var reason, SourceOpenRetry.None));
        Assert.Null(reason);
        using (s)
        using (var r = new StreamReader(s!))
            Assert.Equal("쓰는 중인 파일 내용", r.ReadToEnd());
    }

    [Fact]
    public void 공유를_막은_파일은_다른_프로그램이_쓰고_있어요()
    {
        using var tmp = new TempDir("sfo2");
        string p = tmp.P("잠긴 파일.txt");
        File.WriteAllText(p, "x");
        using var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.False(SourceFileOpener.TryOpenRead(p, out var s, out var reason, SourceOpenRetry.None));
        Assert.Null(s);
        Assert.Equal(SourceFileOpener.ReasonInUse, reason);
        Assert.Equal("다른 프로그램이 쓰고 있어요", reason);
    }

    /// <summary>공유 위반이면 정한 횟수만큼 정한 간격으로 다시 해 본다(DLP 의 아주 짧은 잠금 대비).</summary>
    [Fact]
    public void 공유_위반이면_정한_만큼_다시_해_본다()
    {
        using var tmp = new TempDir("sfo3");
        string p = tmp.P("잠긴 파일.txt");
        File.WriteAllText(p, "x");
        using var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var sw = Stopwatch.StartNew();
        Assert.False(SourceFileOpener.TryOpenRead(p, out _, out _, new SourceOpenRetry(3, TimeSpan.FromMilliseconds(40))));
        Assert.True(sw.ElapsedMilliseconds >= 100, $"다시 해 보지 않은 것 같음({sw.ElapsedMilliseconds}ms — 40ms × 3 기대)");
    }

    /// <summary>잠금이 잠깐이면(다시 해 보는 사이에 풀리면) 열린다.</summary>
    [Fact]
    public async Task 잠깐_잠긴_파일은_다시_해_보면_열린다()
    {
        using var tmp = new TempDir("sfo4");
        string p = tmp.P("잠깐 잠김.txt");
        File.WriteAllText(p, "곧 풀림");
        var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150);
            other.Dispose();
        });

        Assert.True(SourceFileOpener.TryOpenRead(p, out var s, out _, new SourceOpenRetry(100, TimeSpan.FromMilliseconds(50))));
        s!.Dispose();
        await release;
    }

    [Fact]
    public void 없는_파일은_파일이_없어졌어요()
    {
        using var tmp = new TempDir("sfo5");
        Assert.False(SourceFileOpener.TryOpenRead(tmp.P("없음.txt"), out _, out var reason, SourceOpenRetry.None));
        Assert.Equal(SourceFileOpener.ReasonMissing, reason);
        Assert.False(SourceFileOpener.TryOpenRead(tmp.P("없는 폴더", "a.txt"), out _, out reason, SourceOpenRetry.None));
        Assert.Equal(SourceFileOpener.ReasonMissing, reason);
    }

    /// <summary>
    /// 공유는 허락했지만 파일(의 앞부분)을 잠근 경우(LockFileEx — DB 파일 등): 열기는 되는데 읽기가 막힌다. 열 때 첫 블록을 읽어 보므로
    /// '다른 프로그램이 쓰고 있어요'로 실패한다(예전엔 열기는 성공하고 압축하는 도중 엔진 오류로 전체가 실패 — W1-D 1차 검증).
    /// </summary>
    [Fact]
    public void 앞부분이_잠긴_파일은_열기에서_다른_프로그램이_쓰고_있어요()
    {
        using var tmp = new TempDir("sfo7");
        string p = tmp.P("업무.db");
        File.WriteAllBytes(p, new byte[1000]);
        using var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        other.Lock(0, 1000);

        // 보통 열기는 된다(공유 허락) — 읽기에서야 막힌다
        using (var plain = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.Throws<IOException>(() => plain.ReadByte());

        Assert.False(SourceFileOpener.TryOpenRead(p, out var s, out var reason, SourceOpenRetry.None));
        Assert.Null(s);
        Assert.Equal(SourceFileOpener.ReasonInUse, reason);
    }

    /// <summary>열 때 첫 블록을 읽어 봐도 돌려준 스트림은 처음부터 읽힌다(빈 파일도 열림).</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(200_000)]
    public void 첫_블록을_읽어_봐도_처음부터_읽힌다(int size)
    {
        using var tmp = new TempDir("sfo8");
        string p = tmp.P("자료.bin");
        var data = new byte[size];
        new Random(3).NextBytes(data);
        File.WriteAllBytes(p, data);

        Assert.True(SourceFileOpener.TryOpenRead(p, out var s, out _, SourceOpenRetry.None));
        using (s)
        using (var ms = new MemoryStream())
        {
            Assert.Equal(0, s!.Position);
            s.CopyTo(ms);
            Assert.Equal(data, ms.ToArray());
        }
    }

    /// <summary>뒤쪽 한 부분만 잠긴 파일: 열기는 되고, 읽는 도중 막히면 <see cref="SourceFileOpener.Read"/> 가 쉬운 이유의 SourceUnreadableException 으로 바꾼다.</summary>
    [Fact]
    public void 읽는_도중_막히면_SourceUnreadable로_바뀐다()
    {
        using var tmp = new TempDir("sfo9");
        string p = tmp.P("업무.db");
        File.WriteAllBytes(p, new byte[300_000]);
        using var other = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        other.Lock(200_000, 50_000);

        Assert.True(SourceFileOpener.TryOpenRead(p, out var s, out _, SourceOpenRetry.None));
        using (s)
        {
            var buffer = new byte[81920];
            var ex = Assert.Throws<SourceUnreadableException>(() =>
            {
                while (SourceFileOpener.Read(s!, buffer, p) > 0) { }
            });
            Assert.Equal(p, ex.SourcePath);
            Assert.Equal(SourceFileOpener.ReasonInUse, ex.Reason);
            Assert.IsType<IOException>(ex.InnerException);
        }
    }

    /// <summary>읽는 쪽이 FileShare.Delete 까지 허락하므로, 압축하는 동안에도 다른 프로그램이 그 파일을 지우거나 이름을 바꿀 수 있다.</summary>
    [Fact]
    public void 읽는_동안에도_다른_프로그램이_지울_수_있다()
    {
        using var tmp = new TempDir("sfo6");
        string p = tmp.P("임시.log");
        File.WriteAllText(p, "로그");
        Assert.True(SourceFileOpener.TryOpenRead(p, out var s, out _, SourceOpenRetry.None));
        using (s)
        {
            File.Move(p, tmp.P("옮김.log"));
        }
        Assert.True(File.Exists(tmp.P("옮김.log")));
    }
}
