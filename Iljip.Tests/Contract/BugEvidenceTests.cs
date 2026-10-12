using System.Security.Cryptography;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 버그 근거(B1~B7) — 이 묶음은 '지금 버그가 있다'는 사실을 그대로 고정한다(재현 근거).
/// ★ 버그가 고쳐지면 여기 테스트가 실패하는 게 정상이다 → 해당 테스트를 지우고, 같은 번호의 Skip 테스트(Known.Bn)에서 Skip 을 지울 것.
/// 본체 코드는 동결이라 고치지 않았다. 사유 문구는 TestSupport/Known.cs.
/// 전부 '현재 동작 기록'(Trait Kind=Record) — 계약만 볼 때(--filter "Kind!=Record")는 빠진다.
/// </summary>
[Trait(Kinds.Key, Kinds.Record)]
public class BugEvidenceTests
{
    /// <summary>[B1] 반디집 7z 전체 해제: 빈 파일에서 InvalidOperationException. 빈 파일 앞의 내용은 이미 맞게 풀려 있고 빈 파일만 없다.</summary>
    [Fact]
    public async Task B1_반디집_7z는_빈파일에서_해제가_실패한다()
    {
        var fx = FixtureCatalog.Get("7z_default");
        using var tmp = new TempDir("b1");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal("File does not have a stream.", ex!.Message);
        var snap = TreeAssert.Snapshot(dest);
        foreach (var e in fx.ExpectedFiles.Where(e => e.Size > 0))
            Assert.Equal(e.Sha256, snap[e.Path]);
        Assert.False(snap.ContainsKey("일집_샘플/빈파일.txt"));
    }

    /// <summary>[B2] FNAME 없는 .gz — 반디집 산출물과 일집 자신의 산출물 모두 목록 이름 '' + 해제 '안전하지 않은 경로'.</summary>
    [Theory]
    [InlineData("bandizip")]
    [InlineData("iljip")]
    public async Task B2_FNAME_없는_gz는_이름이_비고_해제가_실패한다(string maker)
    {
        using var tmp = new TempDir("b2");
        string arc;
        if (maker == "bandizip")
        {
            arc = FixtureCatalog.Get("gz_single").FullPath;
        }
        else
        {
            string one = tmp.P("원본.txt");
            File.WriteAllText(one, "일집이 만든 gz");
            arc = tmp.P("원본.txt.gz");
            await new GZipArchiveService().CompressAsync(new[] { one }, arc);
        }
        var svc = Svc.For(arc);
        var only = Assert.Single(await svc.ListEntriesAsync(arc));
        Assert.Equal(string.Empty, only.Path);

        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractAsync(arc, dest));
        Assert.IsType<InvalidDataException>(ex);
        Assert.Equal("안전하지 않은 경로: ", ex!.Message);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>[B3] 반디집 한글 암호 ZIP: 맞는 암호를 줘도 지금은 암호 불일치 예외.</summary>
    [Theory]
    [InlineData("zip_zipcrypto_korean_pw", "SharpCompress.Common.CryptographicException", "The password did not match.")]
    [InlineData("zip_aes256_korean_pw", "SharpCompress.Common.InvalidFormatException", "bad password")]
    public async Task B3_반디집_한글암호_ZIP은_맞는_암호로도_못_푼다(string id, string exType, string msg)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("b3");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, FixtureCatalog.KoreanPassword));
        Assert.Equal(exType, ex?.GetType().FullName);
        Assert.Equal(msg, ex!.Message);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// [B3 원인 — ZipCrypto] 같은 파일을 SharpCompress 로 직접, 암호를 'CP949 바이트 그대로'(Latin-1 문자열 + Latin-1 암호 인코딩)
    /// 넘기면 풀린다 → 반디집 암호 바이트는 CP949, 일집이 못 푸는 원인은 암호 인코딩(UTF-8).
    /// (SharpCompress 를 엔진에서 빼면 이 테스트는 컴파일되지 않으니 같이 지울 것)
    /// </summary>
    [Fact]
    public void B3_원인_ZipCrypto는_암호를_CP949_바이트로_주면_풀린다()
    {
        var fx = FixtureCatalog.Get("zip_zipcrypto_korean_pw");
        byte[] cp949 = Encoding.GetEncoding(949).GetBytes(FixtureCatalog.KoreanPassword);
        var opts = new SharpCompress.Readers.ReaderOptions
        {
            Password = Encoding.Latin1.GetString(cp949),
            ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding { Password = Encoding.Latin1 }
        };
        using var archive = SharpCompress.Archives.ArchiveFactory.Open(fx.FullPath, opts);
        var expected = fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256);
        int n = 0;
        foreach (var e in archive.Entries.Where(e => !e.IsDirectory))
        {
            using var s = e.OpenEntryStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(expected[e.Key!], TreeAssert.Sha256(ms.ToArray()));
            n++;
        }
        Assert.Equal(expected.Count, n);
    }

    /// <summary>
    /// [B3 원인 — AES, 양방향] WinZip AES 의 암호 확인값(PBKDF2 결과 끝 2바이트)으로 암호 바이트를 판정:
    ///  - 반디집 AES ZIP 은 '테스트암호'의 CP949 바이트와 맞고 UTF-8 과는 안 맞는다.
    ///  - 일집이 만든 AES ZIP 은 반대로 UTF-8 과 맞고 CP949 와는 안 맞는다 → 일집이 한글 암호로 만든 ZIP 은 반디집(한국어 Windows)이 못 엶.
    /// </summary>
    [Fact]
    public async Task B3_원인_AES_한글암호는_반디집_CP949_일집_UTF8()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(FixtureCatalog.KoreanPassword);
        byte[] cp949 = Encoding.GetEncoding(949).GetBytes(FixtureCatalog.KoreanPassword);

        string bandizip = FixtureCatalog.Get("zip_aes256_korean_pw").FullPath;
        Assert.True(AesPasswordVerifier.Matches(bandizip, cp949), "반디집 AES: CP949 와 안 맞음");
        Assert.False(AesPasswordVerifier.Matches(bandizip, utf8), "반디집 AES: UTF-8 과 맞음(예상 밖)");

        using var tmp = new TempDir("b3a");
        string src = SampleTree.Create(tmp.Dir("src"));
        string iljip = tmp.P("iljip_korean_pw.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, iljip, new CompressionOptions { Password = FixtureCatalog.KoreanPassword });
        Assert.True(AesPasswordVerifier.Matches(iljip, utf8), "일집 AES: UTF-8 과 안 맞음");
        Assert.False(AesPasswordVerifier.Matches(iljip, cp949), "일집 AES: CP949 와 맞음(예상 밖)");
    }

    /// <summary>[B4] CRC 가 틀린 ZIP 을 지금은 '성공'으로 풀고, readme.txt 가 틀린 내용으로 남는다.</summary>
    [Fact]
    public async Task B4_CRC가_틀린_ZIP을_조용히_풀어낸다()
    {
        using var tmp = new TempDir("b4");
        string arc = CorruptArchiveTests.MakeCrcBroken(tmp);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);   // 예외 없음
        var fx = FixtureCatalog.Get("zip_store_l0");
        string expectedSha = fx.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256!;
        Assert.NotEqual(expectedSha, TreeAssert.Sha256(Path.Combine(dest, "일집_샘플", "readme.txt")));
    }

    /// <summary>
    /// [B5] 틀린 암호인데 화면이 암호 문제로 못 알아보는 예외: 7z → DataErrorException('Data Error'),
    /// ZipCrypto 1바이트 검사를 통과한 틀린 암호(<see cref="ZipCryptoLucky"/> 가 픽스처에서 계산) → ZlibException('Bad state …').
    /// </summary>
    [Theory]
    [InlineData("python/7z_pw_names_visible.7z", "wrong", "SharpCompress.Compressors.LZMA.DataErrorException")]
    [InlineData("bandizip/7z_header_encrypted.7z", "wrong", "SharpCompress.Compressors.LZMA.DataErrorException")]
    [InlineData("bandizip/zip_zipcrypto.zip", ZipCryptoLuckyMarker, "SharpCompress.Compressors.Deflate.ZlibException")]
    public async Task B5_틀린_암호를_화면이_암호문제로_못_알아본다(string rel, string password, string exType)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("b5");
        Exception? ex;
        if (password == ZipCryptoLuckyMarker)
        {
            var hit = await ZipCryptoLucky.FindAsync(arc, FixtureCatalog.DefaultPassword, tmp, (e, _) => e?.GetType().FullName == exType);
            Assert.True(hit is not null, $"1바이트 검사를 통과하는 틀린 암호 200개 중 {exType} 로 끝나는 것이 없음");
            ex = hit!.Value.Ex;
        }
        else
        {
            ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        }
        Assert.Equal(exType, ex?.GetType().FullName);
        Assert.False(Internals.IsPasswordRelated(ex!));
    }

    /// <summary>InlineData 자리표시: 'ZipCrypto 1바이트 검사를 우연히 통과하는 틀린 암호'를 픽스처에서 계산해 쓴다.</summary>
    internal const string ZipCryptoLuckyMarker = "<1바이트 검사 통과 틀린 암호>";

    /// <summary>
    /// [B4 결과] 1바이트 검사를 우연히 통과한 틀린 ZipCrypto 암호(약 1/256)는 첫 파일의 쓰레기 Deflate 를 오류 없이 '풀어' 써 버리고,
    /// 다음 파일에서야 '암호 불일치'(CryptographicException)로 멈춘다 — 화면은 암호를 다시 묻지만 대상 폴더에는
    /// 틀린 내용의 파일이 원래 이름으로 남는다(CRC 검사가 없어서, B4). 맞는 암호로 다시 풀면 진짜 파일은 '이름 (1)' 로 비켜 써진다.
    /// </summary>
    [Fact]
    public async Task B4_검사통과_틀린_ZipCrypto_암호는_틀린_내용_파일을_남긴다()
    {
        var fx = FixtureCatalog.Get("zip_zipcrypto");
        var want = fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!);
        using var tmp = new TempDir("b4z");
        var hit = await ZipCryptoLucky.FindAsync(fx.FullPath, FixtureCatalog.DefaultPassword, tmp, (e, dest) =>
            e is not null && Internals.IsPasswordRelated(e)
            && TreeAssert.Files(dest).Any(f => !want.TryGetValue(f, out var sha) || sha != TreeAssert.Sha256(Path.Combine(dest, f))));
        Assert.True(hit is not null, "검사를 통과한 틀린 암호 200개 중 '암호 예외 + 틀린 내용 파일 남음' 인 것이 없음(고쳐졌으면 정상)");
    }

    /// <summary>[B6] TAR 압축은 마지막 큰 파일을 쓰기 시작한 뒤 누른 취소를 무시하고 정상 완료한다.</summary>
    [Fact]
    public async Task B6_TAR_압축은_마지막_파일_쓰는_중_취소가_무시된다()
    {
        using var tmp = new TempDir("b6");
        string src = CancellationTests.MakeSource(tmp);
        string arc = tmp.P("big.tar");
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal)) cts.Cancel(); });
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc, null, progress, cts.Token));
        Assert.Null(ex);
        Assert.True(cts.IsCancellationRequested);
        Assert.True(new FileInfo(arc).Length > CancellationTests.BigSize);
    }

    /// <summary>[B7] 간격을 0 으로 줘도 중간 진행률이 하나도 통과하지 않는다(long.MinValue 뺄셈 오버플로).</summary>
    [Fact]
    public void B7_솎아내기_진행률은_중간보고를_전부_버린다()
    {
        var seen = ProgressTests.RunThrottled(intervalMs: 0, intermediateCount: 50);
        Assert.Single(seen);
        Assert.Equal(string.Empty, seen[0].CurrentFile);
    }

    /// <summary>
    /// [B9] 크기 0 항목이 든 EGG: (가) 폴더 항목만 있어도 목록이 NullReferenceException(EggArchiveEntry.CompressedLength) —
    /// 화면에서 '파일을 열 수 없어요'. (나) 빈 파일이 있으면 해제도 그 파일에서 NullReferenceException(Open()) — 앞 파일만 풀림.
    /// (EGG 는 RawEgg 로 만든 것: 크기 0 항목에 블록 머리 없음 — 진짜 알집 산출물 모양은 미확인)
    /// </summary>
    [Fact]
    public async Task B9_EGG_크기0_항목이_있으면_목록과_해제가_실패한다()
    {
        using var tmp = new TempDir("b9");
        var (items, _, _) = EggAlzTests.EggSample(tmp);
        string dirsOnly = tmp.P("dirs.egg");
        RawEgg.Write(dirsOnly, new[] { new RawEgg.Item(SampleTree.RootName + "/", null) }.Concat(items));
        var svc = Svc.For(dirsOnly);
        Assert.IsType<NullReferenceException>(await Svc.Catch(() => svc.ListEntriesAsync(dirsOnly)));

        string withEmpty = tmp.P("empty.egg");
        RawEgg.Write(withEmpty, items.Take(1).Append(new RawEgg.Item(SampleTree.RootName + "/빈파일.txt", Array.Empty<byte>())).Concat(items.Skip(1)));
        Assert.IsType<NullReferenceException>(await Svc.Catch(() => svc.ListEntriesAsync(withEmpty)));
        string dest = tmp.P("out");
        Assert.IsType<NullReferenceException>(await Svc.Catch(() => svc.ExtractAsync(withEmpty, dest)));
        Assert.Equal(new[] { items[0].Name }, TreeAssert.Files(dest));
    }

    /// <summary>[B10] CP949 한글 이름 ALZ: 목록 이름의 한글이 U+FFFD(�)로 깨지고, 풀린 파일 이름도 깨진 그대로.</summary>
    [Fact]
    public async Task B10_ALZ_CP949_한글이름이_깨진다()
    {
        using var tmp = new TempDir("b10");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        var names = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(2, names.Count);
        Assert.All(names, n => Assert.Contains('�', n));
        Assert.DoesNotContain(names, n => n.Contains("문서", StringComparison.Ordinal));
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        var files = TreeAssert.Files(dest);
        Assert.Single(files);
        Assert.Contains('�', files[0]);
    }
}

/// <summary>
/// WinZip AES 암호 확인값 검사기: 첫 AES 항목의 salt 와 2바이트 확인값(PV)을 읽고
/// PBKDF2-HMAC-SHA1(암호 바이트, salt, 1000회)로 만든 값의 마지막 2바이트와 비교한다(AE-1/AE-2 규격).
/// </summary>
public static class AesPasswordVerifier
{
    public static bool Matches(string zipPath, byte[] passwordBytes)
    {
        var b = File.ReadAllBytes(zipPath);
        int eocd = -1;
        for (int i = b.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(b, i) == 0x06054b50) { eocd = i; break; }
        int count = BitConverter.ToUInt16(b, eocd + 10);
        int p = (int)BitConverter.ToUInt32(b, eocd + 16);
        for (int n = 0; n < count; n++)
        {
            int flags = BitConverter.ToUInt16(b, p + 8), method = BitConverter.ToUInt16(b, p + 10);
            int nameLen = BitConverter.ToUInt16(b, p + 28), extraLen = BitConverter.ToUInt16(b, p + 30), commentLen = BitConverter.ToUInt16(b, p + 32);
            int localOffset = (int)BitConverter.ToUInt32(b, p + 42);
            int strength = 3;
            for (int x = p + 46 + nameLen; x < p + 46 + nameLen + extraLen;)
            {
                int id = BitConverter.ToUInt16(b, x), len = BitConverter.ToUInt16(b, x + 2);
                if (id == 0x9901) strength = b[x + 4 + 4];
                x += 4 + len;
            }
            p += 46 + nameLen + extraLen + commentLen;
            if ((flags & 1) == 0 || method != 99) continue;

            int keyLen = strength switch { 1 => 16, 2 => 24, _ => 32 };
            int saltLen = keyLen / 2;
            int lNameLen = BitConverter.ToUInt16(b, localOffset + 26), lExtraLen = BitConverter.ToUInt16(b, localOffset + 28);
            int data = localOffset + 30 + lNameLen + lExtraLen;
            byte[] salt = b[data..(data + saltLen)];
            byte[] pv = b[(data + saltLen)..(data + saltLen + 2)];
            byte[] derived = Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, 1000, HashAlgorithmName.SHA1, keyLen * 2 + 2);
            return derived[^2] == pv[0] && derived[^1] == pv[1];
        }
        throw new InvalidDataException("AES 항목이 없음: " + zipPath);
    }
}
