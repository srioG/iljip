using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-12 I1-2] 탐색기 우클릭 '일집 ▶' 하위 메뉴 규칙(<see cref="ShellMenu"/> — C++ IljipMenu.cpp 의 C# 거울). 고른 것에 따라:
/// 압축 하나 → 일집으로 열기·여기에 풀기·"이름"\ 에 풀기·알아서 풀기·다른 곳에 풀기..., 압축 여럿 → 압축 풀기...·각각 알아서 풀기·각각 이름 폴더에 풀기,
/// 파일·폴더 → 압축하기...·"이름.zip"으로 압축·"이름.7z"로 압축. 확장자와 '폴더인가'만 본다. 실제 DLL 과의 대조는 ShellExtensionContractTests.
/// </summary>
public class ShellMenuTests
{
    private static ShellMenu.Item F(string p) => new(p, false);
    private static ShellMenu.Item D(string p) => new(p, true);

    private static (string Title, string Verb)[] Menu(params ShellMenu.Item[] items) =>
        ShellMenu.ItemsFor(items).Select(i => (i.Title, i.Verb)).ToArray();

    [Fact]
    public void 압축_하나()
    {
        Assert.Equal(new[]
        {
            ("일집으로 열기", "--open"),
            ("여기에 풀기", "--extract-here"),
            ("\"보고서 2026\"\\ 에 풀기", "--extract-to-folder"),
            ("알아서 풀기", "--extract-smart"),
            ("다른 곳에 풀기...", "--extract"),
        }, Menu(F(@"C:\받은 파일\보고서 2026.zip")));
        Assert.Equal("\"자료\"\\ 에 풀기", Menu(F(@"C:\a\자료.tar.gz"))[2].Title);
        Assert.Equal("\"자료\"\\ 에 풀기", Menu(F(@"C:\a\자료.7z.001"))[2].Title);
        Assert.Equal("\"자료\"\\ 에 풀기", Menu(F(@"C:\a\자료.EGG"))[2].Title);   // 대소문자 무시
        Assert.Equal(ShellSelectionKind.OneArchive, ShellMenu.Classify(new[] { F(@"C:\a\b.iso") }));
    }

    [Fact]
    public void 압축_여럿()
    {
        Assert.Equal(new[]
        {
            ("압축 풀기...", "--extract"),
            ("각각 알아서 풀기", "--extract-smart"),
            ("각각 이름 폴더에 풀기", "--extract-to-folder"),
        }, Menu(F(@"C:\a\x.zip"), F(@"C:\a\y.7z"), F(@"C:\a\z.rar")));
    }

    /// <summary>파일·폴더(압축이 아닌 것이 하나라도 섞이면, 이름이 x.zip 인 폴더도): 압축 메뉴. 이름은 압축 창 제안과 같은 규칙.</summary>
    [Fact]
    public void 파일과_폴더()
    {
        Assert.Equal(new[]
        {
            ("압축하기...", "--compress"),
            ("\"보고서.zip\"으로 압축", "--compress-zip"),
            ("\"보고서.7z\"로 압축", "--compress-7z"),
        }, Menu(F(@"C:\a\보고서.xlsx")));
        Assert.Equal("\"자료.v2.zip\"으로 압축", Menu(D(@"C:\a\자료.v2"))[1].Title);         // 폴더 이름은 그대로
        Assert.Equal("\"a.zip\"으로 압축", Menu(F(@"C:\a\x.zip"), F(@"C:\a\b.txt"))[1].Title);   // 섞이면 압축 메뉴 — 이름은 그 폴더
        Assert.Equal("\"x.zip.zip\"으로 압축", Menu(D(@"C:\a\x.zip"))[1].Title);                  // 이름이 .zip 인 폴더
        Assert.Equal("\".gitignore.zip\"으로 압축", Menu(F(@"C:\a\.gitignore"))[1].Title);
        Assert.Equal("\"C.zip\"으로 압축", Menu(D(@"C:\"))[1].Title);                             // 드라이브 뿌리
        // 공유 폴더 뿌리(\\서버\공유)에서 여럿 — 압축 창 제안과 같은 이름(뿌리는 DirectoryInfo.Name 이 통째로라 못 쓰는 글자만 뺌)
        var unc = new[] { @"\\서버\공유\a.txt", @"\\서버\공유\b.txt" };
        Assert.Equal($"\"{CompressNaming.SuggestBaseName(unc)}.zip\"으로 압축", Menu(F(unc[0]), F(unc[1]))[1].Title);
        Assert.Empty(ShellMenu.ItemsFor(Array.Empty<ShellMenu.Item>()));
    }

    /// <summary>메뉴 이름은 실제로 만드는 이름(<see cref="CompressNaming.SuggestBaseName"/>)과 같다 — 진짜 파일·폴더로.</summary>
    [Fact]
    public void 메뉴_이름은_실제로_만드는_이름과_같다()
    {
        using var tmp = new TempDir("menuname");
        string folder = tmp.Dir("보고서 (최종).v2");
        string file = Path.Combine(tmp.Root, "견적서.2026.xlsx");
        File.WriteAllText(file, "x");
        string dot = Path.Combine(tmp.Root, ".env");
        File.WriteAllText(dot, "x");
        foreach (var sources in new[] { new[] { folder }, new[] { file }, new[] { dot }, new[] { file, folder }, new[] { folder + "\\" } })
        {
            var items = sources.Select(s => new ShellMenu.Item(s, Directory.Exists(s))).ToList();
            Assert.Equal(CompressNaming.SuggestBaseName(sources), ShellMenu.CompressBaseName(items));
            Assert.Equal(CompressNaming.SuggestBaseName(sources, CompressionFormat.SevenZip), ShellMenu.CompressBaseName(items));
        }
    }

    /// <summary>긴 이름은 40자를 넘으면 39자 + '…'(서로게이트 쌍은 반으로 자르지 않음).</summary>
    [Fact]
    public void 긴_이름은_줄인다()
    {
        string forty = new('가', 40);
        Assert.Equal(forty, ShellMenu.ShortenForMenu(forty));
        Assert.Equal(new string('가', 39) + "…", ShellMenu.ShortenForMenu(forty + "나"));
        string emoji = new string('가', 38) + "😀" + "끝끝";   // 39·40번째 글자가 서로게이트 쌍
        Assert.Equal(new string('가', 38) + "…", ShellMenu.ShortenForMenu(emoji));
        Assert.Equal($"\"{new string('가', 39)}…\"\\ 에 풀기", Menu(F($@"C:\a\{new string('가', 50)}.zip"))[2].Title);
    }

    /// <summary>
    /// 압축으로 보는 확장자: 파일 연결(<see cref="FileAssociationRegistrar.Extensions"/>)과 같되 '.z01'(나뉜 ZIP 앞 조각 — 마지막 .zip 으로 엶)만 뺀다.
    /// </summary>
    [Fact]
    public void 압축_확장자는_파일_연결과_같다()
    {
        Assert.Equal(FileAssociationRegistrar.Extensions.Except(new[] { ".z01" }).OrderBy(e => e), ShellMenu.ArchiveExtensions.OrderBy(e => e));
        Assert.Equal(ShellMenu.ArchiveExtensions.Count, ShellMenu.ArchiveExtensions.Distinct().Count());
        Assert.True(ShellMenu.IsArchiveName(@"C:\a\자료.part1.rar"));
        Assert.True(ShellMenu.IsArchiveName(@"C:\a\b.TGZ"));
        Assert.False(ShellMenu.IsArchiveName(@"C:\a\b.z01"));
        Assert.False(ShellMenu.IsArchiveName(@"C:\a\setup.exe"));   // 자동 풀림 exe 는 셸 단계에서 압축으로 치지 않음
        Assert.False(ShellMenu.IsArchiveName(@"C:\a\zip"));
    }
}
