using System.ComponentModel;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Iljip.Views;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-07, I2-12] 한 화면 압축 창의 규칙(<see cref="CompressionDialogViewModel"/> — 순수 VM 이라 창을 띄우지 않고 시험).
/// 7Z 는 7z 칸을 보이고 ZIP 암호 방식 칸을 숨김, TAR·GZ·BZ2 는 암호·나누기를 끔, 날짜·각각 따로·이름을 바꾸면 미리 보기가 바뀜,
/// 처음 값은 설정(compress.*), 형식에 맞지 않는 값은 답에서 빠짐, 7z.dll 을 못 쓰면 7Z 가 형식 목록에 없음.
/// (창의 실제 모양·탭 순서·찾아보기 창은 MANUAL_CHECKLIST §14)
/// </summary>
public class CompressDialogRulesTests
{
    private static readonly FakeClock Today = new FakeClock().SetLocal(2026, 10, 13);

    private static readonly CompressionFormat[] AllFormats =
        { CompressionFormat.Zip, CompressionFormat.SevenZip, CompressionFormat.Tar, CompressionFormat.GZip, CompressionFormat.BZip2 };

    private static CompressionDialogViewModel Vm(IReadOnlyList<string>? sources = null, CompressSettings? defaults = null,
        string folder = @"C:\자료", bool allFiles = true, IReadOnlyList<CompressionFormat>? formats = null, string? suggested = null)
    {
        sources ??= new[] { @"C:\자료\보고서.xlsx" };
        return new CompressionDialogViewModel(new CompressionDialogRequest
        {
            Sources = sources,
            AllSourcesAreFiles = allFiles,
            SuggestedFolder = folder,
            SuggestedBaseName = suggested ?? CompressNaming.SuggestBaseName(sources),
            AvailableFormats = formats ?? AllFormats,
            Defaults = defaults ?? new CompressSettings(),
        }, Today);
    }

    private static List<string> Changes(CompressionDialogViewModel vm, Action act)
    {
        var changed = new List<string>();
        PropertyChangedEventHandler h = (_, e) => changed.Add(e.PropertyName!);
        vm.PropertyChanged += h;
        act();
        vm.PropertyChanged -= h;
        return changed;
    }

    /// <summary>계약: 7Z 를 고르면 7z 칸(솔리드·이름 숨기기)을 보이고 ZIP 암호 방식 칸은 숨긴다. ZIP 은 그 반대. 나누기는 둘 다.</summary>
    [Fact]
    public void 칠z는_7z_칸을_보이고_ZIP_방식_칸을_숨긴다()
    {
        var vm = Vm();
        Assert.Equal(CompressionFormat.Zip, vm.Format);
        Assert.True(vm.ShowZipEncryption);
        Assert.False(vm.ShowSevenZipOptions);
        Assert.True(vm.ShowSplit);
        Assert.True(vm.PasswordEnabled);

        var changed = Changes(vm, () => vm.Format = CompressionFormat.SevenZip);
        Assert.Equal(".7z", vm.Extension);
        Assert.True(vm.ShowSevenZipOptions);
        Assert.False(vm.ShowZipEncryption);
        Assert.True(vm.ShowSplit);
        Assert.True(vm.PasswordEnabled);
        Assert.Contains(nameof(vm.ShowSevenZipOptions), changed);
        Assert.Contains(nameof(vm.ShowZipEncryption), changed);
        Assert.Contains(nameof(vm.PreviewName), changed);
        Assert.Equal("보고서.7z", vm.PreviewName);

        // 이름 숨기기는 7Z 이고 암호가 있을 때만
        Assert.False(vm.CanEncryptFileNames);
        vm.HasPassword = true;
        Assert.True(vm.CanEncryptFileNames);
        vm.Format = CompressionFormat.Zip;
        Assert.False(vm.CanEncryptFileNames);
    }

    /// <summary>계약: TAR·GZ·BZ2 는 암호와 나누기를 끈다(엔진이 지원하지 않음). GZ·BZ2 는 빼기·원본 지우기도 없다.</summary>
    [Theory]
    [InlineData(CompressionFormat.Tar, true)]
    [InlineData(CompressionFormat.GZip, false)]
    [InlineData(CompressionFormat.BZip2, false)]
    public void TAR_GZ_BZ2는_암호와_나누기를_끈다(CompressionFormat format, bool excludeAndDelete)
    {
        var vm = Vm();
        vm.Format = format;
        Assert.False(vm.PasswordEnabled);
        Assert.False(vm.ShowSplit);
        Assert.False(vm.ShowZipEncryption);
        Assert.False(vm.ShowSevenZipOptions);
        Assert.Contains("ZIP이나 7Z", vm.Hint);
        Assert.Equal(excludeAndDelete, vm.ExcludeAvailable);
        Assert.Equal(excludeAndDelete, vm.DeleteAvailable);
    }

    /// <summary>계약: 날짜를 켜거나 모양을 바꾸면 미리 보기가 바뀌고 알림(PropertyChanged)이 간다 — '보고서_20261013.zip'.</summary>
    [Fact]
    public void 날짜를_바꾸면_미리_보기가_바뀐다()
    {
        var vm = Vm();
        Assert.Equal("보고서.zip", vm.PreviewName);

        var changed = Changes(vm, () => vm.DateEnabled = true);
        Assert.Contains(nameof(vm.PreviewName), changed);
        Assert.Equal("보고서_20261013.zip", vm.PreviewName);

        changed = Changes(vm, () => vm.DateFormat = DateNameFormat.YearMonthDayDashed);
        Assert.Contains(nameof(vm.PreviewName), changed);
        Assert.Equal("보고서_2026-10-13.zip", vm.PreviewName);
        vm.DateFormat = DateNameFormat.ShortYearMonthDay;
        Assert.Equal("보고서_261013.zip", vm.PreviewName);

        Assert.Equal(new[] { "20261013", "2026-10-13", "261013" }, vm.DateFormats.Select(d => d.Label));

        vm.BaseName = "보고서_261013";   // 이미 그 날짜가 있으면 두 번 붙이지 않음
        Assert.Equal("보고서_261013.zip", vm.PreviewName);
        vm.BaseName = "  월말 정산.zip ";   // 확장자를 쳐 넣어도 한 번만
        Assert.Equal("월말 정산_261013.zip", vm.PreviewName);
    }

    /// <summary>계약: '각각 따로'는 원본이 둘 이상일 때만, 켜면 이름 칸을 막고 미리 보기는 '첫 이름 외 N개 (원본마다 하나씩)'.</summary>
    [Fact]
    public void 각각_따로는_원본이_둘_이상일_때_이름_칸을_막는다()
    {
        Assert.False(Vm().CanEachSeparately);

        var vm = Vm(new[] { @"C:\자료\보고서.xlsx", @"C:\자료\거래처A", @"C:\자료\견적.txt" }, allFiles: false);
        Assert.True(vm.CanEachSeparately);
        Assert.Equal("자료", vm.BaseName);   // 여럿이면 그 폴더 이름
        Assert.True(vm.NameEditable);

        var changed = Changes(vm, () => vm.EachSeparately = true);
        Assert.Contains(nameof(vm.NameEditable), changed);
        Assert.Contains(nameof(vm.PreviewName), changed);
        Assert.False(vm.NameEditable);
        Assert.Equal("보고서.zip 외 2개 (원본마다 하나씩)", vm.PreviewName);
        vm.DateEnabled = true;
        Assert.Equal("보고서_20261013.zip 외 2개 (원본마다 하나씩)", vm.PreviewName);

        // 원본 하나면 설정에 '각각 따로'가 켜져 있어도 꺼진 채로 시작
        var one = Vm(defaults: new CompressSettings { EachSeparately = true });
        Assert.False(one.EachSeparately);
        Assert.True(one.NameEditable);
    }

    /// <summary>계약: 이름을 고치지 않았으면 형식을 바꿀 때 제안 이름도 바뀐다(GZ·BZ2 는 '보고서.xlsx' — 풀면 원래 이름). 고친 이름은 그대로.</summary>
    [Fact]
    public void 형식을_바꾸면_제안_이름이_따라간다()
    {
        var vm = Vm();
        vm.Format = CompressionFormat.GZip;
        Assert.Equal("보고서.xlsx", vm.BaseName);
        Assert.Equal("보고서.xlsx.gz", vm.PreviewName);
        vm.Format = CompressionFormat.Zip;
        Assert.Equal("보고서", vm.BaseName);

        vm.BaseName = "내 이름";
        vm.Format = CompressionFormat.BZip2;
        Assert.Equal("내 이름", vm.BaseName);
        Assert.Equal("내 이름.bz2", vm.PreviewName);
    }

    /// <summary>계약(I2-12): 처음 값은 설정 — 형식·수준·암호 방식·7z 칸·나누기·날짜·빼기·시험·원본 지우기·마지막 폴더(있으면 위치 목록에).</summary>
    [Fact]
    public void 처음_값은_설정에서()
    {
        using var t = new TempDir("dlgdefaults");
        string last = t.Dir("보낸 곳");
        var d = new CompressSettings
        {
            LastFormat = CompressionFormat.SevenZip,
            Level = CompressionLevelOption.Maximum,
            ZipEncryption = ZipEncryption.ZipCrypto,
            Solid = false,
            EncryptFileNames = true,
            SplitBytes = 20 * 1024 * 1024,
            DateInName = DateNameFormat.YearMonthDayDashed,
            ExcludeEnabled = false,
            ExcludePatterns = new List<string> { "*.bak", "임시/" },
            TestAfter = true,
            DeleteOriginalsAfter = true,
            LastFolder = last,
        };
        var vm = Vm(defaults: d, folder: t.Root);

        Assert.Equal(CompressionFormat.SevenZip, vm.Format);
        Assert.Equal(3, vm.LevelIndex);
        Assert.True(vm.UseZipCrypto);
        Assert.False(vm.Solid);
        Assert.True(vm.EncryptFileNames);
        Assert.Equal(2, vm.SplitIndex);   // 20MB (메일 첨부용)
        Assert.True(vm.DateEnabled);
        Assert.Equal(DateNameFormat.YearMonthDayDashed, vm.DateFormat);
        Assert.False(vm.ExcludeEnabled);
        Assert.Equal("*.bak\r\n임시/", vm.ExcludePatternsText.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        Assert.True(vm.TestAfter);
        Assert.True(vm.DeleteOriginalsAfter);
        Assert.Equal(t.Root, vm.OutputFolder);
        Assert.Equal(new[] { t.Root, last }, vm.FolderSuggestions);
        Assert.Equal("보고서_2026-10-13.7z", vm.PreviewName);

        // 직접 넣은 크기는 '직접 입력' 칸으로, 없는 마지막 폴더는 목록에 넣지 않음
        var custom = Vm(defaults: new CompressSettings { SplitBytes = 15 * 1024 * 1024, LastFolder = t.P("없는 폴더") });
        Assert.Equal(CompressionOptionsRules.SplitCustomIndex, custom.SplitIndex);
        Assert.Equal("15", custom.SplitCustomText);
        Assert.Equal(new[] { @"C:\자료" }, custom.FolderSuggestions);

        // 새 설정의 처음 값: ZIP·보통·빼기 켬(기본 목록)·시험·지우기 끔·날짜 없음
        var fresh = Vm();
        Assert.Equal(CompressionFormat.Zip, fresh.Format);
        Assert.Equal(2, fresh.LevelIndex);
        Assert.True(fresh.ExcludeEnabled);
        Assert.Equal(PatternFileFilter.DefaultPatterns, PatternFileFilter.ParseLines(fresh.ExcludePatternsText));
        Assert.False(fresh.TestAfter);
        Assert.False(fresh.DeleteOriginalsAfter);
        Assert.False(fresh.DateEnabled);
        Assert.Equal(DateNameFormat.YearMonthDay, fresh.DateFormat);   // 켜면 처음 모양
    }

    /// <summary>
    /// 계약(I2-12 — W2-07 검증 지적): '만들 곳'의 처음 값은 설정 compress.folderChoice — 원본이 있는 폴더(기본), 마지막으로 만든 곳, 정해 둔 폴더.
    /// 고른 폴더가 없으면(지워짐·USB 를 뺌) 원본이 있는 폴더. 목록에는 처음 값·원본 폴더·마지막 폴더·정해 둔 폴더(있는 것만)가 겹치지 않게.
    /// </summary>
    [Fact]
    public void 만들_곳의_처음_값은_설정대로()
    {
        using var t = new TempDir("dlgfolder");
        string src = t.Dir("원본");
        string last = t.Dir("보낸 곳");
        string fixedFolder = t.Dir("늘 여기");
        string gone = t.P("없어진 폴더");

        Assert.Equal(CompressFolderChoice.SourceFolder, new CompressSettings().FolderChoice);   // 기본은 예전처럼 원본이 있는 폴더

        var lastVm = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.LastFolder, LastFolder = last }, folder: src);
        Assert.Equal(last, lastVm.OutputFolder);
        Assert.Equal(new[] { last, src }, lastVm.FolderSuggestions);
        Assert.Null(lastVm.Validate());

        var fixedVm = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.FixedFolder, FixedFolder = fixedFolder, LastFolder = last },
            folder: src);
        Assert.Equal(fixedFolder, fixedVm.OutputFolder);
        Assert.Equal(new[] { fixedFolder, src, last }, fixedVm.FolderSuggestions);

        // 고른 폴더가 없으면 원본이 있는 폴더로(없는 폴더는 목록에도 넣지 않음)
        var lastGone = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.LastFolder, LastFolder = gone }, folder: src);
        Assert.Equal(src, lastGone.OutputFolder);
        Assert.Equal(new[] { src }, lastGone.FolderSuggestions);
        var fixedGone = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.FixedFolder, FixedFolder = gone }, folder: src);
        Assert.Equal(src, fixedGone.OutputFolder);
        var fixedEmpty = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.FixedFolder, FixedFolder = null }, folder: src);
        Assert.Equal(src, fixedEmpty.OutputFolder);
        var relative = Vm(defaults: new CompressSettings { FolderChoice = CompressFolderChoice.FixedFolder, FixedFolder = "보낸 곳" }, folder: src);
        Assert.Equal(src, relative.OutputFolder);   // 전체 경로가 아니면 쓰지 않음

        // 원본 폴더를 고른 설정이면 마지막 폴더는 목록에만
        var sourceVm = Vm(defaults: new CompressSettings { LastFolder = last, FixedFolder = fixedFolder }, folder: src);
        Assert.Equal(src, sourceVm.OutputFolder);
        Assert.Equal(new[] { src, last, fixedFolder }, sourceVm.FolderSuggestions);

        // 설정 창의 '정해 둔 폴더' 칸 검사
        Assert.Equal("정해 둔 폴더를 [찾아보기]로 골라 주세요.", CompressSettings.CheckFixedFolder("  "));
        Assert.Equal("정해 둔 폴더 경로가 올바르지 않아요. [찾아보기]로 골라 주세요.", CompressSettings.CheckFixedFolder("보낸 곳"));
        Assert.Equal($"‘{gone}’ 폴더가 없어요. [찾아보기]로 골라 주세요.", CompressSettings.CheckFixedFolder(gone));
        Assert.Null(CompressSettings.CheckFixedFolder(fixedFolder));
    }

    /// <summary>
    /// 계약(W2-07 검증 지적): 만들 압축이 압축하려는 원본 그 자체면('a.zip' 하나를 같은 곳에 ZIP 으로 — 제안 이름도 a.zip) 창은 [바꾸기]를 주지 않는다 —
    /// 묻는 문구가 '지금 압축하려는 원본'이고, [바꾸기]를 골랐다고 넘겨도 답은 바꾸지 않음(비켜 만들 이름 'a (2).zip').
    /// </summary>
    [Fact]
    public void 원본_그_자체에는_바꾸기를_주지_않는다()
    {
        using var t = new TempDir("dlgself");
        string self = t.P("a.zip");
        File.WriteAllText(self, "원래 압축");
        var vm = Vm(new[] { self }, folder: t.Root);

        Assert.Equal("a.zip", vm.PreviewName);
        Assert.True(vm.TargetExists(split: false));
        Assert.True(vm.TargetIsSource);
        Assert.Equal("‘a.zip’은(는) 지금 압축하려는 원본이에요. 같은 이름으로는 만들 수 없어요.", vm.ExistingTargetMessage);
        Assert.Equal("a (2).zip", vm.AlternativeName(split: false));
        Assert.False(vm.BuildRequest(CompressionOptions.Default, overwriteExisting: true).OverwriteExisting);

        // 다른 이름·다른 형식이면 원본이 아님 — 예전처럼 [바꾸기]를 물을 수 있음
        File.WriteAllText(t.P("b.zip"), "다른 압축");
        vm.BaseName = "b";
        Assert.True(vm.TargetExists(split: false));
        Assert.False(vm.TargetIsSource);
        Assert.Equal("‘b.zip’ 파일이 이미 있어요. 바꿀까요?", vm.ExistingTargetMessage);
        Assert.True(vm.BuildRequest(CompressionOptions.Default, overwriteExisting: true).OverwriteExisting);
        vm.BaseName = "a";
        vm.Format = CompressionFormat.SevenZip;
        Assert.False(vm.TargetIsSource);
    }

    /// <summary>계약(W2-07 검증 지적): GZ·BZ2 에 날짜를 붙이면 원래 확장자 앞에 — '보고서_20261013.xlsx.gz'(풀면 '보고서_20261013.xlsx' — 엑셀로 바로 열림).</summary>
    [Fact]
    public void GZ_BZ2의_날짜는_원래_확장자_앞에()
    {
        var vm = Vm();
        vm.Format = CompressionFormat.GZip;
        vm.DateEnabled = true;
        Assert.Equal("보고서_20261013.xlsx.gz", vm.PreviewName);
        vm.Format = CompressionFormat.BZip2;
        vm.DateFormat = DateNameFormat.YearMonthDayDashed;
        Assert.Equal("보고서_2026-10-13.xlsx.bz2", vm.PreviewName);
        vm.Format = CompressionFormat.Zip;   // ZIP 은 예전처럼 이름 끝에
        Assert.Equal("보고서_2026-10-13.zip", vm.PreviewName);

        var each = Vm(new[] { @"C:\자료\가.txt", @"C:\자료\나.csv" });
        each.Format = CompressionFormat.BZip2;
        each.EachSeparately = true;
        each.DateEnabled = true;
        Assert.Equal("가_20261013.txt.bz2 외 1개 (원본마다 하나씩)", each.PreviewName);
    }

    /// <summary>계약: 7z.dll 을 못 쓰는 PC 면 형식 목록에 7Z 가 없고, 설정의 마지막 형식이 7Z 여도 ZIP 으로 시작한다.</summary>
    [Fact]
    public void 칠z_dll이_없으면_7Z를_고를_수_없다()
    {
        using var t = new TempDir("dlgno7z");
        string f = Path.Combine(t.Root, "보고서.txt");
        File.WriteAllText(f, "x");
        var vmMain = new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services);
        CompressionDialogRequest request;
        using (SevenZipLibrary.SimulateUnavailableForTests())
            request = vmMain.BuildCompressionDialogRequest(new[] { f });
        Assert.DoesNotContain(CompressionFormat.SevenZip, request.AvailableFormats);

        var vm = Vm(defaults: new CompressSettings { LastFormat = CompressionFormat.SevenZip }, formats: request.AvailableFormats);
        Assert.DoesNotContain(vm.Formats, c => c.Format == CompressionFormat.SevenZip);
        Assert.Equal(CompressionFormat.Zip, vm.Format);
        // W2-10: 맨 끝의 '자동 풀림 EXE'(스텁이 있을 때 — 시험 출력에도 sfx 폴더가 복사됨)는 7z.dll 과 상관없이 있다
        Assert.Equal(new[] { "ZIP", "TAR", "GZ", "BZ2", "자동" }, vm.Formats.Select(c => c.Label.Split(' ')[0]));
    }

    /// <summary>계약: [확인] 전에 보는 것 — 빈 이름·쓸 수 없는 글자·없는 폴더·이상한 경로·GZ 에 폴더·GZ 에 여러 파일(각각 따로면 됨).</summary>
    [Fact]
    public void 확인_전에_보는_것()
    {
        using var t = new TempDir("dlgvalidate");
        var vm = Vm(folder: t.Root);
        Assert.Null(vm.Validate());

        vm.BaseName = "  ";
        Assert.Equal(CompressNaming.EmptyNameMessage, vm.Validate());
        vm.BaseName = "보고서:최종";
        Assert.Equal(CompressNaming.InvalidCharsMessage, vm.Validate());
        vm.BaseName = "보고서";

        vm.OutputFolder = t.P("없는 폴더");
        Assert.Equal($"‘{t.P("없는 폴더")}’ 폴더가 없어요. [찾아보기]로 골라 주세요.", vm.Validate());
        vm.OutputFolder = "자료";
        Assert.Equal("만들 곳(폴더) 경로가 올바르지 않아요. [찾아보기]로 골라 주세요.", vm.Validate());
        vm.OutputFolder = " ";
        Assert.Equal("만들 곳(폴더)을 골라 주세요.", vm.Validate());

        var folderVm = Vm(new[] { @"C:\자료\거래처A" }, folder: t.Root, allFiles: false);
        folderVm.Format = CompressionFormat.GZip;
        Assert.Equal("GZ·BZ2 는 파일만 압축할 수 있어요. 폴더가 있으면 ZIP·7Z·TAR 를 골라 주세요.", folderVm.Validate());

        var many = Vm(new[] { @"C:\자료\가.txt", @"C:\자료\나.txt" }, folder: t.Root);
        many.Format = CompressionFormat.BZip2;
        Assert.Equal("GZ·BZ2 는 파일 하나만 담을 수 있어요. ‘각각 따로 압축’을 켜거나 ZIP·7Z 를 골라 주세요.", many.Validate());
        many.EachSeparately = true;
        Assert.Null(many.Validate());
        many.BaseName = "";   // 각각 따로면 이름 칸은 보지 않음
        Assert.Null(many.Validate());
    }

    /// <summary>계약: 답에는 그 형식·원본 수에 맞는 값만 — GZ 의 빼기·원본 지우기, 원본 하나의 각각 따로는 빠진다. 이름에 친 확장자는 뗀다. 빼기 목록은 줄 단위.</summary>
    [Fact]
    public void 답에는_맞는_값만_담긴다()
    {
        var vm = Vm(defaults: new CompressSettings { DeleteOriginalsAfter = true, EachSeparately = true, ExcludeEnabled = true });
        vm.BaseName = "보고서.zip";
        vm.DateEnabled = true;
        vm.ExcludePatternsText = "*.bak\r\n\r\n  임시/  \n";
        var options = new CompressionOptions { Level = CompressionLevelOption.Maximum };
        var zip = vm.BuildRequest(options, overwriteExisting: true);
        Assert.Equal("보고서", zip.BaseName);
        Assert.Equal(@"C:\자료", zip.OutputFolder);
        Assert.Equal(CompressionFormat.Zip, zip.Format);
        Assert.Same(options, zip.Options);
        Assert.False(zip.EachSeparately);   // 원본 하나
        Assert.Equal(DateNameFormat.YearMonthDay, zip.DateInName);
        Assert.True(zip.ExcludeEnabled);
        Assert.Equal(new[] { "*.bak", "임시/" }, zip.ExcludePatterns);
        Assert.True(zip.DeleteOriginalsAfter);
        Assert.True(zip.OverwriteExisting);
        Assert.Equal(new[] { @"C:\자료\보고서.xlsx" }, zip.Sources);

        vm.Format = CompressionFormat.GZip;
        var gz = vm.BuildRequest(CompressionOptions.Default, overwriteExisting: false);
        Assert.False(gz.ExcludeEnabled);
        Assert.False(gz.DeleteOriginalsAfter);
        Assert.False(gz.OverwriteExisting);

        vm.DateEnabled = false;
        Assert.Equal(DateNameFormat.None, vm.BuildRequest(CompressionOptions.Default, false).DateInName);
    }

    /// <summary>계약: 하나로 만들 때 같은 이름(나누면 첫 조각)이 있으면 창이 묻는다 — 비켜 만들 이름은 '보고서 (2).zip'. 각각 따로는 묻지 않는다(흐름이 비킴).</summary>
    [Fact]
    public void 같은_이름이_있는지_본다()
    {
        using var t = new TempDir("dlgexists");
        var vm = Vm(folder: t.Root);
        Assert.False(vm.TargetExists(split: false));
        File.WriteAllText(t.P("보고서.zip"), "x");
        Assert.True(vm.TargetExists(split: false));
        Assert.Equal("보고서 (2).zip", vm.AlternativeName(split: false));
        Assert.Equal("‘보고서.zip’ 파일이 이미 있어요. 바꿀까요?", vm.ExistingTargetMessage);

        vm.DateEnabled = true;
        Assert.False(vm.TargetExists(split: false));
        File.WriteAllText(t.P("보고서_20261013.z01"), "x");   // 나눈 ZIP 의 첫 조각
        Assert.True(vm.TargetExists(split: true));
        Assert.Equal("보고서_20261013 (2).zip", vm.AlternativeName(split: true));

        vm.Format = CompressionFormat.SevenZip;
        File.WriteAllText(t.P("보고서_20261013.7z.001"), "x");
        Assert.True(vm.TargetExists(split: true));
        Assert.False(vm.TargetExists(split: false));

        var each = Vm(new[] { t.P("보고서.txt"), t.P("다른.txt") }, folder: t.Root, suggested: "보고서");
        each.EachSeparately = true;
        Assert.False(each.TargetExists(split: false));
    }

    /// <summary>계약: 예전 옵션 창 어댑터(AskCompressionOptions) — 확장자가 정해진 옵션 칸만의 VM. 이름·위치 칸이 없고 형식은 그 확장자.</summary>
    [Theory]
    [InlineData(".7z", CompressionFormat.SevenZip)]
    [InlineData(".tar", CompressionFormat.Tar)]
    [InlineData(".zip", CompressionFormat.Zip)]
    public void 옵션만_보이는_창(string ext, CompressionFormat format)
    {
        var vm = new CompressionDialogViewModel(new CompressionDialogRequest { FixedExtension = ext }, Today);
        Assert.False(vm.ShowNameAndPlace);
        Assert.False(vm.CanEachSeparately);
        Assert.Equal(format, vm.Format);
        Assert.Equal(ext, vm.Extension);
        Assert.Equal(CompressionOptionsRules.LayoutFor(ext).ShowSplit, vm.ShowSplit);
        Assert.Equal(string.Empty, vm.PreviewName);
        Assert.Null(vm.Validate());
        Assert.False(vm.TargetExists(false));
    }

    /// <summary>
    /// 계약: 창(XAML·코드)이 VM 규칙을 쓴다 — 이름·위치·형식·수준·암호·나누기·확인란 순, 기존 칸 이름 그대로, '각각 따로'·'날짜'·'빼기'·'시험'·'원본 휴지통' 칸,
    /// 같은 이름이면 [바꾸기]/[새 이름]/[취소], 예전 옵션 창 어댑터는 같은 창을 씀, 설정 창에 빼기 목록.
    /// </summary>
    [Fact]
    public void 창은_VM_규칙을_쓴다()
    {
        string xaml = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml"));
        string[] order =
        {
            "x:Name=\"NameBox\"", "x:Name=\"FolderCombo\"", "x:Name=\"FormatCombo\"", "x:Name=\"LevelCombo\"", "x:Name=\"PasswordBox1\"",
            "x:Name=\"SplitCombo\"", "x:Name=\"EachCheck\"", "x:Name=\"DateCheck\"", "x:Name=\"ExcludeCheck\"", "x:Name=\"TestCheck\"",
            "x:Name=\"DeleteOriginalsCheck\"",
        };
        int at = -1;
        foreach (var name in order)
        {
            int i = xaml.IndexOf(name, StringComparison.Ordinal);
            Assert.True(i > at, $"{name} 이 순서대로 있지 않음");
            at = i;
        }
        Assert.Contains("{Binding PreviewName", xaml);
        Assert.Contains("{Binding EachSeparately", xaml);
        Assert.Contains("{Binding NameEditable}", xaml);
        Assert.Contains("각각 따로 압축", xaml);
        Assert.Contains("이름에 날짜 붙이기", xaml);
        Assert.Contains("쓸모없는 파일 빼기", xaml);
        Assert.Contains("빼기 목록 고치기", xaml);
        Assert.Contains("다 만들고 시험하기", xaml);
        Assert.Contains("원본을 휴지통으로", xaml);

        string code = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml.cs"));
        Assert.Contains("_vm.Validate()", code);
        Assert.Contains("_vm.BuildRequest(", code);
        Assert.Contains("_vm.TargetExists(", code);
        Assert.Contains("바꾸기 (원래 파일은 없어져요)", code);
        Assert.Contains("_vm.TargetIsSource", code);   // 원본 그 자체면 [바꾸기] 없이 묻는다(W2-07 검증 지적)

        string wpf = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "WpfUserInteraction.cs"));
        Assert.Contains("CompressionDialogViewModel(request, SystemClock.Instance)", wpf);
        Assert.Contains("public CompressionRequest? AskCompression(CompressionDialogRequest request)", wpf);

        string settings = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "SettingsDialog.xaml.cs"));
        Assert.Contains("Compress.ExcludePatterns = PatternFileFilter.ParseLines(", settings);
        Assert.Contains("Compress.ExcludeEnabled", settings);
        // W2-07 검증 지적: 설정 창에서 '만들 곳' 처음 값(원본 폴더·마지막 폴더·정해 둔 폴더)과 '원본을 휴지통으로' 처음 값을 고름
        Assert.Contains("Compress.FolderChoice = ", settings);
        Assert.Contains("Compress.FixedFolder = ", settings);
        Assert.Contains("CompressSettings.CheckFixedFolder(", settings);
        Assert.Contains("Compress.DeleteOriginalsAfter = ", settings);
        string settingsXaml = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "SettingsDialog.xaml"));
        Assert.Contains("원본이 있는 폴더", settingsXaml);
        Assert.Contains("마지막으로 만든 곳", settingsXaml);
        Assert.Contains("정해 둔 폴더", settingsXaml);

        // 압축 순서: 예전 3단계(저장 창 → 옵션 창) 대신 한 화면 창 하나
        string main = File.ReadAllText(RepoFiles.PathOf("Iljip", "ViewModels", "MainViewModel.cs"))
            + File.ReadAllText(RepoFiles.PathOf("Iljip", "ViewModels", "MainViewModel.Compress.cs"));
        Assert.Contains("_ui.AskCompression(", main);
        Assert.DoesNotContain("_ui.AskCompressionOptions(", main);
        Assert.DoesNotContain("_ui.AskSavePath(\"압축 파일로 저장", main);
    }
}
