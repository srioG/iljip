# 일집 프로젝트 인수인계

## 프로젝트 개요

**일집 (Iljip)** — 광고 없는 압축 프로그램. 반디집 클론.

- **작업 폴더**: `D:\Python\일집`
- **빌드 산출물 폴더**: `D:\Python\일집-build` (OneDrive 동기화 충돌 회피용 외부 폴더)

## 기술 스택

- **WPF 본체 (Iljip)**: C# / .NET 8 (사용자 PC SDK는 .NET 10, 하위 호환) / WPF / CommunityToolkit.Mvvm
- **압축 라이브러리**:
  - SharpCompress 0.38.0 (다중 포맷 해제 + ZIP 외 압축)
  - SharpZipLib 1.4.2 (ZIP AES-256 암호 압축)
  - System.Text.Encoding.CodePages (CP949 한글 자동 디코딩)
- **셸 확장 (Iljip.ShellExtension)**: C++/WRL, IExplorerCommand DLL
- **packaging**: MSIX Sparse Package, 자체 서명 인증서

## 솔루션 구조

```
일집/
├── Iljip.sln
├── Iljip/                         # WPF 본체
├── Iljip.ShellExtension/          # C++ COM DLL
└── packaging/                     # MSIX 빌드/설치 스크립트
    ├── Package.appxmanifest
    ├── CreateCert.ps1
    ├── Build.ps1
    ├── Install.ps1
    ├── Uninstall.ps1
    ├── Rebuild.ps1                # 풀 재배포 (-Clean 옵션)
    └── Assets/                    # 더미 PNG
```

## 완료된 기능

- ZIP/7Z/TAR/GZ/BZ2/RAR 해제, ZIP/TAR/GZ/BZ2 압축 (7Z/RAR은 SharpCompress 미지원)
- 한글 파일명 CP949/UTF-8 자동 판별
- ZIP AES-256 암호 압축 / 해제 시 비밀번호 재시도 다이얼로그
- 압축 옵션 다이얼로그 (저장/빠름/보통/최대 4단계)
- 드래그앤드롭: 압축 파일 → 열기 / 일반 파일·폴더 → 스테이징 추가
- 스테이징 모드 (반디집 스타일: 파일 모았다가 "압축 실행" 버튼)
- CLI 인자: `--open`, `--extract`, `--extract-here`, `--extract-to`, `--compress`
- C++ 셸 확장 4개 메뉴: "일집으로 열기" / "여기에 압축 풀기" / "압축 풀기..." / "일집으로 압축"

## 현재 진행 단계 (인수인계 시점)

**MSIX Sparse Package 설치 디버깅 중**. WPF 빌드 통과, C++ DLL 빌드 통과, MSIX 패키징까지 도달.

마지막에 manifest에서 `desktop4:ItemType Type="Directory"` 항목을 제거함 (desktop4 스키마 거부). **폴더 우클릭 메뉴는 임시로 빠진 상태** — 추후 desktop6 namespace로 복원 필요.

**다음 검증 단계**:
1. `Rebuild.ps1` 실행 → MakeAppx 통과 확인
2. SignTool 서명 통과 확인
3. Add-AppxPackage 설치 통과 확인
4. 탐색기에서 .zip 우클릭 → "일집으로 열기" 등 표시 확인

## 빌드 / 실행 명령어

### 최초 1회 (관리자 PowerShell)
```powershell
cd "D:\Python\일집\packaging"
.\CreateCert.ps1
```

### 셸 확장 풀 재배포
```powershell
cd "D:\Python\일집\packaging"
.\Rebuild.ps1
```
- `-Clean` : 빌드 캐시 전부 삭제 후 클린 빌드
- `-SkipExplorerRestart` : 탐색기 재시작 생략

### WPF 본체만 빠르게 테스트
```powershell
cd "D:\Python\일집"
dotnet run --project Iljip
```

## 환경 요구사항

- Windows 10 22H2 이상 (Win11 권장)
- .NET 10 SDK 설치 완료
- Visual Studio Build Tools 2026 + "C++를 사용한 데스크톱 개발" 워크로드
- MSVC v143 - VS 2022 C++ x64/x86 빌드 도구 (체크 완료)
- Windows 11 SDK 10.0.26100.x

## 알려진 함정 (반드시 기억)

1. **PowerShell 5.1 한글**: 새 .ps1 파일에 UTF-8 BOM 필수
2. **C++ 한글**: vcxproj에 `/utf-8` 컴파일러 옵션 필수
3. **WRL CoCreatableClass**: 매크로 인자에 네임스페이스 prefix 금지 (using namespace로 우회) + 클래스에 `__declspec(uuid("..."))` 필수
4. **PlatformToolset**: `$(DefaultPlatformToolset)` 사용 (v143 하드코딩 금지)
5. **링크**: `pathcch.lib` 명시 필요
6. **MakeAppx**: manifest는 정확히 `AppxManifest.xml` 파일명으로 패키지에 복사 필요
7. **desktop4 ItemType**: Type 속성은 `*` 또는 `.확장자`만 허용. `Directory`/`Folder` 거부 → desktop6 필요
8. **빌드 폴더**: OneDrive 안에서 C++ 빌드는 PDB 동기화 충돌. 무조건 `D:\Python\일집-build`로

## 향후 작업

- [ ] **폴더 우클릭 메뉴 복원** (desktop6 namespace 적용)
- [ ] 7Z 암호 압축 검토 (SevenZipSharp 도입 여부)
- [ ] ALZ / EGG 지원
- [ ] 분할 압축
- [ ] 다크 모드
- [ ] 미리보기 (압축 내부 텍스트/이미지)
- [ ] 압축 파일 내부 폴더 트리 탐색 (현재 flat list)

## 사용자 선호

- 한국어, 사근사근한 톤
- 방향성 무조건 따르지 말고 조언할 게 있으면 먼저 물어볼 것
- 코딩 검증 통과 + 질문 없으면 다음 단계 자동 진행
- 매번 칠 명령어는 코드 블록으로 정리해서 제공
