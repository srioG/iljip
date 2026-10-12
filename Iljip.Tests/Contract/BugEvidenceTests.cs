using System.Security.Cryptography;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 버그 근거 — '지금 버그가 있다'는 사실을 그대로 고정하는 묶음(재현 근거).
/// ★ 버그가 고쳐지면 여기 테스트가 실패하는 게 정상이다 → 해당 테스트를 <see cref="FixedBugTests"/>로 옮겨 올바른 기대로 뒤집고,
/// 같은 번호의 Skip 테스트(Known.Bn)에서 Skip 을 지울 것. 사유 문구는 TestSupport/Known.cs.
/// 지금은 안 고친 버그가 없다(B1·B2·B6~B10 은 W1-A, B3·B4·B5 는 W1-C 에서 고쳐 FixedBugTests·각 계약 테스트로 옮김).
/// 남은 것은 B3 의 '원인 사실 기록' 두 개 — 반디집 한글 암호가 CP949 바이트이고 일집 AES 는 UTF-8 이라는 것(고친 뒤에도 사실이라 그대로 통과).
/// 전부 '현재 동작 기록'(Trait Kind=Record) — 계약만 볼 때(--filter "Kind!=Record")는 빠진다.
/// </summary>
[Trait(Kinds.Key, Kinds.Record)]
public class BugEvidenceTests
{
    /// <summary>
    /// [B3 원인 — ZipCrypto] 같은 파일을 SharpCompress 로 직접, 암호를 'CP949 바이트 그대로'(Latin-1 문자열 + Latin-1 암호 인코딩)
    /// 넘기면 풀린다 → 반디집 암호 바이트는 CP949. 일집 ZIP 해제가 CP949 로 판정되면 이 방법을 쓴다(ZipArchiveService).
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
        using var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(fx.FullPath, opts);
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
    ///  - 일집이 만든 AES ZIP 은 반대로 UTF-8 과 맞고 CP949 와는 안 맞는다 → 일집이 한글 암호로 만든 AES ZIP 은 반디집(한국어 Windows)이 못 엶.
    ///    W1-C 결정: SharpZipLib AES 는 암호를 UTF-8 로만 써서 바꿀 수 없으므로 그대로 두고, 화면이 경고(ZipCrypto 로 만들면 CP949 로 써서 호환).
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

    // [B3] '맞는 암호로도 못 푼다' 근거는 지웠다 — BandizipFixtureTests.반디집_한글암호_ZIP이_풀린다(Skip 해제)와
    //      FixedBugTests.반디집_한글암호_ZIP에_틀린_한글암호는_WrongPassword_그리고_파일없음 이 같은 픽스처로 지킨다.
    // [B4]·[B5] 근거는 FixedBugTests 로 옮겨 기대를 뒤집었다.
}

/// <summary>
/// WinZip AES 암호 확인값 검사기: 첫 AES 항목의 salt 와 2바이트 확인값(PV)을 읽고
/// PBKDF2-HMAC-SHA1(암호 바이트, salt, 1000회)로 만든 값의 마지막 2바이트와 비교한다(AE-1/AE-2 규격).
/// 본체 ZipPasswordProbe 와 같은 규격을 따로 구현한 '독립 검증용'이다(본체 코드를 쓰지 않음).
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
