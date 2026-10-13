using System.Text.Json;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-05] 풀기 설정(settings.json — extract.onCollision·extract.openFolderAfter·extract.deleteArchiveAfter)과 같은 이름 처리기,
/// 나뉜 압축의 조각 찾기(<see cref="ArchiveVolumes"/>). 설정 파일은 시험 폴더에만 쓴다(진짜 %LOCALAPPDATA%\Iljip 은 건드리지 않음 — ModuleInit).
/// </summary>
public class ExtractSettingsTests
{
    // ───────────── 설정 저장(settings.json) ─────────────

    [Fact]
    public void 기본값은_묻기_폴더_열기_켬_지우기_꺼짐()
    {
        var s = new AppSettings();
        Assert.Equal(1, s.SchemaVersion);
        Assert.Equal(CollisionPolicy.Ask, s.Extract.OnCollision);
        Assert.True(s.Extract.OpenFolderAfter);
        Assert.False(s.Extract.DeleteArchiveAfter);
    }

    /// <summary>시험 중엔 일집 자료 폴더가 시험 전용 — 가짜 없이 만든 화면도 이 PC 사용자의 settings.json 을 읽지 않는다.</summary>
    [Fact]
    public void 시험_중엔_사용자_설정_파일을_읽지_않는다()
    {
        Assert.StartsWith(ModuleInit.TestAppDataRoot, AppPaths.SettingsFile);
        Assert.StartsWith(ModuleInit.TestAppDataRoot, new JsonSettingsStore().FilePath);
        var vm = new MainViewModel(new FakeUserInteraction());
        Assert.False(vm.Settings.Extract.DeleteArchiveAfter);
        Assert.Equal(CollisionPolicy.Ask, vm.Settings.Extract.OnCollision);
    }

    /// <summary>쓰고 읽으면 같다. JSON 은 camelCase 묶음(extract.onCollision)·같은 이름 동작은 글자로('Overwrite')·한글 그대로.</summary>
    [Fact]
    public void 저장하고_읽으면_같다()
    {
        using var tmp = new TempDir("set");
        var store = new JsonSettingsStore(tmp.P("settings.json"));
        var s = new AppSettings();
        s.Extract.OnCollision = CollisionPolicy.Overwrite;
        s.Extract.OpenFolderAfter = false;
        s.Extract.DeleteArchiveAfter = true;
        store.Save(s);

        string json = File.ReadAllText(store.FilePath);
        using (var doc = JsonDocument.Parse(json))
        {
            var ex = doc.RootElement.GetProperty("extract");
            Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("Overwrite", ex.GetProperty("onCollision").GetString());
            Assert.False(ex.GetProperty("openFolderAfter").GetBoolean());
            Assert.True(ex.GetProperty("deleteArchiveAfter").GetBoolean());
        }
        Assert.False(File.Exists(store.FilePath + ".tmp"), "임시 파일이 남음");

        var back = store.Load();
        Assert.Equal(CollisionPolicy.Overwrite, back.Extract.OnCollision);
        Assert.False(back.Extract.OpenFolderAfter);
        Assert.True(back.Extract.DeleteArchiveAfter);

        // 덮어 저장(File.Replace)도 됨
        back.Extract.OnCollision = CollisionPolicy.Skip;
        store.Save(back);
        Assert.Equal(CollisionPolicy.Skip, store.Load().Extract.OnCollision);
    }

    /// <summary>뒤 판이 더한 모르는 필드는 읽었다 다시 써도 남는다(옛 판이 열었다 저장해도 지워지지 않게).</summary>
    [Fact]
    public void 모르는_필드는_다시_써도_남는다()
    {
        using var tmp = new TempDir("setx");
        string path = tmp.P("settings.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "extract": { "onCollision": "Skip", "keepBrokenFiles": true },
              "view": { "showHidden": true },
              "recentFolders": ["C:\\자료\\견적"]
            }
            """);
        var store = new JsonSettingsStore(path);
        var s = store.Load();
        Assert.Equal(CollisionPolicy.Skip, s.Extract.OnCollision);
        Assert.True(s.Extract.OpenFolderAfter);   // 없는 값은 기본값
        store.Save(s);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.GetProperty("view").GetProperty("showHidden").GetBoolean());
        Assert.Equal(@"C:\자료\견적", doc.RootElement.GetProperty("recentFolders")[0].GetString());
        Assert.True(doc.RootElement.GetProperty("extract").GetProperty("keepBrokenFiles").GetBoolean());
        Assert.Contains("자료", File.ReadAllText(path));   // 한글을 \uXXXX 로 바꾸지 않음
    }

    /// <summary>깨진 설정 파일은 settings.bad-….json 으로 옮겨 두고 기본값으로 시작한다(일집이 안 열리는 일 없게).</summary>
    [Theory]
    [InlineData("{ 이건 JSON 이 아님")]
    [InlineData("null")]
    [InlineData("{ \"extract\": { \"onCollision\": \"모르는값\" } }")]
    public void 깨진_설정_파일은_옆으로_옮기고_기본값(string text)
    {
        using var tmp = new TempDir("setbad");
        string path = tmp.P("settings.json");
        File.WriteAllText(path, text);
        var s = new JsonSettingsStore(path).Load();
        Assert.Equal(CollisionPolicy.Ask, s.Extract.OnCollision);
        Assert.False(File.Exists(path));
        string bad = Assert.Single(Directory.GetFiles(tmp.Root, "settings.bad-*.json"));
        Assert.Equal(text, File.ReadAllText(bad));
    }

    [Fact]
    public void 설정_파일이_없으면_기본값이고_만들지_않는다()
    {
        using var tmp = new TempDir("setnone");
        string path = tmp.P("없음", "settings.json");
        var s = new JsonSettingsStore(path).Load();
        Assert.Equal(CollisionPolicy.Ask, s.Extract.OnCollision);
        Assert.False(File.Exists(path));
    }

    /// <summary>설정 창은 사본을 고친다(취소하면 버림) — 사본을 고쳐도 원래 설정은 그대로. 적용하면 한 번 저장.</summary>
    [Fact]
    public void 설정_창은_사본을_고치고_적용할_때_한번_저장한다()
    {
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        var copy = vm.Settings.Clone();
        copy.Extract.DeleteArchiveAfter = true;
        Assert.False(vm.Settings.Extract.DeleteArchiveAfter);
        Assert.Equal(0, svc.Settings.SaveCount);

        vm.ApplySettings(copy);
        Assert.True(vm.Settings.Extract.DeleteArchiveAfter);
        Assert.Equal(1, svc.Settings.SaveCount);
        Assert.Contains("\"deleteArchiveAfter\": true", svc.Settings.Json);
        Assert.Equal(1, svc.Settings.LoadCount);   // 처음 한 번만 읽음
    }

    // ───────────── 같은 이름 처리기 ─────────────

    private static CollisionQuestion Q(string name = "a.txt") => new(name, 1, DateTime.Now, 2, null, 0);

    [Theory]
    [InlineData(CollisionPolicy.Overwrite, CollisionChoice.Overwrite)]
    [InlineData(CollisionPolicy.Skip, CollisionChoice.Skip)]
    [InlineData(CollisionPolicy.Rename, CollisionChoice.Rename)]
    public void 설정이_묻기가_아니면_묻지_않는다(CollisionPolicy policy, CollisionChoice expected)
    {
        var ui = new FakeUserInteraction();
        var r = AskingResolver.For(policy, ui);
        Assert.Equal(expected, r.Resolve(Q()));
        Assert.Equal(expected, new AskingResolver(ui, policy).Resolve(Q()));
        Assert.Empty(ui.CollisionPrompts);
    }

    [Fact]
    public void 남은_파일도_똑같이는_기억하고_아니면_매번_묻는다()
    {
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Skip, false));
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Overwrite, true));
        var r = AskingResolver.For(CollisionPolicy.Ask, ui);
        Assert.Equal(CollisionChoice.Skip, r.Resolve(Q("1")));
        Assert.Equal(CollisionChoice.Overwrite, r.Resolve(Q("2")));
        Assert.Equal(CollisionChoice.Overwrite, r.Resolve(Q("3")));
        Assert.Equal(CollisionChoice.Overwrite, r.Resolve(Q("4")));
        Assert.Equal(new[] { "1", "2" }, ui.CollisionPrompts.Select(p => p.TargetPath));
    }

    /// <summary>[취소]는 ‘남은 파일도 똑같이’를 골라도 기억하지 않는다(다음 풀기에서 또 묻게).</summary>
    [Fact]
    public void 취소는_기억하지_않는다()
    {
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Cancel, true));
        var r = new AskingResolver(ui);
        Assert.Equal(CollisionChoice.Cancel, r.Resolve(Q()));
        Assert.Null(r.Remembered);
        Assert.Equal(CollisionChoice.Rename, r.Resolve(Q()));   // 가짜 기본 답
        Assert.Equal(2, ui.CollisionPrompts.Count);
    }

    /// <summary>
    /// 메인 창 [풀기]: 같은 이름이면 묻고(질문에 원래 파일 크기·새 파일 크기·남은 수), 덮어쓰기를 고르면 새 내용으로 바뀐다.
    /// </summary>
    [Fact]
    public async Task 메인_창_풀기도_같은_이름이면_묻고_고른_대로()
    {
        var fx = FixtureCatalog.Get("zip_default");
        var entry = fx.Expected.Single(e => e.Path == "일집_샘플/견적서_2026.txt");
        using var tmp = new TempDir("setvm");
        string dest = tmp.Dir("풀 곳");
        string mine = Path.Combine(dest, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Overwrite, false));
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        bool ok = false;
        await VmRunner.Run(async () => ok = await vm.ExtractToFolderAsync(fx.FullPath, dest), "풀기");

        Assert.True(ok);
        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal(mine, q.TargetPath);
        Assert.Equal(new FileInfo(mine).Length, entry.Size);
        Assert.Equal(System.Text.Encoding.UTF8.GetByteCount("사용자"), q.ExistingSize);
        Assert.Equal(entry.Size, q.IncomingSize);
        Assert.InRange(q.Remaining, 0, fx.Expected.Count(e => !e.IsDir) - 1);
        Assert.Equal(entry.Sha256, TreeAssert.Sha256(mine));
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    /// <summary>같은 이름 [취소]는 '취소됨'으로 끝난다(오류 창 없음) — 이번에 만든 것만 되돌리고 원래 파일은 그대로.</summary>
    [Fact]
    public async Task 메인_창_풀기에서_같은_이름_취소는_조용히_끝난다()
    {
        var fx = FixtureCatalog.Get("7z_default");
        using var tmp = new TempDir("setvmc");
        string dest = tmp.Dir("풀 곳");
        string mine = Path.Combine(dest, fx.Expected.First(e => !e.IsDir).Path.Replace('/', '\\'));
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자");
        var before = TreeAssert.Snapshot(dest);
        var ui = new FakeUserInteraction { DefaultCollisionAnswer = new(CollisionChoice.Cancel, false) };
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        bool ok = true;
        await VmRunner.Run(async () => ok = await vm.ExtractToFolderAsync(fx.FullPath, dest), "풀기");

        Assert.False(ok);
        Assert.Empty(ui.Errors);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        Assert.Equal(before, TreeAssert.Snapshot(dest));
    }

    // ───────────── 나뉜 압축의 조각(풀고 나서 지우기 대상) ─────────────

    [Fact]
    public void 숫자_조각은_001부터_빈_번호_전까지()
    {
        using var tmp = new TempDir("vol");
        string d = tmp.Dir("v");
        foreach (var n in new[] { "자료.7z.001", "자료.7z.002", "자료.7z.003", "자료.7z.005", "다른.7z.001" })
            File.WriteAllBytes(Path.Combine(d, n), new byte[] { 1 });
        var parts = ArchiveVolumes.AllParts(Path.Combine(d, "자료.7z.001"));
        Assert.Equal(new[] { "자료.7z.001", "자료.7z.002", "자료.7z.003" }, parts.Select(Path.GetFileName));
        // 가운데 조각을 골라도 세트 전부
        Assert.Equal(3, ArchiveVolumes.AllParts(Path.Combine(d, "자료.7z.002")).Count);
    }

    [Fact]
    public void 나뉘지_않은_압축은_그_파일_하나()
    {
        using var tmp = new TempDir("vol1");
        string d = tmp.Dir("v");
        string zip = Path.Combine(d, "자료.zip");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, zip);
        File.WriteAllBytes(Path.Combine(d, "자료.z01"), new byte[] { 1 });   // 이름만 비슷한 남의 파일 — 지우면 안 됨
        Assert.Equal(new[] { zip }, ArchiveVolumes.AllParts(zip));
        string egg = Path.Combine(d, "자료.egg");
        File.WriteAllBytes(egg, new byte[] { 1 });
        Assert.Equal(new[] { egg }, ArchiveVolumes.AllParts(egg));
    }

    [FixtureFact("bandizip/zip_split.zip")]
    public void PKZIP_나눔은_z01_z02와_zip()
    {
        var fx = FixtureCatalog.Get("zip_split");
        using var tmp = new TempDir("volz");
        string d = tmp.Dir("v");
        foreach (var v in fx.SplitVolumes) File.Copy(FixtureCatalog.PathOf(v), Path.Combine(d, Path.GetFileName(v)));
        Assert.Equal(new[] { "zip_split.z01", "zip_split.z02", "zip_split.zip" },
            ArchiveVolumes.AllParts(Path.Combine(d, "zip_split.zip")).Select(Path.GetFileName));
    }

    [Fact]
    public void RAR_partN과_EGG_volN도_세트_전부()
    {
        using var tmp = new TempDir("volr");
        string d = tmp.Dir("v");
        byte[] rar5 = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00, 0, 0, 0, 0 };
        foreach (var n in new[] { "자료.part1.rar", "자료.part2.rar", "자료.part3.rar" })
            File.WriteAllBytes(Path.Combine(d, n), rar5);
        Assert.Equal(new[] { "자료.part1.rar", "자료.part2.rar", "자료.part3.rar" },
            ArchiveVolumes.AllParts(Path.Combine(d, "자료.part1.rar")).Select(Path.GetFileName));

        var src = tmp.Dir("원본", "분할 자료");
        File.WriteAllBytes(Path.Combine(src, "난수.bin"), Enumerable.Range(0, 90_000).Select(i => (byte)i).ToArray());
        var eggs = RawEgg.WriteSplit(Path.Combine(d, "분할"), RawEgg.FromFolder(src), parts: 3);
        Assert.Equal(eggs.Select(Path.GetFileName), ArchiveVolumes.AllParts(eggs[0]).Select(Path.GetFileName));
    }

    // ───────────── 요약 문구 ─────────────

    [Fact]
    public void 요약_첫_줄과_자세히는_나뉜다()
    {
        var ok = new OperationSummary { Total = 3, Succeeded = 3 };
        Assert.Equal("3개를 모두 풀었어요.", ok.Headline);
        Assert.Equal(string.Empty, ok.DetailText());

        var mixed = new OperationSummary
        {
            Total = 5,
            Succeeded = 4,
            Failures = new[] { new OperationFailure("b.zip", "파일이 손상됐어요.", "SharpCompress.Common.ArchiveException: Failed to locate the Zip Header") },
        };
        Assert.Equal("5개 중 4개를 풀었어요.", mixed.Headline);
        Assert.Contains("- b.zip — 파일이 손상됐어요.", mixed.ToDisplayText());
        Assert.DoesNotContain("SharpCompress", mixed.ToDisplayText());
        Assert.Contains("Failed to locate", mixed.DetailText());

        var cancelled = new OperationSummary { Total = 5, Succeeded = 1, Cancelled = true, NotStarted = new[] { "c.zip", "d.zip", "e.zip" } };
        Assert.Equal("5개 중 1개를 풀고 멈췄어요.", cancelled.Headline);
        Assert.Contains("취소해서 손대지 않은 파일 3개:", cancelled.ToDisplayText());
    }

    /// <summary>모르는 엔진 오류(영어)는 우클릭·여러 개 풀기 화면에서 쉬운 한국어로(날 문구는 '자세히'·로그). 아는 오류는 예전 문구 그대로.</summary>
    [Fact]
    public void 모르는_엔진_오류는_쉬운_한국어로()
    {
        var unknown = new InvalidOperationException("Failed to locate the Zip Header");
        Assert.Equal(ErrorMessages.UnknownPlainMessage, ErrorMessages.ToPlainMessage(unknown));
        Assert.Contains("Failed to locate", ErrorMessages.ToUserMessage(unknown));   // 예전 화면(메인 창 오류 창·창 없이 풀기)은 그대로
        var known = new WrongPasswordException();
        Assert.Equal(ErrorMessages.ToUserMessage(known), ErrorMessages.ToPlainMessage(known));
        Assert.Equal("디스크 공간이 모자라요.", ErrorMessages.ToPlainMessage(new IOException("disk", unchecked((int)0x80070070))));
        foreach (var m in new[] { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" })
            Assert.DoesNotContain(m, ErrorMessages.UnknownPlainMessage, StringComparison.OrdinalIgnoreCase);
    }
}
