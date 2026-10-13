using System.IO;

namespace Iljip.Services;

/// <summary>
/// 열면 바로 실행되는 파일 종류(W2-04 — I1-3). 압축 안에서 이런 파일을 두 번 누르면 열기 전에 한 번 묻는다
/// (메일로 온 압축 속 실행 파일이 악성인 일이 흔해서 — 반디집·알집도 경고).
/// </summary>
public static class RiskyFileTypes
{
    /// <summary>
    /// 실행 파일로 보는 확장자(점 포함, 대소문자 무시). 열면 코드가 돌거나(스크립트·도움말·관리 콘솔·엑셀 추가 기능) 다른 파일·주소를 대신 열거나
    /// (바로 가기·검색·설정 바로 가기), 디스크 이미지처럼 열면 안의 파일이 인터넷 표식 없이 드러나는 형식까지(W2-04 1차 검증 — 흔히 악용되는 것 보강).
    /// 2차 검증에서 원격 데스크톱 연결(.rdp — 2024년 메일 피싱에 실제로 쓰임)·파이썬(깔린 PC 에서는 두 번 누르면 바로 실행)·
    /// 웹 사이트 바로 가기(.website)·자바 웹 실행(.jnlp)·윈도우 업데이트 설치 파일(.msu)을 더함.
    /// </summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 프로그램·설치
        ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msu", ".msix", ".msixbundle", ".appx", ".appxbundle", ".appinstaller",
        ".application", ".appref-ms", ".jar", ".jnlp", ".gadget", ".xbap",
        // 스크립트
        ".bat", ".cmd", ".ps1", ".psm1", ".psd1", ".ps1xml", ".psc1", ".vb", ".vbs", ".vbe", ".js", ".jse", ".ws", ".wsf", ".wsh", ".wsc",
        ".sct", ".hta", ".msh", ".msh1", ".msh2", ".mshxml",
        ".py", ".pyw", ".pyz", ".pyzw", ".pyc",
        // 바로 가기·셸이 대신 여는 것(원격 데스크톱 연결 포함)
        ".lnk", ".url", ".website", ".scf", ".library-ms", ".search-ms", ".searchconnector-ms", ".settingcontent-ms", ".rdp",
        // 도움말·관리 콘솔·설정·진단·추가 기능
        ".chm", ".msc", ".inf", ".reg", ".diagcab", ".xll",
        // 디스크 이미지(열면 탑재 — 안의 파일에 인터넷 표식이 없음)
        ".iso", ".img", ".vhd", ".vhdx",
    };

    /// <summary>이름(또는 경로)의 확장자가 실행 파일인가. 끝의 점·공백은 Windows 처럼 무시한다('a.exe.' 도 실행 파일).</summary>
    public static bool IsExecutable(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string trimmed = name.TrimEnd('.', ' ');
        return Extensions.Contains(Path.GetExtension(trimmed));
    }
}
