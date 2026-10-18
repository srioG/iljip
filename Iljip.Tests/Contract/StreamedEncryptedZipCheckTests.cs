using Iljip.Services;
using Iljip.Tests.TestSupport;
using static Iljip.Tests.Contract.StreamedEncryptedZipTests;

namespace Iljip.Tests.Contract;

/// <summary>
/// [흘려 쓴 암호 ZIP — 압축 검사] 압축 검사(<see cref="ArchiveTestService"/> — ☰·우클릭 '시험하기'·<c>--test</c>)도 풀기와 같은 길로 연다
/// (<see cref="StreamedZipHeaders"/> — 짝: <see cref="StreamedEncryptedZipTests"/>). 예전: 온전한 원본인데 열지 못해 '문제가 있어요'(손상으로 분류 — ZIP 고치기를 권함).
/// </summary>
public class StreamedEncryptedZipCheckTests
{
    /// <summary>계약: 흘려 쓴 암호 ZIP 을 맞는 암호로 검사하면 '문제없어요. 파일 N개를 확인했어요.' — 고치기를 권하지 않는다.</summary>
    [Theory]
    [InlineData(Shape.ZipCrypto)]
    [InlineData(Shape.ZipCryptoEmptyAndFolder)]
    [InlineData(Shape.ZipCryptoKoreanCp949)]
    [InlineData(Shape.Aes256)]
    [InlineData(Shape.ZipCryptoLocalZip64Marker)]
    public async Task 압축_검사가_흘려_쓴_암호_ZIP을_맞는_암호로_문제없다고_본다(Shape shape)
    {
        using var t = new TempDir("szchk");
        var (bytes, items) = Build(shape);
        string zip = t.P("흘린 암호.zip");
        File.WriteAllBytes(zip, bytes);
        Assert.False(SharpCompress.Archives.ArchiveFactory.IsArchive(zip, out _));   // 재료 확인: 엔진은 파일 그대로는 못 알아봄

        var report = await ArchiveTestService.Instance.TestAsync(zip, PasswordOf(shape), null, CancellationToken.None);

        int files = items.Count(kv => kv.Value is not null);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal(files, report.CheckedCount);
        Assert.Equal($"문제없어요. 파일 {files}개를 확인했어요.", report.Headline);
        Assert.False(report.HasDamage);
    }

    /// <summary>계약: 틀린 암호·암호 없음은 결과('문제가 있어요')가 아니라 암호 예외 — 화면이 암호를 다시 묻는다(손상으로 보이지 않음).</summary>
    [Theory]
    [InlineData(Shape.ZipCrypto)]
    [InlineData(Shape.Aes256)]
    public async Task 압축_검사가_흘려_쓴_암호_ZIP의_틀린_암호를_암호_문제로_알린다(Shape shape)
    {
        using var t = new TempDir("szchkpw");
        string zip = t.P("흘린 암호.zip");
        File.WriteAllBytes(zip, Build(shape).Zip);

        var wrong = await Svc.Catch(() => ArchiveTestService.Instance.TestAsync(zip, "틀린-암호", null, CancellationToken.None));
        Assert.IsType<WrongPasswordException>(wrong);
        var none = await Svc.Catch(() => ArchiveTestService.Instance.TestAsync(zip, null, null, CancellationToken.None));
        Assert.IsAssignableFrom<ArchivePasswordException>(none);
    }

    /// <summary>
    /// 계약: 보정은 검사의 손상 확인을 끄지 않는다 — 둘째 파일의 암호화된 데이터가 바뀌면 그 파일만 문제('파일 3개 중 1개'), 나머지 둘은 확인됨, 고치기를 권함.
    /// </summary>
    [Fact]
    public async Task 압축_검사가_흘려_쓴_암호_ZIP의_손상된_파일만_가려낸다()
    {
        using var t = new TempDir("szchkbad");
        var (bytes, _) = Build(Shape.ZipCrypto);
        // 둘째 항목: 로컬 머리(30 + 이름 + 추가 필드) 뒤 암호 머리 12바이트를 지난 데이터 한가운데
        int second = 0;
        for (int i = 4, seen = 0; i < bytes.Length - 4; i++)
            if (BitConverter.ToUInt32(bytes, i) == 0x04034b50 && ++seen == 1) { second = i; break; }
        int dataStart = second + 30 + BitConverter.ToUInt16(bytes, second + 26) + BitConverter.ToUInt16(bytes, second + 28);
        bytes[dataStart + 12 + 20] ^= 0x5A;
        string zip = t.P("깨진 흘림.zip");
        File.WriteAllBytes(zip, bytes);

        var report = await ArchiveTestService.Instance.TestAsync(zip, PasswordOf(Shape.ZipCrypto), null, CancellationToken.None);

        Assert.False(report.Ok);
        Assert.Equal(2, report.CheckedCount);
        Assert.Equal("흘림/암호1.txt", Assert.Single(report.Problems).Entry);
        Assert.Equal("문제가 있어요. 파일 3개 중 1개에 문제가 있어요.", report.Headline);
        Assert.True(report.HasDamage);
    }
}
