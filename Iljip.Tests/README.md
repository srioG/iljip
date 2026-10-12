# Iljip.Tests — 일집 계약(회귀) 테스트

압축 엔진을 바꾸기 **전에** 일집의 '지금 동작'을 고정해 두는 테스트 묶음입니다(사전점검 ②).
엔진을 바꾼 뒤 이 테스트를 돌리면 무엇이 그대로이고 무엇이 달라졌는지 바로 보입니다.

- 대상: `Iljip/Services/IArchiveService.cs` 와 그 구현체(ZIP·7z·TAR·GZ·BZ2·RAR·EGG/ALZ), `ArchiveServiceLocator`,
  `KoreanFileNameDecoder`, `ThrottledArchiveProgress`, `MainViewModel` 의 명령줄 해석 일부, `Iljip.exe --extract-to`(창 없이 풀기).
  - **RAR 은 성공 경로(맞게 풀리는지)를 자동으로 시험하지 못합니다** — 반디집·이 PC 모두 RAR 을 만들 수 없어 픽스처가 없습니다.
    지금은 '깨진 .rar 거절'·라우팅만 자동, 해제는 `MANUAL_CHECKLIST.md` 6절로 사람이 확인합니다.
    rar.exe 가 있는 PC 에서 **합성 데이터**로 만든 RAR 픽스처를 받으면 `Fixtures/` 와 manifest 에 추가하세요.
  - EGG/ALZ 는 테스트 안에서 최소 작성기(`TestSupport/RawArchives.cs` 의 `RawEgg`·`RawAlz`)로 만든 합성 파일로 시험합니다
    (`Contract/EggAlzTests.cs`). 진짜 알집 산출물로는 확인하지 못했으니 MANUAL_CHECKLIST 6절도 같이 보세요.
- 처음(사전점검 ②)에는 본체 코드를 고치지 않고 버그를 'Skip 목록'에 보고만 했습니다. 이후 **W1-A(2026-10-12)** 에서 SharpCompress 를
  0.38.0 → 0.50.5 로 올리고 B1·B2·B6·B7·B8·B9·B10·TAR-빈폴더를 고쳤습니다 — 근거 테스트는 `Contract/FixedBugTests.cs` 로 옮겨
  기대를 뒤집었고(다시 생기면 실패), 같은 번호의 Skip 은 지웠습니다.
  이어서 **W1-A 1차 검증 지적**(같은 날)으로 잘린·손상 .gz/.egg/.alz 를 '완료'로 끝내던 문제, 끝 구분자 없는 EGG/ALZ 폴더 항목,
  ALZ 한자·기호 이름·잘린 ALZ 이름, 점뿐인 .gz 이름, 서명 뒤가 쓰레기인 RAR5, 같은 폴더 동시 해제 충돌, 분할 EGG 형제 파일 전부 열기를 고쳤습니다
  (아래 '고친 버그' 표 아래쪽).
  **W1-C(같은 날)** 에서 암호·무결성 버그 B3(반디집 한글 암호)·B4(ZIP CRC 미검사)·B5(틀린 암호를 화면이 못 알아봄)와 빈틈 I2-11(ZipCrypto 선택)을
  고쳤습니다(아래 'W1-C 에서 고친 것', 그 1차 검증 지적도 같은 절). 이제 안 고친 버그(Bn Skip)는 없습니다. ※ W1-B(7z.dll) 는 검증에서 되돌려져 이 판에 없습니다 —
  I1-7·I2-1·I2-2·I2-9 는 그대로 Skip/수동입니다.
  **W1-D(같은 날)** 에서 0등급 화면 문제 I0-1(우클릭 '여기에 풀기'·'"이름"\ 에 풀기' 분리 + 첫 실행 인자 버그)·I0-2(사용 중인 파일)·I0-4(끌어 놓기 선택 창)·
  I0-5(정렬·열 폭)를 고쳤습니다. 화면의 창 띄우기를 `IUserInteraction` 으로 빼서(W1-B 보관본과 같은 모양) 가짜 창으로 VM 흐름을 시험합니다(아래 'W1-D 에서 고친 것').
  **W1-D 1차 검증 지적(같은 날)** 으로 헤더 암호 7z 를 우클릭으로 못 풀던 회귀(I1-7 화면 쪽까지 고침), 읽는 도중 잠긴 파일, 빈 압축, 끌어 놓기 선택 창, 빈 이름 폴더,
  자연 정렬, 세 번째 암호 미시도를 고쳤습니다(아래 'W1-D 1차 검증 지적으로 고친 것').

## 실행 방법

```powershell
dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj
```

- 솔루션(`Iljip.sln`) 전체를 `dotnet test` 로 돌리면 C++ 셸 확장 때문에 실패하므로 **csproj 를 직접** 지정합니다.
- 처음 한 번은 NuGet(xunit 등)을 내려받습니다. 그다음부터는 빌드 포함 약 1분, 테스트만 30~60초.
- 결과(2026-10-12 W1-D 1차 검증 후): `통과!  - 실패: 0, 통과: 759, 건너뜀: 6, 전체: 765` — **건너뜀(Skip)은 정상**입니다(아래 설명).
  (W1-A 전: 통과 394·건너뜀 20·전체 414 → W1-A: 통과 430·건너뜀 10·전체 440 → W1-A 1차 검증: 통과 478·건너뜀 10·전체 488 → W1-C: 통과 578·건너뜀 6·전체 584
  → W1-D: 통과 714·건너뜀 6·전체 720)
  반디집 콘솔(`C:\Program Files\Bandizip\bz.exe`)이 없는 PC 에서는 `BandizipCliTests` 3개가, 로컬 SFX 픽스처가 없으면 SFX 테스트가 더 건너뜁니다.
  빌드된 셸 확장 DLL(`D:\Python\일집-build\Release\x64\Iljip.ShellExtension\Iljip.ShellExtension.dll`)이 없거나 C++ 소스보다 오래되면
  `ShellExtensionContractTests.DLL_메뉴_제목이_CSharp_이름_규칙과_같다` 1개가 더 건너뜁니다(빌드 명령은 `HANDOFF.md` 'C++ 셸 확장 DLL 만 빌드').
- 느린 축: 파일명 보이는 암호 7z(py7zr, 키 유도 2^19회)를 쓰는 테스트, 32 MiB 취소 테스트, 실제 `Iljip.exe`·반디집 `bz.exe` 를 띄우는 테스트(각 1~10초).
- 특정 묶음만: `dotnet test ... --filter "FullyQualifiedName~PasswordTests"`
- **계약만**(엔진과 무관하게 지켜야 할 것): `dotnet test ... --filter "Kind!=Record"` (640개, 2026-10-12 W1-D 1차 검증 후) — 아래 '계약 테스트와 기록 테스트'.
- **시간대 독립 확인**: 환경 변수 `ILJIP_TEST_TZ` 에 Windows 시간대 ID 를 주면 테스트 프로세스 안에서만 현지 시간대를 바꿉니다
  (시스템 설정은 안 건드림). 2026-10-12 `UTC`·`Pacific Standard Time` 둘 다 전체 통과 확인(W1-A 후 `UTC` 다시 통과).
  ```powershell
  $env:ILJIP_TEST_TZ = "UTC"; dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj; Remove-Item Env:ILJIP_TEST_TZ
  ```
- 빌드 산출물은 `Directory.Build.props`(git 제외) 때문에 `D:\Python\일집-build\dotnet\Iljip.Tests\` 로 갑니다.
- 임시 파일은 `%TEMP%\IljipTests\<테스트별 고유 폴더>` 에 만들고 테스트가 끝나면 지웁니다.

## 폴더 구조

| 경로 | 내용 |
|---|---|
| `Contract/BandizipFixtureTests.cs` | [1] 반디집 픽스처를 반디집과 같게 읽고 푸는지(목록·sha256·수정 시각·압축 크기 범위) |
| `Contract/RoundTripTests.cs` | [2] 일집이 만든 ZIP(수준별·AES)·TAR·GZ·BZ2 왕복 + 표준 라이브러리·'한국어 탐색기 방식 이름' 교차 확인 |
| `Contract/PasswordTests.cs` | [3] 틀린 암호·암호 없음 → 예외·파일 안 남음·화면이 '암호 문제'로 알아봄 |
| `Contract/ZipSlipTests.cs` | [4] `../`·`..\`·절대경로·**형제 접두사(`../dest_evil.txt`)**·Windows 특수 이름·tar 링크 공격 방어(zip·tar·egg) |
| `Contract/CancellationTests.cs` | [5] 32 MiB 해제·압축 도중 취소 → 부분 파일 정리 |
| `Contract/CorruptArchiveTests.cs` | [6] 잘린·망가진 zip/7z/tar/bz2/rar/egg, **쓰는 도중 깨지는 zip**, **잘리거나 확인값(CRC·크기)이 틀린 .gz·.tar.gz**, 서명 뒤가 쓰레기인 rar → 예외·오염 없음(경로별 sha 비교). 여러 멤버 .gz·끝에 0 이 붙은 .gz 는 끝까지 풀림 |
| `Contract/ExtractEntriesTests.cs` | [7] 선택 해제(파일·폴더·표기 차이) |
| `Contract/ProgressTests.cs` | [8] 진행률 0~100%·도중 보고·완료 보고(TAR 는 큰 파일 안에서도) |
| `Contract/ThrottledArchiveProgressTests.cs` | 진행 창으로 가는 진행률 솎아내기 규칙(첫 중간 보고 통과·간격·완료 보고) |
| `Contract/RoutingTests.cs` | [9] 확장자 → 서비스 라우팅 |
| `Contract/KoreanFileNameDecoderTests.cs` | [10] CP949·UTF-8·ASCII·CP437 이름 판별 |
| `Contract/KnownGapTests.cs` | [11] 알려진 빈틈(전부 Skip, 본문은 '고쳐지면 맞아야 할 기대') |
| `Contract/CommandLineTests.cs` | [12] 명령줄 처리(60초 시간 제한) — **가짜 창으로 `--extract-here`(그 자리에)·`--extract-to-folder`(이름 폴더, 다시 하면 '이름 (2)', 암호 묻기·틀리면 다시·취소, 동시에 여러 개)** + 실제 `Iljip.exe --extract-to`. (1차 검증) 헤더 암호 7z 를 열기·우클릭 풀기에서 암호를 한 번 묻고 풀기·틀리면 다시·취소·다른 압축을 열면 잊기, 암호 창은 취소할 때까지(몇 번째 맞는 암호든 풀림), 이름 폴더 풀기를 취소·손상으로 멈추면 파일이 없을 때만 그 폴더를 지움 |
| `Contract/CommandLineParserTests.cs` | (W1-D) 명령줄 해석 순수 함수 — 키워드·인자 개수·대문자는 경로·'--extract-to-folder' 와 '--extract-to' 안 섞임 |
| `Contract/ArchiveNamingTests.cs` | (W1-D) '"이름"\ 에 풀기' 폴더 이름 표(.tar.gz·.7z.001·.part1.rar·.vol1.egg·'.zip' …)와 '이름 (2)'·'(3)' 새 폴더 |
| `Contract/AppStartupContractTests.cs` | (W1-D) 첫 실행 인자 버그 — App.xaml 에 StartupUri 없음, OnStartup 이 창을 직접 만들고 인자를 넘김(소스 확인) |
| `Contract/ShellExtensionContractTests.cs` | (W1-D) C++ 셸 확장·매니페스트·C# 의 약속: CLSID 5개 일치, 메뉴 Verb, '여기에 풀기 (일집)' 제목, C++ 이 넘기는 키워드를 C# 이 알아봄. **빌드된 DLL 이 있으면 실제 GetTitle 이 C# 이름 규칙과 같은지**(Invoke 는 안 부름) |
| `Contract/SourceFileOpenerTests.cs` | (W1-D) 원본 열기 — 쓰기 공유로 열린 파일은 읽힘, 공유를 막으면 '다른 프로그램이 쓰고 있어요', 재시도, 없는 파일. (1차 검증) 앞부분이 잠긴(LockFileEx) 파일은 열기에서 걸러짐, 첫 블록을 읽어 봐도 처음부터 읽힘, 읽는 도중 막히면 `SourceUnreadableException` |
| `Contract/CompressSkipTests.cs` | (W1-D) 잠긴 파일·못 들어가는 하위 폴더·없어진 원본은 건너뛰고 나머지는 그대로(zip·tar), GZ·BZ2 는 `SourceUnreadableException`, 담는 차례는 예전과 같음, 화면 안내('건너뛴 파일 N개')·결과 보존·부분 파일 정리. (1차 검증) 통째로·뒤쪽만 잠긴 파일(바이트 범위 잠금)도 건너뜀(뒤쪽은 빼고 다시 씀), 하나도 못 담으면 빈 zip·tar 를 만들지 않고 `NothingToCompressException`(기존 파일 그대로, 화면은 오류 안내) |
| `Contract/DropPolicyTests.cs` | (W1-D) 끌어 놓기 규칙 표 + 가짜 선택 창으로 [취소]·[새로 압축할 목록]·[열기]·[목록에 넣기]. (1차 검증) 기본 버튼(Enter)은 늘 [취소], `ChoiceDialog` 가 정한 버튼 하나만 IsDefault(창은 만들기만), Drop 이벤트를 끝낸 뒤(Dispatcher.Yield) 처리(소스) |
| `Contract/SortingTests.cs` | (W1-D) 정렬 비교기(폴더 먼저·크기·날짜·같은 값은 이름), VM 정렬(두 번 누르면 내림, 폴더를 옮겨도 유지, 기본은 이름 오름차순), XAML 계약 + 열 머리 템플릿을 메모리에서 적용해 손잡이·화살표 확인. (1차 검증) 이름은 탐색기와 같은 자연 정렬(`2.txt` → `10.txt`, `1월` → `2월` → `10월`) |
| `Contract/EggAlzTests.cs` | [13] EGG/ALZ 성공 경로(목록·해제·충돌·선택·취소·진행률·크기 0 항목·ALZ 한글·한자·기호 이름·끝 구분자 없는 폴더 항목) + **잘림·CRC 불일치 → 예외·짧은 파일 안 남김**, 분할 조각 후보 — 합성 EGG/ALZ |
| `Contract/AlzNameScannerTests.cs` | ALZ 머리 원본 바이트 스캐너(B10 우회·폴더 속성): 맞게 읽기, 잘리면 온전한 항목까지만, 서명 깨짐·항목 수 불일치·분할이면 예외 없이 포기, 이름 바이트 풀기(UTF-8→CP949→CP437) |
| `Contract/CollisionTests.cs` | 같은 이름이 있으면 덮지 않고 `이름 (1).txt` 로 비켜 쓰기, **같은 폴더에 동시에 여러 번 풀어도** 모두 다른 이름으로(gz·zip·egg) |
| `Contract/FileLockTests.cs` | 열기 실패 후 원본 파일을 잠그지 않는지(B8 고침) + 분할 EGG 조각들도 바로 지울 수 있는지 |
| `Contract/BugEvidenceTests.cs` | 버그 '지금 이렇다' 근거(고쳐지면 실패하는 게 정상). 지금은 안 고친 버그가 없어 B3 원인 사실 기록 2개만(반디집 한글 암호 = CP949, 일집 AES = UTF-8) |
| `Contract/FixedBugTests.cs` | 고친 버그 B1·B2(점뿐인 이름 포함)·B6·B7·B9, **B3·B4·B5(W1-C)** — 다시 생기면 안 됨(예전 근거 테스트를 올바른 기대로 뒤집은 것, 계약) |
| `Contract/ZipPasswordProbeTests.cs` | (W1-C) ZIP 암호 판정 — 반디집 한글 암호 = CP949, 일집 AES = UTF-8, 첫 항목만 통과하는 틀린 암호 = None, 판정 못 하는 모양(잘림·분할·SFX·쓰레기) = 예외 없이 Unknown, 두 후보가 다 통과할 때 가려내기·대안, '확인됨'(근거 16비트) |
| `Contract/ZipEncryptionTests.cs` | (W1-C) ZipCrypto·AES 만들기(영문·한글·이모지 암호) 왕복, 반디집 한글 AES 선택 해제, **맞는 암호 + 깨진 AES·ZipCrypto 데이터는 손상으로 알리고 앞 파일은 남김**, 잘린 AES ZIP, 두 후보가 다 통과하는 한글 ZipCrypto 도 풀림(`TestSupport/RawArchives.cs` 의 `RawZipCrypto` 로 결정적으로 만듦) |
| `Contract/PasswordPolicyTests.cs` | (W1-C) 압축 옵션 창의 암호 판정(두 칸 일치·8자 권장, 한글 암호 호환 경고 표) |
| `Contract/BandizipCliTests.cs` | (W1-C) 일집이 한글 암호로 만든 ZIP 을 반디집 콘솔 `bz.exe t`(시험만)로 풀어 봄 — ZipCrypto 는 풀림(계약), AES 는 못 풂(기록). bz.exe 가 없으면 건너뜀 |
| `Contract/FixtureCoverageTests.cs` | 매니페스트의 모든 픽스처가 어느 테스트엔가 쓰이는지, 파일이 다 있는지, 시간대 스위치가 먹었는지 |
| `TestSupport/` | 임시 폴더, 매니페스트 읽기, 비교 도우미, 최소 ZIP/TAR/GZIP/EGG(분할·**ZipCrypto 암호** 포함)/ALZ 작성기, ZipCrypto 검사 계산기·암호화기, 반디집 콘솔 실행기(`BandizipCli`), (W1-D) 가짜 창 `FakeUserInteraction`·VM 시간 제한 실행 `VmRunner`·STA 스레드 `Sta`·저장소 소스 읽기 `RepoFiles` |
| `Fixtures/` | 픽스처(아래) |
| `MANUAL_CHECKLIST.md` | 버전 바꿀 때 사람이 10분 안팎에 확인하는 점검표 |

## 계약 테스트와 기록 테스트

같은 동작에 대해 두 종류를 **다른 테스트로 나눠** 두었습니다. 한 테스트 안에서 엔진 고유 값(예외 타입 등)을 먼저 단언하면,
엔진을 바꾼 직후 그 단언이 먼저 실패해 뒤의 '파일이 남지 않는다'·'대상 폴더 밖에 안 쓴다' 같은 핵심 계약 결과가 가려지기 때문입니다.

- **계약 테스트**(이름에 `기록_` 없음, Trait 없음): 엔진과 무관하게 지켜야 할 것 — 해제 내용 sha256, 한글 이름, zip-slip 방어(대상 폴더 밖 무사),
  예외가 나면 대상 폴더 오염 없음, 취소 시 부분 파일 정리, 동명 파일 비켜 쓰기, 진행률 끝 '완료', `IsPasswordRelated` 판정.
  목록은 '파일 항목은 정확히, 폴더 항목은 있어도 없어도(있다면 기대 목록 안의 것)' 만 봅니다(`TreeAssert.ListFilesEqual`).
- **기록 테스트**(이름이 `기록_` 으로 시작하거나 근거 테스트, `[Trait("Kind","Record")]`, 125개): 지금 엔진의 예외 타입·문구,
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
| I1-7 | 헤더(파일명) 암호 7z/RAR 를 열 때 암호를 안 물음 | **7z 는 W1-D 1차 검증에서 고침(Skip 아님)** — `MainViewModel.OpenArchiveAsync` 가 암호 예외면 암호를 묻고 다시 열며, 받은 암호로 이어서 풂(`CommandLineTests.헤더암호_7z…`). 서비스 쪽(암호 주면 목록 OK, 없으면 암호 예외)은 PasswordTests 가 고정. RAR 헤더 암호는 픽스처가 없어 여전히 미검증(같은 화면 길). ★ `헤더암호_7z_전체_해제가_반디집과_같다` 는 B1 테스트이지 I1-7 이 아님 |
| I2-1 | 7z 만들기 | `NotSupportedException` |
| I2-2 | `.7z.001`·`.zip.001` | 확장자 미등록(Resolve=null). SharpCompress 로 억지로 열면 둘 다 정상 — 등록만 하면 될 가능성 큼. `.zip`+`.z01` 분할은 **이미 됨** |
| I2-9 | zipx·tgz·tar.gz·txz·xz·tbz2·iso·lzh·SFX exe | 미등록(.tar.gz 만 마지막 확장자 .gz 로 GZ 처리기). 기대값은 '반디집 7.46 과 같게': zipx·tgz·tar.gz·iso·lzh 는 안 내용을 바로, **.tar.xz·.tbz2 는 안쪽 `.tar` 하나만**(반디집 실측, 7z.dll 기본도 같음). tar 까지 한 번에 펼치는 건 '반디집보다 나은' 선택 목표라 시험하지 않음. 지금(SharpCompress 0.50.5): **.tar.gz 는 GZ 처리기가 안쪽 `이름.tar` 하나로 보여 주고 꺼냄**(기록 테스트 `기록_tar_gz는_안쪽_tar_하나로_보이고_풀린다`). 억지로 `ArchiveFactory.OpenArchive` 하면 zipx(일반)·SFX 는 정상, zipx(AES+XZ)는 목록은 되지만 해제가 'Specified method is not supported', tgz·tar.gz·xz·txz·tbz2·lzh 는 'Cannot determine compressed stream type'(tgz·tar.gz 는 `GZipArchive.OpenArchive` 로 직접 열면 이름 빈 항목 하나), iso 는 **항목 0개로 조용히 성공** |
| I2-10 | 맥 NFD 이름 | 목록·풀린 이름이 자모 분리 그대로(내용은 정확) |

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
- RAR 헤더 암호(파일 이름 암호화 RAR)는 픽스처가 없어 열기 암호 창을 확인하지 못했다(같은 화면 길 — 엔진이 암호 예외를 내면 묻는다).
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

## 그 밖에 고정해 둔 '현재 동작' (버그는 아니지만 엔진 교체 때 달라질 수 있는 것 — 대부분 기록 테스트)

- 목록 경로: zip·tar 는 폴더 끝에 `/`, 7z 는 없음. 화면은 둘 다 처리하므로 테스트는 둘 다 허용.
- 일집 ZIP·TAR 목록에는 빈 폴더만 폴더 항목이 있음(반디집 ZIP 은 모든 폴더 항목). (TAR 는 W1-A 전엔 폴더 항목이 하나도 없었음)
- ZIP '저장(Store)' 수준도 방식 번호는 8(Deflate, 압축 안 한 블록) — 반디집 `-l:0` 은 방식 0.
- 암호 없는 일집 ZIP 도 한글 이름에 EFS(UTF-8) 플래그를 켬(계약은 '한국어 탐색기 방식으로 읽어도 이름이 맞음').
- 7z 항목의 압축 크기는 모두 0(SharpCompress) — 화면 압축률이 의미 없음. zip·tar 는 항목별 값.
- 순수 .bz2 목록의 크기는 0(풀기 전엔 모름), 이름은 파일명에서 `.bz2` 를 뗀 것. FNAME 없는 .gz 도 같은 규칙(`.gz` 를 뗀 것, 크기는 gzip 끝의 원래 크기). 뗀 이름이 비거나 점·공백뿐이면 '압축풀림'.
- 해제 실패 시 '파일'은 안 남지만 먼저 만든 '폴더'는 남을 수 있음(암호 오류만은 W1-C 부터 이번에 만든 폴더까지 지움). 단 화면의 '"이름"\ 에 풀기'는
  W1-D 1차 검증부터 취소·실패로 그 이름 폴더에 파일이 하나도 안 남았으면 그 폴더(빈 하위 폴더 포함)를 지운다(`ArchiveNaming.RemoveFolderIfNoFiles`).
- 압축 도중 취소 시 서비스는 쓰다 만 ZIP·TAR 를 지우지 않음 — 지우는 건 `MainViewModel.CompressAsync` 의 몫. (W1-D 1차 검증) ZIP·TAR 압축 파일은 담을 첫 파일을 연 뒤에 만들고,
  읽는 도중 막힌 파일이 있어 처음부터 다시 쓸 때는 서비스가 쓰다 만 것을 지운다.
- TAR 압축 도중 원본 파일이 줄어들면(머리에 적은 크기와 실제 읽은 양이 다름) `IOException`('…파일이 압축하는 도중에 바뀌었어요') — 조용히 깨진 tar 를 만들지 않음.
- 이미 취소된 토큰이면 대상 폴더도 만들지 않음.
- 선택 해제는 대소문자·`\`·앞뒤 `/` 차이를 무시하고, 이름 앞부분만 같은 것(`하위` vs `하위폴더`)은 고르지 않음.
- 경로 공격은 `InvalidDataException("안전하지 않은 경로: …")` 로 멈춤(앞 항목은 풀려 있음). Windows 특수 이름(`a.txt:evil`, 끝 점·공백, `con.txt`)은 예외 없이 대상 폴더 안에 풀림. tar 링크 항목은 빈 일반 파일로 풀리고 그 아래 항목은 `IOException`.
- 명령줄 키워드는 대소문자를 가림(`--OPEN` 은 경로로 취급되는데 결과적으로 열림). 창 없이 풀기의 `--extract-to` 는 대소문자 무시.
- (W1-D) 키워드 명령의 인자가 모자라면 아무것도 안 함(예전엔 `--extract-to <있는 압축>` 처럼 폴더가 빠지면 경로 목록으로 처리돼 열렸음).
- (W1-D) 목록 정렬은 압축할 목록(모으는 중)에도 적용된다(보이는 순서만 — 압축 대기 원본 목록 `Entries` 는 넣은 차례 그대로).
