using System.Windows.Media;
using System.Windows.Media.Imaging;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-04 — I3-7] 압축 속 그림 미리 보기(<see cref="ImagePreviewViewModel"/>)를 창 없이 — 가짜 그림 읽기(<see cref="FakeImageDecoder"/>)로
/// 넘기기(끝에서 처음으로)·확대 범위·50MB 자리표시·그림 아님·최근 3장/200MB·암호 단추·임시 파일 지우기를 본다.
/// 진짜 WIC 로 작은 PNG 를 읽는 시험 1개(STA, 창 없음)와 큰 그림 줄여 읽기 계산.
/// </summary>
public class ImagePreviewTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static string PhotoZip(TempDir tmp) => TestZip.Create(tmp.P("사진첩.zip"),
        TestZip.Bytes("사진/a.png", 3000, 1),
        TestZip.Bytes("사진/b.jpg", 3100, 2),
        TestZip.Bytes("사진/c.gif", 3200, 3),
        TestZip.Text("사진/메모.txt", "그림 아님"),
        TestZip.Bytes("사진/d.webp", 3300, 4),
        TestZip.Bytes("사진/e.BMP", 3400, 5),
        TestZip.Bytes("표지.png", 1000, 6));

    private sealed record Ctx(MainViewModel Vm, FakeUserInteraction Ui, TestServices Ts, string PreviewRoot);

    private static async Task<Ctx> OpenPhotos(TempDir tmp, string? zip = null, FakeUserInteraction? ui = null)
    {
        var ts = new TestServices();
        ui ??= new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: ts.Services);
        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip ?? PhotoZip(tmp)));
            vm.NavigateToFolder("사진");
            vm.Preview.IsVisible = true;
        }, "사진 압축 열기");
        return new Ctx(vm, ui, ts, AppPaths.PreviewRoot);
    }

    private static ArchiveEntry Visible(MainViewModel vm, string name) => vm.VisibleEntries.Single(e => e.Name == name);

    private static bool NoTempLeft(string root) => !Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any();

    [Fact]
    public async Task 그림을_고르면_그것만_꺼내_보여주고_임시파일은_바로_지운다()
    {
        using var tmp = new TempDir("pv");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        Assert.True(c.Vm.IsPreviewPaneVisible);
        var a = Visible(c.Vm, "a.png");
        await c.Vm.Preview.ShowAsync(a).WaitAsync(Limit);

        Assert.Equal(PreviewState.Image, c.Vm.Preview.State);
        Assert.True(c.Vm.Preview.HasImage);
        Assert.Same(a, c.Vm.Preview.CurrentEntry);
        var (name, bytes) = Assert.Single(c.Ts.Images.Decoded);
        Assert.Equal("a.png", name);
        Assert.Equal(TestZip.Bytes("x", 3000, 1).Item2, bytes);
        Assert.True(NoTempLeft(c.PreviewRoot), "읽은 뒤 미리 보기 임시 파일이 남음");
        Assert.Empty(c.Ui.ProgressSessions);   // 미리 보기는 진행 창 없이
    }

    [Fact]
    public async Task 이전_다음은_지금_폴더의_그림만_목록_차례로_돌고_목록_선택도_따라간다()
    {
        using var tmp = new TempDir("pvnav");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        var p = c.Vm.Preview;
        Assert.Equal(new[] { "a.png", "b.jpg", "c.gif", "d.webp", "e.BMP" }, p.ImageEntries.Select(e => e.Name));

        await p.ShowAsync(Visible(c.Vm, "e.BMP")).WaitAsync(Limit);
        await p.NextCommand.ExecuteAsync(null).WaitAsync(Limit);
        Assert.Equal("a.png", p.CurrentEntry!.Name);   // 끝 → 처음
        Assert.Same(p.CurrentEntry, c.Vm.SelectedEntry);
        await p.PrevCommand.ExecuteAsync(null).WaitAsync(Limit);
        Assert.Equal("e.BMP", p.CurrentEntry!.Name);   // 처음 → 끝
        await p.PrevCommand.ExecuteAsync(null).WaitAsync(Limit);
        Assert.Equal("d.webp", p.CurrentEntry!.Name);

        await p.ShowAsync(Visible(c.Vm, "메모.txt")).WaitAsync(Limit);
        await p.NextCommand.ExecuteAsync(null).WaitAsync(Limit);
        Assert.Equal("a.png", p.CurrentEntry!.Name);   // 그림이 아닌 데서 → 첫 그림
        Assert.DoesNotContain(c.Ts.Images.Decoded, d => d.Name == "메모.txt" || d.Name == "표지.png");   // 다른 폴더 그림은 안 넘어감
    }

    [Fact]
    public async Task 그림이_아니면_미리보기가_꺼진다()
    {
        using var tmp = new TempDir("pvtxt");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "메모.txt")).WaitAsync(Limit);
        Assert.Equal(PreviewState.NotImage, c.Vm.Preview.State);
        Assert.Null(c.Vm.Preview.Image);
        Assert.False(c.Vm.Preview.HasImage);
        Assert.Equal(ImagePreviewViewModel.NotImageMessage, c.Vm.Preview.Message);
        Assert.Single(c.Ts.Images.Decoded);   // 글 파일은 꺼내지도 읽지도 않음

        c.Vm.NavigateToFolder(string.Empty);
        await c.Vm.Preview.ShowAsync(c.Vm.VisibleEntries.Single(e => e.IsDirectory)).WaitAsync(Limit);
        Assert.Equal(PreviewState.NotImage, c.Vm.Preview.State);
    }

    [Theory]
    [InlineData(60L * 1024 * 1024, 1000L)]           // 원래 크기 60MB
    [InlineData(1000L, 51L * 1024 * 1024)]           // 압축 크기 51MB
    public async Task 크기가_50MB를_넘으면_꺼내지_않고_자리표시를_보인다(long size, long compressed)
    {
        using var tmp = new TempDir("pvbig");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        var big = new ArchiveEntry { Path = "사진/파노라마.png", Size = size, CompressedSize = compressed };
        await c.Vm.Preview.ShowAsync(big).WaitAsync(Limit);
        Assert.Equal(PreviewState.TooLarge, c.Vm.Preview.State);
        Assert.Equal("미리 보기에는 너무 커요", c.Vm.Preview.Message);
        Assert.Empty(c.Ts.Images.Decoded);
        Assert.True(NoTempLeft(c.PreviewRoot));
        Assert.True(ImagePreviewViewModel.IsTooLarge(big));
        Assert.False(ImagePreviewViewModel.IsTooLarge(new ArchiveEntry { Path = "x.png", Size = 50L * 1024 * 1024, CompressedSize = 49L * 1024 * 1024 }));
    }

    [Fact]
    public async Task 최근_3장만_기억하고_다시_보면_읽지_않는다()
    {
        using var tmp = new TempDir("pvlru");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        var p = c.Vm.Preview;
        foreach (var n in new[] { "a.png", "b.jpg", "c.gif", "d.webp" })
            await p.ShowAsync(Visible(c.Vm, n)).WaitAsync(Limit);
        Assert.Equal(new[] { "사진/d.webp", "사진/c.gif", "사진/b.jpg" }, p.CachedKeys);

        await p.ShowAsync(Visible(c.Vm, "b.jpg")).WaitAsync(Limit);   // 기억함 → 다시 안 읽음
        Assert.Equal(1, c.Ts.Images.DecodeCount("b.jpg"));
        Assert.Equal(PreviewState.Image, p.State);
        Assert.Equal("사진/b.jpg", p.CachedKeys[0]);

        await p.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);   // 밀려났음 → 다시 읽음
        Assert.Equal(2, c.Ts.Images.DecodeCount("a.png"));
        Assert.Equal(3, p.CachedKeys.Count);
    }

    [Fact]
    public async Task 기억하는_그림은_합계_200MB를_넘지_않는다()
    {
        using var tmp = new TempDir("pvcap");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        c.Ts.Images.BytesPerImage = 90L * 1024 * 1024;   // 한 장 90MB(큰 그림을 줄여 읽은 것)
        foreach (var n in new[] { "a.png", "b.jpg", "c.gif" })
            await c.Vm.Preview.ShowAsync(Visible(c.Vm, n)).WaitAsync(Limit);
        Assert.Equal(new[] { "사진/c.gif", "사진/b.jpg" }, c.Vm.Preview.CachedKeys);
        Assert.True(c.Vm.Preview.CachedBytes <= ImagePreviewViewModel.CacheBytes);

        c.Ts.Images.BytesPerImage = 300L * 1024 * 1024;   // 혼자 상한을 넘는 한 장 — 보여 주되 그것만 기억
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "d.webp")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, c.Vm.Preview.State);
        Assert.Equal(new[] { "사진/d.webp" }, c.Vm.Preview.CachedKeys);
    }

    [Fact]
    public void 확대는_맞춤_100퍼센트_25단계로_10에서_800까지()
    {
        var vm = new MainViewModel(new FakeUserInteraction(), services: new TestServices().Services);
        var p = vm.Preview;
        Assert.True(p.IsFit);
        Assert.Equal("맞춤", p.ZoomText);

        p.FitPercent = 37;   // 칸에 맞춘 실제 비율
        p.ZoomInCommand.Execute(null);
        Assert.False(p.IsFit);
        Assert.Equal(50, p.ZoomPercent);
        p.ZoomFitCommand.Execute(null);
        p.ZoomOutCommand.Execute(null);
        Assert.Equal(25, p.ZoomPercent);

        for (int i = 0; i < 50; i++) p.ZoomInCommand.Execute(null);
        Assert.Equal(800, p.ZoomPercent);
        for (int i = 0; i < 50; i++) p.ZoomOutCommand.Execute(null);
        Assert.Equal(10, p.ZoomPercent);
        p.ZoomInCommand.Execute(null);
        Assert.Equal(25, p.ZoomPercent);
        p.ZoomActualCommand.Execute(null);
        Assert.Equal(100, p.ZoomPercent);
        Assert.Equal("100%", p.ZoomText);
        Assert.Equal(1.0, p.ZoomScale);
        p.ZoomFitCommand.Execute(null);
        Assert.True(p.IsFit);
    }

    [Fact]
    public async Task 새_그림을_보이면_맞춤으로_돌아간다()
    {
        using var tmp = new TempDir("pvfit");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        c.Vm.Preview.ZoomActualCommand.Execute(null);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "b.jpg")).WaitAsync(Limit);
        Assert.True(c.Vm.Preview.IsFit);
    }

    [Fact]
    public async Task 암호를_모르면_묻지_않고_단추를_보이고_단추를_누르면_묻고_보인다()
    {
        using var tmp = new TempDir("pvpw");
        using var _ = TestServices.UseAppRoot(tmp);
        string src = tmp.Dir("원본");
        File.WriteAllBytes(Path.Combine(src, "비밀 사진.png"), TestZip.Bytes("x", 5000, 9).Item2);
        File.WriteAllText(Path.Combine(src, "메모.txt"), "x");
        string zip = tmp.P("암호사진.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, zip, new CompressionOptions { Password = FixtureCatalog.DefaultPassword });

        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword);
        var ts = new TestServices();
        var vm = new MainViewModel(ui, services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            vm.NavigateToFolder("원본");
            vm.Preview.IsVisible = true;
            await vm.Preview.ShowAsync(Visible(vm, "비밀 사진.png"));
        }, "암호 사진");

        Assert.Equal(PreviewState.NeedsPassword, vm.Preview.State);
        Assert.True(vm.Preview.IsPasswordNeeded);
        Assert.Equal("암호를 넣으면 볼 수 있어요", vm.Preview.Message);
        Assert.Empty(ui.PasswordPrompts);   // 고를 때마다 암호 창이 뜨지 않게
        Assert.Empty(ts.Images.Decoded);

        await VmRunner.Run(() => vm.Preview.EnterPasswordCommand.ExecuteAsync(null), "암호 단추");
        Assert.Single(ui.PasswordPrompts);
        Assert.Equal(PreviewState.Image, vm.Preview.State);
        Assert.Equal(TestZip.Bytes("x", 5000, 9).Item2, Assert.Single(ts.Images.Decoded).Bytes);
        Assert.True(NoTempLeft(AppPaths.PreviewRoot));
    }

    [Fact]
    public async Task 못_읽는_그림은_쉬운_문구를_보인다()
    {
        using var tmp = new TempDir("pverr");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        c.Ts.Images.FailName = "d.webp";   // WebP 코덱이 없는 PC
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "d.webp")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Error, c.Vm.Preview.State);
        Assert.Equal("이 그림은 미리 볼 수 없어요", c.Vm.Preview.Message);
        Assert.Empty(c.Ui.Errors);   // 오류 창은 띄우지 않음(칸에만)
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    [Fact]
    public async Task 다른_압축을_열면_미리보기를_비운다()
    {
        using var tmp = new TempDir("pvreset");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        await VmRunner.Run(() => c.Vm.OpenArchiveAsync(FixtureCatalog.Get("zip_default").FullPath), "다른 압축");
        Assert.Null(c.Vm.Preview.CurrentEntry);
        Assert.Empty(c.Vm.Preview.CachedKeys);
        Assert.Equal(PreviewState.Empty, c.Vm.Preview.State);
    }

    /// <summary>
    /// W2-04 1차 검증: 그림을 읽는 도중 다른 압축을 열면, 끝난 옛 읽기가 비운 기억에 다시 들어가 새 압축의 같은 경로 그림(IMG_0001.JPG·001.jpg 처럼 흔한 이름)
    /// 대신 옛 그림을 보였다. 압축이 바뀌기 전에 시작한 읽기의 결과는 기억하지 않는다.
    /// </summary>
    [Fact]
    public async Task 읽는_도중_다른_압축을_열면_옛_압축의_그림을_기억하지_않는다()
    {
        using var tmp = new TempDir("pvstale");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = TestZip.Create(tmp.P("A.zip"), TestZip.Bytes("사진/a.png", 3000, 1));
        string b = TestZip.Create(tmp.P("B.zip"), TestZip.Bytes("사진/a.png", 3000, 2));   // 경로는 같고 내용은 다름
        var c = await OpenPhotos(tmp, a);
        using var gate = new ManualResetEventSlim(false);
        c.Ts.Images.Gate = gate;

        var first = c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png"));   // A 의 a.png 를 읽는 도중에
        Assert.True(c.Ts.Images.WaitStarted(1, Limit), "그림 읽기가 시작되지 않음");
        await VmRunner.Run(() => c.Vm.OpenArchiveAsync(b), "B 열기");   // 다른 압축을 엶
        gate.Set();
        await first.WaitAsync(Limit);
        Assert.Empty(c.Vm.Preview.CachedKeys);   // 끝난 옛 읽기는 기억하지 않음
        Assert.Equal(PreviewState.Empty, c.Vm.Preview.State);

        c.Vm.NavigateToFolder("사진");
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, c.Vm.Preview.State);
        Assert.Equal(2, c.Ts.Images.DecodeCount("a.png"));   // B 의 그림을 새로 읽음(옛 그림을 보이지 않음)
        Assert.Equal(TestZip.Bytes("x", 3000, 2).Item2, c.Ts.Images.Decoded[^1].Bytes);
        Assert.Equal(new[] { "사진/a.png" }, c.Vm.Preview.CachedKeys);
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    /// <summary>
    /// IJ3-01(a): 압축을 바꾸는 바로 그 순간(새 목록으로 바꾸려고 옛 목록을 비우는 중 — 화면 선택이 바뀌어 미리 보기가 불림)에 시작한 읽기는
    /// 아직 옛 압축에서 꺼낸다. W2-04 보관본은 미리 보기를 비우며 세대를 먼저 올려, 이 읽기가 옛 그림을 새 세대로 기억했고 새 압축의 같은 경로 그림 대신 보였다.
    /// 이제 세대는 화면이 지금 압축을 바꾼 '뒤'에 올리고, 꺼내기·기억 모두 그 세대로 따진다.
    /// </summary>
    [Fact]
    public async Task 압축을_바꾸는_순간에_시작한_읽기도_옛_그림을_새_압축의_것으로_기억하지_않는다()
    {
        using var tmp = new TempDir("pvswitch");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = TestZip.Create(tmp.P("A.zip"), TestZip.Bytes("사진/a.png", 3000, 1));
        string b = TestZip.Create(tmp.P("B.zip"), TestZip.Bytes("사진/a.png", 3000, 2));   // 경로는 같고 내용은 다름
        var c = await OpenPhotos(tmp, a);
        var oldEntry = Visible(c.Vm, "a.png");
        Task? racing = null;
        void OnEntriesChanged(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset && racing is null)
                racing = c.Vm.Preview.ShowAsync(oldEntry);   // 목록을 비우는 순간 화면이 (옛) 선택으로 미리 보기를 부름
        }
        c.Vm.Entries.CollectionChanged += OnEntriesChanged;
        await VmRunner.Run(() => c.Vm.OpenArchiveAsync(b), "B 열기");
        c.Vm.Entries.CollectionChanged -= OnEntriesChanged;
        Assert.NotNull(racing);
        await racing!.WaitAsync(Limit);
        Assert.Empty(c.Vm.Preview.CachedKeys);   // 옛 압축에서 꺼낸 그림은 기억하지 않음
        Assert.NotEqual(PreviewState.Image, c.Vm.Preview.State);

        c.Vm.NavigateToFolder("사진");
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, c.Vm.Preview.State);
        Assert.Equal(TestZip.Bytes("x", 3000, 2).Item2, c.Ts.Images.Decoded[^1].Bytes);   // B 의 그림
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    /// <summary>
    /// 리눅스·맥에서 만든 압축엔 대소문자만 다른 두 그림('A.png'·'a.png')이 있을 수 있다. 예전엔 기억 열쇠·자리 찾기가 대소문자를 무시해
    /// 'A.png' 를 본 뒤 'a.png' 를 고르면 기억한 'A.png' 를 그대로 보였고(꺼내지도 않음), → 로 넘기면 'a.png' 에서 멈췄다(W2-04 2차 검증).
    /// </summary>
    [Fact]
    public async Task 대소문자만_다른_두_그림은_각각_보이고_넘기기가_멈추지_않는다()
    {
        using var tmp = new TempDir("pvcase");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = TestZip.Create(tmp.P("리눅스사진.zip"), TestZip.Bytes("사진/A.png", 1000, 1), TestZip.Bytes("사진/a.png", 1000, 2), TestZip.Bytes("사진/b.png", 1000, 3));
        var c = await OpenPhotos(tmp, zip);
        var p = c.Vm.Preview;

        await p.ShowAsync(Visible(c.Vm, "A.png")).WaitAsync(Limit);
        await p.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        // a.png 는 기억한 A.png 를 쓰지 않고 제 그림을 꺼내 읽음(디스크 이름은 엔진이 'a (1).png' 로 비켜 쓸 수 있어 내용으로 봄)
        Assert.Equal(2, c.Ts.Images.Decoded.Count);
        Assert.Equal(TestZip.Bytes("x", 1000, 1).Item2, c.Ts.Images.Decoded[0].Bytes);
        Assert.Equal(TestZip.Bytes("x", 1000, 2).Item2, c.Ts.Images.Decoded[1].Bytes);
        Assert.Equal("a.png", p.CurrentEntry!.Name);
        Assert.Equal(PreviewState.Image, p.State);

        var seen = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            await p.NextCommand.ExecuteAsync(null).WaitAsync(Limit);
            seen.Add(p.CurrentEntry!.Name);
        }
        var order = p.ImageEntries.Select(e => e.Name).ToList();
        int from = order.IndexOf("a.png");
        Assert.Equal(Enumerable.Range(1, 3).Select(k => order[(from + k) % order.Count]), seen);   // 목록 차례대로 돈다(a.png 에 멈추지 않음)
        await p.ShowAsync(Visible(c.Vm, "A.png")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, p.State);
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    /// <summary>
    /// W2-04 1차 검증: 큰 사진이 든 폴더에서 ←→ 를 누르고 있으면, 이미 시작한 그림 읽기는 멈추지 않아 여러 장(한 장 최대 약 160MB)이 동시에 돌아
    /// 메모리가 GB 단위로 튀었다(80ms 간격 12장 → 동시 7장). 읽기는 한 번에 하나, 기다리는 동안 건너뛴 것은 읽지 않는다.
    /// </summary>
    [Fact]
    public async Task 빠르게_넘겨도_그림은_한번에_하나만_읽고_건너뛴_것은_읽지_않는다()
    {
        using var tmp = new TempDir("pvburst");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        var p = c.Vm.Preview;
        using var gate = new ManualResetEventSlim(false);
        c.Ts.Images.Gate = gate;   // 한 장 읽기가 오래 걸리는 것처럼

        var shows = new List<Task> { p.ShowAsync(Visible(c.Vm, "a.png")) };
        Assert.True(c.Ts.Images.WaitStarted(1, Limit), "그림 읽기가 시작되지 않음");
        foreach (var n in new[] { "b.jpg", "c.gif", "d.webp", "e.BMP" })
        {
            shows.Add(p.ShowAsync(Visible(c.Vm, n)));   // → 를 누르고 있는 것처럼(꺼내기는 끝나 읽을 차례를 기다릴 만큼)
            await Task.Delay(100);
        }
        Assert.Equal(1, c.Ts.Images.Started);   // 앞 읽기가 끝나기 전에는 다음 것을 읽지 않음

        gate.Set();
        await Task.WhenAll(shows).WaitAsync(Limit);
        Assert.Equal(1, c.Ts.Images.MaxActive);
        Assert.Equal(new[] { "a.png", "e.BMP" }, c.Ts.Images.Decoded.Select(d => d.Name));   // 건너뛴 b·c·d 는 읽지 않음
        Assert.Equal("e.BMP", p.CurrentEntry!.Name);
        Assert.Equal(PreviewState.Image, p.State);
        Assert.True(NoTempLeft(c.PreviewRoot), "건너뛴 그림의 임시 파일이 남음");
    }

    /// <summary>[암호 넣기] 단추 뒤: 암호 창을 닫으면 단추를 다시, 암호와 상관없는 실패(손상 등 — 오류 창은 이미 보임)면 '미리 볼 수 없어요'(W2-04 1차 검증).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 암호_단추_뒤_실패는_취소일_때만_암호_단추를_다시_보인다(bool cancelled)
    {
        using var tmp = new TempDir("pvpwfail");
        using var _ = TestServices.UseAppRoot(tmp);
        var entry = new ArchiveEntry { Path = "사진/비밀.png", Size = 100, CompressedSize = 100, IsEncrypted = true };
        var host = new PromptHost(cancelled ? UseOutcome.Cancelled : UseOutcome.Failed, entry);
        var decoder = new FakeImageDecoder();
        var p = new ImagePreviewViewModel(host, decoder);

        await p.ShowAsync(entry).WaitAsync(Limit);
        Assert.Equal(PreviewState.NeedsPassword, p.State);
        await p.EnterPasswordCommand.ExecuteAsync(null).WaitAsync(Limit);

        Assert.Equal(1, host.Prompts);
        Assert.Empty(decoder.Decoded);
        if (cancelled)
        {
            Assert.Equal(PreviewState.NeedsPassword, p.State);
            Assert.True(p.IsPasswordNeeded);
            Assert.Equal(ImagePreviewViewModel.PasswordMessage, p.Message);
        }
        else
        {
            Assert.Equal(PreviewState.Error, p.State);
            Assert.False(p.IsPasswordNeeded);   // 오류 창 뒤에 암호 단추를 또 내밀지 않음
            Assert.Equal(ImagePreviewViewModel.ErrorMessage, p.Message);
        }
    }

    /// <summary>진짜 화면: 암호 단추를 눌렀는데 압축을 읽을 수 없으면(지워짐) 오류 창 한 번 뒤 '미리 볼 수 없어요' — 암호 단추가 다시 뜨지 않음.</summary>
    [Fact]
    public async Task 암호_단추를_눌렀는데_압축을_읽을_수_없으면_오류_뒤_미리볼수없어요()
    {
        using var tmp = new TempDir("pvpwgone");
        using var _ = TestServices.UseAppRoot(tmp);
        string src = tmp.Dir("원본");
        File.WriteAllBytes(Path.Combine(src, "비밀 사진.png"), TestZip.Bytes("x", 5000, 9).Item2);
        // 이름에 '암호'를 넣지 않는다 — 화면의 암호 오류 판정(IsPasswordRelated)이 예외 문구 속 '암호'(파일 경로)도 보기 때문
        string zip = tmp.P("잠긴사진.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, zip, new CompressionOptions { Password = FixtureCatalog.DefaultPassword });

        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword);
        var ts = new TestServices();
        var vm = new MainViewModel(ui, services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            vm.NavigateToFolder("원본");
            vm.Preview.IsVisible = true;
            await vm.Preview.ShowAsync(Visible(vm, "비밀 사진.png"));
        }, "암호 사진");
        Assert.Equal(PreviewState.NeedsPassword, vm.Preview.State);

        File.Delete(zip);   // 보는 사이 압축이 사라짐(손상과 같은 '암호와 상관없는 실패')
        await VmRunner.Run(() => vm.Preview.EnterPasswordCommand.ExecuteAsync(null), "암호 단추");
        Assert.Single(ui.Errors);
        Assert.Equal(PreviewState.Error, vm.Preview.State);
        Assert.False(vm.Preview.IsPasswordNeeded);
        Assert.Empty(ts.Images.Decoded);
    }

    /// <summary>[암호 넣기] 단추가 부르는 꺼내기 결과를 정해 주는 가짜 화면.</summary>
    private sealed class PromptHost(UseOutcome outcome, ArchiveEntry entry) : IPreviewHost
    {
        public int Prompts { get; private set; }

        int IPreviewHost.ArchiveGeneration => 1;

        IReadOnlyList<ArchiveEntry> IPreviewHost.FolderEntries => new[] { entry };

        bool IPreviewHost.NeedsPassword(ArchiveEntry e) => true;

        Task IPreviewHost.ExtractQuietAsync(int generation, ArchiveEntry e, string dest, CancellationToken ct) =>
            throw new InvalidOperationException("암호를 모르는데 묻지 않고 꺼내려 함");

        Task<UseOutcome> IPreviewHost.ExtractWithPromptAsync(int generation, ArchiveEntry e, string dest)
        {
            Prompts++;
            return Task.FromResult(outcome);
        }
    }

    /// <summary>
    /// 세대를 시험이 정하는 가짜 화면(IJ3-01): 꺼내기는 '지금 세대'의 내용(세대마다 다른 바이트)을 쓰고, 넘겨받은 세대가 지났으면 꺼내지 않는다(진짜 화면과 같은 약속).
    /// <see cref="HoldExtract"/> 가 있으면 꺼내기 시작을 알리고 그것이 열릴 때까지 기다린다(읽는 도중 압축이 바뀌는 틈 흉내).
    /// </summary>
    private sealed class GenerationHost(ArchiveEntry entry) : IPreviewHost
    {
        public int Generation { get; set; } = 1;
        public ManualResetEventSlim? HoldExtract { get; set; }
        public ManualResetEventSlim ExtractStarted { get; } = new(false);
        public List<int> ExtractedGenerations { get; } = new();

        int IPreviewHost.ArchiveGeneration => Generation;

        IReadOnlyList<ArchiveEntry> IPreviewHost.FolderEntries => new[] { entry };

        bool IPreviewHost.NeedsPassword(ArchiveEntry e) => false;

        async Task IPreviewHost.ExtractQuietAsync(int generation, ArchiveEntry e, string dest, CancellationToken ct)
        {
            if (generation != Generation) throw new OperationCanceledException();
            int contentOf = Generation;   // 꺼내기를 시작한 때의 압축 내용
            lock (ExtractedGenerations) ExtractedGenerations.Add(contentOf);
            ExtractStarted.Set();
            if (HoldExtract is { } hold) await Task.Run(() => hold.Wait(TimeSpan.FromSeconds(30)), ct);
            string file = Path.Combine(dest, e.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, TestZip.Bytes("x", 500, contentOf).Item2, ct);
        }

        Task<UseOutcome> IPreviewHost.ExtractWithPromptAsync(int generation, ArchiveEntry e, string dest) =>
            throw new InvalidOperationException("암호를 묻지 않음");
    }

    /// <summary>
    /// IJ3-01(a): 세대가 바뀌면(같은 경로 그림이라도 압축 내용이 바뀜 — 다른 압축·다시 열기·고친 뒤 다시 읽기) 기억한 그림을 쓰지 않고 새로 꺼내 읽는다.
    /// 화면이 미리 보기를 비우지(Reset) 않고 세대만 올려도 옛 그림이 나오지 않는다.
    /// </summary>
    [Fact]
    public async Task 세대가_바뀌면_같은_경로의_기억한_그림을_쓰지_않고_새로_읽는다()
    {
        using var tmp = new TempDir("pvgen");
        using var _ = TestServices.UseAppRoot(tmp);
        var entry = new ArchiveEntry { Path = "사진/001.jpg", Size = 500, CompressedSize = 500 };
        var host = new GenerationHost(entry);
        var decoder = new FakeImageDecoder();
        var p = new ImagePreviewViewModel(host, decoder);

        await p.ShowAsync(entry).WaitAsync(Limit);
        await p.ShowAsync(entry).WaitAsync(Limit);   // 같은 세대 — 기억한 것
        Assert.Equal(1, decoder.DecodeCount("001.jpg"));

        host.Generation = 2;   // 압축이 바뀜(Reset 없이)
        await p.ShowAsync(entry).WaitAsync(Limit);
        Assert.Equal(2, decoder.DecodeCount("001.jpg"));
        Assert.Equal(TestZip.Bytes("x", 500, 2).Item2, decoder.Decoded[^1].Bytes);   // 새 압축의 내용
        Assert.Equal(new[] { 1, 2 }, host.ExtractedGenerations);
        Assert.Equal(new[] { "사진/001.jpg" }, p.CachedKeys);   // 옛 세대 것은 버림
        Assert.Equal(PreviewState.Image, p.State);
    }

    /// <summary>
    /// IJ3-01(a): 꺼내는 도중 세대가 바뀌면(화면이 아직 비우지 않은 틈 포함) 끝난 옛 읽기는 기억하지도 보이지도 않고, '불러오는 중'에 멈추지 않게 비운다.
    /// 다시 고르면 새 세대의 내용을 꺼내 읽는다.
    /// </summary>
    [Fact]
    public async Task 꺼내는_도중_세대가_바뀌면_옛_그림을_기억하지도_보이지도_않는다()
    {
        using var tmp = new TempDir("pvgen2");
        using var _ = TestServices.UseAppRoot(tmp);
        var entry = new ArchiveEntry { Path = "사진/001.jpg", Size = 500, CompressedSize = 500 };
        using var hold = new ManualResetEventSlim(false);
        var host = new GenerationHost(entry) { HoldExtract = hold };
        var decoder = new FakeImageDecoder();
        var p = new ImagePreviewViewModel(host, decoder);

        var first = p.ShowAsync(entry);
        Assert.True(host.ExtractStarted.Wait(Limit), "꺼내기가 시작되지 않음");
        Assert.Equal(PreviewState.Loading, p.State);
        host.Generation = 2;   // 꺼내는 도중 압축이 바뀜
        hold.Set();
        await first.WaitAsync(Limit);

        Assert.Empty(p.CachedKeys);
        Assert.Equal(PreviewState.Empty, p.State);
        Assert.Null(p.Image);
        Assert.True(NoTempLeft(AppPaths.PreviewRoot));

        host.HoldExtract = null;
        await p.ShowAsync(entry).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, p.State);
        Assert.Equal(TestZip.Bytes("x", 500, 2).Item2, decoder.Decoded[^1].Bytes);
    }

    /// <summary>
    /// IJ3-01(a) 진짜 화면: 고친 뒤 목록을 다시 읽으면(같은 압축 경로) 세대가 바뀐다 — 지우고 이름을 바꿔 같은 경로에 다른 그림이 와도 옛 그림을 보이지 않는다.
    /// </summary>
    [Fact]
    public async Task 고친_뒤_같은_경로에_다른_그림이_오면_새_그림을_보인다()
    {
        using var tmp = new TempDir("pvedit");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = TestZip.Create(tmp.P("고칠사진.zip"), TestZip.Bytes("사진/a.png", 3000, 1), TestZip.Bytes("사진/b.png", 3000, 2));
        var c = await OpenPhotos(tmp, zip);
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        Assert.Equal(new[] { "사진/a.png" }, c.Vm.Preview.CachedKeys);
        int before = c.Vm.ArchiveGeneration;

        c.Ui.ChoiceAnswers.Enqueue(0);   // 지우기 확인 [지우기]
        await VmRunner.Run(async () =>
        {
            Assert.True(await c.Vm.DeleteEntriesAsync(new[] { Visible(c.Vm, "a.png") }));
            Assert.True(await c.Vm.RenameEntryAsync(Visible(c.Vm, "b.png"), "a.png"));
        }, "지우고 이름 바꾸기");
        Assert.True(c.Vm.ArchiveGeneration > before);
        Assert.Empty(c.Vm.Preview.CachedKeys);

        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "a.png")).WaitAsync(Limit);
        Assert.Equal(PreviewState.Image, c.Vm.Preview.State);
        Assert.Equal(TestZip.Bytes("x", 3000, 2).Item2, c.Ts.Images.Decoded[^1].Bytes);   // 옛 a.png 가 아니라 이름을 바꾼 b.png 의 내용
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    // ───────────────────────────── 진짜 WIC ─────────────────────────────

    /// <summary>진짜 WIC 로 작은 PNG 를 읽는다(STA 스레드, 창 없음). 다 읽은 뒤 파일을 놓아 바로 지울 수 있다.</summary>
    [Fact]
    public void 진짜_WIC로_작은_PNG를_읽고_파일을_놓는다()
    {
        using var tmp = new TempDir("wic");
        string png = tmp.P("작은 그림.png");
        Sta.Run(() =>
        {
            var pixels = new byte[3 * 2 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 0x20; pixels[i + 1] = 0x80; pixels[i + 2] = 0xF0; pixels[i + 3] = 0xFF; }
            var src = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 3 * 4);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var fs = File.Create(png);
            enc.Save(fs);
        });

        DecodedImage? img = null;
        Sta.Run(() => img = new WicImageDecoder().Decode(png));
        Assert.NotNull(img!.Source);
        Assert.Equal(3, img.PixelWidth);
        Assert.Equal(2, img.PixelHeight);
        Assert.Equal(3 * 2 * 4, img.ByteSize);
        Assert.True(img.Source!.IsFrozen, "다른 스레드에서 쓰려면 얼려야 함");
        File.Delete(png);   // OnLoad — 파일을 붙잡고 있지 않음
        Assert.False(File.Exists(png));
    }

    /// <summary>
    /// IJ3-01: 파일은 작아도 화소가 아주 많은 그림(그림 폭탄 — 1비트 PNG 16384×16384 는 수십 KB)은 줄여 읽지도 않고 거절한다 —
    /// PNG 는 줄여 읽어도 WIC 가 원래 크기를 한 번 다 풀 수 있어(이 그림이면 약 1GB) 메모리가 튄다.
    /// </summary>
    [Fact]
    public void 화소가_너무_많은_그림은_WIC가_풀기_전에_거절한다()
    {
        using var tmp = new TempDir("wicbomb");
        string png = tmp.P("폭탄.png");
        const int side = 16384;   // 268MP > 250MP
        Sta.Run(() =>
        {
            int stride = side / 8;
            var src = BitmapSource.Create(side, side, 96, 96, PixelFormats.BlackWhite, null, new byte[stride * side], stride);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var fs = File.Create(png);
            enc.Save(fs);
        });
        Assert.True(new FileInfo(png).Length < 5 * 1024 * 1024, "작은 파일이어야 함");

        Exception? err = null;
        Sta.Run(() =>
        {
            try { new WicImageDecoder().Decode(png); }
            catch (Exception ex) { err = ex; }
        });
        var tooLarge = Assert.IsType<ImageTooLargeException>(err);
        Assert.Equal((side, side), (tooLarge.Width, tooLarge.Height));
        File.Delete(png);   // 거절한 뒤 파일을 붙잡고 있지 않음

        Assert.True(WicImageDecoder.IsTooManyPixels(16384, 16384, jpeg: false));
        Assert.False(WicImageDecoder.IsTooManyPixels(16384, 16384, jpeg: true));   // JPEG 는 풀면서 줄여 읽어 더 큰 것까지
        Assert.False(WicImageDecoder.IsTooManyPixels(12000, 12000, jpeg: false));   // 144MP — 줄여 읽음
        Assert.True(WicImageDecoder.IsTooManyPixels(40000, 30000, jpeg: true));
    }

    [Fact]
    public async Task 화소가_너무_많은_그림은_자리표시를_보인다()
    {
        using var tmp = new TempDir("pvbomb");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenPhotos(tmp);
        c.Ts.Images.TooLargeName = "c.gif";
        await c.Vm.Preview.ShowAsync(Visible(c.Vm, "c.gif")).WaitAsync(Limit);
        Assert.Equal(PreviewState.TooLarge, c.Vm.Preview.State);
        Assert.Equal(ImagePreviewViewModel.TooLargeMessage, c.Vm.Preview.Message);
        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Vm.Preview.CachedKeys);
        Assert.True(NoTempLeft(c.PreviewRoot));
    }

    [Fact]
    public void 그림이_아니면_WIC가_예외를_낸다()
    {
        using var tmp = new TempDir("wicbad");
        string bad = tmp.P("깨진.png");
        File.WriteAllText(bad, "그림 아님");
        Exception? err = null;
        Sta.Run(() =>
        {
            try { new WicImageDecoder().Decode(bad); }
            catch (Exception ex) { err = ex; }
        });
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData(1000, 1000)]
    [InlineData(6000, 6000)]   // 36MP — 그대로
    public void 사천만화소_이하는_그대로_읽는다(int w, int h) => Assert.Null(WicImageDecoder.TargetDecodeWidth(w, h));

    [Theory]
    [InlineData(8000, 6000)]     // 48MP
    [InlineData(20000, 10000)]   // 200MP
    [InlineData(2000, 30000)]    // 60MP 세로 파노라마 — 가로를 키우지 않음
    public void 큰_그림은_사천만화소_가로_4096_이하로_줄여_읽는다(int w, int h)
    {
        int tw = WicImageDecoder.TargetDecodeWidth(w, h)!.Value;
        Assert.True(tw <= WicImageDecoder.DownscaleWidth && tw < w);
        double th = (double)h * tw / w;
        Assert.True(tw * th <= WicImageDecoder.MaxPixels, $"{tw}×{th:F0} > 40MP");
    }
}
