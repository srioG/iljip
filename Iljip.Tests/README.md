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
  기대를 뒤집었고(다시 생기면 실패), 같은 번호의 Skip 은 지웠습니다. 남은 버그는 B3·B4·B5 입니다.
  이어서 **W1-A 1차 검증 지적**(같은 날)으로 잘린·손상 .gz/.egg/.alz 를 '완료'로 끝내던 문제, 끝 구분자 없는 EGG/ALZ 폴더 항목,
  ALZ 한자·기호 이름·잘린 ALZ 이름, 점뿐인 .gz 이름, 서명 뒤가 쓰레기인 RAR5, 같은 폴더 동시 해제 충돌, 분할 EGG 형제 파일 전부 열기를 고쳤습니다
  (아래 '고친 버그' 표 아래쪽).

## 실행 방법

```powershell
dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj
```

- 솔루션(`Iljip.sln`) 전체를 `dotnet test` 로 돌리면 C++ 셸 확장 때문에 실패하므로 **csproj 를 직접** 지정합니다.
- 처음 한 번은 NuGet(xunit 등)을 내려받습니다. 그다음부터는 빌드 포함 약 1분, 테스트만 30~60초.
- 결과(2026-10-12 W1-A 1차 검증 지적 반영 후): `통과!  - 실패: 0, 통과: 478, 건너뜀: 10, 전체: 488` — **건너뜀(Skip)은 정상**입니다(아래 설명).
  (W1-A 전: 통과 394·건너뜀 20·전체 414 → W1-A: 통과 430·건너뜀 10·전체 440)
- 느린 축: 파일명 보이는 암호 7z(py7zr, 키 유도 2^19회)를 쓰는 테스트, 32 MiB 취소 테스트, 실제 `Iljip.exe` 를 띄우는 테스트(각 5~10초).
- 특정 묶음만: `dotnet test ... --filter "FullyQualifiedName~PasswordTests"`
- **계약만**(엔진과 무관하게 지켜야 할 것): `dotnet test ... --filter "Kind!=Record"` (357개) — 아래 '계약 테스트와 기록 테스트'.
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
| `Contract/CommandLineTests.cs` | [12] 명령줄 해석(창이 안 뜨는 경로만, 60초 시간 제한) + 실제 `Iljip.exe --extract-to` |
| `Contract/EggAlzTests.cs` | [13] EGG/ALZ 성공 경로(목록·해제·충돌·선택·취소·진행률·크기 0 항목·ALZ 한글·한자·기호 이름·끝 구분자 없는 폴더 항목) + **잘림·CRC 불일치 → 예외·짧은 파일 안 남김**, 분할 조각 후보 — 합성 EGG/ALZ |
| `Contract/AlzNameScannerTests.cs` | ALZ 머리 원본 바이트 스캐너(B10 우회·폴더 속성): 맞게 읽기, 잘리면 온전한 항목까지만, 서명 깨짐·항목 수 불일치·분할이면 예외 없이 포기, 이름 바이트 풀기(UTF-8→CP949→CP437) |
| `Contract/CollisionTests.cs` | 같은 이름이 있으면 덮지 않고 `이름 (1).txt` 로 비켜 쓰기, **같은 폴더에 동시에 여러 번 풀어도** 모두 다른 이름으로(gz·zip·egg) |
| `Contract/FileLockTests.cs` | 열기 실패 후 원본 파일을 잠그지 않는지(B8 고침) + 분할 EGG 조각들도 바로 지울 수 있는지 |
| `Contract/BugEvidenceTests.cs` | 아직 안 고친 버그 B3·B4·B5 '지금 이렇다' 근거(고쳐지면 실패하는 게 정상) |
| `Contract/FixedBugTests.cs` | 고친 버그 B1·B2(점뿐인 이름 포함)·B6·B7·B9 — 다시 생기면 안 됨(예전 근거 테스트를 올바른 기대로 뒤집은 것, 계약) |
| `Contract/FixtureCoverageTests.cs` | 매니페스트의 모든 픽스처가 어느 테스트엔가 쓰이는지, 파일이 다 있는지, 시간대 스위치가 먹었는지 |
| `TestSupport/` | 임시 폴더, 매니페스트 읽기, 비교 도우미, 최소 ZIP/TAR/GZIP/EGG(분할 포함)/ALZ 작성기, ZipCrypto 검사 계산기 |
| `Fixtures/` | 픽스처(아래) |
| `MANUAL_CHECKLIST.md` | 버전 바꿀 때 사람이 10분 안팎에 확인하는 점검표 |

## 계약 테스트와 기록 테스트

같은 동작에 대해 두 종류를 **다른 테스트로 나눠** 두었습니다. 한 테스트 안에서 엔진 고유 값(예외 타입 등)을 먼저 단언하면,
엔진을 바꾼 직후 그 단언이 먼저 실패해 뒤의 '파일이 남지 않는다'·'대상 폴더 밖에 안 쓴다' 같은 핵심 계약 결과가 가려지기 때문입니다.

- **계약 테스트**(이름에 `기록_` 없음, Trait 없음): 엔진과 무관하게 지켜야 할 것 — 해제 내용 sha256, 한글 이름, zip-slip 방어(대상 폴더 밖 무사),
  예외가 나면 대상 폴더 오염 없음, 취소 시 부분 파일 정리, 동명 파일 비켜 쓰기, 진행률 끝 '완료', `IsPasswordRelated` 판정.
  목록은 '파일 항목은 정확히, 폴더 항목은 있어도 없어도(있다면 기대 목록 안의 것)' 만 봅니다(`TreeAssert.ListFilesEqual`).
- **기록 테스트**(이름이 `기록_` 으로 시작하거나 근거 테스트, `[Trait("Kind","Record")]`, 131개): 지금 엔진의 예외 타입·문구,
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
- **`bug Bn …`** — 테스트를 쓰다 찾은 본체 버그 중 **아직 안 고친 것**(B3·B4·B5). 본문은 역시 '고쳐졌을 때의 기대'입니다.
  고친 버그(B1·B2·B6~B10)와 TAR-빈폴더는 아래 '고친 버그' 표로 옮겼습니다.

| 사유 | 무엇 | 지금 실제 동작(관찰) |
|---|---|---|
| I1-7 | 헤더(파일명) 암호 7z/RAR 를 열 때 암호를 안 물음 | **자동 테스트 없음** — 빈틈이 화면(`MainViewModel.OpenArchiveAsync` 가 암호 없이 목록만 읽고 재시도 안 함)에 있어 MANUAL_CHECKLIST 2절로 확인. 서비스 쪽(암호 주면 목록 OK, 없으면 암호 예외)은 PasswordTests 가 고정. RAR 헤더 암호는 픽스처 없어 미검증. ★ `헤더암호_7z_전체_해제가_반디집과_같다` 는 B1 테스트이지 I1-7 이 아님 |
| I2-1 | 7z 만들기 | `NotSupportedException` |
| I2-2 | `.7z.001`·`.zip.001` | 확장자 미등록(Resolve=null). SharpCompress 로 억지로 열면 둘 다 정상 — 등록만 하면 될 가능성 큼. `.zip`+`.z01` 분할은 **이미 됨** |
| I2-9 | zipx·tgz·tar.gz·txz·xz·tbz2·iso·lzh·SFX exe | 미등록(.tar.gz 만 마지막 확장자 .gz 로 GZ 처리기). 기대값은 '반디집 7.46 과 같게': zipx·tgz·tar.gz·iso·lzh 는 안 내용을 바로, **.tar.xz·.tbz2 는 안쪽 `.tar` 하나만**(반디집 실측, 7z.dll 기본도 같음). tar 까지 한 번에 펼치는 건 '반디집보다 나은' 선택 목표라 시험하지 않음. 지금(SharpCompress 0.50.5): **.tar.gz 는 GZ 처리기가 안쪽 `이름.tar` 하나로 보여 주고 꺼냄**(기록 테스트 `기록_tar_gz는_안쪽_tar_하나로_보이고_풀린다`). 억지로 `ArchiveFactory.OpenArchive` 하면 zipx(일반)·SFX 는 정상, zipx(AES+XZ)는 목록은 되지만 해제가 'Specified method is not supported', tgz·tar.gz·xz·txz·tbz2·lzh 는 'Cannot determine compressed stream type'(tgz·tar.gz 는 `GZipArchive.OpenArchive` 로 직접 열면 이름 빈 항목 하나), iso 는 **항목 0개로 조용히 성공** |
| I2-10 | 맥 NFD 이름 | 목록·풀린 이름이 자모 분리 그대로(내용은 정확) |
| I2-11 | ZIP 암호 방식 선택 | 항상 AES-256(방식 99). 고르는 옵션 없음 |
| B3 | 한글 암호 ZIP | 반디집은 암호를 CP949 로, 일집(SharpCompress·SharpZipLib)은 UTF-8 로 씀 → 서로 못 엶(반디집 `bz.exe` 로도 확인) |
| B4 | ZIP CRC 검사 없음 | 데이터가 깨진 ZIP 을 오류 없이 틀린 내용으로 풀어 냄. **ZipCrypto 1바이트 검사를 우연히 통과한 틀린 암호(약 1/256)는 첫 파일을 쓰레기 내용으로 원래 이름에 남긴 채** 다음 파일에서 '암호 불일치'로 멈춤 → 맞는 암호로 다시 풀면 진짜 파일은 `이름 (1)` 로 비켜 써짐 |
| B5 | 틀린 암호 판정 | 7z 틀린 암호(`DataErrorException 'Data Error'`)와 ZipCrypto 검사를 통과한 틀린 암호 중 Deflate 가 바로 깨지는 경우(`ZlibException 'Bad state'`)를 화면이 암호 문제로 못 알아봐 다시 묻지 않음 |

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

`BugEvidenceTests` 는 '지금 버그가 있다'를 그대로 고정합니다(재현 근거, Trait Kind=Record — 지금은 B3·B4·B5).
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

## 명령줄 해석 테스트의 한계와 제안(코드는 안 고침)

`MainViewModel.ProcessCommandLineAsync` 는 인자 해석과 창 띄우기(폴더 선택 창·진행 창·`MessageBox`·저장 창)가 한 함수에 섞여 있고,
진행 창은 `Application.Current.MainWindow` 를 씁니다. 그래서 테스트에서 돌리면 창이 떠서 멈추거나(모달) `Application.Current` 가 없어 오류 창이 뜹니다.
지금은 창이 안 뜨는 경로만 시험합니다: `--open` 성공, 경로만 준 경우(열기·압축 대기), 인자 없음/파일 없음.
화면 코드가 바뀌어 창으로 빠져도 테스트 호스트가 영영 멈추지 않도록, VM 호출은 스레드 풀에서 60초 시간 제한을 걸고 부릅니다(`RunVm`).
`--extract`·`--extract-here`·`--compress` 는 MANUAL_CHECKLIST 로 사람이 확인합니다.
`--extract-to` 는 `App.OnStartup` 의 창 없이 풀기 경로를 실제 `Iljip.exe` 로 실행해 확인합니다(종료 코드 0/2).

최소 리팩터링 제안:
1. 순수 함수 `CommandLineParser.Parse(string[] args) → CommandRequest { Verb, ArchivePath, Destination, Paths }` 로 해석만 떼어 내기
   (`--extract-here` 의 대상 폴더 계산 `Path.Combine(부모, 확장자 뗀 이름)` 도 여기로 — `.tar.gz` 가 `x.tar` 폴더가 되는 점 등을 시험 가능).
2. 창 띄우기를 `IUserInteraction`(폴더 고르기·저장 위치·암호 묻기·오류 알림·진행 창 만들기) 인터페이스로 빼서 `MainViewModel` 생성자로 주입
   → 테스트는 가짜 구현을 넣어 `--extract-here`·암호 재시도 3회·취소·압축 실패 시 부분 파일 삭제(커밋 c8471d0)까지 창 없이 시험.
   I1-7 도 이때 '열기 → 암호 요청 → 15개 목록'으로 자동 시험할 수 있습니다.
3. `App.IsHeadlessExtractCommand`·`RunHeadlessExtractAsync` 를 `internal static` 으로 두고 `InternalsVisibleTo("Iljip.Tests")` 추가.

## 그 밖에 고정해 둔 '현재 동작' (버그는 아니지만 엔진 교체 때 달라질 수 있는 것 — 대부분 기록 테스트)

- 목록 경로: zip·tar 는 폴더 끝에 `/`, 7z 는 없음. 화면은 둘 다 처리하므로 테스트는 둘 다 허용.
- 일집 ZIP·TAR 목록에는 빈 폴더만 폴더 항목이 있음(반디집 ZIP 은 모든 폴더 항목). (TAR 는 W1-A 전엔 폴더 항목이 하나도 없었음)
- ZIP '저장(Store)' 수준도 방식 번호는 8(Deflate, 압축 안 한 블록) — 반디집 `-l:0` 은 방식 0.
- 암호 없는 일집 ZIP 도 한글 이름에 EFS(UTF-8) 플래그를 켬(계약은 '한국어 탐색기 방식으로 읽어도 이름이 맞음').
- 7z 항목의 압축 크기는 모두 0(SharpCompress) — 화면 압축률이 의미 없음. zip·tar 는 항목별 값.
- 순수 .bz2 목록의 크기는 0(풀기 전엔 모름), 이름은 파일명에서 `.bz2` 를 뗀 것. FNAME 없는 .gz 도 같은 규칙(`.gz` 를 뗀 것, 크기는 gzip 끝의 원래 크기). 뗀 이름이 비거나 점·공백뿐이면 '압축풀림'.
- 해제 실패 시 '파일'은 안 남지만 먼저 만든 '폴더'는 남을 수 있음.
- 압축 도중 취소 시 서비스는 쓰다 만 ZIP·TAR 를 지우지 않음 — 지우는 건 `MainViewModel.CompressAsync` 의 몫.
- TAR 압축 도중 원본 파일이 줄어들면(머리에 적은 크기와 실제 읽은 양이 다름) `IOException`('…파일이 압축하는 도중에 바뀌었어요') — 조용히 깨진 tar 를 만들지 않음.
- 이미 취소된 토큰이면 대상 폴더도 만들지 않음.
- 선택 해제는 대소문자·`\`·앞뒤 `/` 차이를 무시하고, 이름 앞부분만 같은 것(`하위` vs `하위폴더`)은 고르지 않음.
- 경로 공격은 `InvalidDataException("안전하지 않은 경로: …")` 로 멈춤(앞 항목은 풀려 있음). Windows 특수 이름(`a.txt:evil`, 끝 점·공백, `con.txt`)은 예외 없이 대상 폴더 안에 풀림. tar 링크 항목은 빈 일반 파일로 풀리고 그 아래 항목은 `IOException`.
- 명령줄 키워드는 대소문자를 가림(`--OPEN` 은 경로로 취급되는데 결과적으로 열림). 창 없이 풀기의 `--extract-to` 는 대소문자 무시.
