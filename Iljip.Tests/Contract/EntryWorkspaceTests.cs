using System.Reflection;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-04] 압축 속 파일 열기·끌어내기·복사의 바탕 서비스(창 없이): 작업 폴더(<see cref="ArchiveWorkspace"/>)·임시 폴더 청소(<see cref="TempJanitor"/>)·
/// 인터넷 표식 옮기기(<see cref="ZoneIdentifier"/>)·실행 파일 판정(<see cref="RiskyFileTypes"/>)·설정 저장(<see cref="JsonSettingsStore"/>).
/// </summary>
public class EntryWorkspaceTests
{
    private static string CopyFixture(TempDir tmp, string id, string name)
    {
        string p = tmp.P(name);
        File.Copy(FixtureCatalog.Get(id).FullPath, p);
        return p;
    }

    // ───────────────────────────── ArchiveWorkspace ─────────────────────────────

    [Fact]
    public void 같은_파일은_key가_같고_수정시각이_바뀌면_달라진다()
    {
        using var tmp = new TempDir("wskey");
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        string k1 = ArchiveWorkspace.ComputeKey(a);
        Assert.Matches("^[0-9a-f]{16}$", k1);
        Assert.Equal(k1, ArchiveWorkspace.ComputeKey(a));
        Assert.Equal(k1, ArchiveWorkspace.ComputeKey(a.ToUpperInvariant()));   // Windows 경로는 대소문자 무시

        File.SetLastWriteTimeUtc(a, File.GetLastWriteTimeUtc(a).AddMinutes(1));
        Assert.NotEqual(k1, ArchiveWorkspace.ComputeKey(a));

        string b = CopyFixture(tmp, "zip_default", "다른 이름.zip");
        File.SetLastWriteTimeUtc(b, File.GetLastWriteTimeUtc(a));
        Assert.NotEqual(ArchiveWorkspace.ComputeKey(a), ArchiveWorkspace.ComputeKey(b));   // 경로가 다르면 다름
    }

    [Fact]
    public void 작업폴더는_temp_open_key_아래이고_잠금을_잡고_순번_폴더를_만든다()
    {
        using var tmp = new TempDir("wsopen");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        var ws = ArchiveWorkspace.Open(a);
        try
        {
            Assert.Equal(Path.Combine(AppPaths.OpenRoot, ArchiveWorkspace.ComputeKey(a)), ws.FullPath);
            Assert.True(File.Exists(Path.Combine(ws.FullPath, ArchiveWorkspace.LockFileName)));
            Assert.True(TempJanitor.IsAlive(ws.FullPath), "owner.lock 을 잡고 있지 않음");
            Assert.Equal(Path.Combine(ws.FullPath, "1"), ws.NewSlot());
            Assert.Equal(Path.Combine(ws.FullPath, "2"), ws.NewSlot());
            Assert.True(Directory.Exists(Path.Combine(ws.FullPath, "2")));
        }
        finally
        {
            ws.Dispose();
        }
        Assert.False(Directory.Exists(ws.FullPath), "닫았는데 작업 폴더가 남음");
        ws.Dispose();   // 두 번 불러도 됨
    }

    [Fact]
    public void Dispose는_다른_프로그램이_잡은_파일만_남기고_예외없이_지운다()
    {
        using var tmp = new TempDir("wslock");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        var ws = ArchiveWorkspace.Open(a);
        string slot = ws.NewSlot();
        string free = Path.Combine(slot, "자유.txt");
        string held = Path.Combine(slot, "워드가 연 문서.docx");
        File.WriteAllText(free, "a");
        File.WriteAllText(held, "b");
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ws.Dispose();   // 예외 없음
            Assert.False(File.Exists(free));
            Assert.True(File.Exists(held), "잡힌 파일은 남아야 함");
            Assert.False(File.Exists(Path.Combine(ws.FullPath, ArchiveWorkspace.LockFileName)), "잠금 파일은 지워야 다음 시작 청소가 지움");
        }
        // 다음 시작 때 청소가 남은 것을 지운다
        TempJanitor.CleanupStale(new FakeClock());
        Assert.False(Directory.Exists(ws.FullPath));
    }

    [Fact]
    public void 같은_압축을_다른_일집이_쓰는_중이면_옆_이름의_폴더를_쓴다()
    {
        using var tmp = new TempDir("wstwo");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        using var first = ArchiveWorkspace.Open(a);
        using var second = ArchiveWorkspace.Open(a);
        Assert.NotEqual(first.FullPath, second.FullPath);
        Assert.EndsWith(ArchiveWorkspace.ComputeKey(a) + "-2", second.FullPath);
        Assert.True(TempJanitor.IsAlive(first.FullPath));
        Assert.True(TempJanitor.IsAlive(second.FullPath));
    }

    [Fact]
    public void 주인없는_찌꺼기는_잡을_때_지운다()
    {
        using var tmp = new TempDir("wsold");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        string old = Path.Combine(AppPaths.OpenRoot, ArchiveWorkspace.ComputeKey(a));
        Directory.CreateDirectory(Path.Combine(old, "7"));
        File.WriteAllText(Path.Combine(old, "7", "지난번.txt"), "x");
        File.WriteAllText(Path.Combine(old, ArchiveWorkspace.LockFileName), "");
        using var ws = ArchiveWorkspace.Open(a);
        Assert.Equal(old, ws.FullPath);
        Assert.False(Directory.Exists(Path.Combine(old, "7")));
        Assert.Equal(Path.Combine(old, "1"), ws.NewSlot());
    }

    [Fact]
    public void 연_파일을_고쳐_저장하면_고친_것으로_본다()
    {
        using var tmp = new TempDir("wsmod");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = CopyFixture(tmp, "zip_default", "자료.zip");
        using var ws = ArchiveWorkspace.Open(a);
        string f = Path.Combine(ws.NewSlot(), "보고서.txt");
        File.WriteAllText(f, "처음");
        var rec = ws.Record("일집_샘플/보고서.txt", f);
        Assert.False(rec.IsModified());

        // 시각만 바뀜(내용 같음) — 고친 것 아님
        File.SetLastWriteTimeUtc(f, File.GetLastWriteTimeUtc(f).AddMinutes(1));
        Assert.False(rec.IsModified());

        File.WriteAllText(f, "고쳐 저장함");
        Assert.True(rec.IsModified());
        Assert.Single(ws.ModifiedEntries());

        File.Delete(f);   // 지워졌으면 잃을 것이 없음
        Assert.False(rec.IsModified());
    }

    // ───────────────────────────── TempJanitor ─────────────────────────────

    /// <summary>
    /// open·drag 는 잠금(owner.lock)으로 살아 있는지 본다. 잠금이 없는 preview(1시간)·list·convert·args(24시간)는 나이로만 —
    /// 예전(W2-04 보관본)엔 잠금이 없으면 '주인 꺼짐'으로 보아 다른 일집이 지금 쓰는 그림 읽기·tar 목록 캐시까지 지울 수 있었다(IJ3-01).
    /// </summary>
    [Fact]
    public void 청소는_주인이_꺼진_open_drag와_오래된_preview_list_convert_args를_지우고_살아있는_것은_남긴다()
    {
        using var tmp = new TempDir("jan");
        using var _ = TestServices.UseAppRoot(tmp);
        DateTime old2d = DateTime.UtcNow.AddDays(-2), old2h = DateTime.UtcNow.AddHours(-2);
        string Make(string kind, string name, bool withLock, DateTime? createdUtc = null)
        {
            string d = Path.Combine(AppPaths.TempRoot, kind, name);
            Directory.CreateDirectory(Path.Combine(d, "1"));
            File.WriteAllText(Path.Combine(d, "1", "파일.txt"), "x");
            if (withLock) File.WriteAllText(Path.Combine(d, ArchiveWorkspace.LockFileName), "");
            if (createdUtc is { } c) Directory.SetCreationTimeUtc(d, c);
            return d;
        }
        string deadOpen = Make("open", "aaaa", withLock: true);
        string aliveOpen = Make("open", "bbbb", withLock: true);
        string deadDrag = Make("drag", "cccc", withLock: true);
        string oldPreview = Make("preview", "dddd", withLock: false, old2h);
        string freshPreview = Make("preview", "dddd2", withLock: false);   // 다른 일집이 지금 그림을 읽는 중일 수 있음
        string oldList = Make("list", "eeee", withLock: false, old2d);
        string freshList = Make("list", "eeee2", withLock: false, old2h);   // 다른 일집의 tar 목록 캐시
        string oldConvert = Make("convert", "gggg", withLock: false, old2d);
        string aliveDrag = Make("drag", "ffff", withLock: true);
        string args = Path.Combine(AppPaths.TempRoot, "args");
        Directory.CreateDirectory(args);
        string oldArgs = Path.Combine(args, "옛.lst"), freshArgs = Path.Combine(args, "새.lst");
        File.WriteAllText(oldArgs, "x");
        File.SetCreationTimeUtc(oldArgs, old2d);
        File.WriteAllText(freshArgs, "x");

        using (new FileStream(Path.Combine(aliveOpen, ArchiveWorkspace.LockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (new FileStream(Path.Combine(aliveDrag, ArchiveWorkspace.LockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            int n = TempJanitor.CleanupStale(new FakeClock());
            Assert.Equal(5, n);
            Assert.False(Directory.Exists(deadOpen));
            Assert.False(Directory.Exists(deadDrag));
            Assert.False(Directory.Exists(oldPreview));
            Assert.False(Directory.Exists(oldList));
            Assert.False(Directory.Exists(oldConvert));
            Assert.False(File.Exists(oldArgs));
            Assert.True(File.Exists(Path.Combine(aliveOpen, "1", "파일.txt")), "살아 있는(잠금이 잡힌) 작업 폴더를 지움");
            Assert.True(File.Exists(Path.Combine(aliveDrag, "1", "파일.txt")), "살아 있는 끌기 폴더를 지움");
            Assert.True(File.Exists(Path.Combine(freshPreview, "1", "파일.txt")), "막 만든 미리 보기 폴더를 지움(다른 일집이 읽는 중일 수 있음)");
            Assert.True(File.Exists(Path.Combine(freshList, "1", "파일.txt")), "하루 안 된 tar 목록 캐시를 지움(다른 일집이 쓰는 중일 수 있음)");
            Assert.True(File.Exists(freshArgs), "하루 안 된 목록 파일을 지움");
        }
    }

    [Fact]
    public void 클립보드_사본은_24시간이_지나야_지운다()
    {
        using var tmp = new TempDir("janclip");
        using var _ = TestServices.UseAppRoot(tmp);
        string d = Path.Combine(AppPaths.ClipRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "복사한 것.txt"), "x");
        var clock = new FakeClock { UtcNow = Directory.GetCreationTimeUtc(d).AddHours(23) };

        TempJanitor.CleanupStale(clock);
        Assert.True(Directory.Exists(d), "24시간 안 된 클립보드 사본을 지움(붙여넣기가 늦을 수 있음)");

        clock.UtcNow = Directory.GetCreationTimeUtc(d).AddHours(25);
        TempJanitor.CleanupStale(clock);
        Assert.False(Directory.Exists(d));
    }

    [Fact]
    public void 청소를_시작한_뒤에_생긴_폴더는_건드리지_않는다()
    {
        using var tmp = new TempDir("jannew");
        using var _ = TestServices.UseAppRoot(tmp);
        string d = Path.Combine(AppPaths.OpenRoot, "막만듦");
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "x.txt"), "x");
        // 이 일집이 청소 도중에 막 만든 폴더(아직 잠그기 전)를 흉내 — 만든 시각이 청소 시작보다 뒤
        Directory.SetCreationTimeUtc(d, DateTime.UtcNow.AddMinutes(5));
        TempJanitor.CleanupStale(new FakeClock());
        Assert.True(Directory.Exists(d));
    }

    [Fact]
    public void 임시폴더가_없어도_청소는_조용히_끝난다()
    {
        using var tmp = new TempDir("jan0");
        using var _ = TestServices.UseAppRoot(tmp);
        Assert.Equal(0, TempJanitor.CleanupStale(new FakeClock()));
    }

    /// <summary>계약(소스): 청소는 일반 시작에서만 — 창 없이 풀기(재사용장치) 분기·단일 인스턴스 확인 뒤에 부른다.</summary>
    [Fact]
    public void 청소는_창없이_풀기와_두번째_실행에서는_하지_않는다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int start = src.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        int end = src.IndexOf("private static bool IsHeadlessExtractCommand", StringComparison.Ordinal);
        string body = src[start..end];
        int headless = body.IndexOf("IsHeadlessExtractCommand(e.Args)", StringComparison.Ordinal);
        int forward = body.IndexOf("ForwardArgsToRunningInstance(", StringComparison.Ordinal);
        int janitor = body.IndexOf("TempJanitor.CleanupStale(", StringComparison.Ordinal);
        Assert.True(janitor > 0, "시작 때 임시 폴더 청소가 없음");
        Assert.True(janitor > headless && janitor > forward, "청소가 창 없이 풀기·두 번째 실행 분기보다 앞에 있음");
    }

    // ───────────────────────────── ZoneIdentifier ─────────────────────────────

    private const string Zone3 = "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://mail.example.com/\r\n";

    [Fact]
    public void 압축의_인터넷_표식을_꺼낸_파일에_그대로_붙인다()
    {
        using var tmp = new TempDir("zone");
        string arc = CopyFixture(tmp, "zip_default", "메일첨부.zip");
        File.WriteAllText(arc + ZoneIdentifier.StreamName, Zone3);
        string f = tmp.P("꺼낸 문서.docx");
        File.WriteAllText(f, "본문");

        Assert.Equal(1, ZoneIdentifier.Copy(arc, f));
        Assert.Equal(Zone3, File.ReadAllText(f + ZoneIdentifier.StreamName));
        Assert.Equal("본문", File.ReadAllText(f));   // 본 내용은 그대로
    }

    [Fact]
    public void 표식이_없으면_꺼낸_파일에도_없다()
    {
        using var tmp = new TempDir("zone0");
        string arc = CopyFixture(tmp, "zip_default", "직접만든.zip");
        string f = tmp.P("꺼낸.txt");
        File.WriteAllText(f, "x");
        Assert.Equal(0, ZoneIdentifier.Copy(arc, f));
        Assert.Null(ZoneIdentifier.Read(f));
        Assert.ThrowsAny<IOException>(() => File.ReadAllBytes(f + ZoneIdentifier.StreamName));
    }

    [Fact]
    public void 폴더면_안의_모든_파일에_붙인다()
    {
        using var tmp = new TempDir("zonedir");
        string arc = CopyFixture(tmp, "zip_default", "받은.zip");
        File.WriteAllText(arc + ZoneIdentifier.StreamName, Zone3);
        string d = tmp.Dir("폴더");
        File.WriteAllText(Path.Combine(tmp.Dir("폴더", "안쪽"), "b.txt"), "b");
        File.WriteAllText(Path.Combine(d, "a.txt"), "a");
        Assert.Equal(2, ZoneIdentifier.Copy(arc, d));
        Assert.Equal(Zone3, File.ReadAllText(Path.Combine(d, "안쪽", "b.txt") + ZoneIdentifier.StreamName));
    }

    /// <summary>표식의 영역 번호 읽기(끌기 데이터의 'ZoneIdentifier' 형식에 실음 — W2-04 1차 검증).</summary>
    [Theory]
    [InlineData("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://mail.example.com/\r\n", 3)]
    [InlineData("[ZoneTransfer]\nZoneId=4\n", 4)]
    [InlineData("﻿[ZoneTransfer]\r\nZoneId = 2\r\n", 2)]
    [InlineData("[ZoneTransfer]\r\nHostUrl=about:internet\r\n", null)]
    [InlineData("", null)]
    [InlineData("ZoneId=셋", null)]
    public void 표식에서_영역_번호를_읽는다(string text, int? expected) =>
        Assert.Equal(expected, ZoneIdentifier.ParseZoneId(System.Text.Encoding.UTF8.GetBytes(text)));

    // ───────────────────────────── RiskyFileTypes ─────────────────────────────

    [Theory]
    [InlineData("설치.exe", true)]
    [InlineData("RUN.BAT", true)]
    [InlineData("업데이트.cmd", true)]
    [InlineData("송장.js", true)]
    [InlineData("스크립트.ps1", true)]
    [InlineData("매크로.vbs", true)]
    [InlineData("설치.msi", true)]
    [InlineData("화면보호기.scr", true)]
    [InlineData("바로가기.lnk", true)]
    [InlineData("레지.reg", true)]
    [InlineData("끝에점.exe.", true)]
    // 흔히 악용되는 것(W2-04 1차 검증 보강)
    [InlineData("도움말.chm", true)]
    [InlineData("콘솔.msc", true)]
    [InlineData("탐색기.scf", true)]
    [InlineData("추가기능.XLL", true)]
    [InlineData("구성요소.wsc", true)]
    [InlineData("스크립틀릿.sct", true)]
    [InlineData("설치정보.inf", true)]
    [InlineData("라이브러리.library-ms", true)]
    [InlineData("설정.settingcontent-ms", true)]
    [InlineData("검색.search-ms", true)]
    [InlineData("서식.ps1xml", true)]
    [InlineData("모듈.psd1", true)]
    [InlineData("코드.vb", true)]
    [InlineData("스크립트.ws", true)]
    [InlineData("디스크.iso", true)]
    [InlineData("디스크.img", true)]
    [InlineData("가상디스크.vhd", true)]
    [InlineData("가상디스크.vhdx", true)]
    // 두 번 누르면 바로 실행·연결되는 것(W2-04 2차 검증 보강)
    [InlineData("원격.rdp", true)]
    [InlineData("스크립트.py", true)]
    [InlineData("스크립트.PYW", true)]
    [InlineData("묶음.pyz", true)]
    [InlineData("바로가기.website", true)]
    [InlineData("자바.jnlp", true)]
    [InlineData("업데이트.msu", true)]
    [InlineData("사진‮gpj.exe", true)]   // 이름 뒤집기 글자(RLO)로 '사진exe.jpg' 처럼 보이게 한 것
    [InlineData("보고서.docx", false)]
    [InlineData("문서.pdf", false)]
    [InlineData("자료.csv", false)]
    [InlineData("사진.jpg", false)]
    [InlineData("표.xlsx", false)]
    [InlineData("exe", false)]
    [InlineData("", false)]
    public void 실행_파일_판정(string name, bool expected) => Assert.Equal(expected, RiskyFileTypes.IsExecutable(name));

    // ───────────────────────────── 설정(settings.json) ─────────────────────────────

    [Fact]
    public void 설정_기본값()
    {
        var s = new AppSettings();
        Assert.Equal(1, s.SchemaVersion);
        Assert.False(s.Ui.PreviewVisible);
        Assert.True(s.Open.WarnExecutables);
    }

    [Fact]
    public void 설정은_camelCase로_저장하고_모르는_필드는_보존한다()
    {
        using var tmp = new TempDir("set");
        string path = tmp.P("settings.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "ui": { "previewVisible": true, "treeWidth": 300 },
              "open": { "warnExecutables": false },
              "extract": { "onCollision": "Rename", "recentFolders": ["D:\\받은 파일"] }
            }
            """);
        var store = new JsonSettingsStore(path);
        var s = store.Load();
        Assert.True(s.Ui.PreviewVisible);
        Assert.False(s.Open.WarnExecutables);

        s.Ui.PreviewVisible = false;
        store.Save(s);
        string json = File.ReadAllText(path);
        Assert.Contains("\"previewVisible\": false", json);
        Assert.Contains("\"warnExecutables\": false", json);
        Assert.Contains("\"treeWidth\": 300", json);          // 뒤 판(W2-09)이 쓴 것 보존
        Assert.Contains("\"onCollision\": \"Rename\"", json);  // 모르는 묶음 보존
        Assert.Contains("받은 파일", json);
        Assert.False(File.Exists(path + ".tmp"), "임시 파일이 남음");
        Assert.False(new JsonSettingsStore(path).Load().Ui.PreviewVisible);
    }

    [Fact]
    public void 깨진_설정은_옆으로_옮기고_기본값으로_시작한다()
    {
        using var tmp = new TempDir("setbad");
        string path = tmp.P("settings.json");
        File.WriteAllText(path, "{ \"ui\": { \"previewVisible\": tr");
        var s = new JsonSettingsStore(path).Load();
        Assert.False(s.Ui.PreviewVisible);
        Assert.False(File.Exists(path));
        var bad = Assert.Single(Directory.GetFiles(tmp.Root, "settings.bad-*.json"));
        Assert.Matches(@"settings\.bad-\d{14}\.json$", bad);
        Assert.Equal("{ \"ui\": { \"previewVisible\": tr", File.ReadAllText(bad));
    }

    [Fact]
    public void 설정이_없으면_기본값이고_첫_저장에서_만든다()
    {
        using var tmp = new TempDir("set0");
        using var _ = TestServices.UseAppRoot(tmp);
        var store = new JsonSettingsStore();   // AppPaths.SettingsFile
        Assert.True(store.Load().Open.WarnExecutables);
        Assert.False(File.Exists(AppPaths.SettingsFile));
        store.Save(new AppSettings());
        Assert.True(File.Exists(AppPaths.SettingsFile));
        Assert.Contains("\"schemaVersion\": 1", File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8));
    }

    /// <summary>암호는 절대 저장하지 않는다(설계 §4) — 설정 형식에 암호 칸이 없다.</summary>
    [Fact]
    public void 설정에_암호_칸이_없다()
    {
        var types = new[] { typeof(AppSettings), typeof(UiSettings), typeof(OpenSettings) };
        foreach (var t in types)
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.DoesNotContain("password", p.Name, StringComparison.OrdinalIgnoreCase);
    }
}
