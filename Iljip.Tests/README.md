# Iljip.Tests — 일집 계약(회귀) 테스트

압축 엔진을 바꾸기 **전에** 일집의 '지금 동작'을 고정해 두는 테스트 묶음입니다(사전점검 ②).
엔진을 바꾼 뒤 이 테스트를 돌리면 무엇이 그대로이고 무엇이 달라졌는지 바로 보입니다.

- 대상: `Iljip/Services/IArchiveService.cs` 와 그 구현체(ZIP·7z·TAR·GZ·BZ2·RAR·EGG/ALZ, **W2-01 부터** 압축된 TAR·zipx/iso/cab/lzh/wim/xz·자동 풀림 exe),
  7z.dll 다리(`SevenZipEngine`·`SevenZipLibrary`·구조 검사 `ArchiveCompleteness`·검증 `ExtractionVerifier`·임시 폴더 `StagingFolder`),
  내용으로 형식 알아보기(`ArchiveSniffer`)·`ArchiveServiceLocator`, `KoreanFileNameDecoder`, `ThrottledArchiveProgress`,
  `MainViewModel` 의 명령줄 해석 일부, `Iljip.exe --extract-to`(창 없이 풀기).
  - RAR 은 반디집·이 PC 모두 만들 수 없어 픽스처 파일이 없습니다. **통합 점검(2026-10-12)부터** 테스트 안의 최소 작성기
    (`TestSupport/RawRar.cs` 의 `RawRar5`·`RawRar4` — 저장 방식, 파일 암호·머리 암호, 유니코드·CP949 이름)로 만든 합성 RAR 로 목록·해제·암호·손상·진행률을 시험합니다
    (`Contract/RarTests.cs`). 작성기가 맞는지는 반디집 콘솔 `bz.exe t` 로 확인합니다(`합성_RAR을_반디집도_맞게_푼다`).
    W2-01 부터 같은 파일의 `RawRar`(머리 경계 위치를 함께 주는 RAR5·RAR4, RAR5 여러 조각·머리 암호 — 1차 W1-B 때 파이썬 rarfile 로 교차 확인한 작성기)로
    잘림·빠진 조각·RAR 자동 풀림 exe 도 시험합니다(`TruncationTests`·`SfxTests`·`SevenZipLibraryTests`).
    **진짜 WinRAR 산출물(압축된 데이터·RAR5 의 HMAC CRC·BLAKE2·여러 조각)은 확인하지 못했으니** `MANUAL_CHECKLIST.md` 6절로 사람이 확인합니다.
    rar.exe 가 있는 PC 에서 **합성 데이터**로 만든 RAR 픽스처를 받으면 `Fixtures/` 와 manifest 에 추가하세요.
  - CAB 은 테스트 안 최소 작성기(`TestSupport/RawCab.cs`)로 시험하고(`CabTests`), WIM 은 픽스처가 없어 라우팅·막힌 PC 안내만 자동입니다(MANUAL_CHECKLIST 9절).
  - EGG/ALZ 는 테스트 안에서 최소 작성기(`TestSupport/RawArchives.cs` 의 `RawEgg`·`RawAlz`)로 만든 합성 파일로 시험합니다
    (`Contract/EggAlzTests.cs`). 진짜 알집 산출물로는 확인하지 못했으니 MANUAL_CHECKLIST 6절도 같이 보세요.
- 처음(사전점검 ②)에는 본체 코드를 고치지 않고 버그를 'Skip 목록'에 보고만 했습니다. 이후 **W1-A(2026-10-12)** 에서 SharpCompress 를
  0.38.0 → 0.50.5 로 올리고 B1·B2·B6·B7·B8·B9·B10·TAR-빈폴더를 고쳤습니다 — 근거 테스트는 `Contract/FixedBugTests.cs` 로 옮겨
  기대를 뒤집었고(다시 생기면 실패), 같은 번호의 Skip 은 지웠습니다.
  이어서 **W1-A 1차 검증 지적**(같은 날)으로 잘린·손상 .gz/.egg/.alz 를 '완료'로 끝내던 문제, 끝 구분자 없는 EGG/ALZ 폴더 항목,
  ALZ 한자·기호 이름·잘린 ALZ 이름, 점뿐인 .gz 이름, 서명 뒤가 쓰레기인 RAR5, 같은 폴더 동시 해제 충돌, 분할 EGG 형제 파일 전부 열기를 고쳤습니다
  (아래 '고친 버그' 표 아래쪽).
  **W1-C(같은 날)** 에서 암호·무결성 버그 B3(반디집 한글 암호)·B4(ZIP CRC 미검사)·B5(틀린 암호를 화면이 못 알아봄)와 빈틈 I2-11(ZipCrypto 선택)을
  고쳤습니다(아래 'W1-C 에서 고친 것', 그 1차 검증 지적도 같은 절). 이제 안 고친 버그(Bn Skip)는 없습니다. ※ W1-B(7z.dll) 는 검증에서 되돌려졌다가
  아래 W2-01 에서 다시 들어왔습니다.
  **W1-D(같은 날)** 에서 0등급 화면 문제 I0-1(우클릭 '여기에 풀기'·'"이름"\ 에 풀기' 분리 + 첫 실행 인자 버그)·I0-2(사용 중인 파일)·I0-4(끌어 놓기 선택 창)·
  I0-5(정렬·열 폭)를 고쳤습니다. 화면의 창 띄우기를 `IUserInteraction` 으로 빼서(W1-B 보관본과 같은 모양) 가짜 창으로 VM 흐름을 시험합니다(아래 'W1-D 에서 고친 것').
  **W1-D 1차 검증 지적(같은 날)** 으로 헤더 암호 7z 를 우클릭으로 못 풀던 회귀(I1-7 화면 쪽까지 고침), 읽는 도중 잠긴 파일, 빈 압축, 끌어 놓기 선택 창, 빈 이름 폴더,
  자연 정렬, 세 번째 암호 미시도를 고쳤습니다(아래 'W1-D 1차 검증 지적으로 고친 것').
  **통합 점검(같은 날, 세 건을 합친 뒤)** 에서 형식이 달라도 화면 흐름(암호 창·한글 이름·진행 막대)이 같은지 한 표로 보는 테스트와 재사용장치 약속 테스트,
  RAR 자동 시험을 더했고, RAR4 암호 파일을 묻지도 않고 쓰레기로 풀던 문제 등을 고쳤습니다(아래 '통합 점검에서 고친 것').
  **W2-01(2026-10-13)** 에서 1차 W1-B 보관분(stash)을 다시 얹어 7z.dll(7-Zip 26.03 x64, SharpSevenZip 2.0.128)로 '풀기' 쪽 형식을 늘렸습니다 —
  I2-2(숫자 분할 '열기')·I2-9(zipx·tgz·tar.gz·txz·tbz2·xz·iso·lzh·SFX)의 Skip 을 지웠고, 1차 검증에서 막힌 (a)~(f)(잘린 RAR·LZH·ZIPX 의 조용한 '완료',
  잘린 xz 의 끝없는 대기, 손상된 압축 tar 의 반쪽 파일, UDF ISO, 잘린 .gz, 헤더 암호 7z 우클릭)를 새 계약 테스트로 지킵니다(아래 'W2-01 에서 고친 것').
  7z 만들기(I2-1)는 W2-02 몫이라 그대로 Skip 이고, 보관분의 7z 만들기 시험은 `_보류_W2-02/` 에 `.cs.txt` 로 빼 두었습니다(새 Skip 금지).
  **W2-02(2026-10-13)** 에서 7z 만들기(I2-1 — 수준·솔리드·AES-256·파일 이름 암호화, 1차 W1-B 검증 지적의 진행률·취소·메모리)와 I1-7 의 남은 몫
  (목록까지 잠긴 7z·RAR 전용 예외 `ListPasswordRequiredException`·열 때 '암호가 맞지 않아요' 경고)을 메워 I2-1 Skip 을 풀고, `_보류_W2-02/` 시험을
  `Contract/SevenZipCreateTests.cs`·`MainViewModelPasswordTests` 로 되살렸습니다(아래 'W2-02 에서 고친 것').
  **W2-03(2026-10-13)** 에서 I2-2 의 남은 몫 '나눠 만들기'(분할 압축 — ZIP 은 PKZIP 나눔 `이름.z01 … 이름.zip`, 7Z 는 `이름.7z.001 …`, 옵션 창 '나누기' 칸)를
  새 계약 `Contract/SplitCreateTests.cs` 로 지키고, 쓰는 곳이 없어진 `Known.I2_2` 를 지웠습니다(아래 'W2-03 에서 고친 것').
  **W2-04(2026-10-13)** 에서 압축 속 파일 바로 열기(I1-3)·끌어 꺼내기·Ctrl+C(I1-4)·그림 미리 보기(I3-7)를 더하고, 셸·클립보드·그림 읽기·시계·설정을 가짜로 바꿔 끼우는
  `AppServices` 로 화면 흐름을 창 없이 시험합니다(`EntryWorkspaceTests`·`EntryOpenTests`·`DragOutTests`·`ClipboardCopyTests`·`ImagePreviewTests` — 아래 'W2-04 에서 더한 것').
  진짜 탐색기와의 끌어 놓기·붙여넣기·미리 보기 화면은 `MANUAL_CHECKLIST.md` 12절(실기기 확인 필요).

## 실행 방법

```powershell
dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj
```

- 솔루션(`Iljip.sln`) 전체를 `dotnet test` 로 돌리면 C++ 셸 확장 때문에 실패하므로 **csproj 를 직접** 지정합니다.
- 처음 한 번은 NuGet(xunit 등)을 내려받습니다. 그다음부터는 빌드 포함 약 1분, 테스트만 30~60초.
- 결과(2026-10-18 4차 통합 점검 후 — Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번 모두): `통과!  - 실패: 0, 통과: 2411, 건너뜀: 0, 전체: 2411` — 남은 Skip 없음(3차 통합 점검 후 2408, IJ3-03 후 2394, IJ3-01 후 2345). 반디집·픽스처가 없는 PC 에서는 아래처럼 환경 조건으로 건너뛰는 것이 생깁니다.
  (Wave 2 통합 점검 후 — Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번 모두: 통과 2175·건너뜀 0·전체 2175, W2-12 후: 통과 2105·건너뜀 0·전체 2105, W2-11 후: 통과 2041·건너뜀 0·전체 2041, W2-10 검증 지적 수정 2차 후: 통과 1937·건너뜀 0·전체 1937, W2-10 검증 지적 수정 1차 후: 통과 1919·건너뜀 0·전체 1919, W2-10 후: 통과 1900·건너뜀 0·전체 1900, W2-09 검증 지적 수정 1차 후: 통과 1843·건너뜀 0·전체 1843, W2-09 후: 통과 1816·건너뜀 0·전체 1816, W2-07 검증 지적 2차 후: 통과 1746·건너뜀 1·전체 1747, W2-06 검증 지적 2차 후: 통과 1643·건너뜀 1·전체 1644, W2-06 검증 지적 1차 후: 통과 1614·건너뜀 1·전체 1615, W2-06 후: 통과 1587·건너뜀 1·전체 1588, W2-05 후: 통과 1423·건너뜀 1·전체 1424, W2-03 후: 통과 1304·건너뜀 1·전체 1305, W2-02 후: 통과 1200·건너뜀 1·전체 1201)
  (W1-A 전: 통과 394·건너뜀 20·전체 414 → W1-A: 통과 430·건너뜀 10·전체 440 → W1-A 1차 검증: 통과 478·건너뜀 10·전체 488 → W1-C: 통과 578·건너뜀 6·전체 584
  → W1-D: 통과 714·건너뜀 6·전체 720 → W1-D 1차 검증: 통과 759·건너뜀 6·전체 765 → 통합 점검: 통과 834·건너뜀 6·전체 840
  → W2-01: 통과 1097·건너뜀 2·전체 1099)
  반디집 콘솔(`C:\Program Files\Bandizip\bz.exe`)이 없는 PC 에서는 `BandizipCliTests` 3개와 `RarTests.합성_RAR을_반디집도_맞게_푼다` 1개, (W2-03) `SplitCreateTests` 의 반디집 시험(나눔 ZIP·7z 조각 — Theory 행 포함 32개)이, (W2-06) `ArchiveEditTests.고친_압축을_반디집이_푼다`(12개)가, 로컬 SFX 픽스처가 없으면 SFX 테스트가 더 건너뜁니다.
  빌드된 `Iljip.exe` 를 못 찾으면 실제 exe 를 띄우는 테스트(`CommandLineTests.실행파일_…`, `ReuseDeviceContractTests` 7개 — IJ3-03 의 '인터넷 표시를 붙이지 않는다'·3차 통합 점검의 '실패하면 오류 로그에 압축 경로' 포함)가 건너뜁니다.
  (3차 통합 점검) `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_…` 는 이 PC 의 진짜 일집 오류 로그(`%LOCALAPPDATA%\Iljip\logs\error-날짜.log`)를 **읽기만** 합니다(exe 가 거기에 씀 — 시험 기록은 경로에 `IljipTests`).
  → **(4차 통합 점검) 시험 로그 격리**: 이제 시험은 사용자 진짜 로그에 쓰지도 읽지도 않습니다. 시험 프로세스의 일집 자료 폴더는 `%TEMP%\IljipTests\appdata-<pid>`(로그는 그 아래 `logs\` — `Logger.LogDirectory = AppPaths.Root\logs`),
  시험이 띄우는 Iljip.exe 는 환경 변수 `ILJIP_TEST_APPDATA`(`ModuleInit.ApplyTo` — **새 시험이 exe 를 띄울 때도 꼭 거칠 것**, `Wave4IntegrationTests` 가 소스를 훑어 막음)로 같은 폴더를 받습니다.
  끝나면 그 폴더를 지우고, testhost 가 정리를 못 하고 끝난 폴더는 10분 뒤 다음 시험이 시작할 때 치웁니다(그 pid 의 시험이 돌고 있으면 둠).
  (IJ3-03) `ThirdPartyLicenseTests.발행_출력에도_…` 는 `dotnet publish --no-build` 를 시험 임시 폴더에 한 번 돌립니다(약 3초, NuGet·인터넷 없음 — `dotnet` 이 PATH 에 있어야 함.
  3차 통합 점검부터 보안 프로그램이 obj 파일을 잠깐 잡은 'MSB3491' 일 때만 1초 뒤 두 번까지 다시 발행).
  (W2-01) Windows 의 IMAPI2FS(디스크 이미지 만들기)가 없는 PC 에서는 `DiscImageTests` 가 환경 조건(`DiscImageFact`)으로 건너뜁니다.
  (W2-10) 회사 보안 정책(AppLocker·WDAC 등)이 임시 폴더의 exe 실행을 막는 PC 에서는 만든 자동 풀림 exe 를 무인 모드로 돌려 보는 시험(`SfxCreateTests` 의 `SfxStubFact`·`SfxStubTheory`, `SfxCompressFlowTests.앱에서_만든_exe를_무인으로_풀면_원본과_같다`)이
  환경 조건으로 건너뜁니다(스텁을 한 번 실행해 보고 Windows 가 실행 자체를 거절한 Win32Exception 일 때만 — 스텁이 아예 없거나, 파일이 망가져 exe 가 아니거나(193 등),
  실행은 됐는데 바로 죽거나 종료 코드가 다르면 건너뛰지 않고 실패. W2-10 검증 지적 1차에 좁힘 — 예전엔 종료 코드가 1 이 아니기만 해도 건너뛰었음).
  빌드된 셸 확장 DLL(`D:\Python\일집-build\Release\x64\Iljip.ShellExtension\Iljip.ShellExtension.dll`)이 없거나 C++ 소스(`.cpp` 넷·`dllmain.cpp`·`IljipCommands.h`·`pch.h`)보다 오래되면
  `ShellExtensionContractTests` 의 `DLL_…` 4개(W2-12 부터 — 옛 메뉴 제목·'일집 ▶' 하위 메뉴·오른쪽 끌기 메뉴·드롭 처리기)가 더 건너뜁니다(빌드 명령은 `HANDOFF.md` 'C++ 셸 확장 DLL 만 빌드' — C++ 를 고치면 다시 빌드).
- 느린 축: 파일명 보이는 암호 7z(py7zr, 키 유도 2^19회)를 쓰는 테스트, 32 MiB 취소 테스트, 실제 `Iljip.exe`·반디집 `bz.exe` 를 띄우는 테스트(각 1~10초),
  (W2-01) 잘림 전수 시험(`TruncationTests`·`XzTests`·`CabTests` — 구조 검사가 7z.dll 보다 먼저 막아 각 몇 초), 24 MiB xz 를 만드는 취소 시험,
  (W2-02) 160MB 원본으로 7z 진행률·메모리·취소를 재는 `SevenZipCreateLargeTests`(원본 만들기 몇 초 + 보통 수준 압축 20~25초 + 취소 시험 5초 —
  메모리·시간을 재므로 `DisableParallelization` 묶음이라 다른 테스트가 다 끝난 뒤 따로 돈다. %TEMP% 에 160MB 를 잠깐 씀),
  (W2-06) 압축 고치기 `ArchiveEditTests`(110개 — 고칠 때마다 다시 읽어 검사하고 풀어 SHA 를 비교, 반디집 `bz t` 포함). `MainWindowEditUiTests` 는 WPF 앱 객체를 잠깐 만들므로 `DisableParallelization` 묶음.
  (W2-06 검증 지적) 16MB 솔리드 7z 지우기의 진행률·취소를 재는 `SevenZipEditProgressTests`(지우기 한 번 약 7초 — 시간을 재므로 `DisableParallelization` 묶음).
  (Wave 2 통합 점검) 실제 `Iljip.exe` 를 열 번 띄우는 `ReuseDeviceContractTests.창없이_풀기는_7z_dll_길과_압축된_TAR도_…`(전체 실행 중 약 1분), `Wave2IntegrationTests` 는 시험마다 1~5초(병렬로 돌아 전체 시간에는 조금만 더해짐).
- 특정 묶음만: `dotnet test ... --filter "FullyQualifiedName~PasswordTests"`
- **계약만**(엔진과 무관하게 지켜야 할 것): `dotnet test ... --filter "Kind!=Record"` (1303개 — 통과 1302·건너뜀 1, 2026-10-13 W2-04 2차 검증 수정 후) — 아래 '계약 테스트와 기록 테스트'.
- **시간대 독립 확인**: 환경 변수 `ILJIP_TEST_TZ` 에 Windows 시간대 ID 를 주면 테스트 프로세스 안에서만 현지 시간대를 바꿉니다
  (시스템 설정은 안 건드림). 2026-10-12 `UTC`·`Pacific Standard Time` 둘 다 전체 통과 확인(W1-A 후 `UTC`, W1-B 후 둘 다 다시 통과).
  2026-10-14 Wave 2 통합 점검에서 `UTC` 전체 실행이 RAR4 시각 8개·자동 풀림 시각 1개로 실패(Wave 2 동안 이 실행을 안 해 쌓였던 것) → RAR4 는 제품을 고침, 자동 풀림 시험은 기준을 바로잡음(아래 'Wave 2 통합 점검에서 더한 것').
  ★ 흉내는 **이 테스트 프로세스의 .NET 만** 바꿉니다 — 테스트가 띄운 다른 프로세스(`Iljip.exe`·자동 풀림 스텁)와 7z.dll 같은 네이티브 코드는 이 PC 의 진짜 시간대를 씁니다.
  그런 쪽이 쓴 벽시계를 비교할 때는 `TestTimeZone.OsLocal`(흉내 전 OS 시간대)로 읽으세요.
  ```powershell
  $env:ILJIP_TEST_TZ = "UTC"; dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj; Remove-Item Env:ILJIP_TEST_TZ
  ```
- 빌드 산출물은 `Directory.Build.props`(git 제외) 때문에 `D:\Python\일집-build\dotnet\Iljip.Tests\` 로 갑니다.
- 임시 파일은 `%TEMP%\IljipTests\<테스트별 고유 폴더>` 에 만들고 테스트가 끝나면 지웁니다.

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `Contract/BandizipFixtureTests.cs` | [1] 반디집 픽스처를 반디집과 같게 읽고 푸는지(목록·sha256·수정 시각·압축 크기 범위) |
| `Contract/RoundTripTests.cs` | [2] 일집이 만든 ZIP(수준별·AES)·TAR·GZ·BZ2 왕복(7Z 왕복은 `SevenZipCreateTests`·`KnownGapTests`) + 표준 라이브러리·'한국어 탐색기 방식 이름' 교차 확인 |
| `Contract/PasswordTests.cs` | [3] 틀린 암호·암호 없음 → 예외·파일 안 남음·화면이 '암호 문제'로 알아봄 |
| `Contract/ZipSlipTests.cs` | [4] `../`·`..\`·절대경로·**형제 접두사(`../dest_evil.txt`)**·Windows 특수 이름·tar 링크 공격 방어(zip·tar·egg, W1-B 부터 7z.dll 로 여는 zipx 도) |
| `Contract/CancellationTests.cs` | [5] 32 MiB 해제·압축 도중 취소 → 부분 파일 정리(W1-B 부터 .7z 도) |
| `Contract/CorruptArchiveTests.cs` | [6] 잘린·망가진 zip/7z/tar/bz2/rar/egg, **쓰는 도중 깨지는 zip**, **잘리거나 확인값(CRC·크기)이 틀린 .gz·.tar.gz**, 서명 뒤가 쓰레기인 rar → 예외·오염 없음(경로별 sha 비교). 여러 멤버 .gz·끝에 0 이 붙은 .gz 는 끝까지 풀림 |
| `Contract/ExtractEntriesTests.cs` | [7] 선택 해제(파일·폴더·표기 차이) |
| `Contract/ProgressTests.cs` | [8] 진행률 0~100%·도중 보고·완료 보고(TAR·**7z(W2-02)** 는 큰 파일 안에서도, W1-B 부터 7z·zipx·iso·lzh 풀기와 7z 만들기, zip·7z 선택 해제) |
| `Contract/ThrottledArchiveProgressTests.cs` | 진행 창으로 가는 진행률 솎아내기 규칙(첫 중간 보고 통과·간격·완료 보고, (W2-07 검증 지적) 단계 알림 `IsStage` 는 늘 통과) |
| `Contract/RoutingTests.cs` | [9] 확장자 → 서비스 라우팅(등록 확장자 전체 목록·저장 필터는 기록 — W2-02 부터 ZIP·7Z·TAR·GZ·BZ2) |
| `Contract/KoreanFileNameDecoderTests.cs` | [10] CP949·UTF-8·ASCII·CP437 이름 판별 |
| `Contract/NameEncodingTests.cs` | [W2-09, I2-10] 압축 하나에 이름 방식 하나(한국어 먼저 → Shift-JIS·GBK, 섞이면 이름마다 예전 규칙), ZIP(목록·풀기·고르기·고치기)·TAR 의 일본어·중국어·한국어 이름, 표시 있는 UTF-8 섞임, 맥 NFD 이름 → NFC(ZIP·7z·EGG, NFD·NFC 어느 이름으로 골라도, NFC 로 겹치는 두 이름은 '이름 (1)'). (검증 지적 1차) 한자가 많은 한국어 이름은 한국어(중국어 아님)·드문 한자가 조금 섞인 중국어는 중국어, NFC 로 같은 두 파일은 하나만 골라 지우거나 이름을 바꾸지 않음·폴더와 그 안 파일을 함께 지우기, 파일 뒤 같은 이름 폴더도 풀림('이름 (1)')·원래 있던 사용자 파일은 그대로 |
| `Contract/ExtractDialogTests.cs` | [W2-09, I2-13] 풀기 창: 최근 푼 곳(맨 앞·겹치지 않음·10개·없는 폴더 숨김)·새 설정 칸 JSON 왕복과 옛 설정, 창 VM(처음 값·미리 보기 '이름 (2)'·잘못된 풀 곳·고른 항목만이면 휴지통 칸 없음·여러 개), 화면 흐름(답대로 '이름' 폴더·폴더 열기·기억·한 번 저장, 폴더 만들기 끄기, 취소, 휴지통은 이번에만, 고른 항목만, 못 풀면 만든 폴더 지움·최근에 안 넣음, `--extract` 하나·여럿). (검증 지적 1차) 압축 안의 압축·임시 폴더의 압축은 바탕 화면을 권함·임시 폴더는 기억하지 않음, 일집 임시 폴더에는 풀지 않음, 네트워크 폴더는 열 때·칠 때 디스크를 보지 않음. `ExtractDialogXamlTests`(같은 파일 — 창 XAML 양쪽 묶기, 순차 묶음) |
| `Contract/BrowseTests.cs` | [W2-09, I2-14] 이름으로 찾기(모든 폴더·경로 열·대소문자·폴더 이름·NFD·지우기·200ms 기다림·다른 압축을 열면 지움·찾은 폴더로 들어가면 지움·10만 개 100ms 안 — 실측 기록), 위 폴더(Backspace — 소스 확인 포함), 선택 합계('선택: N개 · 크기', 폴더 합, '약'), 크기 글자, 트리 너비(기본 260·바뀌면 한 번 저장·쓸 수 없는 값). (검증 지적 1차) 찾은 폴더에서 Enter 도 찾기 지우고 그 폴더로, 모으는 목록의 폴더는 크기를 세지 않음. `BrowseXamlTests`(같은 파일 — 메인 창의 찾기 칸·가르는 막대·경로 열) |
| `Contract/KnownGapTests.cs` | [11] 알려진 빈틈(본문은 '고쳐지면 맞아야 할 기대'). W2-01 에서 I2-2(열기)·I2-9 의 Skip 을 지워 통과, I2-1(7z 왕복 — 한글·빈 파일·빈 폴더)은 W2-02 에서 Skip 을 지워 통과. 압축된 tar 는 펼친 tar 안 파일 전부를 매니페스트 expected_entries 와 비교하고 '.tar 항목 없음'까지 봄(일집은 반디집보다 한 단계 더 펼친다 — W2-01 요구). 압축 **서비스**(창 없이 풀기가 바로 부르는 길)는 웹에서 받은 표시(MOTW)를 옮기지 않는다는 기록(IJ3-03 — 창·우클릭 풀기는 화면이 붙임, `InternetMarkTests`) |
| `Contract/CommandLineTests.cs` | [12] 명령줄 처리(60초 시간 제한) — **가짜 창으로 `--extract-here`(그 자리에)·`--extract-to-folder`(이름 폴더, 다시 하면 '이름 (2)', 암호 묻기·틀리면 다시·취소, 동시에 여러 개)** + 실제 `Iljip.exe --extract-to`. (1차 검증) 헤더 암호 7z 를 열기·우클릭 풀기에서 암호를 한 번 묻고 풀기·틀리면 다시·취소·다른 압축을 열면 잊기, 암호 창은 취소할 때까지(몇 번째 맞는 암호든 풀림), 이름 폴더 풀기를 취소·손상으로 멈추면 파일이 없을 때만 그 폴더를 지움. (W2-01) '"이름"\ 에 풀기'는 압축 확장자를 통째로 뗀 폴더(`자료.tar.gz`→`자료`, `견적.7z.001`→`견적`)에, .tar.gz 는 tar 안 내용까지 펼침, 헤더 암호 7z '여기에 풀기'가 7z.dll 로도 틀리면 다시·취소면 아무것도 없음, 실제 exe 로 7z·lzh·iso·tgz 풀기·ZIP 바탕 문서(.xlsx·.docx·.hwpx)는 `Unsupported archive type:`·종료 코드 2·잘린 xz·tar.xz·lzh·rar 는 30초 안에 종료 코드 1(한국어 한 줄, '압축 아님' 표지 없음) |
| `Contract/CommandLineParserTests.cs` | (W1-D) 명령줄 해석 순수 함수 — 키워드·인자 개수·대문자는 경로·'--extract-to-folder' 와 '--extract-to' 안 섞임. (W2-05) 풀기 동사는 압축 여러 개(`Archives`), 우클릭 풀기 동사만 메인 창 없이(`ShowMainWindow`). (W2-06) `--add-to 압축 경로…`(키워드 7개 — 기대 6→7), 넣을 경로가 없으면 None, 메인 창 없이. (W2-06 검증 지적) `--add-to-pick 경로…`(압축은 비워 둠 — 키워드 8개, 기대 7→8). (W2-11) `--compress-to 만들폴더 원본…`·`--extract-to-folder-in 폴더 압축…`(키워드 10개, 기대 8→10), 동사 앞 표시 `--new-window`·`--elevated`(키워드 아님 — `Flags`), `ToArgs` 는 `Parse` 의 반대(모든 동사·표시 왕복), 새 동사의 메인 창 여부. (W2-11 검증 지적) `--extract-into 폴더 압축…`(여럿을 그 폴더에 바로 — 키워드 11개, 기대 10→11, 메인 창 없이, 창 없이 풀기 판정에 안 걸림). (W2-12) `--extract-smart`·`--compress-zip`·`--compress-7z`·`--register-associations [--drop-handler]`·`--unregister-associations`(키워드 16개, 기대 11→16 — `--list-file`·`--drop-handler` 는 키워드 아님), 새 동사의 메인 창 여부·해석·`ToArgs` 왕복 |
| `Contract/ArchiveNamingTests.cs` | (W1-D) '"이름"\ 에 풀기' 폴더 이름 표(.tar.gz·.7z.001·.part1.rar·.vol1.egg·'.zip' …)와 '이름 (2)'·'(3)' 새 폴더 |
| `Contract/AppStartupContractTests.cs` | (W1-D) 첫 실행 인자 버그 — App.xaml 에 StartupUri 없음, OnStartup 이 창을 직접 만들고 인자를 넘김(소스 확인). (W2-05) 우클릭 풀기는 win.Show() 전에 메인 창 없이 따로, 두 번째 실행의 우클릭 풀기는 메인 창을 앞으로 안 가져옴, 파이프 '받았음' 답, 같은 이름 창은 UI 스레드에서 |
| `Contract/ShellExtensionContractTests.cs` | (W1-D) C++ 셸 확장·매니페스트·C# 의 약속: CLSID 일치, 메뉴 Verb, '여기에 풀기 (일집)' 제목, C++ 이 넘기는 키워드를 C# 이 알아봄. **빌드된 DLL 이 있으면 실제 GetTitle 이 C# 이름 규칙과 같은지**(Invoke 는 안 부름). (W2-12) CLSID 9개 표(헤더·constexpr·매니페스트 3개·C# 드롭 처리기), 모든 .cpp 의 COM 등록·vcxproj, 매니페스트('*'·Directory·Directory\Background 의 '일집 ▶'·'일집 열기', 확장자별 없음, desktop9 끌어 놓기, 0.4.0.0·외부 위치·mediumIL·win32App·EntryPoint 없음), C++ 압축 확장자·하위 메뉴 차례·제목·동사가 `ShellMenu` 와, 명령줄(30,000자·목록 폴더·`.lst`·`--list-file`·따옴표)이 `ShellCommandLine` 과, 끌어 놓기 제목·동사가 같음(소스 대조). **실제 DLL**: '일집 ▶' 제목·ECF_HASSUBCOMMANDS·아이콘·고른 것 12가지의 하위 메뉴 제목 차례(`ShellMenu.ItemsFor` 와)·GetState 없이 11개·'일집 열기', 오른쪽 끌기 메뉴(빈 팝업 메뉴에 넣어 글만 — 압축만이면 풀기 먼저·섞이면 압축만·CMF_DEFAULTONLY·동사 이름), 드롭 처리기(zip·7z 에 파일이면 복사·옮기기만·글·rar 는 안 됨 — Drop 은 안 부름) |
| `Contract/SourceFileOpenerTests.cs` | (W1-D) 원본 열기 — 쓰기 공유로 열린 파일은 읽힘, 공유를 막으면 '다른 프로그램이 쓰고 있어요', 재시도, 없는 파일. (1차 검증) 앞부분이 잠긴(LockFileEx) 파일은 열기에서 걸러짐, 첫 블록을 읽어 봐도 처음부터 읽힘, 읽는 도중 막히면 `SourceUnreadableException` |
| `Contract/CompressSkipTests.cs` | (W1-D) 잠긴 파일·못 들어가는 하위 폴더·없어진 원본은 건너뛰고 나머지는 그대로(zip·tar), GZ·BZ2 는 `SourceUnreadableException`, 담는 차례는 예전과 같음, 화면 안내('건너뛴 파일 N개')·결과 보존·부분 파일 정리. (1차 검증) 통째로·뒤쪽만 잠긴 파일(바이트 범위 잠금)도 건너뜀(뒤쪽은 빼고 다시 씀), 하나도 못 담으면 빈 zip·tar 를 만들지 않고 `NothingToCompressException`(기존 파일 그대로, 화면은 오류 안내). (W2-02) 7z 도 같은 규칙(.7z 행 — 공유를 막은·통째로·뒤쪽만 잠긴 파일, 못 들어가는 하위 폴더, 없어진 원본, 하나도 못 담음·빈 폴더만 남음, 화면 안내) |
| `Contract/DropPolicyTests.cs` | (W1-D) 끌어 놓기 규칙 표 + 가짜 선택 창으로 [취소]·[새로 압축할 목록]·[열기]·[목록에 넣기]. (1차 검증) 기본 버튼(Enter)은 늘 [취소], `ChoiceDialog` 가 정한 버튼 하나만 IsDefault(창은 만들기만), Drop 이벤트를 끝낸 뒤(Dispatcher.Yield) 처리(소스). (W2-06) 고칠 수 있는 압축(ZIP·7Z)을 보는 중이면 [이 압축 파일에 넣기]가 맨 앞(기본은 여전히 [취소] — 기존 VM 시험 2개의 선택지 기대가 바뀜), 고칠 수 없는 압축(TAR)은 예전 선택지 그대로, 넣기를 고르면 지금 폴더에 들어감 |
| `Contract/SortingTests.cs` | (W1-D) 정렬 비교기(폴더 먼저·크기·날짜·같은 값은 이름), VM 정렬(두 번 누르면 내림, 폴더를 옮겨도 유지, 기본은 이름 오름차순), XAML 계약 + 열 머리 템플릿을 메모리에서 적용해 손잡이·화살표 확인. (1차 검증) 이름은 탐색기와 같은 자연 정렬(`2.txt` → `10.txt`, `1월` → `2월` → `10월`) |
| `Contract/SevenZipLibraryTests.cs` | [W2-01] 출력에 x64\7z.dll 이 있고 실제로 불러짐, 판(26.03)·SHA-256 기록, 막힌 PC 흉내(7z·RAR 는 SharpCompress 로 대신 — 암호·잘림·여러 조각도 쉬운 말로, zipx·iso·cab·lzh·wim·xz·txz 는 한국어 안내·대상 폴더 안 생김, 저장 형식에 7Z 없음), 공개 소프트웨어 안내·라이선스 전문(BSD 2·3조항 포함, IJ3-03 부터 `7-Zip-License.txt` 원문도). (W2-02) 막힌 PC 에서는 저장 형식에 7Z 가 없고 7z 만들기는 `EngineUnavailableException`(쉬운 안내, 파일·임시 파일 없음), 목록까지 잠긴 7z·머리 암호 RAR 은 대신 경로에서도 `ListPasswordRequiredException` |
| `Contract/TruncationTests.cs` | [W2-01 (a)·(e)] 끝이 잘린 RAR5·RAR4(한 바이트씩 전부 — 머리 사이 포함)·ZIPX·LZH(전부)·머리 암호 RAR5 는 '끝부분이 잘려'(목록도) + 대상에 아무것도 안 남김(새로 만든 폴더도), 끝 머리 없는 RAR4 는 판 20 이면 풀고 판 29 면 잘림, 여러 조각 RAR 의 마지막 조각 없음은 그 이름, 숫자 분할의 마지막 조각이 덜 받아졌으면 '덜 받아졌어요', 잘린 .gz 는 어디서 잘려도 반쪽 파일 없음 |
| `Contract/XzTests.cs` | [W2-01 (b)] 잘린 xz·tar.xz·txz(끝 120바이트 전부 + 표본)는 10초 안에 '끝부분이 잘려'·아무것도 안 남김(1차 실험의 무한 루프 자리 포함), 큰 xz·tar.xz 취소는 2초 안(서비스·화면 둘 다), 잘린 xz 의 내용 판별 2초 안, 본체에 SharpCompress `XZStream` 없음(소스 확인) |
| `Contract/CabTests.cs` | [W2-01] 합성 CAB(`RawCab`) 한글 이름 목록·풀기, 잘린 CAB 은 머리의 전체 크기로 '끝부분이 잘려'·아무것도 안 남김 |
| `Contract/BuildOutputTests.cs` | [W2-01] 저장소 안 `Iljip\bin\Release·Debug` 에 Iljip.exe 가 없음(재사용장치가 먼저 집어 가는 자리), 테스트 출력은 저장소 밖 |
| `Contract/EntryNameTests.cs` | [W2-01, 1차 W1-B 2차 지적] Windows 가 못 쓰는 이름(':'·'?'·'*'·CON·aux·nul·lpt1·'...')을 모든 해제 경로(tar.gz·tar·zip·egg·7z 솔리드/비솔리드·선택)가 같은 규칙으로 바꿔 씀, 숨은 스트림 안 생김, '/abs.txt' 를 골라도 조용히 빠지지 않음 |
| `Contract/SfxTests.cs` | [W2-01] RAR5·RAR4·EGG·ALZ 자동 풀림 exe(가짜 PE 스텁 + 합성 압축) 목록·해제, 서명 글자만 든 보통 exe 는 압축으로 안 봄 |
| `Contract/SevenZipEngineTests.cs` | [W2-01] 7z.dll 해제: 틀린 암호·손상 때 파일·임시 폴더 안 남김, 이름 (1) 비켜 쓰기, 수정 시각, 숫자 분할의 빠진 조각(가운데·끝) 이름, 7z SFX(합성)·반디집 SFX 선택 해제, 이름 바꾸기 규칙, ZIP 한글 이름 세 가지(ZIP 은 SharpCompress 로 읽는 근거) |
| `Contract/LocatorTests.cs` | [W2-01] 내용(서명)으로 형식 알아보기(확장자가 우선, 모를 때만), 확장자가 바뀐 압축, .gz·.bz2·.xz 안이 tar 면 압축된 tar 처리기, .docx·zstd, 끌어 놓기는 확장자로만, 창 없이 풀기는 ZIP 바탕 문서를 압축으로 안 봄, 저장 형식에 7Z 없음, 오류 문구에 '압축 파일이 아닌' 없음, 한글 경로가 든 영어 문구 바꾸기, 옮기기 이름 비키기·잠금 재시도, 우클릭 메뉴(매니페스트)에 풀 수 있는 확장자 전부. (W2-02) 저장 형식의 7Z 는 7z.dll 을 쓸 수 있을 때만 |
| `Contract/HeaderProbeTests.cs` | [W2-01] 헤더 암호 7z·RAR4/RAR5 머리(합성), 여러 조각 RAR 빠진 조각 이름, 숫자 분할 이어 붙이기 |
| `Contract/CompressedTarTests.cs` | [W2-01 (c)] tgz·tar.gz·tbz2·txz 한 번에 펼치기: 선택 해제, gzip 확인값이 틀리면 아무것도 안 남김, 잘린 것(여러 자리)·데이터가 깨진 것은 '잘림'·손상 + 아무것도 안 남김(원래 있던 파일은 그대로), 취소는 끝난 파일만, 대상 폴더 쪽 오류는 손상이 아니고 옮긴 것을 되돌림, 진행률(기록), 만들기 미지원(기록) |
| `Contract/DiscImageTests.cs` | [W2-01 (d)] ISO 의 UDF 우선(UDF 만·Joliet 없는 다리·Joliet+UDF·Joliet 만, .img 확장자, 대신 형식으로 다시 열기), 잘린 디스크 이미지는 내용 구역 안이면 반드시 '잘림'·그 밖에 오류 없이 끝나면 내용이 다 맞음, 반디집 iso 는 끝 1바이트만 잘라도 '잘림' — 이미지는 Windows IMAPI2FS 로 그때그때 만듦 |
| `Contract/MainViewModelPasswordTests.cs` | [W2-01, I1-7·B-067] 가짜 창으로 화면 흐름: 헤더 암호 7z 열기→암호→목록, 틀리면 다시, 취소, 열 때 받은 암호로 풀기, 풀 때 암호 무제한 재시도, 명령줄 풀기는 한 번만 묻기, 손상 안내, .docx 드롭, 손상된 헤더 암호 7z 는 맞는 암호를 한 번만 묻고 손상으로. (W2-02) 첫 창 이유 `ListLocked`·틀리면 경고, 화면에서 파일 이름까지 암호화한 7z 를 만들고 다시 열 때·풀 때 묻지 않음(보관분 되살림), 7z 만들기 취소면 덮어쓰려던 파일 그대로 |
| `Contract/SevenZipCreateTests.cs` | [W2-02, I2-1] 7z 만들기(보관분 되살림): 솔리드 켜고 끄기(7z.dll·SharpCompress 가 보는 솔리드 여부), 파일 이름 암호화(암호 없이는 `ListPasswordRequiredException`, 한글 암호 왕복), 암호만(목록은 보임·풀기는 암호), 한글 암호를 다른 엔진도 엶, 서로 다른 폴더의 원본·빈 폴더(이름 암호화 포함)·빈 폴더만, 파일만 고르면 폴더 항목 없이, 모든 폴더가 항목이고 폴더 시각 보존·같은 이름 폴더 둘은 거절, 자기 자신 안 담음, 실패면 덮어쓰려던 파일 그대로·임시 파일 없음, 수준별 크기·왕복, 수준별 7z.dll 설정(기록), 취소면 아무것도 안 남김, 읽는 도중 크기가 바뀐 파일은 빼고 다시 씀, 잠긴 원본은 건너뜀·하나도 못 담으면 이름이 든 쉬운 안내, 손상과 틀린 암호 가리기, 원본 스트림(`LazySourceStream`) 단위 시험 |
| `Contract/SevenZipCreateLargeTests.cs` | [W2-02, 1차 W1-B 검증 지적] 160MB(무작위 120 + 글자 40) 보통 수준: 진행률이 줄지 않고 서로 다른 값 10개 이상·끝 100%·다 읽은 시점이 전체 시간의 절반 뒤, 전용 메모리 증가 700MB 아래, 기록(시간·메모리·크기 — 테스트 출력 `기록_7z_보통_160MB`), 시작 3초·30% 뒤 취소는 3초 안·덮어쓰려던 파일 그대로, 처음 읽자마자 취소(서비스·화면)도 3초 안·아무것도 안 남김. 따로 실행(`DisableParallelization`) |
| `Contract/ListPasswordFlowTests.cs` | [W2-02, I1-7] 목록까지 잠긴 압축(일집 7z·py7zr 헤더 암호 7z 두 개·머리 암호 RAR5·RAR4): 서비스는 암호 없이 `ListPasswordRequiredException`·틀리면 머리 암호 `WrongPasswordException`, 화면은 열 때 한 번(`ListLocked`)·틀리면 경고 뒤 다시·취소면 보던 압축·목록·기억한 암호 그대로(오류 창 없음)·열고 나서 풀면 다시 안 물음·우클릭 '여기에 풀기'·'"이름"\ 에 풀기' 는 한 번만, 자동 풀림 exe 안쪽(7z.dll·대신 경로 둘 다), 예외 형식(하위 형식·문구) |
| `Contract/SplitCreateTests.cs` | [W2-03, I2-2 만들기] 나눠 만들기: 7Z 64KB 조각(이름·크기·첫 조각으로 목록·풀기·7z.dll `VolumeFileNames`·암호 + 파일 이름 암호화), ZIP 픽스처 9개 × 무작위 1,000~5,000바이트(첫 조각 `50 4B 07 08`·로컬 머리가 한 조각 안·마지막 디스크 번호·일집 목록·풀기가 원본과 같음·7z.dll Check), 서비스로 나눈 ZIP(AES·ZipCrypto·한글 암호), 목록이 조각을 넘는 2,000개(4KB), ZIP64 7만 개, 조각 너무 많음(ZIP 99·7Z 999), 빠진 조각(.z02·.7z.002·.part2.rar — 이름·대상 폴더 안 생김), 마지막 조각만, 취소(압축 중·나누는 중), '이름 (2)' 세트, 한 조각이면 안 나눔, 진행률, 화면(세트를 열고 조각 이름으로 안내·크기 전달·TAR 무시), 옵션 창 규칙·XAML. 반디집(bz.exe t)은 `BandizipCliFact`/`BandizipCliTheory` 로 따로 |
| `Contract/CompressionOptionsRulesTests.cs` | [W2-02] 압축 옵션 창 규칙(`Views/CompressionOptionsRules` — 창 없이): .7z 는 솔리드·파일 이름 암호화 칸(암호가 있을 때만)·ZIP 암호 방식 칸 숨김·한글 암호 경고 없음, 수준 글자 저장만·빠름·보통·최대, 형식에 맞지 않는 값은 버림, XAML 칸 이름·문구(소스) |
| `Contract/EggAlzTests.cs` | [13] EGG/ALZ 성공 경로(목록·해제·충돌·선택·취소·진행률·크기 0 항목·ALZ 한글·한자·기호 이름·끝 구분자 없는 폴더 항목) + **잘림·CRC 불일치 → 예외·짧은 파일 안 남김**, 분할 조각 후보 — 합성 EGG/ALZ |
| `Contract/AlzNameScannerTests.cs` | ALZ 머리 원본 바이트 스캐너(B10 우회·폴더 속성): 맞게 읽기, 잘리면 온전한 항목까지만, 서명 깨짐·항목 수 불일치·분할이면 예외 없이 포기, 이름 바이트 풀기(UTF-8→CP949→CP437) |
| `Contract/CollisionTests.cs` | 같은 이름이 있으면 덮지 않고 `이름 (1).txt` 로 비켜 쓰기, **같은 폴더에 동시에 여러 번 풀어도** 모두 다른 이름으로(gz·zip·egg). (W2-05, I1-5) 같은 이름 처리 범위가 있으면 덮어쓰기(임시 파일 없이 새 내용)·건너뛰기(바이트 그대로)·이름 바꾸기·취소(이번에 만든 것만 되돌림)·‘남은 파일도 똑같이’(한 번만 묻기)를 zip·7z·rar·tar·egg 에서, 잠긴 파일 덮어쓰기는 한국어 오류·원본 그대로, 압축 안 같은 이름 둘은 묻지 않음(zip·zipx), raw bz2 |
| `Contract/ShellExtractFlowTests.cs` | [W2-05, I1-6] 탐색기 우클릭 풀기(`ProcessShellCommandAsync`): 메인 창 보기를 안 바꾸고 진행 창 하나, '이름' 폴더를 한 번 엶(설정으로 끔, '여기에 풀기'는 안 엶), 손상·잘린 zip·7z·rar·tar.gz·egg 는 쉬운 한국어 오류 하나(영어 엔진 문구·'압축 아님' 표지 없음, 임시 파일 없음), 같은 이름 묻기·설정 덮어쓰기, 목록까지 잠긴 7z 는 암호 한 번, 암호 창 취소는 조용히, 없는 파일 알림, 탐색기를 못 띄워도 오류 창 없음 |
| `Contract/BatchExtractTests.cs` | [W2-05, I2-6] 여러 개 풀기: 같은 암호 셋은 한 번만 묻기·진행 창 '2/3 · b.zip'·전체 막대, 암호가 다른 파일만 '앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.', 암호 창 취소는 그 파일만 건너뜀, 5개 중 손상 1개는 요약(4/5·이름·한국어 이유·날 문구는 자세히), 같은 이름 [취소]·진행 창 [취소]면 남은 것 손대지 않음, 따로 들어온 우클릭 명령이 돌던 묶음에 붙음, 열기 창 여러 개 → '모두 풀기', `--extract a b` |
| `Contract/RecycleAfterExtractTests.cs` | [W2-05, I2-5] 풀고 나서 압축 파일 지우기(가짜 휴지통): 다 풀면 보냄·기본은 안 보냄, 나뉜 압축은 조각 모두 한 번에(7z.001·z01/zip·zip.001), 실패·건너뜀·취소·휴지통 없는 곳(알림)·조각 하나라도 휴지통 없는 곳·일집 임시 폴더·고른 항목만 풀기는 안 보냄, 덮어쓰기·이름 바꾸기로 다 풀면 보냄, 못 보내면 알림(날 코드 없음), 메인 창에서 지우면 화면 비움, 여러 개 풀기는 다 풀린 것만, 창 없이 풀기는 설정·휴지통을 안 씀(소스) |
| `Contract/ExtractSettingsTests.cs` | [W2-05] 설정 settings.json(기본값·왕복·모르는 필드 보존·깨진 파일은 settings.bad-*.json·없으면 안 만듦·설정 창은 사본·시험 중엔 사용자 설정을 안 읽음), 같은 이름 처리기(설정 고정·‘남은 파일도 똑같이’ 기억·취소는 기억 안 함), 메인 창 풀기의 같은 이름 질문(크기·남은 수)·취소, 나뉜 압축 조각 찾기(`ArchiveVolumes` — 숫자·PKZIP·RAR partN·EGG volN·나뉘지 않음), 요약 문구, 모르는 엔진 오류의 쉬운 한국어 |
| `Contract/ArchiveEditTests.cs` | [W2-06, I2-3] 압축 고치기(서비스 — `Services/Editing`): ZIP 7종(반디집 기본·CP949 이름·ZipCrypto·한글 ZipCrypto·AES-256·한글 AES·데이터 서술자) × 넣기·지우기·이름 바꾸기·새 폴더, 7z 5종(반디집 기본·파일 이름 암호화·한글 이름 암호화·7z.dll 솔리드·내용만 암호) × 같은 4가지 — 목록·풀린 SHA·(ZIP) 손대지 않은 항목은 이름 바이트·플래그·압축 데이터까지 그대로, 새·바뀐 이름은 UTF-8(bit 11), 암호 압축은 같은 방식(같은 암호 바이트·AES 세기)·여전히 암호 필요, (7z) 파일 이름 암호화 유지. 반디집 `bz t` 12개(`BandizipCliTheory`). 취소·잠긴 원본·바꾸는 순간 잠김·쓰기 실패·검사 실패면 원본 바이트 그대로·임시/예비 파일 없음, 검사기가 계획과 다른 목록을 잡음, 고칠 수 없는 형식 16가지·나뉜 압축·앞에 붙은 ZIP, 암호 없음/틀림, 암호 ZIP 지우기는 암호 없이, 같은 이름(비켜 넣기·덮어쓰기·건너뛰기·취소=전체 취소), 이름 규칙·대소문자만 바꾸기·없는 항목, 저장만 ZIP 에는 저장만으로 |
| `Contract/MainViewModelEditTests.cs` | [W2-06, I2-3] 가짜 창으로 화면 흐름: Del 묻기(기본 [취소]·이름 셋까지)·지우고 목록 다시 읽기, 이름 바꾸기·못 쓰는 이름·이미 있는 이름 안내, 새 폴더 이름 묻기(`AskText`)·취소, 지금 폴더에 넣기, 같은 이름 묻고 덮어쓰기, 암호 ZIP 은 암호 창·취소면 그대로, 고칠 수 없는 형식은 안내만, `--add-to` 는 메인 창 없이·짧은 알림·암호 묻기·실패는 쉬운 오류 한 번, 명령줄 안에서 고치기가 차례(`_commandGate`)를 다시 기다리지 않음. (검증 지적) `--add-to-pick` 은 압축 고르기 창(ZIP·7Z)·고르지 않으면 조용히·TAR 는 쉬운 오류, 7z.dll 을 못 불러온 PC 의 7z 는 '이 PC에서는 7Z 파일을 고치는 부품…'(TAR 는 그대로), 반디집 유니코드 경로 ZIP 도 화면에서 같은 이름 묻기·지우기. (W2-09 검증 지적 1차) 겉보기 이름이 같은 두 줄은 하나만 골라 지우지 않음·둘 다 고르면 지움·찾은 결과의 폴더와 안 파일 함께 지우기 |
| `Contract/EntryReinsertTests.cs` | [W2-06, I3-3] 압축 속 파일을 열어 고치면(가짜 파일 감시 `FakeFileChangeSource`): '다시 넣을까요?' → [넣기]면 다시 넣음, 내용이 같으면 안 묻고 알림 여러 번은 한 번만, [나중에]면 압축을 떠날 때·닫을 때 다시, 닫을 때 [취소]면 안 닫힘, 암호 압축은 열 때의 암호로, 고칠 수 없는 형식은 '다른 곳에 저장', 닫으면 감시 멈춤·작업 폴더 지움, 실행 파일은 열기 전에 경고, 감시기는 조용해진 뒤 내용(SHA-256)으로 판단, 이/가 고르기. (검증 지적) 넣는 동안 또 저장하면 넣은 뒤 다시 묻기(ZIP·7z), [넣지 않기]는 물어본 내용만, 연 뒤 지운 항목도 제자리에 넣기(ZIP·7z), 넣지 못하면 '다른 곳에 저장' 권하기·닫을 때 [닫지 않기], 7z.dll 을 못 불러온 PC 는 '이 PC에서는 7Z 파일을 고칠 수 없어요.'. (검증 지적 2차) 묻는 동안 명령줄(--open·--extract)로 다른 압축이 와도 멈추지 않음, 묻는 중·넣는 중에는 일집을 닫지 않음(ZIP·7z), Windows 에 못 쓰는 이름(':'·끝 점)의 항목도 그 이름 그대로 다시 넣기, 감시기의 '가라앉음'은 묻기를 기다리지 않음 |
| `Contract/ArchiveEditReview2Tests.cs` | [W2-06 검증 지적 2차] 빈 7z(모두 지운 결과·빈 폴더만)에 넣기·새 폴더 + 반디집 `bz t`, 다시 넣기(바꿔 넣기)는 압축에 있던 이름(':'·끝 점) 그대로(ZIP·7z — 사용자가 고르는 이름은 예전 규칙), 이름 검사(순수), 일집이 못 푸는 정상 ZIP(흘려 쓴 ZipCrypto + 데이터 서술자)은 '손상'이 아니라 '일집이 풀 수 없는 방식'·결과만 못 풀면 고치다 생긴 문제, ZIP 검사 동안에도 막대가 오름(암호 있음·없음) |
| `Contract/ArchiveEditReviewTests.cs` | [W2-06 검증 지적] 반디집 ZIP 유니코드 경로(0x7075 — `nfd_bandizip_default`)도 목록 이름으로 지우기·이름 바꾸기·같은 이름 넣기(묻기)·바꿔 넣기 + 반디집 `bz t`, 파일과 같은 이름의 폴더는 폴더째 '이름 (1)'(ZIP·7z, 한 넣기 안에서 같은 이름), 원래부터 손상된 ZIP 은 '원래 압축 파일에 이미 손상된 곳이…', 고치는 사이 원본이 바뀌면 덮어쓰지 않음(ZIP·7z)·동시에 셋을 고쳐도 '됐다'고 한 것은 모두 남음, 바꿔 넣기(`ReplaceExisting` — 묻지 않고 바꿈, 없으면 새로, 폴더면 비켜 넣기). 같은 파일의 `SevenZipEditProgressTests`(`DisableParallelization` 묶음 — 16MB 솔리드 7z): 지우기 동안 막대가 '다시 정리하는 중'에 오름·도중 취소는 3초 안·원본 그대로 |
| `Contract/MainWindowEditUiTests.cs` | [W2-06] 메인 창 XAML 을 STA 스레드에서 만들기만(띄우지 않음): 이름 칸만 편집 가능·행 지우기/더하기 끔·우클릭/☰ 메뉴 항목. F2 로만 편집 시작·이름 확정은 편집을 끝낸 뒤(Dispatcher.Yield)·닫을 때 묻기(소스). WPF 앱 객체를 잠깐 만들고 끝나면 `Application.Current` 를 되돌림(따로 순차 실행). (W2-11) ☰ 메뉴 맨 앞에 새 창·다른 이름으로 저장·이 압축 파일 지우기(기대 목록에 셋 더함), 시작 화면(`Views/StartView`)이 읽히고 큰 단추 셋·모두 지우기·최근 목록이 VM 명령에 묶임, Ctrl+N = 새 창 |
| `Contract/CompressFlowTests.cs` | [W2-07, I2-4·I3-4·I3-5·I3-6·I2-12] 한 화면 압축 창 뒤의 흐름(가짜 창 `CompressionRequestAnswer`·가짜 휴지통·시계·시험기): 각각 따로(파일·폴더·한글 이름 셋 → 셋, ZIP·7Z·TAR 왕복, 있던 이름은 '이름 (2)', 진행 '1/3'…, 요약 창, 못 만든 것만 요약·대기 목록에 남김, 원본 하나는 원본 이름), 이름에 날짜(세 모양·두 번 안 붙임·숫자에 붙은 것은 날짜 아님·각각 따로에도), 쓸모없는 파일 빼기(기본 목록이 압축에 없음·뺀 수 — ZIP·7Z·TAR, '쓸모없는 파일 5개는 뺐어요', 끄면 다 넣음, 사용자 목록 `*.bak`·폴더 `하위/`, 직접 고른 것은 담음·다 빠진 폴더는 빈 폴더, 규칙), 원본 지우기(시험 통과 + [옮기기]면 고른 원본만 가짜 휴지통으로·질문 문구·기본 [그대로 두기], 진짜 시험기로 ZIP·7Z·TAR·나눈 ZIP·나눈 7Z·암호 ZIP·이름 숨긴 7z, 시험 실패·[그대로 두기]·창 닫기·기본 꺼짐(예전 저장 창 + 옵션 창 답)·휴지통 없는 곳·각각 따로에서 실패한 것만 남음·뺀 파일이 든 폴더·원본 폴더 안에 만든 압축·묻는 동안 바뀐 원본·휴지통 실패는 경고만·GZ 는 안 지움), '다 만들고 시험하기', 진짜 시험기가 망가진 ZIP·7Z·GZ·BZ2 를 잡음(아무것도 안 남김), 설정(settings.json 왕복·camelCase·'password' 없음·기본값, 깨진 파일 → 기본값 + settings.bad-*.json, 모르는 필드 보존·옛 설정에 compress 채움, 창의 값을 한 번 저장·암호 저장 안 함·다음 창의 처음 값·취소면 저장 안 함), 하나로 만들 때 같은 이름은 [바꾸기]일 때만 바꿈, 창에 넘기는 제안(이름·폴더·형식 — 7z.dll 이 막히면 7Z 없음). (검증 지적 1차) 크기가 같아도 내용이 바뀐 원본은 안 지움(묻는 동안·압축하는 동안), 폴더와 그 안의 파일을 같이 고르면 폴더만 옮김(하나로·각각 따로), 원본 그 자체('a.zip' → a.zip)는 [바꾸기]여도 덮지 않고 'a (2)'(ZIP·7Z·TAR·각각 따로 + 덮어쓰기 처리기), GZ·BZ2 의 날짜·번호는 원래 확장자 앞('보고서_20261013.xlsx.gz'·'… (2).xlsx.gz', 각각 따로 BZ2), 시험·맞춰 보기 단계 알림이 진짜 솎아내기를 지남, '원본을 휴지통으로'는 압축 창에서 기억 안 함, folderChoice·fixedFolder 왕복. (검증 지적 2차) TAR 파일을 GZ·BZ2 로('backup.tar.gz'·'backup.tar.bz2'·날짜·각각 따로·속이 TAR 가 아닌 '.tar' — 풀면 원래 TAR 그대로·시험 통과), 만든 형식의 처리기로 시험(`CreatedArchiveTester.ServiceFor`·`ArchiveServiceLocator.ForExtension`), 각각 따로 시험 중 진행 막대가 뒤로 안 감(`ProgressDialog.BarValue` — 원본 지우기 켬·끔), 원본 훑기는 취소를 봄(`SourceCollector.Collect`·`CompressFlow.TakeStamps`)·맞춰 보는 도중 취소면 묻지도 옮기지도 않음, 휴지통으로 일부만 옮겨지면 옮긴 것·남은 것을 따로 알림, 시험만 켜고 시험을 취소하면 알림(하나로·각각 따로 — 상태 줄 '(시험은 취소)') |
| `Contract/CompressDialogXamlTests.cs` | [W2-07 검증 지적 2차] 한 화면 압축 창·옵션 칸만 보이는 창(예전 옵션 창 어댑터)·설정 창 XAML 을 STA 스레드에서 만들기만(띄우지 않음 — `TestSupport/WpfAppScope`): 압축 창은 VM 과 양쪽으로 묶임(이름·형식·만들 곳·날짜·각각 따로·시험·빼기, 형식을 바꾸면 7Z 칸·나누기·암호 칸이 바뀜), 옵션 칸만 보이는 창은 이름·형식·그 밖 칸이 숨음, 설정 창은 받은 설정이 칸에 들어감. 모두 묶기 오류 0(일부러 틀린 묶기로 추적이 켜졌는지 먼저 확인). `MainWindowEditUiTests` 와 같은 순차 묶음. (W2-10) '자동 풀림 EXE' 를 고르면 형식 칸 아래 메일 안내가 보이고 ZIP 암호 방식 칸 보임·나누기·7z 칸 숨음, 설정 창의 메일 안내 칸(= skipMailWarning 의 반대), 선택 창의 '다시 묻지 않기' 확인란(글자를 줄 때만) |
| `Contract/SfxCreateTests.cs` | [W2-10, I3-1] 자동 풀림 exe 만들기(일집 자체 스텁 `Iljip.SfxStub` + ZIP + 꼬리 'ILJIPSFX'). 만든 exe 를 **무인 모드(/s)로만** 실제로 돌려 봄(창 없이 — 도는 동안 MainWindowHandle 이 늘 0, 60초 제한): SampleTree 왕복·수정 시각, 암호(AES-256·ZipCrypto × 영문·한글) 맞으면 원본과 같음·틀리거나 없으면 종료 코드 1·대상에 아무것도 없음, 끝 1KB 가 잘림 → 1·'잘렸어요'·아무것도 안 남음, 내용 깨짐(CRC·HMAC) → 1·반쪽 파일 없음, 경로 공격 이름(12가지 — '..'·절대 경로·드라이브 'C:'·장치 이름 'con'·':' 스트림·끝 점·'.') → 1·안팎에 아무것도 없음, 인자가 틀리면 2, 무인 모드는 같은 이름을 덮어쓰고 다른 파일은 그대로, 빈 폴더만, 일집 안에서 열고 풂(암호 포함·깨진 것은 손상으로), 스텁이 없거나 취소면 아무것도 안 남음, 원본 폴더 안의 예전 exe 는 안 담음, 스텁은 공유 암호 코드(`Iljip\Services\Shared`)를 링크·sfx 폴더에만 복사. 실행이 막힌 PC 는 `SfxStubFact`·`SfxStubTheory` 로 건너뜀. (검증 지적 1차) 건너뛰기는 정책 거절만(`SkipReasonFor`)·스텁만 실행하면 1, 같은 풀 곳의 마무리 잠금(시험이 뮤텍스를 쥐면 임시 폴더에 다 풀고 기다림)·셋 동시에 풀어도 모두 0·빠진 파일 없음, 강제 종료(kill) 뒤 바로 다시 풀면 숨은 임시 폴더를 지움, 끊긴 풀기가 비켜 둔 원래 파일은 '덮어쓰기 전 원래 파일 (…)' 로 옮김(무인 모드·본체 `StagingFolder.CleanupStale` 둘 다)·살아 있는 것·표시 없는 새 것은 그대로·못 옮기면 남김, 옮기다 실패하면 통째로 옮긴 폴더·덮어쓴 파일까지 되돌림, 표시 이름과 같은 항목도 풀림. (검증 지적 2차) 한도(4GB − 1바이트)를 넘는 exe 는 만들지 않음(`SfxTooLargeException`·아무것도 안 남음·한도와 같은 크기는 만듦 — 한도는 `SfxBuilder.LimitForTests` 로 작게), 나중에 코드 서명한 exe(서명 표를 붙인 모양)도 무인으로 풀리고 일집에서 열림·꼬리 뒤에 서명 표가 아닌 것이 붙으면 손상, NFC·NFD 두 모양의 같은 이름이 함께 있어도 원본대로(맥 NFD 만이면 NFC 로·대소문자만 다른 두 이름은 손상), 끝이 잘린 자동 풀림은 일집 안에서도 '끝부분이 잘려 있어요'(지원 안 함이 아님), 임시 폴더를 지우는 동안 주인 표시를 놓지 않음(`SfxStaging.DeleteOwnedTree`), 원래 자리 목록은 열어 둔 채 줄마다 남음(`SfxStaging.BackupList`), 많이 덮어써도 결과가 같음 |
| `Contract/SfxCompressFlowTests.cs` | [W2-10, I3-1] 압축 창으로 자동 풀림 EXE 만들기(가짜 창·휴지통·설정): 만들기 직전 메일 안내 한 번(문구·[그래도 만들기][ZIP으로 만들기][취소]·'다시 묻지 않기'·기본 0), [ZIP으로 만들기] → 같은 옵션의 .zip(있으면 '이름 (2).zip')·exe 없음, [취소]·창 닫기 → 아무것도 안 만들고 저장 안 함, 설정으로 끄면 질문 0번, '다시 묻지 않기' → settings.json `sfx.skipMailWarning` 한 번 저장·다음엔 안 물음, 마지막 형식으로 기억 안 함, 다 만들고 시험하기(진짜 시험기 — 암호 없음·AES·ZipCrypto 한글)·원본 옮기기, 진짜 시험기가 깨진 exe 를 잡음, 각각 따로, 스텁이 없으면 형식 목록에 없음, 앱에서 만든 exe 를 무인으로 풀기, 창 VM 규칙·옵션(나누기 버림·한글 암호 경고 안 함), 설정 왕복·모르는 필드. (검증 지적 1차) [ZIP으로 만들기] + AES 한글 암호면 한 번 더 묻기([ZipCrypto 로] → ZipCrypto·CP949 로 써짐·방식 기억 / [AES 그대로] / [취소]·창 닫기 → 아무것도 안 만들고 '다시 묻지 않기'도 기억 안 함), 이모지 암호는 [취소]·[그대로]만, 암호 없음·영문 암호·[그래도 만들기]는 더 안 물음. (검증 지적 2차) '다시 묻지 않기'는 [그래도 만들기]일 때만 기억([ZIP으로 만들기]와 켜면 다음에 또 물음), 원본이 4GB(시험은 작게)를 넘으면 만들기 전에 묻기([ZIP으로 만들기 (권장)]·[그래도 … 해 보기]·[취소] — 취소면 아무것도 안 만들고 저장 안 함), 해 보다가 넘으면 '…만들지 않았어요' + 'ZIP으로 만들까요?'([ZIP으로 만들기]면 진행 창을 다시 띄워 ZIP·[닫기]면 '오류 발생'), 각각 따로는 요약 창에 까닭, 압축 창 안내에 '4GB 가 넘는 exe 는…' |
| `Contract/RecentFilesTests.cs` | [W2-11, I3-15] 최근 연 압축(`RecentFilesService` — 설정 `recent.files`): 맨 앞·대소문자 무시로 하나·10개, 없는 파일은 '없음'·빼기·모두 지우기, 임시 폴더(일집 temp·Windows 임시·Outlook 첨부) 안은 넣지 않음, JSON 왕복·빈 경로 버림, 창 두 개 합치기(`Merge` — 늦게 연 시각·이 실행에서 뺀 것 제외·10개). 화면: 열면 메모리에만·창을 닫을 때(`SaveOnExit`) 트리 너비와 함께 한 번 저장·바뀐 것이 없으면 저장 안 함·다음 실행에서 보임, 창 두 개를 닫으면 둘 다 남음, 일집 임시 폴더의 압축은 기록 안 함. (검증 지적 1차) 창 두 개가 한 설정 저장소를 함께 쓸 때 — 다른 창의 풀기 창 저장이 먼저 닫은 창의 최근 파일을 안 지움(A18), 닫는 창이 다른 창의 설정 창 값을 안 되돌림(A1)·압축 창 기억도 그렇고, 다른 창에서 뺀 줄을 안 되살림, 모두 지우기는 저장된 줄까지, 못 읽으면 덮어쓰지 않고 다음 저장에 다시(`InMemorySettingsStore.FailUpdates`), 설정 창 적용 뒤에 연 압축도 저장, 진짜 `JsonSettingsStore.Update`(다시 읽어 얹기·잠겼으면 null 이고 파일 그대로), `SettingsPatch`(바꾼 칸만) |
| `Contract/StartScreenTests.cs` | [W2-11, I3-15] 같은 파일에 세 묶음. `StartScreenTests` — 시작 화면이 보이는 때(빈 창·열면 숨음·닫으면 다시·모으는 목록이면 숨음), 최근 파일 누르기, 없는 파일 질문([목록에서 빼기]면 바로 한 번 저장·[그대로 두기]면 저장 안 함·열기 오류 창 없음), 목록에서 지우기·모두 지우기(기본 [취소]), 들어 있는 폴더 열기, 새 창(`--new-window`·최근 파일 '새 창으로 열기' `--new-window --open 경로`·관리자 창이면 `--elevated --new-window` — 가짜 `FakeProcessLauncher`)·못 띄우면 오류 창, [압축하기]·[압축 풀기…](하나·여럿). `SaveAsTests` — 처음 이름 '이름 - 복사본'+꼬리, 계획(같은 파일·모르는 형식 거절), 같은 형식은 바이트·수정 시각이 같은 복사본을 열고 숨김 임시 파일 없음(ZIP·7Z), 나뉜 7z·ZIP 은 조각 전부 새 이름으로·이미 있는 조각(다음 번호 포함)이면 안 함, ZIP↔7Z 바꾸기는 내용 왕복이 같고 temp\convert 가 안 남음, 암호 압축은 '같은 암호를 걸까요?'([같은 암호]면 암호 없이는 못 풂·[암호 없이]·[취소]면 파일 없음), 저장할 곳에 쓸 권한이 없으면 반쪽 파일 없이 '관리자 권한으로 일집 다시 열기' 질문. `DeleteArchiveTests` — [휴지통으로 옮기기]면 보내기 전에 닫힘(FileShare.None 으로 열림)·조각 전부 한 번에·시작 화면·최근에서 빠짐(ZIP·나뉜 ZIP·나뉜 7z), [취소]·창 닫기면 0번, 휴지통 없는 곳은 묻지 않고 알림, 못 옮기면 알리고 다시 엶, 옮기면 열어 둔 파일의 작업 폴더·감시도 놓음, (검증 지적) 못 옮기면 열어 고친 파일·감시는 그대로. (검증 지적) `StartScreenTests` 에 '없는 파일 정리'(찾을 수 없는 줄만 한꺼번에 묻고 빼기·[취소]면 그대로·없으면 알리기만 — NAS 줄은 빼지 않음) |
| `Contract/ElevationTests.cs` | [W2-11, I3-16] 관리자 권한으로 다시 시도(진짜 UAC 없음 — 가짜 `FakeProcessLauncher`·`FakeMappedDriveResolver`): 따옴표 싸기를 Windows `CommandLineToArgvW` 로 되읽어 같음(공백·따옴표·끝 '\'·빈 인자·한글), 동사별 인자 표(`--elevated` 맨 앞, 다른 곳에 풀기 `--extract-to`·여기에 `--extract-here`·압축 `--compress-to`·그 밖은 `--open` 으로 다시 열기), 상대 경로는 전체 경로로, 매핑 드라이브는 UNC·모르면 '네트워크 드라이브는…', 권한 오류만 권함(ArchiveException·공간 부족·잠김 등은 아님). 화면: 현재 사용자 '만들기 거부' 권한을 건 시험 폴더에 풀면(ZIP·7Z·우클릭 '이름' 폴더·여기에·여러 개는 요약 뒤 못 푼 것만) 질문 → 같은 인자로 띄움·아무것도 안 남음, [닫기]면 안 띄움, 관리자 창·이미 관리자면 안 권하고 제목 '(관리자)', UAC [아니요](1223)는 조용히·다른 실패는 오류 창, 암호는 인자에 없음, 고른 항목만은 다시 열기, 압축(만들 곳 권한 없음)은 `--compress-to`·`--elevated --compress-to` 로 뜬 창은 압축 창의 만들 곳이 그 폴더·설정 저장 안 함. (검증 지적 1차) 여럿을 한 폴더에 바로 풀다 막히면 모두를 `--extract-into 폴더 압축…`·그 명령은 메인 창 없이 그 폴더에 바로 풂, 읽기 전용 파일을 덮어쓰지 못하면 권하지 않고 '읽기 전용을 끄고' 오류 하나(ZIP·7Z — `ReadOnlyTargetException`), 쓰려던 곳이 네트워크(UNC)면 권하지 않음(`CanHelpWriteTo`), `--add-to` 는 같은 명령·그 밖의 고치기는 다시 열기, 제목 '(관리자)'는 '--elevated' 표시가 아니라 실제 권한 |
| `Contract/StartupDecisionTests.cs` | [W2-11] 켜질 때 결정(`StartupDecision` — App 에서 떼어 낸 순수 함수): 보통 동사는 예전대로(첫 일집·넘기기), `--new-window`·`--elevated` 는 늘 따로 뜸, 창 없이 풀기(재사용장치) 판정은 예전 그대로이고 `--elevated` 가 앞이면 아님, App 소스에서 뮤텍스·파이프가 건너뛰기 확인 안에 있음. (W2-12) 파일 연결 명령은 뮤텍스와 상관없이 `Associations`(자체 프로세스) — App 소스에서 창 없이 풀기 다음·뮤텍스 전에 처리하고 끝냄 |
| `Contract/FileAssociationRegistrarTests.cs` | [W2-12, I1-1] 파일 연결(`FileAssociationRegistrar` — **가짜 레지스트리 `FakeRegistryRoot` 에만**): 넣으면 기대 키·값 전체와 정확히 같음(.reg 모양 글자 그대로 — 따옴표·%1·아이콘·REG_NONE, iso 는 Capabilities 에 없음, UserChoice·형식 기본값 안 씀), 두 번 넣어도 같음, 남의 값(7-Zip OpenWithProgids·.zip 기본값·RegisteredApplications)은 그대로이고 빼면 처음과 같음, 빈 HKCU 에서 넣고 빼면 남는 것 0, 기록 없으면 아무것도 안 지움, 기록이 남의 키를 가리켜도 안 지움, 드롭 처리기는 고를 때만 zip·7z(+ CLSID InprocServer32 Apartment)·끄면 지움, 쓰기가 막히면 쉬운 한국어, [기본 앱 설정 열기]는 가짜 셸 한 번(진짜 ms-settings 안 엶), 설정 창 단추·명령줄 넣기·빼기(설정 `shell.associationsRegistered`·탐색기 알림 한 번), 설정 창 [확인]이 기록을 되돌리지 않음, `.z01` 을 열면 짝 `.zip`, 설정 `shell` 칸 JSON, `installer/Iljip.iss` 의 `[Registry]` 가 같은 키·값(`{app}`)·제거 플래그·`ChangesAssociations=yes`·아이콘 줄 |
| `Contract/ShellCommandLineTests.cs` | [W2-12, I1-2] 셸 확장이 일집을 부르는 명령줄(`ShellCommandLine` — C++ 의 C# 거울): Windows `CommandLineToArgvW` 로 되읽으면 한글·빈칸·`C:\`·끝 '\'·UNC 가 그대로, 글자 그대로 모양, 실행 파일 포함 정확히 30,000자면 그대로·한 글자 넘으면 목록 파일, 800개면 `temp\args\<guid>.lst`(UTF-8 BOM 없음·CRLF)로 넘기고 `ExpandListFile` 이 펼친 뒤 지움, 남의 파일·하위 폴더·다른 확장자는 읽기만, 없어진 목록이면 None, App 이 해석 전에 펼침(소스) |
| `Contract/ShellMenuTests.cs` | [W2-12, I1-2] '일집 ▶' 하위 메뉴 규칙(`ShellMenu`): 압축 하나·여럿·파일·폴더·섞임별 제목과 동사, 메뉴 이름 = 실제로 만드는 이름(`CompressNaming.SuggestBaseName` — 진짜 파일·폴더로), 40자 줄이기(서로게이트 쌍), 압축 확장자 = 파일 연결 − `.z01`, 자동 풀림 exe 는 압축 아님 |
| `Contract/SmartExtractTests.cs` | [W2-12, I1-2] 알아서 풀기(`--extract-smart`): 계획 표(폴더 하나·파일 하나·대소문자·'./'·역슬래시·여럿·빈 압축·자기 이름·'..'), 맨 위 폴더 하나 zip 은 두 겹 없이 바로(그 폴더를 엶), 여럿이면 '이름 (2)'(기존 폴더와 안 섞음), .gz 파일 하나는 옆에(폴더 안 엶), 바로 풀 때 같은 이름은 묻기([건너뛰기]면 그대로), 목록까지 잠긴 7z 는 한 번 묻고 바로·취소면 아무것도 없음, 여럿은 압축마다 따로·진행 창 하나·위 폴더 한 번, 관리자 다시 시도 명령 |
| `Contract/QuickCompressTests.cs` | [W2-12, I1-2] 창 없이 바로 압축(`--compress-zip`·`--compress-7z`): 폴더 하나 → '폴더.zip'(압축 창·질문·안내 없음, 진행 창 '압축 중...' 하나, 설정 저장 0), 여럿 → 그 폴더 이름·같은 이름이면 묻지 않고 '(2)', 7z 는 마지막 설정(수준·솔리드·시험)대로·날짜·암호 없음, 뺀 파일이 있으면 안내 하나, 원본이 없으면 오류 하나, 해석 |
| `Contract/CompressDialogRulesTests.cs` | [W2-07, I2-12] 한 화면 압축 창 규칙(`ViewModels/CompressionDialogViewModel` — 창 없이): 7Z 는 7z 칸·ZIP 방식 칸 숨김·이름 숨기기는 암호가 있을 때만, TAR·GZ·BZ2 는 암호·나누기 끔(GZ·BZ2 는 빼기·원본 지우기도), 날짜를 켜고 모양을 바꾸면 미리 보기와 PropertyChanged, 각각 따로(원본 둘 이상만·이름 칸 막음·'… 외 N개 (원본마다 하나씩)'), 형식을 바꾸면 제안 이름이 따라감(GZ 는 '보고서.xlsx', 고친 이름은 그대로), 처음 값은 설정(마지막 폴더·직접 입력 크기 포함), 7z.dll 이 막히면 7Z 없음·ZIP 으로 시작, [확인] 전 검사(빈 이름·못 쓰는 글자·없는 폴더·이상한 경로·GZ 에 폴더·GZ 에 여러 파일), 답에는 맞는 값만, 같은 이름(나눈 ZIP·7Z 첫 조각 포함)·비켜 만들 이름, 예전 옵션 창 어댑터, XAML 칸 순서·문구·코드가 VM 을 씀(소스). (검증 지적 1차) '만들 곳' 처음 값은 설정(원본 폴더·마지막 폴더·정해 둔 폴더 — 없으면 원본 폴더, 목록 겹치지 않게, 설정 창 칸 검사), 원본 그 자체면 [바꾸기] 없음('지금 압축하려는 원본'·답은 안 바꿈), GZ·BZ2 미리 보기의 날짜는 원래 확장자 앞, 설정 창이 folderChoice·fixedFolder·deleteOriginalsAfter 를 씀(소스) |
| `Contract/FileLockTests.cs` | 열기 실패 후 원본 파일을 잠그지 않는지(B8 고침) + 분할 EGG 조각들도 바로 지울 수 있는지 |
| `Contract/RarTests.cs` | (통합 점검) 합성 RAR4·RAR5 — 작성기를 반디집으로 검증, 목록·해제·풀린 수정 시각, 목록 시각은 현지 시각, 암호 없음/틀림 → 암호 예외·아무것도 안 남음(머리 암호는 '파일 이름까지…'), 이름 보이는 암호 RAR 은 암호 없이 목록, 손상 → CRC 오류(앞 파일 남김), 진행률, '/' 로 고른 선택 해제. (W2-02) 머리 암호 RAR 을 암호 없이 읽으면 `ListPasswordRequiredException`. (Wave 2 통합 점검) RAR4 DOS 벽시계는 7z.dll 이 쓴 '지금' 시간대 편차를 되돌려 그대로(`DosWallClock` — 서머타임 시작·끝 시각 포함, 이상한 시각에서 멈추지 않음) |
| `Contract/FormatFlowTests.cs` | (통합 점검) 형식이 달라도 화면 흐름이 같은가 — 가짜 창으로 ZIP·7z·EGG·RAR4·RAR5 암호 창 길(없음 → 틀림 → 맞음), ZIP(CP949·UTF-8)·7z·TAR·EGG·ALZ·RAR 한글 이름(목록·폴더 트리·풀린 이름), 모든 해제의 진행 막대 도중 보고, 목록 시각이 UTC 로 오지 않음, 암호 ZIP(AES·한글 ZipCrypto)도 읽는 도중 막힌 파일만 빼고 다시 써서 같은 암호로 풀림. (W2-02) 목록까지 잠긴 7z·RAR 은 첫 창이 `ListLocked`, 틀리면 '암호가 맞지 않아요' 경고 한 번 뒤 다시 |
| `Contract/ReuseDeviceContractTests.cs` | (통합 점검) 재사용장치(`재사용장치_processor.py`)가 기대는 `Iljip.exe --extract-to` 약속 — 실제 exe 로 egg·alz·RAR4·RAR5·분할 egg 는 종료 0, 암호·잘린 파일은 종료 1 이고 실패 문구가 '압축 파일 아님'으로 읽히지 않음(반디집 재시도 유지), 본체 문구 검사. (W2-02) 일집·py7zr 이 만든 헤더 암호 7z·머리 암호 RAR5 를 창 없이 풀면 묻지 않고 종료 1·'파일 목록까지 암호로 잠긴…'·아무것도 안 남김. (W2-05) 같은 폴더에 두 번 풀어도 묻지 않고 종료 0·'이름 (1)'·압축 파일 그대로. (Wave 2 통합 점검) 그 '이름 (1)' 이 7z.dll 길(7z·RAR5·LZH)·압축된 TAR·ZIP CP949 에서도 같고 임시 파일이 남지 않음. (IJ3-03) 인터넷에서 받은 첨부(압축에 Zone.Identifier ZoneId=3)를 창 없이 풀어도 풀린 파일에는 표시를 **붙이지 않음**(EGG·ALZ·RAR5·ZIP·7Z·TGZ — 재사용장치 엑셀 자동 처리를 막지 않게) |
| `Contract/InternetMarkTests.cs` | [IJ3-03, I2-15 사용자 결정] 인터넷에서 받은 압축(Zone.Identifier)을 일집 창·탐색기 우클릭(여기에·'이름' 폴더·알아서)으로 풀면 풀린 파일마다 표시를 **바이트 그대로**(ZoneId·ReferrerUrl·HostUrl) — 해제 길 전부(ZIP·7Z·LZH·TGZ·TAR·GZ·raw BZ2·XZ·EGG·ALZ·RAR5·RAR4), 이번에 만든 파일에만(원래 있던 파일·[건너뛰기]한 파일·같은 폴더의 압축 자체는 그대로, [덮어쓰기]한 파일은 붙임), 수정 시각은 압축에 적힌 그대로, 고른 항목만·암호 다시 묻기·손상으로 멈춤(앞 파일엔 붙음)·다 풀고 휴지통, 진행 창 단계 알림, 표시 없는 압축은 아무것도 안 함, 설정 `extract.keepInternetMark`(기본 켬·끄면 풀기·열기·Ctrl+C·끌기 모두 안 붙임·JSON 이름), 범위(`ExtractionScope.CreatedFiles`)가 raw bz2 도 적음 |
| `Contract/ThirdPartyLicenseTests.cs` | [IJ3-03, I2-15] 7-Zip 라이선스 원문 대조: `licenses\7-Zip-License.txt` 가 7-zip.org License.txt 와 같음(6031바이트·SHA-256·'1999-2025'), BSD 3·2조항 파일은 원문 절을 그대로 자른 것(연도 있는 저작권 줄, 기억으로 적었던 소스 파일 이름 없음), 안내의 7-Zip 부분은 원문 머리·unRAR 제한을 줄 그대로 + 판 안내(26.03), 빌드 출력(본체·시험)과 **발행 출력**(`dotnet publish --no-build` 를 임시 폴더에)에 안내·라이선스가 같은 바이트로, 프로젝트 파일의 복사 설정 |
| `Contract/Wave4IntegrationTests.cs` | (4차 통합 점검, 2026-10-18) 시험 로그 격리: 시험 프로세스의 일집 자료·로그 폴더는 시험 전용(`%TEMP%\IljipTests\appdata-<pid>`)이고 실제로 쓴 줄이 거기 남음, 사용자 PC 의 자료 폴더는 `%LOCALAPPDATA%\Iljip` 그대로, 시험이 띄우는 Iljip.exe 는 환경 변수 `ILJIP_TEST_APPDATA` 로 같은 폴더를 받음(시험 소스의 exe 띄우는 곳 전부 — 소스 훑기) |
| `Contract/Wave3IntegrationTests.cs` | (3차 통합 점검, 2026-10-15) IJ3-01·IJ3-03 × 앞 묶음: 같은 이름 [이름 바꾸기](W2-05)로 비켜 푼 사본에만 인터넷 표시(ZIP·7z.dll·EGG), 표시가 있는 압축을 고쳐도(W2-06) 압축 표시가 남고 다시 풀면 넣은 파일까지, 받은 압축 속 파일을 열어 고쳐 다시 넣어도 묻기 한 번·압축 표시 그대로, 나눠 만든 조각 묶음(W2-03 ZIP·7Z)·자동 풀림 exe(일집 스텁·7-Zip SFX)를 창에서 풀어도 표시. **오류 문구 전수(소스)** — 본체 전체 .cs 의 화면 호출·던지는 예외·XAML 글자에 영어만으로 된 문구가 없음(허용 셋 — 이유는 시험 안). 셸 확장 설치 스크립트의 `-SystemVolume`(기본은 그대로, BOM 유지) |
| `Contract/Wave2IntegrationTests.cs` | (Wave 2 통합 점검, 2026-10-14) 묶음 사이 상호작용: 우클릭 풀기('"이름"\ 에'·'여기에'·알아서 풀기)·풀기 창의 암호 흐름이 ZIP·7z·EGG·RAR4·RAR5(이름 보임·목록 잠김·한글 암호) 모두 같은 차례, 형식이 다른 같은 암호 압축 여럿은 한 번만 묻기, ZIP·7z(7z.dll)·EGG 를 한 폴더에 풀 때 같은 이름은 한 번만 묻고 건너뛴 압축은 지우지 않음, 나눠 만든 ZIP(AES)·7Z(한글 암호·파일 이름 암호)도 진짜 시험기로 시험하고 원본 지우기·망가진 조각은 잡음, 압축 속 파일을 연 채로 넣기·이름 바꾸기를 해도 다시 넣기가 그것을 잃지 않음, 확장자가 바뀐 ZIP(CP949)·EGG·ALZ·RAR4 첨부도 창 없이 풀기가 원래 처리기로(한글 이름 그대로) |
| `Contract/BugEvidenceTests.cs` | 버그 '지금 이렇다' 근거(고쳐지면 실패하는 게 정상). 지금은 안 고친 버그가 없어 B3 원인 사실 기록 2개만(반디집 한글 암호 = CP949, 일집 AES = UTF-8) |
| `Contract/FixedBugTests.cs` | 고친 버그 B1·B2(점뿐인 이름 포함)·B6·B7·B9, **B3·B4·B5(W1-C)** — 다시 생기면 안 됨(예전 근거 테스트를 올바른 기대로 뒤집은 것, 계약) |
| `Contract/ZipPasswordProbeTests.cs` | (W1-C) ZIP 암호 판정 — 반디집 한글 암호 = CP949, 일집 AES = UTF-8, 첫 항목만 통과하는 틀린 암호 = None, 판정 못 하는 모양(잘림·분할·SFX·쓰레기) = 예외 없이 Unknown, 두 후보가 다 통과할 때 가려내기·대안, '확인됨'(근거 16비트) |
| `Contract/ZipEncryptionTests.cs` | (W1-C) ZipCrypto·AES 만들기(영문·한글·이모지 암호) 왕복, 반디집 한글 AES 선택 해제, **맞는 암호 + 깨진 AES·ZipCrypto 데이터는 손상으로 알리고 앞 파일은 남김**, 잘린 AES ZIP, 두 후보가 다 통과하는 한글 ZipCrypto 도 풀림(`TestSupport/RawArchives.cs` 의 `RawZipCrypto` 로 결정적으로 만듦) |
| `Contract/PasswordPolicyTests.cs` | (W1-C) 압축 옵션 창의 암호 판정(두 칸 일치·8자 권장, 한글 암호 호환 경고 표) |
| `Contract/BandizipCliTests.cs` | (W1-C) 일집이 한글 암호로 만든 ZIP 을 반디집 콘솔 `bz.exe t`(시험만)로 풀어 봄 — ZipCrypto 는 풀림(계약), AES 는 못 풂(기록). bz.exe 가 없으면 건너뜀. (W2-02) 일집 7z(AES-256 + 한글 암호 + 파일 이름 암호화 + 빈 폴더)를 반디집이 맞는 암호로 풂(틀린 암호로는 못 풂) |
| `Contract/FixtureCoverageTests.cs` | 매니페스트의 모든 픽스처가 어느 테스트엔가 쓰이는지, 파일이 다 있는지, 시간대 스위치가 먹었는지, (Wave 2 통합 점검) 흉내를 켜도 `TestTimeZone.OsLocal` 이 이 PC 의 진짜 시간대인지 |
| `Contract/EntryWorkspaceTests.cs` | [IJ3-01 — W2-04 보관본에서] 바탕 서비스(창 없이): 작업 폴더 `ArchiveWorkspace`(key16, `temp\open\key16\순번`, owner.lock, 닫으면 지우되 잡힌 파일만 남김, 다른 일집이 쓰는 중이면 `key16-2`, 고친 파일 판정), 청소 `TempJanitor`(주인이 꺼진 open·drag 는 지우고 살아 있는 것은 남김, 잠금이 없는 preview 는 1시간·list·convert·args 는 24시간 지난 것만(IJ3-01 — 다른 일집이 쓰는 중인 것을 지우지 않게), clip 은 24시간(가짜 시계), 청소 시작 뒤 생긴 폴더는 안 건드림, 창 없이 풀기에서는 안 부름 — 소스), 인터넷 표식 `ZoneIdentifier`(IJ3-03: 붙여도 수정 시각·읽기 전용 그대로, 없는 파일은 건너뜀, 64KB 넘는 비정상 표식은 영역 번호만), 실행 파일 판정, 설정 `settings.json` |
| `Contract/EntryOpenTests.cs` | [IJ3-01, I1-3] 가짜 셸로 압축 속 파일 열기: 그 파일만 `temp\open\key16\순번\파일 이름`에 꺼내 한 번 열기(깊은 폴더도), 다시 열면 새 순번, 실행 파일은 먼저 묻고(기본 [취소]) 취소면 꺼내지도 않음·설정 `open.warnExecutables` 로 끔, 암호 ZIP 은 한 번 묻고 틀리면 다시·취소면 아무것도 안 남김, 머리 암호 7z, 인터넷 표식, 폴더 행, 열 프로그램 없음 안내, 닫을 때 고친 파일은 W2-06 의 다시 넣기 질문(`ConfirmExitAsync`), 압축을 바꿔도 연 파일은 앱을 닫을 때까지, 열기·풀기·복사·정리가 설정을 쓰지 않음, 미리 보기 칸 설정 적용·`SaveOnExit` 로 저장, 창 껍데기 연결(소스) |
| `Contract/DragOutTests.cs` | [IJ3-01, I1-4] 끌어 꺼내기: 서술자(`DragPayloadBuilder` — 폴더 재귀·한글 이름·이름 겹침 '(2)'·'..'·금지 글자·259글자, 폴더 2만 개도 한 번만 훑음), 지연 꺼내기(`LazyEntryExtractor` — 부르기 전엔 안 만듦·첫 요청에 한 번에(솔리드 7z 40개도 서비스 호출 1번)·경로 공격 항목이 섞이면 안전한 파일만 한 번에·손상 항목 하나로 실패하면 항목마다·취소하면 남은 요청도 E_ABORT·암호), COM 직접 호출(`VirtualFileDataObject`), 화면(놓기 전엔 안 꺼냄·끌기 전에만 암호·진행 창 하나와 취소·오류 창 한 번·긴 경로 안내·닫으면 정리) |
| `Contract/ClipboardCopyTests.cs` | [IJ3-01, I1-4] Ctrl+C(가짜 클립보드): 고른 파일·폴더를 `temp\clip\guid` 에 꺼내 최상위 경로를 '복사'로, 취소면 클립보드·사본 없음, 암호 한 번, 표식, 사본 2개·닫을 때 1개, 디스크 이름이 바뀌는 항목(`: ? * \| " < >`·장치 이름·점만·폴더)을 빠짐없이 + '바꿔서 복사했어요' 알림(폴더 안 것도), 수천 개 — 정리는 화면 스레드 밖·같은 진행 창 단계 알림·정리 중 취소·겹쳐 누르기, 엔진 열쇠 고르기 빠르기·같은 결과, 진짜 클립보드 데이터 모양 |
| `Contract/ImagePreviewTests.cs` | [IJ3-01, I3-7] 그림 미리 보기(가짜 그림 읽기): 고른 그림만 꺼내 보이고 임시 파일 바로 지움, 이전/다음, 그림 아니면 꺼짐, 50MB 초과·화소가 너무 많은 그림은 자리표시, 최근 3장·200MB, 확대, 암호 단추, 못 읽는 그림, 다른 압축을 열면 비움, 압축 세대 — 읽는 도중·압축을 바꾸는 바로 그 순간 시작한 읽기·고친 뒤 같은 경로의 다른 그림·세대만 바뀐 경우에도 옛 그림을 안 보임, 진짜 WIC 작은 PNG·그림 폭탄 PNG 거절·줄여 읽기 계산 |
| `TestSupport/` | 임시 폴더, 매니페스트 읽기, 비교 도우미, 최소 ZIP/TAR/GZIP/EGG(분할·**ZipCrypto 암호** 포함)/ALZ 작성기, ZipCrypto 검사 계산기·암호화기, 반디집 콘솔 실행기(`BandizipCli`), (W1-D) 가짜 창 `FakeUserInteraction`·VM 시간 제한 실행 `VmRunner`·STA 스레드 `Sta`·저장소 소스 읽기 `RepoFiles`, (통합 점검) 최소 RAR 작성기 `RawRar.cs`(`RawRar5`·`RawRar4` — W2-01 에 끝 머리 빼기 `withEndArc`, 머리 경계를 주는 `RawRar`)·같은 내용의 합성 RAR4/RAR5/EGG/ALZ 만들기 `SyntheticArchives`, (W2-01) 시험용 7z·xz 만들기 `SevenZipTestArchives`(7z.dll 직접 — 풀기 시험 재료), (W2-02) py7zr 이 만든 헤더 암호 7z 표본 `Py7zrSamples`(base64, 426바이트 두 개), 최소 CAB 작성기 `RawCab`, 시험용 ISO 만들기 `DiscImages`(Windows IMAPI2FS, 없으면 `DiscImageFact` 로 건너뜀), (W2-05) 가짜 셸·휴지통·설정 `FakeServices`(`TestServices`), `ModuleInit` 이 일집 자료 폴더를 시험 전용 빈 폴더로, (W2-06) ZIP 원본 바이트 읽기·데이터 서술자 ZIP 만들기 `ZipInspect`, 가짜 파일 감시 `FakeFileChangeSource`(`TestServices.FileChanges`), 가짜 창 글 묻기 `FakeUserInteraction.AskText`(`TextAnswers`), (W2-07) 가짜 한 화면 압축 창 `FakeUserInteraction.AskCompression`(`CompressionRequestAnswer` — 비면 예전 `SavePath` + `CompressionOptions` 답으로), 가짜 시계 `FakeClock`(`TestServices.Clock`)·만든 압축 시험기 `FakeCompressTester`(`TestServices.CompressTester` — 기본은 진짜 시험기에 맡기고 기록만, `Pass()`·`Fail()`·`FailFor`), (W2-07 검증 지적 2차) 띄우지 않고 창 만들기 틀 `WpfAppScope`(앱 객체·테마 자원·묶기 오류 기록 `BindingErrorLog`), 조건으로 진행 창 [취소] `FakeUserInteraction.CancelWhen`, 가짜 휴지통의 끼워 넣기 `FakeRecycleBin.OnIsRecyclable`·`OnSend`, (W2-09) 가짜 풀기 창 `FakeUserInteraction.AskExtractTarget`(`ExtractTargetAnswer` — 비면 예전 `FolderToPick` 답으로), 옛 방식 이름 바이트 TAR `RawTar.Entry.RawName`, (W2-10) 확인란이 있는 가짜 선택 창 `FakeUserInteraction.ChooseWithCheck`(`CheckChoicePrompts`·`CheckAnswer` — 버튼은 `Choose` 와 같은 규칙으로 고르고 `ChoicePrompts` 에도 남김), (W2-11) 가짜 일집 띄우기 `FakeProcessLauncher`(`TestServices.Processes` — 새 창·관리자 다시 시작의 실행 파일·인자만 기록, `ThrowOnElevate`·`ThrowOnNewInstance`)·가짜 네트워크 드라이브 `FakeMappedDriveResolver`(`TestServices.MappedDrives` — `Map`·`Unresolvable`), (W2-12) 가짜 레지스트리 `FakeRegistryRoot`(`TestServices.Registry` — 메모리 트리·대소문자 무시·`DenyWrites`·정렬한 .reg 모양 `ExportReg` — 진짜 HKCU 는 시험용 하위 키조차 안 씀), 가짜 셸의 `DefaultAppsOpened`·`AssociationsChangedNotified`(진짜 ms-settings·SHChangeNotify 안 부름), 진행 창 제목 `FakeUserInteraction.ProgressTitles`, (IJ3-01) 가짜 클립보드 `FakeClipboard`(`TestServices.Clipboard` — 넣은 목록·스레드만, 진짜 클립보드 안 씀)·가짜 그림 읽기 `FakeImageDecoder`(`TestServices.Images` — `Gate`·`FailName`·`TooLargeName`), 일집 자료 폴더를 시험 폴더로 `TestServices.UseAppRoot`, 작은 ZIP 만들기 `TestZip`, 화면 스레드 흉내 `DispatcherThread`(창 없는 WPF 디스패처 STA 스레드) |
| `Fixtures/` | 픽스처(아래) |
| `MANUAL_CHECKLIST.md` | 버전 바꿀 때 사람이 10분 안팎에 확인하는 점검표 |

## 계약 테스트와 기록 테스트

같은 동작에 대해 두 종류를 **다른 테스트로 나눠** 두었습니다. 한 테스트 안에서 엔진 고유 값(예외 타입 등)을 먼저 단언하면,
엔진을 바꾼 직후 그 단언이 먼저 실패해 뒤의 '파일이 남지 않는다'·'대상 폴더 밖에 안 쓴다' 같은 핵심 계약 결과가 가려지기 때문입니다.

- **계약 테스트**(이름에 `기록_` 없음, Trait 없음): 엔진과 무관하게 지켜야 할 것 — 해제 내용 sha256, 한글 이름, zip-slip 방어(대상 폴더 밖 무사),
  예외가 나면 대상 폴더 오염 없음, 취소 시 부분 파일 정리, 동명 파일 비켜 쓰기, 진행률 끝 '완료', `IsPasswordRelated` 판정.
  목록은 '파일 항목은 정확히, 폴더 항목은 있어도 없어도(있다면 기대 목록 안의 것)' 만 봅니다(`TreeAssert.ListFilesEqual`).
- **기록 테스트**(이름이 `기록_` 으로 시작하거나 근거 테스트, `[Trait("Kind","Record")]`, W2-02 후 157개): 지금 엔진의 예외 타입·문구,
  목록 모양(일집 ZIP·TAR 모두 빈 폴더만 폴더 항목), ZIP 방식 번호(Store 도 8), EFS 플래그, 라우팅 표, 버그 근거.
  엔진이 바뀌면 **달라지는 게 당연**하니 새 값으로 고치면 됩니다. 단, 고치기 전에 같은 동작의 계약 테스트가 통과하는지 먼저 보세요.

## 픽스처 — 파일 자체가 원본입니다

- `Fixtures/bandizip/` : 반디집 7.46 `bz.exe` 로 만든 압축 파일들. **반디집을 지우면 다시 만들 수 없으니 지우거나 고치지 마세요.**
- `Fixtures/python/` : 반디집이 못 만드는 경우(파일명 보이는 7z 암호, NFD 이름 zip, `.zip.001`, tbz2, bz2)를 Python 으로 만든 것.
- `Fixtures/local/` : 반디집 SFX 스텁이 든 자동 풀림 exe — 재배포 불가라 **git 제외**. 없으면 해당 테스트는 자동으로 건너뜁니다.
- `Fixtures/src/` : 압축하기 전 원본 샘플(합성 데이터 — 견적서 문구도 '테스트용 가상품목', 실제 업무와 무관).
  **`src/일집_샘플/빈폴더`(빈 폴더)는 git 이 기록하지 않아 새로 클론하면 없습니다 — 정상입니다.** 매니페스트의 일집_샘플 계열
  expected_entries 에는 `일집_샘플/빈폴더`(폴더)가 있고 반디집 픽스처 안에도 들어 있습니다. 클론한 src 로 다른 도구(7-Zip 등)를 써서
  기대값을 다시 대조할 일이 있으면 빈 폴더를 직접 만드세요(`.gitkeep` 을 넣으면 '빈 폴더'가 아니게 되어 기대값과 어긋나므로 넣지 않음).
- `Fixtures/manifest.json` : 각 픽스처의 기대 목록(경로·크기·sha256)과 만든 명령, 반디집이 보여 준 모양(`bandizip_view`).
- 크기: 커밋 대상 약 1.9 MiB(2026-10-12 재생성 — 분할 원본 난수를 1 MiB → 160 KiB, 볼륨을 400k → 64k 로 줄여 3조각 유지).
  가장 큰 `iso_default.iso`(723 KB)는 대부분 0 채움이라 git 압축으로 거의 사라집니다.
- 수정 시각: 모든 원본은 `2026-01-02 03:04:06 KST` = 절대 시각 `2026-01-01T18:04:06Z`.
  tar·7z 는 절대 시각을, ZIP 은 DOS '벽시계'(03:04:06)를 저장하므로 테스트는 형식별로 비교합니다(`MtimeExpect`) — 어느 시간대 PC·CI 에서든 같은 결과.
- 테스트는 픽스처를 출력 폴더로 복사해서 읽기만 합니다(쓰기는 전부 임시 폴더).
- `.gitattributes` 의 `* -text` 때문에 줄바꿈 변환이 일어나지 않습니다(sha256 유지).

### 다시 만들기 — 커밋한 뒤에는 하지 마세요

- 반디집이 설치된 **한국 시간(UTC+9) PC** 에서만: `python -I Fixtures\make_fixtures.py`
- 안전장치: 시스템 임시 폴더에 전부 새로 만들고, **모든 픽스처가 만들어지고 검증이 하나도 틀리지 않았고 기존 매니페스트의 픽스처가
  하나도 빠지지 않았을 때만** 기존 `src/`·`bandizip/`·`python/`·`local/`·`manifest.json` 과 바꿔 넣습니다. 실패하면 기존 파일은 그대로입니다
  (2026-10-12 실제로 바꿔 넣기 도중 DLP/동기화 잠금으로 실패했을 때 기존 파일이 그대로 되돌려지는 것 확인).
  `--out <폴더>` 를 주면 그 폴더에만 만들고 바꿔 넣지 않습니다(재현성 비교용). bz.exe 가 없으면 아무것도 건드리기 전에 종료 코드 2.
- 다시 만들면 **같은 내용이어도 10개 파일의 바이트가 바뀝니다** — 암호 파일 8개(난수 salt/IV)와 tgz·tar.gz(gzip 머리 시각).
  나머지 41개는 원본의 수정·접근·만든 시각을 고정하고 임시 폴더에서 만들어 매번 같습니다. 의미 없는 바이너리 변경이 이력에 쌓이니 커밋 후에는 다시 만들지 마세요.
- ZipCrypto 픽스처가 바뀌면 'B5/B4 재현용, 1바이트 암호 검사를 우연히 통과하는 틀린 암호'도 바뀌므로 테스트가 매번 계산합니다(`ZipCryptoLucky`).

## Skip(건너뜀) 목록의 의미

Skip 은 두 종류입니다. 사유 문구는 `TestSupport/Known.cs` 한 곳에 모여 있습니다.

- **`known gap …`** — 기능 대조표(`D:\Python\_기능대조_알PDF_반디집.md`)의 알려진 빈틈. 테스트 본문은 '반디집처럼 됐을 때의 올바른 기대'입니다.
  엔진을 바꾼 뒤 **Skip 만 지우고** 돌려 보면 고쳐졌는지 바로 압니다.
- **`bug Bn …`** — 테스트를 쓰다 찾은 본체 버그 중 **아직 안 고친 것**. 본문은 역시 '고쳐졌을 때의 기대'입니다.
  지금은 하나도 없습니다 — 고친 버그(B1~B10)와 TAR-빈폴더·I2-11 은 아래 '고친 버그' 표로 옮겼습니다.

| 사유 | 무엇 | 지금 실제 동작(관찰) |
|---|---|---|
| I1-7 | 헤더(파일명) 암호 7z/RAR 를 열 때 암호를 안 물음 | **Skip 아님** — 7z 는 W1-D 1차 검증, RAR 머리 암호는 통합 점검에서 고침(`CommandLineTests.헤더암호_7z…`·`RarTests.머리암호_RAR_…`). W2-01 에서 7z.dll 로 바뀐 뒤에도 같은 흐름(`CommandLineTests.헤더암호_7z_여기에_풀기는_7z_dll로도_…`). W2-02 에서 전용 예외·경고까지 메움(`ListPasswordFlowTests`) |
| I2-1 | 7z 만들기 | **Skip 아님** — W2-02 에서 메움(`KnownGapTests.압축_7z_왕복이_원본과_같다` Skip 해제, `SevenZipCreateTests`·`SevenZipCreateLargeTests`) |
| I2-2 | 분할 열기·만들기 | **Skip 아님** — 열기는 W2-01(`KnownGapTests.숫자분할_…`), 만들기는 W2-03(`SplitCreateTests` — 새 계약) |
| I2-10 | 맥 NFD 이름 | **Skip 아님** — W2-09 에서 메움(`BandizipFixtureTests.NFD_이름이_NFC로_보이고_NFC로_풀린다` Skip 해제, `NameEncodingTests`) |

### 남은 Skip 0개(2026-10-18 4차 통합 점검 후) — 알려진 빈틈도, 안 고친 버그(Bn)도 없음

`Known.I2_1` 은 지웠습니다(W2-02). `Known.I2_2` 는 W2-03 에서 지웠습니다(쓰는 Skip 이 없음 — 만들기 계약은 Skip 없이 `SplitCreateTests` 로). `Known.I2_9` 는 지웠습니다.
`Known.I2_10` 은 W2-09 에서 지웠습니다(마지막 Skip). 환경 조건으로 건너뛰는 시험(반디집 콘솔·로컬 SFX 픽스처·IMAPI2FS·셸 확장 DLL 이 없는 PC)은 위 '실행 방법' 대로입니다.
Wave 2 에서 검증에 걸려 되돌린 W2-04(끌어 꺼내기·Ctrl+C I1-4·그림 미리 보기 I3-7·압축 속 파일 열기 I1-3 의 나머지)는 IJ3-01 이 보관분(`stash` `iljip-wave2-W2-04-failed` — drop 안 함)을
`git stash apply` 로 얹어 다시 들였습니다(아래 'IJ3-01 에서 더한 것'). W2-08(압축 검사 `--test` I2-7·오류 원인 문제 창 I2-8·깨진 ZIP 고치기 I3-12 — `iljip-wave2-W2-08-failed`)은
Skip 으로 남기지 않았습니다(시험도 보관분 안에 있음 — 다시 할 때 같이 들임). 3차에서 W2-08 을 다시 한 IJ3-02 도 검증에서 되돌려 `stash@{0}` `iljip-wave3-IJ3-02-failed` 에 있습니다
(시험 `ArchiveTestReview*Tests` 등도 그 안 — Skip 으로 남기지 않음). 4차에서 IJ3-02 를 다시 한 I4-1 도 3차 검증에서 되돌려 `stash@{0}` `iljip-wave4-I4-1-failed` 에 있습니다
(IJ3-02 보관분은 `stash@{1}` 로 밀림 — 시험도 보관분 안, Skip 으로 남기지 않음).

### 4차 통합 점검에서 더한 것(2026-10-18) — I4-1 은 stash, 시험 로그 격리만 들임

푼 Skip·더한 Skip 없음. 새 시험 3개(`Wave4IntegrationTests`). 기존 시험 하나는 더 엄격해짐 — `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_…` 가 시험 자료 폴더의 logs 에서 같은 줄을 찾고,
사용자 PC 의 자료 폴더(`AppPaths.DefaultRoot()`)가 `%LOCALAPPDATA%\Iljip` 그대로임도 확인합니다. 전체 2408 → 2411, 실패 0(Debug·Release·UTC 세 번).

| 무엇 | 예전 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 시험이 쓰는 일집 로그 | 사용자 진짜 로그 `%LOCALAPPDATA%\Iljip\logs`(전체 시험 한 번에 수백 KB — IJ3-02 시험은 하루 85MB·255MB) | 시험 자료 폴더 `%TEMP%\IljipTests\appdata-<pid>\logs`(끝나면 지움) | `시험의_자료_폴더와_로그는_사용자_폴더가_아니다` |
| 시험이 띄우는 Iljip.exe 의 로그·설정·임시 폴더 | 사용자 폴더 | 환경 변수 `ILJIP_TEST_APPDATA` 로 시험 자료 폴더(본체는 비지 않은 절대 경로일 때만 받음) | `시험이_띄우는_exe_에는_…`·`시험이_Iljip_exe_를_띄우는_곳은_모두_…`·`ReuseDeviceContractTests.창없이_풀기가_실패하면_…` |

### 3차 통합 점검에서 더한 것(2026-10-15) — IJ3-01·IJ3-03 을 합친 뒤

푼 Skip·더한 Skip 없음. 새 시험 14개: `Wave3IntegrationTests` 13(Theory 줄 포함)·`ReuseDeviceContractTests` +1(실제 exe). 제품 동작을 겨냥한 상호작용 시험 11개는 처음부터 모두 통과(어긋남 없음).
기존 시험의 기대는 바꾸지 않음 — `ThirdPartyLicenseTests.발행_출력에도_…` 는 판정 그대로, 'MSB3491'(보안 프로그램이 obj 파일을 잠깐 잡음 — 첫 UTC 실행에서 한 번 실패)일 때만 발행을 두 번까지 다시.
전체 2394 → 2408, 실패 0(Debug·Release·UTC 세 번).

| 무엇 | 예전 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 같은 이름 [이름 바꾸기] × 인터넷 표시 | (따로 시험 없음) | 'a (1).txt' 에 붙고 원래 'a.txt' 는 그대로 — SharpCompress·7z.dll 임시 폴더·EggDotNet 길 | `같은_이름을_이름_바꾸기로_…`·`EGG도_이름_바꾸기로_…` |
| 압축 고치기·다시 넣기 × 인터넷 표시 | (따로 시험 없음) | 고친 압축(`File.Replace`)에 표시가 남고 다시 풀면 넣은 파일까지, 연 파일을 고쳐 넣어도 묻기 한 번 | `표시가_있는_압축을_고쳐도_…`·`표시가_있는_압축에서_연_파일을_…` |
| 나눠 만든 조각·자동 풀림 exe × 인터넷 표시 | (따로 시험 없음) | 창에서 풀면 붙음 | `나눠_만든_조각_묶음도_…`·`일집이_만든_자동풀림_exe를_…`·`다른_프로그램이_만든_…` |
| 화면·예외 문구 | 묶음마다 자기 문구만 | 본체 전체 소스·XAML 훑기 — 영어만으로 된 문구 0(심은 영어 문구 둘을 잡는 것 확인) | `화면과_예외_문구에_영어만으로_된_글이_없다` |
| 창 없이 풀기 실패 로그 | 'Headless extract failed'(경로 없음), 다른 일집과 겹치면 기록을 잃을 수 있음 | `Headless extract failed: <압축 경로>`(표준 오류 그대로), 공유·잠금 위반이면 5번까지 다시 씀 | `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_…`(머리를 되돌리면 실패 — 확인) |
| 셸 확장 설치 | 늘 기본 볼륨(이 PC 는 D: — 0x80070032 위험) | `-SystemVolume` 이면 C: 앱 저장소, 0x80070032 면 쉬운 안내 | `셸_확장_설치는_시스템_볼륨을_…` |

### IJ3-03 에서 더한 것(2026-10-15) — 인터넷 표시(MOTW) 전파·7-Zip 라이선스 원문 대조(사용자 결정 I2-15)

푼 Skip·더한 Skip 없음. 새 시험 49개: `InternetMarkTests` 37(Theory 줄 포함)·`ThirdPartyLicenseTests` 7·`EntryWorkspaceTests` +3·`ReuseDeviceContractTests` +1(실제 exe)·`CompressDialogXamlTests` +1.
`SevenZipLibraryTests` 의 라이선스 파일 목록에 `7-Zip-License.txt` 한 줄을 더함(더 엄격). 기대를 바꾸거나 약하게 한 기존 시험 없음 —
`KnownGapTests.기록_웹에서_받은_표시를_풀린_파일에_옮기지_않는다` 는 본문 그대로(서비스는 여전히 옮기지 않음), 설명만 '창 없이 풀기가 부르는 서비스 수준 기록'으로.
전체 2345 → 2394, 실패 0.

| 무엇 | 예전 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 일집 창·우클릭으로 풀기 | 받은 압축을 풀어도 풀린 파일에 표시 없음 — 오피스가 보통으로 엶 | 이번 풀기가 만든(덮어쓴) 파일에만 원본 표시를 바이트 그대로(ZoneId·ReferrerUrl·HostUrl), 다 풀었을 때·손상으로 멈췄을 때 모두, 풀기 전에 읽음(휴지통으로 보내도), 진행 창 단계 알림 | `InternetMarkTests` 의 풀기·우클릭 시험 |
| 붙이는 범위 | — | 같은 이름 처리 범위가 적은 '이번에 만든 파일'(`ExtractionScope.CreatedFiles`) — 풀 곳의 원래 파일·[건너뛰기]한 파일·같은 폴더의 압축은 그대로. raw bz2(장부 없이 씀)도 `OutputTarget.Commit` 이 적음 | `원래_있던_파일과_건너뛴_파일에는_…`·`범위는_raw_bzip2로_…`(고치기 전엔 bz2 시험 2개 실패 — 손으로 확인) |
| 창 없이 풀기(재사용장치) | 표시 없음 | 그대로 **없음**(설정과도 상관없음) | `ReuseDeviceContractTests.창없이_풀기는_인터넷에서_받은_표시를_…`(실제 exe)·`KnownGapTests` 기록 |
| 표시를 붙인 파일의 수정 시각 | (IJ3-01 열기·Ctrl+C) 표시를 쓰면 NTFS 가 '지금'으로 바꿔 압축에 적힌 시각이 사라짐 | 원래 시각으로 되돌림, 읽기 전용도 그대로 | `EntryWorkspaceTests.표식을_붙여도_수정_시각과_…`·`InternetMarkTests.표시를_붙여도_수정_시각은_…` |
| 아주 큰 표식 | 통째로 복사 | 64KB 넘으면 영역 번호만(디스크 채우기 방지) | `EntryWorkspaceTests.아주_큰_표식은_…` |
| 설정 | 없음(열기·끌기·Ctrl+C 는 늘 붙임) | `extract.keepInternetMark`(기본 켬, ☰ ▸ 설정 칸) — 끄면 풀기·열기·끌기('ZoneIdentifier' 형식도)·Ctrl+C 모두 안 붙임 | `설정을_끄면_…`·`열기는_설정을_따른다`·`Ctrl_C_는_…`·`끌기는_…`·`CompressDialogXamlTests.설정_창의_인터넷_표시_칸은_…` |
| 7-Zip 라이선스 | 기억으로 적은 BSD 저작권 줄(연도 없음)·소스 파일 이름·'public domain' 줄·1999-2026 | 7-zip.org 원문 `7-Zip-License.txt`(바이트 그대로), BSD 파일·안내는 원문 줄 그대로 + 판 안내(원문 1999-2025, 동봉 26.03) | `ThirdPartyLicenseTests` |
| 빌드·발행 출력 | 빌드 출력만 시험 | 빌드(본체·시험)와 발행(`dotnet publish --no-build` 를 임시 폴더에) 출력에 안내·라이선스 11개가 같은 바이트로 | `ThirdPartyLicenseTests.빌드_출력에_…`·`발행_출력에도_…` |

### IJ3-01 에서 더한 것(2026-10-14) — W2-04 다시: I1-3 바로 열기 나머지·I1-4 끌어 꺼내기·Ctrl+C·I3-7 그림 미리 보기

W2-04 보관분의 시험 5파일(`EntryWorkspaceTests`·`EntryOpenTests`·`DragOutTests`·`ClipboardCopyTests`·`ImagePreviewTests`)과 도우미(`DispatcherThread`·`TestZip`·`FakeClipboard`·`FakeImageDecoder`)를
지금 main(W2-06 의 열기·다시 넣기, W2-05 의 설정·`AppServices`) 위에 들이고, 검증에서 지적된 네 가지를 고쳤습니다(아래 표 — 보관분 기록은 그 아래 'W2-04 보관본에서 더한 것').
전체 2175 → 2345(+170 — 보관분 시험 5파일과 이번에 더한 시험, Theory 줄 포함). 푼 Skip·더한 Skip 없음.

| 무엇 | 보관분(W2-04) 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| (a) 미리 보기 — 압축을 바꾸는 순간 시작한 읽기 | 미리 보기 쪽에서만 세대를 세고, 그 세대를 화면의 '지금 압축' 을 바꾸기 **전에** 올림 — 그 틈(옛 목록을 비울 때 선택이 바뀌어 미리 보기가 불림)에 시작한 읽기가 옛 압축에서 꺼낸 그림을 새 세대로 기억해, 새 압축의 같은 경로 그림 대신 보임. 고친 뒤 목록을 다시 읽어도(같은 압축) 옛 그림을 기억에서 꺼냄 | 압축 세대는 화면(`MainViewModel.BumpArchiveGeneration`)이 지금 압축(경로·암호)을 바꾼 **뒤** 올림 — 열기(같은 압축 다시 열기 포함)·고친 뒤 다시 읽기·닫기. 꺼내기는 넘겨받은 세대가 지났으면 하지 않고, 기억은 (세대, 경로)로, 끝났을 때 세대가 다르면 기억도 표시도 안 함(멈춘 '불러오는 중'은 비움) | `ImagePreviewTests.압축을_바꾸는_순간에_시작한_읽기도_…`·`세대가_바뀌면_…`·`꺼내는_도중_세대가_바뀌면_…`·`고친_뒤_같은_경로에_다른_그림이_오면_…`·`읽는_도중_다른_압축을_열면_…`(보관분) |
| (b) Ctrl+C — 디스크에서 이름이 바뀌는 항목 | 자리는 찾았지만(`LocateExtracted`) 이름이 바뀌었다는 말이 없음 | `: ? * \| " < >`·장치 이름·점만 있는 이름·이름이 바뀌는 폴더 모두 빠짐없이 안전한 이름으로 넣고, 넣은 뒤 '바꿔서 복사했어요: - 원래 → 바뀐'(5개 + 외 N개, 고른 폴더 안의 것도 처음 바뀐 조각 하나씩 — `ClipboardCopyPlan`), 상태줄에 바꾼 개수 | `ClipboardCopyTests.이름이_바뀌는_항목은_빠짐없이_…`·`폴더를_고르면_그_안에서_…`·`폴더_안에서_바뀌는_이름은_…` |
| (c) 끌어 꺼내기 — 큰 폴더·솔리드 압축 | 묶음 만들기가 고른 폴더마다 압축 전체를 다시 훑음(폴더 수 × 항목 수), 경로 공격 항목이 하나라도 섞이면 한 번에 꺼내기가 통째로 실패해 모든 파일을 하나씩(솔리드면 제곱), 진행 창 [취소] 뒤에도 탐색기가 달라는 남은 파일을 하나씩 다시 꺼냄(진행 창도 다시) | 묶음 만들기·열쇠 고르기·끌기 전 암호 확인은 압축을 한 번만 훑음, 공격 항목이 섞이면 안전한 파일 열쇠만 넘겨 한 번에(공격 항목만 따로 실패), [취소]하면 그 끌기의 남은 요청은 꺼내지 않고 E_ABORT. 손상 항목 하나로 한 번에 꺼내기가 실패한 경우만 예전처럼 항목마다 | `DragOutTests.폴더_수천_개를_끌어도_…`·`경로_공격_항목이_섞인_폴더는_…`·`한번에_꺼내기가_실패하면_…`(진짜 손상 항목으로 새로)·`큰_항목은_…(취소)`·`솔리드_7z_폴더를_끌면_…`·`한번에_꺼낼_때는_…` |
| (d) Ctrl+C — 수천 개 | 꺼낸 뒤 자리 찾기를 화면 스레드에서 고른 것마다 폴더 전체를 다시 읽어(제곱) 진행 창도 [취소]도 없이 굳음. 엔진의 열쇠 고르기도 항목 수 × 열쇠 수 | 꺼내기와 정리(자리 찾기·표식)를 한 진행 창으로 — 정리는 뒤 스레드에서 단계 알림 '복사할 파일을 정리하는 중...'·[취소] 됨(폴더 목록은 한 번만 읽음), 사본 지우기도 뒤에서, 도는 중에 또 누르면 겹치지 않고 알림. 엔진 열쇠 고르기(SharpCompress·7z.dll·EGG 공용)는 해시로 | `ClipboardCopyTests.수천_개를_골라도_…`(창 없는 디스패처 스레드)·`정리하는_도중_취소하면_…`·`복사하는_도중_또_누르면_…`·`고른_열쇠가_수천_개여도_…` |
| 그림 폭탄 | 화소 수와 상관없이 줄여 읽기(PNG 는 WIC 가 원래 크기를 다 풀 수 있음) | 2억 5천만 화소 넘는 그림(JPEG 는 10억)은 읽지 않고 '미리 보기에는 너무 커요' | `ImagePreviewTests.화소가_너무_많은_그림은_WIC가_…`·`…_자리표시를_보인다` |
| 임시 폴더 청소 | 잠금 파일이 없는 preview·list 도 '주인 없음'으로 보고 지움(다른 일집 창이 쓰는 중인 것까지) | open·drag 만 잠금으로, preview 1시간·list·convert·args 24시간·clip 24시간은 나이로 | `EntryWorkspaceTests.청소는_주인이_꺼진_open_drag와_…` |
| 닫을 때 고친 파일 | 보관분은 따로 '닫지 않기/그냥 닫기' 창 | W2-06 의 다시 넣기 질문(`ConfirmExitAsync` — [넣기][넣지 않기(닫기)][취소]) 하나로 | `EntryOpenTests.닫을_때_고친_파일이_있으면_묻고_취소하면_…`·`다른_압축으로_바꿔도_…` |

**기대가 바뀐 보관분 테스트(약화 아님 — 이유)**: `DragOutTests.한번에_꺼내기가_실패하면_…` 은 경로 공격 항목으로 '실패'를 만들었는데 (c) 로 그 경우는 이제 한 번에 꺼내므로
`경로_공격_항목이_섞인_폴더는_안전한_파일만_한번에_…`(호출 1+3, 넘긴 열쇠까지 단언)로 바꾸고, 같은 이름의 시험은 진짜 손상(확인값이 틀린 항목)으로 다시 만듦.
`큰_항목은_…(취소)` 의 '취소 뒤 작은 항목은 따로 꺼냄' → '취소 뒤엔 E_ABORT·더 꺼내지 않음'(더 엄격). `EntryOpenTests` 의 보관분 `ConfirmClose` 시험 둘은 W2-06 의 `ConfirmExitAsync` 로,
`시험이_만든_VM은_설정을_읽지_않고_…` 는 main 의 풀기 설정 계약(W2-05 — 처음 한 번 읽음)과 맞지 않아 `열고_풀고_복사하고_닫아도_설정은_쓰지_않는다`(쓰기 0·읽기 1번 이하)로,
`창이_뜰_때_미리보기_…` 는 저장 시점이 `SaveOnExit`(창이 닫힐 때 한 번)이라 그것을 부르고 다시 불러도 또 쓰지 않음까지, `창_껍데기가_…` 는 XAML 의 Closing·Closed 대신 코드 연결(W2-06)을 봄.
`MainWindowEditUiTests` 의 목록 우클릭 메뉴 기대에 '복사 (Ctrl+C)' 한 줄을 더함.

### Wave 2 통합 점검에서 더한 것(2026-10-14) — 묶음 사이 상호작용

푼 Skip 은 없습니다(0 → 0). 새 시험 70개: `Wave2IntegrationTests` 61(Theory 줄 포함)·`ReuseDeviceContractTests` +1·`RecentFilesTests` +1·`RarTests` +6·`TimeZoneSwitchTests` +1.
전체 2105 → 2175, 실패 0(세 번).
- **찾아서 고친 것 하나**: Release 전체 실행에서 `RecentFilesTests.설정_파일은_저장_직전에_다시_읽어_바꾼_칸만_얹는다` 가 한 번 실패 — 회사 PC 보안 프로그램(DLP)이 막 쓴 `settings.json` 을
  검사하는 동안 `File.Replace` 가 '바꿀 파일을 제거할 수 없습니다'(1175)로 실패했는데, 설정 저장의 다시 하기가 공유 위반·잠금·접근 거부만 다시 해 보고 이 오류는 곧장 실패(null)로 끝냈다.
  `JsonSettingsStore.IsTransientReplace` 가 1175·1176·1177 도 다시 해 보게 함(시험 `설정_바꾸기가_잠깐_막혀도_다시_해_본다` — 디스크 가득·없는 파일은 다시 하지 않음, 실제 파일을 0.25초 잡고 있어도 저장됨).
  (압축 고치기의 바꾸기 `ArchiveSwap` 은 예전부터 이 경우 예비 이름 방식으로 넘어가므로 그대로.)
- **찾아서 고친 것 둘 — RAR4 시각(시간대 흉내 실행에서 찾음)**: `ILJIP_TEST_TZ=UTC` 전체 실행에서 `RarTests` 의 RAR4 시각 8개(목록·풀린 파일)가 9시간 어긋나 실패.
  W2-01 에서 RAR 이 7z.dll 로 가면서 생긴 것 — 7-Zip 의 RAR4 처리기는 DOS 벽시계를 이 PC 의 '지금' 시간대 편차(Win32 `LocalFileTimeToFileTime`)로 UTC 로 바꾸고,
  .NET 은 그 UTC 를 그 날짜의 규칙으로 되돌린다. 둘의 시간대가 다르면(흉내, 그리고 실제로는 서머타임이 있는 나라에서 계절이 다른 파일) 그만큼 어긋난다
  (한국은 서머타임이 없어 이 PC 의 실제 사용에서는 같은 값이었음). `DosWallClock.FromSevenZip` 이 같은 '지금' 편차를 쓰는 `FileTimeToLocalFileTime` 으로 정확히 되돌려,
  목록 시각과 풀린 파일 시각이 적힌 벽시계 그대로(7z.dll 을 못 쓸 때의 SharpCompress 길과 같은 값). 시험 `RAR4_DOS_벽시계는_7z_dll이_쓴_시간대_편차를_되돌려_그대로`
  (서머타임 시작·끝 시각 포함 5줄)·`RAR4_벽시계_되돌리기는_이상한_시각에서_멈추지_않는다`. 기존 `RarTests` 의 기대는 그대로이고 이제 UTC·'Pacific Standard Time' 흉내에서도 통과.
- **찾아서 고친 것 셋 — 7z 만들기 취소가 바쁜 PC 에서 3초를 넘음**: `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…` 가 이번 전체 실행 세 번 모두 실패(3.1·6.4·3.1초 — W2-03 부터 '바쁠 때만'으로 적어 두던 것).
  원인: 솔리드 7z 를 취소하면 7z.dll 이 스레드마다 맡은 블록을 끝까지 압축한 뒤 돌아온다(취소 시간 ≈ 블록 하나 압축 시간 — c=8m 2.3~3.3초, c=2m 0.5~0.6초, 솔리드가 아니면 0.2초 안).
  보통 수준의 블록을 8m → 2m(압축 고치기와 같음). 크기 +0.1%(160MB 섞인 원본)·+2.4%(글자만), 시간은 같거나 빠름, 메모리 1/3. 계약 시험 한도(3초)는 그대로,
  기록 시험 `SevenZipCreateTests.기록_수준별_7z_dll_설정` 의 보통 값만 새 설정으로(기록 시험 — 엔진 설정이 바뀌면 고치는 것).
- 기대의 기준을 바로잡은 기존 시험 하나(단언을 지우거나 허용 범위를 넓히지 않음 — 정확히 같은 값 비교 그대로):

  | 시험 | 예전 | 지금 |
  |---|---|---|
  | `SfxCreateTests.만든_자동풀림_exe를_무인으로_풀면_원본과_같다`(시각 줄) | 풀린 파일 시각을 이 테스트 프로세스의 현지 시각(`File.GetLastWriteTime`)으로 읽어 원본 벽시계와 같음 | 자동 풀림 스텁은 **따로 도는 프로세스**라 이 PC(OS)의 시간대로 ZIP DOS 벽시계를 쓴다 → 그 시간대(`TestTimeZone.OsLocal`)로 읽은 벽시계가 원본 벽시계와 정확히 같음. 흉내가 없으면 예전과 똑같은 비교(OsLocal = Local). 흉내는 이 테스트 프로세스 안만 바꿔 UTC 실행에서 9시간 어긋나 실패했었다. 기준이 정말 OS 시간대인지는 새 시험 `TimeZoneSwitchTests.OS_시간대는_흉내와_상관없이_이_PC의_시간대다`(레지스트리 `TimeZoneKeyName` 과 대조)가 지킨다 |
- 상호작용 시험은 모두 처음부터 통과(어긋남 없음). 확인한 차이 하나(버그 아님 — 기록): 목록 잠긴 7z·RAR 에 틀린 암호를 넣으면 **메인 창 흐름**(열기·풀기 창)은 '암호가 맞지 않아요' 경고 창 한 번 + 다시 묻기,
  **우클릭 풀기**(메인 창 없이)는 경고 창 없이 암호 창 문구('암호가 맞지 않아요…')로만 다시 묻는다 — 각 길 안에서는 형식과 상관없이 같다.

### W2-12 에서 더한 것(2026-10-14) — I1-1 파일 연결, I1-2 우클릭 메뉴 정비, I3-10 오른쪽 끌기 메뉴

푼 Skip 은 없습니다(셋 다 Skip 이 없던 새 기능 — 0 → 0). 새 시험 64개: `FileAssociationRegistrarTests` 15·`ShellCommandLineTests` 7·`ShellMenuTests` 6·`SmartExtractTests` 9·`QuickCompressTests` 6,
`ShellExtensionContractTests` +7(소스 대조 4·실제 DLL 3), `CommandLineParserTests` +12(Theory 줄 포함), `StartupDecisionTests` +1, `CompressDialogXamlTests` +1(설정 창 '파일 연결' 칸). 전체 2041 → 2105, 실패 0.
시험은 진짜 셸·레지스트리·클립보드·휴지통·runas 를 부르지 않습니다 — 파일 연결은 `FakeRegistryRoot`, 셸 확장 DLL 은 시험 프로세스에 직접 불러 글·상태만 봄(Invoke·InvokeCommand·Drop 은 안 부름, 메뉴는 화면에 안 띄우는 빈 팝업 메뉴).

기대가 바뀐 기존 시험(설계가 바뀐 곳 — 단언을 지우거나 허용 범위를 넓힌 것 없음, 바뀐 설계에 맞춰 같은 것을 더 세게 봄):

| 시험 | 예전 | 지금 |
|---|---|---|
| `ShellExtensionContractTests.헤더_uuid_다섯개와_constexpr_GUID와_매니페스트_클래스가_같다` → `헤더_uuid와_constexpr_GUID와_매니페스트_클래스가_같다` | 헤더 5개 = constexpr = 매니페스트 5개 | 이름·CLSID 9개 표와 헤더가 정확히 같음, constexpr 개수·값이 같음, 매니페스트는 '일집 ▶'·'일집 열기'·끌어 놓기 셋(Path·STA 까지), 드롭 처리기 CLSID = C# 등록기 |
| `ShellExtensionContractTests.모든_cpp_클래스가_COM으로_등록된다` | `IljipCommands.cpp` 에 클래스마다 CoCreatableClass·템플릿 인스턴스화 | 모든 .cpp 에서 클래스마다 CoCreatableClass 가 **정확히 한 번**(접두사 없이), 베이스 템플릿을 쓰는 6개는 인스턴스화, vcxproj 에 .cpp 다섯·shell32 |
| `ShellExtensionContractTests.매니페스트_메뉴가_클래스와_맞는다` | 확장자 20개 ItemType 마다 '여기에 풀기'·'"이름"\ 에 풀기' | ItemType 이 정확히 셋('*' desktop4·Directory·Directory\Background desktop5)과 그 Verb·CLSID, 확장자별 없음, desktop9 끌어 놓기(Directory·Drive), Identity 0.4.0.0·AllowExternalContent·mediumIL·win32App·EntryPoint 없음·IgnorableNamespaces. 확장자 목록은 아래 둘이 C#·C++ 로 |
| `ShellExtensionContractTests.cpp가_넘기는_키워드를_모두_알아본다` | `IljipCommands.cpp` 의 키워드 | 모든 .cpp 의 키워드(새 다섯 포함 12개가 있어야 함), `--list-file` 만 따로, 파일 연결 동사는 셸 확장이 넘기면 안 됨 |
| `ShellExtensionContractTests.여기에_풀기_메뉴_제목` | 옛 제목 | 옛 제목 + '일집 열기' 제목 |
| `LocatorTests.우클릭_메뉴에_풀_수_있는_확장자가_모두_있다` | 풀 수 있는 끝 확장자가 매니페스트 ItemType 에 | 매니페스트에 '*'·Directory, 풀 수 있는 끝 확장자가 `ShellMenu.ArchiveExtensions`(= C++ 목록 — `cpp_압축_확장자_목록이_CSharp과_같다`)·`IsArchiveName`·파일 연결 목록에 모두 |
| `CommandLineParserTests.키워드_목록은_모두_해석된다` | 11개 | 16개·겹침 없음·새 다섯 있음·`--list-file`·`--drop-handler` 없음 |
| `CommandLineParserTests.ToArgs는_다시_해석하면_같은_명령` | 12가지 | + 새 동사 6가지 |
| (도우미) `ShellDll.SkipReason` | 소스 넷보다 오래되면 건너뜀 | 새 .cpp 셋을 더한 일곱 — 더 엄격(C++ 를 고치고 다시 빌드하지 않으면 건너뜀) |

| 무엇 | 예전 | 지금 | 시험 |
|---|---|---|---|
| 파일 연결 | 없음 | HKCU '연결 프로그램' 후보·기본 앱 목록 칸·형식 아이콘(기본 앱은 안 빼앗음, 기록한 것만 뺌), ☰ ▸ 설정 '파일 연결'·`--register-associations [--drop-handler]`·`--unregister-associations`, Inno `[Registry]` | `FileAssociationRegistrarTests`, `StartupDecisionTests`, `CompressDialogXamlTests` |
| 우클릭 메뉴 | 낱 메뉴 5개(압축 확장자 20개에), 폴더 메뉴 없음, 파일마다 일집 | '일집 ▶' 하나('*'·폴더) + 하위 메뉴(고른 것에 따라), 빈 곳 '일집 열기', 한 명령줄(길면 `--list-file`) | `ShellMenuTests`, `ShellCommandLineTests`, `ShellExtensionContractTests` |
| 알아서 풀기 | 없음 | 맨 위가 하나면 바로, 여럿이면 '이름' 폴더 | `SmartExtractTests` |
| 바로 압축 | 없음(압축 창만) | '"이름.zip"으로 압축'·'"이름.7z"로 압축' — 창 없이 | `QuickCompressTests` |
| 오른쪽 끌기·끌어 놓아 넣기 | 없음 | '일집: 여기에 풀기'·'일집: 여기에 압축', zip·7z 드롭 처리기(`--add-to`) | `ShellExtensionContractTests`(DLL) |

확인하지 못한 것(MANUAL_CHECKLIST 18절): 진짜 탐색기 메뉴·MSIX 설치·HKCU 등록·기본 앱 화면·오른쪽 끌기·드롭 처리기 실제 동작, 회사 그룹 정책, Inno 설치 프로그램 컴파일·설치·제거.

### W2-11 에서 더한 것(2026-10-14) — I3-15 시작 화면·최근 파일·압축 파일 관리, I3-16 관리자 권한으로 다시 시도

푼 Skip 은 없습니다(둘 다 Skip 이 없던 새 기능 — 새 시험 81개: `RecentFilesTests` 10·`StartScreenTests.cs`(`StartScreenTests` 10·`SaveAsTests` 18·`DeleteArchiveTests` 8)·`ElevationTests` 19·`StartupDecisionTests` 6·`CommandLineParserTests` 10, Theory 줄 포함).
기대가 바뀐 기존 시험 2개 — 둘 다 새로 더한 것이 목록에 들어간 것이고 원래 확인하던 것은 그대로: `MainWindowEditUiTests.메인_창이_읽히고_고치기_장치가_있다`(☰ 메뉴 기대 목록 맨 앞에 '새 창 (Ctrl+N)'·'다른 이름으로 저장...'·'이 압축 파일 지우기...' 더함 — 시작 화면·Ctrl+N 단언도 더함),
`CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 수 8→10 — 새 두 키워드가 있는지·표시와 겹치지 않는지 단언을 더함).

| 무엇 | 예전 | 지금 | 시험 |
|---|---|---|---|
| 빈 창 | '압축 파일을 끌어다 놓거나…' 안내 글 | 시작 화면(`Views/StartView.xaml` — [압축 파일 열기][압축하기][압축 풀기…]·끌어 놓기 안내·최근 파일 10개, 없는 파일은 흐리게 '없음', 오른쪽 클릭 메뉴) | `StartScreenTests`, `MainWindowEditUiTests` |
| 최근 파일 | 없음 | 설정 `recent.files`(경로·마지막으로 연 시각 UTC). 열 때마다 메모리에만, 창을 닫을 때 한 번(트리 너비와 함께), 빼기·지우기는 바로 — 저장된 목록과 합침(창 두 개) | `RecentFilesTests` |
| 다른 이름으로 저장 | 없음 | ☰ 메뉴. 같은 형식은 복사(나뉜 압축은 조각 전부, 숨김 임시 파일 → 이름 바꾸기), ZIP↔7Z 는 `temp\convert\guid` 에 풀고 다시 압축('같은 암호를 걸까요?'), 다 되면 새 파일을 엶 | `SaveAsTests` |
| 이 압축 파일 지우기 | 없음 | ☰ 메뉴. 묻고(기본 [취소]) 닫은 뒤(작업 폴더·감시 포함) 조각 전부를 휴지통으로, 시작 화면, 최근에서 뺌 | `DeleteArchiveTests` |
| 새 창 | 없음(Ctrl+N = 압축 실행) | ☰ ▸ 새 창·**Ctrl+N** — `--new-window`(단일 인스턴스 뮤텍스·파이프를 건너뜀 — `StartupDecision`) | `StartScreenTests`, `StartupDecisionTests` |
| 권한 오류 | 오류 창 하나 | 풀기·압축(하나·여러 개·우클릭)에서 '관리자 권한으로 다시 시도' 질문 → `runas` 로 같은 인자(`ElevationService.BuildRetry` — `--elevated` 맨 앞, 암호 없음, 매핑 드라이브는 UNC). 나타낼 수 없는 작업은 '관리자 권한으로 일집 다시 열기'. 관리자 창은 다시 권하지 않음 | `ElevationTests` |

확인하지 못한 것(MANUAL_CHECKLIST 17절): 시작 화면 생김새, 진짜 휴지통, 두 창을 함께 쓰기, **진짜 UAC 창·관리자 일집**(진행 창·제목·끌어 놓기), 네트워크 드라이브, 표준 사용자 계정.

#### W2-11 검증 지적 수정(1차)

새 시험 23개(`RecentFilesTests` 9·`StartScreenTests` 3·`DeleteArchiveTests` 1·`ElevationTests` 8·`CommandLineParserTests` 2, Theory 줄 포함), 푼 Skip 없음.
기대가 바뀐 기존 시험(약하게 한 것 없음 — 단언을 더하거나 고친 동작에 맞춤):
`ElevationTests.관리자_일집은_다시_권하지_않는다`('(관리자)' 제목은 이제 실제 권한을 따름 — 표시만 있고 보통 권한이면 **안 붙는지**까지 두 경우 모두 단언),
`CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 10→11 — `--extract-into` 가 있는지 단언 더함), `ElevationTests.동사별_인자`·`CommandLineParserTests.ToArgs는_다시_해석하면_같은_명령`(새 동사 줄 더함),
`MainWindowEditUiTests`('없는 파일 정리' 단추 묶임 단언 더함), `DeleteArchiveTests.열어_둔_파일의_작업_폴더와_감시도_먼저_놓는다` → `지우면_…_놓는다`(이름만 — 놓는 때가 '옮긴 뒤'로 바뀜, 단언 그대로).

| 지적 | 고친 것 | 시험 |
|---|---|---|
| 창 두 개면 최근 파일·설정이 서로 지워짐(major) | 모든 저장이 `MainViewModel.SaveSettings` 로 — `ISettingsStore.Update`(새)가 저장 직전 최신 settings.json 을 다시 읽어 **이 창이 바꾼 것만**(설정 창은 `SettingsPatch` 의 바뀐 칸, 풀기 창·압축 창·트리 너비는 그 칸 바꾸기를 다시) 얹고, 최근 파일은 이 창에서 넣고 뺀 것만 합침. `JsonSettingsStore` 쓰기는 이름 붙은 뮤텍스로 한 번에 하나(.tmp 경합), 있는데 못 읽으면 쓰지 않고 다음 저장에 다시. 설정 창 적용이 설정 객체를 바꿔 끼우던 것도 고침(그 뒤 연 압축이 최근 파일에 안 남던 문제) | `RecentFilesTests`(새 9개) |
| 읽기 전용 파일 덮어쓰기에도 관리자 권한 권함 | `OutputFiles.ReplaceExisting` 이 읽기 전용이면 `ReadOnlyTargetException`('…읽기 전용이라 덮어쓰지 못했어요…읽기 전용을 끄고') — `IsAccessDenied` 가 권한 오류로 안 봄. 그 밖의 접근 거부·공유 위반 문구도 갈라 관리자 질문과 앞뒤가 맞게. 쓰려던 곳이 네트워크면 권하지 않음(`ElevationService.CanHelpWriteTo`) | `ElevationTests.읽기_전용_…`·`쓰려던_곳이_네트워크면_…` |
| 지우기 실패에도 열어 고친 파일이 사라짐 | 작업 폴더·감시는 휴지통으로 옮긴 **뒤에만** 놓음 | `DeleteArchiveTests.못_옮기면_열어_고친_파일과_감시를_그대로_둔다` |
| MANUAL 알려진 점이 다름·`--add-to` 다시 시도 시험 없음 | MANUAL 17절 알려진 점을 실제대로, 시험 둘 | `ElevationTests.add_to는_…`·`넣기_밖의_고치기는_일집_다시_열기` |
| 여럿을 한 폴더에 바로 풀다 막히면 첫 압축만 다시 엶 | 새 동사 `--extract-into 폴더 압축…`(메인 창 없이 그 폴더에 바로 모두) — 다시 시도가 모두를 같은 곳에 | `ElevationTests.여러_압축을_…`·`extract_into는_…`, `CommandLineParserTests.extract_into는_…` |
| '(관리자)' 제목이 '--elevated' 인자를 따름 | 제목은 실제 권한(`IsRunningElevated` — 토큰), 다시 권하지 않기는 표시 또는 실제 권한(무한 반복 방지 유지) | `ElevationTests.관리자_일집은_다시_권하지_않는다` |
| '없는 파일 정리'가 한 줄씩만 | 시작 화면 '최근 파일' 옆 [없는 파일 정리](흐린 줄이 있을 때만)·오른쪽 클릭 메뉴 — 찾을 수 없는 줄만 한꺼번에 묻고 뺌 | `StartScreenTests.없는_파일_정리는_…`·`없는_파일이_없으면_…`, `MainWindowEditUiTests` |

### W2-10 에서 더한 것(2026-10-14) — I3-1 자동 풀림 EXE 만들기

푼 Skip 은 없습니다(I3-1 은 Skip 이 없던 새 기능 — 새 계약 `SfxCreateTests` 35·`SfxCompressFlowTests` 19·`CompressDialogXamlTests` 3, Theory 줄 포함).
기대가 바뀐 기존 시험 2개 — 둘 다 형식 목록 맨 끝에 '자동 풀림 EXE' 가 더해진 새 동작(스텁이 시험 출력의 sfx 폴더에 있을 때)이고 원래 확인하던 것(7Z 있음·없음)은 그대로:
`CompressFlowTests.압축_창에_넘기는_것`(형식 목록 전체 비교에 Sfx 더함), `CompressDialogRulesTests.칠z_dll이_없으면_7Z를_고를_수_없다`(라벨 첫 낱말 목록에 '자동' 더함).

| 무엇 | 예전 | 지금 | 시험 |
|---|---|---|---|
| 자동 풀림 exe 만들기 | 없음(열기만 — W2-01) | 압축 창 형식 '자동 풀림 EXE'. 일집 자체 스텁(`Iljip.SfxStub` — .NET Framework 4.6.2 WinForms, 한국어 화면, 실행 파일 옆 `sfx\Iljip.SfxStub.exe`) + 보통 ZIP + 꼬리 24바이트. 무인 모드 `/s /d:폴더 [/p:암호]`(창 없음·0 성공·1 실패·2 인자 오류·표준 오류 UTF-8 한국어). 풀 곳 안의 숨은 임시 폴더에 다 풀고 크기·CRC(AES 는 HMAC)를 맞춘 뒤에만 옮김 — 실패·취소면 이번에 만든 것 하나도 안 남김(덮어쓴 파일은 되돌림) | `SfxCreateTests` |
| 일집 안에서 자동 풀림 ZIP | SharpCompress 로 바로(한글 ZipCrypto 암호·AES 인증값 없음) | 우리 꼬리면 그 범위만, 아니면 서명으로 찾은 위치부터 `ZipArchiveService` 에 맡김(CP949 암호·HMAC·CRC — 반디집 ZIP 자동 풀림도 같이 좋아짐). '다 만들고 시험하기'도 같은 길 | `SfxCreateTests.일집_안에서_…`, `SfxCompressFlowTests.시험기는_깨진_자동풀림을_잡는다` |
| 메일 안내 | — | 압축 창 형식 칸 아래 늘 보임 + 만들기 직전 한 번 묻기([그래도 만들기][ZIP으로 만들기][취소] + '다시 묻지 않기' → `sfx.skipMailWarning`, ☰ ▸ 설정에서 되살림) | `SfxCompressFlowTests`, `CompressDialogXamlTests` |
| 암호 계산 공유 | `WinZipAes`·`ZipPasswordProbe` 안에 | `Iljip\Services\Shared\WinZipAesCore.cs`·`ZipCryptoCore.cs`(본체·스텁이 같은 파일을 링크 — 기존 AES·ZipCrypto 시험이 그대로 지킴) | `SfxCreateTests.스텁은_공유_암호_코드를_쓰고_sfx_폴더에만_복사된다`, 기존 `WinZipAes`·`ZipPasswordProbeTests` |

가짜·도우미에 더한 것: `FakeUserInteraction.ChooseWithCheck`(`CheckChoicePrompts`·`CheckAnswer`), `SfxStubFact`·`SfxStubTheory`(exe 실행이 막힌 PC 만 건너뜀).
확인하지 못한 것(MANUAL_CHECKLIST 16절): 화면 모드(풀 위치 창·한글 표시·진행·같은 이름 묻기·취소), 다른 PC 의 SmartScreen·백신, 회사 메일 게이트웨이, .NET Framework 4.6.2 가 없는 옛 PC. 이 PC 의 Windows Defender 는 꺼져 있어(다른 백신 사용 — MpCmdRun 'Product/Feature disabled') 검사를 못 했습니다.

### W2-10 검증 지적 수정(2026-10-14, 1차)

푼 Skip 은 없습니다. 지적 6개를 모두 직접 확인했고(검증자의 재현 스크립트를 고친 스텁으로 다시 돌림) 모두 고쳤습니다 — '작은 파일이 많으면 느림'은 체크리스트만 바란 지적이지만 원인(파일마다 옮기기)을 재서 고쳤습니다.
새 시험 19개(`SfxCompressFlowTests` 9·`SfxCreateTests` 10 — Theory 줄 포함). 기대가 바뀐 기존 테스트는 없습니다(`SfxCompressFlowTests.ZIP으로_만들기면_zip이_생기고_exe는_없다` 는 단언을 더함 — 질문 창 하나·CP949 로 써짐).
고친 동작의 시험은 고치기 전 동작으로 잠깐 되돌려 실패함을 확인했습니다(한글 암호 확인 빼기 → 6개, 마무리 잠금 빼기 → 1개, 주인 없는 임시 폴더를 하루 규칙으로 → 4개, 통째로 옮긴 폴더 안 되돌리기 → 1개).
셋 동시 시험은 경쟁이 우연이라 잠금 없이도 통과할 수 있어, 잠금은 시험이 뮤텍스를 쥐고 스텁이 기다리는지 보는 결정적 시험이 지킵니다.

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| [ZIP으로 만들기] + AES 한글 암호(major) | 메일 질문에서 [ZIP으로 만들기]면 옵션 그대로 ZIP — 반디집·알집이 못 여는 UTF-8 AES ZIP 이 경고 없이 생김(압축 창은 .exe 라 한글 암호 경고를 안 함) | ZIP 으로 바꿀 때만 압축 창 ZIP 과 같은 판정(`PasswordPolicy.CheckCompat`)으로 한 번 더 묻기 — [ZipCrypto 방식으로 만들기](기본, CP949 로 씀)·[AES-256 그대로]·[취소], 이모지 등은 [취소]·[그대로]. 취소면 아무것도 안 만들고 '다시 묻지 않기'도 기억 안 함 | `SfxCompressFlowTests.ZIP으로_만들기에서_AES_한글_암호면_…`(2)·`…_취소하면_아무것도_만들지_않는다`(2)·`…_다른_프로그램이_못_읽는_암호면_묻는다`(2)·`한글_암호_질문은_ZIP으로_바꿀_때만`(3) |
| 같은 풀 곳에 동시에 | 옮기기의 '있나?→옮기기' 경쟁으로 한쪽이 실패하면 되돌리기가 남이 덮어쓴 파일까지 지워 '종료 코드 0 인데 파일이 없음'(315MB 셋 동시: 0·1·1 + 300MB 파일 없음) | 같은 풀 곳의 옮기기·되돌리기는 이름 있는 뮤텍스(`SfxStaging.DestinationMutexName` — 같은 로그인 세션)로 한 번에 하나씩(임시 폴더에 푸는 것은 동시에, 버려진 뮤텍스는 넘겨받음, 10분 넘으면 쉬운 말 오류). 먼저 옮겨 보고 그 자리에 무엇이 있으면 같은 이름 처리. '이미 있으므로'·'찾을 수 없습니다' 도 쉬운 말로 | `SfxCreateTests.같은_풀_곳의_마무리는_한_번에_하나씩_한다`·`같은_곳에_셋을_동시에_풀어도_모두_되고_빠진_파일이_없다` |
| 강제 종료 흔적 | 하루 지난 임시 폴더만 지워, 바로 다시 풀어도 숨은 '.일집-임시-…'·반쪽 파일(수백 MB)이 남음. 못 되돌린 사용자 원래 파일('.덮어쓴 파일')은 숨은 폴더에 말없이 남았다가 하루 뒤 지워짐 | 새 공유 소스 `Iljip/Services/Shared/SfxStagingCore.cs`: 스텁은 임시 폴더 안 `.사용 중` 을 독점으로 열어 두고, 다음 풀기는 그걸 열 수 있으면(주인이 죽음) 바로 정리. 원래 파일은 지우지 않고 보이는 폴더 '덮어쓰기 전 원래 파일 (날짜 시각)' 에 원래 모양대로 옮기고 알림(못 옮기면 임시 폴더를 남김). 표시 없는 것(본체 것)은 하루 규칙 그대로, 살아 있는 것은 그대로. 본체 `StagingFolder.CleanupStale` 도 같은 규칙. 로그오프·작업 끝내기면 닫기를 막지 않음(4초 되돌리기 뒤 닫힘) | `SfxCreateTests.강제로_끝난_풀기의_임시_폴더는_바로_다음_풀기가_지운다`·`끊긴_풀기가_비켜_둔_원래_파일은_…`·`본체의_임시_폴더_정리도_같은_규칙이다`·`원래_파일을_못_옮기면_임시_폴더를_지우지_않는다` |
| SfxStubFact 건너뛰기 | 탐색용 스텁 실행의 종료 코드가 1 이 아니기만 해도 실행 시험 약 37개가 '건너뜀'(스텁이 바로 죽어도 '실패 0') | Windows 가 실행을 거절한 Win32Exception(정책)만 건너뜀 — 파일 없음·exe 아님(193)·CPU 다름(216)은 망가진 스텁이라 실패. 종료 코드는 새 시험이 봄 | `SfxCreateTests.건너뛰기는_실행이_정책으로_막혔을_때만`·`스텁만_실행하면_데이터가_없어_1이고_창이_없다` |
| 작은 파일이 많으면 느림 | 파일마다 임시 폴더→제자리 옮기기(이 PC 2.3ms)·시각 맞추려 다시 열기·'있나?' 여러 번 — 1만 개 39~60초, 7만 개 411초 | 풀 곳에 아직 없는 폴더는 통째로 한 번에 옮김(되돌릴 때도 통째로), 시각은 열린 핸들로, 확인한 폴더는 기억. 풀린 것은 임시 폴더 안 `내용\` 아래(표시 이름과 겹치지 않게). 무인 모드 실측 1만 개 8.6~9.1초·7만 개 63초(파이썬 zipfile 11~15초·86초). 같은 곳에 덮어쓰며 풀 때는 여전히 파일마다(체크리스트에 시간 기록 줄) | 기존 왕복·시각·덮어쓰기·되돌리기 시험 + `SfxCreateTests.옮기다_실패하면_통째로_옮긴_폴더와_덮어쓴_파일까지_되돌린다`·`표시_이름과_같은_항목도_풀린다` |
| '[추가 정보] → [실행]' 단정 | "창이 뜨면 [추가 정보] → [실행]을 누르면 돼요" | "…뜰 수 있어요. [추가 정보] → [실행]이 보이면 눌러서 풀면 되고, 실행이 아예 막히는 PC(회사 보안 설정 등)도 있으니 그때는 ZIP으로…", 압축 창 안내에도 '실행이 아예 막히기도 해요' | `SfxCompressFlowTests.처음_만들_때_메일_안내를_…`(첫 문장 그대로) |

확인하지 못한 것(MANUAL_CHECKLIST 16절 '(검증 지적 1차)' 줄): 화면 모드의 기다림 문구·두 창 동시·로그오프 때 닫힘·끝 창의 원래 파일 안내, Smart App Control PC, 작은 파일이 많을 때 화면 모드 시간. 마무리 잠금은 같은 로그인 세션만 막습니다(다른 사용자·다른 PC 가 같은 공유 폴더에 동시에 풀면 예전과 같음). 백신 오탐은 여전히 실측하지 못했습니다(Defender 꺼짐).

### W2-10 검증 지적 수정(2026-10-14, 2차)

푼 Skip 은 없습니다. 지적 7개를 모두 직접 확인했고(검증자의 재현 스크립트와 같은 것을 고치기 전·뒤 스텁으로 돌림 — 아래 표의 수치) 모두 고쳤습니다.
새 시험 18개(`SfxCreateTests` 10·`SfxCompressFlowTests` 7·`CommandLineTests` 1 — Theory 줄 포함). 기대가 바뀐 기존 테스트 1개:
`SfxCompressFlowTests.다시_묻지_않기를_켜면_기억하고_다음엔_묻지_않는다` 가 Theory(0·1) → Fact(0 — [그래도 만들기]) — 지적 5 가 '[ZIP으로 만들기]와 함께 켠 확인란을 기억하는 것'을 틀린 동작으로 짚어,
그 줄(1)은 반대 계약의 새 시험 `다시_묻지_않기를_켜도_ZIP으로_만들기면_기억하지_않고_다음에_또_묻는다` 로 바뀌었습니다(단언을 줄이지 않음 — exe 이름 확인을 더함).
`SfxCompressFlowTests.자동풀림을_고르면_안내가_보이고_나누기는_숨는다` 에는 '4GB' 안내 단언을 더했습니다.
고친 동작의 시험은 고치기 전 동작으로 잠깐 되돌려 실패함을 확인했습니다(일곱 가지를 한꺼번에 되돌림 → 새·바뀐 시험 15개 실패, 되살린 뒤 0).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 4GB 넘는 exe(major) | 크기 확인 없이 만듦 — Windows 는 4GB(2^32 바이트) 이상인 실행 파일을 시작하지 않아(이 PC 실측: 4,294,967,295 바이트는 시작, 4,294,967,296 바이트부터 CreateProcess 오류 193) 받는 사람이 못 엶 | 만들기 전 어림(원본 크기 합 — 빼기 목록 적용, 각각 따로면 원본마다)이 넘으면 진행 창에서 재고 묻기([ZIP으로 만들기 (권장)]·[그래도 … 해 보기]·[취소]), 만든 뒤 exe 가 `SfxBuilder.MaxExeBytes`(4GB − 1바이트)를 넘으면 잇기 전에 `SfxTooLargeException`(임시 파일 지움·기존 exe 그대로) → 하나로 만들 때는 'ZIP으로 만들까요?', 각각 따로는 요약 창에 까닭. 압축 창 안내에도 한 줄 | `SfxCreateTests.한도를_넘는_exe는_만들지_않고_아무것도_남지_않는다`, `SfxCompressFlowTests.원본이_4GB를_넘으면_…`·`…_취소하면_…`(2)·`그래도_해_보다가_…`(2)·`만든_뒤에_넘으면_…`·`각각_따로도_4GB를_…` |
| 다 풀고 지우는 사이 '지난번 흔적' 오보 | 성공한 풀기가 주인 표시를 먼저 닫고 임시 폴더를 지워, 그 사이 시작한 다른 풀기가 방금 일부러 덮어쓴 원래 파일을 '덮어쓰기 전 원래 파일 (…)' 로 옮기며 '지난번에 끝까지 못 푼 흔적…' 이라고 알림(재현: 1,500개 덮어쓰기 + 다른 풀기를 0.08초마다 — 고치기 전 4번 중 3번) | 표시를 쥔 채 나머지를 먼저 지우고 표시는 마지막에(`SfxStaging.DeleteOwnedTree` — 스텁의 마무리·흔적 정리 모두). 고친 뒤 같은 재현 5번 중 0번 | `SfxCreateTests.임시_폴더를_지우는_동안에는_주인_표시를_놓지_않는다`(남은 파일 하나를 잠가 지우기를 멈춰 두고 '살아 있음'인지 봄 — 결정적) |
| 덮어쓰며 풀기가 느림 | 같은 이름 파일마다 실패하는 옮기기 + '폴더인가?' + 목록.txt 열고 닫기 + 옮기기 둘(3,000개: 파일당 9~12ms) | 원래 있던 폴더는 처음 볼 때 한 번 훑어 같은 이름을 가림(대소문자까지 같은 것만 — 다르면 예전 길), 목록.txt 는 한 번 열어 둔 채 줄마다 OS 로(`SfxStaging.BackupList`), 비켜 둘 폴더는 한 번만 만듦, 지울 때 읽기 전용만 풀기. 3,000개 파일당 4.6~5.6ms(같은 때 예전 스텁 9.1ms) — 남은 시간은 되돌릴 수 있게 하는 옮기기 둘(파일당 약 3.4ms) | `SfxCreateTests.이미_있는_파일을_많이_덮어써도_결과가_같다`·`원래_자리_목록은_열어_둔_채_줄마다_남는다` + 기존 덮어쓰기·되돌리기·흔적 정리 시험 |
| 끝이 잘린 일집 자동 풀림을 일집에서 열면 | 꼬리·ZIP 끝 머리가 없어 내용 판별이 '압축 아님' — 창 없이 풀기 종료 코드 2 'Unsupported archive type: .exe', 화면은 지원 안 함 | PE 덧붙은 영역이 ZIP 로컬 머리로 시작하면 ZIP 자동 풀림으로 보고(`ArchiveSniffer.IsTruncatedZipSfx`) 목록·풀기·시험에서 '압축 파일의 끝부분이 잘려 있어요…'(종료 코드 1). 스텁만 있는 exe·보통 exe 는 그대로 2 | `SfxCreateTests.끝이_잘린_자동풀림은_일집_안에서도_손상으로_알린다`(2), `CommandLineTests.실행파일_extract_to_끝이_잘린_자동풀림은_손상으로_1` |
| '다시 묻지 않기' + [ZIP으로 만들기] | 확인란을 기억해 다음 자동 풀림이 묻지 않고 exe 로 만들어짐 | [그래도 만들기]일 때만 기억(뒤 질문에서 취소하면 그것도 기억 안 함) | `SfxCompressFlowTests.다시_묻지_않기를_켜도_ZIP으로_…`, `…_취소하면_아무것도_만들지_않는다`(2) |
| 나중에 코드 서명하면 손상 | 꼬리가 파일 맨 끝이어야 해서 서명 표가 붙으면 스텁이 '손상' | 꼬리 찾기를 공유 소스 `Iljip/Services/Shared/SfxTrailerCore.cs`(`SfxTrailer`)로 — PE 보안 디렉터리가 가리키는 서명 표가 8바이트 경계에서 시작해 정확히 파일 끝까지면 그 앞(0 채움 0~7바이트)에서 찾음. 서명 표 뒤에 무엇이 더 붙었거나 표가 아닌 것이 붙으면 그대로 손상 | `SfxCreateTests.나중에_코드_서명을_한_exe도_풀리고_일집에서도_열린다`·`꼬리_뒤에_서명_표가_아닌_것이_붙으면_손상이다`(2) |
| NFC·NFD 같은 이름이 함께 | 둘 다 NFC 로 바꿔 '같은 이름이 두 번… 손상'으로 통째로 거절 | NFC 로 바꿔서 겹칠 때만 압축에 적힌 그대로 풂(원본과 같은 두 파일), NFD 만이면 예전처럼 NFC, 바꾸지 않아도 겹치면 손상 | `SfxCreateTests.NFC와_NFD_두_모양이_같이_있어도_원본대로_푼다` |

확인하지 못한 것(MANUAL_CHECKLIST 16절 '(검증 지적 2차)' 줄): 진짜 4GB 가 넘는 자료로 화면에서 질문·진행 창 모양, 진짜 코드 서명(이 PC 에 signtool·인증서 없음 — 서명 도구의 파일 모양을 규격대로 흉내 내 시험), 화면 모드에서 많이 덮어쓸 때 시간.
4GB 실측은 성긴 파일(스텁 + 0 + 꼬리)로 했고 시험에는 넣지 않았습니다(4GB 파일을 백신이 오래 잡아 지우기가 늦음 — 한도 판정은 `LimitForTests` 로 작게 시험).

### W2-09 에서 고친 것(2026-10-13) — I2-10 맥 NFD·일본어·중국어 이름, I2-13 풀기 창, I2-14 찾기·Backspace·선택 합계·트리 너비

푼 Skip: `BandizipFixtureTests.NFD_이름이_NFC로_보이고_NFC로_풀린다`(Theory 4행 — 반디집 기본·UTF-8, 파이썬 EFS·표시 없음). 기대가 바뀐 기존 시험은 없습니다
(`KoreanFileNameDecoderTests` 의 섞인 이름 ZIP·반디집 한글 픽스처 목록이 글자 하나 다르지 않게 그대로 통과 — 한국어 우선).

| 무엇 | 예전 | 지금 | 시험 |
|---|---|---|---|
| 맥 ZIP 의 NFD 한글 이름 | 목록·풀린 이름이 'ㅂㅗㄱㅗㅅㅓ' 처럼 자모 분리 | 목록·풀기·고르기 모두 NFC(`Services/EntryNames.cs` — 목록 이름, `EntryPaths.ToSafeRelative`, 고르기 비교 양쪽, `EditPlanner.Normalize` 열쇠, 7z.dll·EGG/ALZ 이름). NFD 이름으로 골라도 풀림. NFC 로 같아지는 두 항목은 둘 다 풀리고 둘째는 '이름 (1)' | `BandizipFixtureTests`(Skip 해제), `NameEncodingTests` NFD 줄(ZIP 표시 있음·없음, 7z, EGG, 고르기, 겹치는 이름) |
| 일본어(Shift-JIS)·중국어(GBK) 이름 | CP949 로 읽혀 엉뚱한 한글('깻깥깸랍됨'·'꿎桿匡숭') 또는 CP437 | 압축 파일 하나에 방식 하나(`Services/ArchiveNameEncoding.cs` — 한국어 먼저: KS X 1001 완성형·자주 쓰는 음절 비율로 판정, 그다음 Shift-JIS·GBK). SharpCompress 로 여는 ZIP·TAR·RAR4 대체·ZIPX/RAR4 이름 대조는 처음 열 때 이름을 모아 정하고 다르면 한 번 더 엶, ZIP 원시 읽기(암호 판정·압축 고치기)는 같은 규칙(`Services/ZipEntryNames.cs` — 0x7075 우선) | `NameEncodingTests`(판별기, ZIP 목록·풀기·고르기·고치기 3방식, 표시 있는 UTF-8 섞임, TAR 2방식) |
| 풀기(툴바·Ctrl+E·선택 항목·`--extract`·열기 창 여러 개) | 폴더 고르기 창 | 풀기 창(`Views/ExtractOptionsDialog.xaml` + 순수 VM `ViewModels/ExtractOptionsViewModel.cs`): 풀 곳·최근 푼 곳 10개(없는 폴더 숨김·대소문자 무시)·'압축파일명으로 폴더 만들기'(기본 켬·기억 — 있으면 '이름 (2)')·'다 풀고 휴지통으로'(이번에만)·'다 풀면 폴더 열기'(기억). 끝나면 설정 한 번 저장. `IUserInteraction.AskExtractTarget`(가짜 `ExtractTargetAnswer` — 비면 예전 `FolderToPick` 답: 하나면 그 폴더에 바로, 여럿이면 '이름' 폴더마다) | `ExtractDialogTests`(설정 왕복·최근 목록 규칙·VM 규칙·화면 흐름 9개), `ExtractDialogXamlTests` |
| 둘러보기 | 찾기 없음, Backspace 없음, 트리 너비 고정 220 | 이름으로 찾기(모든 폴더·200ms 기다림·NFC·대소문자 무시·글자 일부, '경로' 열 — 정렬도 됨, Esc·✕ 로 지움 — 10만 개에 찾기 약 5ms), Backspace = 위 폴더(`UpCommand` — 이름 고치는 중엔 그 칸에), 상태 줄 '선택: 3개 · 12.4MB'(폴더는 그 아래 전부, 모르는 크기면 '약'), 트리|목록 가르는 막대(ui.treeWidth — 기본 260, 닫을 때 한 번 저장) | `BrowseTests`, `BrowseXamlTests`, `SortingTests`(경로 열도 정렬할 수 있는 열) |

가짜·도우미에 더한 것: `FakeUserInteraction.AskExtractTarget`(`ExtractTargetPrompts`·`ExtractTargetAnswer`), `RawTar.Entry.RawName`(옛 방식 이름 바이트 그대로).
확인하지 못한 것(MANUAL_CHECKLIST 15절): 진짜 맥·일본어·중국어 Windows 산출물, 창 모양·Tab·작은 화면, 찾은 결과가 수만 개일 때 목록 그리기 속도(VM 은 10만 개 중 1,111개를 찾아 정렬·목록 채우기까지 15ms — WPF 가 그리는 시간은 못 잼), 진짜 가르는 막대 끌기·저장.

### W2-09 검증 지적 수정(2026-10-13, 1차)

푼 Skip 은 없습니다. 지적 7개를 모두 직접 확인했고 모두 고쳤습니다(파일·폴더 이름 겹침 지적의 '영어 IOException' 부분만은 화면에서 이미 '같은 이름의 파일이나 폴더가 이미 있어…'로
바뀌어 보였음 — 풀기가 멈추고 앞 파일이 남는 것은 맞아 고침). 새 시험 27개(`NameEncodingTests` 20 — Theory 줄·`LegacyZips` 새 줄로 기존 Theory 3개에 한 줄씩 포함, `ExtractDialogTests` 4,
`BrowseTests` 2, `MainViewModelEditTests` 1). 기대가 바뀐 기존 테스트는 없습니다.

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 한자가 많은 한국어 이름(major) | 한글 수 < 한자 수면 한국어로 못 정하고 GBK(중국어)로 넘어가, 같은 압축의 '보고서.txt' 까지 '焊绊辑.txt' 로 깨져 그 이름으로 풀림 | GBK 로 읽어 사용자 정의 글자(PUA)가 나오거나 GB2312 2급 한자가 한자의 25%를 넘으면 중국어 아님, 자주 쓰는 한글이 2자 이상이고 80% 이상이면 한자가 많아도 한국어, 중국어·일본어가 아니고 CP949 완성형으로 다 풀리면 한국어. 자주 쓰는 음절에 '폴·플·클·캔·및·워·텍·딩·런' 더함. 실측(scratch 실험): 이 PC 실제 이름 53,704개(폴더 6,286개 = 압축 하나씩) 예전 규칙보다 나빠진 이름 0, 한자 섞인 한국어 모의 압축 6만 개(한자 비율 30~90%·이름 1~5개) 예전 규칙보다 나빠진 이름 18,828 → 0(중국어로 잘못 정한 압축은 최대 97% → 이름 하나·한자뿐인 6% — 그건 예전 규칙도 CP437 로 깨지던 것), 중국어 압축이 한국어로 보이는 비율은 이름 1개 33.0→34.3%·3개 10.5→11.4%(한국어 우선 위험 그대로), 일본어 100% 그대로 | `NameEncodingTests.한자가_많은_한국어_이름도_…`(10)·`드문_한자가_조금_섞인_중국어_이름은_중국어`·`LegacyZips` 의 한자 줄(목록·풀기·고르기·고치기) |
| 압축 안의 압축·임시 폴더의 압축 풀기(major) | 풀기 창의 처음 풀 곳 = 압축 파일이 있는 폴더 — 일집 임시 폴더(닫으면 지워짐)·%TEMP%·Outlook 첨부의 숨은 INetCache 그대로, 최근 목록에도 들어감 | 새 `Services/TempLocations.cs`(`AppServices.TempLocations`): 그런 곳이면 처음 풀 곳은 바탕 화면(없으면 최근에 푼 곳, 그것도 없으면 빈 칸), 그런 곳은 최근 목록에 넣지도 보이지도 않음. 일집 임시 폴더는 창에서 골라도 "일집 임시 폴더에는 풀 수 없어요…" | `ExtractDialogTests.압축_안의_압축은_일집_임시_폴더_대신_바탕_화면에_권한다`·`임시_폴더의_압축은_바탕_화면을_권하고_임시_폴더는_기억하지_않는다`·`창_VM_일집_임시_폴더에는_풀지_않는다` |
| NFC 로 같은 두 파일 고치기 | 한 줄만 골라 지워도 둘 다 지움, 이름 바꾸기는 둘 다 같은 새 이름(겹친 이름의 압축) | 한 줄만 고르면 "… 2개 있어 어느 것을 …알 수 없어요"로 멈춤(원본 그대로), 두 줄 다 고르면 둘 다 지움(화면이 고른 줄마다 키 하나 — 예전엔 겹치는 키를 하나로 줄였음). 찾은 결과에서 폴더와 그 안 파일을 함께 골라 지우면 '찾지 못했어요'로 멈추던 것도 고침('찾지 못함'은 지우기 전에 한꺼번에 봄) | `NameEncodingTests.NFC로_같아지는_두_파일은_하나만_골라_고치지_않는다`·`폴더와_그_안_파일을_함께_골라_지워도_된다`, `MainViewModelEditTests.겉보기_이름이_같은_두_줄은_하나만_골라_지우지_않는다` |
| 찾은 폴더에서 Enter | 고른 폴더만 바뀌고 찾은 결과 목록 그대로 | 두 번 누르기와 같은 `GoToFolder`(찾기를 지우고 그 폴더로) | `BrowseTests.찾은_폴더에서_Enter도_찾기를_지우고_그_폴더로` |
| 파일 뒤에 같은 이름 폴더 | 파일 '보고서'(맥 NFD 또는 같은 이름) 다음 폴더 '보고서/' 면 폴더를 못 만들어 풀기가 멈추고 앞 파일이 남음(폴더가 먼저면 '보고서 (1)') | `ExtractionLedger.EnsureDirectory` 가 이번 해제가 만든 파일이면 '보고서 (1)' 로 비켜 두고 폴더를 만듦 — 차례와 상관없이 같은 결과(SharpCompress·EGG/ALZ 길, 7z.dll·압축된 TAR 는 원래 폴더 먼저). 풀 곳에 원래 있던 사용자 파일은 건드리지 않음 | `NameEncodingTests.파일_뒤에_같은_이름_폴더가_와도_…`(3)·`원래_있던_파일과_같은_이름_폴더는_…` |
| 끊긴 NAS 에서 풀기 창이 굳음 | 창이 열릴 때 최근 폴더마다, 풀 곳 칸에 한 글자 칠 때마다 `Directory.Exists`(끊긴 SMB 는 몇 초~수십 초) | 새 `Services/FolderProbe.cs`: 네트워크 폴더(UNC·네트워크 드라이브 — 드라이브 종류는 네트워크를 안 거침)는 열 때·칠 때 디스크를 보지 않고(목록에 그대로, 미리 보기는 '(2)' 확인 없이), [풀기] 때 한 번만 5초까지 기다림 — 못 닿으면 "‘…’ 폴더에 닿지 않아요…" | `ExtractDialogTests.네트워크_폴더는_창을_열거나_칠_때_디스크를_보지_않는다` |
| 모으는 목록의 폴더 선택 합계 | '선택: 1개 · 약 0B' | 폴더만이면 '선택: 1개', 섞이면 '선택: 2개 · 파일 10B' | `BrowseTests.모으는_목록의_폴더는_크기를_세지_않는다` |

### W2-07 검증 지적 수정(2026-10-13, 2차)

푼 Skip 은 없습니다. 지적 7개(소소한 것 넷을 묶은 '범위·과정' 하나 포함)를 모두 확인했습니다 — 5개(GZ·BZ2 회귀·진행 막대·XAML 시험·훑기 취소·휴지통 일부)와
'범위·과정'의 (c) 낡은 주석·(d) 시험 취소 알림을 고치고, '두 일집이 같은 순간 같은 이름으로 만들 때' 1개는 고치지 않았습니다(아래). (a) I2-12 범위·(b) HEAD 빌드는 1차에 적은 대로입니다.
새 시험 18개(`CompressFlowTests` 15·`CompressDialogXamlTests` 3 — Theory 줄 포함). 기대가 바뀐 기존 테스트는 없습니다.
새 시험 중 동작을 고친 것(GZ·BZ2 로 TAR 압축 6·진행 막대 2·맞춰 보는 도중 취소 1·휴지통 일부 1·시험 취소 2)은 고치기 전 코드로 잠깐 되돌려 실패함을 확인했습니다
(휴지통 '하나도 못 옮김' 줄은 예전 문구 그대로라 통과가 맞음. 처리기 고르기·훑기 취소·XAML 은 새 함수·창 만들기 확인이라 되돌려 보지 않음).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| TAR 파일을 GZ·BZ2 로(회귀) | 'backup.tar' → 'backup.tar.gz' 를 이름의 가장 긴 확장자('.tar.gz' — 풀기만 되는 압축된 TAR 처리기)로 골라 '압축된 TAR 만들기는 아직 지원하지 않아요'로 실패(날짜·각각 따로도) | `CompressFlow` 는 고른 형식으로 처리기를 고름(`ArchiveServiceLocator.ForExtension` — 확장자 하나 그대로), 만든 압축 시험기도 마지막 확장자로(GZ 는 GZ 로 시험 — 안쪽 TAR 를 임시 폴더에 다 풀지 않음). 열기·풀기는 예전 그대로(압축된 TAR 를 바로 펼침) | `CompressFlowTests.TAR_파일을_GZ_BZ2로_압축할_수_있다`(6)·`만든_압축은_만든_형식의_처리기로_시험한다` |
| 각각 따로 시험 중 진행 막대 | 시험 단계 알림에 '2/3' 순번을 붙여 보내 막대가 100% → 0%·33%·66% 로 돌아감 | 시험 단계는 순번을 글자로만('만든 압축 파일을 시험하는 중 (2/3) — 나.zip') — 움직이는 막대. 진행 창 막대 규칙은 `ProgressDialog.BarValue`(순수 함수) | `CompressFlowTests.각각_따로_시험_중에_진행_막대가_뒤로_가지_않는다`(2) |
| XAML 만들기 시험 | 소스 글자만 확인 — XAML 이 실제로 읽히는지는 앱을 켜야 앎 | 압축 창·옵션 칸만 보이는 창·설정 창을 띄우지 않고 만들어 묶기(양쪽)·칸 모양·묶기 오류 0 확인 | `CompressDialogXamlTests`(3) |
| 원본 훑기와 [취소] | 압축 전 원본 재기·지우기 전 맞춰 보기(원본 폴더 다시 훑기)가 취소를 안 봐 큰 폴더면 다 훑을 때까지 기다림, 맞춰 보는 도중 취소해도 '옮길까요?'를 물음 | `SourceCollector.Collect(…, 취소)`(폴더마다)·`TakeStamps`·`CheckDeletable`(파일마다)이 취소를 봄 — 취소면 '취소해서 원본은 지우지 않았어요.'. 엔진(ZIP·7Z·TAR)의 원본 모으기도 같은 취소를 받음 | `CompressFlowTests.원본_훑기는_취소를_본다`·`맞춰_보는_도중_취소하면_묻지도_옮기지도_않는다` |
| 휴지통으로 일부만 옮겨짐 | 한 번에 보낸 것 중 하나라도 실패하면 '옮기지 못해 그대로 두었어요'(옮겨진 것이 있어도) | 없어진 원본은 '원본 N개를 휴지통으로 옮겼어요.', 남은 것만 '원본 M개는 휴지통으로 옮기지 못해 그대로 두었어요: 이름…' | `CompressFlowTests.휴지통으로_일부만_옮겨지면_옮긴_것과_남은_것을_따로_알린다`(2) |
| 시험만 켜고 시험 취소 | 아무 알림 없음(각각 따로는 한 개만 시험했어도 '문제없어요', 상태 줄 '압축 완료') | '시험을 취소했어요(3개 중 1개만 시험했어요). 만든 압축 파일은 그대로 있어요.'('문제없어요'는 안 함), 각각 따로 상태 줄 '압축 완료: 3개 (시험은 취소)' | `CompressFlowTests.시험만_켜고_시험을_취소하면_알린다`(2) |
| 낡은 주석 | `CommandLineParser` '--compress … 저장 창부터' | '한 화면 압축 창부터' | (주석) |

고치지 않은 지적: '두 일집이 같은 순간 같은 이름으로 압축을 만들면 뒤의 것이 앞의 것을 덮을 수 있음' — 맞지만 예전 저장 창 흐름부터 같은 방식(이름을 고른 뒤 `File.Create`)이고,
막으려면 모든 엔진(ZIP·TAR·GZ·BZ2·나눈 ZIP·7Z)의 파일 열기를 '없을 때만 만들기'로 바꾸고 실패 정리가 남의 파일을 지우지 않게 해야 합니다. 빈 자리 파일을 먼저 만들어 두는 방법은
회사 PC 의 DLP(PCFILTER)가 막 닫은 파일을 잠깐 잠그는 일 때문에 바로 다시 열다 실패할 위험이 있어 하지 않았습니다. 원본은 지우기 직전에 압축 목록과 다시 맞춰 보므로 원본이 사라지지는 않습니다(검증 하네스 확인).
'HEAD(88814ba)만으로는 빌드 안 됨'은 1차와 같이 커밋 몫입니다. I2-12 '해제 방식 기억'은 1차에 적은 대로(W2-05 `extract.*` — 같은 이름 처리·폴더 열기·다 푼 뒤 압축 지우기를 ☰ ▸ 설정에 기억, 메인 창 풀기 대상 폴더·최근 폴더는 I2-13).

### W2-07 검증 지적 수정(2026-10-13, 1차)

푼 Skip 은 없습니다. 지적 9개를 모두 확인했습니다 — 7개를 고치고(코드), 1개(HEAD 만으로 빌드 안 됨)는 커밋 때 할 일이라 코드는 그대로, 1개('원본 휴지통' 기억)는 '압축 창은 기억하지 않고 설정 창에서만'으로 정했습니다.
새 시험 19개(`CompressFlowTests` 15·`CompressDialogRulesTests` 3·`ThrottledArchiveProgressTests` 1 — Theory 줄 포함). 기대가 바뀐 기존 테스트는 없습니다(`창은_VM_규칙을_쓴다` 는 소스 확인 줄을 더함 — 단언을 줄이지 않음).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| '만들 곳' 처음 값(I2-12) | 늘 첫 원본이 있는 폴더(마지막 폴더는 목록에만) | 설정 `compress.folderChoice`(☰ ▸ 설정): 원본이 있는 폴더(기본 — 예전과 같음)·마지막으로 만든 곳·정해 둔 폴더(`compress.fixedFolder`). 그 폴더가 없으면 원본이 있는 폴더. 목록에는 처음 값·원본 폴더·마지막 폴더·정해 둔 폴더(있는 것만) | `CompressDialogRulesTests.만들_곳의_처음_값은_설정대로`, `CompressFlowTests.만들_곳_설정을_저장하고_다시_읽으면_같다` |
| 시험 단계 알림 | 압축 끝 보고와 40ms 안이라 진행 창 앞 솎아내기가 버림 → 창이 '압축 100%'에 멈춘 듯 | `ArchiveProgress.IsStage` 단계 알림은 솎아내기가 늘 통과, 진행 창은 크기를 모르는 단계면 움직이는 막대. '원본을 살펴보는 중'·'원본과 압축 내용을 맞춰 보는 중'·'원본을 휴지통으로 옮기는 중'도 같은 알림 | `ThrottledArchiveProgressTests.단계_알림은_…`, `CompressFlowTests.시험_단계_알림은_솎아내기를_지나_진행_창에_닿는다` |
| GZ·BZ2 + 날짜 | `보고서.xlsx_20261013.gz` → 풀면 `보고서.xlsx_20261013`(확장자 깨짐) | 날짜·'(2)' 번호를 원래 확장자 앞에: `보고서_20261013.xlsx.gz`·`보고서_20261013 (2).xlsx.gz`(확장자로 보는 것은 점 뒤 영문·숫자 1~10자 — '보고서 v1.2 최종' 은 끝에) | `CompressFlowTests.GZ_BZ2의_날짜는_원래_확장자_앞에_붙는다`(2)·`각각_따로_BZ2에도_…`, `CompressDialogRulesTests.GZ_BZ2의_날짜는_원래_확장자_앞에` |
| 원본 지우기 확인 | 이름·크기만 압축 목록과 비교 → 같은 크기로 내용만 바뀐 원본도 휴지통으로 | 압축하기 직전에 원본 파일마다 크기·수정 시각을 재 두고(`CompressOutcome.SourceStamps`), 지우기 전(묻기 전·보내기 직전)에 같아야 지움 | `CompressFlowTests.크기가_같아도_내용이_바뀐_원본은_지우지_않는다`(묻는 동안·압축하는 동안) |
| 원본 지우기의 화면 굳음 | 진행 창을 닫은 뒤 UI 스레드에서 원본 폴더를 묻기 전·후 두 번 훑고 휴지통도 UI 스레드에서 | 압축 전 재기·묻기 전 맞춰 보기는 진행 창을 띄운 채 작업 스레드, [옮기기] 뒤 다시 맞춰 보기·휴지통은 새 진행 창('원본을 휴지통으로 옮기는 중...')을 띄우고 작업 스레드(보내기 전 [취소]면 안 보냄) | (자동 시험은 흐름이 그대로 도는지만 — 굳지 않는지는 MANUAL_CHECKLIST 14절) |
| 폴더와 그 안 파일을 같이 고름 | 둘 다 후보 → '원본 2개(크기 두 번)', 보내는 목록에 부모·자식 | 다른 후보 안의 후보·같은 원본 두 번은 빼고 폴더만 | `CompressFlowTests.폴더와_그_안의_파일을_같이_골라도_폴더만_옮긴다`(하나로·각각 따로) |
| 원본 그 자체 덮기(예전부터) | 'a.zip' 하나를 같은 곳에 a.zip 으로 [바꾸기] → 빈 압축으로 덮고 '압축 완료' | 창: [바꾸기] 없이 '‘a.zip’은(는) 지금 압축하려는 원본이에요…' [새 이름으로][취소], 흐름: 무엇을 골랐든 'a (2).zip'(각각 따로의 덮어쓰기 처리기도) | `CompressFlowTests.원본_그_자체는_바꾸기를_골라도_덮지_않는다`(ZIP·7Z·TAR)·`각각_따로에서도_원본_그_자체는_덮지_않는다`, `CompressDialogRulesTests.원본_그_자체에는_바꾸기를_주지_않는다` |
| '원본을 휴지통으로' 기억 | 압축 창에서 한 번 켜면 다음 창부터 켜진 채 | 압축 창은 기억하지 않음 — 처음 값은 ☰ ▸ 설정 "압축 창의 '다 만든 뒤 원본을 휴지통으로'를 켠 채로 열기"(기본 끔)에서만 | `CompressFlowTests.원본_지우기는_압축_창에서_기억하지_않는다`(2) |

고치지 않은 지적: 'HEAD(88814ba)만으로는 빌드 안 됨' — 맞는 지적이지만 코드가 아니라 커밋 몫(W2-06 에서 빠진 `Iljip/Services/EngineStreams.cs` 4줄·`Iljip.Tests/Contract/ArchiveEditReviewTests.cs` 를 W2-07 커밋에 같이 넣을 것 — HANDOFF '현재 진행 단계'에 이미 적혀 있음).
I2-12 의 '해제 방식 기억'은 W2-05 의 `extract.*`(같은 이름 처리·우클릭 풀기 뒤 폴더 열기·다 푼 뒤 압축 지우기)가 맡고, 메인 창 풀기의 대상 폴더 기억·최근 폴더는 I2-13(풀기 창) 몫으로 남깁니다.

### W2-07 에서 고친 것(2026-10-13) — I2-4 각각 따로·I3-4 압축 후 원본 지우기·I3-5 이름에 날짜·I3-6 쓸모없는 파일 빼기·I2-12 설정 기억과 한 화면 압축 창

푼 Skip 은 없습니다(Skip 테스트가 없던 새 기능). 창은 띄우지 않습니다 — 창의 규칙은 순수 VM(`CompressDialogRulesTests`), 그 뒤 흐름은 가짜 창의 답
(`FakeUserInteraction.CompressionRequestAnswer`)·가짜 휴지통·가짜 시계·가짜 시험기(`FakeCompressTester` — 기본은 진짜 시험기에 맡김)로 시험합니다(`CompressFlowTests`). 진짜 휴지통·셸은 부르지 않습니다.
새 시험 66개(`CompressFlowTests` 50·`CompressDialogRulesTests` 16 — Theory 줄 포함). 기대가 바뀐 기존 테스트는 없습니다 — 예전 압축 시험(저장 창 `SavePath` + 옵션 창 `CompressionOptions` 답)은
가짜 창의 `AskCompression` 이 그 두 답을 한 화면 창의 답으로 바꿔(덮어쓰기는 저장 창이 확인한 것으로) 그대로 통과합니다(`CompressSkipTests`·`RoundTripTests`·`SplitCreateTests` 등).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 압축 창(I2-12) | 저장 창 → 옵션 창 차례로, 매번 처음 값 | 한 화면 창(이름·만들 곳·형식·수준·암호·나누기·확인란·만들 파일 미리 보기), [확인]이면 값을 settings.json `compress` 에 한 번 저장(암호는 저장 안 함), 다음 창의 처음 값 | `CompressDialogRulesTests` 전부, `CompressFlowTests.압축_창의_값을_기억하고_암호는_저장하지_않는다`·`압축_설정을_…`·`깨진_설정이면_…`·`모르는_필드는_보존된다` |
| 각각 따로(I2-4) | 없음(늘 하나로) | 원본마다 압축 하나(파일은 확장자 뗀 이름·폴더는 폴더 이름), 있던 이름은 '이름 (2)', 진행 창 하나 '2/3', 끝에 요약 창, 못 만든 것만 요약·대기 목록에 남김 | `CompressFlowTests.각각_따로_…`(5) |
| 이름에 날짜(I3-5) | 없음 | `_20261013`·`_2026-10-13`·`_261013`, 이미 그 날짜가 있으면 안 붙임 | `CompressFlowTests.이름에_날짜를_붙인다`(3)·`날짜는_두_번_붙이지_않는다`·`각각_따로에도_…` |
| 쓸모없는 파일 빼기(I3-6) | 다 넣음 | 기본 켬 — Thumbs.db·desktop.ini·.DS_Store·._*·__MACOSX/·~$*·*.~tmp(폴더 안에서만 — 직접 고른 것은 담음), '쓸모없는 파일 N개는 뺐어요', 끄면 다 넣음, 목록은 압축 창·설정 창에서 | `CompressFlowTests.기본_빼기_목록의_…`(3)·`화면은_뺀_수를_알린다`·`빼기를_끄면_…`·`사용자_빼기_목록도_…`·`직접_고른_것은_…`·`빼기_목록_규칙` |
| 압축 후 원본 지우기(I3-4) | 없음 | 기본 끔. 켜면 만든 압축을 시험 → 통과하면 '원본 N개(…)를 휴지통으로 옮길까요?' [옮기기][그대로 두기](기본 [그대로 두기]) → 휴지통만(영구 삭제 없음). 시험 실패·휴지통 없는 곳·뺀 파일이 든 폴더·원본 폴더 안에 만든 압축·압축과 맞지 않는 원본·GZ/BZ2 는 안 지우고 까닭을 알림 | `CompressFlowTests.시험을_통과하고_…`·`진짜_시험기로도_…`(7)·`시험에_실패하면_…`·`그대로_두기면_…`(2)·`원본_지우기는_기본_꺼짐`·`휴지통이_없는_곳의_…`·`각각_따로에서_시험에_실패한_…`·`뺀_파일이_든_폴더는_…`·`압축_파일이_원본_폴더_안이면_…`·`묻는_동안_원본이_바뀌면_…`·`휴지통_실패는_경고만`·`GZ는_원본을_지우지_않는다` |
| 다 만들고 시험하기 | 없음 | 만든 압축을 모두 풀어 보며 확인(디스크에 안 남김) — '문제없어요' 또는 경고(압축은 남김) | `CompressFlowTests.다_만들고_시험하기`(2)·`진짜_시험기는_망가진_압축을_잡는다`(4) |

### W2-06 에서 고친 것(2026-10-13) — I2-3 압축 고치기·I3-3 압축 속 파일 고쳐서 다시 넣기

푼 Skip 은 없습니다(Skip 테스트가 없던 새 기능). 고치기는 서비스(`ArchiveEditTests` — 진짜 ZIP·7z, 반디집 `bz t`)와 화면(`MainViewModelEditTests` — 가짜 창)으로,
다시 넣기는 가짜 파일 감시(`FakeFileChangeSource` — 진짜 FileSystemWatcher·메모장·엑셀은 안 씀)로 시험합니다. 메인 창은 만들기만 하고 띄우지 않습니다(`MainWindowEditUiTests`).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 열린 압축 고치기(I2-3) | 없음(보기·풀기만) | ZIP·7Z 에 파일 넣기(메뉴·끌어 놓기)·지우기(Del)·이름 바꾸기(F2)·새 폴더. 같은 폴더 숨김 `.~이름.8글자.tmp` 에 다 쓰고 다시 읽어 검사한 뒤에만 바꿔 넣음 — 취소·잠김·쓰기 실패·검사 실패면 원본 바이트 그대로. ZIP 은 손대지 않은 항목을 바이트 그대로 옮기고 새 항목은 원래 암호 바이트·방식으로 암호화, 새 이름은 UTF-8. 7z 는 솔리드·파일 이름 암호화·암호 그대로. 그 밖의 형식은 '이 형식은 고칠 수 없어요' | `ArchiveEditTests` 전부, `MainViewModelEditTests` 전부 |
| 끌어 놓기(압축을 보는 중) | [새로 압축할 목록 만들기][취소](압축 파일이면 [열기]도) | 고칠 수 있는 압축이면 [이 압축 파일에 넣기]가 맨 앞(기본은 [취소] 그대로), 고칠 수 없는 압축은 예전 그대로 | `DropPolicyTests.고칠_수_있는_압축을_보는_중에는_넣을지_묻는다`·`…압축_파일_하나면_넣기_다음에_열기`·`고칠_수_없는_압축을_보는_중에는_넣기가_없다`·`열린_압축에_끌어_놓고_넣기를_고르면_들어간다` |
| 명령줄 `--add-to` | 없음(경로 목록으로 봄) | `--add-to 압축 경로…` = 메인 창 없이 넣기('{n}개를 넣었어요.' 알림) — 탐색기 메뉴는 아직 없음 | `CommandLineParserTests.add_to는_압축_하나와_넣을_경로들`·`우클릭_풀기만_메인_창_없이`, `MainViewModelEditTests.우클릭_넣기는_…` |
| 압축 속 파일 고쳐 저장(I3-3) | 없음 | 연 파일을 감시하다 내용이 바뀌면 '‘이름’이 바뀌었어요. 압축 파일에 다시 넣을까요?' [넣기][나중에][넣지 않기], [나중에]는 압축을 떠날 때·일집을 닫을 때 다시, 고칠 수 없는 형식은 '다른 곳에 저장' | `EntryReinsertTests` 전부 |

기대가 바뀐 기존 테스트: `CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 6 → 7 + '--add-to' 포함), `DropPolicyTests.압축을_보는_중_끌어_놓고_취소하면_그대로`·`압축을_보는_중_다른_압축을_끌어_와_열기를_고르면_그것이_열린다`
(보는 압축이 ZIP 이라 문구가 `AskWhileEditableArchive`, 선택지 맨 앞에 [이 압축 파일에 넣기] — 기본 [취소]·보던 압축 그대로라는 단언은 그대로. 고칠 수 없는 TAR 는 예전 문구·선택지라는 새 시험을 더함).
알아 둘 것(시간에 민감한 기존 시험 — 이번에 안 건드린 길): 마지막 전체 실행은 실패 0 이지만, 이 PC 가 바쁠 때(빈 메모리 0.8GB·PFDlpMon 이 CPU 한 개 이상·OneDrive 동기화) 중간 실행에서
`SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…`(W2-05 에 적은 것과 같음 — 3.2~4.8초, **이것만 따로 돌려도 3.6초로 실패**한 때가 있었고 그때 160MB 압축이 평소 20~25초가 아닌 41초 — 7z 만들기 코드는 이번에 안 바뀜)와
`LocatorTests.옮기기는_잠깐_잠긴_파일을_기다렸다_옮긴다`(0.25초 뒤 풀리는 잠금을 0.6초 동안 다시 해 봄 — 풀어 주는 작업이 스레드 풀에서 늦게 돈 한 번)가 실패했습니다. 한도는 그대로 — 실패하면 PC 가 한가할 때 다시 돌려 볼 것.

### W2-06 검증 지적 수정(2026-10-13, 2차)

푼 Skip 은 없습니다. 지적 7개를 모두 확인해 고치고(설명 문구 1개는 코드·문서만) 새 시험 29개를 더했습니다(`ArchiveEditReview2Tests` 21·`EntryReinsertTests` 8). 기대가 바뀐 기존 테스트는 없습니다.
새 시험은 모두 **고치기 전 코드로 되돌려 돌리면 실패**하는 것을 확인했습니다(명령줄 교착 2개는 60초 시간 제한으로 실패).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| '다시 넣을까요?'와 명령줄 열기(--open·--extract)가 겹침 | 명령줄이 명령 차례를 쥔 채 묻기·넣기가 끝나기를 기다리고, [넣기]는 그 차례를 기다려 둘 다 영영 멈춤(그 뒤 명령도 모두 막힘) | 압축을 떠나기 전에는 '조용히 기다리는 확인'만 기다리고(`OpenedEntryWatcher.WhenSettledAsync`) 묻거나 넣는 중인 파일은 그 흐름에 맡김 — 다른 압축이 열린 뒤 원래 압축에 넣음 | `EntryReinsertTests.묻는_동안_명령줄로_다른_압축을_열어도_멈추지_않는다`(2)·`감시기의_가라앉음은_묻기를_기다리지_않는다` |
| 묻는 중·넣는 중 일집 닫기 | 그 파일을 건너뛰고 닫기를 허락 → 작업 폴더가 지워져 고친 내용이 사라짐(넣기도 끊김) | 닫지 않고 '‘…’를 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요. 끝난 뒤에 닫아 주세요.' | `EntryReinsertTests.묻는_동안_일집을_닫으려_하면_닫지_않는다`·`넣는_동안_일집을_닫으려_하면_…`(ZIP·7z) |
| 빈 7z(모두 지운 결과)에 넣기·새 폴더 | 솔리드 정보가 없어 '압축 파일이 손상됐어요. 다시 받아 보세요.' | 솔리드 아님으로 보고 고침(빈 폴더만 있는 7z 도) | `ArchiveEditReview2Tests.빈_7z에도_넣기와_새_폴더가_된다`(3) |
| Windows 에 못 쓰는 이름(':'·끝 점)의 항목 다시 넣기 | 바꾼 적 없는 이름 때문에 '이름에 쓸 수 없는 글자가 있어요' → 못 넣음 | 다시 넣기(바꿔 넣기)는 압축에 있던 이름 그대로(`EditPlanner.ValidateExistingName` — 경로를 깨는 것만 막음). 사용자가 고르는 이름은 예전 규칙 | `ArchiveEditReview2Tests.다시_넣기는_압축에_있던_이름을_그대로_쓴다`(4)·`압축에_있던_이름_검사는_…`(9), `EntryReinsertTests.윈도우에_못_쓰는_이름의_항목도_…`(2) |
| 일집 풀기 엔진이 못 푸는 정상 ZIP(흘려 쓴 ZipCrypto + 데이터 서술자) | '원래 압축 파일에 이미 손상된 곳이 있어서…'(사실과 다름) | 구조는 맞는데 엔진이 ZIP 으로 알아보지 못하거나 방식을 모르면 '이 압축 파일은 일집이 풀 수 없는 방식으로 만들어져 있어서 고치지 않았어요…'(`ArchiveTester.UnreadableMessage`). 결과만 못 풀면 고치다 생긴 문제 | `ArchiveEditReview2Tests.일집이_못_푸는_정상_ZIP은_…`(2)·`결과만_못_풀면_…` |
| ZIP 검사 동안 진행 막대 | 다시 쓰기가 끝나면 100% 에 멈춰 검사 내내 그대로 | 검사(풀린 크기)도 막대에 넣고 풀어 본 바이트로 올림 | `ArchiveEditReview2Tests.ZIP_검사_동안에도_진행_막대가_오른다`(2) |
| 설명 문구 | '다시 넣기 = 지우기 + 넣기'가 남음 | '바꿔 넣기'로(코드 주석·HANDOFF·이 파일) | — |

### W2-06 검증 지적 수정(2026-10-13, 1차)

푼 Skip 은 없습니다. 지적 9개 중 8개를 고치고 새 시험 27개를 더했습니다(`ArchiveEditReviewTests` 14·`SevenZipEditProgressTests` 2·`EntryReinsertTests` 7·`MainViewModelEditTests` 3·`CommandLineParserTests` 1).
기대가 바뀐 기존 테스트: `CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 7 → 8 + '--add-to-pick' 포함 — 키워드를 더한 것, 단언을 줄이지 않음).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 7z 지우기·이름 바꾸기·새 폴더의 취소·진행 | 7z.dll 이 다시 압축하는 동안 [취소]가 듣지 않고(그 단계가 끝날 때까지) 막대가 멈춰 보임 | 취소하면 작업 사본을 잠가 7z.dll 의 다음 읽기를 실패시켜 바로 멈춤(블록 2MB — 이 PC 0.5~1.4초), 다시 압축하는 동안 결과 크기로 막대가 오름, 사본 만들기·검사도 진행에 넣음 | `SevenZipEditProgressTests` 2개 |
| 반디집 ZIP 유니코드 경로(0x7075) | 고치기가 원시 이름(CP949)으로 키를 만들어 목록 이름과 달라 '찾지 못했어요'·같은 이름을 묻지 않고 두 개 | 목록(SharpCompress)과 같게 첫 0x7075 의 UTF-8 이름을 키로 | `ArchiveEditReviewTests.유니코드_경로가_…`(4)·`유니코드_경로_ZIP을_고친_결과를_반디집이_푼다`, `MainViewModelEditTests.반디집_유니코드_경로_ZIP도_화면에서_고친다` |
| 다시 넣는 동안 또 저장(I3-3) | 넣은 뒤 기준을 '지금 파일'로 잡아 그 사이 저장한 내용이 넣은 것으로 여겨져 사라짐 | 넣을 내용을 먼저 복사해 고정하고 그 바이트의 표로 기준을 잡음 — 또 바뀌었으면 바로 다시 묻기. [넣지 않기]는 물어본 내용만 | `EntryReinsertTests.넣는_동안_또_저장하면_…`(2)·`넣지_않기는_물어본_내용만_넘긴다` |
| '기존 zip 고르기' | 없음 | `--add-to-pick 경로…` → 압축 고르기 창(ZIP·7Z), 고르지 않으면 조용히 | `CommandLineParserTests.add_to_pick은_…`, `MainViewModelEditTests.기존_압축_고르기로_넣으면_고른_압축에_넣는다` |
| 7z.dll 을 못 불러온 PC 의 7z | '이 형식은 고칠 수 없어요'(7z 는 고칠 수 있는 형식 — 틀린 말) | '이 PC에서는 7Z 파일을 고치는 부품(7z.dll)을 불러오지 못해…', 다시 넣기 질문도 '이 PC에서는 7Z 파일을 고칠 수 없어요.' | `MainViewModelEditTests.막힌_PC에서는_7z를_…`, `EntryReinsertTests.막힌_PC에서는_7z_안의_…` |
| 파일과 같은 이름의 폴더 넣기 | 파일 '보고서'와 폴더 '보고서/…'가 둘 다 생김(Windows 에 풀 수 없음) | 폴더째 '보고서 (1)' 로(한 넣기 안에서 같은 이름으로) | `ArchiveEditReviewTests.파일과_같은_이름의_폴더를_…`(2)·`비켜_넣기는_…` |
| 원래부터 손상된 압축 | '고친 압축 파일을 검사했더니 맞지 않는 곳이 있어서…'(고치다 생긴 것처럼) | 검사가 실패하면 원본도 검사해 '원래 압축 파일에 이미 손상된 곳이 있어서 고치지 않았어요(고치다 생긴 문제가 아니에요)…' | `ArchiveEditReviewTests.원래부터_손상된_ZIP을_…` |
| 연 뒤 지운·이름 바꾼 항목 다시 넣기, 넣기 실패 | '지우기 + 넣기'라 '찾지 못했어요' 오류, 고친 내용을 살릴 길 없음 | '바꿔 넣기'(`EditOp.Add.ReplaceExisting` — 있으면 묻지 않고 바꿈, 없으면 새로, 그 이름이 폴더면 비켜 넣기), 실패하면 '…넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?'(닫을 때는 [닫지 않기]) | `ArchiveEditReviewTests.바꿔_넣기는_…`(2), `EntryReinsertTests.연_뒤에_지운_항목도_…`(2)·`넣지_못하면_…` |
| 잃어버린 갱신 | 읽은 뒤 다른 프로그램·다른 고치기가 원본을 바꿔도 덮어씀 | 읽기 시작할 때 잰 크기·수정 시각이 바꾸기 직전과 다르면 바꾸지 않고 '고치는 동안 다른 곳에서 이 압축 파일이 바뀌어서…', 같은 프로세스 안 바꾸기는 한 번에 하나 | `ArchiveEditReviewTests.고치는_사이에_원본이_바뀌면_…`(2)·`동시에_고쳐도_…` |

고치지 않은 지적 1개: `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…` 가 바쁠 때 3027ms 로 실패 — W2-02 의 7z **만들기** 길(이번 고치기와 상관없음)이고, 한도를 늘리거나 Skip 하면 시험을 약하게 하는 것이라 그대로 둡니다(이미 `DisableParallelization` 묶음). 이번 전체 실행 두 번은 2770ms·1958ms 로 통과.
(→ Wave 2 통합 점검에서 원인을 찾아 고침: 솔리드 취소는 블록 하나 압축 시간만큼 걸림 — 보통의 블록 8m → 2m, 아래 'Wave 2 통합 점검에서 더한 것'.)

### W2-05 에서 고친 것(2026-10-13) — I1-5 같은 이름·I1-6 메인 창 없는 우클릭 풀기·I2-5 풀고 나서 지우기·I2-6 여러 개 풀기

푼 Skip 은 없습니다(이 묶음의 빈틈은 Skip 테스트가 없던 새 기능). 화면은 `FakeUserInteraction`(같은 이름 창 `AskCollision`·요약 창 `ShowSummary` 를 덧붙임 —
답을 넣지 않으면 '이름 바꾸기'라 예전 '이름 (1)' 시험이 그대로 통과), 셸·휴지통·설정은 `TestSupport/FakeServices.cs`(`TestServices` — 진짜 탐색기·휴지통은 절대 안 부름).
`ModuleInit` 이 일집 자료 폴더(`AppPaths.Root` — settings.json·임시 폴더)를 시험 전용 빈 폴더로 돌려, 가짜 없이 만든 화면(`new MainViewModel(ui)`)도 이 PC 사용자의 설정을 읽지 않습니다
(사용자가 '휴지통으로 옮기기'를 켜 두어도 시험이 픽스처를 진짜 휴지통으로 보내는 일이 없게).

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 같은 이름 파일(I1-5) | 늘 '이름 (1).ext' 로 비켜 씀(묻지 않음) | 일집 화면·우클릭 풀기는 설정(extract.onCollision, 기본 '물어보기')대로 덮어쓰기·건너뛰기·이름 바꾸기·취소 + '남은 파일도 똑같이'. 덮어쓰기는 같은 폴더 숨김 `.~이름.8글자.tmp` 에 다 쓴 뒤 한 번에 바꿈(잠겨 있으면 쉬운 한국어 오류·원래 파일 그대로), 취소는 이번에 만든 것만 되돌림(덮어쓴 경로는 장부에 안 넣어 되돌릴 때 사용자 파일을 안 지움). 압축 안의 같은 이름 두 개·폴더와 겹침은 묻지 않고 '(1)'. zip·7z·rar·tar·egg·bz2·7z.dll 임시 폴더 길 모두 | `CollisionTests.같은_이름_덮어쓰기는_…`·`건너뛰기는_…`·`이름바꾸기는_…`·`취소는_…`·`남은_파일도_똑같이를_…`·`덮어쓸_파일이_잠겨_있으면_…`·`압축_안의_같은_이름_두개는_…`·`bz2_단일_스트림도_…`, `ExtractSettingsTests.메인_창_풀기도_…`·`…취소는_조용히_끝난다`·처리기 3개 |
| 창 없이 풀기(재사용장치) | '이름 (1)' | 그대로(설정·같은 이름 창·휴지통·폴더 열기를 안 씀) | `ReuseDeviceContractTests.창없이_풀기는_같은_이름을_묻지_않고_이름1로_비켜쓴다`(실제 exe 두 번), `RecycleAfterExtractTests.창_없이_풀기는_설정과_휴지통을_쓰지_않는다`(소스) |
| 우클릭 풀기(I1-6) | 메인 창을 띄우고(이미 떠 있으면 앞으로) 보던 압축을 바꿔 연 뒤 풂 | '여기에 풀기'·'"이름"\ 에 풀기'·대화형 --extract-to 는 메인 창 없이 진행 창만, 보던 압축 그대로. 끝나면 '이름' 폴더를 탐색기로 엶(설정 extract.openFolderAfter, '여기에 풀기'는 안 엶). 오류는 쉬운 한국어 창 하나(모르는 엔진 오류는 `ErrorMessages.ToPlainMessage` — 영어 없음), 없는 파일이면 알림 | `ShellExtractFlowTests` 전부, `AppStartupContractTests.우클릭_풀기는_…`·`두번째_실행의_…`·`파이프는_받았음_…`·`같은_이름_창은_UI_스레드에서_…`, `CommandLineParserTests.우클릭_풀기만_메인_창_없이` |
| 풀고 나서 압축 파일 지우기(I2-5) | 없음 | 설정(extract.deleteArchiveAfter, 기본 끔)을 켜면 오류 0·건너뛴 파일 0·취소 아님일 때만 휴지통으로(나뉜 압축은 조각 모두 한 번에 — `ArchiveVolumes`). 휴지통 없는 곳(네트워크·이동식)·일집 임시 폴더·선택 항목만 풀기는 안 지움, 못 보내면 그대로 두고 알림 | `RecycleAfterExtractTests` 전부, `ExtractSettingsTests.숫자_조각은_…`·`PKZIP_나눔은_…`·`RAR_partN과_EGG_volN도_…`·`나뉘지_않은_압축은_…` |
| 여러 개 풀기(I2-6) | 파일마다 따로(진행 창·암호 창이 파일마다) | 진행 창 하나('2/5 · b.zip' + 전체 막대), 앞에서 맞은 암호(최근 3개)를 조용히 먼저 써 보고 틀리면 그 파일만 '앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.', 취소면 그 파일만 건너뜀. 끝에 요약(성공·실패·이유, 날 문구는 '자세히'). 취소면 남은 것은 손대지 않음. 탐색기에서 여러 개 골라 우클릭(파일마다 뜬 일집이 파이프로 넘긴 것)은 돌던 묶음에 붙음. 일집 열기 창에서 여러 개 고르면 '모두 풀기', `--extract a b …` 는 폴더 한 번 | `BatchExtractTests` 전부, `CommandLineParserTests.풀기_동사는_압축_여러_개를_받는다` |
| 설정 저장 | 없음 | `%LOCALAPPDATA%\Iljip\settings.json`(camelCase, schemaVersion 1, 같은 이름 동작은 글자, 모르는 필드 보존, 깨지면 settings.bad-….json 으로 옮기고 기본값, .tmp → Replace) — 모양은 W2-04 보관본과 같게 | `ExtractSettingsTests.저장하고_읽으면_같다`·`모르는_필드는_…`·`깨진_설정_파일은_…`·`설정_창은_사본을_…`·`시험_중엔_사용자_설정_파일을_읽지_않는다` |

기대가 바뀐 기존 테스트: 없음(덧붙이기만). `CollisionTests.NoLeftovers` 를 다른 시험에서도 쓰게 `internal` 로 바꿈.
알아 둘 것: `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…`(W2-02 — 압축 쪽, 이번에 안 건드림)는 PC 가 바쁘면(빈 메모리 0.4GB·PFDlpMon 등) 전체 실행에서 취소가 3.2~5.0초로 늦어 실패합니다(따로 돌리면 2.0~2.7초로 통과). W2-05 전 HEAD 를 따로 빌드해 같은 PC 에서 돌려도 같은 시험만 실패해 이번 변경 탓이 아님을 확인 — 한도는 그대로, PC 가 한가할 때 다시 돌려 볼 것.

### W2-04 보관본에서 더한 것(2026-10-13 — 검증에서 되돌렸다가 IJ3-01 이 다시 들임, 바뀐 것은 위 'IJ3-01 에서 더한 것') — I1-3·I1-4·I3-7

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 목록에서 파일 두 번 누르기·Enter | 아무 일 없음(폴더만 들어감) | 앱 전용 `%LOCALAPPDATA%\Iljip\temp\open\key16\순번\` 에 그 파일만 꺼내 기본 프로그램으로 엶. 실행 파일(.exe·.bat·.js·.ps1·.msi·.lnk …)은 먼저 '그래도 열까요?'(기본 [취소]). 암호는 열 때·앞서 맞은 것, 모르면 묻고 틀리면 다시. 인터넷 표식(Zone.Identifier)을 꺼낸 파일에 옮김 | `EntryOpenTests` 전체 |
| 임시 파일 정리 | — | 압축을 닫거나 바꿔도 작업 폴더는 앱을 닫을 때까지 둠(1차 검증 뒤 — 같은 압축을 다시 열면 다시 씀), 앱을 닫으면 지움(다른 프로그램이 잡은 파일만 남김), 다음 시작 때 주인 없는 것 청소(clip 은 24시간) | `EntryWorkspaceTests`(작업 폴더·청소)·`EntryOpenTests.다른_압축으로_바꿔도_…` |
| 닫을 때 고친 파일 | — | 압축 안에서 연 파일을 고쳐 저장했으면 '고친 내용은 … 지워져요' + [닫지 않기](기본)/[그냥 닫기] — W2-06 의 고친 파일 감시 전까지의 안전망 | `EntryOpenTests.닫을_때_고친_파일이_…` |
| 끌어 꺼내기 | 없음 | 탐색기·바탕화면으로 끌면 놓는 순간 꺼냄(2차 검증 뒤 — 파일이 여럿이면 첫 요청에 한 번에, 가상 파일 `FileGroupDescriptorW` + `FileContents` IStream, 비동기 모드, CF_HDROP 안 씀), 큰 항목은 진행 창·취소, 암호는 끌기 전에만 | `DragOutTests` 전체 |
| Ctrl+C | 없음(목록 글자 복사) | `temp\clip\guid\` 에 꺼내 클립보드에 파일 목록(복사) — 탐색기에서 붙여넣기 | `ClipboardCopyTests` 전체 |
| 그림 미리 보기 | 없음 | 툴바 그림 단추로 오른쪽 칸 켜기(설정 `ui.previewVisible` 기억), ←→ 넘기기, 확대, 50MB 초과 자리표시, 큰 그림 줄여 읽기(40MP), 최근 3장·200MB, 암호 단추 | `ImagePreviewTests` 전체 |
| 설정 파일 | 없음 | `%LOCALAPPDATA%\Iljip\settings.json`(이번에 처음 — `ui.previewVisible`·`open.warnExecutables`, 모르는 필드 보존, 깨지면 옆으로 옮김, 창을 닫을 때 한 번 저장, 창 없이 풀기는 읽지 않음) | `EntryWorkspaceTests.설정…`·`EntryOpenTests.시험이_만든_VM은_…`·`창이_뜰_때_…` |
| 목록 항목의 암호 표시 | — | `ArchiveEntry.IsEncrypted`(ZIP·7z·RAR·EGG — 엔진이 알려 줄 때) — 끌기·미리 보기 전에 물을지 정함 | `DragOutTests.암호_압축은_…`(목록이 암호 항목을 알려 줌) |

기존 테스트의 기대는 바꾸지 않았습니다(약화·Skip 추가 없음). 진짜 탐색기와의 끌어 놓기·붙여넣기·미리 보기 화면은 `MANUAL_CHECKLIST.md` 12절(실기기 확인 필요).

#### W2-04 1차 검증 뒤 고친 것(2026-10-13)

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 미리 보기 — 읽는 도중 다른 압축 열기 | 끝난 옛 읽기가 비운 기억에 다시 들어가, 새 압축의 같은 경로 그림(IMG_0001.JPG 등) 대신 옛 그림을 보임 | 압축이 바뀌기 전에 시작한 읽기의 결과는 기억하지 않음 | `ImagePreviewTests.읽는_도중_다른_압축을_열면_…` |
| 미리 보기 — ←→ 를 누르고 있기 | 시작한 그림 읽기는 멈추지 않아 여러 장이 겹침(80ms 간격 12장 → 동시 7장, 한 장 최대 약 160MB) | 그림 읽기는 한 번에 하나, 기다리다 건너뛴 것은 읽지 않음 | `ImagePreviewTests.빠르게_넘겨도_…` |
| 미리 보기 — [암호 넣기] 뒤 실패 | 손상 등 암호와 상관없는 실패도 오류 창 뒤 다시 '암호를 넣으면 볼 수 있어요' | 암호 창을 닫은 때만 단추, 그 밖의 실패는 '이 그림은 미리 볼 수 없어요' | `ImagePreviewTests.암호_단추_뒤_실패는_…`·`암호_단추를_눌렀는데_압축을_읽을_수_없으면_…` |
| Ctrl+C — 디스크 이름이 바뀌는 항목 | '회의록 10:00.txt'·'왜?.txt'·'CON.txt'·'자료:모음' 은 꺼내 놓고도 '찾지 못했어요', 여럿을 고르면 그것만 말없이 빠짐 | 풀기 엔진이 실제로 쓴 자리를 찾음(`LazyEntryExtractor.LocateExtracted` — 바뀐 이름·'(1)' 비켜 쓰기·대소문자), 하나라도 못 찾으면 알리고 아무것도 안 넣음 | `ClipboardCopyTests.디스크에서_이름이_바뀌는_…`·`이름이_바뀌는_항목을_한꺼번에_…`·`꺼낸_자리를_못_찾은_…` |
| 대소문자만 다른 두 파일 | Ctrl+C 는 하나만 넣음, 열기·끌기는 하나만 골라도 엔진이 둘 다 꺼내 앞엣것 내용을 줄 수 있음 | 둘 다·제 내용으로(뒤엣것은 엔진처럼 '이름 (1)', 끌기 서술자는 예전처럼 '(2)') | `ClipboardCopyTests.대소문자만_다른_…`·`EntryOpenTests.대소문자만_다른_…`·`DragOutTests.대소문자만_다른_…` |
| 연 파일의 처음 SHA-256 | 화면 스레드에서 재 큰 파일(400MB)이면 창이 2초쯤 굳음 | 열기 전에 뒤 스레드에서 잼 | `EntryOpenTests.연_파일의_처음_내용은_…`(창 없는 WPF 디스패처 스레드 `TestSupport/DispatcherThread`) |
| 끌기의 인터넷 표식 | 임시 파일에만 붙어 탐색기가 놓은 곳에 만든 파일엔 없음 | 원본에 표식이 있으면 끌기 데이터에 셸 형식 'ZoneIdentifier'(영역 번호) — 탐색기가 실제로 붙이는지는 손 점검 12절 | `DragOutTests.인터넷_표식이_있는_압축이면_…`·`EntryWorkspaceTests.표식에서_영역_번호를_읽는다` |
| 압축을 바꿀 때 연 파일 | 안 고친 파일의 작업 폴더를 바로 지움(메모장에서 고치는 중이면 저장 실패) | 앱을 닫을 때까지 둠(요구: '앱 종료·다음 시작 때'), 같은 압축을 다시 열면 그 폴더를 다시 씀 | `EntryOpenTests.다른_압축으로_바꿔도_연_파일은_…` |
| 실행 파일 경고 | .chm .msc .scf .xll .wsc .sct .inf .library-ms .search-ms .settingcontent-ms .ps1xml .psd1 .vb .ws .iso .img .vhd .vhdx 등은 경고 없이 엶 | 경고 목록에 더함 | `EntryWorkspaceTests.실행_파일_판정` |
| Ctrl+C 사본 | 복사할 때마다 쌓이고 앱을 닫아도 남음(24시간 뒤 청소만) | 켜 둔 동안 최근 2개, 닫을 때 마지막 1개만 | `ClipboardCopyTests.사본은_켜_둔_동안_…` |

**기대가 바뀐 기존 테스트(약화 아님 — 요구에 맞춘 계약 변경)**: `EntryOpenTests.다른_압축으로_바꿔도_고친_파일은_앱을_닫을_때까지_남는다` 의
"안 고친 작업 폴더는 압축을 바꿀 때 지워야 함"(`Assert.False(File.Exists(untouched))`)은 요구('앱 종료·다음 시작 때 정리')와 달라
`다른_압축으로_바꿔도_연_파일은_앱을_닫을_때까지_남는다` 로 바꿈 — 압축을 바꿔도(같은 압축을 다시 열어도) 남고, 같은 압축으로 돌아오면 같은 작업 폴더의 새 순번,
닫을 때 고친 것만 알리며, 앱을 닫으면 둘 다 지워짐을 단언. Skip 은 더하지도 풀지도 않음.

#### W2-04 2차 검증 뒤 고친 것(2026-10-13)

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 끌어 꺼내기 — 솔리드 압축의 폴더 | 탐색기가 파일마다 내용을 달라고 할 때마다 그 하나만 꺼내, 솔리드 7z(일집 7z 기본값)는 매번 묶음 처음부터 다시 풀어 파일 수의 제곱으로 느려짐(100KB × 300개: 95초) | 첫 요청에 끌어낸 파일을 한 번에 꺼내고(고른 것의 열쇠만 넘김 — 진행 창 하나) 나머지는 꺼내 둔 것을 줌(같은 예: 3초). 한 번에 꺼내기가 실패·취소되면 그 뒤는 예전처럼 항목마다 — 멀쩡한 파일은 받음 | `DragOutTests.솔리드_7z_폴더를_끌면_…`·`한번에_꺼내기가_실패하면_…`·`한번에_꺼낼_때는_…`·`작은_파일이_여럿이라도_합쳐_크면_…`·`파일_하나만_끌면_…` |
| 끌기 중 여러 항목 실패 | 실패한 항목마다 '끌어 꺼내지 못했어요' 창이 하나씩 | 끌기 한 번에 오류 창 한 번, 알림줄에 '끌어 꺼내지 못한 파일 N개' | `DragOutTests.끌기_중_여러_항목이_실패해도_…` |
| 미리 보기 — 대소문자만 다른 두 그림 | 'A.png' 를 본 뒤 'a.png' 를 고르면 기억한 'A.png' 를 보이고, → 로 넘기면 'a.png' 에서 멈춤 | 기억 열쇠·자리 찾기가 대소문자를 가림(자리는 같은 객체 먼저) | `ImagePreviewTests.대소문자만_다른_두_그림은_…` |
| 실행 파일 경고 | .rdp .py .pyw .website .jnlp 등은 경고 없이 엶 | 경고 목록에 더함(.rdp .py .pyw .pyz .pyzw .pyc .website .jnlp .msu) | `EntryWorkspaceTests.실행_파일_판정` |
| 안의 파일 열기 — 깊은 폴더 | 압축 안 폴더 구조째 꺼내(`…\open\key16\1\폴더\…\파일`) 깊은 폴더 파일은 엑셀(전체 218자)이 못 열 수 있음, 그때도 '열 프로그램을 찾지 못했어요' | 옆 폴더(`1.part`)에 꺼낸 뒤 파일만 `…\1\파일 이름` 으로 옮김(7-Zip·반디집처럼). '열 프로그램을 찾지 못했어요'는 연결된 프로그램이 없을 때만, 그 밖엔 '열지 못했어요' | `EntryOpenTests.깊은_폴더의_파일도_…`·`연결된_프로그램이_없을_때만_…`·`파일을_열면_…`(경로 단언을 새 구조로) |
| 꺼낸 자리 찾기(`LocateExtracted`) | 대상 경로에 '~' 가 있으면 .NET 이 짧은 이름을 풀며 디스크의 실제 대소문자로 바꿔, 대소문자만 다른 두 파일의 뒤엣것('Report (1).txt')을 못 찾고 앞엣것을 줌(이번 수정 중 옆 폴더를 '1~' 로 했다가 실제로 걸림) | 찾을 이름은 열쇠에서, 비켜 쓴 '(n)' 이름은 대소문자 무시로도 | `DragOutTests.엔진이_뒤엣것을_앞엣것_대소문자로_…` |

**기대가 바뀐 기존 테스트(약화 아님 — 끌기 계약 변경)**: 끌기는 '부른 항목만 하나씩'에서 '첫 요청에 끌어낸 파일을 한 번에'로 바뀜(솔리드 압축 지적).
`DragOutTests.부르기_전에는_꺼내지_않고_부른_항목만_한번_꺼낸다` → `…_처음_부를_때_끌어낸_파일을_한번에_꺼낸다`(서비스 호출 1번·넘긴 열쇠·세 파일 내용을 단언),
`내용은_GetData를_받은_순간에_그_항목만_…` → `내용은_GetData를_받은_순간에_꺼내_IStream으로_준다`, `화면은_끌기_데이터만_…`·`암호가_필요하면_…`·`큰_항목은_…`(취소 안 함)의
'꺼낸 파일이 하나'를 '끌어낸 파일 전부(이름·내용까지)'로. 파일 하나만 끌 때 그것만 꺼내는 계약은 새 `파일_하나만_끌면_그것만_꺼낸다` 로 그대로 지킴.
미리 보기 기억 열쇠 단언 4곳은 대문자('사진/D.WEBP')에서 원래 이름('사진/d.webp')으로(같은 강도). `EntryOpenTests.파일을_열면_…` 의 '그 파일만 꺼냄' 단언은 작업 폴더 전체로 넓혀
(파일 하나·옆 폴더 없음까지) 더 엄격하게. Skip 은 더하지도 풀지도 않음.

### W2-03 에서 고친 것(2026-10-13) — I2-2 나눠 만들기(분할 압축)

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| 7Z 나눠 만들기 | 없음 | 옵션 창 '나누기'(10MB·20MB·직접 입력 MB, 1MB 이상) → 7z 하나를 대상 폴더 안 숨김 임시 폴더에 다 만든 뒤 `이름.7z.001 …` 로 단순 절단(7-Zip 볼륨과 같은 바이트, 뒤에서부터 잘라 디스크는 원래 크기 + 조각 하나만 더 씀). 999조각까지 | `SplitCreateTests.칠지_볼륨은_…`·`칠지_볼륨을_반디집이_푼다` |
| ZIP 나눠 만들기 | 없음 | 보통 ZIP 을 다 만든 뒤 PKZIP 나눔(`이름.z01 … 이름.zip`)으로 바꿈(다시 압축 안 함 — `Services/ZipSpanWriter.cs`). 첫 조각 `50 4B 07 08`, 로컬 머리는 한 조각 안, 목록은 마지막 조각에(크면 기록 경계에서 넘김, 마지막 조각에 기록 하나 이상), ZIP64 도. 99조각까지 | `SplitCreateTests.픽스처_ZIP을_…`(일집·7z.dll·반디집)·`ZIP_서비스가_…`·`목록이_조각을_넘어도_…`·`ZIP64_…` |
| 나뉜 ZIP 읽기 | SharpCompress 가 형제 조각을 찾음 — 목록이 마지막 조각을 넘으면 못 읽음, 암호 판정은 마지막 조각만 봐 Unknown(한글 ZipCrypto 암호 나눔 ZIP 을 맞는 암호로도 못 풂) | 조각을 이어 '보통 ZIP 한 개'로 보이게 한 스트림(`Services/SpannedZipStream.cs`)으로 main ZIP 처리기가 엶 — 판정·AES 직접 해제도 그대로. 앞 조각이 없으면 그 이름으로 `VolumeMissingException`, 풀 대상 폴더도 안 만듦 | `SplitCreateTests.가운데_조각이_빠지면_…`·`마지막_조각만_있으면_…`, `ZipPasswordProbeTests.나눔_ZIP도_조각이_다_있으면_판정한다`, 기존 `zip_split` 픽스처 시험 |
| 결과 이름·덮어쓰기 | — | 같은 이름의 조각이 하나라도 있으면 세트 전체를 '이름 (2)' 로(있던 세트는 바이트 그대로, 7Z 는 다음 번호 조각도 봄). 다 만든 것이 조각 크기 이하면 나누지 않고 고른 이름 그대로(덮어쓰기는 예전과 같음). 실패·취소면 조각·임시 폴더 없음 | `SplitCreateTests.같은_이름_세트가_…`·`한_조각에_들어가면_…`·`취소하면_…`·`조각이_너무_많으면_…` |
| 화면 | — | 끝나면 '3개 조각으로 나눠 만들었어요(이름.z01 … 이름.zip). 받는 사람은 모든 조각을 같은 폴더에 두고 이름.zip을 열면 돼요.' + Windows 탐색기 안내, 그 세트를 엶(ZIP 은 .zip, 7Z 는 .001). '나누기' 칸은 ZIP·7Z 만 | `SplitCreateTests.화면은_…`·`나누기_칸은_ZIP과_7Z만_보인다`·`나누기_크기_고르기`·`창_XAML에_나누기_칸이_있다` |
| 덤: ZIP 진행률의 총 파일 수 | 원본 폴더 안에 저장하면 자기 자신이 총수에 들어가 끝에도 N-1/N | 담지 않을 자기 자신(나눌 때는 고른 이름도)을 총수·총량에서 미리 뺌 | `SplitCreateTests.ZIP_나누기_진행률과_원본_폴더_안_저장` |

기대가 바뀐 기존 테스트(약화 아님): `ZipPasswordProbeTests.판정할_수_없는_모양은_예외없이_Unknown` 의 `split_last`(조각이 다 있는 반디집 나눔 ZIP 의 마지막 조각 → Unknown) 행은
나눔 ZIP 을 이제 판정하므로 '앞 조각이 없는 마지막 조각 → Unknown'(`split_missing_piece`)으로 바꾸고, 조각이 다 있으면 NotApplicable·항목 수·한글 ZipCrypto 암호 CP949 판정을
새 시험(`나눔_ZIP도_조각이_다_있으면_판정한다`)으로 단언. `LocatorTests` 의 '압축 파일이 아닌' 문구 검사에 새 예외 두 개(`TooManyVolumesException`·`SplitSizeTooSmallException`)를 더함.

### W2-02 에서 고친 것(2026-10-13) — I2-1 7z 만들기·I1-7 목록까지 잠긴 압축

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| I2-1 7z 만들기 | `NotSupportedException`, 저장 형식에 7Z 없음 | 7z.dll 로 만듦(수준 저장만·빠름·보통·최대, 솔리드, AES-256, 파일 이름 암호화). 저장 형식에 7Z(7z.dll 을 쓸 수 있을 때만), 옵션 창 7Z 칸. 모든 폴더가 항목(7-Zip·반디집 7z 와 같은 목록), 잠긴 원본은 ZIP 처럼 건너뜀 | `KnownGapTests.압축_7z_왕복이_원본과_같다`(Skip 해제), `SevenZipCreateTests`, `CompressionOptionsRulesTests`, `BandizipCliTests.일집_7z_…`, `CompressSkipTests` .7z 행 |
| 1차 W1-B 지적 ① 진행 막대가 몇 초 만에 100% | 7z.dll 이 원본을 메모리로 다 읽음(7-Zip 기본 블록·스레드) | 진행률 = 7z.dll 이 실제로 읽은 바이트(`LazySourceStream`), 사전·블록·스레드를 줄임(보통 mt≤4·d=16m·c=8m — Wave 2 통합 점검에서 c=2m). 160MB 실측: 100% 도달 17.7초/전체 19.9초(기본값이면 0.3초/29초) | `SevenZipCreateLargeTests.보통_수준_큰_원본의_진행률이_…`, `ProgressTests` 7z 행 |
| ② 취소가 끝날 때만 먹음 | 원본을 다 읽은 뒤엔 취소를 볼 곳이 없음 | 원본 읽기마다·출력 쓰기마다(파일만 고른 경우) 취소 확인. 30% 지점 취소 1.1~1.7초, 처음 바이트에서 취소 13~36ms. 실패·취소면 임시 파일(`.~이름.8글자.tmp`)·임시 폴더를 지우고 덮어쓰려던 기존 파일은 그대로 | `SevenZipCreateLargeTests` 취소 3개, `SevenZipCreateTests.압축_도중_취소하면_…`, `MainViewModelPasswordTests.화면에서_7z_만들기를_취소하면_…` |
| ③ 메모리 1.7~3.4GB | 7-Zip 기본 설정 | 보통 수준 160MB 에서 전용 메모리 증가 약 205MB(한도 700MB). 설정을 7-Zip 기본으로 되돌리면 1,167MB 로 시험이 실패하는 것을 확인 | `SevenZipCreateLargeTests.보통_수준_큰_원본의_메모리가_700MB_아래다`, `SevenZipCreateTests.기록_수준별_7z_dll_설정` |
| I1-7 남은 몫 | 목록까지 잠긴 7z·RAR 의 '암호 없음'이 일반 `PasswordRequiredException`, 열 때 틀린 암호는 경고 없이 다시 물음 | `ListPasswordRequiredException`(PasswordRequiredException 의 하위 형식, 문구 '파일 목록까지 암호로 잠긴 압축 파일이에요. 암호를 넣어 주세요.'), 화면은 `PasswordPromptReason.ListLocked` 로 묻고 틀리면 '암호가 맞지 않아요. 다시 넣어 주세요.' 경고 뒤 다시, 취소면 아무것도 안 바뀜. 만든 7z 를 열 때는 방금 넣은 암호로(다시 안 물음) | `ListPasswordFlowTests`, `ReuseDeviceContractTests.창없이_풀기는_목록까지_잠긴_…` |

기대가 바뀐 기존 테스트(약화 아님 — 설계가 바꾼 동작을 그대로 단언): `PasswordTests`(기록 2곳)·`RarTests`·`SevenZipLibraryTests` 의 머리 암호 '암호 없음'
`IsType<PasswordRequiredException>` → `IsType<ListPasswordRequiredException>`(정확한 형식 단언 그대로, 하위 형식으로), `MainViewModelPasswordTests`·`CommandLineTests`·
`FormatFlowTests` 의 첫 창 이유 `Required` → `ListLocked`(머리 암호만)와 틀린 암호 뒤 `Warnings` 비어 있음 → '맞지 않아요' 경고 한 번(새 동작을 단언 — 그 밖의 형식은 여전히 경고 없음 단언),
문구 상수 `PasswordRequiredException.HeaderMessage` 값이 '파일 목록까지 암호로 잠긴…'으로 바뀜(테스트는 상수를 씀), `RoundTripTests` 의 '.7z 만들기 NotSupported' 행 → '.7z 원본 없음' 행,
`RoutingTests` 저장 필터(기록)에 7Z, `LocatorTests.저장_형식에_7Z가_없다` → `저장_형식의_7Z는_7z_dll을_쓸_수_있을_때만`(있을 때 들어 있음 + 막힌 PC 에선 없음),
`SevenZipLibraryTests` 막힌 PC 7z 만들기 `NotSupportedException` → `EngineUnavailableException`(문구까지). 보관분 시험 중 `원본이_잠겨_있으면_그_파일_이름으로_알린다`는
I0-2 규칙(건너뛰고 나머지 압축, 하나도 못 담으면 이름이 든 안내)으로, `기록_보통과_최대는_LZMA2_블록을_줄인다` 는 새 설정 표(`기록_수준별_7z_dll_설정` — 수준별 전체 값)로,
'보통 수준 큰 입력' 시험은 더 엄격한 `SevenZipCreateLargeTests`(취소 20초 → 3초, 메모리 추가)로 옮김.

### W2-01 에서 고친 것(2026-10-13) — Skip 해제·1차 W1-B 검증에서 막힌 것

| 무엇 | 예전 동작 | 지금 | 지키는 테스트 |
|---|---|---|---|
| I2-2 숫자 분할 열기(`.7z.001`·`.zip.001`) | 미등록 | 7z 는 7z.dll 이 경로로 조각을 찾고, ZIP 은 `SplitVolumeStream` 으로 이어 main ZIP 처리기(한글 이름·암호 판정 유지)로. 빠진 조각(가운데·끝)은 그 이름, 덜 받은 마지막 조각은 '덜 받아졌어요' | `KnownGapTests.숫자분할_…`, `SevenZipEngineTests.숫자분할_조각이_빠지면_…`, `TruncationTests.숫자분할_…` |
| I2-9 zipx·tgz·tar.gz·txz·tbz2·xz·iso·lzh·SFX (+cab·wim) | 미등록, .tar.gz 는 안쪽 `.tar` 하나 | zipx·iso·lzh·xz·cab·wim 은 7z.dll, 압축된 tar 는 tar 안 내용까지 한 번에(**반디집 7.46 은 .tar.xz·.tbz2 를 안쪽 `.tar` 하나로만 보여 줌 — 일집은 한 단계 더 펼친다, W2-01 요구**), SFX exe 는 안쪽 zip·7z·rar·egg·alz 를 찾아 엶(실행 안 함) | `KnownGapTests` 세 개, `CompressedTarTests`, `SfxTests`, `CabTests` |
| (a) 잘린 RAR·LZH·ZIPX 가 '완료' | 7z.dll 이 읽은 데까지만 열어 뒤 파일이 조용히 빠짐(RAR4·LZH 머리 사이 잘림은 1차 W1-B 의 '남은 한계') | 열 때 구조 검사(`ArchiveCompleteness` — RAR5 끝 머리, RAR4 판 29 이상이면 ENDARC 필수, LZH 끝 표시 0 필수) + 풀린 항목마다 크기·확인값 검증. 잘리면 아무것도 옮기지 않음 | `TruncationTests` |
| (b) 잘린 xz 에서 CPU 100%·끝없는 대기(창 없이 풀기가 안 끝남) | SharpCompress `XZStream` 이 블록 채움·검사값 구간이 잘린 xz 에서 Read 안에서 끝없이 돎(취소도 안 먹음) | `XZStream` 을 어디서도 안 씀 — xz 는 7z.dll 로만(열 때 꼬리 검사, 스니퍼는 512바이트만), 취소는 출력 스트림에서 | `XzTests`, `CommandLineTests.실행파일_extract_to_잘린_압축은_30초_안에_1로_끝난다` |
| (c) 손상된 .tgz·.tar.gz·.tar.xz 가 '손상' 오류와 함께 틀린·절반 파일을 남김 | 앞으로만 읽으며 대상에 바로 씀 | 대상 안 숨김 임시 폴더에 `.inner.tar` → main TAR 처리기로 펴고 모두 맞을 때만 옮김(전부 아니면 전무, 남은 공간 미리 확인) | `CompressedTarTests`, `XzTests` |
| (d) UDF ISO 가 '손상', Joliet 없는 ISO9660+UDF 이름이 8.3 | 늘 'Iso' 로 엶 | UDF 먼저·안 되면 ISO9660, 잘림은 PVD 볼륨 크기·UDF 끝 기준점으로(7z.dll 의 '끝 잘림' 표시는 정상 이미지에도 떠서 안 믿음). Joliet 없는 순수 ISO9660 은 원래 8.3 대문자(반디집·7-Zip 도 같음) | `DiscImageTests` |
| (e) 잘린 .gz 가 '완료'·반쪽 파일 | W1-A 에서 이미 고침(확인값 검사) | 모든 자리 절단 시험을 더해 확인 | `TruncationTests.끝이_잘린_gz는_…`, `CorruptArchiveTests` |
| (f) 헤더 암호 7z 우클릭 회귀 | — | 7z.dll 경로에서도 '여기에 풀기'·'"이름"\ 에 풀기'가 암호를 한 번 묻고, 틀리면 다시, 취소면 아무것도 없음 | `CommandLineTests.헤더암호_7z…`, `MainViewModelPasswordTests` |
| 7z.dll 을 못 불러오는 PC | — | 7z·RAR 는 main 의 SharpCompress 로, 나머지는 `EngineUnavailableException`(한국어). 앞서 목록을 봐 안쪽 tar 가 캐시에 있는 .tar.xz 도 목록부터 같은 안내(목록만 보이고 풀기는 안 되는 어긋남·테스트 순서에 따라 흔들리던 것 고침) | `SevenZipLibraryTests.막힌_PC에서도_…`, `SevenZipLibraryTests.캐시된_tar_xz도_…` |

7z.dll 로 바꾸며 '기록' 테스트를 새 값으로 고친 것: 라우팅 표(`RoutingTests` — 새 확장자), 예외 타입·문구(`CorruptArchiveTests`·`PasswordTests` — 7z·RAR·.tar.gz 손상은 일집 `CorruptArchiveException`,
잘림이면 '끝부분이 잘려'), 반디집 픽스처 목록 모양(`BandizipFixtureTests.기록_압축된_tar는_tar_안_내용이_바로_보인다`·`기록_xz_단일파일_목록`), RAR 항목 이름의 폴더 구분
(`RarTests.기록_RAR_항목_이름의_폴더_구분` — 7z.dll 은 '/', 대신 경로는 '\'), 압축된 tar 진행률(`CompressedTarTests.기록_진행률은_…` — 앞 절반 바깥 풀기·뒤 절반 tar).
보관분(1차 W1-B) 시험 중 '앞 파일은 남긴다'였던 잘림·취소·대상 쪽 오류 시험은 설계(전부 아니면 전무)대로 '아무것도 남기지 않는다'로 바꿨고,
한 파일의 CRC 오류만은 main 계약(`RarTests.손상된_RAR은_CRC_오류로_알리고_앞_파일은_남긴다`)대로 그 앞에서 맞게 풀린 파일을 남깁니다.

웹에서 받은 파일 표시(MOTW)는 W2-01 때는 **고치지 않고 기록만** 했습니다(재사용장치 엑셀 COM 자동 처리 영향 — 사용자 결정 사항).
→ IJ3-03(2026-10-15, 사용자 결정 I2-15): 일집 창·우클릭 풀기만 붙이고 창 없이 풀기는 붙이지 않음(`InternetMarkTests`·`ReuseDeviceContractTests.창없이_풀기는_인터넷에서_받은_표시를_…`).
`KnownGapTests.기록_웹에서_받은_표시를_풀린_파일에_옮기지_않는다` 는 이제 '서비스(엔진)는 옮기지 않는다'는 기록으로 그대로 둡니다.

### 고친 버그(W1-A, 2026-10-12) — Skip 해제·근거 테스트는 `FixedBugTests` 로

| 사유 | 무엇 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| B1 | 반디집·7-Zip 7z 의 빈 파일에서 해제 실패 | SharpCompress 0.50.5 로 올리니 고쳐짐(코드 변경 없음) | `반디집_7z_전체_해제가_반디집과_같다`, `헤더암호_7z_전체_해제가_반디집과_같다`, `FixedBugTests.반디집_7z_빈파일은_0바이트로_풀린다` |
| B2 | 원본 이름(FNAME) 없는 .gz 를 못 풂 | 이름이 비면 압축 파일 이름에서 `.gz` 를 뗀 이름(`ArchiveNaming.SingleStreamName`, 비면 '압축풀림') | `gz_단일파일_반디집산출물이_풀린다`, `GZ_단일파일_왕복이_원본과_같다`, `FixedBugTests.FNAME_없는_gz는_…` |
| B6 | TAR 압축 취소가 큰 파일 쓰는 중 안 먹힘 | 원본을 `CancellableReadStream` 으로 감싸 읽을 때마다 취소 확인 + 파일 안 진행률 보고 | `TAR_압축_도중_취소하면_OCE`, `FixedBugTests.TAR_마지막_큰파일_쓰는중_취소하면_OCE`, `TAR_압축은_큰파일_안에서도_진행률이_오른다` |
| B7 | 진행 창이 끝까지 0 | `long.MinValue` 시작값 대신 '한 번 보냈는지' 플래그 | `솎아내기_진행률은_첫_중간보고를_통과시킨다`, `ThrottledArchiveProgressTests`, `FixedBugTests.솎아내기_간격0이면_…` |
| B8 | 손상 7z·egg·alz 열기 실패 후 원본 잠김 | 7z 는 0.50.5 로 고쳐짐. egg/alz 는 원본·분할 조각 스트림을 직접 열고 실패하면 모두 닫음 | `FileLockTests` 전부(분할 EGG 조각 포함) |
| B9 | 크기 0 항목이 든 EGG 를 못 엶 | 크기 0·폴더 항목의 NullReferenceException 만 0 으로 보고, 빈 파일은 열지 않고 만듦 | `EGG_폴더항목과_빈파일이_있어도_열리고_풀린다`, `FixedBugTests` 두 개 |
| B10 | ALZ 의 CP949 한글 이름이 `�` 로 깨짐 | `AlzNameScanner` 가 원본 바이트를 직접 읽어 항목 수·ASCII 부분이 맞을 때만 CP949/UTF-8 로 푼 이름으로 바꿈(아니면 예전 이름) | `ALZ_CP949_한글_이름이_바르게_보인다`, `ALZ_한글_이름으로_선택해제가_된다`, `AlzNameScannerTests` |
| TAR-빈폴더 | TAR 압축에 빈 폴더가 빠짐 | 0.50 의 `TarWriter.WriteDirectory` 로 빈 폴더 항목 기록 | `KnownGapTests.TAR_압축이_빈폴더를_담는다` |

B9·B10 은 합성 EGG/ALZ 로만 확인했습니다(진짜 알집 산출물은 MANUAL_CHECKLIST 6절).

#### W1-A 1차 검증 지적으로 고친 것(2026-10-12)

| 무엇 | 예전 동작 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| 잘리거나 확인값이 틀린 .gz·.tar.gz 를 '완료'로 풂 | SharpCompress 0.50·.NET GZipStream 은 gzip 끝의 CRC32·크기를 안 보고 잘림도 모름 → 절반짜리가 정상 이름으로 남음(B2 를 고치며 흔한 .gz 가 모두 이 길). 이어 붙인 여러 멤버 .gz 는 첫 멤버만 | 풀린 내용은 SharpZipLib `GZipInputStream` 으로 읽음(멤버마다 CRC·크기 확인, 잘림 예외, 여러 멤버 이어 풀기, 멤버 뒤 쓰레기 무시) — `GZipArchiveService.OpenEntryData`. 목록·이름은 그대로 SharpCompress. 속도는 SharpCompress 와 비슷(128 MB 약 2.5초) | `CorruptArchiveTests` 의 gz 사례들, `손상된_gz는_…`, `여러_멤버를_이어붙인_gz는_모두_풀린다`, `끝에_0이_덧붙은_gz도_풀린다` |
| 잘리거나 데이터가 깨진 EGG/ALZ 를 '완료'로 풂 | EggDotNet 은 풀린 내용을 확인하지 않음 → 짧은·틀린 파일이 남음 | 서비스가 크기는 늘, CRC 는 적혀 있을 때(0 아님) 맞춰 보고 틀리면 그 파일을 지우고 예외(솔리드 EGG 는 항목별 CRC 가 없어 크기만). 암호 항목이면 문구에 '암호가 틀렸거나'를 넣어 화면이 다시 묻게 함 | `잘린_EGG_ALZ는_…`, `데이터가_깨진_EGG_ALZ는_CRC로_알아챈다`, `확인값_불일치는_암호_항목에서만_…` |
| 끝 구분자 없는 EGG/ALZ 폴더 항목이 0바이트 파일로 풀림 | 이름 끝 `/` 만 봄 → 그 아래 항목에서 'already exists' 로 실패 | 크기 0 이면서 폴더 속성(EGG 0x80·POSIX 디렉터리, ALZ 0x10 — ALZ 속성은 EggDotNet 이 버려 `AlzNameScanner` 가 읽음)이면 폴더 | `끝_구분자_없는_폴더항목도_폴더로_풀린다` |
| ALZ 한자·기호만 있는 이름이 CP437 로 깨짐, 잘린 ALZ 는 한글 이름 복구 포기 | ZIP 용 판별(CP949 결과에 한글이 있어야 채택)을 씀 / 끝 표시가 없으면 스캐너가 통째로 포기 | `AlzNameScanner.DecodeName`(UTF-8 → 빈틈없는 CP949 → CP437), 잘린 파일은 이름까지 온전한 항목만 돌려주고 항목 수·ASCII 부분이 맞으면 씀 | `ALZ_한자_기호_확장한글_UTF8_…`, `잘린_ALZ도_온전한_항목의_한글_이름은_보인다`, `AlzNameScannerTests` |
| 점뿐인 이름의 FNAME 없는 .gz(`..gz`·`...gz`) | 항목 이름 '.'·'..' → '안전하지 않은 경로' 오류 | 점·공백뿐이면 '압축풀림'(`ArchiveNaming`) | `이름이_점뿐인_gz는_압축풀림으로_풀린다` |
| RAR5 서명 뒤가 쓰레기인 파일이 '0개 항목'으로 열리고 '해제 완료' | SharpCompress 0.50 이 오류 없이 빈 목록 | 항목이 하나도 없는 RAR 은 손상으로 알림(WinRAR 은 빈 RAR 을 만들지 않음) — `RarArchiveService.OpenForRead` | `CorruptArchiveTests` 의 `rar5_sig_garbage.rar` |
| 같은 폴더에 동시에 여러 개를 풀면 일부가 '사용 중' 오류 | '있는지 확인 → 만들기' 사이 경쟁 | 만들기를 `FileMode.CreateNew` 로 한 번에, 그새 생겼으면 다음 이름(`OutputFiles`, 모든 서비스 공용) | `같은_폴더에_동시에_여러번_풀어도_…` |
| 분할 EGG 를 열 때 폴더의 모든 파일을 열어 봄 | 수천 개 폴더에서 느리고, OneDrive '온라인 전용' 파일을 내려받게 할 수 있음 | 같은 폴더의 다른 `.egg` 만 후보로(`SplitVolumeCandidates`) | `분할_EGG_조각_후보는_같은_폴더의_다른_egg_파일만`, `FileLockTests` 분할 사례 |

### W1-C 에서 고친 것(2026-10-12) — 암호·무결성

| 사유 | 무엇 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| B3 | 반디집(한국어 Windows)이 한글 암호로 만든 ZIP 을 맞는 암호로도 못 풂(반디집은 암호를 CP949 바이트로 씀) | 풀기 전에 `Services/ZipPasswordProbe.cs` 가 중앙 디렉터리를 읽어 암호의 UTF-8·CP949 바이트 중 맞는 것을 판정. CP949 면 ZipCrypto 는 엔진에 바이트 그대로(Latin-1 요령), WinZip AES 는 `Services/WinZipAes.cs` 가 직접 품(7z.dll 없이 — 한국어가 아닌 Windows 에서도 됨). 만들 때 ZipCrypto 한글 암호는 CP949 로 씀(반디집 `bz.exe` 양방향 실측), AES 한글 암호는 SharpZipLib 가 UTF-8 로만 써서 화면이 경고 | `반디집_한글암호_ZIP이_풀린다`(Skip 해제), `FixedBugTests.반디집_한글암호_ZIP에_틀린_한글암호는_…`, `ZipPasswordProbeTests`, `ZipEncryptionTests`, `BandizipCliTests` |
| B4 | ZIP 해제 때 CRC-32 를 안 봄 — 깨진 데이터를 틀린 내용으로 풂, 1바이트 검사를 우연히 통과한 틀린 ZipCrypto 암호가 쓰레기 파일을 남김 | 풀린 내용의 CRC 를 항목 값과 비교(암호 없는 항목 → `ChecksumMismatchException`, 깨진 그 파일만 지움 / 암호 항목 → 판정이 암호를 충분히 확인했으면 손상, 아니면 틀린 암호 — 아래 1차 검증 지적). 판정이 모든 ZipCrypto 항목의 확인 바이트를 보므로 우연 통과는 둘째 항목에서 걸러짐. 항목이 하나뿐이어도 CRC·Deflate 오류에서 걸리고 `Services/ExtractionLedger.cs` 가 이번에 만든 것을 모두 지움. 덤: AE-2(일집 AES, CRC 칸 0)는 SharpCompress 가 인증값을 안 봐 깨져도 조용히 풀었는데 `WinZipAes` 가 HMAC 을 확인 | `CRC가_틀린_ZIP은_오류로_알려야_한다`(Skip 해제), `FixedBugTests` B4 세 개, `AES_데이터가_깨지면_…` |
| B5 | 7z 틀린 암호(Data Error)·ZipCrypto 우연 통과(Deflate 'Bad state')를 화면이 암호 문제로 못 알아봄 | 일집 예외 체계(`Services/ArchiveExceptions.cs`: `PasswordRequiredException`·`WrongPasswordException`·`ChecksumMismatchException` …)와 `Services/EngineErrorTranslator.cs` — 서비스 경계에서 엔진 예외를 바꿈(암호 항목에서 암호를 넣었는데 데이터 오류면 틀린 암호, 7z 헤더 암호는 `SevenZipArchiveService` 가 확인). 화면 `IsPasswordRelated` 는 `ArchivePasswordException` 이면 예, 다른 일집 예외면 아니오, 엔진 예외만 예전 문구 판정. EGG 도 `DecryptFailedException`·암호 항목 CRC 불일치를 같은 예외로 | `어떤_틀린_암호도_화면이_암호문제로_알아본다`(Skip 해제), `FixedBugTests.틀린_암호는_타입으로_WrongPassword`, `EggAlzTests.암호_EGG…` |
| I2-11 | ZIP 암호 방식 선택 불가(AES 고정) | `CompressionOptions.Encryption`(`ZipEncryption.Aes256` 기본 / `ZipCrypto`), 옵션 창에 라디오 | `KnownGapTests.ZIP_암호를_ZipCrypto로_고를_수_있다`(Skip 해제), `ZipCrypto_왕복이_원본과_같다` |
| I0-3 | 암호 입력 | 옵션 창·풀 때 암호 창에 '암호 보이기', '8자 이상 권장' 안내(짧으면 주황), 두 번 입력 일치 확인 유지. 판정은 `Services/PasswordPolicy.cs`(순수 함수) | `PasswordPolicyTests`(창은 MANUAL_CHECKLIST 2절) |

암호 오류면 **이번 해제가 새로 만든 파일·폴더를 모두 지운다**(없던 대상 폴더 포함, 원래 있던 것은 안 건드림 — `PasswordTests.암호_오류면_대상폴더도_남지_않는다`·`…롤백은_원래_있던_것을_건드리지_않는다`).
손상·CRC 오류·취소는 예전대로 쓰다 만 그 파일만 지운다. 기록 테스트 변경: `PasswordTests.CurrentFailures`(엔진 예외 → 일집 예외, 한국어 문구), `기록_헤더암호_7z_목록_예외_타입`.

#### W1-C 1차 검증 지적으로 고친 것(2026-10-12)

| 무엇 | 예전 동작 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| 맞는 한글 암호로도 못 푸는 ZipCrypto ZIP(B3 빈틈) | 판정이 UTF-8 바이트부터 보고 통과하면 멈춤. 확인 바이트가 하나뿐인 경우(항목 하나짜리 ZIP, 또는 큰 ZIP 에서 한 파일만 골라 풀기·더블클릭 열기)에 CP949 로 쓴 암호의 UTF-8 바이트가 약 1/128 로 우연히 통과 → 쓰레기를 풀다 '암호가 맞지 않아요', 같은 압축은 영영 못 엶(반디집 `bz t` 는 통과). 검증 하네스: 항목 하나 478개 중 3개, 400개 ZIP 의 한 파일 골라 풀기 400번 중 6번 | `ZipPasswordProbe` 가 두 후보를 다 봄. 둘 다 맞으면 **고르지 않은 암호 항목**(최대 64+3개)으로 가리고, 그래도 못 가리면 ZipCrypto 는 CP949 먼저(반디집·알집·일집 방식) + UTF-8 을 대안(`AlternateMatch`)으로. 해제는 먼저 쓴 후보로 데이터·CRC 가 틀리면 만든 것을 모두 지우고 대안으로 처음부터 다시(`ExtractPlan.Alternate`, 같은 이름 그대로) | `ZipPasswordProbeTests.두_후보가_모두_통과하고_…`·`고른_항목이_모호하면_…`, `ZipEncryptionTests.두_후보가_모두_통과하는_한글암호_ZipCrypto도_…`(UTF-8·CP949 로 쓴 것 × 저장·Deflate, `RawZipCrypto` 로 결정적으로 만듦)·`일집이_만든_항목하나_…`·`여러_항목에서_한_파일만_골라도_…`·`두_후보가_모두_통과하는데_데이터가_깨지면_…` |
| 맞는 암호 + 깨진 데이터를 '틀린 암호'로 알림 | 암호 항목의 CRC·Deflate·AES 인증값 오류는 모두 `WrongPasswordException`(손상일 수도) → 화면이 3번까지 다시 묻고, 앞서 맞게 풀린 파일까지 모두 지움 | 판정 근거가 16비트 이상(틀린 암호가 우연히 통과할 확률 1/65536 이하 — ZipCrypto 확인 바이트 둘 이상, AES 확인값 하나, 골라 풀기면 고르지 않은 항목까지)이고 후보가 하나면 `Confirmed` → 그때의 데이터 오류는 `CorruptArchiveException`(CRC 는 `ChecksumMismatchException`): 다시 묻지 않고, 깨진 그 파일만 지우고 앞 파일은 남김. 근거가 약하면(항목 하나짜리 ZipCrypto 등) 예전대로 틀린 암호(손상일 수도) + 모두 지움 | `ZipEncryptionTests.AES_데이터가_깨지면_…`(기대 바뀜: 틀린 암호·폴더 없음 → 손상·앞 파일 남음), `맞는_암호인데_ZipCrypto_데이터가_깨지면_…`, `ZipPasswordProbeTests.근거가_16비트_이상이면_확인됨`. 항목 하나짜리 우연 통과는 그대로 `FixedBugTests.항목하나_ZipCrypto_우연통과_…` |
| 이모지 암호 + AES → 'ZipCrypto(반디집·알집과 호환)'를 고르면 경고 없이 UTF-8 ZipCrypto | 옵션 창이 방식을 바꾼 뒤 다시 판정하지 않음 | `PasswordPolicy.CheckCompat` 가 CP949 로 못 바꾸는 암호면 방식과 상관없이 `NotCp949Encodable`(이모지 경고 — ZipCrypto 로 바꾸라고 권하지 않음). 옵션 창은 방식을 바꾸면 다시 판정 | `PasswordPolicyTests.호환_판정`(AES + 이모지 2건 추가), 창은 MANUAL_CHECKLIST 2절 |
| 테스트 주석이 없는 클래스(`ZipPasswordCreateTests`)를 가리킴 | — | `ZipEncryptionTests` 로 고침(`RoundTripTests`) | — |

#### W1-C 에서 확인하지 못한 위험

- 'CP949 = 반디집 호환'은 한국어 Windows 의 반디집 7.46 기준. 알집·Windows 탐색기 내장 ZIP·맥/리눅스 도구의 한글 암호는 확인 못 함.
- `ZipPasswordProbe` 가 Unknown 을 내는 파일(.z01 분할·앞에 다른 것이 붙은 SFX·중앙 디렉터리 손상)은 엔진의 암호 확인 + CRC + 번역 + 롤백 장부만 남는다.
  이런 파일의 AES 항목은 예전처럼 SharpCompress 가 풀어 인증값(HMAC)을 보지 않는다.
- 판정은 ZipCrypto 64개·AES 3개 항목까지만 본다(그 뒤 AES 항목은 풀 때 확인값으로 거름). AES 확인값은 항목당 2바이트라 항목이 하나뿐이면 틀린 암호가 1/65536 로 통과 —
  그때는 (확인됨으로 보므로) 인증값 불일치가 '틀린 암호'가 아니라 '손상'으로 보인다. 같은 확률로 ZipCrypto 확인 바이트 둘(16비트)도 그렇다.
- '확인됨' 기준 16비트는 정한 값이다. 일집 ZipCrypto 는 데이터 설명자를 써서 항목당 약 7비트라 **항목 둘짜리는 14비트 = 확인 안 됨** →
  맞는 암호 + 깨진 데이터도 '틀린 암호(손상일 수도)'로 다시 묻고 모두 지운다(셋부터 손상으로 알림). 항목 하나짜리 ZipCrypto 는 늘 그렇다.
- 판정은 '같은 압축 파일은 한 암호'를 가정해 골라 풀 때 고르지 않은 항목을 근거로 쓴다. 항목마다 암호가 다른 ZIP 은 근거에서 빠질 뿐(후보를 버리지 않음).
- 확인 바이트·salt·PV 가 깨지면(암호 머리 12바이트 등) 맞는 암호도 판정에서 '틀린 암호'다 — 판정 단계에선 손상과 구분할 수 없다.
- ZIP 이름·머리(로컬·중앙 디렉터리)는 CRC 가 없어, 그 바이트가 깨지면 이름이 바뀐 채 풀리거나 항목이 빠질 수 있다(검증 하네스 비트 뒤집기 120번 중 몇 번, 암호와 무관한 예전 동작).
- `WinZipAes` 는 안쪽 방식 저장·Deflate·BZip2 만 직접 푼다. 그 밖(LZMA·PPMd 등)은 UTF-8 영문 암호면 SharpCompress 에 맡기고, CP949 암호면 '이 압축 방식은 아직 풀 수 없어요'.
- 엔진 항목과 중앙 디렉터리 항목의 짝은 순번(+크기·CRC)으로 맞춘다. 어긋나면 이름으로 한 번 더 찾고, 그래도 없으면 위와 같이 엔진/못 푸는 방식.
- EGG 한글 암호: EggDotNet 은 암호 글자의 아랫바이트만 써서(0.4.0 IL 확인) 알집이 만든 한글 암호 EGG 와 맞는지 모름(범위 밖).
- 화면 흐름 중 압축 옵션 창의 경고 버튼은 창 안의 일이라 자동 테스트가 없다(MANUAL_CHECKLIST 2절). 풀 때 암호 창 흐름(묻기·틀리면 다시·취소, 반디집 한글 AES·ZipCrypto)은
  W1-D 의 `IUserInteraction`(가짜 창)으로 VM 테스트가 생겼다(`CommandLineTests.extract_to_folder는_암호를_묻고_푼다`·`암호를_틀리면_다시_묻고_맞으면_풀린다`).

### W1-D 에서 고친 것(2026-10-12) — 0등급 화면 문제

| 사유 | 무엇 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| (I0-1 전제) | 일집이 꺼져 있을 때 우클릭 메뉴를 고르면 빈 창만 뜨고 아무 일도 안 함(켜져 있으면 파이프로 넘겨 동작) | App.xaml 의 `StartupUri` 가 OnStartup 이 끝난 뒤에 창을 만들어 `MainWindow` 가 늘 null 이었음 → StartupUri 를 지우고 OnStartup 이 단일 인스턴스 확인 뒤 창을 직접 만들어 인자를 넘김 | `AppStartupContractTests`(소스), 실제 실행은 MANUAL_CHECKLIST 1절 |
| I0-1 | 우클릭 '여기에 압축 풀기'가 실제로는 '이름 폴더에 풀기', 같은 이름 폴더가 있으면 말없이 합쳐 풂, 'x.tar.gz' 는 'x.tar' 폴더 | `--extract-here` = 압축 파일이 있는 폴더에 바로(메뉴 '여기에 풀기 (일집)', CLSID 그대로). 새 `--extract-to-folder` = '부모\이름' 폴더, 있으면 '이름 (2)'(`ArchiveNaming.FolderNameFor`·`UniqueFolder`, C++ 에도 같은 규칙). 새 C++ `ExtractToFolderCommand`(CLSID F5964E4F-…) 제목 '"보고서"\ 에 풀기 (일집)'/'각각 이름 폴더에 풀기 (일집)'(파일 이름 문자열만 봄), 매니페스트에 클래스·메뉴. 해석은 `Services/CommandLineParser.cs`(순수 함수). 여러 개를 골라 명령이 동시에 들어와도 VM 이 하나씩 처리 | `CommandLineParserTests`, `ArchiveNamingTests`, `CommandLineTests` 의 extract_here·extract_to_folder 들, `ShellExtensionContractTests`(DLL 제목 대조 포함) |
| I0-2 | 다른 프로그램이 열어 둔 파일(아웃룩 PST 등)이 하나라도 있으면 압축 전체가 실패, 하위 폴더 하나만 권한이 없어도 실패 | 원본을 FileShare.ReadWrite\|Delete 로 열고 공유 위반이면 150ms×3 재시도(`Services/SourceFileOpener.cs`). 그래도 못 여는 파일·못 들어가는 폴더·없어진 원본은 건너뛰고(`Services/SourceCollector.cs` — 담는 차례는 예전과 같음) `CompressAsync` 가 `Task<CompressResult>` 로 건너뛴 목록을 돌려줌. 화면은 '압축을 마쳤어요. 건너뛴 파일 N개:'(10줄까지) 안내, 결과는 지우지 않음. GZ·BZ2 는 `SourceUnreadableException`(빈 압축 파일을 안 만듦). 덤: 실패 정리는 이번에 만들거나 고친 파일만 지움, 원본 폴더 안에 저장하는 압축 자신은 담지 않음 | `SourceFileOpenerTests`, `CompressSkipTests` |
| I0-4 | 압축을 보다가 파일을 끌어 놓으면 보던 압축이 말없이 닫힘 | `Services/DropPolicy.cs`: 압축을 보는 중이면 [새로 압축할 목록 만들기]/[취소](압축 파일 하나면 맨 앞에 [이 압축 파일 열기]), 모으는 중에 압축 파일 하나면 [열기]/[목록에 넣기]/[취소]. 선택 창은 W1-C 의 `ChoiceDialog`(`IUserInteraction.Choose`). `AddToStaging` 은 더 이상 보던 압축을 닫지 않음(닫는 것은 고른 경우만 — `StartNewStaging`) | `DropPolicyTests` |
| I0-5 | 이름 열 정렬이 안 됨(템플릿 열에 SortMemberPath 없음), 열 폭 손잡이·정렬 화살표 없음(머리 템플릿이 Border+ContentPresenter 뿐) | 이름 열 `SortMemberPath="Name"`, `Sorting` 에서 기본 정렬을 막고 VM 이 `EntrySortComparer` 로 정렬(폴더 먼저, 폴더를 옮기거나 다른 압축을 열어도 유지, 기본 이름 오름차순 = 예전 순서). Styles.xaml 머리 템플릿에 `PART_LeftHeaderGripper`·`PART_RightHeaderGripper`(투명 8px Thumb)·1px 구분선·▲▼ 화살표(SortDirection 트리거, `FgMutedBrush`) | `SortingTests`(창 없이 템플릿 적용까지), 끌기·화살표 모양·다크 테마는 MANUAL_CHECKLIST 7절 |

기록 테스트 변경 없음. 바뀐 계약: `IArchiveService.CompressAsync` 반환형(`Task` → `Task<CompressResult>` — 기존 `await` 는 그대로 컴파일), `RoutingTests.FakeService` 한 줄,
`MainViewModel.OpenArchiveAsync` 가 `Task<bool>`(열었는지). `CommandLineTests.대상이_없으면_아무것도_안_한다` 에 `--extract-to-folder` 행 추가.

#### W1-D 1차 검증 지적으로 고친 것(2026-10-12)

| 무엇 | 예전 동작 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| (회귀, major) 파일 이름까지 암호가 걸린 7z 를 우클릭 '여기에 풀기'·'"이름"\ 에 풀기'로 못 풂 | W1-D 가 우클릭 풀기를 '열기에 성공해야 푼다'로 바꿨는데, 헤더 암호 7z 는 목록 읽기가 암호 없이 늘 실패(I1-7) → 오류 창('…암호를 넣어 주세요')만 뜨고 넣을 곳이 없음. 열기·끌어 놓기로도 못 풂 | I1-7 의 화면 쪽을 고침: `OpenArchiveAsync` 가 암호 예외면 암호 창을 띄우고(틀리면 다시, 취소하면 아무것도 안 바꿈) 다시 엶. 맞은 암호는 기억해(`_archivePassword`, W1-B 보관본과 같은 모양) 같은 압축을 풀 때 다시 묻지 않음. 다른 압축을 열거나 비우면 잊음 | `CommandLineTests.헤더암호_7z도_우클릭으로_암호를_한번_묻고_풀린다`(4행)·`…열기에서_틀리면_다시_묻고_취소하면_풀지_않는다`·`…열면_암호를_묻고_목록을_보이며_풀때_다시_묻지_않는다`·`다른_압축을_열면_기억한_암호를_잊는다` |
| 읽는 도중 잠긴 파일(LockFileEx 바이트 범위 잠금 — DB 등)이 있으면 압축 전체 실패 | 열기 단계의 공유 위반만 건너뜀 → 열고 나서 Read 에서 잠금 위반이면 '압축 실패' + 결과 삭제(TAR 도) | ① 열 때 첫 블록(80KB)을 읽어 봐서 앞부분 잠금은 열기에서 거름(`SourceFileOpener.TryOpenRead`) ② 그 뒤쪽 잠금은 `SourceFileOpener.Read`(TAR 는 `CancellableReadStream` 에 원본 경로)가 `SourceUnreadableException` 으로 알리고, 항목 머리를 이미 써서 그 항목만 뺄 수 없으므로 베이스 `RewriteWithoutUnreadable` 이 쓰다 만 것을 지우고 그 파일을 뺀 채 처음부터 다시 씀(파일마다 한 번). GZ·BZ2 는 엔진 문구 대신 쉬운 안내로 실패 | `CompressSkipTests.통째로_잠긴_파일은_…`·`읽는_도중_막힌_파일은_빼고_다시_써서_…`(zip·tar)·`단일파일_형식은_읽는_도중_막혀도_SourceUnreadable`, `SourceFileOpenerTests` 3개 |
| 원본을 모두 건너뛰면 빈 압축 파일을 남기고 '압축을 마쳤어요' | 22바이트 빈 zip(tar 는 1024바이트)이 생겨 '0개 항목'으로 열림(GZ·BZ2 는 오류로 알리고 안 만듦 — 처리가 다름) | ZIP·TAR 는 담을 첫 파일을 연 뒤에야 압축 파일을 만듦(`LazyArchiveOutput`). 담은 파일이 0개이고 건너뛴 것이 있으면(빈 폴더만 남아도) 만들지 않고 `NothingToCompressException`(덮어쓰려던 기존 파일도 그대로). 화면은 '읽을 수 있는 파일이 하나도 없어서 압축 파일을 만들지 않았어요. 건너뛴 파일 N개: …' 오류, 모으던 목록은 그대로. 원래 담을 것이 없던 경우(건너뛴 것 없음)는 예전처럼 빈 압축 | `CompressSkipTests.모두_못_읽으면_빈_압축을_만들지_않는다`(4행)·`빈_폴더만_남으면_…`·`건너뛴_것이_없으면_…`·`화면은_하나도_못_담으면_…` |
| 끌어 놓기 선택 창을 Drop 이벤트 안에서 모달로 띄움, 보던 것을 닫는 버튼이 기본(Enter) | 선택 창이 떠 있는 동안 끌기를 시작한 탐색기 창이 멈출 수 있음(OLE DoDragDrop 대기). Enter 한 번에 보던 압축이 닫히거나 모으던 목록이 비워짐 | `Window_Drop` 은 경로만 꺼내 두고 `await Dispatcher.Yield(Background)` 로 Drop 을 먼저 끝낸 뒤 처리. `IUserInteraction.Choose` 에 기본 버튼 순번, `DropDecision.DefaultIndex` = [취소], `ChoiceDialog` 는 그 버튼 하나만 IsDefault·처음 포커스(압축 옵션 창의 경고 창은 그대로 첫 버튼) | `DropPolicyTests.묻는_창의_기본_버튼은_늘_취소`·`선택_창은_정해_준_버튼_하나만_기본으로`(STA, 창은 안 띄움)·`끌어_놓기는_Drop_이벤트를_끝낸_뒤에_처리한다`(소스), VM 테스트 2개에 기본 버튼 확인. 탐색기가 안 멈추는지는 MANUAL_CHECKLIST 4절 |
| '"이름"\ 에 풀기'를 취소하거나 손상으로 멈추면 빈 이름 폴더가 남아 다음엔 번호가 밀림 | 큰 파일을 풀다 취소 → 빈 '큰 (2)\큰 자료\' 가 남고 다시 풀면 '큰 (3)' | `ExtractToFolderAsync` 가 `Task<bool>`(다 풀었는지). 다 못 풀었으면 `ArchiveNaming.RemoveFolderIfNoFiles` 가 그 새 폴더에 파일이 하나도 없을 때만 아래에서부터 지움(비었을 때만 지우는 방식이라 그새 생긴 파일은 안 건드림, 정션은 안 따라감). 파일이 남았으면 그대로(앞서 다 풀린 파일은 남긴다는 계약) | `CommandLineTests.이름_폴더_풀기를_도중에_취소하면_빈_폴더를_남기지_않는다`(진행 첫 보고에서 취소 — `FakeUserInteraction.CancelOnFirstBytes`)·`…손상으로_멈추면_파일이_없을_때만_폴더를_지운다`(2행) |
| 이름 정렬이 서수 비교라 '10.txt' 가 '2.txt' 앞에 | 탐색기·반디집과 차례가 다름(1월·10월, 견적서_2·견적서_10) | `EntrySortComparer.CompareNames` = `StrCmpLogicalW`(탐색기가 쓰는 함수, 대소문자 무시·숫자는 수 크기, 그 PC 의 언어 규칙) | `SortingTests.이름의_숫자는_수_크기로_비교한다`·`이름_비교는_탐색기_함수와_같다`·`압축할_목록의_숫자_이름도_수_크기_차례`, `기본은_폴더_먼저_이름순`(예전 이름 `기본_이름순이_예전_순서와_같다` — 샘플은 두 규칙 차례가 같아 둘 다 확인) |
| (W1-D 이전부터) 암호 창을 세 번 띄우고도 세 번째 암호는 써 보지 않고 '3회 모두 일치하지 않아' | `attempt < 3` 루프의 첫 시도가 암호 없음이라 실제로 써 보는 암호는 앞의 둘뿐 | 반디집처럼 [취소]할 때까지 다시 묻고 넣은 암호는 모두 써 봄('3회' 경고 문구 없앰). 열기 단계 암호 창도 같은 규칙 | `CommandLineTests.틀린_암호_뒤에_넣은_맞는_암호는_몇_번째든_풀린다`(틀림 2·4번)·`틀린_암호를_넣다가_취소하면_아무것도_안_남는다` |

바뀐 계약: `IUserInteraction.Choose(…, int defaultIndex = 0)`, `MainViewModel.ExtractToFolderAsync` 가 `Task<bool>`, ZIP·TAR `CompressAsync` 가 하나도 못 담으면 `NothingToCompressException`(새 예외).
기록 테스트 변경 없음. `SortingTests.이름은_대소문자_무시_서수_비교` → `이름은_대소문자를_가리지_않는다`(같은 데이터·기대).

#### W1-D 1차 검증에서 확인하지 못한 위험

- 끌어 놓기 때 탐색기 창이 멈추지 않는지, Enter 가 [취소]인지는 창에서만 보인다(MANUAL_CHECKLIST 4절 — 사용자 화면에 창을 띄우는 자동 조작은 하지 않았다).
  대화 상자 버튼 스타일(`DialogButton`)이 기본 버튼을 강조 색으로 칠하므로, 끌어 놓기 선택 창에서는 [취소]가 강조 색으로 보인다(어색하면 스타일 쪽 손질 필요).
- 읽는 도중 막힌 파일을 빼고 다시 쓸 때는 앞서 쓴 만큼을 다시 압축한다(큰 폴더면 그만큼 더 걸리고 진행 막대가 처음부터 다시 오른다). 잠긴 파일이 여럿이면 파일마다 한 번씩.
- 첫 블록 읽어 보기는 원본마다 처음 80KB 를 미리 읽는 것뿐이라(FileStream 버퍼에 남아 그대로 쓰임) 거의 느려지지 않는다. OneDrive '온라인 전용' 파일은 열 때 내려받기가 시작된다(예전에도 압축할 때 내려받음).
- 바이트 범위 잠금 시험은 같은 프로세스의 다른 핸들로 흉내 냈다(잠금은 핸들마다라 결과는 같다). 실제 DB 프로그램으로는 MANUAL_CHECKLIST 7절.
- 자연 정렬은 Windows 함수(StrCmpLogicalW)라 그 PC 의 언어 설정을 따른다(한국어 PC 의 탐색기와 같음). 테스트는 숫자·대소문자처럼 언어와 무관한 차례만 단언한다.
- ~~RAR 헤더 암호(파일 이름 암호화 RAR)는 픽스처가 없어 열기 암호 창을 확인하지 못했다~~ — 통합 점검에서 합성 RAR4·RAR5 로 확인(RAR4 는 틀린 암호가 영어 오류 창이던 것을 고침).
- 열 때 받은 암호를 기억하는 것은 '지금 열린 압축'만이다. 우클릭으로 여러 압축을 한꺼번에 풀면 압축마다 따로 묻는다(반디집의 '모든 압축 파일에 사용'은 없음).

#### W1-D 에서 확인하지 못한 위험

- **셸 메뉴·첫 실행은 실제로 눌러 보지 못했다** — 이 PC 에 셸 확장이 설치돼 있지 않고(MSIX 패키징·설치는 범위 밖), 사용자 화면에 창을 띄우는 자동 조작은 하지 않았다.
  C++ DLL 은 NuGet Windows SDK 로 빌드(종료 코드 0)하고 테스트 프로세스에서 불러 GetTitle 만 확인했다. MANUAL_CHECKLIST 1절.
- 예전 DLL(설치본)과 새 exe 를 섞으면 '여기에 압축 풀기' 메뉴가 이제 그 자리에 푼다(이름 폴더가 아니라). 재사용장치는 `--extract-to`(창 없이 풀기)만 써서 영향 없음.
- 쓰기 공유로 열린 파일(PST·로그)은 그 순간의 내용이 담겨 일관되지 않을 수 있다(반디집도 같음). 안내 문구에는 넣지 않았다.
- TAR 는 머리에 크기를 먼저 적으므로, 쓰기 공유로 연 파일이 압축 도중 줄어들면 예전처럼 '압축하는 도중에 바뀌었어요' 오류로 전체가 실패한다.
- ~~선택 창의 첫 버튼이 기본(Enter)이다~~ — 1차 검증에서 끌어 놓기 선택 창의 기본 버튼을 [취소]로 바꿈(압축 옵션 창의 경고 창은 그대로 첫 버튼).
- 명령줄 '경로만'(두 번째 실행으로 들어온 경로)도 끌어 놓기와 같은 규칙이라, 압축을 보는 중에 다른 압축을 '연결 프로그램 → 일집'으로 열면 선택 창이 뜬다(`--open` 은 묻지 않음).
- 정렬 화살표는 MainWindow 가 VM 상태대로 열의 SortDirection 을 맞춘다. 목록을 새로 채울 때 DataGrid 가 화살표를 지우는지는 창에서만 보인다(MANUAL_CHECKLIST 7절).

### 통합 점검에서 고친 것(2026-10-12 — W1-A·W1-C·W1-D 를 합친 뒤)

점검한 것: Release 빌드(경고 0) + 전체 테스트(Debug·Release·UTC), 건 사이 상호작용 — 라우팅을 거친 egg/alz·zip 한글 이름, 7z/zip/rar 암호가 같은 창 길로 가는지,
진행 막대가 실제로 움직이는지(서비스 → 솎아내기 → 화면), 재사용장치가 쓰는 창 없이 풀기. 픽스처가 없던 RAR 은 합성 RAR4·RAR5 를 만들어(반디집으로 작성기 검증) 확인했다.

| 무엇 | 예전 동작 | 어떻게 고쳤나 | 지키는 테스트 |
|---|---|---|---|
| **RAR4 암호 파일을 암호 없이·틀린 암호로 풀면 쓰레기를 '완료'로 씀** | SharpCompress 는 RAR4(RAR 3.x AES)를 암호 없이도 멈추지 않고 암호화된 바이트를 그대로 내보내고, 암호 항목의 CRC 를 보지 않음(RAR4 는 암호 확인값도 없음) → 암호 창이 안 뜨고, 틀린 암호도 '압축 해제 완료'. 예전 일집-dist(0.38)도 같음(창 없이 풀기 종료 0 + 쓰레기, 실측) | ① 베이스 `ExtractWithPlan`: 암호 항목(크기 &gt; 0)인데 암호를 안 줬으면 풀기 전에 `PasswordRequiredException`(형식과 상관없이 같은 길 — 크기 0 은 제외: SharpCompress 는 암호 없는 7z 의 빈 파일도 IsEncrypted = true 로 줌) ② `RarArchiveService.PrepareExtract`: RAR4 는 풀린 내용 CRC 확인(틀리면 '암호가 맞지 않아요(손상일 수도)' + 만든 것 모두 지움). RAR5 는 확인값이 있고 WinRAR 이 암호 항목 CRC 를 HMAC 으로 바꿔 적어 하지 않음 | `RarTests.RAR_암호가_없거나_틀리면_…`(rar4_enc), `FormatFlowTests.암호_압축은_…`(syn:rar4_enc), `ReuseDeviceContractTests.창없이_풀기가_실패하면_1이고_…` |
| RAR4 머리 암호(-hp)의 틀린 암호가 영어 오류 창 | 'Unknown Rar Header: 136'·'rar header crc mismatch'(형식 오류)라 화면이 암호 문제로 못 알아보고 다시 묻지 않음, 빈 대상 폴더도 남음 | `RarArchiveService.TranslateError`: 열기·목록 단계 실패인데 암호 없이 읽으면 '암호 없음'인 RAR(머리 암호)이면 7z 헤더 암호와 같은 예외(공용 `NeedsPasswordToList` — 7z 도 이것을 씀) | `RarTests.머리암호_RAR_…`, `RAR_암호가_없거나_틀리면_…`(rar4_hdr) |
| RAR 머리 암호 문구가 7z 와 다름 | RAR5 머리 암호도 '암호가 걸린 압축 파일이에요'(7z 는 '파일 이름까지 암호가 걸려 있어요') | 위와 같은 번역으로 `IsHeaderEncrypted` = true | `FormatFlowTests.암호_압축은_…`(syn:rar5_hdr·rar4_hdr) |
| 암호 없는 RAR 의 데이터 손상이 영어 문구 | 엔진 'file crc mismatch: 나.txt' 가 그대로 오류 창에 | `ChecksumMismatchException`(ZIP 과 같은 쉬운 문구, 깨진 그 파일만 지우고 앞 파일은 남김) | `RarTests.손상된_RAR은_CRC_오류로_…` |
| RAR5 유닉스 시각·gzip 머리 시각(.gz·.tar.gz)이 목록에 9시간 이르게 | 엔진이 UTC(Kind=Utc)로 주고 화면은 그대로 찍음(풀린 파일 시각은 맞았음) | 베이스 `AsLocalTime` — 목록 시각이 UTC 면 현지 시각으로 | `RarTests.RAR_목록의_수정시각은_현지시각`, `FormatFlowTests.목록의_수정시각은_형식과_상관없이_UTC로_오지_않는다` |

새로 고정한 것(문제는 없었음): 라우팅을 거친 화면의 한글 이름(ZIP CP949·UTF-8, 7z, TAR, EGG, ALZ, RAR4 유니코드·CP949, RAR5 — 목록·폴더 트리·풀린 이름),
ZIP·7z·EGG·RAR 암호 창 길(없음 → 틀림 → 맞음 — 반디집 한글 암호 ZIP 포함), 모든 해제에서 진행 창이 도중 보고를 받음, 실제 해제 진행률이 솎아내기를 지나 화면 쪽까지 감
(`ProgressTests.실제_해제의_진행률이_솎아내기를_지나_화면까지_간다`), 암호 ZIP(AES·한글 ZipCrypto)도 읽는 도중 막힌 파일만 빼고 다시 써서 같은 암호로 풀림(W1-C × W1-D),
재사용장치의 `--extract-to` 약속(`ReuseDeviceContractTests`). 기록 테스트 하나 추가: `RarTests.기록_RAR_항목_이름은_역슬래시_구분`.

#### 통합 점검에서 확인하지 못한 위험

- **진짜 WinRAR 산출물**: 합성 RAR 은 '저장' 방식뿐이다. 압축된 RAR(대부분의 실제 파일)에서 틀린 RAR4 암호가 어떻게 드러나는지(압축 해제기가 먼저 멈추거나 CRC 에서 걸림 —
  둘 다 '암호가 맞지 않아요' 길), RAR5 의 HMAC CRC·BLAKE2 확인값, 여러 조각(.part1.rar) RAR 은 확인하지 못했다(MANUAL_CHECKLIST 6절).
- RAR4 는 틀린 암호를 데이터 끝(CRC)에서야 알아서, 큰 파일이면 다 푼 뒤에 '암호가 맞지 않아요'가 뜬다(그동안 쓴 것은 지움). 7z 와 같은 성질.
- RAR5 암호 항목의 데이터 손상은 여전히 못 알아챈다(엔진이 암호 항목 CRC 를 안 보고, 일집도 HMAC CRC 를 계산하지 않음 — 확인값으로 틀린 암호는 걸러짐).
- 진행 창 자체(ProgressDialog.UpdateProgress → 막대 값)는 창을 띄워야 보여 자동 테스트가 없다(App 자원이 있어야 창을 만들 수 있음). MANUAL_CHECKLIST 5절.
- 창 없이 풀기의 실패 문구는 표준 오류에 CP949 로 나가 '—' 가 '?' 로 보인다(재사용장치 분류에는 영향 없음).

### 확인하지 못한 위험(테스트 없음)

- **솔리드 EGG 의 잘림**: 솔리드 EGG 는 항목별 CRC 가 없고 EggDotNet 이 풀린 묶음을 미리 잡아 둔 크기의 0 채움 버퍼에 담아서,
  잘린 솔리드 egg 는 크기 확인도 통과해 뒷부분이 0 으로 풀릴 수 있다(솔리드 EGG 작성기가 없어 미검증, MANUAL_CHECKLIST 6절).
- **EGG/ALZ 내용 확인의 진짜 알집 대조**: 크기·CRC 확인은 합성 파일(Store)과 EggDotNet 구조로만 맞췄다. 진짜 알집 파일에서
  멀쩡한데 '크기가 맞지 않아요'·'확인값이 맞지 않아요'가 나면 확인 규칙(예: 암호 항목의 CRC 칸)이 어긋난 것이다(MANUAL_CHECKLIST 6절).
- 잘린 .gz 의 **목록 크기**는 파일 끝 4바이트(잘린 자리의 쓰레기)에서 읽어 터무니없는 값일 수 있다(풀기는 오류로 막힘 — 화면 표시만).
  여러 멤버 .gz 의 목록 크기는 마지막 멤버의 크기다.

- **EGG 수정 시각 9시간 밀림 가능성**: EggDotNet 은 EGG 의 FILETIME(규격상 보통 UTC)을 `DateTimeKind.Unspecified` 로 돌려주고,
  서비스는 그것을 현지 시각으로 보고 `File.SetLastWriteTime` 한다. 합성 EGG(UTC FILETIME)는 한국 PC 에서 9시간 이르게 풀린다.
  알집이 FILETIME 을 UTC 로 쓰는지 현지 시각으로 쓰는지 몰라 테스트로 고정하지 않았다(MANUAL_CHECKLIST 6절).
- ZIP64(4 GB 초과·항목 65,535개 초과), 260자 넘는 긴 경로, 루트 상대(`/evil.txt`)·드라이브 상대(`C:evil.txt`) 경로 공격은 시험하지 않음
  (뒤의 둘은 방어가 깨지면 드라이브 루트나 알 수 없는 폴더에 쓰게 되어 넣지 않았다).
- **W1-B(7z.dll)에서 확인 못 한 것**:
  - 진짜 RAR(RAR4·RAR5·여러 조각 `.partN.rar`/`.rNN`·헤더 암호) — 픽스처가 없어 머리 바이트 읽기와 빠진 조각 이름 추정은 규격대로 만든 합성 바이트로만.
    여러 조각 RAR 은 경로로 열어(7z.dll 이 다음 조각을 직접 찾음) 취소가 파일 단위로만 먹는다.
  - CAB·WIM 실물, 7-Zip 이 만든 진짜 7z SFX(테스트는 7z 앞에 가짜 스텁을 붙인 합성 exe), 4 GB 넘는 7z.
  - 7z.dll 이 서명 없는 네이티브 DLL 이라 다른 PC 의 AppLocker·WDAC·백신이 막을 때 — 테스트는 '못 불러온 것'을 흉내만 냄
    (`SevenZipLibraryTests.막힌_PC에서도_…`: 7z 는 SharpCompress 로 대신, 나머지는 쉬운 안내).
  - 7z.dll 해제는 대상 폴더 안 숨김 임시 폴더(`.일집-임시-xxxxxxxx`)에 먼저 푼 뒤 옮긴다 — 비정상 종료 때 남은 것은 다음 해제 때 하루 지난 것만 지운다.
    옮길 때 공유 위반은 다시 해 보지만(최대 1.5초), **진짜 DLP(PCFILTER)가 감시하는 바탕 화면·OneDrive 대상에서는 시험하지 못했다**
    (테스트는 파일을 직접 잡아 흉내만 냄).
  - 7z 만들기 '최대'의 취소 지연: 느린 글자 데이터 300 MB 에서 7~15초(하네스 실측) — 큰 원본 계약 테스트는 '보통'만 둠(최대는 블록이 두 배라 병렬 테스트에서 흔들릴 수 있어).
  - 7z 만들기 메모리는 계약 테스트로 재지 않는다(같이 도는 테스트 때문에 흔들림) — 위 표의 숫자는 하네스 실측.
- **W1-B 2차 검증 지적에서 확인 못 한 것**:
  - 진짜 WinRAR·알집 자동 풀림 exe — 가짜 PE 스텁 + 합성(저장만) RAR/EGG/ALZ 로만. 실제 SFX 모듈이 압축 뒤에 다른 자료를 붙이거나
    덧붙은 영역 앞쪽에 다른 자료를 두면 못 찾을 수 있다(그때도 앞 64KB 훑기로 한 번 더 찾음).
  - 진짜(압축된) RAR 의 잘림·여러 조각 빠짐 — 합성 '저장' RAR 과 반디집 zipx·lzh 로만. 7z.dll 이 압축된 RAR 데이터 중간 잘림에도 같은 표시를 주는지는 실물 미확인.
  - RAR4·LZH 를 **머리 사이에서 정확히** 자른 것은 7z.dll 도 모른다(규격상 끝 표시가 선택 — 위 기록 테스트). 7z.dll 이 막힌 PC 의 끝 머리 확인은
    머리 암호 없는 RAR5 만(RAR4·머리 암호 RAR5 는 SharpCompress 가 알리는 것만).
  - 목록 열기는 너그러운 그대로 — 잘린 압축도 읽히는 데까지 목록이 보이고, 풀 때 알린다.
  - 7z.dll 경로의 '따로 꺼내기'(장치 이름·점만 있는 폴더)와 7z 만들기의 빈 폴더 '덧붙이기'를 진짜 DLP(PCFILTER) 대상 폴더에서 해 보지 못했다.
  - BSD 전문의 저작권 줄은 7-Zip `License.txt` 원문을 내려받지 못해 기억으로 적었다(Igor Pavlov 연도 생략) — 재배포 전에 원문과 대조(HANDOFF 보류).
  - 창 없이 풀기의 'ZIP 바탕 문서' 확장자 목록은 판단으로 정했다(docx·xlsx·pptx·hwpx·odt·epub·xps·vsdx 등) — 빠진 문서 형식은 예전처럼 풀린다.

### '근거' 테스트 (고쳐지면 실패하는 게 정상)

`BugEvidenceTests` 는 '지금 버그가 있다'를 그대로 고정합니다(재현 근거, Trait Kind=Record — 지금은 안 고친 버그가 없어 B3 원인 기록만).
버그를 고치면 이 테스트가 **실패하는 게 정상**이니, 그때는 해당 근거 테스트를 `FixedBugTests`(Trait 없음, 계약)로 옮겨
'올바른 기대'로 뒤집고, 같은 번호의 Skip 테스트에서 `Skip = …` 을 지우세요(같은 데이터·계약을 이미 지키는 테스트가 있으면 근거 테스트는 지우고 주석만 남김).
(`B3_원인_ZipCrypto…` 는 SharpCompress 를 직접 쓰므로, 엔진에서 SharpCompress 를 빼면 컴파일이 안 됩니다 — 그때 같이 지우면 됩니다.)

## 엔진을 바꾼 뒤 할 일

1. 먼저 계약만 돌립니다: `dotnet test ... --filter "Kind!=Record"`. **여기 실패는 회귀**입니다(새 엔진이 고쳐야 함).
2. 그다음 전체를 돌려 기록 테스트(`기록_…`, 근거 테스트)의 실패를 봅니다. 예외 타입·문구, 목록 모양, 방식 번호는 엔진이 바뀌면 달라지는 게 당연하니
   새 값으로 고칩니다. 단 **한꺼번에 새 엔진 출력으로 덮어쓰지 말고** 하나씩 '이 변화가 사용자에게 괜찮은가'를 보세요.
   새 엔진의 예외를 `MainViewModel.IsPasswordRelated` 가 못 알아보면(계약 테스트 `암호_실패는_화면이_암호문제로_알아본다` 실패) 암호 재입력 창이 안 뜹니다.
3. `RoutingTests` 의 등록·미등록 확장자 목록과 `AllSupportedExtensions` 를 새 엔진에 맞게 옮깁니다.
4. `KnownGapTests` 와 각 `[…(Skip = Known.…)]` 의 Skip 을 하나씩 지워 보고, 통과하면 Skip 을 지운 채로 둡니다.
5. EGG/ALZ: 7z.dll 계열은 egg/alz 를 못 풉니다. `EggAlzTests` 가 실패하면 EGG/ALZ 를 EggDotNet 서비스로 계속 보내도록 라우팅을 남기세요.
6. 시간대 스위치로 한 번 더: `$env:ILJIP_TEST_TZ = "UTC"` 로 전체 실행.

## 명령줄·화면 흐름 테스트(W1-D 에서 제안 1·2 를 반영)

- 해석은 순수 함수 `Services/CommandLineParser.cs`(`CommandLineParserTests`), '"이름"\ 에 풀기' 폴더 계산은 `ArchiveNaming.FolderNameFor`(`ArchiveNamingTests`).
- 창 띄우기(폴더 고르기·저장 위치·압축 옵션·암호 묻기·선택 창·오류/안내·진행 창)는 `Services/IUserInteraction.cs` 로 빠졌고 `MainViewModel(IUserInteraction?, ArchiveServiceLocator?)`
  생성자로 넣는다(기본은 실제 WPF 창 `Views/WpfUserInteraction.cs`). 테스트는 `TestSupport/FakeUserInteraction.cs` 를 넣어 `--extract-here`·`--extract-to-folder`·
  암호 묻기/틀리면 다시/취소·끌어 놓기 선택·압축 건너뜀 안내·부분 파일 정리를 창 없이 시험한다.
- 실제 WPF 창을 쓰는 VM(`new MainViewModel()`)으로 하는 테스트는 창이 안 뜨는 경로만이다. 그래도 화면 코드가 바뀌어 창으로 빠지면 테스트 호스트가
  영영 멈추지 않도록, VM 호출은 스레드 풀에서 60초 시간 제한을 걸고 부른다(`TestSupport/VmRunner.cs`).
- `--extract`(폴더 고르는 창)·`--compress` 의 실제 창, 셸 메뉴, 첫 실행은 MANUAL_CHECKLIST 로 사람이 확인한다.
- `--extract-to` 는 `App.OnStartup` 의 창 없이 풀기 경로를 실제 `Iljip.exe` 로 실행해 확인한다(종료 코드 0/1/2).
- 남은 제안: `App.IsHeadlessExtractCommand`·`RunHeadlessExtractAsync` 를 `internal static` 으로 두고 직접 시험(지금은 exe 를 띄워 확인).
- (W2-02) 화면에서 7z 만들기 흐름 시험은 `MainViewModelPasswordTests.화면에서_파일이름까지_암호화한_7z를_만들고_다시_연다` 로 되살림. 압축 옵션 창의 7Z 칸 규칙은
  `Views/CompressionOptionsRules`(순수 함수 — `CompressionOptionsRulesTests`), 창 모양은 MANUAL_CHECKLIST 10절.

## 그 밖에 고정해 둔 '현재 동작' (버그는 아니지만 엔진 교체 때 달라질 수 있는 것 — 대부분 기록 테스트)

- 목록 경로: zip·tar 는 폴더 끝에 `/`, 7z 는 없음. 화면은 둘 다 처리하므로 테스트는 둘 다 허용.
- 일집 ZIP·TAR 목록에는 빈 폴더만 폴더 항목이 있음(반디집 ZIP 은 모든 폴더 항목). (TAR 는 W1-A 전엔 폴더 항목이 하나도 없었음)
- ZIP '저장(Store)' 수준도 방식 번호는 8(Deflate, 압축 안 한 블록) — 반디집 `-l:0` 은 방식 0.
- 암호 없는 일집 ZIP 도 한글 이름에 EFS(UTF-8) 플래그를 켬(계약은 '한국어 탐색기 방식으로 읽어도 이름이 맞음').
- 7z 항목의 압축 크기는 모두 0(SharpCompress 도, W1-B 의 7z.dll 도 항목별 값을 주지 않음) — 화면 압축률이 의미 없음. zip·tar 는 항목별 값.
- 7z.dll 로 여는 형식은 시각이 없는 항목(xz·ISO 폴더)의 수정 시각을 '모름'(null)으로 둔다. ZIP 은 7z.dll 이 플래그 없는 UTF-8 이름을 깨뜨려
  계속 SharpCompress 로 읽는다(`SevenZipEngineTests.기록_7z_dll은_플래그없는_UTF8_ZIP_이름을_깨뜨린다`).
- 순수 .bz2 목록의 크기는 0(풀기 전엔 모름), 이름은 파일명에서 `.bz2` 를 뗀 것. FNAME 없는 .gz 도 같은 규칙(`.gz` 를 뗀 것, 크기는 gzip 끝의 원래 크기). 뗀 이름이 비거나 점·공백뿐이면 '압축풀림'.
- 해제 실패 시 '파일'은 안 남지만 먼저 만든 '폴더'는 남을 수 있음(암호 오류만은 W1-C 부터 이번에 만든 폴더까지 지움). 단 화면의 '"이름"\ 에 풀기'는
  W1-D 1차 검증부터 취소·실패로 그 이름 폴더에 파일이 하나도 안 남았으면 그 폴더(빈 하위 폴더 포함)를 지운다(`ArchiveNaming.RemoveFolderIfNoFiles`).
- 7z(W2-02) 는 모든 폴더가 항목(일집 ZIP·TAR 와 다름 — 7-Zip·반디집 7z 와 같음). 폴더가 있으면 폴더만 든 7z 를 먼저 만들고 파일을 덧붙인다(파일만 고르면 바로).
  쓰기는 대상 옆 숨김 임시 파일 `.~이름.8글자.tmp`(덧붙이기는 `.일집-임시-…` 폴더도)에 하고 다 맞을 때만 바꿔 넣는다 — 실패·취소면 서비스가 지운다.
- 압축 도중 취소 시 서비스는 쓰다 만 ZIP·TAR 를 지우지 않음 — 지우는 건 `MainViewModel.CompressAsync` 의 몫. (W1-D 1차 검증) ZIP·TAR 압축 파일은 담을 첫 파일을 연 뒤에 만들고,
  읽는 도중 막힌 파일이 있어 처음부터 다시 쓸 때는 서비스가 쓰다 만 것을 지운다.
- TAR 압축 도중 원본 파일이 줄어들면(머리에 적은 크기와 실제 읽은 양이 다름) `IOException`('…파일이 압축하는 도중에 바뀌었어요') — 조용히 깨진 tar 를 만들지 않음.
- 이미 취소된 토큰이면 대상 폴더도 만들지 않음.
- 선택 해제는 대소문자·`\`·앞뒤 `/` 차이를 무시하고, 이름 앞부분만 같은 것(`하위` vs `하위폴더`)은 고르지 않음.
- 경로 공격은 `InvalidDataException("안전하지 않은 경로: …")` 로 멈춤(앞 항목은 풀려 있음). Windows 특수 이름은 예외 없이 대상 폴더 안에 바꾼 이름으로 풀림
  (W1-B 2차부터 `a.txt:evil` → `a.txt_evil`, `con.txt` → `_con.txt`, `...` → `.._` — `EntryPaths`, 끝 점·공백은 Windows 가 떼는 대로). tar 링크 항목은 빈 일반 파일로 풀리고 그 아래 항목은 `IOException`.
- 명령줄 키워드는 대소문자를 가림(`--OPEN` 은 경로로 취급되는데 결과적으로 열림). 창 없이 풀기의 `--extract-to` 는 대소문자 무시.
- (W1-D) 키워드 명령의 인자가 모자라면 아무것도 안 함(예전엔 `--extract-to <있는 압축>` 처럼 폴더가 빠지면 경로 목록으로 처리돼 열렸음).
- (W1-D) 목록 정렬은 압축할 목록(모으는 중)에도 적용된다(보이는 순서만 — 압축 대기 원본 목록 `Entries` 는 넣은 차례 그대로).
