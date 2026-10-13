# 일집 프로젝트 인수인계

## 프로젝트 개요

**일집 (Iljip)** — 광고 없는 압축 프로그램. 반디집 클론.

- **작업 폴더**: `D:\Python\일집`
- **빌드 산출물 폴더**: `D:\Python\일집-build` (OneDrive 동기화 충돌 회피용 외부 폴더)

## 기술 스택

- **WPF 본체 (Iljip)**: C# / .NET 8 (사용자 PC SDK는 .NET 10, 하위 호환) / WPF / CommunityToolkit.Mvvm
- **압축 라이브러리**:
  - SharpCompress 0.50.5 (다중 포맷 해제 + ZIP 외 압축) — 2026-10-12 0.38.0(보안 권고 GHSA-6c8g-7p36-r338)에서 올림
  - SharpZipLib 1.4.2 (ZIP AES-256·ZipCrypto 암호 압축, CRC-32 계산)
  - EggDotNet 0.4.0 (EGG/ALZ 해제)
  - SharpSevenZip 2.0.128 (LGPL-3.0) + 그 안의 7z.dll 7-Zip 26.03 x64 (풀기: 7z·RAR·ISO/UDF·CAB·LZH·ZIPX·WIM·XZ·자동 풀림 exe —
    2026-10-13 W2-01, **7Z 만들기 — W2-02**). `x64\7z.dll` 은 단일 파일 발행에서도 따로 두는 파일(LGPL), 못 불러오면 7Z·RAR 풀기는 SharpCompress 로 대신
    (나머지 형식·7Z 만들기는 쉬운 안내, 저장 형식에서 7Z 를 뺌)
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

- ZIP/7Z/TAR/GZ/BZ2/RAR(RAR4·RAR5)/EGG/ALZ 해제, ZIP/TAR/GZ/BZ2 압축 (7Z/RAR은 SharpCompress 미지원)
  → W2-01(2026-10-13, 7z.dll): RAR(여러 조각)·ISO/UDF·CAB·LZH·ZIPX·WIM·XZ·숫자 분할(`.7z.001`·`.zip.001`)·압축된 TAR(`.tgz` 등 — tar 안 내용까지 바로)·
  자동 풀림 exe 해제 추가. → **W2-02(2026-10-13): 7Z 만들기**(수준 저장만·빠름·보통·최대, 솔리드, AES-256, 파일 이름도 암호로 숨기기 — 7z.dll)
  → **W2-03(2026-10-13): 나눠 만들기**(분할 압축 — 옵션 창 '나누기' 10MB·20MB·직접 입력. ZIP 은 PKZIP 나눔 `이름.z01 … 이름.zip`, 7Z 는 `이름.7z.001 …`)
- 암호: ZIP(ZipCrypto·AES)·7z(이름 보임·파일 이름까지 암호)·RAR4·RAR5(파일 암호·머리 암호)·EGG 모두 같은 암호 창 길
  (없으면 '암호가 걸린…', 머리 암호면 '파일 이름까지…', 틀리면 '암호가 맞지 않아요…', [취소]할 때까지 다시 물음)
- 한글 파일명 CP949/UTF-8 자동 판별
- ZIP AES-256·ZipCrypto 암호 압축 / 해제 시 비밀번호 재시도 다이얼로그(반디집 한글 암호 ZIP 도 풀림, '암호 보이기')
- 압축 옵션 다이얼로그 (저장/빠름/보통/최대 4단계)
- 드래그앤드롭: 빈 창에 압축 파일 → 열기 / 일반 파일·폴더 → 스테이징 추가. 압축을 보는 중·모으는 중에 끌어 놓으면 선택 창(보던 압축을 말없이 닫지 않음)
- 스테이징 모드 (반디집 스타일: 파일 모았다가 "압축 실행" 버튼). 다른 프로그램이 잠근 파일은 건너뛰고 끝에 '건너뛴 파일 N개' 안내
- 목록 열 머리 클릭 정렬(폴더 먼저, 폴더를 옮겨도 유지, 이름은 탐색기와 같은 자연 정렬)·열 폭 끌기
- 파일 이름까지 암호가 걸린 7z·RAR 는 열 때 암호를 물음('파일 목록까지 암호로 잠긴…' — 틀리면 '암호가 맞지 않아요' 경고 뒤 다시, 취소면 보던 것 그대로,
  받은 암호로 이어서 풂 — W2-02). 암호 창은 [취소]할 때까지 다시 물음
- CLI 인자: `--open`, `--extract`, `--extract-here`(그 자리에), `--extract-to-folder`(이름 폴더에, 있으면 '이름 (2)'), `--extract-to`, `--compress` — 해석은 `Services/CommandLineParser.cs`
  (W2-05: 풀기 동사 뒤 압축 여러 개 = 여러 개 풀기, 탐색기 우클릭 풀기는 메인 창 없이 진행 창만 → 끝나면 '이름' 폴더를 탐색기로)
- **W2-05(2026-10-13)**: 같은 이름 파일이 있으면 덮어쓰기·건너뛰기·이름 바꾸기·취소 + '남은 파일도 똑같이'(☰ ▸ 설정에서 기본 동작 고정), '다 푼 뒤 압축 파일을 휴지통으로'(기본 끔), 여러 압축 한꺼번에 풀기(진행 창 하나·암호 한 번·끝에 요약)
- C++ 셸 확장 5개 메뉴: "일집으로 열기" / "여기에 풀기 (일집)" / "\"이름\"\ 에 풀기 (일집)" / "압축 풀기... (일집)" / "일집으로 압축"

## 현재 진행 단계 (2026-10-13 W2-05 후)

**W2-05(같은 이름 묻기 I1-5·메인 창 없는 우클릭 풀기 I1-6·풀고 나서 지우기 I2-5·여러 개 풀기 I2-6) 작업 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`stash` 안 함,
stash 두 개 `iljip-wave2-W2-04-failed`·`iljip-wave1-W1-B-failed` 는 손대지 않음). 계약 테스트는 아래 변경 이력 'W2-05'(시간에 민감한 7z 취소 시험 1개가 이 PC 가 바쁠 때만 실패 — 그 문단 끝).
설정 저장(`settings.json`)·`AppServices` 는 W2-04 보관본과 같은 모양으로 이 묶음에서 처음 들였다 — W2-04 를 다시 할 때 겹치는 부분은 이쪽을 바탕으로. 그 전(W2-03) 기록:

**W2-03(나눠 만들기 — 분할 압축 I2-2 만들기) 작업 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`stash` 안 함).
계약 테스트 실패 0(전체 1305 — 통과 1304·건너뜀 1: I2-10, 계약만 `Kind!=Record` 1148 — 통과 1147·건너뜀 1, 기록 157). 아래 변경 이력 'W2-03'. 그 전(W2-02) 기록:

**W2-02(7z 만들기 I2-1·목록까지 잠긴 7z·RAR 열기 I1-7) 작업 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`stash` 안 함).
계약 테스트 실패 0(전체 1201 — 통과 1200·건너뜀 1: I2-10, 계약만 `Kind!=Record` 1044 — 통과 1043·건너뜀 1, 기록 157). 아래 변경 이력 'W2-02'. 그 전(W2-01) 기록:

**W2-01(7z.dll 해제 쪽 — 1차 W1-B 재작업) 작업 끝, 커밋 전**(커밋·`git stash` 정리는 오케스트레이터 몫 — stash 는 drop 하지 않았고 인덱스에 UU 표시가 남아 있음).
계약 테스트 실패 0(전체 1099 — 통과 1097·건너뜀 2: I2-1·I2-10, 계약만 `Kind!=Record` 944 — 통과 942·건너뜀 2, 기록 155. 통합 점검 후 840 — 통과 834·건너뜀 6 에서). 7z 만들기는 W2-02(아래 '향후 작업'). 아래는 그 전 상태 기록.

**Wave 1(W1-A·W1-C·W1-D) 커밋·푸시 완료 + 통합 점검 끝**. W1-B(7z.dll — SharpSevenZip)는 검증에서 되돌려 `git stash`(`iljip-wave1-W1-B-failed`)에 있다.
계약 테스트 실패 0(통과 834·건너뜀 6·전체 840 — Debug·Release·`ILJIP_TEST_TZ=UTC` 모두). **`D:\Python\일집-dist`(재사용장치가 매일 egg 를 푸는 배포본)는 아직 예전 판**
(2026-06-23, SharpCompress 0.38.0·EggDotNet 0.3.0)이다 — 바꾸기 전에 아래 '일집-dist 재배포' 절과 `Iljip.Tests/MANUAL_CHECKLIST.md` 를 사람이 확인할 것.

**다음 단계**:
1. 사용자 실제 PC 확인(MANUAL_CHECKLIST 1~8절 — 특히 6절 진짜 알집·WinRAR 파일, 8절 재사용장치 경로)
2. 확인되면 일집-dist 재배포(아래 절차) → 재사용장치 다음 날 로그에서 '(Iljip으로 .egg 해제)' 뒤 'Bandizip 폴백'이 늘지 않았는지 확인
3. ~~W1-B(7z.dll) 다시 시도~~ → W2-01 에서 해제 쪽 끝(숫자 분할 풀기·tar.gz 등 한 번에 펼치기 I2-9 포함), W2-02 에서 7z 만들기(I2-1) 끝, W2-03 에서 분할 만들기(I2-2) 끝
   — 재배포 전에 MANUAL_CHECKLIST 9절(7z.dll·새 형식 실물 확인)·10절(7z 만들기·목록까지 잠긴 압축)·11절(나눠 만들기)도 볼 것

### MSIX 셸 확장(예전 진행 상황 — 그대로)

WPF 빌드 통과, C++ DLL 빌드 통과, MSIX 패키징까지 도달. manifest 에서 `desktop4:ItemType Type="Directory"` 항목을 제거함(desktop4 스키마 거부).
**폴더 우클릭 메뉴는 임시로 빠진 상태** — 추후 desktop6 namespace로 복원 필요. 다음 검증: `Rebuild.ps1` → MakeAppx → SignTool 서명 → Add-AppxPackage 설치 → 탐색기 우클릭 메뉴 표시.

## 일집-dist 재배포 (재사용장치 영향) — 사람이 할 일, 자동으로 하지 않음

- 재사용장치(`D:\Python\재사용장치\System_code\재사용장치_processor.py` `_find_iljip`)는 `config.json` 의 `iljip_path` 가 비어 있으면
  `D:\Python\일집\Iljip\bin\Release\…\Iljip.exe` → `…\bin\Debug\…` → **`D:\Python\일집-dist\Iljip.exe`** 순으로 찾는다. 빌드 산출물은
  `Directory.Build.props` 때문에 `D:\Python\일집-build\dotnet\…` 로 가서 앞의 둘은 비어 있다 → 지금은 일집-dist 를 쓴다(2026-10-12 확인).
  ★ 그래서 `Iljip\bin\Release` 에 exe 를 만들면(빌드 설정을 바꾸는 등) 재배포 없이도 재사용장치가 그 판으로 바뀐다 — 조심.
- 재사용장치는 egg·alz(그리고 파이썬 py7zr·rarfile 이 없으면 7z·rar)를 `Iljip.exe --extract-to <첨부> <임시폴더>` 로 푼다. 종료 코드 0 이면 가져가고,
  0 이 아니면 출력에 '압축 파일이 아닌'·'not archive'·'unsupported archive' 가 있으면 '압축 아님'으로 끝, 아니면 반디집으로 다시 푼다.
  이 약속은 `Iljip.Tests/Contract/ReuseDeviceContractTests.cs` 가 실제 exe 로 지킨다.
- 바뀌는 점(합성 파일로 예전 배포본과 새 판을 나란히 실행해 비교, 2026-10-12):
  | 첨부 | 예전 일집-dist | 새 판 |
  |---|---|---|
  | 0바이트 파일·폴더가 든 egg | 종료 1(Object reference…) → 반디집으로 | 종료 0, 다 풀림 |
  | 한글 이름 alz(CP949) | 종료 0 이지만 이름이 깨짐(`�ڷ�\…`) | 종료 0, 이름 정상 |
  | 암호 egg | 콘솔에 'Please enter password' 를 띄우고 종료 1 | 바로 종료 1('암호가 걸린 압축 파일이에요…') → 반디집으로 |
  | 끝이 잘린 egg | 종료 1(Object reference…) | 종료 1(쉬운 문구, 짧은 파일 남기지 않음) → 반디집으로 |
  | 암호 RAR4 | **종료 0 + 쓰레기 파일**(암호를 안 물음) | 종료 1 → 반디집으로 |
  | 암호·머리 암호 RAR5 | 종료 1(영어 문구) | 종료 1(쉬운 문구) |
  실패 문구는 표준 오류에 CP949 로 나가서(재사용장치는 UTF-8 → CP949 순으로 읽음) '—'(긴 줄표)가 '?' 로 보일 수 있다(동작 영향 없음).
- 위험: 새 판은 egg/alz 를 풀면서 크기·CRC 를 직접 확인한다(예전엔 확인 안 함). 진짜 알집 파일에서 이 확인이 어긋나면 멀쩡한 egg 가 종료 1 → 반디집으로 풀린다
  (반디집이 있으면 결과는 같고 로그만 늘어남. **반디집을 지운 PC 라면 해제 실패**). 그래서 재배포 전에 MANUAL_CHECKLIST 8절로 진짜 egg 몇 개를 새 판으로 풀어 볼 것.
- 절차(예시 — 재사용장치가 돌지 않는 시간에):
  ```powershell
  dotnet publish "D:\Python\일집\Iljip\Iljip.csproj" -c Release -o "D:\Python\일집-build\publish-새판"
  Rename-Item "D:\Python\일집-dist" "일집-dist-20260623-예전판"     # 되돌릴 곳
  Copy-Item "D:\Python\일집-build\publish-새판" "D:\Python\일집-dist" -Recurse
  ```
  새 판 파일은 9개(Iljip.exe·dll·pdb·deps.json·runtimeconfig.json, SharpCompress·EggDotNet·SharpZipLib·CommunityToolkit.Mvvm). 예전 판의 `ZstdSharp.dll` 은 이제 필요 없다.
  되돌리기: 새 폴더를 지우고 예전 판 이름을 `일집-dist` 로 되돌린다.

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

### C++ 셸 확장 DLL 만 빌드 (Windows SDK 미설치 PC — 2026-10-12 W1-D 실측)
이 PC 에는 Windows SDK 헤더가 없어 `Build.ps1` 이 'windows.h 없음' 으로 멈춘다. NuGet 의 SDK 패키지로 DLL 만 빌드할 수 있다
(시스템 설치 없음. MakeAppx·서명·설치는 여전히 설치된 SDK 필요).
```powershell
dotnet restore "D:\Python\일집\packaging\WinSdk\WinSdk.csproj"
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" "D:\Python\일집\Iljip.ShellExtension\Iljip.ShellExtension.vcxproj" /p:Configuration=Release /p:Platform=x64 /p:UseNuGetWindowsSdk=true "/p:OutDir=D:\Python\일집-build\Release\x64\Iljip.ShellExtension\\" "/p:IntDir=D:\Python\일집-build\obj\Release\x64\Iljip.ShellExtension\\"
```
빌드된 DLL 이 있으면 계약 테스트 `ShellExtensionContractTests.DLL_메뉴_제목이_CSharp_이름_규칙과_같다` 가 실제 메뉴 제목을 확인한다(없거나 소스보다 오래되면 건너뜀).

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

- [ ] **일집-dist 재배포** (위 '일집-dist 재배포' 절 — MANUAL_CHECKLIST 확인 뒤)
- [ ] **폴더 우클릭 메뉴 복원** (desktop6 namespace 적용)
- [x] W1-B 다시 → **W2-01(해제 쪽) 끝**: 7z.dll 로 7z·RAR·ISO/UDF·CAB·LZH·ZIPX·WIM·XZ·숫자 분할·압축된 TAR·자동 풀림 exe 풀기(아래 변경 이력)
- [x] **W2-02**: 7z 만들기(I2-1)·파일 이름 암호화·솔리드·옵션 창 7Z 칸·저장 형식 7Z, 목록까지 잠긴 7z·RAR 열기(I1-7) — 보관 시험 되살림(아래 변경 이력)
- [ ] W2-02 남은 확인: MANUAL_CHECKLIST 10절(옵션 창 모양, 반디집·7-Zip 으로 일집 7z 열기, 작업 관리자 메모리, 진짜 7-Zip -mhe·WinRAR 머리 암호 파일)
- [ ] **재배포 전 필수(보류)**: 7-Zip `License.txt` 원문을 받아 `Iljip/licenses/7-Zip-*.txt`·`THIRD-PARTY-NOTICES.txt` 와 대조(내려받기는 사용자 확인 필요).
      GPL-3·LGPL-2.1 전문은 이 PC 의 Git for Windows 사본(FSF 원문)으로 넣음, BSD 3·2조항 전문의 저작권 줄(연도 등)은 원문 없이 적음
- [x] ALZ / EGG 지원 (EggDotNet 0.4.0, W1-A 에서 빈 항목·ALZ 한글 이름·잠김·내용 확인까지)
- [ ] 새 형식·.egg/.alz 우클릭 메뉴: 매니페스트에는 넣음(W2-01 — 1차 W1-B 검증 지적) — `Rebuild.ps1` 재설치 때 반영(재배포 범위, 메뉴 정비는 I1-2)
- [ ] 보고 안건: 압축을 풀 때 '웹에서 받은 파일 표시(MOTW)'를 옮길지(지금은 안 옮김 — 재사용장치 엑셀 COM 영향 확인 필요)
- [x] **W2-03**: 분할 압축(나눠 만들기 — I2-2 만들기) — ZIP PKZIP 나눔·7Z 숫자 조각·옵션 창 '나누기' 칸(아래 변경 이력)
- [ ] W2-03 남은 확인: MANUAL_CHECKLIST 11절(옵션 창 '나누기' 칸, Windows 탐색기·알집으로 나눔 ZIP 열기, 진짜 메일 첨부, OneDrive 폴더)
- [x] **W2-05**: 같은 이름 묻기(I1-5)·메인 창 없는 우클릭 풀기(I1-6)·풀고 나서 압축 파일 휴지통으로(I2-5)·여러 개 풀기(I2-6), 설정 창(☰ ▸ 설정)
- [ ] W2-05 남은 확인: MANUAL_CHECKLIST 12절(같은 이름 창·설정 창 모양, 메인 창이 정말 안 뜨는지·창이 뒤에 숨지 않는지, 진짜 휴지통·네트워크 드라이브, 여러 개 골라 우클릭이 하나로 묶이는지)
- [ ] 셸 확장이 고른 파일들을 한 번에 넘기기(지금은 파일마다 일집을 띄우고 일집이 파이프로 묶음 — C++ `IExplorerCommand` 가 여러 경로를 한 명령줄로 넘기게, 재배포 범위)
- [ ] W2-04 다시 할 때: 설정(`Services/AppSettings.cs`)·`AppServices`·`IShellLauncher` 는 W2-05 가 같은 모양으로 먼저 들였음 — 덧붙이기만
- [ ] 다크 모드
- [ ] 미리보기 (압축 내부 텍스트/이미지)
- [ ] 압축 파일 내부 폴더 트리 탐색 (현재 flat list)

## 변경 이력

- **2026-10-12 W1-A — SharpCompress 보안 업데이트 + 엔진 버그 수정(커밋 45c5906, 배포 안 함)**: SharpCompress 0.38.0 → 0.50.5
  (0.50.4 로도 전체 테스트 통과 확인 — 문제 생기면 내릴 곳), EggDotNet 0.3.0 → 0.4.0. 0.50 API 변경은 `OpenTyped`(암호 없이 형식만
  판별 → 형식별 `OpenArchive`, 암호 준 판별이 AES ZIP 에서 실패하는 문제 회피)·`BZip2Stream.Create`·CustomDecoder 4인자로 맞춤.
  고친 버그: B1(7z 빈 파일 — 업그레이드로), B2(FNAME 없는 .gz → 파일명에서 `.gz` 뗀 이름, `Services/ArchiveNaming.cs`),
  B6(TAR 압축 취소가 큰 파일 쓰는 중 안 먹힘 → `Services/CancellableReadStream.cs`, 덤으로 파일 안 진행률·빈 폴더 기록),
  B7(진행 막대가 끝까지 0 — `ThrottledArchiveProgress` 오버플로), B8(손상 7z·egg·alz 열기 실패 후 원본 잠김 — egg/alz 는 스트림을
  직접 열고 실패 시 분할 조각까지 닫음, 암호 콜백을 늘 넣어 콘솔 암호 읽기도 막음), B9(크기 0 항목 EGG NRE), B10(ALZ CP949 한글
  이름 — `Services/AlzNameScanner.cs` 가 원본 바이트를 읽어 항목 수·ASCII 부분이 맞을 때만 바꿈). .tar.gz 는 0.50 이 아예 못 열어
  GZ 처리기가 gzip 으로 직접 열어 안쪽 `.tar` 하나를 꺼내게 함(.tar.bz2 와 같은 방식, 반디집처럼 tar 까지 펼치기는 I2-9).
  계약 테스트: 근거 테스트를 `Iljip.Tests/Contract/FixedBugTests.cs` 로 옮겨 뒤집고 Skip 10개 해제 → 통과 430·건너뜀 10·전체 440, 실패 0
  (자세한 표는 `Iljip.Tests/README.md` '고친 버그'). `D:\Python\일집-dist`(재사용장치가 쓰는 배포본)는 건드리지 않았다 — 재배포 전까지
  매일 egg 해제는 예전 판으로 돈다. B9·B10 은 합성 EGG/ALZ 로만 확인(진짜 알집 산출물은 MANUAL_CHECKLIST 6절).
- **2026-10-12 W1-A 1차 검증 지적 반영(커밋 45c5906 에 포함, 배포 안 함)**: ① 잘리거나 확인값이 틀린 .gz·.tar.gz 가 '완료'로 끝나며 절반짜리를
  남기던 문제 — 풀린 내용을 SharpZipLib `GZipInputStream` 으로 읽어 CRC·크기·잘림 확인(여러 멤버 이어 풀기, 멤버 뒤 쓰레기 무시),
  베이스에 `OpenEntryData` 훅. ② EGG/ALZ 도 풀린 내용의 크기·CRC 를 서비스가 직접 확인(솔리드 EGG 는 크기만), 틀리면 그 파일을 지우고
  오류(암호 항목이면 '암호가 틀렸거나' 문구로 다시 물음). ③ 끝 구분자 없는 폴더 항목을 폴더 속성으로 알아봄(ALZ 속성은
  `AlzNameScanner` 가 읽음). ④ ALZ 한자·기호 이름(CP949)·잘린 ALZ 의 이름 복구. ⑤ 점뿐인 이름의 .gz → '압축풀림'. ⑥ 항목 0개 RAR 은
  손상으로 알림. ⑦ 같은 폴더 동시 해제 충돌 → `Services/OutputFiles.cs`(CreateNew, 모든 서비스 공용). ⑧ 분할 EGG 는 같은 폴더의
  `.egg` 만 후보로. 계약 테스트 통과 478·건너뜀 10·전체 488, 실패 0(표는 `Iljip.Tests/README.md`).
- **2026-10-12 W1-C — 암호·무결성(커밋 98dce3b, 배포 안 함)**: W1-B(7z.dll)가 검증에서 되돌려져(stash) W1-A 위에서 했다 — 그래서 반디집 한글 AES
  해제는 7z.dll 대신 일집이 직접 푼다. ① B3: `Services/ZipPasswordProbe.cs` 가 풀기 전(아무것도 만들기 전) 중앙 디렉터리를 읽어 암호의
  UTF-8·CP949 바이트 중 맞는 것을 판정(ZipCrypto 는 모든 항목의 확인 바이트, AES 는 확인값 PV) — CP949 ZipCrypto 는 SharpCompress 에 바이트
  그대로(Latin-1), WinZip AES 는 `Services/WinZipAes.cs`(AES-CTR + HMAC 인증값 확인 — 한국어가 아닌 Windows 에서도 됨, 일집 AES(AE-2)의
  깨진 데이터도 이제 알아챔). 만들 때 ZipCrypto 한글 암호는 CP949 로 써서 반디집과 양방향 호환(bz.exe 실측 테스트 `BandizipCliTests`),
  AES 한글 암호는 SharpZipLib 가 UTF-8 로만 써서 옵션 창이 경고(다시 쓰기 / ZipCrypto 로 / 그대로). ② B4: ZIP 해제 때 CRC-32 확인 —
  암호 없는 항목은 `ChecksumMismatchException`(깨진 그 파일만 지움), 암호 항목은 판정이 암호를 충분히 확인했으면 손상, 아니면 틀린 암호.
  암호 오류면 `Services/ExtractionLedger.cs` 가 이번에 만든 파일·폴더를 모두 지움(EGG 도). ③ B5: 일집 예외 체계 `Services/ArchiveExceptions.cs` + `Services/EngineErrorTranslator.cs`
  — 7z 틀린 암호(Data Error)·헤더 암호·ZipCrypto 우연 통과·EGG 가 모두 `WrongPasswordException`/`PasswordRequiredException`, 화면
  `IsPasswordRelated` 는 타입으로 판정. ④ I2-11: `CompressionOptions.Encryption`(AES-256 기본 / ZipCrypto). ⑤ I0-3: 옵션 창·암호 창에 '암호 보이기',
  8자 권장 안내(`Services/PasswordPolicy.cs`), 새 `Views/ChoiceDialog`. `일집-dist` 는 그대로.
  **W1-C 1차 검증 지적(같은 날)**: ⑥ 항목 하나짜리(또는 하나만 골라 푼) ZipCrypto 에서 한글 암호의 UTF-8 바이트가 우연히 검사를 통과하면(약 1/128)
  맞는 암호로도 영영 못 풀던 것 — 판정이 두 후보를 다 보고, 둘 다 맞으면 고르지 않은 항목으로 가리고, 그래도 못 가리면 CP949 먼저 풀다가
  데이터가 틀리면 만든 것을 지우고 UTF-8 로 처음부터 다시(`ExtractPlan.Alternate`). ⑦ 판정 근거가 16비트 이상(`ZipPasswordProbeResult.Confirmed`)이면
  맞는 암호 + 깨진 데이터를 '틀린 암호'가 아니라 손상(`CorruptArchiveException`/`ChecksumMismatchException`)으로 — 다시 묻지 않고 앞서 맞게 풀린
  파일은 남김. ⑧ 이모지 암호 + AES 에서 'ZipCrypto(반디집·알집과 호환)'로 바꾸면 경고 없이 UTF-8 ZipCrypto 가 되던 것 — CP949 로 못 바꾸는 암호는
  방식과 상관없이 이모지 경고, 방식을 바꾸면 다시 판정. 계약 테스트 통과 578·건너뜀 6·전체 584, 실패 0.
- **2026-10-12 W1-D — 0등급 화면 문제 I0-1·I0-2·I0-4·I0-5(커밋 01a2897, 배포 안 함)**: W1-B(7z.dll)가 stash 라 그 안의 화면 분리(`IUserInteraction`)를
  같은 모양으로 새로 들였다(`Services/IUserInteraction.cs`·`Views/WpfUserInteraction.cs`, 선택 창 `Choose` 추가 — 테스트는 `FakeUserInteraction`).
  ⓪ 첫 실행 인자 버그: App.xaml 의 `StartupUri` 때문에 일집이 꺼져 있을 때 우클릭 메뉴를 고르면 빈 창만 떴다 → StartupUri 를 지우고 `OnStartup` 이
  단일 인스턴스 확인 뒤 창을 직접 만들어 인자를 넘김. ① I0-1: 명령줄 해석을 `Services/CommandLineParser.cs`(순수 함수)로 떼고, **`--extract-here` 의 뜻을
  '압축 파일이 있는 폴더에 바로'로 바꿈**(예전엔 '이름' 폴더 — 그 동작은 새 `--extract-to-folder` 로, 그 이름이 이미 있으면 '이름 (2)' 새 폴더, 합치지 않음).
  폴더 이름 규칙 `ArchiveNaming.FolderNameFor`(.001·.partN·.volN·.tar.gz 등을 뗌 — 'x.tar.gz' 는 이제 'x')·`UniqueFolder`. C++ 셸 확장: '여기에 풀기 (일집)'(CLSID 그대로)
  + 새 `ExtractToFolderCommand`(CLSID F5964E4F-…, 하나면 '"보고서"\ 에 풀기 (일집)', 여럿이면 '각각 이름 폴더에 풀기 (일집)', 제목은 파일 이름 문자열만 봄),
  매니페스트에 클래스·메뉴 추가(.zip .7z .rar .tar .gz .bz2). C++ 은 NuGet Windows SDK(`packaging/WinSdk`, vcxproj `UseNuGetWindowsSdk`)로 빌드 확인(종료 코드 0,
  MSIX 패키징·설치는 안 함). 여러 압축을 한꺼번에 고르면 명령이 거의 동시에 들어오므로 VM 이 하나씩 차례로 처리. ② I0-2: 원본을
  FileShare.ReadWrite|Delete 로 열고(`Services/SourceFileOpener.cs`, 공유 위반이면 150ms×3 재시도 — DLP 대비), 그래도 못 읽는 파일·못 들어가는 하위 폴더는
  건너뛰고(`Services/SourceCollector.cs`, 담는 차례는 예전과 같음) `IArchiveService.CompressAsync` 가 `Task<CompressResult>`(건너뛴 목록)를 돌려줌 —
  화면은 '압축을 마쳤어요. 건너뛴 파일 N개' 안내, 결과는 지우지 않음. GZ·BZ2(단일 파일)는 `SourceUnreadableException`. 덤: 실패 정리는 이번에 만들거나
  고친 파일만 지움(원본을 못 읽어 시작도 못 했는데 덮어쓰려던 기존 파일을 지우던 것), 원본 폴더 안에 저장하는 압축 자신은 담지 않음. 7z 만들기는 W1-B 와 같이.
  ③ I0-4: 끌어 놓기 규칙 `Services/DropPolicy.cs` — 압축을 보는 중이면 [이 압축 파일 열기]/[새로 압축할 목록 만들기]/[취소], 모으는 중에 압축 파일 하나면
  [열기]/[목록에 넣기]/[취소]. `AddToStaging` 은 더 이상 보던 압축을 몰래 닫지 않음. ④ I0-5: 이름 열 `SortMemberPath`, 정렬은 VM(`Services/EntrySortComparer.cs` —
  폴더 먼저, 폴더를 옮겨도 유지, 기본 이름순은 예전 순서), Styles.xaml 열 머리 템플릿에 열 폭 손잡이(PART_Left/RightHeaderGripper)·정렬 화살표 복구.
  계약 테스트 통과 714·건너뜀 6·전체 720, 실패 0(새 파일: CommandLineParser·ArchiveNaming·AppStartupContract·ShellExtensionContract·SourceFileOpener·
  CompressSkip·DropPolicy·Sorting Tests). 셸 메뉴·첫 실행·창 모양은 MANUAL_CHECKLIST 1·4·7절(사람 확인). `일집-dist` 는 그대로.
- **2026-10-12 W1-D 1차 검증 지적 반영(커밋 01a2897 에 포함, 배포 안 함)**: ① (회귀) 파일 이름까지 암호가 걸린 7z 를 우클릭 풀기로 못 풀던 것 — W1-D 가 '열기에 성공해야
  푼다'로 바꾸며 생김. I1-7 의 화면 쪽을 고쳐 `OpenArchiveAsync` 가 암호 예외면 암호를 묻고 다시 열고, 맞은 암호는 기억해 이어서 풀 때 다시 묻지 않음(W1-B 보관본과
  같은 `_archivePassword` 모양 — 열기·끌어 놓기·우클릭 모두). ② 읽는 도중 잠긴 파일(바이트 범위 잠금 — DB 등): 열 때 첫 블록을 읽어 보고, 그 뒤쪽에서 막히면
  그 파일을 뺀 채 처음부터 다시 씀(베이스 `RewriteWithoutUnreadable`, `SourceFileOpener.Read`). ③ 하나도 못 담으면 빈 zip·tar 를 만들지 않고 오류
  (`NothingToCompressException`, 압축 파일은 첫 파일을 연 뒤에 만듦 — `LazyArchiveOutput`). ④ 끌어 놓기: Drop 이벤트를 먼저 끝내고(`Dispatcher.Yield`) 선택 창,
  기본 버튼(Enter)은 [취소](`IUserInteraction.Choose` 에 기본 순번). ⑤ '"이름"\ 에 풀기'를 취소·손상으로 멈추면 파일 하나 없는 이름 폴더는 지움
  (`ArchiveNaming.RemoveFolderIfNoFiles`, `ExtractToFolderAsync` 가 `Task<bool>`). ⑥ 이름 정렬을 탐색기와 같은 자연 정렬(StrCmpLogicalW). ⑦ 암호 창은 [취소]할 때까지
  (예전엔 세 번째 암호를 써 보지 않고 '3회' 경고). C++ 셸 확장은 바꾸지 않음. 계약 테스트 통과 759·건너뜀 6·전체 765, 실패 0
  (표는 `Iljip.Tests/README.md` 'W1-D 1차 검증 지적으로 고친 것'). `일집-dist` 는 그대로.
- **2026-10-12 통합 점검(W1-A·W1-C·W1-D 를 합친 뒤, 배포 안 함)**: Release 빌드 경고 0·오류 0. 건 사이 상호작용을 가짜 창으로 한 표에 확인하는
  `Iljip.Tests/Contract/FormatFlowTests.cs`(암호 창 길·한글 이름·진행 막대를 ZIP·7z·TAR·EGG·ALZ·RAR4·RAR5 에 대해), 재사용장치 약속을 실제 exe 로 지키는
  `ReuseDeviceContractTests.cs`, RAR 을 처음으로 자동 시험하는 `RarTests.cs` + 합성 RAR 작성기 `TestSupport/RawRar.cs`(RAR4·RAR5, 저장 방식, 파일 암호·머리 암호·
  유니코드/CP949 이름 — 반디집 bz.exe 로 작성기 검증)를 더했다. 찾아서 고친 것: ① **RAR4 암호 파일을 암호 없이·틀린 암호로 풀면 묻지도 않고 암호화된 바이트(쓰레기)를
  '완료'로 씀**(SharpCompress 가 RAR4 는 암호 없이도 멈추지 않고, 암호 항목 CRC 를 안 봄) → 베이스가 암호 항목(크기 0 제외 — SharpCompress 는 암호 없는 7z 의 빈 파일도
  암호 항목으로 줌)에 암호가 없으면 풀기 전에 `PasswordRequiredException`, RAR4 는 풀린 내용 CRC 확인(`RarArchiveService.PrepareExtract`). ② RAR4 머리 암호의 틀린 암호가
  'Unknown Rar Header' 영어 오류 창(다시 묻지 않음), RAR 머리 암호 문구가 7z 와 다름 → `RarArchiveService.TranslateError` 가 7z 와 같은 머리 암호 예외(공용 `NeedsPasswordToList`).
  ③ 암호 없는 RAR 의 CRC 오류가 엔진 영어 문구 → `ChecksumMismatchException`. ④ RAR5 유닉스 시각·gzip 머리 시각이 목록에 UTC 로(9시간 이르게) 보임 → 베이스 `AsLocalTime`.
  계약 테스트 통과 834·건너뜀 6·전체 840, 실패 0(Debug·Release·UTC 시간대). 예전 일집-dist 와 새 판을 합성 egg/alz/rar 로 나란히 돌려 본 비교는 위 '일집-dist 재배포' 절.
- **2026-10-13 W2-01 — 7z.dll 해제 엔진·여는 형식 확대(1차 W1-B 재작업, 커밋 전, 배포 안 함)**: 보관분 `iljip-wave1-W1-B-failed` 를 `git stash apply` 로 얹고
  (drop 안 함 — `git stash list` 에 그대로 있고, `git add` 금지라 인덱스의 UU 표시도 그대로 → 커밋·정리는 오케스트레이터 몫) 충돌은 main 의도로 풀었다
  (W1-C CRC·한글 암호·`ExtractionLedger`·PasswordConfirmed·Alternate, W1-D, 통합 점검의 RAR 암호 흐름 유지 — 보관분의 1차 W1-B 이력 문단은 사실만 이 문단으로 옮김).
  SharpSevenZip 2.0.128 + 7z.dll 7-Zip 26.03 x64(빌드 출력 `x64\7z.dll`, SHA-256 65E4C1F8…4871B0D, 1,906,688바이트, 서명 없음 — 기록 테스트로 고정).
  해제는 `Services/SevenZipEngine.cs` 한 길: 열 때 형식별 구조 검사(`Services/ArchiveCompleteness.cs` — RAR4 ENDARC 규칙·RAR5 끝 머리·LZH 끝 표시 0·ISO
  PVD 볼륨 크기·UDF 끝 기준점·CAB cbCabinet·XZ 꼬리)로 목록에서도 잘림을 알리고, 대상 안 숨김 임시 폴더(`Services/StagingFolder.cs`, `.일집-임시-<8글자>`)에
  푼 뒤 항목마다 검증(`Services/ExtractionVerifier.cs` — 있음·크기, LZH 는 늘 CRC-16, 7z.dll 이 실패로 알린 항목은 CRC32 재계산으로 거짓 오류 가림)해
  다 맞을 때만 옮김(이름 겹치면 `(1)`, 잠김은 200ms×3 재시도, 옮기다 막히면 장부로 되돌림). 잘림·손상·암호 오류면 아무것도 옮기지 않고 이번에 만든 빈 대상
  폴더도 지움(단 한 파일의 CRC 오류는 main 의 ZIP·RAR 계약대로 그 앞에서 맞게 풀린 파일만 남기고 `ChecksumMismatchException`). 1차 검증에서 막힌 것:
  (a) 잘린 RAR·LZH·ZIPX 의 조용한 '완료'(RAR4 머리 사이 잘림 포함 — 판 29 이상 파일이 있는데 ENDARC 가 없으면 잘림), (b) 잘린 xz 의 CPU 100%·무한 대기 —
  SharpCompress `XZStream` 을 코드 어디서도 안 쓰고 xz 는 7z.dll 로만(스니퍼도 7z.dll 로 512바이트만, 취소는 출력 스트림에서 1초 안), (c) 압축된 tar(.tgz·.tar.gz·
  .tbz2·.tbz·.tar.bz2·.txz·.tar.xz, 안이 tar 인 .gz·.bz2·.xz 도 — `Services/CompressedTarArchiveService.cs` 새로 씀)는 임시 폴더에 `.inner.tar` → main TAR 처리기로
  펴고 모두 맞을 때만 옮김(전부 아니면 전무, 남은 공간 미리 확인, 목록·선택 풀기는 `%LOCALAPPDATA%\Iljip\temp\list` 캐시), (d) ISO 는 UDF 먼저·안 되면 ISO9660
  (UDF 만 이미지가 '손상'·Joliet 없는 다리 이미지의 8.3 이름이던 것. Joliet 없는 순수 ISO9660 은 원래 8.3 대문자 — 반디집·7-Zip 도 같음), (e) 잘린 .gz 는
  main 이 이미 고쳐 있었음(W1-A) — 모든 자리 절단 시험만 더함, (f) 헤더 암호 7z 의 우클릭 '여기에 풀기'·'"이름"\ 에 풀기' 회귀 없음. 그 밖에 새로 되는 것:
  RAR 여러 조각(빠진·덜 받은 조각 이름으로 알림), `.7z.001`·`.zip.001`, 자동 풀림 exe(열기 창 '자동 풀림 압축 파일 (*.exe)' 칸 — exe 는 절대 실행 안 함),
  ZIPX·RAR4 이름은 SharpCompress 날 바이트 + 한글 판별로 덮어씀. 형식 판별: 확장자 먼저, 내용(서명)은 일집 안(열기 창·파일 연결)에서만 —
  우클릭·끌어 놓기는 확장자만, 창 없이 풀기는 `ResolveForAutomation`(ZIP 기반 문서는 종료 코드 2). 7z.dll 을 못 불러오면 7z·RAR 는 main 의 SharpCompress 로,
  그 밖(zipx·iso·cab·lzh·wim·xz·txz)은 `EngineUnavailableException`(쉬운 한국어 — 앞서 캐시된 .tar.xz 목록도 캐시보다 부품을 먼저 봄). 매니페스트 우클릭 확장자 추가(.xz .tgz .tbz2 .tbz .txz .zipx .001 .iso .cab
  .lzh .lha .wim .egg .alz — 설치는 재배포 때). LGPL 고지 `Iljip/THIRD-PARTY-NOTICES.txt`·`Iljip/licenses/`·'일집 정보' 창(ⓘ 단추 — 일집 판, 7z.dll 판 또는 못
  불러온 까닭, SharpSevenZip 판, 라이선스 전문·라이선스 폴더 열기). 7z 만들기는 W2-02: `SevenZipArchiveService.CompressAsync` 는 main 대로 NotSupported,
  저장 형식에 7Z 없음, 보관분의 `SevenZipCreateTests` 와 화면 7z 만들기 시험은 `Iljip.Tests/_보류_W2-02/*.cs.txt` 로 옮김(새 Skip 금지라 컴파일에서 뺌 —
  W2-02 가 되살릴 것). KnownGap 의 txz·tbz2 기대 변경(반디집이 안쪽 .tar 하나만 보여 주던 갈래를 지움)은 약화가 아니라 강화: 과제의 명시 요구이고, 비교 대상이
  안쪽 tar 1개에서 펼친 tar 안 파일 전부(크기·SHA-256)로 늘었으며 '.tar 항목 없음' 단언을 더함(일집은 반디집보다 한 단계 더 펼침). 보관분 시험 중 '앞 파일은
  남긴다'(1차 W1-B)였던 잘림·취소·대상 쪽 오류 시험은 '전부 아니면 전무' 계약으로 바꿈, `--extract-here` 이름 폴더 시험은 main 의 I0-1 뜻대로
  `--extract-to-folder` 로. 엄격해진 곳(위험): LZH 끝 표시 0x00 을 요구해 옛 도구가 빠뜨린 정상 LZH 도 '잘림'(문구에 '다른 프로그램으로 확인해 보세요'),
  CAB·WIM·XZ 는 7z.dll 의 실패 보고를 그대로 믿음(확인값 재계산 없음 — 예전 보관분보다 엄격), RAR4 판 29 이상 + ENDARC 없음(-en)도 잘림, 7z.dll 은 LZH 등의
  이름을 OEM 코드 페이지로 읽어 시스템 지역이 한국어가 아니면 깨질 수 있음, 압축된 tar 는 임시 폴더에 inner.tar 와 풀린 내용이 함께 있어 디스크를 두 배 씀,
  대상이 OneDrive 폴더면 숨김 임시 폴더가 동기화·잠길 수 있음, 7z.dll 은 서명이 없어 다른 PC 의 AppLocker·WDAC·백신이 막을 수 있음(대신 경로로 7z·RAR 는 됨) —
  모두 MANUAL_CHECKLIST 9절. 7-Zip 26.04 의 미공개 수정은 26.03 에 없다(XZ 원격 실행 취약점 CVE-2026-14266 은 26.02 에서 고쳐져 26.03 에는 들어 있음 —
  26.04 패키지가 나오면 판 올림 검토). 7-Zip `License.txt` 원문 대조는 보류(위 '향후 작업'). 계약 테스트 실패 0(전체 1099 — 통과 1097·건너뜀 2: I2-1·I2-10, 계약만 `Kind!=Record` 944 — 통과 942·건너뜀 2, 기록 155. 통합 점검 후 840 — 통과 834·건너뜀 6 에서).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-02 — 7z 만들기(I2-1)·목록까지 잠긴 7z·RAR 열기(I1-7)(커밋 전, 배포 안 함)**: `SevenZipArchiveService.CompressAsync` 를 7z.dll 로 새로 씀 —
  원본은 ZIP·TAR 와 같은 `SourceCollector`(잠긴·없어진 원본·못 들어간 폴더는 건너뜀 안내, 하나도 못 담으면 `NothingToCompressException`, 만들 7z 자신·임시 파일 제외),
  파일은 `CompressStreamDictionary` + `LazySourceStream`(`Services/EngineStreams.cs` — 처음 읽을 때 열고 끝까지 읽으면 닫음, 읽기마다 취소 확인, 길이가 바뀌면
  '압축하는 동안 파일이 바뀌었어요'로 그 파일만 빼고 처음부터 다시 씀). 쓰기는 대상 옆 숨김 임시 파일 `.~이름.8글자.tmp` → 다 맞을 때만 `File.Move`(덮어쓰기는 시작할 때
  대상이 있었을 때만), 실패·취소면 임시 파일·임시 폴더를 지워 반쪽 파일이 없고 덮어쓰려던 기존 파일은 바이트 그대로. 7z.dll 이 콜백 예외를 결과 코드(0x8013153B)로만
  돌려주므로 토큰·원본 실패 기록·출력 쓰기 실패 기록으로 원인을 가름(`Classify`). 수준 저장만=None·빠름=Fast·보통=Normal·최대=Ultra, 추가 설정 `s`(솔리드, 기본 켬)·
  빠름 `mt;d=4m;c=4m`·보통 `mt;d=16m;c=8m`·최대 `mt=2;d=32m;c=16m`(mt=min(4, 논리 CPU)), 파일 이름 암호화(`he`)는 암호가 있을 때만. **실측(이 PC 논리 CPU 8·RAM 7.8GB,
  원본 160MB = 무작위 4×30MB + 글자 2×20MB, 보통)**: 19.9~25.0초(4회), 결과 124.9MB, 프로세스 전용 메모리 증가 204~205MB(최대 248~284MB, 한도 700MB), 진행률 100% 도달
  17.7/19.9초·22.2/25.0초, 30% 지점 취소 1.1~1.7초, 처음 바이트에서 취소 13~36ms(화면 명령 전체 41~90ms). **설정을 7-Zip 기본(`s` 만)으로 잠깐 되돌려 확인**: 메모리 증가
  1,167~1,169MB·100% 도달 0.2~0.3초/26~29초로 메모리·진행률 시험이 실패하고, '시작 3초·30% 뒤 취소' 시험도 '3초 뒤 이미 100%'로 실패(그 뒤 압축이 끝날 때까지 취소가 안 먹음)
  — 시험이 1차 지적을 잡아냄을 확인하고 되돌림(`SevenZipCreateLargeTests`, 테스트 출력 `기록_7z_보통_160MB`). **설계와 다르게 한 것**: 이름표 방식(`CompressStreamDictionary`)은
  폴더 항목을 못 받아(SharpSevenZip 2.0.128 — 폴더면 시각 속성에서 NullReferenceException, 디컴파일로 확인) 처음엔 파일 뒤에 빈 폴더만 덧붙였는데, 그러면 파일이 든 폴더가
  목록에 없어 `KnownGapTests.압축_7z_왕복이_원본과_같다`(모든 폴더를 비교 — 약화 금지)가 실패했다. 파일 뒤에 모든 폴더를 덧붙이면 큰 압축을 통째로 한 번 더 쓰고(취소 불가),
  그래서 **폴더가 있으면 폴더만 든 작은 7z 를 경로 방식으로 먼저(우리 `CancellableWriteStream` 으로) 만들고 파일을 덧붙인다**(`CompressionMode.Append` + 이름표 방식 —
  옮겨 담을 옛 데이터가 없음, 같은 암호·파일 이름 암호화). 이때 출력은 SharpSevenZip 이 대상 폴더 안 숨김 임시 폴더(`StagingFolder`, 같은 볼륨)에 직접 쓰고 우리 임시 파일
  자리로 옮김 — 그래서 폴더가 있는 압축은 출력 쓰기에서 취소를 못 보고(읽기에서 봄 — 원본을 다 읽은 뒤 남은 블록 압축 몇 초는 못 멈춤), 디스크 가득은 7z.dll 결과 코드
  (0x80070070) 문구로 알아봐 '디스크 공간이 모자라요'로 바꿈(실제 디스크 가득은 시험 못 함). 파일만 고르면 덧붙이기 없이 우리 스트림에 바로 씀. 모든 폴더가 항목이라
  일집 ZIP(빈 폴더만)과 목록 모양이 다르고(7-Zip·반디집 7z 와 같음, 폴더 시각 보존), 서로 다른 곳의 같은 이름 폴더 둘을 고르면 7-Zip 처럼 '같은 이름이 여러 개라…'로 거절(ZIP 은 합침).
  화면: 옵션 창 .7z 칸(솔리드 기본 켬·'파일 이름도 암호로 숨기기'는 암호가 있을 때만·ZIP 암호 방식 칸 숨김·한글 암호 경고 없음 — 7z 암호는 UTF-16 이라 반디집과 맞음,
  bz.exe 실측), 수준 글자 저장만·빠름·보통·최대(규칙은 `Views/CompressionOptionsRules.cs`), 저장 형식 7Z 는 7z.dll 을 쓸 수 있을 때만(`BuildSaveFilter`), 만든 7z 는 방금 넣은
  암호로 열어 다시 묻지 않음(`OpenArchiveAsync(path, knownPassword)`). 진행 창 문구 '압축하는 중' → '폴더 넣는 중' → 원본 이름 → '마무리하는 중'.
  I1-7: `PasswordRequiredException` 을 봉인 해제하고(공개 API 변화) 하위 형식 `ListPasswordRequiredException`(늘 머리 암호, 문구 '파일 목록까지 암호로 잠긴 압축 파일이에요.
  암호를 넣어 주세요.' — `HeaderMessage` 값도 이것으로 바뀜, 기존 멤버의 이름·시그니처는 그대로)과 `PasswordRequiredException.For` 공장, `PasswordPromptReason.ListLocked` 를 더함.
  판별은 머리 바이트(`ArchiveHeaderProbe` — 7z kEncodedHeader+AES, RAR5 머리 종류 4, RAR4 MAIN_HEAD 0x0080) 먼저, 그다음 7z.dll `EncryptedHeadersError`·SharpCompress 대신 경로.
  7z·RAR·자동 풀림 exe 안쪽 7z·RAR(7z.dll·대신 경로 둘 다) 모두 같은 예외. 화면(`ViewModels/MainViewModel.ListPassword.cs`)은 열 때 `ListLocked` 로 묻고, 틀리면
  '암호가 맞지 않아요. 다시 넣어 주세요.' 경고 뒤 다시, 취소면 보던 압축·목록·기억한 암호 그대로(오류 창 없음), 열 때 받은 암호로 풀면 다시 안 묻고 우클릭 풀기도 한 번만.
  창 없이 풀기(`--extract-to`)는 묻지 않고 종료 코드 1·'파일 목록까지…'(재사용장치 '압축 아님' 표지 없음 → 반디집 재시도 유지). 시험: `_보류_W2-02/*.cs.txt` 를
  `Contract/SevenZipCreateTests.cs`·`MainViewModelPasswordTests` 로 되살리고(보관분의 '잠긴 원본이면 실패'는 I0-2 건너뛰기 규칙으로, 블록 기록은 새 설정 표로, 큰 입력 시험은
  더 엄격한 `SevenZipCreateLargeTests` 로), `ListPasswordFlowTests`(일집 7z·py7zr 헤더 암호 7z 두 개 — `TestSupport/Py7zrSamples.cs` base64·머리 암호 RAR5·RAR4)·
  `CompressionOptionsRulesTests` 를 새로, `CompressSkipTests`·`ProgressTests`·`BandizipCliTests`(일집 7z 를 bz.exe 가 한글 암호로 풂)·`ReuseDeviceContractTests` 에 7z 행.
  `KnownGapTests.압축_7z_왕복이_원본과_같다` Skip 해제(`Known.I2_1` 삭제). 기대가 바뀐 기존 테스트(머리 암호 '암호 없음'의 정확한 형식이 하위 형식으로, 첫 창 이유 `ListLocked`,
  틀린 암호 뒤 경고 한 번, 저장 필터에 7Z, 막힌 PC 7z 만들기 `EngineUnavailableException`, RoundTrip 의 '.7z 미지원' 행 → '원본 없음' 행)는 약화가 아니라 새 동작을
  단언한 것 — 목록은 `Iljip.Tests/README.md` 'W2-02 에서 고친 것'. 위험: 덧붙이기(Append) 경로가 폴더 압축의 주 경로가 됨(헤더 암호·솔리드·한글 이름은 시험으로 확인),
  `EngineUnavailableException` 문구가 '여는 부품'이라 만들 때도 같은 말, 메모리·시간은 PC 마다 다름(시험 한도 700MB·취소 3초는 이 PC 기준), 다른 프로그램의 -mhe 7z 는
  py7zr 로만·WinRAR 머리 암호 RAR 은 합성으로만 확인(MANUAL_CHECKLIST 10절). 계약 테스트 실패 0(전체 1201 — 통과 1200·건너뜀 1: I2-10, 계약만 1044 — 통과 1043·건너뜀 1,
  기록 157. W2-01 후 1099 — 통과 1097·건너뜀 2 에서). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-03 — 나눠 만들기(분할 압축, I2-2 만들기)(커밋 전, 배포 안 함)**: `CompressionOptions.SplitBytes`(null = 안 나눔)를 ZIP·7Z 서비스가 받으면
  `Services/SplitArchiveOutput.cs` 가 대상 폴더 안 숨김 임시 폴더(`.일집-임시-8글자`)에 보통 압축 하나를 다 만든 뒤(형식별 암호·건너뛰기·진행률·취소 그대로) 조각으로 바꾸고,
  다 맞을 때만 대상 폴더로 옮긴다(실패·취소면 임시 폴더째 지움 — 조각·반쪽 파일 없음). 다 만든 것이 조각 크기 이하면 나누지 않고 고른 이름의 보통 파일(덮어쓰기는 예전과 같음).
  같은 이름 조각이 하나라도 있으면 세트 전체를 '이름 (2)'(세트 단위, 있던 세트는 바이트 그대로, 7Z 는 다음 번호 조각도 봄 — 이어 읽다 남의 조각이 붙지 않게).
  `CompressResult.Volumes`(조각 경로)·`OpenPath`(열 조각 — ZIP 은 .zip, 7Z 는 .001)를 더함. **7Z**: `VolumeSplitter` 가 7z 를 `이름.7z.001 …` 로 단순 절단(7z 볼륨 = 단순 절단,
  `SplitVolumeStream` 이 그렇게 읽음). SharpSevenZip 의 `VolumeSize` 는 새로 만들기 + 경로 출력에서만 되고(디컴파일: `GetOutStream` 이 `CompressionMode != Create` 면 볼륨을 안 씀,
  이름표 방식은 `_compressingFilesOnDisk` 가 아니라 볼륨 없음) W2-02 의 폴더 덧붙이기(Append)와 같이 못 써서, 설계가 허락한 '단일 7z 를 만들고 자르기' 한 길로 통일.
  뒤에서부터 잘라 내며(`SetLength`) 조각을 만들어 디스크는 원래 크기 + 조각 하나만 더 쓰고 첫 조각은 이름만 바꿈. 999조각까지. **ZIP — 방식 결정: PKZIP 나눔(`이름.z01 … 이름.zip`)만**
  (설계 근거 그대로: 반디집 7.46 기본값과 같고 반디집·7-Zip·WinRAR·WinZip 이 읽음, `.zip.001` 단순 절단은 받는 사람이 헷갈리고 Windows 는 어느 쪽도 못 엶 — 반디집 `bz.exe t` 는
  `.z01` 픽스처·`.zip.001` 픽스처 모두 0, 7z.dll 26.03 도 둘 다 Check=true 실측). `Services/ZipSpanWriter.cs` 가 다 만든 ZIP 을 다시 압축하지 않고 바꿈: 첫 조각 앞 `50 4B 07 08`
  (모든 위치 +4), 자를 자리가 로컬 머리(30 + 이름 + 추가 필드) 안이면 머리 시작으로 당김(머리는 한 조각 안 — 데이터·서술자는 넘어가도 됨), 중앙 디렉터리 기록마다 시작 디스크·
  조각 안 위치로 고쳐 씀(ZIP64 추가 필드는 다시 만듦, AES 0x9901 등 나머지는 그대로 — `Services/ZipDirectory.cs`), 끝 머리(디스크 = 마지막, 목록 시작 디스크·위치, 이 디스크
  항목 수, 전체, 크기 — 65,535개 이상이면 ZIP64 끝 머리·위치 표도). **목록(중앙 디렉터리) 놓는 자리 실측**(작은 파일 2,000개 ZIP 300KB → 4KB 조각, 이 PC):
  ① 목록 전체를 마지막 조각에(마지막 조각 150KB 로 커짐) — 7z.dll·반디집 둘 다 통과 ② 기록 경계에서 조각을 넘김(마지막 조각에 기록 49개 + 끝 머리) — 둘 다 통과
  ③ 끝 머리만 든 마지막 조각 — 7z.dll 은 통과, **반디집은 'Cannot open archive(0xa0000061)'(종료 코드 2)**. 그래서 목록 + 끝 머리가 마지막 데이터 조각에 들어가면 거기,
  한 조각에 들어가면 새 조각 하나에(반디집처럼 목록이 마지막 조각에만 — SharpCompress 도 바로 읽음), 그보다 크면 기록 경계에서 넘기되 마지막 기록은 끝 머리와 같은 조각에.
  99조각을 넘으면 '조각이 너무 많아요(99개까지). 나눌 크기를 키워 주세요.'(`TooManyVolumesException`, 7Z 는 999개), 머리·기록 한 줄이 한 조각보다 크면
  `SplitSizeTooSmallException`. 원형 `scratchpad\span_conv.py` 와 같은 결과를 C# 으로 — 반디집 `bz.exe t`·7z.dll·일집이 모두 읽음(시험). **나뉜 ZIP 읽기를 바꿈**:
  SharpCompress 0.50.5 는 나눔 ZIP 의 목록을 마지막 조각(.zip) 안에서만 읽어(디컴파일 `ZipArchive.LoadEntries`) 목록이 조각을 넘으면 못 읽고, 암호 판정(`ZipPasswordProbe`)·
  AES 직접 해제는 마지막 조각만 봐 '판정 못 함'이라 일집이 만든 한글 ZipCrypto 암호(CP949) 나눔 ZIP 을 맞는 암호로도 못 풀었다 → `Services/SpannedZipStream.cs` 가 조각을
  이어(나눔 표시 4바이트 뺌) 목록·끝 머리를 디스크 0·이은 위치로 고쳐 쓴 '보통 ZIP 한 개'를 보여 주고, `ZipArchiveService.OpenRawStream`·`OpenForRead` 가 숫자 분할처럼 그것을 씀
  (반디집 `zip_split` 픽스처도 이 길로 — 기존 시험 그대로 통과). 끝 머리의 '이 디스크 번호'(ZIP64 면 위치 표의 디스크 수)로 마지막 조각을 알아보고 앞 조각이 없으면 그 이름으로
  `VolumeMissingException`(풀기 전에 확인해 대상 폴더도 안 생김 — `.zip.001` 도 같이). 화면: 옵션 창 ZIP·7Z 에 '나누기 (메일로 보낼 때)' 칸(나누지 않음·10MB·20MB (메일 첨부용)·
  직접 입력 MB — 1MB 미만이면 '1MB 이상으로 넣어 주세요.', 4095MB 까지, 규칙은 `CompressionOptionsRules`, TAR·GZ·BZ2 는 숨기고 값도 버림), 안내 '메일 첨부 한도보다 작게'
  (첨부는 약 1.37배 커짐 — 정확한 한도는 말하지 않음)·'Windows 탐색기로는 열리지 않아요'. 끝나면 '3개 조각으로 나눠 만들었어요(이름.z01 … 이름.zip). 받는 사람은 모든 조각을
  같은 폴더에 두고 이름.zip을 열면 돼요.' + 탐색기 한 줄(`ViewModels/MainViewModel.Split.cs`), 그 세트를 엶. 진행 창은 압축 뒤 '조각으로 나누는 중', 끝 보고는 맨 끝 한 번.
  덤: ZIP 을 원본 폴더 안에 저장할 때 자기 자신을 진행률 총수에서도 뺌(끝에 N-1/N 으로 보이던 것). 시험: `Contract/SplitCreateTests.cs`(새 계약 — 7Z 64KB 조각,
  ZIP 픽스처 9개 × 무작위 1,000~5,000바이트 속성 검사, 서비스 ZIP 암호 5가지, 목록이 조각을 넘는 2,000개, ZIP64 7만 개, 거절, 빠진 .z02·.7z.002·.part2.rar, 취소 4가지,
  '이름 (2)', 한 조각, 진행률, 화면·옵션 창 — 반디집은 `BandizipCliFact`/새 `BandizipCliTheory`), `ZipPasswordProbeTests`(나눔 ZIP 판정 — 'split_last → Unknown' 행은
  '앞 조각 없음 → Unknown' 으로 바꾸고 새 동작은 따로 단언, 약화 아님), `LocatorTests` 문구 검사에 새 예외. `Known.I2_2` 삭제(쓰는 곳 없음). 설계와 다르게 한 것·주의:
  설계의 'AES·ZipCrypto 한글 암호에서 bz.exe t -p 통과'는 **ZipCrypto 한글·영문 AES 만** 맞다 — 일집 AES + 한글 암호는 나누지 않아도 반디집이 못 연다(SharpZipLib 가 UTF-8 로만 씀,
  기존 기록 `BandizipCliTests.기록_일집_AES_한글암호는_반디집이_못_푼다`). 대신 반디집이 만든 한글 AES·ZipCrypto 픽스처를 나눈 것은 반디집이 연다(시험). 7z.dll 확인도 한글 ZipCrypto
  암호는 빼고(7-Zip 이 암호 바이트를 다르게 바꿀 수 있음) 영문·암호 없음만. 99조각 초과는 압축을 다 한 뒤에야 알 수 있다(압축률을 미리 모름 — 큰 폴더를 아주 작게 나누면
  기다린 뒤 거절). 위험: 직접 짠 이진 처리(데이터 서술자·ZIP64·주석·추가 필드 길이·AES 0x9901 — 세 판독기 + 시험 쪽 독립 판독기로 막음), 알집·Windows 탐색기·옛 프로그램은
  실기기 확인 필요(MANUAL_CHECKLIST 11절), 디스크는 원래 압축 + 조각 하나만큼 더 씀, 작은 조각이 많을 때 PCFILTER 잠금·OneDrive 가 조각을 하나씩 올림.
  계약 테스트 실패 0(전체 1305 — 통과 1304·건너뜀 1: I2-10, 계약만 1148 — 통과 1147·건너뜀 1, 기록 157. W2-02 후 1201 — 통과 1200·건너뜀 1 에서; 새 시험 중 오래 걸리는 것은
  ZIP64 7만 개 10초·목록이 조각을 넘는 2,000개 8초·같은 모양 반디집 5초). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-05 — 같은 이름 묻기(I1-5)·메인 창 없는 우클릭 풀기(I1-6)·풀고 나서 지우기(I2-5)·여러 개 풀기(I2-6)(커밋 전, 배포 안 함)**:
  ① 같은 이름: `Services/CollisionResolver.cs` — 화면이 해제 한 번(여러 개 풀기는 압축 하나)을 `ExtractionScope.Begin(처리기)` 로 감싸면 AsyncLocal 로 서비스 안 Task.Run 까지 따라가
  모든 쓰기 길목(`OutputFiles.OpenTarget` — SharpCompress 직접 쓰기·EGG/ALZ·raw bz2, `OutputFiles.MoveResolved` — 7z.dll·압축된 tar 의 임시 폴더 옮기기)이 묻는다.
  **설계와 다르게 한 것**: 처리기를 `ExtractPlan`·서비스 시그니처로 넘기지 않고 이 범위로(IArchiveService 시그니처 그대로, 범위가 없으면 — 창 없이 풀기·재사용장치·서비스를 바로 부르는
  시험 — 예전대로 '이름 (1)' 이라 헤드리스 동작이 바뀔 길이 없음). 덮어쓰기는 같은 폴더 숨김 `.~이름.8글자.tmp` 에 다 쓴 뒤 `File.Move(덮어쓰기)`(잠김 200ms×3 — 실패면 임시 파일 지우고
  "‘이름’ 파일을 덮어쓰지 못했어요. 다른 프로그램(보안 프로그램 포함)이 쓰고 있거나 읽기 전용이에요…", 원래 파일 그대로), 덮어쓴 경로는 `ExtractionLedger` 에 넣지 않아 뒤에서 되돌려도
  사용자 파일을 안 지움. [취소]는 `CollisionCancelledException`(OperationCanceledException — 화면은 '취소됨', 오류 창 없음) → 서비스가 이번에 만든 것만 되돌림. 이번 해제가 만든 파일·폴더와
  겹치면(압축 안 같은 이름 둘·대소문자) 묻지 않고 '(1)'. '남은 파일도 똑같이'는 처리기가 기억([취소]는 기억 안 함), 설정 extract.onCollision(물어보기·덮어쓰기·건너뛰기·이름 바꾸기)이
  '물어보기'가 아니면 묻지 않음. 창은 `Views/CollisionDialog`(기본 버튼 [이름 바꾸기], X·Esc = 취소, 해제 작업 스레드에서 `Dispatcher.Invoke`). ② 설정: `Services/AppSettings.cs`
  (`%LOCALAPPDATA%\Iljip\settings.json`, camelCase, schemaVersion 1, 모르는 필드 보존, 깨지면 `settings.bad-….json` 으로 옮기고 기본값, .tmp → Replace)·`AppServices`(셸·설정·휴지통 —
  `MainViewModel` 생성자 선택 인자, null 이면 진짜)·`IShellLauncher` 는 **W2-04 보관본과 같은 모양으로 여기서 처음 들임**. ☰ ▸ '설정...' → `Views/SettingsDialog`(사본을 고쳐 [확인] 때 한 번 저장).
  ③ 우클릭 풀기(I1-6): `ParsedCommand.ShowMainWindow` 가 false 인 '여기에 풀기'·'"이름"\ 에 풀기'·대화형 --extract-to 는 App 이 창(VM)만 만들고 띄우지 않은 채 `ProcessShellCommandAsync`
  (`ViewModels/MainViewModel.CommandLine.cs` — ProcessCommandLineAsync 도 이리로 옮김) → 진행 창 하나, 보던 압축 그대로, 끝나면 '이름' 폴더를 탐색기로(extract.openFolderAfter, '여기에 풀기'는 안 엶,
  여럿이면 같은 위 폴더 한 번), 오류는 쉬운 한국어 창 하나 — 모르는 엔진 오류(SharpCompress 'Failed to locate the Zip Header' 등)는 새 `ErrorMessages.ToPlainMessage`
  ('파일이 손상됐거나 끝까지 받아지지 않은 것 같아요…' — 메인 창 오류 창·창 없이 풀기는 예전 `ToUserMessage` 그대로), 없는 파일이면 알림. 주인 창 없는 창은 화면 가운데·작업 표시줄·잠깐 맨 위.
  앱 수명: `ShutdownMode.OnExplicitShutdown` + 할 일 수(`_busy`) — 메인 창을 띄웠으면 닫을 때, 아니면 우클릭 풀기가 끝나고 0.7초 기다려(늦게 온 명령이 묶음에 붙게) 끝냄.
  파이프는 `InOut` + '받았음' 1바이트(받는 쪽은 넘긴 쪽이 읽어 간 뒤 `WaitForPipeDrain` 으로 확인하고서만 실행 — 끝나는 중이면 0, 넘긴 쪽은 3초 기다려 첫 일집이 되거나 혼자 처리, 예전 판의
  줄 끝 CRLF 도 받음), 두 번째 실행의 우클릭 풀기는 메인 창을 앞으로 안 가져옴. ④ 풀고 나서 지우기(I2-5): extract.deleteArchiveAfter(기본 끔)면 오류 0·건너뛴 파일 0·취소 아님일 때만
  `Services/IRecycleBin.cs` 의 `RecycleBin`(IFileOperation, FOFX_RECYCLEONDELETE|FOF_ALLOWUNDO|FOF_NOCONFIRMATION|FOF_SILENT|FOF_NOERRORUI, STA)으로 — 나뉜 압축은 조각 모두 한 번에
  (`Services/ArchiveVolumes.cs` — .001…·.z01…+.zip·.partN.rar/.r00·.volN.egg, 모르면 그 파일 하나). 미리 거름: 공유(\\서버)·고정 디스크가 아님·NoRecycleFiles 정책·그 볼륨 휴지통 꺼짐
  (BitBucket NukeOnDelete)·한도(MaxCapacity) 초과면 안 지우고 "‘이름’은 휴지통이 없는 곳(네트워크 드라이브·USB 등)에 있어 지우지 않고 그대로 두었어요.". **덧붙인 안전망**: FOF_WANTNUKEWARNING
  (그래도 Windows 가 영구 삭제로 바꾸려 하면 묻게). 일집 임시 폴더 안의 압축·고른 항목만 풀기·창 없이 풀기는 안 지움, 못 보내면 그대로 두고 알림. ⑤ 여러 개 풀기(I2-6):
  `Services/BatchExtractor.cs` — 진행 창 하나에 '2/5 · b.zip' + 전체 막대(`ArchiveProgress.BatchIndex/BatchCount/BatchName/OverallPercentage`), 맞은 암호(최근 3개, 끝나면 지움)를 다음 파일에
  조용히 먼저 써 보고 모두 틀리면 그 파일만 '앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.', 취소면 그 파일만 건너뜀, 손상 등은 그 파일만 실패, 진행 창·같은 이름 [취소]는 남은 것을
  손대지 않고 멈춤, 끝에 `Views/SummaryDialog`(성공·실패·이유, 날 문구는 '자세히' — `Models/OperationSummary.cs`, 우클릭으로 모두 풀고 폴더를 열었으면 안 띄움). 들어오는 길: 명령줄 풀기 동사 뒤 압축
  여러 개(`ParsedCommand.Archives`), 일집 열기 창 여러 개 선택 → '모두 풀까요?' → 풀 폴더 안 '이름' 폴더마다, **C++ 셸 확장은 바꾸지 않아 여전히 파일마다 일집을 띄우지만** 같은 동사로 들어온 우클릭
  명령은 돌던 묶음에 붙음(`TryAdd`). 시험: 새 `ShellExtractFlowTests`·`BatchExtractTests`·`RecycleAfterExtractTests`·`ExtractSettingsTests`, `CollisionTests`(zip·7z·rar·tar·egg 덮어쓰기·건너뛰기·
  이름 바꾸기·취소·한 번만 묻기·잠긴 파일·압축 안 같은 이름·bz2)·`CommandLineParserTests`·`AppStartupContractTests`(소스)·`ReuseDeviceContractTests`(실제 exe 로 같은 폴더 두 번 → 묻지 않고 '(1)')에 덧붙임,
  가짜 `FakeUserInteraction.AskCollision/ShowSummary`(답이 없으면 '이름 바꾸기' — 예전 시험 그대로 통과)·`TestSupport/FakeServices.cs`(셸·휴지통·설정 — 진짜는 안 부름). `ModuleInit` 이 일집 자료 폴더를
  시험 전용 빈 폴더로 돌림(가짜 없이 만든 화면도 이 PC 사용자의 settings.json 을 안 읽어, 사용자가 '휴지통으로'를 켜 두어도 시험이 픽스처를 진짜 휴지통으로 보내지 않게). 기대가 바뀐 기존 시험 없음,
  푼 Skip 없음. 위험·못 본 것: 창 모양·창이 뒤에 숨는지·진짜 휴지통(IFileOperation vtable)·네트워크 드라이브·여러 개 골라 우클릭 묶임·0.7초 끝내기 시점은 사람 확인(MANUAL_CHECKLIST 12절),
  새 판 클라이언트가 예전 판(받기만 하는 파이프)이 떠 있을 때는 넘기지 못해 3초 기다린 뒤 혼자 처리, 우클릭 풀기의 같은 이름 창·암호 창은 주인 창이 없어 다른 창 뒤로 갈 수 있음(맨 위로 잠깐 올림).
  계약 테스트: 전체 1424 — 통과 1422·건너뜀 1(I2-10)·**실패 1**(W2-03 후 1305 — 통과 1304·건너뜀 1 에서 새 시험 119개, 기대가 바뀐 기존 시험 없음). 실패 1개는 W2-02 의 `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_멈추고_기존_파일이_그대로다`(7z 만들기를 30% 지점에서 취소하면 3초 안 — 이번에 안 건드린 압축 길): 이 PC 가 바쁠 때(빈 메모리 약 0.4GB/8GB, PFDlpMon) 전체 실행에서 3.2~5.0초로 실패하고 따로 돌리면 2.0~2.7초로 통과(3/3). **바꾸기 전 HEAD(W2-03 커밋)를 임시 폴더에 받아 따로 빌드해 같은 PC 에서 전체를 돌려도 같은 시험만 3.2초로 실패**해 이번 변경 탓이 아님을 확인했다(한도는 약화 금지라 그대로 — PC 가 한가할 때 다시 돌려 볼 것).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).

## 사용자 선호

- 한국어, 사근사근한 톤
- 방향성 무조건 따르지 말고 조언할 게 있으면 먼저 물어볼 것
- 코딩 검증 통과 + 질문 없으면 다음 단계 자동 진행
- 매번 칠 명령어는 코드 블록으로 정리해서 제공
