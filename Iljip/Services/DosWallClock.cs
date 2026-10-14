using System.Runtime.InteropServices;

namespace Iljip.Services;

/// <summary>
/// 7z.dll 이 '현지 벽시계'(DOS 시각 — RAR4)를 UTC 로 바꿀 때 쓴 시간대 편차를 되돌려, 압축에 적힌 벽시계 그대로를 얻는다(Wave 2 통합 점검).
/// <para>
/// 7-Zip 의 RAR4 처리기는 DOS 시각을 Win32 <c>LocalFileTimeToFileTime</c> 으로 UTC 로 바꾼다 — 이 PC 의 '지금' 시간대 편차를 쓰고 그 날짜의 편차는 보지 않는다.
/// .NET(SharpSevenZip)은 그 UTC 를 그 날짜의 규칙으로 현지 시각으로 바꾸므로, 서머타임이 있는 시간대에서는 계절이 다른 파일이 1시간 어긋났고,
/// 7z.dll 이 보는 시간대와 .NET 이 보는 시간대가 다르면(시간대 흉내 <c>ILJIP_TEST_TZ</c>) 그 차이만큼 어긋났다. 한국(서머타임 없음)에서는 예전과 같은 값이다.
/// 같은 RAR4 를 7z.dll 을 못 쓰는 PC 에서 SharpCompress 로 열면 벽시계 그대로라, 두 길의 시각을 이것으로 맞춘다.
/// </para>
/// 되돌리기는 같은 '지금' 편차를 쓰는 <c>FileTimeToLocalFileTime</c> 이라 정확하다(7z.dll 과 같은 프로세스·같은 OS 설정).
/// </summary>
internal static class DosWallClock
{
    /// <summary>
    /// SharpSevenZip 이 준 시각(7z.dll 의 UTC FILETIME 을 <see cref="DateTime.FromFileTime"/> 한 현지 시각)에서 압축에 적힌 벽시계를 되찾는다.
    /// 결과는 그 벽시계를 현지 시각으로 본 값(<see cref="DateTimeKind.Local"/> — 화면은 그대로 찍고, 풀 때는 <see cref="System.IO.File.SetLastWriteTime(string, DateTime)"/> 에 그대로 준다).
    /// 바꿀 수 없는 값(1601년 전 등)은 그대로 돌려준다.
    /// </summary>
    public static DateTime FromSevenZip(DateTime fromEngine)
    {
        try
        {
            long utc = fromEngine.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(fromEngine, DateTimeKind.Local).ToFileTimeUtc()
                : fromEngine.ToFileTimeUtc();
            if (!FileTimeToLocalFileTime(ref utc, out long local)) return fromEngine;
            return DateTime.SpecifyKind(DateTime.FromFileTimeUtc(local), DateTimeKind.Local);
        }
        catch (ArgumentOutOfRangeException)
        {
            return fromEngine;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FileTimeToLocalFileTime(ref long lpFileTime, out long lpLocalFileTime);
}
