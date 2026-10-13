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
- **자동 풀림 스텁 (Iljip.SfxStub — W2-10)**: C# / .NET Framework 4.6.2 / WinForms 한 파일 exe(약 70KB, 서드파티 코드 없음). 일집이 만드는 자동 풀림 exe 의 앞부분 —
  빌드하면 일집 실행 파일 옆 `sfx\Iljip.SfxStub.exe` 로 복사됨(단일 파일 발행에서도 따로). 암호 계산·풀 곳 임시 폴더 규칙·꼬리 찾기는 본체와 같은 파일(`Iljip\Services\Shared\*.cs`)을 링크
- **셸 확장 (Iljip.ShellExtension)**: C++/WRL, IExplorerCommand DLL
- **packaging**: MSIX Sparse Package, 자체 서명 인증서

## 솔루션 구조

```
일집/
├── Iljip.sln
├── Iljip/                         # WPF 본체
├── Iljip.SfxStub/                 # 자동 풀림 exe 스텁(.NET Framework 4.6.2 WinForms — W2-10)
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
- CLI 인자: `--open`, `--extract`, `--extract-here`(그 자리에), `--extract-to-folder`(이름 폴더에, 있으면 '이름 (2)'), `--extract-to`, `--compress`, `--add-to`(W2-06 — 압축에 넣기), `--add-to-pick`(W2-06 검증 지적 — 넣을 압축을 고르는 창부터) — 해석은 `Services/CommandLineParser.cs`
  (W2-05: 풀기 동사 뒤 압축 여러 개 = 여러 개 풀기, 탐색기 우클릭 풀기는 메인 창 없이 진행 창만 → 끝나면 '이름' 폴더를 탐색기로)
  (W2-11: `--compress-to <만들 폴더> <원본…>`(만들 곳을 정한 압축 창), `--extract-to-folder-in <폴더> <압축…>`(그 폴더 안 '이름' 폴더마다 — 메인 창 없이),
  `--extract-into <폴더> <압축…>`(검증 지적 — 여럿을 그 폴더에 바로, 메인 창 없이. ★ 창 없이 풀기 `--extract-to` 와는 다른 키워드), 동사 앞 표시
  `--new-window`(단일 인스턴스를 건너뛰는 새 창)·`--elevated`(관리자 권한으로 다시 뜬 일집 — 다시 권하지 않음). `CommandLineParser.ToArgs` 가 `Parse` 의 반대)
- **W2-05(2026-10-13)**: 같은 이름 파일이 있으면 덮어쓰기·건너뛰기·이름 바꾸기·취소 + '남은 파일도 똑같이'(☰ ▸ 설정에서 기본 동작 고정), '다 푼 뒤 압축 파일을 휴지통으로'(기본 끔), 여러 압축 한꺼번에 풀기(진행 창 하나·암호 한 번·끝에 요약)
- **W2-06(2026-10-13)**: 열린 ZIP·7Z 고치기 — 파일 넣기(끌어 놓기·메뉴)·지우기(Del)·이름 바꾸기(F2)·새 폴더, 임시 파일에 다 쓰고 검사한 뒤에만 바꿔 넣음(실패면 원본 그대로, 암호·방식·한글 이름 유지),
  `--add-to <압축> <경로…>`(창 없이 넣기). 압축 속 파일을 열어 고쳐 저장하면 '압축에 다시 넣을까요?'(I3-3)
- **W2-07(2026-10-13)**: 압축은 **한 화면 압축 창**(이름·만들 곳·형식·수준·암호·나누기·확인란 — 예전 저장 창 → 옵션 창 두 번을 하나로, 마지막 값 기억), 각각 따로 압축(원본마다 하나·끝에 요약),
  이름에 날짜(`보고서_20261013.zip` — 세 모양, GZ·BZ2 는 `보고서_20261013.xlsx.gz`), 쓸모없는 파일 빼기(Thumbs.db·desktop.ini·~$ 등 — 목록은 압축 창·☰ ▸ 설정에서 고침), 다 만들고 시험하기, 시험을 통과하면 묻고 원본을 휴지통으로(기본 끔 —
  압축 창은 기억하지 않고 ☰ ▸ 설정에서만 켜 둠), '만들 곳' 처음 값은 ☰ ▸ 설정(원본이 있는 폴더·마지막으로 만든 곳·정해 둔 폴더)
- **W2-09(2026-10-13)**: 맥 ZIP 의 자모 분리(NFD) 한글 이름을 붙은 글자(NFC)로 보이고 풂, 일본어(Shift-JIS)·중국어(GBK) 이름 판별(한국어 먼저), **풀기 창**(풀 곳·최근 푼 곳 10개·'압축파일명으로 폴더 만들기'·
  '다 풀고 휴지통으로'·'다 풀면 폴더 열기' — 툴바 [압축 풀기]·선택 항목 풀기·`--extract` 가 폴더 고르기 창 대신), 이름으로 찾기(모든 폴더, Ctrl+F·Esc), Backspace = 위 폴더, 상태 줄 '선택: 3개 · 12.4MB',
  트리|목록 가르는 막대(너비 기억)
- **W2-10(2026-10-14)**: **자동 풀림 EXE 만들기** — 압축 창 형식 '자동 풀림 EXE'. 받는 사람에게 프로그램이 없어도 두 번 눌러 풀림(한국어 화면·풀 위치 묻기·한글 이름·한글 암호 —
  AES-256·ZipCrypto), 무인 모드 `이름.exe /s /d:폴더 [/p:암호]`. 형식 칸 아래와 만들기 직전에 '회사 메일은 실행 파일(.exe)을 막는 경우가 많아요…'([그래도 만들기][ZIP으로 만들기][취소] + '다시 묻지 않기'),
  일집 안에서도 열고 풂·'다 만들고 시험하기'. 4GB 가 넘는 exe 는 Windows 가 실행하지 않아 만들지 않음(만들기 전에 묻고 ZIP 권함), 나중에 코드 서명해도 풀림(검증 지적 2차)
- **W2-11(2026-10-14)**: **시작 화면**(빈 창 — [압축 파일 열기][압축하기][압축 풀기…]·끌어 놓기 안내·**최근 파일** 10개, 없는 파일은 흐리게 '없음' → '목록에서 뺄까요?', 오른쪽 클릭 메뉴),
  ☰ ▸ **새 창**(Ctrl+N — 일집 창 둘로 나란히)·**다른 이름으로 저장**(같은 형식은 복사 — 나뉜 압축은 조각 전부, ZIP↔7Z 는 풀고 다시 압축·'같은 암호를 걸까요?')·**이 압축 파일 지우기**(묻고 닫은 뒤 조각 전부 휴지통으로),
  권한이 필요한 곳(Program Files 등)에 풀기·압축하다 막히면 **'관리자 권한으로 다시 시도'**(UAC 로 같은 작업을 다시 — 암호는 넘기지 않음, 관리자 창 제목 끝 '(관리자)')
- C++ 셸 확장 5개 메뉴: "일집으로 열기" / "여기에 풀기 (일집)" / "\"이름\"\ 에 풀기 (일집)" / "압축 풀기... (일집)" / "일집으로 압축"

## 현재 진행 단계 (2026-10-14 W2-11 후)

**W2-11(시작 화면·최근 파일·압축 파일 관리 I3-15, 관리자 권한으로 다시 시도 I3-16) 작업 + 검증 지적 수정 1차 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함, stash 셋은 손대지 않음).
새 파일(아직 git 에 없음 — 커밋할 때 같이): `Iljip/Services/`(`RecentFilesService.cs`·`ArchiveSaveAs.cs`·`ElevationService.cs`·`CommandLineQuoting.cs`·`IProcessLauncher.cs`·`MappedDrives.cs`·`StartupDecision.cs`·검증 지적 1차에 `SettingsPatch.cs`),
`Iljip/ViewModels/MainViewModel.Start.cs`·`MainViewModel.Elevation.cs`, `Iljip/Views/StartView.xaml(.cs)`, 시험 `Iljip.Tests/Contract/`(`RecentFilesTests.cs`·`StartScreenTests.cs`·`ElevationTests.cs`·`StartupDecisionTests.cs`).
계약 테스트 실패 0(전체 2041 — 통과 2041·건너뜀 0, W2-11 직후 2018·W2-10 검증 지적 2차 뒤 1937 에서). 아래 변경 이력 'W2-11'·'W2-11 검증 지적 수정(1차)'. 그 전(W2-10) 기록:

**W2-10(자동 풀림 EXE 만들기 I3-1) 작업 + 검증 지적 수정 1차·2차 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함, stash 셋은 손대지 않음). 새 프로젝트 `Iljip.SfxStub/`·`Iljip/Services/Shared/`(검증 지적 1차에 `SfxStagingCore.cs`, 2차에 `SfxTrailerCore.cs` 더함)·`Iljip/Services/SfxBuilder.cs`·
새 시험 두 파일이 아직 git 에 없다(untracked) — 커밋할 때 같이 넣을 것(`Iljip.csproj` 가 스텁 프로젝트를 참조하므로 빠지면 빌드가 안 됨). `Iljip.sln` 에도 스텁 프로젝트를 더했다.
계약 테스트 실패 0(전체 1937 — 통과 1937·건너뜀 0, 검증 지적 1차 뒤 1919·W2-10 직후 1900·W2-09 검증 지적 수정 1차 뒤 1843 에서). 아래 변경 이력 'W2-10'·'W2-10 검증 지적 수정(1차)'·'(2차)'. 그 전(W2-09) 기록:

**W2-09(맥 NFD·일본어·중국어 이름 I2-10·풀기 창 I2-13·찾기·Backspace·선택 합계·트리 너비 I2-14) 작업 + 검증 지적 수정 1차 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함, stash 셋은 손대지 않음).
계약 테스트 실패 0(전체 1843 — 통과 1843·건너뜀 0 — 마지막 Skip I2-10 을 풀었다. W2-09 직후 1816). 아래 변경 이력 'W2-09'·'W2-09 검증 지적 수정(1차)'. W2-08(만든 압축 시험 등)은 검증에서 되돌려져 `stash@{0}`(`iljip-wave2-W2-08-failed`)에 있다.
**★ 커밋할 때 꼭 같이 넣을 것(HEAD 5a95fa6 만으로는 빌드가 안 됨)**: W2-07 검증 지적 수정 2차의 몫이 W2-07 커밋에 빠지고 W2-08 stash 로 쓸려 들어가 있었다 —
`Iljip/Models/ArchiveProgress.cs`(`IsStage`)·`Iljip/Services/ThrottledArchiveProgress.cs`(단계 알림은 늘 통과)·`Iljip/Services/EngineStreams.cs`(`BytesRead` 4줄)·
`Iljip.Tests/Contract/ThrottledArchiveProgressTests.cs`(시험 1개)·`Iljip.Tests/Contract/ArchiveEditReviewTests.cs`(새 파일). 이번 묶음 시작 때 stash 를 건드리지 않고(`git diff HEAD stash@{0} -- 파일`·`git show stash@{0}^3:…` 를 읽어
작업 폴더에 `git apply`) 이 다섯만 되살렸다(W2-08 의 다른 변경은 넣지 않음) — 되살린 뒤 기준 통과 1746·건너뜀 1·전체 1747(W2-07 2차 기록과 같음). 그 전(W2-07) 기록:

**W2-07(각각 따로 I2-4·압축 후 원본 지우기 I3-4·이름에 날짜 I3-5·쓸모없는 파일 빼기 I3-6·설정 기억과 한 화면 압축 창 I2-12) 작업 + 검증 지적 수정 1차·2차 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`stash` 안 함, stash 두 개는 손대지 않음).
계약 테스트 실패 0(전체 1747 — 통과 1746·건너뜀 1: I2-10). 아래 변경 이력 'W2-07'·'W2-07 검증 지적 수정(1차)'·'(2차)'. 주의: W2-06 커밋(88814ba)에 `Iljip/Services/EngineStreams.cs` 의 4줄(`BytesRead`)과 `Iljip.Tests/Contract/ArchiveEditReviewTests.cs` 가 빠져 있어
HEAD 만으로는 빌드가 안 된다 — 둘 다 작업 폴더에 그대로 있다(이번 묶음은 손대지 않음, 이번 커밋에 같이 넣을 것). 그 전(W2-06) 기록:

**W2-06(압축 고치기 I2-3·압축 속 파일 고쳐서 다시 넣기 I3-3) 작업 + 검증 지적 수정 1차·2차 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`stash` 안 함, stash 두 개는 손대지 않음).
계약 테스트 실패 0(전체 1644 — 통과 1643·건너뜀 1: I2-10). 아래 변경 이력 'W2-06'·'W2-06 검증 지적 수정(1차)'·'(2차)'. 압축 속 파일 열기(I1-3)는 W2-04 보관본의 작업 폴더 모양을 최소한으로 들였다 — W2-04 를 다시 할 때 이쪽을 바탕으로. 그 전(W2-05) 기록:

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
   — 재배포 전에 MANUAL_CHECKLIST 9절(7z.dll·새 형식 실물 확인)·10절(7z 만들기·목록까지 잠긴 압축)·11절(나눠 만들기)·12절(같은 이름·우클릭 풀기 — W2-05)·13절(압축 고치기·다시 넣기 — W2-06)·14절(한 화면 압축 창·원본 지우기 — W2-07)·15절(맥·일본어·중국어 이름, 풀기 창, 찾기 — W2-09)·16절(자동 풀림 EXE — W2-10: 화면 모드·다른 PC 의 SmartScreen·백신·회사 메일)·17절(시작 화면·최근 파일·다른 이름으로 저장·지우기·새 창, 진짜 UAC 와 관리자 일집 — W2-11)도 볼 것

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
  (W2-10 뒤) 발행 폴더에 `sfx\Iljip.SfxStub.exe`(자동 풀림 만들기 부품 — 단일 파일 발행에서도 따로 남음, 2026-10-14 임시 폴더 발행으로 확인)가 더해진다. 재사용장치 풀기와는 상관없고,
  없으면 압축 창 형식 목록에서 '자동 풀림 EXE' 만 빠진다. 회사 백신이 이 파일을 지울 수 있다(MANUAL_CHECKLIST 16절).
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
- [ ] W2-04 다시 할 때: 설정(`Services/AppSettings.cs`)·`AppServices`·`IShellLauncher` 는 W2-05 가 같은 모양으로 먼저 들였음 — 덧붙이기만.
      W2-06 이 압축 속 파일 열기(작업 폴더 `ArchiveWorkspace`·`OpenedEntry`·`RiskyFileTypes`·`ZoneIdentifier`, 더블클릭·우클릭 '열기')를 최소한으로 먼저 들였음 — 그 위에 덧붙이기(임시 폴더 청소기 `TempJanitor` 등은 아직 없음)
- [x] **W2-06**: 압축 고치기(I2-3 — ZIP·7Z 넣기·지우기·이름 바꾸기·새 폴더, `--add-to`)·고친 파일 다시 넣기(I3-3)
- [ ] W2-06 남은 확인: MANUAL_CHECKLIST 13절(도구 막대·☰·우클릭 메뉴·F2·Del 창 모양, 오피스로 고쳐 저장했을 때 한 번만 묻는지, 닫을 때 묻기, 회사 PC DLP·OneDrive 폴더에서 바꿔 넣기, 큰 압축 시간)
- [ ] 탐색기 우클릭 'zip에 추가 (일집)': C++ 셸 확장에 메뉴를 더해 `Iljip.exe --add-to <압축> <고른 파일…>` 을 부르게(명령줄 쪽은 W2-06 에서 끝, 재배포 범위).
      '기존 zip 고르기' 메뉴는 `Iljip.exe --add-to-pick <고른 파일…>`(일집이 압축 고르기 창을 띄움 — W2-06 검증 지적에서 명령줄 끝)
- [x] **W2-07**: 한 화면 압축 창·설정 기억(I2-12)·각각 따로(I2-4)·압축 후 원본 휴지통(I3-4)·이름에 날짜(I3-5)·쓸모없는 파일 빼기(I3-6)
- [ ] W2-07 남은 확인: MANUAL_CHECKLIST 14절(압축 창 모양·칸 순서·Tab·작은 화면, 진짜 휴지통으로 옮기고 되살리기, 네트워크 드라이브·USB, 회사 PC DLP·OneDrive 폴더, 엑셀이 만든 `~$` 파일)
- [x] **W2-09**: 맥 NFD 이름·일본어·중국어 이름(I2-10), 풀기 창(I2-13), 이름으로 찾기·Backspace·선택 합계·트리 너비(I2-14)
- [ ] W2-09 남은 확인: MANUAL_CHECKLIST 15절(진짜 맥·일본어·중국어 Windows 산출물, 풀기 창 모양·Tab·작은 화면, 찾은 결과가 많을 때 목록 속도, 가르는 막대 끌기·기억)
- [ ] (선택) 이름 글자 방식 손으로 고르기 메뉴(한국어·일본어·중국어·UTF-8) — 자동 판별이 틀릴 때(짧은 중국어 이름 한두 개 등). W2-09 는 자동만
- [x] **W2-10**: 자동 풀림 EXE 만들기(I3-1) — 일집 자체 스텁·무인 모드·메일 안내(아래 변경 이력)
- [x] **W2-10 검증 지적 수정(1차)**: [ZIP으로 만들기] 한글 AES 암호 질문·같은 풀 곳 마무리 잠금·강제 종료 흔적 바로 정리와 원래 파일 보이는 폴더로·SfxStubFact 건너뛰기 좁힘·작은 파일 많을 때 속도·PC 보호 문구(아래 변경 이력)
- [x] **W2-10 검증 지적 수정(2차)**: 4GB 넘는 exe 막기(만들기 전 묻기·만든 뒤 확인·ZIP 권함)·다 풀고 지우는 사이 '지난번 흔적' 오보·덮어쓰기 속도·잘린 exe 를 일집에서 손상으로·'다시 묻지 않기'는 [그래도 만들기]만·나중에 코드 서명해도 풀림·NFC/NFD 같은 이름(아래 변경 이력)
- [ ] W2-10 남은 확인: MANUAL_CHECKLIST 16절(화면 모드 — 풀 위치 창·한글·진행·같은 이름·취소·DPI, 다른 PC 의 SmartScreen·Smart App Control·백신, 회사 메일 게이트웨이, .NET Framework 4.6.2 없는 옛 PC, 압축 창 안내·질문 창 모양,
  (검증 지적 1차) 두 창 동시 풀기·작업 끝내기 뒤 다시 풀기·로그오프·작은 파일 많을 때 시간·[ZIP으로 만들기] 한글 암호 질문,
  (검증 지적 2차) 4GB 넘는 자료의 질문 창·다시 묻기·덮어쓰기 시간·잘린 exe 열기·코드 서명한 exe)
- [ ] (선택) 자동 풀림 exe 코드 서명: 서명은 **만든 exe 마다**(만든 뒤 signtool 등으로 — 검증 지적 2차부터 스텁이 끝의 서명 표를 건너뛰고 꼬리를 찾음, 서명 도구의 파일 모양을 규격대로 흉내 내 시험.
      진짜 signtool·인증서로는 확인 안 함 — 이 PC 에 없음). 스텁만 서명해 두는 방법은 뒤에 ZIP 을 붙이면 서명 범위 밖 데이터가 생겨 확인 안 됨. 인증서·비용은 사람이 정할 일
- [ ] (선택) 자동 풀림 exe 를 우클릭 메뉴('자동 풀림 exe 로 압축')에 — C++ 셸 확장·재배포 범위
- [x] **W2-11**: 시작 화면·최근 파일·다른 이름으로 저장·이 압축 파일 지우기·새 창(I3-15), 관리자 권한으로 다시 시도(I3-16)(아래 변경 이력)
- [ ] W2-11 남은 확인: MANUAL_CHECKLIST 17절(시작 화면 생김새·작은 창, 진짜 휴지통으로 지우고 되살리기, 두 창 함께 쓰기, **진짜 UAC 창·관리자 일집의 진행 창·제목·끌어 놓기**, 표준 사용자 계정의 관리자 암호 묻기, 네트워크 드라이브, 관리자가 만든 파일 지우기,
  검증 지적 1차 줄 — 두 창의 최근 파일·설정, 없는 파일 정리, 여럿을 Program Files 에 바로 풀기, 읽기 전용 덮어쓰기, `--add-to` 다시 시도, `--elevated` 만 붙인 창의 제목)
- [ ] (W2-11 검증 지적 1차에서 남김) 압축 파일 **자체에** 쓸 권한이 없을 때 고치기 문구가 '다른 프로그램이 쓰고 있어서…'(`ArchiveSwap.EnsureWritable` 이 접근 거부를 잠김으로 봄) — 권한 문구·관리자 권한으로 다시 시도로 가르기
- [ ] W2-08 다시 할 때: '관리자 권한으로 다시 시도'는 지금 `MainViewModel.OfferElevatedRetry`(기존 `Choose`)가 물음 — 문제 창(ShowProblem·ErrorCatalog)이 들어오면 '권한 없음' 분류의 Actions 로(`ElevationService.ActionsFor`·`RetryActionId` 를 그대로 쓰면 됨)
- [ ] (선택) 압축 고치기(넣기·지우기·이름 바꾸기)의 권한 오류에도 '관리자 권한으로 다시 시도' — 지금은 `ArchiveSwap.EnsureWritable` 이 권한 오류를 '다른 프로그램이 압축 파일을 쓰고 있어…'(LockedMessage)로 바꿔 대부분 권하지 않음(기존 시험이 그 문구에 기댐). `--add-to` 만 인자(`--elevated --add-to`)를 넘기게 해 둠
- [ ] 임시 폴더 청소기(`TempJanitor`): 바꿔 저장하는 도중 일집이 강제로 끝나면 `temp\convert\guid` 가 남음(정상 끝·실패·취소는 지움)
- [ ] (선택) 탐색기 오른쪽 끌기 메뉴('여기에 압축'·'여기에 풀기') — `--compress-to`·`--extract-to-folder-in` 명령줄은 W2-11 에서 끝(C++ 셸 확장·재배포 범위)
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
- **2026-10-13 W2-06 — 압축 고치기(I2-3)·압축 속 파일 고쳐서 다시 넣기(I3-3)(커밋 전, 배포 안 함)**:
  ① 고치기 엔진 `Services/Editing/` — `EditOp`(넣기·지우기·이름 바꾸기·새 폴더)·`IArchiveEditor`, `ArchiveServiceLocator.EditorFor/CanEdit`. **ZIP·7Z 만** — 그 밖의 형식·나뉜 압축(`.zip.001`·`.z01`…`.zip`·`.7z.001`)·앞에 다른 것이 붙은 ZIP(SFX 등)·7z.dll 을 못 쓰는 PC 의 7z 는 `ArchiveNotEditableException`('이 형식은 고칠 수 없어요. ZIP이나 7Z로 다시 압축하면 고칠 수 있어요.').
  계획은 `EditPlanner`(빈 이름·`\ / : * ? " < > |`·끝 점/빈칸은 쉬운 안내, 같은 폴더의 같은 이름(대소문자 무시 — 자기 이름의 대소문자만 바꾸기는 됨)은 '‘…’ 이름이 이미 있어요…', 넣을 때 같은 이름은 W2-05 같은 이름 창을
  설정과 상관없이 늘 물음(덮어쓰기·건너뛰기·이름 바꾸기·[취소]=고치기 전체 취소), 처리기가 없으면 '이름 (1)'). **ZIP**(`ZipArchiveEditor`·`ZipRawRewriter`): 손대지 않는 항목은 로컬 머리·압축 데이터·중앙 목록 기록을
  **바이트 그대로 복사**(다시 압축·다시 암호화 안 함 — CP949 이름·시각·확장 필드 그대로), 이름을 바꾼 항목만 UTF-8 이름 + bit 11(유니코드 경로 0x7075 는 뺌). 새 파일은 SharpZipLib 로 임시 ZIP 에 압축
  (수준은 원래 항목에서 짐작 — 모두 저장이면 저장) 뒤 그 데이터를 **원래 암호 바이트(`ZipPasswordProbe` 로 CP949/UTF-8 판정)·원래 방식(ZipCrypto, 또는 같은 세기 AES·AE-1/AE-2)으로 암호화**해 붙임 —
  반디집 한글 암호 ZIP 에 넣어도 반디집이 같은 암호로 풂. 데이터 서술자·ZIP64 읽기, 강한 암호(bit 6)·중앙 목록 암호(bit 13)·여러 디스크는 고칠 수 없음.
  **7z**(`SevenZipArchiveEditor` — SharpSevenZip): 원본의 **작업 사본**에서 지우기 → 이름 바꾸기 → 파일 넣기 → 빈 폴더(자리표 이름 `~일집-새폴더-…` 로 넣고 이름 바꾸기)를 **따로따로**
  (실험: 지우기와 이름 바꾸기를 한 번에 하면 이름 바꾸기가 사라지고, 경로로 넣으면 위 폴더가 겹쳐 생기며, `ModifyArchive` 는 실패해도 원본을 지우고 임시 파일을 옮김 — 그래서 사본). 솔리드·파일 이름 암호화·암호 그대로,
  수준은 모두 저장이면 저장 아니면 보통. ② 바꿔 넣기(`ArchiveSwap`): 같은 폴더 숨김 `.~이름.8글자.tmp` 에 다 쓴 뒤 **검사(`ArchiveTester` — ZIP 은 구조 다시 읽기 + 목록이 계획과 같은지 + 모든 항목 CRC
  (암호 항목은 암호로), 7z 는 목록 비교 + 7z.dll Check)를 통과해야만** `File.Replace`(예비 `.~이름.bak` — 안 되면 옮기기 방식, 도중 실패면 되돌림, 잠김 200ms×3). 시작 전에 원본을 쓰기로 열어 보아
  잠겼으면 '다른 프로그램이 이 압축 파일을 쓰고 있어서 고치지 못했어요. 그 프로그램을 닫고 다시 해 주세요.'(읽기 전용·없음·권한도 쉬운 문구). 취소·쓰기 실패·검사 실패·바꾸는 순간 잠김이면 원본은 바이트 그대로,
  임시·예비 파일·임시 폴더를 남기지 않음. ③ 화면: 압축을 볼 때 도구 막대 [파일 넣기][지우기][이름 바꾸기][새 폴더](압축할 목록 단추는 숨김), ☰ 메뉴·목록 우클릭 메뉴에 같은 항목, 이름 칸 **F2 인라인 바꾸기**
  (확장자 앞까지 선택, 다른 열은 읽기 전용, 편집은 F2·메뉴로만 시작), Del = '{N}개를 압축 파일에서 지울까요?… 지운 것은 되돌릴 수 없어요.' [지우기][취소](기본 취소), 새 폴더 이름은 새 `IUserInteraction.AskText`
  (`Views/TextPromptDialog`). 지금 보는 폴더 안에 넣기·만들기, 고친 뒤 목록을 다시 읽음, 암호 압축은 암호 창(열 때 받은 암호 먼저, 틀리면 다시). 끌어 놓기: 고칠 수 있는 압축을 볼 때 선택 창이
  '지금 압축 파일을 보고 있어요. 끌어 온 파일을 이 압축 파일에 넣을까요?' [이 압축 파일에 넣기]…[취소](기본은 여전히 [취소]) — `DropPolicy.Decide` 4인자판을 더함(3인자판 그대로, 고칠 수 없는 압축은 예전 선택지).
  명령줄 `--add-to <압축> <경로…>`(`CommandVerb.AddTo` — 메인 창 없이 진행 창 → '{n}개를 넣었어요.' 짧은 알림, 실패는 쉬운 오류 한 번). **C++ 셸 확장의 'zip에 추가' 메뉴는 아직 없음**(재배포 범위 — 명령줄만).
  `_commandGate` 를 쥔 명령줄 처리 안에서 고치기를 부르면 다시 잠그다 멈추는 자리는 AsyncLocal(`_inCommandGate`)로 피함. ④ I3-3: 압축 속 파일 열기(더블클릭·우클릭 '열기')는 **W2-04 보관본
  (stash `iljip-wave2-W2-04-failed`)의 작업 폴더 모양을 최소한으로 들임**(`Services/ArchiveWorkspace.cs`·`RiskyFileTypes.cs`·`ZoneIdentifier.cs` 를 보관본에서 옮기고 `AppPaths.OpenRoot` = `%LOCALAPPDATA%\Iljip\temp\open`,
  실행 파일은 열기 전에 경고, 웹에서 받은 표시를 연 파일에 옮김) + `OpenedEntry` 에 압축 경로·'나중에' 표시·기준 다시 잡기(Rebase)를 덧붙임 — W2-04 를 다시 할 때 이 모양을 바탕으로. 연 파일은 `IFileChangeSource`
  (FileSystemWatcher + 2초 폴링 — `AppServices.FileChanges`, 시험은 가짜)로 보고 `OpenedEntryWatcher` 가 1초 조용해진 뒤 SHA-256 으로 정말 바뀌었을 때만(오피스의 바꿔치기 저장·여러 알림은 한 번) 묻는다:
  '‘보고서.xlsx’가 바뀌었어요. 압축 파일에 다시 넣을까요?' [넣기][나중에][넣지 않기] → 넣기는 같은 자리 같은 이름으로 바꿔 넣기를 한 번의 고치기로(처음엔 '지우기 + 넣기'였다가 검증 지적 수정 1차 ⑧(b)에서 바꿔 넣기로 바꿈 — 연 뒤에 그 항목을 지웠으면 제자리에 새로; 암호는 열 때의 암호 — 메모리에만, 디스크에 안 씀).
  [나중에]는 다른 압축을 열거나 비울 때·일집을 닫을 때 다시 묻고(닫을 때는 [넣기][넣지 않기(닫기)][취소] — 취소면 안 닫힘, `MainWindow` Closing), 고칠 수 없는 형식(RAR·TAR 등)은
  '…바뀌었어요. 이 형식은 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?' → 저장 창. 일집을 닫으면 감시를 멈추고 작업 폴더를 지움(`MainViewModel.Shutdown`).
  시험: 새 `ArchiveEditTests`(ZIP 7종 — 반디집 기본·CP949·ZipCrypto·한글 ZipCrypto·AES·한글 AES·데이터 서술자 × 넣기·지우기·이름 바꾸기·새 폴더, 7z 5종 — 반디집 기본·이름 암호화·한글 이름 암호화·솔리드·내용만 암호 × 같은 4가지:
  목록·풀린 SHA·손대지 않은 항목 바이트·UTF-8 플래그·같은 암호 방식, 반디집 `bz t` 12개, 취소·잠김·바꾸는 순간 잠김·쓰기 실패·검사 실패에 원본 그대로·남는 파일 없음, 고칠 수 없는 형식 16가지·나뉜 압축·앞에 붙은 ZIP,
  암호, 같은 이름, 이름 규칙)·`MainViewModelEditTests`·`EntryReinsertTests`(가짜 파일 감시 `FakeFileChangeSource`)·`MainWindowEditUiTests`(메인 창 XAML 을 STA 에서 만들기만 — 띄우지 않음, 잠깐 만든 WPF 앱 객체는 끝나면
  되돌리고 따로 순차 실행), `DropPolicyTests`·`CommandLineParserTests` 덧붙임, `TestSupport/ZipInspect.cs`(ZIP 원본 바이트 읽기·데이터 서술자 ZIP 만들기). 기대가 바뀐 기존 시험: `CommandLineParserTests.키워드_목록은_모두_해석된다`
  (키워드 6→7), `DropPolicyTests.압축을_보는_중_끌어_놓고_취소하면_그대로`·`압축을_보는_중_다른_압축을_끌어_와_열기를_고르면_그것이_열린다`(보는 압축이 ZIP 이라 [이 압축 파일에 넣기]가 맨 앞에 — 고칠 수 없는 TAR 는
  예전 선택지 그대로라는 새 시험을 더함). 푼 Skip 없음. 계약 테스트: 전체 1588 — 통과 1587·건너뜀 1(I2-10)·**실패 0**(W2-05 후 1424 에서 새 시험 164개). 단 이 PC 가 바쁠 때(빈 메모리 0.8GB·PFDlpMon·OneDrive)
  중간 실행에서 시간에 민감한 기존 시험 둘 — `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…`(따로 돌려도 3.6초로 실패한 때가 있음, 그때 160MB 압축 41초 — 7z 만들기 코드는 안 바뀜)·
  `LocatorTests.옮기기는_잠깐_잠긴_파일을_기다렸다_옮긴다` — 이 실패했다(한도는 그대로, `Iljip.Tests/README.md` 'W2-06').
  위험·못 본 것: 실제 창(F2 인라인 바꾸기·우클릭 메뉴·닫을 때 묻기)·진짜 FileSystemWatcher 와 오피스 저장·회사 PC DLP(PCFILTER)·OneDrive 폴더에서의 `File.Replace`·큰 압축의 시간(7z 는 다시 압축 — 솔리드면 통째로)은
  MANUAL_CHECKLIST 13절. `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-06 검증 지적 수정(1차, 커밋 전, 배포 안 함)** — 지적 9개 중 8개 고침, 1개(④)는 이유와 함께 건너뜀:
  ① 7z 지우기·이름 바꾸기·새 폴더가 바로 취소되지 않고 진행이 안 보임 → `SevenZipArchiveEditor`: SharpSevenZip 의 `ModifyArchive`·`CompressStreamDictionary` 는 도중에 멈출 길이 없어서(진행 콜백 `SetCompleted` 가 빈 함수),
  취소하면 **작업 사본을 FileStream.Lock 으로 잠가** 7z.dll 의 다음 읽기가 ERROR_LOCK_VIOLATION 으로 실패하게 함(`PassGuard` — 사본은 우리 것이라 망가져도 됨, 원본은 손대지 않음) + 블록 크기 `c=2m`(한 번에 쥔 일을 줄임 — 이 PC 0.5~1.4초).
  다시 압축하는 동안 임시 폴더의 결과 크기를 0.2초마다 재 막대를 올림(지우기는 남는 비율로 결과 크기를 짐작), 사본 만들기('준비하는 중')·검사(7z Check 를 취소 가능한 스트림으로 — 읽은 바이트로 진행)도 진행에 넣음.
  ② 반디집 ZIP 유니코드 경로(0x7075) → `ZipRawRewriter` 가 SharpCompress 처럼 첫 0x7075 의 UTF-8 이름을 키로(목록 이름과 같음 — 손대지 않은 항목은 0x7075 포함 바이트 그대로).
  ③ I3-3 넣는 동안 또 저장한 내용이 사라짐 → 넣을 내용을 작업 폴더 옆 `.reinsert` 에 복사(복사하며 SHA-256 — `OpenedEntry.CopyTo`·`ContentStamp`)해 넣고 그 바이트로 기준을 다시 잡음, 넣은 뒤 또 바뀌었으면 바로 다시 묻기. [넣지 않기]는 물어본 때의 내용만 넘김.
  ④ (건너뜀) `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…` 가 바쁠 때 3027ms 로 실패 — W2-02 의 7z **만들기** 길(이번 고치기 코드와 상관없음)이고, 한도를 늘리거나 Skip 하면 시험을 약하게 하는 것이라 그대로(이미 순차 묶음). 이번 전체 실행 두 번은 2770ms·1958ms 로 통과.
  ⑤ '기존 zip 고르기' → 명령줄 `--add-to-pick 경로…`(키워드 8개): 메인 창 없이 '넣을 압축 파일 고르기 (ZIP·7Z)' 창 → 고른 압축에 넣기, 고르지 않으면 조용히 그만(`RunAddToAsync(null, …)`). C++ 메뉴는 아직(재배포 범위).
  ⑥ 7z.dll 을 못 불러온 PC 의 7z → '이 PC에서는 7Z 파일을 고치는 부품(7z.dll)을 불러오지 못해 고칠 수 없어요. 보안 프로그램이 막았을 수 있어요…'(`ArchiveNotEditableException.SevenZipEngineMessage`·`ArchiveServiceLocator.NotEditableMessage`),
  다시 넣기 질문도 '이 PC에서는 7Z 파일을 고칠 수 없어요.'. TAR·나뉜 7z 는 그대로 '이 형식은 고칠 수 없어요'.
  ⑦ 파일과 같은 이름의 폴더를 넣으면 둘 다 생김 → `EditPlanner.AvoidFiles` 가 폴더째 '이름 (1)' 로(한 넣기 안의 같은 폴더는 같은 이름으로), ZIP·7z 둘 다.
  ⑧ (a) 원래부터 손상된 압축 → 검사가 실패하면 원본도 검사해, 원본이 이미 손상이면 '원래 압축 파일에 이미 손상된 곳이 있어서 고치지 않았어요(고치다 생긴 문제가 아니에요)…'(`ArchiveTester.TestOrBlameOriginal` — 7z 는 엔진 실패 때도).
  (b) 다시 넣기를 '지우기 + 넣기' 대신 **바꿔 넣기**(`EditOp.Add(…, ReplaceExisting: true)` — 있으면 묻지 않고 바꿈, 없으면(연 뒤에 지웠거나 이름을 바꿈) 제자리에 새로, 그 이름이 폴더가 됐으면 폴더는 두고 '이름 (1)'),
  넣기에 실패하면 오류 창 뒤 '‘…’를 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?' [다른 곳에 저장][나중에](일집을 닫을 때는 [닫지 않기] — 닫기를 멈춤).
  ⑨ 잃어버린 갱신 → 읽기 시작할 때 원본의 크기·수정 시각(`ArchiveSwap.FileStamp`)을 재 두고 바꾸기 직전에 다르면 바꾸지 않고 '고치는 동안 다른 곳에서 이 압축 파일이 바뀌어서, 그 내용을 덮어쓰지 않으려고 고친 것을 넣지 않았어요…',
  같은 프로세스 안의 바꾸기는 한 번에 하나(`CommitGate`).
  시험: 새 `ArchiveEditReviewTests`(14 — 유니코드 경로 4가지 + 반디집 `bz t`, 같은 이름 폴더 ZIP·7z + 계획, 원래부터 손상, 바뀐 원본 ZIP·7z, 동시에 셋, 바꿔 넣기 ZIP·7z)·`SevenZipEditProgressTests`(2 — 16MB 솔리드 7z: 막대가 오름, 취소 3초 안·원본 그대로 —
  실측 776~878ms)·`EntryReinsertTests` 7·`MainViewModelEditTests` 3·`CommandLineParserTests` 1, 가짜 창에 `OnChoicePrompt`·`OnProgressReport`(묻는 동안·넣는 동안 또 저장하는 시험). 기대가 바뀐 기존 시험:
  `CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 7→8 + '--add-to-pick' 포함). 푼 Skip 없음. 계약 테스트: 전체 1615 — 통과 1614·건너뜀 1(I2-10)·**실패 0**(전체 실행 두 번 모두).
  위험·못 본 것: 실제 창(압축 고르기 창·'다른 곳에 저장' 권하기)·진짜 FileSystemWatcher·회사 PC DLP(PCFILTER)·OneDrive 폴더, **네트워크 드라이브에서 7z 취소**(잠금으로 멈추게 하는 방법은 로컬 NTFS 에서만 확인 — 안 되면 그 단계가 끝난 뒤 멈춤),
  큰 실물 7z 의 진행 막대 모양은 MANUAL_CHECKLIST 13절(검증 지적 줄). C++ 셸 확장의 'zip에 추가'·'기존 zip 고르기' 메뉴는 아직 없음. `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-06 검증 지적 수정(2차, 커밋 전, 배포 안 함)** — 지적 7개 모두 확인해 맞아서 고침:
  ① (major) '다시 넣을까요?'와 명령줄 열기(--open·--extract·경로)가 겹치면 교착 + 그대로 닫으면 고친 내용이 지워짐 → `AskPendingBeforeLeavingAsync` 가 감시의 '조용히 기다림·내용 재기'만 기다리고
  (`OpenedEntryWatcher.WhenSettledAsync` — 처리마다 '가라앉음' 표시: 묻기로 넘기기 직전, 또는 앞 처리(묻기·넣기)를 기다리기 시작할 때) 묻거나 넣는 중인 파일은 그 흐름에 맡김(예전 `WhenIdleAsync` 는
  묻기·넣기까지 기다렸는데, 명령줄은 `_commandGate` 를 쥔 채 오고 [넣기]는 그 차례를 기다려 서로 멈춤). 다른 압축이 열린 뒤 차례가 풀리면 원래 압축(그 경로·기억한 암호)에 넣음.
  `ConfirmExitAsync` 는 묻거나 넣는 중인 파일이 있으면 닫지 않고 '‘…’를 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요. 끝난 뒤에 닫아 주세요.'(예전: 그 파일을 건너뛰고 닫기 허락 → 작업 폴더 삭제).
  ② 빈 7z(일집으로 모두 지운 32바이트 결과·빈 폴더만 있는 7z)에 넣기·새 폴더가 '손상됐어요' → `SevenZipArchiveEditor.IsSolid`: 솔리드 정보가 없어 SharpSevenZip 이 InvalidOperationException 을 내면 솔리드 아님.
  ③ (①과 같은 장치) 넣는 도중(진행 창은 모달 아님) 창을 닫으면 묻지 않고 닫혀 고친 파일·넣기가 사라짐 → 위 닫기 거절로 넣기는 끝까지, 고친 파일 남음.
  ④ Windows 에 못 쓰는 이름(':' 든 이름·끝 점 — 맥·리눅스 압축)의 항목을 고쳐 [넣기] → '쓸 수 없는 글자' 오류 → 다시 넣기(바꿔 넣기 `ReplaceExisting`)의 이름은 압축에 있던 원래 이름이라
  `EditPlanner.ValidateExistingName`(빈 이름·'.'·'..'·'/'·'\'·NUL 만 막음)으로 봄. 사용자가 고르는 이름(넣기의 다른 이름·이름 바꾸기·새 폴더)은 예전 `ValidateName` 그대로.
  ⑤ SharpCompress 가 못 여는 정상 ZIP(흘려 쓴 ZipCrypto + 데이터 서술자 — SharpCompress 가 ZIP 으로 알아보지 못해 일집 풀기·목록도 실패하는 기존 엔진 한계)을 '원래부터 손상'이라 알림 →
  ZIP 검사가 구조(`VerifyStructure`)·목록은 맞는데 엔진이 알아보지 못하거나(`ArchiveFactory.IsArchive` 거짓) 방식을 모르면(NotSupported·UnsupportedMethod·UnsupportedFormat)
  '이 압축 파일은 일집이 풀 수 없는 방식으로 만들어져 있어서 고치지 않았어요. 원래 압축 파일은 그대로예요. 다른 프로그램으로 풀어서 다시 압축하면 고칠 수 있어요.'(`ArchiveTester.UnreadableMessage`,
  `OriginalProblem` 이 원본 탓 문구를 고름 — 원본은 멀쩡한데 결과만 못 풀면 '고친 압축 파일을 검사했더니…'). 엔진 자체(풀기)는 바꾸지 않음.
  ⑥ ZIP 고치기 진행 막대가 검사 내내 100% → 검사 몫(풀어 볼 크기 — 암호를 모르면 암호 항목 빼고)을 총량에 넣고, 검사기(`TestEntriesSync(…, onTestedBytes)`)가 풀어 본 바이트로 올림.
  ⑦ 설명 문구('다시 넣기 = 지우기 + 넣기') → '바꿔 넣기'로(`EditOp`·`MainViewModel.EntryOpen` 요약·이 파일 W2-06 본문·`EntryReinsertTests` 설명).
  시험: 새 `ArchiveEditReview2Tests`(21 — 빈 7z 3·이름 그대로 다시 넣기 ZIP·7z 4 + 반디집 `bz t`·이름 검사 9·못 푸는 ZIP 2·결과만 못 풀면 1·ZIP 검사 진행 2)·`EntryReinsertTests` 8(명령줄 겹침 --open·--extract,
  묻는 중 닫기, 넣는 중 닫기 ZIP·7z, 못 쓰는 이름 2, 감시기 가라앉음). **새 시험은 고치기 전 코드로 잠깐 되돌려 모두 실패함을 확인**(교착 2개는 60초 시간 제한). 기대가 바뀐 기존 시험 없음, 푼 Skip 없음.
  계약 테스트: 전체 1644 — 통과 1643·건너뜀 1(I2-10)·**실패 0**(1차 후 1615 에서 새 시험 29개).
  위험·못 본 것: 실제 모달 질문 창 위에서 파이프 명령이 도는 모양·진행 창이 떠 있을 때 X 를 누른 실제 화면은 MANUAL_CHECKLIST 13절(검증 지적 2차 줄). '일집이 풀 수 없는 방식' 판정은 ZIP 만(7z 는 7z.dll 이 못 푸는 방식도
  '손상'으로 남음 — 7z.dll 이 Check 실패 까닭을 주지 않음). 앱을 닫을 때 다른 고치기(넣기·지우기 등)가 도는 중이면 막는 장치는 여전히 없음(이번 지적 범위는 다시 넣기 — 원본은 그대로, 숨김 임시 파일이 남을 수 있음).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-07 — 각각 따로(I2-4)·압축 후 원본 지우기(I3-4)·이름에 날짜(I3-5)·쓸모없는 파일 빼기(I3-6)·설정 기억과 한 화면 압축 창(I2-12)(커밋 전, 배포 안 함)**:
  ① 압축 순서를 '저장 창 → 옵션 창' 두 번에서 **한 화면 창 하나**로: `IUserInteraction.AskCompression(CompressionDialogRequest) → CompressionRequest?`(덧붙임 — `AskCompressionOptions` 는 같은 창을 옵션 칸만 보이게 띄우는 얇은 어댑터로 남김),
  창은 `Views/CompressionOptionsDialog`(제목 '압축하기' — 위에서 아래로 이름·만들 곳[찾아보기]·형식·수준·암호·ZIP 방식/7z 칸·나누기·확인란, 맨 아래 '만들 파일: …' 미리 보기, 가운데 스크롤), 보이기 규칙과 값은
  순수 VM `ViewModels/CompressionDialogViewModel`(7Z 칸은 7Z 만·나누기는 ZIP·7Z·암호는 TAR·GZ·BZ2 에서 끔 — 예전 `CompressionOptionsRules` 그대로, 7Z 는 7z.dll 을 쓸 수 있을 때만 형식에 있음, 이름·폴더·GZ 에 폴더 검사,
  같은 이름이면 창이 [바꾸기 (원래 파일은 없어져요)][새 이름으로 만들기 (이름 (2).zip)][취소 (이름 다시 쓰기)] — 기본 [새 이름]). 처음 값은 새 설정 `AppSettings.Compress`(`compress.*` — 마지막 형식·수준·ZIP 방식·솔리드·
  이름 숨기기·나누기·각각 따로·날짜 모양·빼기와 목록·시험·원본 지우기·마지막 폴더, 모르는 필드 보존, **암호 칸 없음**), [확인]이면 그 형식에 맞는 값만 기억해 한 번 저장(`MainViewModel.Compress.cs`).
  만들기는 새 `Services/CompressFlow`(진행 창 하나, 실패·취소면 반쪽 파일을 지우고 덮어쓰려던 파일은 그대로 — 예전 규칙 그대로 옮김). 하나로 만들면 예전처럼 결과를 열어 보이고 안내를 한 창에.
  ② I2-4 각각 따로(원본 둘 이상일 때): 원본마다 압축 하나(파일은 확장자 뗀 이름·폴더는 폴더 이름 — GZ·BZ2 는 `보고서.xlsx.gz`), 있던 이름은 '이름 (2)', 진행 '2/5 · 이름', 끝에 요약 창(`OperationSummary.Kind = Compress` —
  '5개를 모두 압축했어요.'·'압축하지 못한 것:'), 만든 원본은 대기 목록에서 빠지고 못 만든 것은 남음. ③ I3-5 날짜: `_` + 오늘(`IClock` — 시험은 가짜 시계) `yyyyMMdd`·`yyyy-MM-dd`·`yyMMdd`, 이미 그 날짜가
  (숫자에 붙지 않은 채로) 있으면 안 붙임(`CompressNaming`). ④ I3-6 빼기(`Services/FileFilter.cs` `PatternFileFilter` — 이름 단위·대소문자 무시·`*` `?`·끝 `/` 는 폴더): 기본 켬, 기본 목록 Thumbs.db·desktop.ini·.DS_Store·._*·__MACOSX/·~$*·*.~tmp,
  **폴더 안에서만**(직접 고른 파일·폴더는 담음), `SourceCollector.Collect(…, filter)` → `CompressResult.Excluded`(ZIP·7Z·TAR), '쓸모없는 파일 N개는 뺐어요: …', 목록은 압축 창 '빼기 목록 고치기'·☰ ▸ 설정 '압축하기'.
  ⑤ I3-4 원본 지우기(기본 끔, ZIP·7Z·TAR 만): 만든 압축을 새 `ICreatedArchiveTester`(모든 항목을 풀어 보며 — 7z 는 7z.dll Check, 나눈 세트도)로 시험 → 통과한 것의 원본만, 원본을 다시 모아 **이름·크기가 압축 목록과 맞을 때만**
  `ui.Choose('원본 N개(12.3MB)를 휴지통으로 옮길까요?', [옮기기, 그대로 두기], 기본 1)` → 묻는 사이 바뀌었는지 다시 맞춰 본 뒤 `IRecycleBin.Send`(휴지통만 — 영구 삭제 없음). 지우지 않는 경우와 까닭: 시험 실패
  ('시험에서 문제가 있어 원본을 지우지 않았어요'), 휴지통 없는 곳(UNC·고정 디스크 아님 — '네트워크 드라이브나 USB 처럼…'), 뺀 파일이 든 폴더, 압축 파일을 원본 폴더 안에 만듦, 압축과 맞지 않음, GZ·BZ2. 휴지통 실패는 경고만.
  '다 만들고 시험하기'만 켜면 시험 결과만 알림. 실제 휴지통(`RecycleBin`)은 폴더 크기도 재고(`IsRecyclable`), 옮긴 뒤 폴더가 남았는지도 봄. 헤드리스(`--extract-to`)는 손대지 않음(설정을 읽지 않음).
  시험: 새 `CompressFlowTests`(50)·`CompressDialogRulesTests`(16), 가짜 창 `FakeUserInteraction.AskCompression`(답이 없으면 예전 `SavePath` + `CompressionOptions` 답을 한 화면 창의 답으로 — 예전 압축 시험 그대로 통과)·
  `TestServices.Clock`(`FakeClock`)·`TestServices.CompressTester`(`FakeCompressTester`). 기대가 바뀐 기존 시험 없음, 푼 Skip 없음. 계약 테스트: 전체 1710 — 통과 1709·건너뜀 1(I2-10)·**실패 0**(W2-06 후 1644 에서 새 시험 66개).
  위험·못 본 것: 실제 창 모양·칸 순서·Tab·작은 화면, [찾아보기] 폴더 창, 진짜 휴지통으로 폴더 옮기기·되살리기, 네트워크 드라이브·USB 판정, 회사 PC DLP·OneDrive 폴더에서 시험·휴지통, 엑셀이 만든 `~$` 파일,
  7z.dll 이 막힌 PC 는 MANUAL_CHECKLIST 14절. 만들 수 있는 형식은 예전과 같은 ZIP·7Z·TAR·GZ·BZ2(tar.gz 만들기는 없음). W2-06 커밋에 빠진 `EngineStreams.cs` 4줄·`ArchiveEditReviewTests.cs` 는 그대로 둠(위 '현재 진행 단계').
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-07 검증 지적 수정(1차)(커밋 전, 배포 안 함)** — 지적 9개 확인, 7개 고침·1개(HEAD 빌드)는 커밋 몫·1개(원본 휴지통 기억)는 결정:
  ① I2-12 '만들 곳' 처음 값: 새 설정 `compress.folderChoice`(`CompressFolderChoice` — `SourceFolder`(기본, 예전과 같음)·`LastFolder`·`FixedFolder`)·`compress.fixedFolder`,
  `CompressSettings.InitialFolder(원본 폴더)`(고른 폴더가 없으면 원본 폴더)를 VM 이 처음 값으로, 목록은 처음 값·원본 폴더·마지막 폴더·정해 둔 폴더(있는 것만). 설정 창(☰ ▸ 설정 '압축하기')에 라디오 셋 + [찾아보기]
  (`CompressSettings.CheckFixedFolder` — 비었거나 없는 폴더면 막음). 설정 창은 가운데가 스크롤(작은 화면).
  ② 시험 단계 알림이 진행 창에 안 닿음: `ArchiveProgress.IsStage`(덧붙임) — `ThrottledArchiveProgress` 가 늘 통과시킴, `ProgressDialog` 는 크기 0 인 단계면 움직이는 막대·크기 줄 비움. `CompressFlow` 는 '만든 압축 파일을 시험하는 중'
  (여러 개면 '2/3 · 이름 — 시험하는 중')·'원본을 살펴보는 중'·'원본과 압축 내용을 맞춰 보는 중'·'원본을 휴지통으로 옮기는 중'을 단계 알림으로(`BatchProgress` 도 IsStage 를 넘김).
  ③ GZ·BZ2 + 날짜: `CompressNaming.WithDate(…, archiveFormat)` 가 GZ·BZ2 면 원래 확장자(점 뒤 영문·숫자 1~10자) 앞에 날짜, `UniqueTarget` 도 '(2)' 를 그 앞에(`보고서_20261013 (2).xlsx.gz`).
  ④ 원본 지우기 확인: 압축하기 직전 원본 파일마다 크기·수정 시각(`CompressOutcome.SourceStamps` — 같은 빼기 목록으로 모음)을 재 두고 `CheckDeletable` 이 압축 목록(이름·크기)과 함께 그것과도 맞춰 봄(같은 크기로 내용만 바뀐 파일 거름).
  ⑤ 화면 굳음: 재기·묻기 전 맞춰 보기(`FindDeletableAsync`)는 진행 창을 띄운 채 `Task.Run`, 묻기(`Choose`)만 UI 스레드, [옮기기] 뒤 다시 맞춰 보기·`IRecycleBin.Send` 는 새 진행 창을 띄우고 `Task.Run`
  (보내기 전 [취소]면 '취소해서 원본은 지우지 않았어요.'). 진짜 `RecycleBin.Send` 는 작업 스레드(MTA)에서 불리면 원래대로 STA 스레드를 따로 만듦.
  ⑥ 폴더와 그 안 파일을 같이 고름: 후보 중 다른 후보(폴더) 안의 것·같은 원본 두 번을 뺌(묻는 수·크기 한 번, 보내는 목록에 부모만).
  ⑦ 원본 그 자체 덮기(예전부터): `CompressFlow` 는 만들 압축이 원본이면(`SamePath`) 무엇을 골랐든 '이름 (2)', VM `TargetIsSource` → 창은 [바꾸기] 없이 '‘a.zip’은(는) 지금 압축하려는 원본이에요…' [새 이름으로][취소], `BuildRequest` 도 덮어쓰기 끔.
  ⑧ '원본을 휴지통으로' 기억: `RememberCompressSettings` 가 더는 기억하지 않음 — 처음 값은 설정 창 "압축 창의 '다 만든 뒤 원본을 휴지통으로'를 켠 채로 열기"(기본 끔)에서만.
  ⑨ HEAD 만으로 빌드 안 됨: 맞음 — 코드는 그대로, W2-07 커밋에 `Iljip/Services/EngineStreams.cs`·`Iljip.Tests/Contract/ArchiveEditReviewTests.cs` 를 같이 넣을 것(위 '현재 진행 단계').
  I2-12 '해제 방식 기억'은 W2-05 `extract.*` 가 맡고 메인 창 풀기의 대상 폴더 기억·최근 폴더는 I2-13 몫으로 남김.
  시험: 새 19개(`CompressFlowTests` 15·`CompressDialogRulesTests` 3·`ThrottledArchiveProgressTests` 1), 기대가 바뀐 기존 시험 없음(`창은_VM_규칙을_쓴다` 에 소스 확인 줄만 더함), 푼 Skip 없음.
  계약 테스트: 전체 1729 — 통과 1728·건너뜀 1(I2-10)·**실패 0**(W2-07 의 1710 에서 새 시험 19개).
  위험·못 본 것: 진짜 진행 창의 움직이는 막대·설정 창 모양·[찾아보기], 진짜 휴지통으로 2만 개 폴더 옮기기(굳지 않는지), 메모장으로 같은 글자 수 고치기는 MANUAL_CHECKLIST 14절(검증 지적 줄).
  수정 시각까지 되돌리는 프로그램이 고친 원본은 알아채지 못함(휴지통에서 되살릴 수 있음). 원본을 재는 일이 하나 늘어(원본 지우기를 켰을 때만) 큰 폴더는 압축 전에 조금 더 걸림.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-07 검증 지적 수정(2차)(커밋 전, 배포 안 함)** — 지적 7개 확인, 5개 + '범위·과정'의 (c)·(d) 고침, 1개(두 일집 동시 같은 이름)는 고치지 않음:
  ① (회귀) TAR 파일을 GZ·BZ2 로: `CompressFlow` 가 만들 파일 이름의 가장 긴 확장자로 처리기를 골라 'backup.tar' → 'backup.tar.gz' 가 압축된 TAR 처리기(풀기만)로 가 '압축된 TAR 만들기는 아직 지원하지 않아요'로
  실패하던 것 — 이제 고른 형식으로 고름(새 `ArchiveServiceLocator.ForExtension(".gz")` — 확장자 하나 그대로, 원본 맞춰 보기의 목록 읽기도). 만든 압축 시험기(`CreatedArchiveTester.ServiceFor`)도 마지막 확장자로
  (GZ 는 GZ 로 시험 — 압축된 TAR 처리기로 시험하면 안쪽 TAR 를 임시 폴더에 다 풀고 또 풀어 내용의 두 배를 씀). 열기·풀기 라우팅은 그대로.
  ② 각각 따로 시험 중 진행 막대가 100% → 0·33·66% 로 되돌아감: 시험 단계 알림은 순번을 글자로만('만든 압축 파일을 시험하는 중 (2/3) — 이름') 보내 움직이는 막대. 진행 창 막대 규칙을 `ProgressDialog.BarValue`(순수)로 빼서 시험에서 씀.
  ③ XAML 만들기 시험: 새 `CompressDialogXamlTests`(압축 창·옵션 칸만 보이는 창·설정 창을 띄우지 않고 만들기 — 양쪽 묶기·칸 모양·묶기 오류 0) + 틀 `TestSupport/WpfAppScope`
  (앱 객체를 만들고 되돌릴 때 WPF 의 '한 번 만들었음' 표시 `_appCreatedInThisAppDomain` 도 되돌림 — `MainWindowEditUiTests` 와 같은 순차 묶음에서 둘 다 앱 객체를 만들 수 있게).
  ④ [취소]가 원본 훑기를 못 멈춤: `SourceCollector.Collect(…, 취소)`(폴더마다 — 엔진 ZIP·7Z·TAR 도 넘김), `CompressFlow.TakeStamps`(이제 internal, 취소는 삼키지 않음)·`CheckDeletable`(파일마다)·목록 읽기가 취소를 봄.
  맞춰 보는 도중 취소면 묻지 않고 '취소해서 원본은 지우지 않았어요.'(예전엔 끝까지 훑고 '옮길까요?'를 물었다).
  ⑤ 휴지통 일부 실패: `RecycleResult` 가 실패여도 없어진 원본은 옮긴 것으로 — '원본 N개를 휴지통으로 옮겼어요.' + '원본 M개는 휴지통으로 옮기지 못해 그대로 두었어요: 이름…', `Recycled` 에도 옮긴 것만.
  ⑦(c) `CommandLineParser` '--compress' 주석 '한 화면 압축 창부터'. ⑦(d) 시험만 켜고 시험 취소: '시험을 취소했어요(3개 중 1개만 시험했어요). 만든 압축 파일은 그대로 있어요.'(`CompressFlow.TestCancelledMessage` — '문제없어요'는 안 함),
  각각 따로 상태 줄 '압축 완료: 3개 (시험은 취소)'.
  고치지 않음 ⑥ 두 일집이 같은 순간 같은 이름: 맞지만 예전 저장 창 흐름부터 같은 방식(`File.Create`). 막으려면 모든 엔진 쓰기를 '없을 때만 만들기'로 + 실패 정리가 남의 파일을 지우지 않게 해야 하고,
  빈 자리 파일을 먼저 만드는 방법은 회사 PC DLP(PCFILTER)가 막 닫은 파일을 잠깐 잠가 바로 다시 열다 실패할 위험(교훈 'DLP 연속재기록 레이스')이라 하지 않음 — MANUAL_CHECKLIST 14절 '알려진 점'에 적음.
  ⑦(a) I2-12 '해제 방식 기억'·(b) HEAD 빌드는 1차에 적은 대로(W2-05 `extract.*` / 커밋 때 `EngineStreams.cs`·`ArchiveEditReviewTests.cs` 같이 넣기).
  시험: 새 18개(`CompressFlowTests` 15 — GZ·BZ2 로 TAR 6·처리기 고르기 1·진행 막대 2·훑기 취소 1·맞춰 보는 도중 취소 1·휴지통 일부 2·시험 취소 2, `CompressDialogXamlTests` 3).
  동작을 고친 새 시험은 고치기 전 코드로 잠깐 되돌려 실패함을 확인(휴지통 '하나도 못 옮김' 줄은 예전 문구 그대로라 통과가 맞음). 기대가 바뀐 기존 시험 없음, 푼 Skip 없음.
  가짜에 더한 것: `FakeUserInteraction.CancelWhen`(조건으로 진행 창 [취소]), `FakeRecycleBin.OnIsRecyclable`·`OnSend`.
  계약 테스트: 전체 1747 — 통과 1746·건너뜀 1(I2-10)·**실패 0**(1차 후 1729 에서 새 시험 18개).
  위험·못 본 것: 진짜 진행 창에서 시험 중 움직이는 막대·큰 폴더 훑기 중 [취소]·엑셀로 연 원본의 휴지통 일부 실패는 MANUAL_CHECKLIST 14절(검증 지적 2차 줄). 속이 TAR 가 아닌 '.tar' 파일을 GZ 로 만든
  '.tar.gz' 는 일집으로 열면 TAR 로 펼치려 해 제대로 안 보일 수 있음(만들기·시험은 됨 — 열기 라우팅은 일부러 그대로 둠). 엔진의 원본 모으기가 취소를 받게 돼 그 사이 취소는 예전보다 빨리 '취소'로 끝남(정리 규칙은 같음).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-09 — 맥 NFD·일본어·중국어 이름(I2-10)·풀기 창(I2-13)·찾기·Backspace·선택 합계·트리 너비(I2-14)(커밋 전, 배포 안 함)**:
  시작 전: HEAD(5a95fa6)가 빌드되지 않아(W2-07 검증 2차 몫이 W2-08 stash 로 쓸려 감) 다섯 파일만 stash 에서 읽어 되살림 — 위 '현재 진행 단계'(커밋 때 같이 넣을 것).
  ① I2-10 이름: 새 `Services/EntryNames.cs`(NFC·고르기 열쇠) — 목록 이름(SharpCompress·7z.dll `SevenZipEngine.BuildItems`·EGG/ALZ), 디스크에 쓰는 이름(`EntryPaths.ToSafeRelative`), 고르기 비교 양쪽(`EntrySelector`·EGG),
  고치기 열쇠(`EditPlanner.Normalize` → `ArchiveTester`·7z 고치기도 같이)를 NFC 로. 그대로 둔 바이트는 그대로 옮김(ZIP 고치기). NFC 로 같아지는 두 항목은 같은 실행 안 겹침 규칙대로 둘째가 '이름 (1)'.
  새 `Services/ArchiveNameEncoding.cs`(`ArchiveNameEncodingDetector`·`NameDecoder`·`NameCollector`): UTF-8 표시 없는 옛 방식 이름 전체로 압축 하나에 방식 하나 — 한국어 먼저.
  ★ 설계의 'CP949 로 엄격히 풀리고 한글 1자 이상이면 한국어'는 실측에서 틀림: Shift-JIS 가나·한자는 CP949 확장 음절로('テスト資料' → '깻깥깸랍됨'), GBK 간체는 완성형 한글+한자 섞임으로('测试文件' → '꿎桿匡숭') 다 풀린다 —
  그래서 KS X 1001 완성형 음절·자주 쓰는 음절(약 600자) 비율·한자/확장 음절 수로 가르고, 그다음 Shift-JIS(가나·한자 80% 이상), GBK(GB2312 한자 60% 이상), 모두 아니면 예전 규칙(이름마다 — CP437 보존).
  실험(말뭉치, 이름 하나씩): 한국어 이름 192개 중 190개 한국어(틀린 것은 '韓國語 교재' 처럼 한자가 많은 이름), 일본어 65/65, 중국어 이름 하나씩은 31/61 만 중국어(나머지는 한국어 — 짧은 이름은 CP949·GBK 둘 다 그럴듯, '한국어 우선' 위험 그대로),
  압축 단위(이름 여럿)는 모두 맞음. 바이트 모양을 먼저 보고 맞을 때만 엄격히 풀어 예외 흐름 비용을 줄임(8a04404 교훈).
  SharpCompress 로 여는 길(ZIP·TAR·압축된 TAR·7z.dll 이 없을 때의 RAR, ZIPX·RAR4 의 이름 대조 `NamesFromSharpCompress`)은 처음 열 때 이름을 모아 정하고, 정한 방식으로 풀어도 같으면(한국어·UTF-8 대부분) 그대로,
  다르면 한 번 더 엶(`SharpCompressArchiveServiceBase.OpenWithNameDetection`). ZIP 중앙 디렉터리를 직접 읽는 암호 판정(`ZipPasswordProbe`)·압축 고치기(`ZipRawRewriter`)도 같은 규칙(`Services/ZipEntryNames.cs` — 이름·항목 주석·압축 주석으로 정하고 0x7075 이름 우선, SharpCompress 와 같게).
  `KoreanFileNameDecoder` 는 그대로(정하지 않음일 때 씀).
  ② I2-13 풀기 창: 새 `Views/ExtractOptionsDialog.xaml`·순수 VM `ViewModels/ExtractOptionsViewModel.cs`, 모델 `Models/ExtractTarget.cs`(`ExtractTargetRequest`·`ExtractTarget(Folder, MakeFolder, DeleteArchiveAfter, OpenFolderAfter)`),
  `IUserInteraction.AskExtractTarget`(덧붙임 — `WpfUserInteraction`·가짜 둘 다). 툴바 [압축 풀기]·Ctrl+E·선택 항목 풀기·`--extract`(하나·여럿)·열기 창에서 여럿 고르기가 폴더 고르기 창 대신 씀(새 `MainViewModel.ExtractDialog.cs`).
  최종 풀 곳 = 폴더 만들기면 `ArchiveNaming.UniqueFolder(고른 곳, FolderNameFor(압축))`, 아니면 고른 곳(여럿이면 `NamedFolderIn`/`Into`). 못 풀면 만든 폴더에 파일이 없을 때 지움.
  설정 덧붙임: `extract.makeFolder`(기본 켬·기억)·`extract.recentFolders`(맨 앞·대소문자 무시로 겹치지 않음·10개, 창에는 지금 있는 것만)·`extract.dialogOpenFolderAfter`(기본 꺼짐·기억 — 우클릭 풀기의 `openFolderAfter` 와 따로)·`ui.treeWidth`.
  휴지통은 창의 처음 값이 설정(`deleteArchiveAfter`)이고 창에서 켠 것은 이번에만. 다 풀면 고른 곳을 최근 맨 앞에 — 바뀐 것이 있을 때만 끝에 한 번 저장. 창 없이 풀기·우클릭 '여기에/이름 폴더에'는 그대로(설정 안 읽음).
  가짜 `ExtractTargetAnswer` 가 없으면 예전 `FolderToPick` 답으로(하나는 그 폴더에 바로, 여럿은 '이름' 폴더마다) — 기존 화면 시험이 그대로 통과. '모두 풀까요?' 문구 괄호만 '(다음 창에서 풀 곳을 골라요…)'.
  ③ I2-14 둘러보기(새 `MainViewModel.Browse.cs`): 이름으로 찾기(`SearchText` — 200ms 기다림, NFC·소문자 이름 목록을 처음 찾을 때 한 번 만듦, 모든 폴더의 파일·폴더, '경로' 열 `ArchiveEntry.FolderPath` — 정렬 열에도 넣음,
  Esc·✕·트리에서 폴더 고르기·찾은 폴더 두 번 누르기로 지움, 다른 압축을 열면 지움, 고친 뒤 다시 읽으면 새 목록에서 다시 찾음). 10만 개: 열기 1.6초, 첫 찾기(이름 목록 만들기 포함) 87ms, 찾기 약 5ms.
  Backspace = `UpCommand`(위 폴더, 찾는 중이면 찾기 지우기 — 이름 고치는 중엔 그 칸에), Ctrl+F = 찾기 칸. 상태 줄 오른쪽 '선택: 3개 · 12.4MB'(폴더는 트리로 그 아래 전부 — 한 번 계산해 둠, 크기를 모르는 항목은 '약').
  트리|목록 사이 `GridSplitter`(트리 칸은 압축을 볼 때만 — 너비는 `ui.treeWidth`, 기본 260, 창을 닫을 때 바뀌었으면 한 번 저장). 새 폴더 이름 제안은 찾는 중이면 트리의 지금 폴더로.
  시험: 새 66개(`NameEncodingTests` 28·`ExtractDialogTests` 19·`ExtractDialogXamlTests` 1·`BrowseTests` 17·`BrowseXamlTests` 1) + Skip 해제 `BandizipFixtureTests.NFD_이름이_NFC로_보이고_NFC로_풀린다`(4행), `Known.I2_10` 삭제.
  기대가 바뀐 기존 시험 없음(`SortingTests` 의 '목록 열은 모두 정렬할 수 있는 열' 계약에 맞춰 '경로' 열을 정렬 열로 넣음). 가짜에 더한 것: `FakeUserInteraction.AskExtractTarget`·`ExtractTargetAnswer`, `RawTar.Entry.RawName`.
  계약 테스트: 전체 1816 — 통과 1816·건너뜀 0·**실패 0**(되살린 기준 1747 에서).
  위험·못 본 것: 진짜 맥·일본어·중국어 Windows 산출물·창 모양·찾은 결과 수만 개일 때 WPF 목록 그리기는 MANUAL_CHECKLIST 15절. 짧은 중국어 이름 한두 개뿐인 압축은 한국어로 보일 수 있음(손으로 고르는 메뉴는 안 만듦 — 향후 작업).
  7z.dll 이 OEM 코드 페이지로 읽는 LZH·CAB·ISO 이름과 ALZ 이름은 다시 판별하지 않음(한국어 판별 그대로). 시간을 재는 `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…` 가 이번 첫 전체 실행에서 3084ms 로 한 번 실패(W2-06 때와 같은 바쁜 PC 현상 — 따로 돌리면 통과, 7z 만들기 길은 손대지 않음).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-14 W2-09 검증 지적 수정(1차, 커밋 전, 배포 안 함)**: 지적 7개를 직접 확인 — 모두 맞아 고침(파일·폴더 이름 겹침의 '영어 IOException'은 화면에서는 이미 쉬운 말이었고, 멈추고 파일이 남는 것만 맞음).
  ① (major) 한자가 많은 한국어 이름이 중국어로 읽힘: `ArchiveNameEncoding.cs` — GBK 로 읽은 글자에 사용자 정의 글자(U+E000~F8FF)가 있거나 GB2312 2급 한자(머리 D8~F7)가 한자의 25%를 넘으면 중국어 아님
  (KS X 1001 한자는 소리 차례라 '사~힐' 소리 약 4분의 3이 GBK 로는 2급·사용자 정의 구역 — 실측: 진짜 중국어 이름의 2급 비율 0.1~0.3%, 한국어 한자 66%+PUA 10%), `LooksKorean` 에 '확장 음절 없고 자주 쓰는 한글 2자 이상·80% 이상' 줄,
  중국어·일본어가 아니고 CP949 완성형으로 다 풀리면 한국어(한자뿐인 '會議錄.hwp' 도 바르게 — 예전 규칙은 CP437). 자주 쓰는 음절에 '폴·플·클·캔·및·워·텍·딩·런'(이 PC 실제 이름에서 자주 빠지던 것 — '새 폴더'·'샘플'·'스캔').
  실험(scratch `encprobe`): 이 PC 실제 이름 53,704개(폴더 6,286개) 예전 규칙보다 나빠진 이름 0, 한자 섞인 한국어 모의 압축 6만 개에서 예전 규칙보다 나빠진 이름 18,828 → 0, 중국어 압축이 한국어로 보이는 비율 이름 1개 33.0→34.3%·3개 10.5→11.4%, 일본어 그대로 100%.
  ② (major) 풀기 창 처음 풀 곳: 새 `Services/TempLocations.cs`(`AppServices.TempLocations` — 일집 임시 폴더·%TEMP%·LOCALAPPDATA\Temp·INetCache·Windows\Temp)면 바탕 화면(없으면 최근, 그것도 없으면 빈 칸),
  그런 곳은 최근 푼 곳에 넣지도 보이지도 않음, 일집 임시 폴더는 창에서 골라도 "일집 임시 폴더에는 풀 수 없어요…"(`ExtractOptionsViewModel.CheckPath`). 시험은 가짜 위치(`TestServices(…, tempLocations)` — 기본은 시스템 임시·바탕 화면 없음: 픽스처가 진짜 %TEMP% 아래라서).
  ③ NFC 로 같은 두 파일 고치기: `EditPlanner.Delete` 는 같은 키를 고른 수만큼 받아 같은 이름 파일 수보다 적으면 멈춤(쉬운 말), `Rename` 은 같은 이름 파일이 둘 이상이면 멈춤, 화면(`DeleteEntriesAsync`)은 고른 줄마다 키 하나(예전 같은 키를 하나로 줄임).
  덤(같은 곳): 찾은 결과에서 폴더와 그 안 파일을 함께 골라 지우면 '찾지 못했어요'로 멈추던 것 — '찾지 못함'을 지우기 전에 한꺼번에 봄.
  ④ 찾은 폴더에서 Enter·우클릭 '열기': `OpenEntryAsync` 가 `NavigateToFolder` 대신 `GoToFolder`(찾기 지움).
  ⑤ 파일 뒤 같은 이름 폴더: `ExtractionLedger.EnsureDirectory` 가 만들 자리의 '이번 해제가 만든 파일'을 `OutputFiles.MoveNew` 로 '이름 (1)' 로 비켜 두고 장부·같은 이름 범위를 고침(사용자 파일은 그대로 — 예전처럼 실패).
  ⑥ 끊긴 NAS: 새 `Services/FolderProbe.cs` — 네트워크 폴더(UNC·네트워크 드라이브)는 창이 열릴 때(`VisibleRecentFolders`)·칠 때(`PreviewText`)·[찾아보기] 처음 위치에서 디스크를 보지 않고, [풀기] 때 한 번 5초까지(`TryBuildResult` — 확인 한 번, 모래시계).
  ⑦ 모으는 목록의 폴더 선택 합계: 폴더만 '선택: 1개', 섞이면 '선택: 2개 · 파일 10B'.
  시험: 새 27개(`NameEncodingTests` 20·`ExtractDialogTests` 4·`BrowseTests` 2·`MainViewModelEditTests` 1), 기대가 바뀐 기존 시험 없음. 계약 테스트: 전체 1843 — 통과 1843·건너뜀 0·**실패 0**.
  위험·못 본 것: 진짜 Outlook 첨부·NAS 끊김·창 모양은 MANUAL_CHECKLIST 15절 '(검증 지적 1차)' 줄. 짧은 중국어 이름의 한국어 오판(한국어 우선)은 조금 늘었음(위 수치). 2급 한자가 많은 중국어 사람 이름 한두 개뿐인 압축('王鑫.jpg')은 이제 한국어(한자)로 보임.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-14 W2-10 — 자동 풀림 EXE 만들기(I3-1)(커밋 전, 배포 안 함)**: 받는 사람에게 일집·반디집이 없어도 두 번 눌러 풀리는 exe. **방식 결정 — 7-Zip 자동 풀림 모듈 대신 일집 자체 스텁**(실측 비교):
  | | 7-Zip 모듈(SharpSevenZipSfx 안에 든 것 — 꺼내서 잼) | 일집 자체 스텁(`Iljip.SfxStub`) |
  |---|---|---|
  | 판·날짜 | 7z.sfx·7zCon.sfx·7zSD.sfx = 7-Zip 4.65(2009), 7zxSD_All(x86·x64) = 7zSFX 1.4.0.1704(Oleg Scherbakov, 2005~2010) — 7zS.sfx·나머지 7zxSD_* 는 판을 재지 않음 | 이번에 만듦(.NET Framework 4.6.2 WinForms) |
  | 크기·CPU | 7z.sfx 140,288 B·7zCon 139,776·7zS 133,120·7zSD 98,304(모두 x86 만), 7zxSD_All 103,424(x86)·153,088(x64) | 58,368 B(Release)·61,952 B(Debug), AnyCPU |
  | 서명 | 잰 다섯 개(7z·7zCon·7zSD·7zxSD_All x86·x64) 모두 서명 없음(NotSigned) | 서명 없음 |
  | 화면 | 영어(7zSD·7zxSD 는 설정 파일로 일부 문구만 바꿈) | 쉬운 한국어(풀 위치·찾아보기·암호·진행·같은 이름 묻기·폴더 열기) |
  | 담는 형식 | 7z 만(일집 '다 만들고 시험하기'·한글 ZipCrypto 등 ZIP 규칙과 따로) | 일집 ZIP 그대로(수준·AES-256·ZipCrypto·빼기 목록 — 같은 엔진) |
  | 라이선스 | 7-Zip SFX 는 LGPL(배포 고지 필요), 7zxSD 수정판의 라이선스 원문은 **확인 못 함** | 우리 코드뿐(서드파티 없음) |
  | 고쳐 쓸 수 있나 | 바이너리를 그대로 씀 — 손상·경로 공격·반쪽 파일 동작을 우리가 정할 수 없음. SharpSevenZipSfx 의 Custom 모듈 경로는 소스상 동작 안 함 | ZipSlip·CRC/HMAC·잘림·반쪽 파일 규칙을 우리 계약으로 시험 |
  옛 7-Zip SFX 모듈(16.02 이하)에 DLL 바꿔치기 약점이 보고됐다고 알고 있으나 CVE 번호·해당 모듈 판은 **확인하지 않았다**(근거 아님 — 위 표만으로 결정).
  스텁을 .NET Framework 4.6.2 로 한 까닭: Windows 10 1607 이후·Windows 11 에 기본으로 들어 있어 받는 PC 에 따로 깔 것이 없음(.NET 8 은 받는 PC 에 없을 수 있음 — 자체 포함이면 60MB+).
  4.6.2 에 없는 API(ValueTuple·Span·ReadExactly 등)는 쓰지 않았고 참조 어셈블리는 NuGet `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3(빌드 PC 에 설치 불필요).
  ① 모양: `[스텁 exe][ZIP][꼬리 24바이트 = 'ILJIPSFX' + 데이터 시작 8 + 데이터 길이 8]`(`Services/SfxBuilder.cs` — 만들 exe 옆 숨김 임시 파일에 ZIP 을 다 만들고 다른 임시 파일에 이어 쓴 뒤에만 '이름.exe' 로 옮김, 실패·취소면 임시 파일을 지움·덮어쓰려던 exe 그대로,
  원본 폴더 안의 만들 exe 자신은 담지 않음, 나누기 없음). 스텁이 없으면 `SfxStubMissingException`(쉬운 말 — '보안 프로그램이 지웠을 수 있어요') + 형식 목록에서 빠짐.
  ② 스텁(`Iljip.SfxStub/` — `Program`·`SfxArgs`·`SfxPayload`·`SfxExtractor`·`ExtractForm`·`SfxMessages`): 꼬리·첫 서명·끝 레코드(주석 길이까지)·중앙 디렉터리를 먼저 확인하고, 이름을 모두 미리 검사('..'·'.'·절대 경로·드라이브·':' 스트림·끝 점/공백·장치 이름·겹치는 이름 — 하나라도 있으면 아무것도 풀지 않음),
  암호는 무엇을 만들기 전에 정함(UTF-8·CP949 두 후보 — AES 는 확인값, ZipCrypto 는 머리 바이트), 풀 곳 안의 숨은 임시 폴더(`.일집-임시-…`)에 다 풀며 크기·CRC(AES 는 HMAC)를 맞춘 뒤에만 옮김.
  옮기는 중 실패하면 되돌림(덮어쓴 파일은 임시 폴더의 백업에서 되살림 — 되살리지 못하면 임시 폴더를 지우지 않고 남김), 새로 만든 풀 곳 폴더도 지움.
  무인 모드 `/s /d:<폴더> [/p:<암호>]`: 창을 하나도 띄우지 않음(WER 오류 창까지 끔), 0 성공·1 실패·2 인자 오류, 표준 오류에 UTF-8 한국어 한 줄, 같은 이름은 덮어씀(다른 파일은 그대로). 실측: 인자 오류로 끝나는 데 약 0.4초(.NET Framework 시작), SampleTree 를 만들고 풀기까지 약 0.8초.
  화면 모드: 처음 풀 곳 = 바탕 화면\이름(있으면 '이름 (2)'), 같은 이름이면 [덮어쓰기][건너뛰기][취소] + '남은 파일도 똑같이'(기본 건너뛰기) — **실행해 보지 않음**(창을 띄우는 시험 금지 — MANUAL_CHECKLIST 16절).
  ③ 암호 계산 공유: `WinZipAes.cs` 는 4.6.2 에 없는 API 를 써서 그대로 링크할 수 없어, 순수 계산만 `Iljip/Services/Shared/WinZipAesCore.cs`(키 유도·확인값·AES-CTR·HMAC)·`ZipCryptoCore.cs`(ZipCrypto 키·머리 확인·CRC-32)로 떼어 본체(`WinZipAes`·`ZipPasswordProbe`)와 스텁이 같은 파일을 씀(기존 AES·ZipCrypto 시험이 그대로 지킴).
  ④ 일집 안: `SfxArchiveService` 가 우리 꼬리를 먼저 보고 그 범위만, 아니면 서명으로 찾은 ZIP 위치부터 `ZipArchiveService` 에 맡김(예전엔 SharpCompress 로 바로 — 한글 ZipCrypto 암호·AES 인증값을 못 봄. 반디집 ZIP 자동 풀림도 같이 좋아짐),
  `ArchiveSniffer.IljipSfxData`, `ArchiveServiceLocator.ForExtension(".exe")` = 자동 풀림 처리기(확장자 표에는 넣지 않음 — 열기·우클릭에서 .exe 를 확장자로 압축으로 치지 않는 규칙 그대로), '다 만들고 시험하기'(`CreatedArchiveTester`)도 같은 길.
  ⑤ 화면: `CompressionFormat.Sfx`('자동 풀림 EXE (프로그램 없이 풀림 · 메일로는 막힐 수 있음)' — 형식 목록 맨 끝, 스텁이 있을 때만), 형식 칸 바로 아래 주황 안내 '회사 메일은 실행 파일(.exe)을 막는 경우가 많아요. 받는 사람이 못 받을 수 있어요. 메일로 보낼 거면 ZIP을 권해요.'(늘 보임),
  ZIP 암호 방식 칸 보임·나누기 숨김·한글 암호 호환 경고 안 함(스텁이 두 방식을 다 맞춰 봄). 만들기 직전 한 번 묻기(`IUserInteraction.ChooseWithCheck` 덧붙임 — `ChoiceDialog` 확인란): [그래도 만들기][ZIP으로 만들기](같은 이름·같은 옵션의 ZIP — 있으면 '이름 (2).zip')[취소] + '다시 묻지 않기'(설정 `sfx.skipMailWarning`, ☰ ▸ 설정에서 되살림).
  자동 풀림은 마지막 형식으로 기억하지 않음(다음 창이 exe 로 열려 메일로 보내다 막히지 않게 — 설계에 없던 판단).
  ⑥ 빌드: `Iljip.csproj` 가 스텁 프로젝트를 참조만 하고(어셈블리 참조 아님) 그 exe 를 `sfx\Iljip.SfxStub.exe` 로 출력·발행 폴더에 복사(단일 파일 발행에서도 따로 — 임시 폴더 발행으로 확인). 산출물은 `D:\Python\일집-build\dotnet\Iljip.SfxStub\…`(저장소 `bin` 없음).
  시험: 새 57개(`SfxCreateTests` 35 — 만든 exe 를 **무인 모드로만** 실제로 돌림, 도는 동안 MainWindowHandle 0·60초 제한, exe 실행이 막힌 PC 는 `SfxStubFact` 로 건너뜀 / `SfxCompressFlowTests` 19 / `CompressDialogXamlTests` 3),
  기대가 바뀐 기존 시험 2개(`CompressFlowTests.압축_창에_넘기는_것`·`CompressDialogRulesTests.칠z_dll이_없으면_7Z를_고를_수_없다` — 형식 목록 끝에 Sfx 가 더해짐), 푼 Skip 없음.
  일부러 고장 내 보기: 스텁의 CRC 확인을 끄면 `내용이_깨지면_…(null)` 이, 메일 질문을 건너뛰게 하면 `SfxCompressFlowTests` 11개가 실패함을 확인하고 되돌림. 계약 테스트: 전체 1900 — 통과 1900·건너뜀 0·**실패 0**.
  **백신 오탐 위험**: 서명 없는 새 exe + 뒤에 붙은 데이터 + 파일을 쓰는 동작이라 다른 PC 의 백신(행동·평판 검사)이 '위험'으로 잡거나 격리할 수 있고, 메일·웹으로 받은 exe 는 SmartScreen 'Windows의 PC 보호' 창이 뜬다(평판 없음).
  관리자 권한을 묻지 않게(`asInvoker` 매니페스트 — 이름에 setup·install 이 있어도 설치 프로그램으로 보지 않음) 했을 뿐 오탐을 막는 장치는 없음. Windows Defender 검사(`MpCmdRun -Scan -ScanType 3 -File … -DisableRemediation`)는 **못 함** —
  이 PC 의 Defender 가 꺼져 있음('Product/Feature disabled', `AMRunningMode: Not running` — 회사 백신 사용). 회사 메일 게이트웨이는 exe 첨부(압축 속 exe 포함)를 막는 경우가 많아 안내를 두 겹으로 둠.
  위험·못 본 것: 화면 모드 전부·DPI·다른 PC 의 SmartScreen·백신·메일 게이트웨이·.NET Framework 4.6.2 가 없는 옛 PC(Windows 10 1607 이전·꺼 둔 PC)는 MANUAL_CHECKLIST 16절. `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-14 W2-10 검증 지적 수정(1차)(커밋 전, 배포 안 함)**: 지적 6개를 모두 직접 확인했고(검증자의 scratch 재현 스크립트를 고친 스텁으로 다시 돌림) 모두 고쳤다.
  ① **[ZIP으로 만들기] + AES 한글 암호(major)**: 메일 질문에서 [ZIP으로 만들기]면 옵션을 그대로 ZIP 으로 바꿔 반디집 7.46 이 'Invalid Password' 를 내는 UTF-8 AES ZIP 이 조용히 생겼다(압축 창은 .exe 라 한글 암호 경고를 안 했음).
  `MainViewModel.ConfirmZipPasswordCompat` 가 ZIP 으로 바꿀 때만 `PasswordPolicy.CheckCompat(…, ".zip", …)` 로 한 번 더 묻는다 — AES+한글이면 [ZipCrypto 방식으로 만들기 (반디집·알집과 호환, 보안은 약해요)](기본 — 옵션 사본의 방식만 바꿈, CP949 로 써짐)·[AES-256 그대로 만들기 (일집에서는 풀려요)]·[취소 (암호를 영문·숫자로 다시 정하기)],
  이모지 등 CP949 로 못 바꾸는 글자면 [취소 (암호 다시 정하기)](기본)·[그대로 만들기]. 취소면 아무것도 안 만들고 '다시 묻지 않기'도 기억 안 함. [그래도 만들기](exe)·영문 암호·암호 없음은 더 묻지 않음. 압축 창 안내 '한글 암호도 돼요' → '한글 암호도 이 exe 로는 풀려요'.
  ② **같은 풀 곳에 동시에 풀면 되돌리기가 남의 결과를 지움**: 확인한 원인 — 옮기기의 '있나?→옮기기' 사이 경쟁으로 한쪽이 실패하면 `Rollback` 이 자기 장부의 경로를 확인 없이 지워, 그 사이 다른 실행이 덮어쓴 파일까지 지웠다.
  같은 풀 곳의 '제자리로 옮기기·되돌리기'를 이름 있는 뮤텍스(`Local\Iljip.SfxDest.<풀 곳 경로 SHA-256 앞 16바이트>` — `SfxStaging.DestinationMutexName`)로 한 번에 하나씩(임시 폴더에 푸는 것은 동시에 됨, 버려진 뮤텍스는 넘겨받음, 10분 넘게 못 잡으면 '같은 풀 위치에 다른 자동 풀림이 아직 풀고 있어요…').
  화면 모드는 기다리는 동안 '같은 곳에 다른 풀기가 진행 중이에요. 끝나기를 기다리는 중...'. 옮기기는 '먼저 옮겨 보고 그 자리에 무엇이 있으면 같은 이름 처리'(보고 나서 옮기기 사이의 틈이 없음). 분류 안 된 문구 둘('이미 있으므로'·'찾을 수 없습니다')도 쉬운 말로.
  ③ **강제 종료 흔적**: 새 공유 소스 `Iljip/Services/Shared/SfxStagingCore.cs`(`SfxStaging` — 본체·스텁·시험이 같이 씀). 스텁은 임시 폴더를 만들자마자 그 안의 `.사용 중` 을 독점으로 열어 두고(주인 표시), 다음 풀기는 그 파일을 열 수 있으면(= 주인이 죽음) **하루를 기다리지 않고** 정리한다.
  표시 없는 것(본체가 만든 것)은 예전처럼 하루 지난 것만, 열려 있으면 건드리지 않음. 덮어쓰려고 비켜 둔 사용자 원래 파일(`.덮어쓴 파일` — 비키기 **전에** `목록.txt` 에 원래 자리를 적음)은 지우지 않고 풀 곳의 **보이는 폴더 '덮어쓰기 전 원래 파일 (yyyy-MM-dd HH-mm-ss)'** 에 원래 폴더 모양대로 옮기고 알린다
  (무인 모드는 표준 출력 한 줄·종료 코드 0, 화면 모드는 끝 창). 하나라도 못 옮기면 임시 폴더를 지우지 않음(다음 정리 때 다시). 되돌리기에서 원래 파일을 못 되돌린 경우도 같은 폴더로 옮기고 오류 문구에 위치를 붙인다(예전: 숨은 폴더에 말없이 남겼다가 하루 뒤 지움).
  본체 `StagingFolder.CleanupStale`(7z.dll·압축된 TAR 풀기)도 같은 규칙을 쓴다(본체 임시 폴더는 표시가 없어 동작 그대로). 풀린 것은 임시 폴더 안 한 칸 아래(`내용\`)에 둔다 — 압축 속 맨 위 이름이 `.사용 중`·`.덮어쓴 파일` 이어도 겹치지 않게.
  화면 모드에서 로그오프·종료·작업 관리자 [작업 끝내기]로 창이 닫히면 `e.Cancel` 로 막지 않고 멈추라고 한 뒤 되돌리기를 4초까지 기다리고 닫힌다(그 안에 못 끝내면 숨은 폴더가 남지만 위 규칙으로 다음에 정리).
  ④ **SfxStubFact 건너뛰기 조건**: 예전엔 탐색용 스텁 실행의 종료 코드가 1 이 아니기만 해도 실행 시험 약 37개가 '건너뜀'이 됐다. 이제 Windows 가 실행 자체를 거절한 `Win32Exception` 만(파일 없음 2·3, exe 아님 193, CPU 다름 216 은 망가진 스텁이라 제외 — `SfxCreateTests.SkipReasonFor`) 건너뛰고,
  종료 코드는 새 시험 `스텁만_실행하면_데이터가_없어_1이고_창이_없다` 가 본다(스텁이 바로 죽으면 실패로 드러남).
  ⑤ **작은 파일이 많으면 느림**(검증자는 체크리스트만 바랐지만 원인을 재서 고침): 이 PC 실측 파일 하나 옮기기(rename) 2.3ms·있나 보기 0.2ms·시각 맞추기(다시 열기) 0.4ms — 옮기기가 대부분. **풀 곳에 아직 없는 폴더는 임시 폴더에서 통째로 한 번에 옮김**(2,000개 든 폴더 옮기기 4.6ms — 되돌릴 때도 통째로),
  수정 시각은 닫기 전에 열린 핸들로(`SetFileTime`), 확인한 폴더는 기억, 파일마다 '있나?' 두 번 보던 것 없앰. 무인 모드 실측(Release 스텁): 1만 개 39~46초 → **8.6~9.1초**(파이썬 zipfile 11~15초), 7만 개 411초(검증자) → **63초**(파이썬 86초), 315MB 3.9초·최대 작업 집합 25MB(그대로).
  같은 곳에 한 번 더 풀어 덮어쓸 때는 파일마다 옮겨 여전히 느림(MANUAL_CHECKLIST 16절에 시간 기록 줄).
  ⑥ **'[추가 정보] → [실행]' 단정**: 만들기 직전 질문을 "받는 PC 에 따라 'Windows의 PC 보호' 창이나 백신 경고가 뜰 수 있어요. [추가 정보] → [실행]이 보이면 눌러서 풀면 되고, 실행이 아예 막히는 PC(회사 보안 설정 등)도 있으니 그때는 ZIP으로 보내 주세요." 로,
  압축 창 안내에 '회사 보안 설정에 따라 실행이 아예 막히기도 해요' 를 더함. 백신 오탐은 여전히 **실측 못 함**(이 PC 의 Defender 꺼짐 — 회사 백신이 스텁 사본을 격리하지 않은 것만 확인).
  검증자 재현 다시(고친 스텁, scratch): 315MB exe 셋을 같은 곳에 동시에 4번 → 모두 0·빠진 파일 없음(예전 0·1·1 + 300MB 파일 없음), 1초 뒤 kill → 숨은 임시 폴더 남음 → 바로 다시 풀기 0·숨은 폴더 없음(예전 그대로 남음).
  시험: 새 19개(`SfxCompressFlowTests` 9 — ZIP 으로 바꿀 때 한글 AES 질문(ZipCrypto·AES 그대로·취소·창 닫기)·이모지 암호 질문·묻지 않는 경우 3, `SfxCreateTests` 10 — 건너뛰기 판정·스텁만 실행·마무리 잠금(시험이 뮤텍스를 쥐고 기다리는지 봄)·셋 동시·강제 종료 뒤 바로 정리·원래 파일 옮기기(무인·본체)·못 옮기면 남김·통째로 옮긴 폴더와 덮어쓴 파일 되돌리기·표시 이름과 같은 항목),
  기대가 바뀐 기존 시험 없음(`SfxCompressFlowTests.ZIP으로_만들기면_zip이_생기고_exe는_없다` 는 단언을 **더함** — 질문 창 하나·CP949 로 써짐). 일부러 고장 내 보기: 한글 암호 확인을 빼면 6개, 마무리 잠금을 빼면 1개, 주인 없는 임시 폴더를 하루 규칙으로 되돌리면 4개,
  통째로 옮긴 폴더를 되돌리지 않으면 1개가 실패함을 확인하고 되돌림(셋 동시 시험은 경쟁이 우연이라 잠금 없이도 통과할 수 있음 — 잠금은 결정적인 '뮤텍스 쥐고 보기' 시험이 지킴). 계약 테스트: 전체 1919 — 통과 1919·건너뜀 0·**실패 0**.
  위험·못 본 것: 화면 모드(기다림 문구·두 창 동시·로그오프 때 닫힘·끝 창의 원래 파일 안내)는 실행해 보지 않음(MANUAL_CHECKLIST 16절 '(검증 지적 1차)' 줄). 마무리 잠금은 같은 로그인 세션만(다른 사용자·다른 PC 가 같은 공유 폴더에 동시에 풀면 예전과 같음 — 드묾).
  폴더 통째로 옮기기·주인 표시 파일과 회사 DLP 의 상호작용은 이 PC 실측만(안의 파일이 잠겨 통째로 못 옮기면 하나씩 옮기는 예전 길로). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.

- **2026-10-14 W2-10 검증 지적 수정(2차)(커밋 전, 배포 안 함)**: 지적 7개를 모두 직접 확인했고(검증자의 재현과 같은 것을 고치기 전·뒤 스텁으로 돌림 — scratch) 모두 고쳤다.
  ① **4GB 넘는 exe(major)**: 확인 — 성긴 파일(Release 스텁 + 0 + 꼬리)을 `/s` 로 시작해 보니 4,294,967,295 바이트는 시작(종료 코드 2), 4,294,967,296·+1 바이트는 CreateProcess 오류 193('올바른 Win32 응용 프로그램이 아닙니다').
  `SfxBuilder.MaxExeBytes` = 4GB − 1바이트. 만든 뒤: 임시 ZIP 을 다 만들고 잇기 전에 exe 크기(스텁 + ZIP + 꼬리)가 넘으면 `SfxTooLargeException`('자동 풀림 exe 가 4GB 를 넘어서(약 …GB) 만들지 않았어요. … ZIP으로 만들어 주세요…' —
  임시 파일 지움·덮어쓰려던 exe 그대로). 만들기 전: 압축 창 흐름(`MainViewModel.CompressStagedAsync`)이 메일 질문 다음에 진행 창('원본 크기를 재는 중')을 띄운 채 원본 크기 합(빼기 목록 적용 — `SourceCollector` 가 훑을 때 같이 오는 크기 `SourceSet.TotalBytes`, 각각 따로면 원본마다)을 재고
  넘으면 묻는다 — [ZIP으로 만들기 (권장)](기본 — 한글 암호 확인을 거친 같은 옵션의 ZIP)·[그래도 자동 풀림 exe 로 해 보기 (4GB 가 넘으면 만들지 않아요)]·[취소]. 해 보다가(또는 어림은 아래였는데) 넘으면 하나로 만들 때는 그 까닭 + 'ZIP으로 만들까요?'([ZIP으로 만들기]면 진행 창을 다시 띄워 ZIP·[닫기]면 '오류 발생'),
  각각 따로는 요약 창에 까닭. 압축 창 안내에 '4GB 가 넘는 exe 는 Windows 에서 실행되지 않아 만들지 않아요 — 큰 자료는 ZIP으로…'. 시험은 한도를 `SfxBuilder.LimitForTests`(AsyncLocal)로 작게 — 4GB 를 실제로 쓰지 않음(4GB 성긴 파일은 회사 백신이 오래 잡아 지우기가 늦어 시험에 넣지 않음).
  ② **다 풀고 지우는 사이 '지난번 흔적' 오보**: 확인 — 1,500개 덮어쓰기 한 번 + 끝날 즈음 다른 풀기를 0.08초마다(scratch `race_check.py`): 고치기 전 스텁 4번 중 3번 '덮어쓰기 전 원래 파일 (…)' 폴더가 생김.
  원인 그대로 `Staging.Delete` 가 주인 표시를 먼저 닫고 지웠다. 공유 소스에 `SfxStaging.DeleteOwnedTree`(표시를 쥔 채 나머지를 먼저 지우고 표시는 마지막에 닫은 뒤 표시·폴더) — 스텁 마무리와 흔적 정리(`CleanupStale` — 정리하는 쪽도 표시를 쥔 채 지움)가 같이 씀. 고친 뒤 5번 중 0번.
  ③ **덮어쓰며 풀기가 느림**: 확인 — 3,000개 같은 이름 덮어쓰기 파일당 9~12ms(고치기 전 스텁, 같은 때). 재 보니(scratch 계측판) 목록.txt 열고 닫기보다 **실패하는 옮기기(파일당 1.6ms)·'폴더인가?'(0.3ms)** 가 컸다.
  원래 있던 폴더는 처음 볼 때 한 번 훑어 같은 이름을 가림(`CommitLedger.Existing` — 대소문자까지 같은 이름만, 다르면 예전처럼 옮겨 보고 가림), 목록.txt 는 한 번 열어 둔 채 줄마다 OS 로 넘김(`SfxStaging.BackupList` — 강제로 죽어도 줄이 남음, 읽거나 지우기 전에 닫음),
  비켜 둘 폴더는 한 번만 만듦, 지울 때 읽기 전용 표시가 있는 것만 풂(`ClearReadOnly`). 3,000개 파일당 **4.6~5.6ms**(같은 때 예전 스텁 9.1ms) — 남은 것은 되돌릴 수 있게 하는 옮기기 둘(원래 파일 비키기·새 파일 넣기, 파일당 약 3.4ms)이라 더 줄이지 않았다.
  덮어쓰다 강제 종료 → 다시 풀기(scratch `kill_check.py`, 50%·80% 시점): 비켜 둔 원래 파일 1,054개·865개가 모두 원래 이름·원래 폴더 모양으로 옮겨짐.
  ④ **끝이 잘린 일집 자동 풀림이 '지원하지 않는 형식'**: 확인 — 끝 5,000바이트를 뗀 exe 를 `Iljip.exe --extract-to` 하면 종료 코드 2 'Unsupported archive type: .exe'. `ArchiveSniffer.DetectSfx` 가 일집 꼬리·ZIP 끝 머리를 못 찾아도 PE 덧붙은 영역이 ZIP 로컬 머리로 시작하면(서명·판·방식·이름 길이 확인)
  ZIP 자동 풀림으로 보고(7z·RAR 찾기보다 먼저 — 잘린 ZIP 안의 .7z 를 압축 자체로 열지 않게), `SfxArchiveService` 가 엔진에 맡기기 전에 `ArchiveSniffer.IsTruncatedZipSfx` 로 '압축 파일의 끝부분이 잘려 있어요…'(`CorruptArchiveException.Truncated`, 종료 코드 1).
  그냥 맡기면 SharpCompress 의 'Failed to locate the Zip Header'(화면 '알 수 없는 오류가 났어요: …')라서. 스텁만 있는 exe·보통 exe 는 그대로 2. 재사용장치는 .exe 를 넘기지 않아 계약 영향 없음(문구에 '압축 아님' 표지 없음도 시험).
  ⑤ **'다시 묻지 않기' + [ZIP으로 만들기]**: 확인 — 기억해서 다음 자동 풀림이 안 묻고 exe 로 만들어짐. 이제 [그래도 만들기]일 때만 기억하고, 그것도 뒤의 질문(4GB·한글 암호)에서 취소하면 기억하지 않는다(취소 없이 만들기로 갈 때 압축 설정과 한 번 저장).
  ⑥ **나중에 코드 서명하면 '손상'**: 확인 — 서명 도구 모양(8바이트 경계까지 0 + WIN_CERTIFICATE 표 + PE 보안 디렉터리)을 흉내 낸 exe 를 고치기 전 스텁이 '손상'으로 거절. HANDOFF 기록만 바란 지적이지만 고쳤다:
  새 공유 소스 `Iljip/Services/Shared/SfxTrailerCore.cs`(`SfxTrailer.TryLocate` — 스텁 `SfxPayload.Open`·본체 `SfxBuilder.TryReadTrailer` 가 같이 씀) — 꼬리는 파일 끝, 아니면 보안 디렉터리가 가리키는 서명 표가 8바이트 경계에서 시작해 **정확히 파일 끝까지**일 때만 그 앞(0 채움 0~7바이트)에서.
  표 뒤에 무엇이 더 붙었거나 표가 아닌 것이 붙으면 예전처럼 손상. 서명은 만든 exe 마다 해야 한다(Authenticode 해시는 덧붙은 데이터까지 덮음). **진짜 signtool·인증서로는 확인 못 함**(이 PC 에 없음 — 규격대로 흉내 낸 모양으로만 시험).
  ⑦ **NFC·NFD 같은 이름이 함께 있으면 통째로 거절**: 확인(NTFS 는 두 모양을 다른 이름으로 둠 — 일집이 그 폴더로 exe 를 만들면 ZIP 에 둘 다 들어감). `SfxExtractor.Plan` 이 NFC 로 바꿔서 겹칠 때만 압축에 적힌 그대로(`ZipItem.RawName`) 풂 — 원본과 같은 두 파일. NFD 만이면 예전처럼 NFC, 바꾸지 않아도 겹치면 예전처럼 손상.
  시험: 새 18개(`SfxCreateTests` 10 — 한도·서명(1+2)·NFC/NFD·잘린 exe 일집 안(2)·지우는 동안 표시 유지(남은 파일을 잠가 결정적으로)·목록 줄마다 남음·많이 덮어쓰기, `SfxCompressFlowTests` 7 — 4GB 질문·취소(2)·해 보다가 넘음(2)·만든 뒤 넘음+각각 따로·각각 따로 질문, `CommandLineTests` 1 — 실제 Iljip.exe 창 없이 풀기 잘린 exe 1·멀쩡한 것 0·스텁만 2),
  기대가 바뀐 기존 시험 1개(`SfxCompressFlowTests.다시_묻지_않기를_켜면_기억하고_다음엔_묻지_않는다` Theory(0·1) → Fact(0) — ⑤의 반대 계약은 새 시험이 지킴), 단언을 더한 것 1개(압축 창 안내 '4GB'). 일부러 고장 내 보기: 일곱 가지를 한꺼번에 되돌리면 새·바뀐 시험 15개 실패, 되살린 뒤 0.
  계약 테스트: 전체 1937 — 통과 1937·건너뜀 0·**실패 0**.
  위험·못 본 것: 화면 모드(4GB 질문·다시 묻는 진행 창·덮어쓰기 시간·잘린 exe 열기)는 실행해 보지 않음(MANUAL_CHECKLIST 16절 '(검증 지적 2차)' 줄). 원래 있던 폴더를 한 번 훑는 것은 그 폴더에 파일이 아주 많으면(수십만) 처음 한 번 그만큼 걸림.
  지우는 동안 남은 파일이 오래 잠겨(백신 등) 끝내 못 지우면 표시를 놓고 끝나므로 그 남은 원래 파일은 다음 풀기가 '지난번 흔적'으로 옮겨 알림(예전과 같음 — 드묾). 스텁 크기 68,096 → 70,656 바이트(Release, 같은 소스면 같은 바이트).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.

- **2026-10-14 W2-11 — 시작 화면·최근 파일·압축 파일 관리(I3-15), 관리자 권한으로 다시 시도(I3-16)(커밋 전, 배포 안 함)**: 둘 다 Skip 이 없던 새 기능.
  ① **시작 화면**: `Views/StartView.xaml`(UserControl)이 메인 창의 빈 화면 안내 글 자리를 대신함 — 보이는 때는 `MainViewModel.IsStartVisible`(= 압축도 모으는 목록도 없을 때, 열기·닫기·목록이 바뀔 때 알림).
  큰 단추 [압축 파일 열기](기존 열기)·[압축하기](파일 고르기 → 모으는 목록 → 한 화면 압축 창)·[압축 풀기…](하나면 열고 풀기 창, 여럿이면 풀기 창 한 번 + 여러 개 풀기), 끌어 놓기 안내, '최근 파일'(오른쪽 클릭: 열기·새 창으로 열기·들어 있는 폴더 열기·목록에서 지우기·모두 지우기).
  ② **최근 파일**: `Services/RecentFilesService.cs` + 설정 `recent.files`(`AppSettings.Recent` 를 덧붙임 — 경로·마지막으로 연 시각 UTC, 빈 경로는 읽을 때 버림). 압축을 열 때마다 맨 앞(전체 경로, 대소문자 무시로 하나, 10개),
  임시 폴더(`TempLocations.IsTemporary` — 일집 temp·%TEMP%·Outlook 첨부 INetCache) 안은 넣지 않음. **열 때마다 저장하지 않고 창을 닫을 때 한 번**(`MainViewModel.SaveOnExit` — W2-09 트리 너비 저장과 합쳐 한 번, DLP 연속 저장 경합 교훈 · 기존 'SaveCount == 1' 시험들도 그대로),
  목록에서 빼기·모두 지우기는 바로 저장. 저장할 때마다 저장된 목록과 합침(`RecentFilesService.Merge` — 늦게 연 시각, 이 실행에서 뺀 것은 빼고 — 창 두 개가 서로의 것을 지우지 않게). 없는 파일은 흐리게 '없음', 누르면
  '‘이름’을 찾을 수 없어요… 최근 파일 목록에서 뺄까요?'([목록에서 빼기][그대로 두기]). 있는지는 로컬만 바로 보고 네트워크 경로는 보지 않음(끊긴 NAS 로 시작 화면이 굳지 않게 — 누를 때 작업 스레드에서 5초까지).
  ③ **다른 이름으로 저장**(☰): `Services/ArchiveSaveAs.cs`(이름 꼬리 `SuffixOf` — '.7z.001'·'.tar.gz'·'.part1.rar', 처음 이름 '이름 - 복사본'+꼬리, 계획, 복사) + `MainViewModel.Start.cs`. 고른 이름의 꼬리가 같으면 **복사** —
  나뉜 압축은 `ArchiveVolumes.AllParts` 조각 전부를 이름 부분만 바꿔(새 이름의 조각이 하나라도 있거나 숫자 조각의 다음 번호가 있으면 섞이지 않게 하지 않음), 새 파일 옆 숨김 임시 파일에 다 쓴 뒤 이름을 바꿈(하나는 `OutputFiles.ReplaceExisting`, 여럿은 덮어쓰지 않고 — 실패면 놓은 조각까지 지움, 수정 시각 원본대로).
  '.zip'·'.7z'(7z.dll 이 있을 때)로 바꾸면 **변환** — `%LOCALAPPDATA%\Iljip\temp\convert\guid` 에 다 풀고(맞았던 암호, 모르면 묻기), 원래 암호가 있었으면 '원래 압축 파일에 암호가 걸려 있었어요. 새 파일에도 같은 암호를 걸까요?'([같은 암호 걸기][암호 없이 저장][취소]),
  ZIP 이면 압축 창과 같은 한글 암호 호환 질문, 새 파일 옆 임시 파일에 만들고 하나라도 못 담았으면(`CompressResult.Skipped`) 실패로, 끝나면(실패·취소도) 임시 파일·temp\convert 를 지움. 다 되면 새 파일을 엶. 그 밖의 꼬리는 '…형식으로는 저장할 수 없어요' 안내.
  ④ **이 압축 파일 지우기**(☰): 묻고('‘a.zip’을 휴지통으로 옮길까요?' — 기본 [취소], 나뉜 조각 수, 열어 고쳐 놓고 넣지 않은 파일이 있으면 그 경고) → 먼저 닫기(보기·찾기 + 이 압축의 작업 폴더와 연 파일 감시 — 쉬는 작업 폴더 포함) → `IRecycleBin.Send(조각 전부)` → 시작 화면·최근에서 뺌.
  휴지통이 없는 곳(네트워크·USB)은 지우지 않고 알림(영구 삭제 안 함), 못 옮기면 알리고 다시 엶.
  ⑤ **새 창**: ☰ 새 창·**Ctrl+N**(★ 예전 Ctrl+N = '압축 실행'을 바꿈 — 탐색기·브라우저와 같게, 압축은 툴바 [압축] 단추)·최근 파일 '새 창으로 열기' → `IProcessLauncher.StartNewInstance(일집, ["--new-window", …])`(관리자 창이면 `--elevated` 도 — 자식은 관리자 권한을 물려받음).
  App: 새 순수 함수 `Services/StartupDecision.cs`(Headless·Independent·FirstInstance·Forward) — `--new-window`·`--elevated` 로 뜬 일집은 **단일 인스턴스 뮤텍스를 만들지도 파이프 서버를 열지도 않음**(그래서 우클릭 명령은 늘 처음 띄운 보통 일집이 받고, 뒤에 켠 보통 일집이 따로 뜬 창을 기다리지 않음).
  ★ 재사용장치 계약: 창 없이 풀기(`--extract-to` 가 첫 인자) 판정은 `StartupDecision.IsHeadlessExtract` 로 옮겼을 뿐 같다(시험) — `--elevated` 가 앞에 붙으면 대화형(진행 창). 종료 코드·문구는 손대지 않음.
  ⑥ **관리자 권한으로 다시 시도**: `Services/ElevationService.cs`(권한 오류 판정·질문 문구·인자 만들기)·`CommandLineQuoting.cs`(Windows `CommandLineToArgvW` 규칙 따옴표 — 시험이 실제 API 로 되읽음)·`IProcessLauncher.cs`(진짜는 `runas`, Win32 1223 = UAC [아니요])·`MappedDrives.cs`(`WNetGetConnectionW`) + `MainViewModel.Elevation.cs`.
  권한 오류(UnauthorizedAccess·0x80070005·Win32 5 — 안쪽 예외까지, 일집 `ArchiveException` 은 아님)면 오류 창 대신 '…쓸 권한이 없어요…\n\n관리자 권한으로 다시 해 볼까요? (Windows 가 허락을 물어요…)' [관리자 권한으로 다시 시도][닫기].
  같은 작업 인자: 다른 곳에 풀기(하나) `--elevated --extract-to 압축 폴더`, 우클릭 여기에 `--extract-here`, '이름' 폴더 `--extract-to-folder`, 풀기 창의 여러 개 '이름' 폴더 **`--extract-to-folder-in 폴더 압축…`**(새 동사), 압축 **`--compress-to 만들폴더 원본…`**(새 동사 — 관리자 창이 압축 창을 만들 곳 그 폴더로 띄움, 설정의 '만들 곳' 선택은 안 바꿈),
  나타낼 수 없는 작업(고른 항목만 풀기·다른 이름으로 저장·여럿을 한 폴더에 바로)은 **'관리자 권한으로 일집 다시 열기'**(`--elevated --open 압축`). 여러 개 풀기는 요약 창 뒤 권한으로 못 푼 것만 묻기. **암호는 인자에 넣지 않음**(다른 프로그램이 명령줄을 볼 수 있어서 — 관리자 창에서 다시 묻거나 입력).
  경로는 전체 경로로, 매핑 드라이브는 UNC(관리자 쪽에서는 Z: 가 안 보여서) — 모르면 '네트워크 드라이브는 관리자 권한으로 다시 시도할 수 없어요…'. `--elevated` 창·이미 관리자인 일집은 다시 권하지 않음(무한 반복 방지 — 예전처럼 오류 창, 제목 끝 ' (관리자)').
  W2-08(문제 창 ShowProblem·ErrorCatalog)이 main 에 없어 기존 `Choose` 로 묻는다 — W2-08 을 다시 할 때 `ElevationService.ActionsFor`·`RetryActionId` 를 '권한 없음' 분류의 Actions 로.
  덧붙이기만 한 것: `AppSettings.Recent`·`RecentSettings`·`RecentFile`, `AppServices.Processes`·`MappedDrives`, `CommandVerb.CompressTo`·`ExtractToFolderIn`, `ParsedCommand.NewWindow`·`Elevated`(기존 멤버 이름·모양 그대로). `ArchiveExceptions`·`IUserInteraction` 은 바꾸지 않음.
  시험: 새 81개(`RecentFilesTests` 10, `StartScreenTests.cs` 의 `StartScreenTests` 10·`SaveAsTests` 18·`DeleteArchiveTests` 8, `ElevationTests` 19, `StartupDecisionTests` 6, `CommandLineParserTests` 10 — Theory 줄 포함).
  권한 오류는 시험 임시 폴더에만 현재 사용자 '파일·폴더 만들기 거부' 권한을 걸어 진짜로 만듦(끝나면 되돌림 — 시스템 설정·레지스트리는 건드리지 않음), UAC·새 프로세스는 가짜(`FakeProcessLauncher`) — 인자와 분기만 봄.
  기대가 바뀐 기존 시험 2개: `MainWindowEditUiTests.메인_창이_읽히고_고치기_장치가_있다`(☰ 메뉴 기대 목록 맨 앞에 새 셋 — 시작 화면·Ctrl+N 단언도 더함), `CommandLineParserTests.키워드_목록은_모두_해석된다`(키워드 8→10 — 새 키워드·표시와 안 겹침 단언 더함).
  일부러 고장 내 보기: 다섯 가지(휴지통에 보낸 뒤에 닫기·따로 뜨는 창도 단일 인스턴스·임시 폴더 제외 끄기·이미 있는 조각 확인 끄기·관리자 창에서도 권하기)를 한꺼번에 넣으면 새 시험 11개 실패, 되살린 뒤 0.
  계약 테스트: 전체 2018 — 통과 2018·건너뜀 0·**실패 0**.
  위험·못 본 것: **진짜 UAC 창·관리자 일집**(진행 창·제목·Ctrl+N)은 띄워 보지 않음 — 관리자 창에는 Windows 가 보통 탐색기에서의 **끌어 놓기를 막음**(UIPI — 알려진 Windows 동작). 표준 사용자가 다른 관리자 계정 암호로 UAC 를 통과하면 관리자 일집은 **그 계정의**
  설정·최근 목록·임시 폴더를 쓰고, 관리자 일집이 만든 파일은 주인이 Administrators(보통 탐색기에서 지울 때 묻을 수 있음). 압축 고치기(넣기·지우기·이름 바꾸기)는 `ArchiveSwap.EnsureWritable` 이 권한 오류를 '다른 프로그램이 압축 파일을 쓰고 있어…'로 바꿔 대부분 권하지 않음(기존 시험이 그 문구에 기대 그대로 둠).
  `subst` 드라이브는 UNC 로 바꾸지 않음. 창 두 개면 최근 파일 밖의 설정(압축 창 마지막 값·트리 너비 등)은 예전처럼 나중에 저장한 창 것(→ 검증 지적 1차에서 고침). 바꿔 저장 중 강제 종료면 temp\convert 가 남음(청소기 없음). 시작 화면 생김새·진짜 휴지통은 MANUAL_CHECKLIST 17절.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.

- **2026-10-14 W2-11 검증 지적 수정(1차)(커밋 전, 배포 안 함)**: 지적 7개 모두 직접 확인 — 모두 맞아 고침(건너뛴 지적 없음).
  ① **(major) 창 두 개면 최근 파일·설정이 서로 지워짐**: 각 창이 켜질 때 읽은 사본 전체를 쓰던 저장 네 곳(설정 창 `ApplySettings`·풀기 창 `RememberExtractTarget`·압축 창 `RememberCompressSettings`·트리 너비)과 닫을 때 저장을
  모두 `MainViewModel.SaveSettings`(ExtractSettings.cs) 하나로 — 새 `ISettingsStore.Update(change)` 가 **저장 직전에 최신 settings.json 을 다시 읽어** 이 창이 바꾼 것만 얹는다:
  설정 창은 `Services/SettingsPatch.cs`(새 — 이 창 설정과 창 결과의 JSON 잎 차이, 목록은 한 칸, `recent` 묶음은 빼고), 풀기 창·압축 창·트리 너비는 그 칸 바꾸기를 최신 설정에 다시(최근 푼 곳은 맨 앞에 넣기 그대로),
  최근 파일은 `RecentFilesService` 가 저장 전까지 이 창에서 **넣은 것·뺀 것만** 기억해(`AddedThisSession`·`RemovedThisSession`·`MarkSaved`) `Merge` 로 얹음 — 예전 합치기는 이 창 목록 전체를 '내 것'으로 써서 다른 창이 뺀 줄을 되살렸음.
  '모두 지우기'는 저장된 줄까지 비움. 저장한 뒤 이 창의 최근 목록을 저장된 것으로 바꿔 다시 보임(다른 창이 연 것도). `JsonSettingsStore`: 쓰기(`Save`·`Update`)를 설정 파일 경로 해시 이름의 뮤텍스(`Local\Iljip.Settings.…`, 3초)로 한 번에 하나 —
  같은 `.tmp` 경합, 읽기는 잠김이면 200ms×3 다시, **있는데 못 읽으면 `Update` 는 쓰지 않고 null**(기본값으로 덮어쓰지 않음) → 화면은 바꾼 것을 들고 있다가 다음 저장(닫을 때 포함)에 다시. `InMemorySettingsStore` 에 `Update`·`UpdateCount`·`FailUpdates`(시험용).
  덤으로 찾은 버그: 설정 창 적용이 `_settings` 객체를 바꿔 끼워 `RecentFilesService` 가 옛 객체를 쥐고 있었음 — 그 뒤 연 압축이 닫을 때 저장되지 않았다(`AppSettings.CopyFrom` 으로 내용만 바꿈).
  ② **읽기 전용 파일 덮어쓰기에도 관리자 권한 권함**: `OutputFiles.ReplaceExisting` 이 대상이 읽기 전용이면 기다리지 않고 새 `ReadOnlyTargetException`(IOException — '…읽기 전용이라 덮어쓰지 못했어요. …[속성] → '읽기 전용'을 끄고…')을 던지고
  `ElevationService.IsAccessDenied` 가 그것(안쪽에 있어도)은 권한 오류로 안 봄. 그 밖의 접근 거부는 '이 폴더에 쓸 권한이 없거나 다른 프로그램(보안 프로그램 포함)이 쓰고 있어요', 공유 위반은 '…쓰고 있어요. 그 프로그램을 닫고…'로 갈라 관리자 질문과 앞뒤가 맞게.
  쓰려던 곳이 네트워크(UNC·네트워크 드라이브 — `FolderProbe.IsNetwork`, 디스크를 보지 않음)면 권하지 않음(`ElevationService.CanHelpWriteTo` — `OfferElevatedRetry` 에 쓰려던 곳 인자를 더해 부르는 곳 여섯 모두 넘김: 풀 곳·만들 곳·고칠 압축·저장할 파일).
  ③ **지우기 실패에도 열어 고친 파일이 사라짐**: `DeleteArchiveFileAsync` 가 작업 폴더·감시(`ReleaseWorkspacesOf`)를 휴지통으로 옮긴 **뒤에만** 놓음(작업 폴더는 일집 임시 폴더라 압축 파일을 잡지 않음 — 보기 닫기는 그대로 먼저).
  ④ **MANUAL 알려진 점·`--add-to` 시험**: 압축 고치기는 폴더에 새 파일을 못 만들면 권함(넣기 `--add-to` = 같은 명령, 그 밖 = 다시 열기) — 시험 둘. 압축 파일 **자체에** 쓸 권한이 없으면(Program Files 에 원래 있던 파일)
  `ArchiveSwap.EnsureWritable` 이 여전히 '다른 프로그램이 이 압축 파일을 쓰고 있어서…'로 바꿔 권하지 않음 — MANUAL 17절 알려진 점에 그대로 적음(남은 일).
  ⑤ **여럿을 한 폴더에 바로 풀다 막히면 첫 압축만 다시 엶**: 새 동사 `CommandVerb.ExtractInto`(`--extract-into 폴더 압축…` — 메인 창 없이, 규칙 Into) — `ElevatedRetryFor` 가 여럿이면 그것(하나는 예전대로 `--extract-to`). 키워드 10→11.
  ⑥ **'(관리자)'가 인자를 따름**: 제목은 `IsRunningElevated`(실제 토큰 — `ProcessLauncher.IsCurrentProcessElevated` 는 한 번만 봄), 다시 권하지 않기는 '--elevated' 표시 또는 실제 권한(UAC 가 꺼진 PC 의 무한 반복 막기는 유지).
  ⑦ **'없는 파일 정리'**: 시작 화면 '최근 파일' 옆 [없는 파일 정리](흐린 '없음' 줄이 있을 때만 — `HasMissingRecentFiles`)·오른쪽 클릭 메뉴 → '찾을 수 없는 파일 N개(…)를 최근 파일 목록에서 뺄까요?' [목록에서 빼기][취소] — 네트워크 줄은 보지 않으므로 빼지 않음.
  시험: 새 23개(`RecentFilesTests` 9·`StartScreenTests` 3·`DeleteArchiveTests` 1·`ElevationTests` 8·`CommandLineParserTests` 2). 기대가 바뀐 기존 시험은 Iljip.Tests/README 'W2-11 검증 지적 수정(1차)'(약하게 한 것 없음).
  일부러 고장 내 보기: 예전처럼 사본 전체 쓰기 → 창 두 개 시험 5개 실패, 지우기 순서·읽기 전용 판정·`--extract-into`·네트워크 판정·제목을 예전대로 → 8개 실패, 되살린 뒤 0.
  계약 테스트: 전체 2041 — 통과 2041·건너뜀 0·**실패 0**.
  위험·못 본 것: 이미 떠 있는 창은 다른 창이 바꾼 설정을 다시 켤 때부터 씀(그 창의 설정 창도 켜질 때 값 — 손대지 않은 칸은 저장된 값을 되돌리지 않음). 네트워크 드라이브 판정은 실제 매핑 드라이브로 못 봄(UNC 모양만 시험).
  뮤텍스는 같은 사용자 세션 안만(다른 계정으로 UAC 를 통과한 관리자 일집은 어차피 다른 %LOCALAPPDATA%). MANUAL_CHECKLIST 17절 '(검증 지적)' 줄. `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.

## 사용자 선호

- 한국어, 사근사근한 톤
- 방향성 무조건 따르지 말고 조언할 게 있으면 먼저 물어볼 것
- 코딩 검증 통과 + 질문 없으면 다음 단계 자동 진행
- 매번 칠 명령어는 코드 블록으로 정리해서 제공
