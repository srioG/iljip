using System.IO;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// 공개 소프트웨어 안내(I2-15): 실행 파일 옆의 THIRD-PARTY-NOTICES.txt 와 licenses 폴더의 라이선스 전문.
/// '일집 정보' 창(<see cref="Iljip.Views.AboutWindow"/>)이 보여 준다.
/// </summary>
public static class ThirdPartyNotices
{
    public const string NoticesFileName = "THIRD-PARTY-NOTICES.txt";
    public const string LicensesFolderName = "licenses";

    public static string NoticesPath => Path.Combine(AppContext.BaseDirectory, NoticesFileName);
    public static string LicensesFolder => Path.Combine(AppContext.BaseDirectory, LicensesFolderName);

    /// <summary>창에 한 번에 보여 줄 전체 글: 안내 + 라이선스 전문(파일 이름 순). 파일이 없으면 그 사실을 적는다.</summary>
    public static string LoadAll()
    {
        var sb = new StringBuilder();
        try
        {
            sb.AppendLine(File.Exists(NoticesPath)
                ? File.ReadAllText(NoticesPath)
                : $"{NoticesFileName} 파일이 없어요. 일집을 다시 설치해 주세요.");
            if (Directory.Exists(LicensesFolder))
            {
                foreach (var f in Directory.EnumerateFiles(LicensesFolder, "*.txt").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    sb.AppendLine("========================================================================");
                    sb.AppendLine($"{LicensesFolderName}\\{Path.GetFileName(f)}");
                    sb.AppendLine("========================================================================");
                    sb.AppendLine(File.ReadAllText(f));
                }
            }
        }
        catch (Exception ex)
        {
            // W2-08: 영어 예외 문구를 그대로 보이지 않는다(원문은 로그에)
            Logger.LogError("라이선스 파일 읽기 실패", ex);
            sb.AppendLine("라이선스 파일을 읽지 못했어요: " + ErrorCatalog.Describe(ex).Message);
        }
        return sb.ToString();
    }
}
