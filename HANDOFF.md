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
- 암호: ZIP(ZipCrypto·AES)·7z(이름 보임·파일 이름까지 암호)·RAR4·RAR5(파일 암호·머리 암호)·EGG 모두 같은 암호 창 길
  (없으면 '암호가 걸린…', 머리 암호면 '파일 이름까지…', 틀리면 '암호가 맞지 않아요…', [취소]할 때까지 다시 물음)
- 한글 파일명 CP949/UTF-8 자동 판별
- ZIP AES-256·ZipCrypto 암호 압축 / 해제 시 비밀번호 재시도 다이얼로그(반디집 한글 암호 ZIP 도 풀림, '암호 보이기')
- 압축 옵션 다이얼로그 (저장/빠름/보통/최대 4단계)
- 드래그앤드롭: 빈 창에 압축 파일 → 열기 / 일반 파일·폴더 → 스테이징 추가. 압축을 보는 중·모으는 중에 끌어 놓으면 선택 창(보던 압축을 말없이 닫지 않음)
- 스테이징 모드 (반디집 스타일: 파일 모았다가 "압축 실행" 버튼). 다른 프로그램이 잠근 파일은 건너뛰고 끝에 '건너뛴 파일 N개' 안내
- 목록 열 머리 클릭 정렬(폴더 먼저, 폴더를 옮겨도 유지, 이름은 탐색기와 같은 자연 정렬)·열 폭 끌기
- 파일 이름까지 암호가 걸린 7z 는 열 때 암호를 물음(받은 암호로 이어서 풂). 암호 창은 [취소]할 때까지 다시 물음
- CLI 인자: `--open`, `--extract`, `--extract-here`(그 자리에), `--extract-to-folder`(이름 폴더에, 있으면 '이름 (2)'), `--extract-to`, `--compress` — 해석은 `Services/CommandLineParser.cs`
- C++ 셸 확장 5개 메뉴: "일집으로 열기" / "여기에 풀기 (일집)" / "\"이름\"\ 에 풀기 (일집)" / "압축 풀기... (일집)" / "일집으로 압축"

## 현재 진행 단계 (2026-10-12 통합 점검 후)

**Wave 1(W1-A·W1-C·W1-D) 커밋·푸시 완료 + 통합 점검 끝**. W1-B(7z.dll — SharpSevenZip)는 검증에서 되돌려 `git stash`(`iljip-wave1-W1-B-failed`)에 있다.
계약 테스트 실패 0(통과 834·건너뜀 6·전체 840 — Debug·Release·`ILJIP_TEST_TZ=UTC` 모두). **`D:\Python\일집-dist`(재사용장치가 매일 egg 를 푸는 배포본)는 아직 예전 판**
(2026-06-23, SharpCompress 0.38.0·EggDotNet 0.3.0)이다 — 바꾸기 전에 아래 '일집-dist 재배포' 절과 `Iljip.Tests/MANUAL_CHECKLIST.md` 를 사람이 확인할 것.

**다음 단계**:
1. 사용자 실제 PC 확인(MANUAL_CHECKLIST 1~8절 — 특히 6절 진짜 알집·WinRAR 파일, 8절 재사용장치 경로)
2. 확인되면 일집-dist 재배포(아래 절차) → 재사용장치 다음 날 로그에서 '(Iljip으로 .egg 해제)' 뒤 'Bandizip 폴백'이 늘지 않았는지 확인
3. W1-B(7z.dll) 다시 시도 — 7z 만들기(I2-1)·숫자 분할(I2-2)·tar.gz 등 한 번에 펼치기(I2-9)

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
- [ ] W1-B 다시: 7z.dll(SharpSevenZip — `D:\Python\_엔진결정.md`)로 7z 만들기·숫자 분할·tar.gz 한 번에 펼치기 (stash `iljip-wave1-W1-B-failed`)
- [x] ALZ / EGG 지원 (EggDotNet 0.4.0, W1-A 에서 빈 항목·ALZ 한글 이름·잠김·내용 확인까지)
- [ ] .egg/.alz 우클릭 메뉴(매니페스트에 확장자 없음)
- [ ] 분할 압축
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

## 사용자 선호

- 한국어, 사근사근한 톤
- 방향성 무조건 따르지 말고 조언할 게 있으면 먼저 물어볼 것
- 코딩 검증 통과 + 질문 없으면 다음 단계 자동 진행
- 매번 칠 명령어는 코드 블록으로 정리해서 제공
