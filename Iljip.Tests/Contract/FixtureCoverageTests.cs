using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>픽스처·매니페스트 자체의 건강 검사 — 빠진 픽스처나 깨진 파일이 있으면 다른 테스트가 '조용히' 덜 돌게 된다.</summary>
public class FixtureCoverageTests
{
    [Fact]
    public void 매니페스트의_모든_픽스처가_어느_테스트엔가_속한다()
    {
        var all = FixtureCatalog.All.Keys.ToHashSet();
        var covered = FixtureSets.AllCovered.ToHashSet();
        Assert.Empty(all.Except(covered));     // 새 픽스처를 만들면 FixtureSets 에 넣을 것
        Assert.Empty(covered.Except(all));     // 없어진 픽스처를 가리키면 안 됨
    }

    /// <summary>git 에 올라가는(commit_ok) 픽스처 파일과 분할 조각이 모두 있다.</summary>
    [Fact]
    public void 커밋되는_픽스처_파일이_모두_있다()
    {
        var missing = FixtureCatalog.All.Values.Where(f => f.CommitOk)
            .SelectMany(f => f.SplitVolumes.Count > 0 ? f.SplitVolumes : new[] { f.File })
            .Where(rel => !File.Exists(FixtureCatalog.PathOf(rel)))
            .ToList();
        Assert.Empty(missing);
    }

    /// <summary>기대값 자체가 말이 되는지(파일은 sha256 64자리, 폴더는 null, 경로는 '/' 구분·끝 '/' 없음).</summary>
    [Fact]
    public void 기대값_형식이_올바르다()
    {
        foreach (var f in FixtureCatalog.All.Values)
        {
            Assert.NotEmpty(f.Expected);
            foreach (var e in f.Expected)
            {
                Assert.DoesNotContain('\\', e.Path);
                Assert.False(e.Path.EndsWith('/'), $"{f.Id}: {e.Path}");
                if (e.IsDir) Assert.Null(e.Sha256);
                else Assert.Matches("^[0-9a-f]{64}$", e.Sha256);
            }
        }
    }

    /// <summary>
    /// 테스트 실행 중 픽스처를 고치지 않는다는 확인: 원본 소스(src)의 파일 sha256 이 기대값과 같다.
    /// src/ 는 출력 폴더로 복사하지 않으므로 소스 트리에서 찾는다 — 못 찾으면 '통과'가 아니라 '건너뜀'으로 보고.
    /// ★ 빈 폴더(src/일집_샘플/빈폴더)는 git 이 기록하지 않아 새로 클론하면 없는 게 정상이다. 그래서 여기서는 '파일'만 본다
    /// (매니페스트의 일집_샘플 계열 expected_entries 에는 '일집_샘플/빈폴더'(폴더)가 있음 — 반디집 픽스처 안에는 들어 있다).
    /// </summary>
    [SourceFixturesFact]
    public void 원본_샘플_파일이_기대값과_같다()
    {
        string srcRoot = SourceFixturesFactAttribute.Find()!;
        var fx = FixtureCatalog.Get("zip_default");
        foreach (var e in fx.ExpectedFiles)
        {
            string p = Path.Combine(srcRoot, "src", e.Path.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(p), "원본 없음: " + p);
            Assert.Equal(e.Sha256, TreeAssert.Sha256(p));
        }
    }
}

/// <summary>시간대 독립 확인 스위치(ILJIP_TEST_TZ)가 정말 먹었는지 — 안 먹은 채 '통과'로 보이면 확인이 무의미하다.</summary>
public class TimeZoneSwitchTests
{
    [Fact]
    public void ILJIP_TEST_TZ_를_주면_이_프로세스의_현지_시간대가_그것이다()
    {
        string? id = Environment.GetEnvironmentVariable(TestTimeZone.EnvVar);
        if (string.IsNullOrWhiteSpace(id)) return;   // 스위치를 안 쓴 보통 실행
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(id.Trim()).BaseUtcOffset, TimeZoneInfo.Local.BaseUtcOffset);
        Assert.Equal(id.Trim(), TimeZoneInfo.Local.Id);
        // 파일 시각 변환(File.GetLastWriteTime = UTC → 현지)도 바뀐 시간대를 따른다
        Assert.Equal(TimeZoneInfo.ConvertTimeFromUtc(FixtureCatalog.FixedMtimeUtc, TimeZoneInfo.Local), FixtureCatalog.FixedMtimeUtc.ToLocalTime());
    }

    /// <summary>
    /// 흉내를 켜도 <see cref="TestTimeZone.OsLocal"/> 은 이 PC(OS)의 진짜 시간대다 — 다른 프로세스(자동 풀림 스텁)가 쓴 벽시계를 비교할 때 기준
    /// (Wave 2 통합 점검). 기준이 흉내 시간대로 바뀌어 버리면 그 비교가 무의미해진다.
    /// </summary>
    [Fact]
    public void OS_시간대는_흉내와_상관없이_이_PC의_시간대다()
    {
        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\TimeZoneInformation");
        string? osId = key?.GetValue("TimeZoneKeyName") as string;
        Assert.False(string.IsNullOrWhiteSpace(osId), "레지스트리에서 이 PC 의 시간대 이름을 읽지 못함");
        Assert.Equal(osId!.Trim('\0', ' '), TestTimeZone.OsLocal.Id);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TestTimeZone.EnvVar)))
            Assert.Equal(TimeZoneInfo.Local.Id, TestTimeZone.OsLocal.Id);
    }
}

/// <summary>소스 트리의 Fixtures/src 를 찾지 못하면(출력 폴더만 있는 환경 등) 건너뛰는 Fact.</summary>
public sealed class SourceFixturesFactAttribute : FactAttribute
{
    public SourceFixturesFactAttribute()
    {
        if (Find() is null) Skip = "소스 트리의 Iljip.Tests/Fixtures/src 를 찾지 못함(출력 폴더만 있는 환경) — 원본 sha 확인 생략";
    }

    public static string? Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string cand = Path.Combine(dir.FullName, "Iljip.Tests", "Fixtures");
            if (Directory.Exists(Path.Combine(cand, "src"))) return cand;
            dir = dir.Parent;
        }
        const string known = @"D:\Python\일집\Iljip.Tests\Fixtures";
        return Directory.Exists(Path.Combine(known, "src")) ? known : null;
    }
}
