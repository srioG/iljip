using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I5-B] 최종 감사 IB-zip-replace-cancel-deletes-old: 같은 이름 압축이 있을 때 [바꾸기]로 만들다 취소·실패하면 예전엔 ZIP(그리고 같은 길의 TAR·GZ·BZ2)이
/// 기존 파일에 바로 써서, 정리(CompressFlow — '시작 전과 달라진 파일' 지우기)가 그 파일을 지워 기존 압축도 새 압축도 없었다(7z·나눔·자동 풀림은 임시 파일이라 그대로).
/// 이제 기존 파일이 있으면 옆 임시 파일(<c>.~이름.8글자.tmp</c>)에 다 쓴 뒤에만 바꿔 넣는다(<see cref="OutputFiles.WriteReplacing"/>) —
/// 취소·쓰기 실패면 기존 파일 SHA-256 그대로, 임시 파일 0개. 다 되면 새 내용으로 바뀌고 임시 파일 0개.
/// 취소는 진행 창 [취소] 흉내, 쓰기 실패는 진행 보고에서 '디스크 공간이 모자라요'(0x80070070) 예외를 던지는 가짜(실제 디스크를 채우지 않음).
/// </summary>
public class CompressReplaceTests
{
    private static readonly byte[] Old = "OLD CONTENT — 어제 만든 자료(합성)"u8.ToArray();

    /// <summary>진행 창 흉내: 쓴 바이트가 처음 0을 넘으면 [취소](cancel) 또는 쓰기 실패 예외(fail).</summary>
    private sealed class MidwaySession : IProgressSession, IProgress<ArchiveProgress>
    {
        private readonly bool _fail;
        private readonly CancellationTokenSource _cts = new();
        private int _fired;

        public MidwaySession(bool fail) => _fail = fail;

        public bool Fired => Volatile.Read(ref _fired) == 1;
        public IProgress<ArchiveProgress> Progress => this;
        public CancellationToken CancellationToken => _cts.Token;
        public void Close() { }

        public void Report(ArchiveProgress value)
        {
            if (value.IsStage || value.BytesProcessed <= 0 || Interlocked.Exchange(ref _fired, 1) == 1) return;
            if (_fail) throw new IOException("디스크 공간이 모자라요.") { HResult = unchecked((int)0x80070070) };
            _cts.Cancel();
        }
    }

    public static TheoryData<string, string, bool> Cases()
    {
        var d = new TheoryData<string, string, bool>();
        foreach (var (format, opts) in new[] { ("Zip", "plain"), ("Zip", "aes"), ("Zip", "zipcrypto"), ("Zip", "store"), ("Tar", "plain"), ("GZip", "plain"), ("BZip2", "plain") })
            foreach (bool fail in new[] { false, true })
                d.Add(format, opts, fail);
        return d;
    }

    private static CompressionOptions OptionsFor(string opts) => opts switch
    {
        "aes" => new CompressionOptions { Password = "x", Level = CompressionLevelOption.Maximum },
        "zipcrypto" => new CompressionOptions { Password = "x", Encryption = ZipEncryption.ZipCrypto },
        "store" => new CompressionOptions { Level = CompressionLevelOption.Store },
        _ => CompressionOptions.Default,
    };

    /// <summary>
    /// 계약(B2): 같은 이름 압축이 있고 [바꾸기](OverwriteExisting)를 골랐는데 쓰는 도중 취소·쓰기 실패로 끝나면 기존 압축 파일은 바이트 그대로(SHA-256),
    /// 만들던 곳에 임시 파일·반쪽 파일이 하나도 남지 않는다. 결과는 '취소'(취소면) 또는 실패(이유는 쉬운 한국어).
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task 바꾸기로_만들다_취소나_실패하면_기존_파일은_그대로다(string format, string opts, bool fail)
    {
        using var tmp = new TempDir("repl" + format + opts);
        var fmt = Enum.Parse<CompressionFormat>(format);
        string src = tmp.Dir("원본", "큰자료");
        var rnd = new Random(1020);
        string single = Path.Combine(src, "자료1.bin");
        for (int i = 1; i <= 3; i++)
        {
            var b = new byte[400_000];
            rnd.NextBytes(b);
            File.WriteAllBytes(Path.Combine(src, $"자료{i}.bin"), b);
        }
        bool singleFile = fmt is CompressionFormat.GZip or CompressionFormat.BZip2;
        string outDir = tmp.Dir("만든곳");
        string baseName = singleFile ? "자료1.bin" : "기존";
        string target = Path.Combine(outDir, baseName + CompressionFormats.ExtensionOf(fmt));
        File.WriteAllBytes(target, Old);
        string oldSha = TreeAssert.Sha256(target);
        var session = new MidwaySession(fail);
        var flow = new CompressFlow(new ArchiveServiceLocator(), new FakeUserInteraction(), new FakeRecycleBin(), new FakeCompressTester().Pass(),
            new FakeClock().SetLocal(2026, 10, 20));

        var result = await flow.RunAsync(new CompressionRequest
        {
            Sources = singleFile ? new[] { single } : new[] { src },
            OutputFolder = outDir,
            BaseName = baseName,
            Format = fmt,
            Options = OptionsFor(opts),
            OverwriteExisting = true,
        }, session);

        string context = $"{format}/{opts}/{(fail ? "실패" : "취소")}";
        Assert.True(session.Fired, $"{context}: 쓰는 도중에 끊지 못함(시험 준비)");
        Assert.Equal(0, result.SucceededCount);
        Assert.Equal(!fail, result.Cancelled);
        Assert.True(File.Exists(target), $"{context}: 기존 압축 파일이 사라짐");
        Assert.Equal(oldSha, TreeAssert.Sha256(target));
        Assert.Equal(new[] { Path.GetFileName(target) }, Directory.GetFiles(outDir).Select(Path.GetFileName).ToArray());   // 임시·반쪽 파일 0개(숨김 포함)
        if (fail)
        {
            var o = Assert.Single(result.Outcomes);
            Assert.False(string.IsNullOrEmpty(o.Reason));
            Assert.True(ErrorCatalog.ContainsHangul(o.Reason!), o.Reason);
        }
    }

    /// <summary>
    /// 계약(회귀): [바꾸기]가 끝까지 되면 기존 파일이 새 압축으로 바뀌고(풀면 원본과 같음) 임시 파일이 남지 않는다. 원본 폴더 안에 둔 기존 압축(같은 이름)은
    /// 새 압축에 담기지 않는다(예전처럼 — 자기 자신을 담지 않음).
    /// </summary>
    [Theory]
    [InlineData("Zip")]
    [InlineData("Tar")]
    public async Task 바꾸기가_끝까지_되면_새_내용으로_바뀌고_임시_파일이_없다(string format)
    {
        using var tmp = new TempDir("replok" + format);
        var fmt = Enum.Parse<CompressionFormat>(format);
        string src = tmp.Dir("원본", "거래처");
        File.WriteAllText(Path.Combine(src, "견적.txt"), "새 견적");
        File.WriteAllText(Path.Combine(src, "메모.txt"), "새 메모");
        string target = Path.Combine(src, "거래처" + CompressionFormats.ExtensionOf(fmt));   // 원본 폴더 안에 저장(기존 파일이 원본 폴더 안에 있음)
        File.WriteAllBytes(target, Old);
        var flow = new CompressFlow(new ArchiveServiceLocator(), new FakeUserInteraction(), new FakeRecycleBin(), new FakeCompressTester().Pass(),
            new FakeClock().SetLocal(2026, 10, 20));

        var result = await flow.RunAsync(new CompressionRequest
        {
            Sources = new[] { src },
            OutputFolder = src,
            BaseName = "거래처",
            Format = fmt,
            OverwriteExisting = true,
        }, new FakeUserInteraction().ShowProgress("x"));

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(target, result.Outcomes[0].Target);
        Assert.NotEqual(TreeAssert.Sha256(Old), TreeAssert.Sha256(target));
        var names = (await Svc.For(target).ListEntriesAsync(target)).Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "거래처/견적.txt", "거래처/메모.txt" }, names);   // 바꿔 넣은 기존 압축(원본 폴더 안)은 담지 않음
        Assert.DoesNotContain(Directory.GetFiles(src), f => Path.GetFileName(f).StartsWith(OutputFiles.TempPrefix, StringComparison.Ordinal));
    }
}
