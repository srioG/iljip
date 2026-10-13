; ============================================================
;  일집 (Iljip) 설치 스크립트 — Inno Setup
;
;  동작:
;    - .NET 8 데스크톱 런타임이 없으면 Microsoft에서 자동 다운로드 후 설치
;    - 일집 본체를 Program Files에 설치
;    - 시작 메뉴 / (선택) 바탕화면 바로가기 생성
;    - 제거 프로그램 등록
;    - (W2-12) 압축 파일의 '연결 프로그램'에 일집을 넣음(ProgId·OpenWithProgids·아이콘 — 기본 앱은 빼앗지 않음, 제거하면 지움)
;      관리자 설치는 HKLM\Software\Classes, 사용자 설치는 HKCU(Root: HKA). 일집 ☰ ▸ 설정 '파일 연결'(Iljip.exe --register-associations)이
;      쓰는 것과 같은 키(Iljip.Tests FileAssociationRegistrarTests 가 C# FileAssociationRegistrar 와 맞춰 봄). 기본 앱(UserChoice)은 쓰지 않는다.
;    - ★ 이 묶음(W2-12)에서는 ISCC 로 만들어 보지 않았다(설치 프로그램 만들기·dist 재배포는 사람이 할 일)
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
; W2-12: 파일 연결을 바꾸므로 설치·제거 뒤 탐색기에 알림(SHChangeNotify)
ChangesAssociations=yes

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; GroupDescription: "추가 작업:"

[Files]
; 일집 publish 산출물 전체
Source: "{#PublishDir}\*"; Excludes: "icons\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
; W2-12: 형식 아이콘(파일 연결 DefaultIcon '{app}\icons\<ext>.ico') — Iljip.csproj 가 발행 폴더 icons\ 에 둔다
Source: "{#PublishDir}\icons\*.ico"; DestDir: "{app}\icons"; Flags: ignoreversion

[Icons]
Name: "{group}\일집"; Filename: "{app}\{#AppExeName}"
Name: "{group}\일집 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\일집"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; W2-12(I1-1): 파일 연결 — C# FileAssociationRegistrar.ExpectedEntries 와 같은 키·값(드롭 처리기는 셸 확장 DLL 이 있을 때 일집 설정에서만).
; ProgId·Applications·Capabilities 는 제거하면 키째(uninsdeletekey), 남과 같이 쓰는 OpenWithProgids·RegisteredApplications 는 우리 값만(uninsdeletevalue).
; .zip
Root: HKA; Subkey: "Software\Classes\Iljip.zip"; ValueType: string; ValueName: ""; ValueData: "ZIP 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.zip"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "ZIP 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.zip\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\zip.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.zip\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.zip\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.zip\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.zip\OpenWithProgids"; ValueType: string; ValueName: "Iljip.zip"; ValueData: ""; Flags: uninsdeletevalue
; .7z
Root: HKA; Subkey: "Software\Classes\Iljip.7z"; ValueType: string; ValueName: ""; ValueData: "7Z 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.7z"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "7Z 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.7z\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\7z.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.7z\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.7z\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.7z\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.7z\OpenWithProgids"; ValueType: string; ValueName: "Iljip.7z"; ValueData: ""; Flags: uninsdeletevalue
; .rar
Root: HKA; Subkey: "Software\Classes\Iljip.rar"; ValueType: string; ValueName: ""; ValueData: "RAR 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.rar"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "RAR 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.rar\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\rar.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.rar\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.rar\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.rar\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.rar\OpenWithProgids"; ValueType: string; ValueName: "Iljip.rar"; ValueData: ""; Flags: uninsdeletevalue
; .tar
Root: HKA; Subkey: "Software\Classes\Iljip.tar"; ValueType: string; ValueName: ""; ValueData: "TAR 묶음 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.tar"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "TAR 묶음 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.tar\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\tar.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.tar\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.tar\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.tar\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.tar\OpenWithProgids"; ValueType: string; ValueName: "Iljip.tar"; ValueData: ""; Flags: uninsdeletevalue
; .gz
Root: HKA; Subkey: "Software\Classes\Iljip.gz"; ValueType: string; ValueName: ""; ValueData: "GZ 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.gz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "GZ 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.gz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\gz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.gz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.gz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.gz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.gz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.gz"; ValueData: ""; Flags: uninsdeletevalue
; .tgz
Root: HKA; Subkey: "Software\Classes\Iljip.tgz"; ValueType: string; ValueName: ""; ValueData: "TGZ 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.tgz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "TGZ 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.tgz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\tgz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.tgz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.tgz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.tgz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.tgz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.tgz"; ValueData: ""; Flags: uninsdeletevalue
; .bz2
Root: HKA; Subkey: "Software\Classes\Iljip.bz2"; ValueType: string; ValueName: ""; ValueData: "BZ2 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.bz2"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "BZ2 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.bz2\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\bz2.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.bz2\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.bz2\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.bz2\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.bz2\OpenWithProgids"; ValueType: string; ValueName: "Iljip.bz2"; ValueData: ""; Flags: uninsdeletevalue
; .tbz2
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2"; ValueType: string; ValueName: ""; ValueData: "TBZ2 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "TBZ2 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\tbz2.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz2\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.tbz2\OpenWithProgids"; ValueType: string; ValueName: "Iljip.tbz2"; ValueData: ""; Flags: uninsdeletevalue
; .tbz
Root: HKA; Subkey: "Software\Classes\Iljip.tbz"; ValueType: string; ValueName: ""; ValueData: "TBZ 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.tbz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "TBZ 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\tbz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.tbz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.tbz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.tbz"; ValueData: ""; Flags: uninsdeletevalue
; .xz
Root: HKA; Subkey: "Software\Classes\Iljip.xz"; ValueType: string; ValueName: ""; ValueData: "XZ 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.xz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "XZ 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.xz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\xz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.xz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.xz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.xz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.xz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.xz"; ValueData: ""; Flags: uninsdeletevalue
; .txz
Root: HKA; Subkey: "Software\Classes\Iljip.txz"; ValueType: string; ValueName: ""; ValueData: "TXZ 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.txz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "TXZ 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.txz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\txz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.txz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.txz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.txz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.txz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.txz"; ValueData: ""; Flags: uninsdeletevalue
; .zipx
Root: HKA; Subkey: "Software\Classes\Iljip.zipx"; ValueType: string; ValueName: ""; ValueData: "ZIPX 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.zipx"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "ZIPX 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.zipx\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\zipx.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.zipx\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.zipx\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.zipx\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.zipx\OpenWithProgids"; ValueType: string; ValueName: "Iljip.zipx"; ValueData: ""; Flags: uninsdeletevalue
; .lzh
Root: HKA; Subkey: "Software\Classes\Iljip.lzh"; ValueType: string; ValueName: ""; ValueData: "LZH 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.lzh"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "LZH 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.lzh\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\lzh.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.lzh\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.lzh\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.lzh\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.lzh\OpenWithProgids"; ValueType: string; ValueName: "Iljip.lzh"; ValueData: ""; Flags: uninsdeletevalue
; .lha
Root: HKA; Subkey: "Software\Classes\Iljip.lha"; ValueType: string; ValueName: ""; ValueData: "LHA 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.lha"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "LHA 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.lha\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\lha.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.lha\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.lha\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.lha\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.lha\OpenWithProgids"; ValueType: string; ValueName: "Iljip.lha"; ValueData: ""; Flags: uninsdeletevalue
; .cab
Root: HKA; Subkey: "Software\Classes\Iljip.cab"; ValueType: string; ValueName: ""; ValueData: "CAB 압축 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.cab"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "CAB 압축 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.cab\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\cab.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.cab\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.cab\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.cab\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.cab\OpenWithProgids"; ValueType: string; ValueName: "Iljip.cab"; ValueData: ""; Flags: uninsdeletevalue
; .wim
Root: HKA; Subkey: "Software\Classes\Iljip.wim"; ValueType: string; ValueName: ""; ValueData: "WIM 이미지 파일"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.wim"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "WIM 이미지 파일"
Root: HKA; Subkey: "Software\Classes\Iljip.wim\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\wim.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.wim\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.wim\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.wim\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.wim\OpenWithProgids"; ValueType: string; ValueName: "Iljip.wim"; ValueData: ""; Flags: uninsdeletevalue
; .egg
Root: HKA; Subkey: "Software\Classes\Iljip.egg"; ValueType: string; ValueName: ""; ValueData: "EGG 압축 파일(알집)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.egg"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "EGG 압축 파일(알집)"
Root: HKA; Subkey: "Software\Classes\Iljip.egg\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\egg.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.egg\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.egg\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.egg\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.egg\OpenWithProgids"; ValueType: string; ValueName: "Iljip.egg"; ValueData: ""; Flags: uninsdeletevalue
; .alz
Root: HKA; Subkey: "Software\Classes\Iljip.alz"; ValueType: string; ValueName: ""; ValueData: "ALZ 압축 파일(알집)"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.alz"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "ALZ 압축 파일(알집)"
Root: HKA; Subkey: "Software\Classes\Iljip.alz\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\alz.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.alz\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.alz\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.alz\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.alz\OpenWithProgids"; ValueType: string; ValueName: "Iljip.alz"; ValueData: ""; Flags: uninsdeletevalue
; .001
Root: HKA; Subkey: "Software\Classes\Iljip.001"; ValueType: string; ValueName: ""; ValueData: "나뉜 압축 파일 첫 조각"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.001"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "나뉜 압축 파일 첫 조각"
Root: HKA; Subkey: "Software\Classes\Iljip.001\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\001.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.001\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.001\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.001\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.001\OpenWithProgids"; ValueType: string; ValueName: "Iljip.001"; ValueData: ""; Flags: uninsdeletevalue
; .z01
Root: HKA; Subkey: "Software\Classes\Iljip.z01"; ValueType: string; ValueName: ""; ValueData: "나뉜 ZIP 첫 조각"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.z01"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "나뉜 ZIP 첫 조각"
Root: HKA; Subkey: "Software\Classes\Iljip.z01\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\z01.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.z01\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.z01\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.z01\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.z01\OpenWithProgids"; ValueType: string; ValueName: "Iljip.z01"; ValueData: ""; Flags: uninsdeletevalue
; .iso
Root: HKA; Subkey: "Software\Classes\Iljip.iso"; ValueType: string; ValueName: ""; ValueData: "ISO 디스크 이미지"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Iljip.iso"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "ISO 디스크 이미지"
Root: HKA; Subkey: "Software\Classes\Iljip.iso\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\icons\iso.ico"
Root: HKA; Subkey: "Software\Classes\Iljip.iso\shell"; ValueType: string; ValueName: ""; ValueData: "open"
Root: HKA; Subkey: "Software\Classes\Iljip.iso\shell\open"; ValueType: string; ValueName: ""; ValueData: "일집으로 열기"
Root: HKA; Subkey: "Software\Classes\Iljip.iso\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\.iso\OpenWithProgids"; ValueType: string; ValueName: "Iljip.iso"; ValueData: ""; Flags: uninsdeletevalue
; Applications\Iljip.exe — '연결 프로그램' 목록의 이름·지원 형식
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "일집"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Iljip.exe"" --open ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".zip"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".7z"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".rar"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".tar"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".gz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".tgz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".bz2"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".tbz2"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".tbz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".xz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".txz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".zipx"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".lzh"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".lha"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".cab"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".wim"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".egg"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".alz"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".001"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".z01"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Iljip.exe\SupportedTypes"; ValueType: string; ValueName: ".iso"; ValueData: ""
; Capabilities + RegisteredApplications — Windows '기본 앱' 목록에 '일집'(iso 는 넣지 않음 — Windows 디스크 이미지 기본 연결)
Root: HKA; Subkey: "Software\Iljip"; Flags: uninsdeletekeyifempty
Root: HKA; Subkey: "Software\Iljip\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "일집"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Iljip\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "광고 없는 압축 프로그램"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".zip"; ValueData: "Iljip.zip"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".7z"; ValueData: "Iljip.7z"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".rar"; ValueData: "Iljip.rar"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tar"; ValueData: "Iljip.tar"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".gz"; ValueData: "Iljip.gz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tgz"; ValueData: "Iljip.tgz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".bz2"; ValueData: "Iljip.bz2"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tbz2"; ValueData: "Iljip.tbz2"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".tbz"; ValueData: "Iljip.tbz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".xz"; ValueData: "Iljip.xz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".txz"; ValueData: "Iljip.txz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".zipx"; ValueData: "Iljip.zipx"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".lzh"; ValueData: "Iljip.lzh"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".lha"; ValueData: "Iljip.lha"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".cab"; ValueData: "Iljip.cab"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".wim"; ValueData: "Iljip.wim"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".egg"; ValueData: "Iljip.egg"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".alz"; ValueData: "Iljip.alz"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".001"; ValueData: "Iljip.001"
Root: HKA; Subkey: "Software\Iljip\Capabilities\FileAssociations"; ValueType: string; ValueName: ".z01"; ValueData: "Iljip.z01"
Root: HKA; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "일집"; ValueData: "Software\Iljip\Capabilities"; Flags: uninsdeletevalue

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
