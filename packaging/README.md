# 일집 셸 확장 — 빌드 및 설치 가이드

## 사전 준비 (한 번만)

### 1. Visual Studio 2022 + C++ 워크로드

이미 .NET 10 SDK가 깔려 있으면 Visual Studio Installer를 열고 **"C++를 사용한 데스크톱 개발"** 워크로드만 추가하면 돼. (또는 Build Tools for VS 2022도 가능)

### 2. 자체 서명 인증서 생성

PowerShell을 **관리자 권한**으로 열고:

```powershell
cd "C:\Users\H800005\OneDrive - 일진홀딩스\문서\Claude\Projects\일집\packaging"
.\CreateCert.ps1
```

→ `packaging\cert\Iljip.pfx` 가 생성되고, 신뢰 저장소에 등록돼.

## 빌드

일반 PowerShell:

```powershell
cd "C:\Users\H800005\OneDrive - 일진홀딩스\문서\Claude\Projects\일집\packaging"
.\Build.ps1
```

→ `build\Iljip.msix` + `build\package\` 폴더에 Iljip.exe, DLL 등.

## 설치

```powershell
.\Install.ps1
```

→ Add-AppxPackage 가 MSIX 등록. 윈도우가 셸 확장을 인식.

## 제거

```powershell
.\Uninstall.ps1
```

→ Get-AppxPackage / Remove-AppxPackage.

## 동작 확인

탐색기에서:
- `.zip` 파일 우클릭 → "일집으로 열기", "여기에 압축 풀기 (일집)", "압축 풀기... (일집)"
- 일반 파일/폴더 우클릭 → "일집으로 압축"

(Win11에선 새 컨텍스트 메뉴에 직접 노출. 새로고침에 1~2초 걸릴 수 있음.)

## 트러블슈팅

- **"패키지 신뢰할 수 없음" 에러**: 인증서가 `TrustedPeople`에 등록 안 됨. CreateCert.ps1을 관리자 권한으로 다시 실행.
- **메뉴 안 보임**: 작업 관리자에서 "Windows 탐색기"를 다시 시작 (셸 확장 DLL이 캐시됨).
- **빌드 시 MakeAppx.exe 못 찾음**: Windows 10/11 SDK 설치 확인. VS Installer에서 "Windows 10/11 SDK" 선택.

## Assets 폴더

`packaging\Assets\` 에 PNG 로고들이 필요해 (manifest에서 참조):
- `StoreLogo.png` (50x50)
- `Square150x150Logo.png` (150x150)
- `Square44x44Logo.png` (44x44)

지금은 더미 이미지를 직접 만들거나 임시로 빈 PNG 넣어도 빌드 통과.
