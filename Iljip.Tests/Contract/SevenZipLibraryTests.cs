using System.Diagnostics;
using System.Security.Cryptography;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01] 7z.dll(7-Zip 26.03 x64, SharpSevenZip 2.0.128 패키지에 든 것)이 빌드 출력의 x64 폴더에 있고, 이 PC 에서 실제로 불려 쓰인다.
/// 서명 없는 네이티브 DLL 이라 다른 PC 의 보안 정책이 막을 수 있다 — 그때 동작(7z·RAR 는 SharpCompress 로 대신)은 MANUAL_CHECKLIST.
/// </summary>
public class SevenZipLibraryTests
{
    /// <summary>계약: 출력 폴더에 x64\7z.dll 이 있고(x86 은 복사하지 않음), 불러와 7z 풀기 기능이 된다.</summary>
    [Fact]
    public void x64_7z_dll이_출력에_있고_불러진다()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "x64", "7z.dll");
        Assert.True(File.Exists(dll), "x64\\7z.dll 이 테스트 출력에 없음: " + dll);
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "x86", "7z.dll")), "x86 7z.dll 은 복사하지 않기로 함(64비트 전용)");
        Assert.True(Environment.Is64BitProcess, "테스트 프로세스가 64비트가 아님");
        Assert.Equal(dll, SevenZipLibrary.LibraryPath);
        Assert.True(SevenZipLibrary.IsAvailable, "7z.dll 을 불러오지 못함: " + SevenZipLibrary.LoadError);
        Assert.Null(SevenZipLibrary.LoadError);
    }

    /// <summary>계약: 7z.dll 로 반디집 7z 를 열면 15개 항목(엔진이 실제로 쓰임 — SharpCompress 대신).</summary>
    [Fact]
    public void 반디집_7z가_7z_dll로_열린다()
    {
        var fx = FixtureCatalog.Get("7z_default");
        var src = new SevenZipArchiveService().CreateSource(fx.FullPath);
        Assert.NotNull(src);   // null 이면 SharpCompress 대체 경로로 간 것
        var entries = SevenZipEngine.List(src!, null, CancellationToken.None);
        TreeAssert.ListEquals(entries, fx.Expected, context: "7z.dll 직접");
    }

    /// <summary>
    /// 계약: 7z.dll 을 못 불러온 PC(보안 정책이 막음 — 이 흐름에서만 흉내)에서도 7z(숫자 분할 포함)는 SharpCompress 로 열고 풀린다.
    /// 7z.dll 전용 형식(ISO·zipx·LZH·XZ·.tar.xz 등)은 쉬운 안내(EngineUnavailableException)로 끝난다. 7z 만들기(W2-02)도 쉬운 안내(EngineUnavailableException)로 끝나고 파일을 만들지 않는다.
    /// </summary>
    [Fact]
    public async Task 막힌_PC에서도_7z는_SharpCompress로_대신_풀린다()
    {
        using var _ = SevenZipLibrary.SimulateUnavailableForTests();
        Assert.False(SevenZipLibrary.IsAvailable);
        using var tmp = new TempDir("no7z");
        foreach (var id in new[] { "7z_default", "7z_split" })
        {
            var fx = FixtureCatalog.Get(id);
            var svc = Svc.For(fx.FullPath);
            Assert.Null(((SevenZipArchiveService)svc).CreateSource(fx.FullPath));   // 7z.dll 경로를 안 탐
            TreeAssert.ListEquals(await svc.ListEntriesAsync(fx.FullPath), fx.Expected, context: id);
            string dest = tmp.P(id);
            await svc.ExtractAsync(fx.FullPath, dest);
            TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
        }

        var iso = FixtureCatalog.Get("iso_default");
        var isoEx = await Svc.Catch(() => Svc.For(iso.FullPath).ListEntriesAsync(iso.FullPath));
        Assert.Equal(EngineUnavailableException.DefaultMessage, Assert.IsType<EngineUnavailableException>(isoEx).Message);
        // 7z.dll 전용 형식(zipx·iso·cab·lzh·wim·xz·txz)은 목록·풀기 모두 같은 한국어 안내(엔진 영어 원문을 덧붙이지 않음)로 끝나고
        // 아무것도 남기지 않는다(.tar.xz 도 — 안쪽 tar 는 펴지 못함). CAB·WIM 은 픽스처가 없어 만든 파일로.
        string cab = tmp.P("드라이버.cab");
        File.WriteAllBytes(cab, RawCab.Build(new[] { ("a.txt", new byte[] { 1, 2, 3 }) }));
        string wim = tmp.P("이미지.wim");
        File.WriteAllBytes(wim, "MSWIM\0\0\0"u8.ToArray().Concat(new byte[200]).ToArray());
        var paths = new[] { "zipx_default", "lzh_default", "xz_single", "txz_two_step", "iso_default" }.Select(id => FixtureCatalog.Get(id).FullPath)
            .Append(cab).Append(wim);
        foreach (var path in paths)
        {
            string name = Path.GetFileName(path);
            var svc = Svc.For(path);
            var listEx = await Svc.Catch(() => svc.ListEntriesAsync(path));
            Assert.True(listEx is EngineUnavailableException, $"{name}: 목록 예외 {listEx?.GetType().Name}");
            string dest = tmp.P("x_" + name);
            var ex = await Svc.Catch(() => svc.ExtractAsync(path, dest));
            Assert.Equal(EngineUnavailableException.DefaultMessage, Assert.IsType<EngineUnavailableException>(ex).Message);
            Assert.Equal(EngineUnavailableException.DefaultMessage, ErrorMessages.ToUserMessage(ex!));
            Assert.False(Directory.Exists(dest), name + ": 대상 폴더가 생김");
        }
        // 저장 형식에 7Z 가 없다(W2-02 부터 7z 만들기는 7z.dll 로만 — 못 쓰는 PC 에서는 고를 수 없고, 불러도 쉬운 안내로 끝나며 파일을 안 만든다)
        Assert.DoesNotContain("7z", new ArchiveServiceLocator().BuildSaveFilter(), StringComparison.OrdinalIgnoreCase);
        var make = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { iso.FullPath }, tmp.P("x.7z")));
        Assert.Equal(EngineUnavailableException.DefaultMessage, ErrorMessages.ToUserMessage(Assert.IsType<EngineUnavailableException>(make)));
        Assert.False(File.Exists(tmp.P("x.7z")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(tmp.Root, ".~*"));   // 임시 파일도 안 남김
    }

    /// <summary>
    /// 계약(W2-01): 7z.dll 이 될 때 목록을 본(안쪽 tar 가 캐시에 있는) .tar.xz 도 7z.dll 이 막히면 목록부터 같은 안내로 끝난다 —
    /// 목록만 보이고 풀기는 안 되는 어긋남이 없고, 선택 풀기도 캐시로 몰래 되지 않는다(풀기와 같은 규칙). 테스트 순서에 따라 흔들리던 것을 고정.
    /// </summary>
    [Fact]
    public async Task 캐시된_tar_xz도_7z_dll이_막히면_목록부터_안내한다()
    {
        using var tmp = new TempDir("no7zcache");
        string arc = tmp.P("캐시.tar.xz");
        File.Copy(FixtureCatalog.Get("txz_two_step").FullPath, arc);
        var svc = Svc.For(arc);
        var entries = await svc.ListEntriesAsync(arc);   // 안쪽 tar 를 캐시에 둠
        string first = entries.First(e => !e.IsDirectory).Path;

        using var _ = SevenZipLibrary.SimulateUnavailableForTests();
        var listEx = await Svc.Catch(() => svc.ListEntriesAsync(arc));
        Assert.Equal(EngineUnavailableException.DefaultMessage, Assert.IsType<EngineUnavailableException>(listEx).Message);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractEntriesAsync(arc, dest, new[] { first }));
        Assert.Equal(EngineUnavailableException.DefaultMessage, ErrorMessages.ToUserMessage(Assert.IsType<EngineUnavailableException>(ex)));
        Assert.False(Directory.Exists(dest), "대상 폴더가 생김");
    }

    /// <summary>
    /// 계약(W1-B 검증 지적): 7z.dll 을 못 불러온 PC 에서 SharpCompress 로 대신 열 때도 암호 문제를 일집 예외(쉬운 한국어)로 알린다 —
    /// 예전엔 창 없이 풀기에서 '알 수 없는 오류가 났어요: Encrypted 7Zip archive has no password specified.'(영어),
    /// 틀린 암호는 'Data Error'(화면이 암호를 다시 묻지 않음)였다. 맞는 암호면 그대로 풀린다.
    /// </summary>
    [Fact]
    public async Task 막힌_PC에서도_암호_7z는_암호를_묻는다()
    {
        using var _ = SevenZipLibrary.SimulateUnavailableForTests();
        using var tmp = new TempDir("no7zpw");

        var header = FixtureCatalog.Get("7z_header_encrypted");
        var svc = Svc.For(header.FullPath);
        // W2-02(I1-7): 목록까지 잠긴 7z 는 전용 하위 형식(화면이 열 때 묻는 신호) — SharpCompress 로 대신 열어도 같다
        var none = Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ListEntriesAsync(header.FullPath)));
        Assert.True(none.IsHeaderEncrypted);
        Assert.Equal(PasswordRequiredException.HeaderMessage, ErrorMessages.ToUserMessage(none));
        var wrong = Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ListEntriesAsync(header.FullPath, "틀린암호")));
        Assert.True(wrong.IsHeaderEncrypted);
        string ok = tmp.P("ok");
        await svc.ExtractAsync(header.FullPath, ok, header.Password);
        TreeAssert.ExtractedTreeEquals(ok, header.Expected, context: "막힌 PC 헤더 암호 7z");

        var visible = FixtureCatalog.Get("py7z_pw_names_visible");
        var svc2 = Svc.For(visible.FullPath);
        Assert.NotEmpty(await svc2.ListEntriesAsync(visible.FullPath));   // 이름은 암호 없이 보인다
        Assert.IsType<PasswordRequiredException>(await Svc.Catch(() => svc2.ExtractAsync(visible.FullPath, tmp.P("none"))));
        var wrong2 = Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc2.ExtractAsync(visible.FullPath, tmp.P("wrong"), "틀린암호")));
        Assert.Equal(WrongPasswordException.DefaultMessage + WrongPasswordException.HeaderNote, wrong2.Message);
    }

    /// <summary>
    /// 계약(W1-B 2차 검증 지적): 7z.dll 을 못 불러온 PC 에서 RAR 를 SharpCompress 로 대신 열 때도 암호·손상을 일집 예외(쉬운 한국어)로 알린다 —
    /// 예전엔 창 없이 풀기(재사용장치가 쓰는 길)에서 '알 수 없는 오류가 났어요: Encrypted Rar archive has no password specified.'·
    /// 'The password did not match.'·'Could not read the requested amount of bytes…' 영어 원문이 나갔다.
    /// 여러 조각 RAR 의 둘째 조각을 열면 같은 파일이 두 번 나와 'part2_file (1).txt' 가 생기던 것도 고침. 뒤 조각이 없으면 그 이름으로 알린다.
    /// </summary>
    [Fact]
    public async Task 막힌_PC에서도_RAR_암호와_손상을_쉬운_말로_알린다()
    {
        using var _ = SevenZipLibrary.SimulateUnavailableForTests();
        using var tmp = new TempDir("no7zrar");
        var items = new[] { new RawRar.Item("readme.txt", System.Text.Encoding.ASCII.GetBytes("plain readme")), new RawRar.Item("한글.txt", System.Text.Encoding.UTF8.GetBytes("안녕")) };

        string hp = tmp.P("머리암호.rar");
        File.WriteAllBytes(hp, RawRar.Rar5HeaderEncrypted(items, "Iljip-Test-1234"));
        var svc = Svc.For(hp);
        var none = Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ExtractAsync(hp, tmp.P("none"))));   // W2-02(I1-7)
        Assert.True(none.IsHeaderEncrypted);
        Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ExtractAsync(hp, tmp.P("wrong"), "틀린암호")));
        string ok = tmp.P("ok");
        await svc.ExtractAsync(hp, ok, "Iljip-Test-1234");
        Assert.Equal(new[] { "readme.txt", "한글.txt" }, TreeAssert.Files(ok));

        var plain = RawRar.Rar5(items).Bytes;
        string cut = tmp.P("잘림.rar");
        File.WriteAllBytes(cut, plain[..60]);
        var corrupt = Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(cut).ExtractAsync(cut, tmp.P("cut"))));
        Assert.DoesNotContain("알 수 없는 오류", ErrorMessages.ToUserMessage(corrupt));
        Assert.False(Internals.IsPasswordRelated(corrupt));

        string dir = tmp.Dir("vols");
        File.WriteAllBytes(Path.Combine(dir, "v.part1.rar"), RawRar.Rar5(new[] { new RawRar.Item("part1_file.txt", new byte[] { 1 }) }, volume: true, moreVolumes: true).Bytes);
        File.WriteAllBytes(Path.Combine(dir, "v.part2.rar"), RawRar.Rar5(new[] { new RawRar.Item("part2_file.txt", new byte[] { 2 }) }, volume: true, volumeNumber: 1).Bytes);
        string second = Path.Combine(dir, "v.part2.rar");
        Assert.Equal(new[] { "part2_file.txt" }, (await Svc.For(second).ListEntriesAsync(second)).Select(e => e.Path));
        await Svc.For(second).ExtractAsync(second, tmp.P("p2"));
        Assert.Equal(new[] { "part2_file.txt" }, TreeAssert.Files(tmp.P("p2")));
        string first = Path.Combine(dir, "v.part1.rar");
        await Svc.For(first).ExtractAsync(first, tmp.P("p1"));
        Assert.Equal(new[] { "part1_file.txt", "part2_file.txt" }, TreeAssert.Files(tmp.P("p1")));

        File.Delete(second);
        var missing = Assert.IsType<VolumeMissingException>(await Svc.Catch(() => Svc.For(first).ExtractAsync(first, tmp.P("miss"))));
        Assert.Equal("v.part2.rar", missing.MissingVolumeName);
    }

    /// <summary>현재 동작 기록: 쓰는 7z.dll 의 판(파일 버전 26.03)과 SHA-256 — 패키지를 올리면 새 값으로 고치고 THIRD-PARTY-NOTICES 도 고칠 것.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public void 기록_7z_dll_판과_해시()
    {
        string dll = SevenZipLibrary.LibraryPath;
        Assert.Equal("26.03", FileVersionInfo.GetVersionInfo(dll).FileVersion);
        Assert.Equal("26.03", SevenZipLibrary.DllVersion);
        using var s = File.OpenRead(dll);
        Assert.Equal("65E4C1F855F9EF6E8F0F5DF8E3F27D9EB5F07311408639DA0A1CA0B8F4871B0D", Convert.ToHexString(SHA256.HashData(s)));
    }

    /// <summary>
    /// 계약(I2-15): 공개 소프트웨어 안내 파일과 라이선스 전문이 실행 파일 옆에 복사되고, '일집 정보' 창이 읽는 글에
    /// 7-Zip·LGPL·unRAR 제한·SharpSevenZip 과 각 라이선스 전문이 들어 있다.
    /// </summary>
    [Fact]
    public void 공개소프트웨어_안내와_라이선스_전문이_있다()
    {
        Assert.True(File.Exists(ThirdPartyNotices.NoticesPath), "THIRD-PARTY-NOTICES.txt 가 출력에 없음");
        string notices = File.ReadAllText(ThirdPartyNotices.NoticesPath);
        foreach (var must in new[] { "7-Zip", "26.03", "Igor Pavlov", "LGPL", "unRAR", "SharpSevenZip", "SharpCompress", "SharpZipLib", "EggDotNet", "x64\\7z.dll" })
            Assert.Contains(must, notices);

        var licenses = Directory.EnumerateFiles(ThirdPartyNotices.LicensesFolder, "*.txt").Select(Path.GetFileName).ToList();
        foreach (var name in new[]
                 {
                     "SharpSevenZip-LGPL-3.0.txt", "SharpCompress-MIT.txt", "SharpZipLib-MIT.txt", "EggDotNet-MIT.txt",
                     "CommunityToolkit.Mvvm-MIT.txt", "System.Text.Encoding.CodePages-MIT.txt",
                     "GPL-3.0.txt", "7-Zip-LGPL-2.1.txt",   // W1-B 검증 지적: LGPL-3 이 끌어 쓰는 GPL-3 전문, 7z.dll 의 LGPL-2.1 전문
                     "7-Zip-BSD-3-Clause.txt", "7-Zip-BSD-2-Clause.txt",   // W1-B 2차 지적: 7z.dll 안 BSD 코드의 저작권 고지·조건 전문
                     "7-Zip-License.txt",   // IJ3-03: 7-zip.org 의 License.txt 원문 그대로(ThirdPartyLicenseTests)
                 })
            Assert.Contains(name, licenses);
        // BSD 는 바이너리로 배포할 때 저작권 고지와 조건 전문을 함께 실어야 한다(LZFSE·ZSTD = BSD-3, XXH64 = BSD-2 — 7-Zip License.txt 기준)
        string bsd3 = File.ReadAllText(Path.Combine(ThirdPartyNotices.LicensesFolder, "7-Zip-BSD-3-Clause.txt"));
        foreach (var must in new[] { "Apple Inc", "Facebook", "Igor Pavlov", "LZFSE", "ZSTD", "Neither the name of the copyright holder",
                                     "Redistributions in binary form must reproduce the above copyright" })
            Assert.Contains(must, bsd3);
        string bsd2 = File.ReadAllText(Path.Combine(ThirdPartyNotices.LicensesFolder, "7-Zip-BSD-2-Clause.txt"));
        foreach (var must in new[] { "Yann Collet", "XXH64", "Redistributions in binary form must reproduce the above copyright" })
            Assert.Contains(must, bsd2);
        Assert.DoesNotContain("Neither the name", bsd2);
        Assert.DoesNotContain("BSD 2-clause license (Zstandard", notices);   // 예전 요약의 잘못(ZSTD 는 BSD-3)
        // 안내 표에 적힌 전문 파일이 모두 실제로 있다
        foreach (var line in notices.Split('\n').Where(l => l.Contains("licenses\\", StringComparison.Ordinal)))
        {
            string rel = line[line.IndexOf("licenses\\", StringComparison.Ordinal)..].Trim();
            if (rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, rel)), "안내에 적힌 전문이 없음: " + rel);
        }

        string all = ThirdPartyNotices.LoadAll();
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", all);
        Assert.Contains("Permission is hereby granted", all);
        Assert.Contains("Andrew Kolman", all);
        Assert.Contains("Version 3, 29 June 2007", all);          // GPL-3 / LGPL-3
        Assert.Contains("Version 2.1, February 1999", all);       // LGPL-2.1
        // '일집 정보' 창에 개발 메모(보류·HANDOFF 같은 내부 말)가 보이지 않는다
        Assert.DoesNotContain("[보류]", all);
        Assert.DoesNotContain("HANDOFF", all);
    }
}
