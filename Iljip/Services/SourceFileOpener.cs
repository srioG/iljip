using System.IO;

namespace Iljip.Services;

/// <summary>공유 위반(다른 프로그램이 잠금)일 때 다시 해 보는 규칙. 테스트는 지연 0 을 넣는다.</summary>
public sealed record SourceOpenRetry(int Retries, TimeSpan Delay)
{
    /// <summary>150ms 간격으로 3번 더 — 회사 PC 의 DLP(PCFILTER)가 파일을 아주 잠깐 잡는 경우 대비(잠긴 파일마다 최대 약 0.5초 늦어짐).</summary>
    public static readonly SourceOpenRetry Default = new(3, TimeSpan.FromMilliseconds(150));
    public static readonly SourceOpenRetry None = new(0, TimeSpan.Zero);
}

/// <summary>
/// 압축할 원본 파일 열기(I0-2). 다른 프로그램이 쓰는 중(아웃룩 PST·로그 등)이어도 상대가 공유를 허락했으면 읽을 수 있게
/// FileShare.ReadWrite|Delete 로 연다 — 예전 File.OpenRead(FileShare.Read)는 쓰기로 열린 파일이면 늘 실패해 압축 전체가 깨졌다(실측).
/// 상대가 공유를 아예 막았으면(FileShare.None) 어느 방식으로도 못 연다 → 호출자가 그 파일만 건너뛰고(ZIP·TAR) 끝에 알린다.
/// 열기는 되는데 다른 프로그램이 파일의 한 부분을 잠가(LockFileEx — DB 파일 등) 읽기에서 막히는 경우도 있다(W1-D 1차 검증):
/// 열 때 첫 블록을 읽어 봐서 앞부분 잠금은 여기서 걸러 내고, 그 뒤쪽 잠금은 <see cref="Read"/> 가 <see cref="SourceUnreadableException"/> 으로 알린다.
/// ★ 쓰는 중인 파일을 읽으면 그 순간의 (일관되지 않을 수 있는) 내용이 담긴다 — 반디집도 같다.
/// </summary>
public static class SourceFileOpener
{
    public const string ReasonInUse = "다른 프로그램이 쓰고 있어요";
    public const string ReasonDenied = "읽을 권한이 없어요";
    public const string ReasonMissing = "파일이 없어졌어요";
    public const string ReasonOther = "읽지 못했어요";

    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);

    /// <summary>
    /// 읽기 전용으로 연다. 성공하면 true + <paramref name="stream"/>(호출자가 닫음, 위치는 처음), 실패하면 false + 쉬운 한국어 이유.
    /// 첫 블록(80KB)까지 읽혀야 성공이다 — 열기만 되고 읽기가 막힌 파일(앞부분 잠금)을 항목 머리를 쓰기 전에 거르려고.
    /// 공유 위반·잠금 위반이면 <paramref name="retry"/>(기본 <see cref="SourceOpenRetry.Default"/>)대로 다시 해 본다. 예외는 내지 않는다.
    /// </summary>
    public static bool TryOpenRead(string path, out Stream? stream, out string? reason, SourceOpenRetry? retry = null)
    {
        retry ??= SourceOpenRetry.Default;
        for (int attempt = 0; ; attempt++)
        {
            FileStream? fs = null;
            try
            {
                fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
                // 첫 블록 읽어 보기: 버퍼(80KB)만큼 읽힌다. 다른 프로그램이 그 범위를 잠갔으면 여기서 잠금 위반 → 이 파일만 건너뜀
                if (fs.Length > 0)
                {
                    fs.ReadByte();
                    fs.Position = 0;
                }
                stream = fs;
                reason = null;
                return true;
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                fs?.Dispose();
                if (attempt >= retry.Retries)
                {
                    stream = null;
                    reason = ReasonInUse;
                    return false;
                }
                if (retry.Delay > TimeSpan.Zero) Thread.Sleep(retry.Delay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                          or ArgumentException or NotSupportedException)
            {
                fs?.Dispose();
                stream = null;
                reason = ReasonFor(ex);
                return false;
            }
        }
    }

    /// <summary>
    /// 열어 둔 원본에서 버퍼만큼 읽는다. 읽는 도중 못 읽게 되면(다른 프로그램이 파일의 뒤쪽 한 부분을 잠근 DB 등) 쉬운 이유를 담은
    /// <see cref="SourceUnreadableException"/> 으로 바꿔 던진다 — ZIP·TAR 는 그 파일을 뺀 채 처음부터 다시 쓰고(건너뜀으로 안내),
    /// GZ·BZ2 는 그 안내로 실패한다. 예전엔 엔진 오류 문구('다른 프로세스가 파일의 한 부분을 잠갔으므로…')로 압축 전체가 실패했다.
    /// </summary>
    public static int Read(Stream input, byte[] buffer, string sourcePath)
    {
        try
        {
            return input.Read(buffer, 0, buffer.Length);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            throw ReadFailed(sourcePath, ex);
        }
    }

    /// <summary>원본 읽기 실패(다른 형식의 오류·취소는 아님).</summary>
    internal static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException;

    /// <summary>원본 읽기 실패 → 화면에 보일 예외.</summary>
    internal static SourceUnreadableException ReadFailed(string sourcePath, Exception ex) => new(sourcePath, ReasonFor(ex), ex);

    /// <summary>열기 실패 예외 → 화면에 보일 이유.</summary>
    public static string ReasonFor(Exception ex) => ex switch
    {
        IOException io when IsSharingViolation(io) => ReasonInUse,
        FileNotFoundException or DirectoryNotFoundException => ReasonMissing,
        UnauthorizedAccessException or System.Security.SecurityException => ReasonDenied,
        _ => ReasonOther,
    };

    private static bool IsSharingViolation(IOException ex) => ex.HResult is ErrorSharingViolation or ErrorLockViolation;
}
