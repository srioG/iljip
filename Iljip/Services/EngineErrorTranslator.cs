using System.IO;

namespace Iljip.Services;

/// <summary>
/// 엔진(SharpCompress·EggDotNet) 예외를 서비스 경계에서 일집 암호 예외로 바꾼다(B5 — 화면이 문구가 아니라 타입으로 '암호 문제'를 판정).
/// 바꾸는 것은 암호 문제뿐이다: 암호 없음 → <see cref="PasswordRequiredException"/>, 틀린 암호 → <see cref="WrongPasswordException"/>.
/// 그 밖의 손상 예외는 null(엔진 예외 그대로 — 손상 문구 정리는 W1-B 의 일).
/// <para>
/// 판단 근거: 엔진 문구('No password supplied'·'did not match'·'bad password'), EggDotNet DecryptFailedException,
/// 그리고 '암호를 넣었고 그 항목이 암호 항목인데 데이터가 깨짐'(7z 틀린 암호의 Data Error, ZipCrypto 1바이트 검사를 우연히 통과한
/// 틀린 암호의 Deflate 오류 등). 마지막 경우는 손상과 구분이 안 되어 '손상된 경우에도 이렇게 보일 수 있어요'를 덧붙인다
/// (<see cref="WrongPasswordException.MayBeCorrupt"/>). ZIP 은 판정이 암호를 이미 충분히 확인했으면 해제 공통 구현이 이것을 손상 예외로 바꾼다.
/// </para>
/// </summary>
internal static class EngineErrorTranslator
{
    /// <param name="PasswordGiven">암호를 넣었는지(빈 문자열은 안 넣은 것).</param>
    /// <param name="EntryEncrypted">실패한 항목이 암호 항목인지(열기 단계면 false).</param>
    /// <param name="EntryPath">실패한 항목 경로(모르면 null).</param>
    /// <param name="HeaderEncrypted">파일 이름까지 암호가 걸린 압축인지(7z 헤더 암호).</param>
    public readonly record struct Context(bool PasswordGiven, bool EntryEncrypted, string? EntryPath, bool HeaderEncrypted = false);

    /// <summary>암호 문제면 일집 예외, 아니면 null(원래 예외를 그대로 던질 것).</summary>
    public static ArchivePasswordException? Translate(Exception ex, Context ctx)
    {
        if (ex is ArchiveException or OperationCanceledException) return null;

        string type = ex.GetType().FullName ?? string.Empty;
        string msg = ex.Message ?? string.Empty;

        // EggDotNet: 암호가 비었거나 틀리면 DecryptFailedException('Decryption of entry failed') 하나로 알린다.
        if (type == "EggDotNet.Exceptions.DecryptFailedException")
            return ctx.PasswordGiven
                ? new WrongPasswordException(ctx.HeaderEncrypted, ex, ctx.EntryPath)
                : new PasswordRequiredException(ctx.HeaderEncrypted, ex, ctx.EntryPath);

        // SharpCompress: 'No password supplied for encrypted zip.' / 'Encrypted 7Zip archive has no password specified.' / 'The password did not match.'
        if (ex is SharpCompress.Common.CryptographicException)
        {
            if (!ctx.PasswordGiven || Has(msg, "no password"))
                return new PasswordRequiredException(ctx.HeaderEncrypted, ex, ctx.EntryPath);
            return new WrongPasswordException(ctx.HeaderEncrypted, ex, ctx.EntryPath);
        }

        // WinZip AES 확인값 불일치(InvalidFormatException 'bad password')
        if (Has(msg, "bad password") || Has(msg, "did not match"))
            return ctx.PasswordGiven
                ? new WrongPasswordException(ctx.HeaderEncrypted, ex, ctx.EntryPath)
                : new PasswordRequiredException(ctx.HeaderEncrypted, ex, ctx.EntryPath);

        // 암호 항목(또는 헤더 암호)에서 암호를 넣었는데 데이터가 깨짐 → 틀린 암호(또는 손상)
        if (ctx.PasswordGiven && (ctx.EntryEncrypted || ctx.HeaderEncrypted) && IsDataError(ex))
            return new WrongPasswordException(ctx.HeaderEncrypted, ex, ctx.EntryPath, mayBeCorrupt: true);

        return null;
    }

    /// <summary>
    /// 압축 데이터가 깨졌을 때 엔진이 내는 예외인가. 틀린 암호로 푼 쓰레기 데이터는 해제기마다 다른 예외로 끝난다
    /// (SharpCompress 의 모든 예외는 SharpCompressException 아래, .NET Deflate 는 InvalidDataException, 범위 밖 읽기 등).
    /// 경로 공격 거부('안전하지 않은 경로')는 데이터 오류가 아니다.
    /// </summary>
    internal static bool IsDataError(Exception ex) => ex switch
    {
        SharpCompress.Common.SharpCompressException => true,
        InvalidDataException d => !d.Message.StartsWith("안전하지 않은 경로", StringComparison.Ordinal),
        EndOfStreamException or IndexOutOfRangeException or ArgumentOutOfRangeException => true,
        _ => ex.GetType().Namespace?.StartsWith("EggDotNet", StringComparison.Ordinal) == true,
    };

    private static bool Has(string s, string part) => s.Contains(part, StringComparison.OrdinalIgnoreCase);
}
