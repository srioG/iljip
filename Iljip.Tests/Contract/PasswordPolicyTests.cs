using Iljip.Models;
using Iljip.Services;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-C, I0-3·B3] 압축 옵션 창의 암호 판정(순수 함수 — 창은 이 결과대로만 동작한다). 창 자체(암호 보이기·경고 버튼)는 MANUAL_CHECKLIST.
/// </summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("", "", PasswordEntryCheck.Empty)]
    [InlineData("a", "b", PasswordEntryCheck.Mismatch)]
    [InlineData("", "x", PasswordEntryCheck.Mismatch)]          // 확인 칸에만 넣음 — 막음
    [InlineData("1234567", "1234567", PasswordEntryCheck.Short)]
    [InlineData("12345678", "12345678", PasswordEntryCheck.Ok)]
    [InlineData("테스트암호", "테스트암호", PasswordEntryCheck.Short)]   // 5글자
    [InlineData("한글암호여덟글자", "한글암호여덟글자", PasswordEntryCheck.Ok)]
    [InlineData("😀😀😀😀😀😀😀", "😀😀😀😀😀😀😀", PasswordEntryCheck.Short)]   // 이모지는 사람이 보는 한 글자(UTF-16 으로는 14)
    public void 두_칸_판정(string p1, string p2, PasswordEntryCheck expected)
    {
        Assert.Equal(expected, PasswordPolicy.CheckEntry(p1, p2));
    }

    [Theory]
    [InlineData("테스트", ".zip", ZipEncryption.Aes256, PasswordCompat.NonAsciiAes)]
    [InlineData("테스트", ".ZIP", ZipEncryption.Aes256, PasswordCompat.NonAsciiAes)]
    [InlineData("테스트", ".zip", ZipEncryption.ZipCrypto, PasswordCompat.Cp949ZipCrypto)]
    [InlineData("😀", ".zip", ZipEncryption.ZipCrypto, PasswordCompat.NotCp949Encodable)]
    [InlineData("테스트😀", ".zip", ZipEncryption.ZipCrypto, PasswordCompat.NotCp949Encodable)]
    // AES 라도 CP949 로 못 바꾸는 글자면 '이모지' 경고 — 'ZipCrypto 로 만들기(반디집·알집과 호환)'를 권하면 거짓(ZipCrypto 도 UTF-8 로 씀, W1-C 1차 검증)
    [InlineData("😀", ".zip", ZipEncryption.Aes256, PasswordCompat.NotCp949Encodable)]
    [InlineData("암호😀", ".zip", ZipEncryption.Aes256, PasswordCompat.NotCp949Encodable)]
    [InlineData("테스트", ".7z", ZipEncryption.Aes256, PasswordCompat.Ok)]
    [InlineData("Abc-1234", ".zip", ZipEncryption.Aes256, PasswordCompat.Ok)]
    [InlineData("Abc-1234", ".zip", ZipEncryption.ZipCrypto, PasswordCompat.Ok)]
    [InlineData("", ".zip", ZipEncryption.Aes256, PasswordCompat.Ok)]
    [InlineData("테스트", ".tar", ZipEncryption.Aes256, PasswordCompat.Ok)]
    public void 호환_판정(string password, string ext, ZipEncryption encryption, PasswordCompat expected)
    {
        Assert.Equal(expected, PasswordPolicy.CheckCompat(password, ext, encryption));
    }
}
