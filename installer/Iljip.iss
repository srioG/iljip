; ============================================================
;  일집 (Iljip) 설치 스크립트 — Inno Setup
;
;  동작:
;    - .NET 8 데스크톱 런타임이 없으면 Microsoft에서 자동 다운로드 후 설치
;    - 일집 본체를 Program Files에 설치
;    - 시작 메뉴 / (선택) 바탕화면 바로가기 생성
;    - 제거 프로그램 등록
;
;  빌드 전 준비:
;    1) 일집을 framework-dependent로 publish 해 두기 (PublishDir 경로에)
;         dotnet publish Iljip\Iljip.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "D:\Python\일집-dist"
;    2) 이 스크립트를 ISCC로 컴파일
;         "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "D:\Python\일집\installer\Iljip.iss"
; ============================================================

#define AppName "일집"
#define AppVersion "0.2.0"
#define AppPublisher "Iljip"
#define AppExeName "Iljip.exe"

; 일집 publish 산출물 폴더 (위 1)번 -o 경로와 동일해야 함)
#define PublishDir "D:\Python\일집-dist"

[Setup]
; 앱 고유 식별자 (제거/업그레이드 추적용 — 바꾸지 말 것)
AppId={{9A2E7C1B-5D3F-4E8A-A1C8-D5F7B2E9A4C7}}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\일집
DefaultGroupName=일집
DisableProgramGroupPage=yes
OutputDir=D:\Python\일집-build\installer
OutputBaseFilename=일집-setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Program Files 설치 + 런타임 설치를 위해 관리자 권한으로 실행
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; GroupDescription: "추가 작업:"

[Files]
; 일집 publish 산출물 전체
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\일집"; Filename: "{app}\{#AppExeName}"
Name: "{group}\일집 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\일집"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; 런타임이 없으면 (NeedsDotNet) 다운로드해 둔 설치 프로그램을 조용히 설치
Filename: "{tmp}\dotnet-desktop-runtime.exe"; Parameters: "/install /quiet /norestart"; \
    StatusMsg: ".NET 8 데스크톱 런타임 설치 중..."; Check: NeedsDotNet; Flags: waituntilterminated
; 설치 완료 후 일집 실행 (선택)
Filename: "{app}\{#AppExeName}"; Description: "일집 실행"; Flags: nowait postinstall skipifsilent

[Code]
var
  DownloadPage: TDownloadWizardPage;

{ %ProgramFiles%\dotnet\shared\Microsoft.WindowsDesktop.App\8.* 폴더 존재로 런타임 설치 여부 판단 }
function IsDotNet8DesktopInstalled: Boolean;
var
  FindRec: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.*'), FindRec) then
  begin
    Result := True;
    FindClose(FindRec);
  end;
end;

function NeedsDotNet: Boolean;
begin
  Result := not IsDotNet8DesktopInstalled;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  { '설치 준비 완료' 페이지에서, 런타임이 없으면 다운로드 진행 }
  if (CurPageID = wpReady) and NeedsDotNet then
  begin
    DownloadPage.Clear;
    { aka.ms 링크는 항상 최신 8.0 데스크톱 런타임(x64)으로 리다이렉트됨 }
    DownloadPage.Add('https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', 'dotnet-desktop-runtime.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;
