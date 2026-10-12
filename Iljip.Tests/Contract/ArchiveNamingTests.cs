using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-1] '"이름"\ 에 풀기' 폴더 이름(<see cref="ArchiveNaming.FolderNameFor"/>)과 겹치지 않는 새 폴더(<see cref="ArchiveNaming.UniqueFolder"/>).
/// 같은 규칙이 셸 확장(C++)에도 있다 — 메뉴 제목과 실제 폴더 이름이 같은지는 ShellExtensionContractTests 의 DLL 테스트가 이 표로 대조한다.
/// </summary>
public class ArchiveNamingTests
{
    /// <summary>파일 이름 → 폴더 이름 표(셸 확장 DLL 대조에도 씀).</summary>
    public static readonly TheoryData<string, string> FolderNames = new()
    {
        { "x.zip", "x" },
        { "x.tar.gz", "x" },                 // 예전 '--extract-here' 는 'x.tar' 폴더(B-028 (3))
        { "x.TAR.GZ", "x" },
        { "x.tgz", "x" },
        { "x.tar.bz2", "x" },
        { "x.tar.xz", "x" },
        { "x.7z.001", "x" },
        { "x.zip.001", "x" },
        { "x.part1.rar", "x" },
        { "x.Part12.rar", "x" },
        { "x.vol1.egg", "x" },
        { "x.party.rar", "x.party" },        // 'part' 뒤에 숫자가 없으면 조각 표시가 아님
        { "x.001", "x" },
        { "x.1234", "x" },                   // 숫자 셋이 아니면 그냥 확장자
        { "x.z01", "x" },
        { ".zip", ArchiveNaming.FallbackName },
        { "..zip", ArchiveNaming.FallbackName },
        { "x..zip", "x" },                   // 끝의 점은 Windows 가 지우므로 미리 지움
        { "보고서 .zip", "보고서" },
        { "이름 있는.한글.zip", "이름 있는.한글" },
        { "확장자없음", "확장자없음" },
        { "견적서_2026 [최종] (2).7z", "견적서_2026 [최종] (2)" },
    };

    [Theory]
    [MemberData(nameof(FolderNames))]
    public void 폴더_이름_규칙(string fileName, string expected)
    {
        Assert.Equal(expected, ArchiveNaming.FolderNameFor(fileName));
        // 경로를 줘도 파일 이름만 본다(폴더 이름의 점은 상관없음)
        Assert.Equal(expected, ArchiveNaming.FolderNameFor(Path.Combine(@"C:\폴더.zip\안.tar", fileName)));
    }

    [Fact]
    public void 없으면_그_이름()
    {
        using var tmp = new TempDir("uf");
        Assert.Equal(tmp.P("보고서"), ArchiveNaming.UniqueFolder(tmp.Root, "보고서"));
        Assert.False(Directory.Exists(tmp.P("보고서")), "확인만 하고 만들지는 않아야 함");
    }

    [Fact]
    public void 폴더가_있으면_2_그것도_있으면_3()
    {
        using var tmp = new TempDir("uf2");
        tmp.Dir("보고서");
        Assert.Equal(tmp.P("보고서 (2)"), ArchiveNaming.UniqueFolder(tmp.Root, "보고서"));
        tmp.Dir("보고서 (2)");
        Assert.Equal(tmp.P("보고서 (3)"), ArchiveNaming.UniqueFolder(tmp.Root, "보고서"));
    }

    /// <summary>같은 이름의 '파일'이 있어도 겹치는 것(그 이름으로 폴더를 못 만듦) — (2).</summary>
    [Fact]
    public void 같은_이름_파일이_있어도_2()
    {
        using var tmp = new TempDir("uf3");
        File.WriteAllText(tmp.P("보고서"), "확장자 없는 파일");
        Assert.Equal(tmp.P("보고서 (2)"), ArchiveNaming.UniqueFolder(tmp.Root, "보고서"));
    }

    /// <summary>예전 규칙(단일 스트림 항목 이름)은 그대로 — 마지막 확장자 하나만 뗀다.</summary>
    [Fact]
    public void 단일_스트림_이름은_그대로()
    {
        Assert.Equal("x.tar", ArchiveNaming.SingleStreamName("x.tar.gz"));
        Assert.Equal(ArchiveNaming.FallbackName, ArchiveNaming.SingleStreamName(".gz"));
    }
}
