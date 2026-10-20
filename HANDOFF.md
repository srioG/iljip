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
- CLI 인자: `--open`, `--extract`, `--extract-here`(그 자리에), `--extract-to-folder`(이름 폴더에, 있으면 '이름 (2)'), `--extract-to`, `--compress`, `--add-to`(W2-06 — 압축에 넣기), `--add-to-pick`(W2-06 검증 지적 — 넣을 압축을 고르는 창부터), `--test`(W2-08 — 압축 검사, 메인 창 없이 결과 창만) — 해석은 `Services/CommandLineParser.cs`
  (W2-05: 풀기 동사 뒤 압축 여러 개 = 여러 개 풀기, 탐색기 우클릭 풀기는 메인 창 없이 진행 창만 → 끝나면 '이름' 폴더를 탐색기로)
  (W2-11: `--compress-to <만들 폴더> <원본…>`(만들 곳을 정한 압축 창), `--extract-to-folder-in <폴더> <압축…>`(그 폴더 안 '이름' 폴더마다 — 메인 창 없이),
  `--extract-into <폴더> <압축…>`(검증 지적 — 여럿을 그 폴더에 바로, 메인 창 없이. ★ 창 없이 풀기 `--extract-to` 와는 다른 키워드), 동사 앞 표시
  `--new-window`(단일 인스턴스를 건너뛰는 새 창)·`--elevated`(관리자 권한으로 다시 뜬 일집 — 다시 권하지 않음). `CommandLineParser.ToArgs` 가 `Parse` 의 반대)
  (W2-12: `--extract-smart <압축…>`(알아서 풀기)·`--compress-zip`·`--compress-7z <경로…>`(창 없이 바로 압축) — 메인 창 없이, `--register-associations [--drop-handler]`·`--unregister-associations`(파일 연결 — 자체 프로세스로 돌고 끝남),
  긴 명령줄은 `--list-file <목록 파일>`(셸 확장이 30,000자를 넘으면 `%LOCALAPPDATA%\Iljip\temp\args\<guid>.lst` 에 경로를 쓰고 넘김 — 처리하는 일집이 해석 전에 펼치고 지움))
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
- **W2-12(2026-10-14)**: 탐색기 연결 — **파일 연결**(☰ ▸ 설정 '파일 연결'·`--register-associations [--drop-handler]`·`--unregister-associations` — HKCU 에 '연결 프로그램' 후보·기본 앱 목록 칸·형식 아이콘만 넣고
  기본 앱은 빼앗지 않음, [기본 앱 설정 열기]), 우클릭 **'일집 ▶' 하나**(압축 하나: 일집으로 열기·여기에 풀기·"이름"\ 에 풀기·**알아서 풀기**·다른 곳에 풀기... / 압축 여럿: 압축 풀기...·각각 알아서 풀기·각각 이름 폴더에 풀기 /
  파일·폴더: 압축하기...·**"이름.zip"으로 압축**·**"이름.7z"로 압축** — 창 없이 바로), 폴더 안 빈 곳 '일집 열기', **오른쪽 끌기** '일집: 여기에 풀기'·'일집: 여기에 압축', zip·7z 아이콘 위에 **끌어 놓아 넣기**(선택),
  고른 것을 한 명령줄로(길면 `--list-file`). 설치·등록은 사람이 할 일(아래 '셸 확장·파일 연결 반영(W2-12)')
- **IJ3-01(2026-10-14 — W2-04 다시)**: 목록에서 파일 두 번 누르기·Enter → 그 파일만 꺼내 기본 프로그램으로 열기(W2-06 이 먼저 들인 것 + 실행 파일 경고를 설정으로 끄기),
  탐색기·바탕 화면으로 **끌어 꺼내기**(놓는 순간 꺼냄 — 여럿이면 한 번에, 큰 것은 진행 창), **Ctrl+C → 탐색기 붙여넣기**(우클릭 '복사 (Ctrl+C)' — 수천 개도 화면이 굳지 않고 진행·취소,
  Windows 가 못 쓰는 이름은 바꿔 복사하고 알림), 오른쪽 **그림 미리 보기** 칸(툴바 그림 단추 — ←→·확대, 큰 그림은 줄여 읽고 아주 큰 그림은 안 읽음), 다음 시작 때 임시 폴더 청소
- **IJ3-03(2026-10-15 — 사용자 결정 I2-15)**: 메일·웹에서 받은 압축(‘인터넷에서 받은 파일’ 표시 — Zone.Identifier)을 **일집 창·탐색기 우클릭으로 풀면** 풀린 파일에도 그 표시를 그대로 붙임
  (오피스 '제한된 보기' 유지 — 이번에 만든 파일에만, 수정 시각 그대로. 열기·끌기·Ctrl+C 도). ☰ ▸ 설정에서 끌 수 있음(기본 켬). **창 없이 풀기(재사용장치)는 붙이지 않음**.
  7-Zip 라이선스를 7-zip.org 원문(`licenses\7-Zip-License.txt`)으로 바로잡음(안내·BSD 파일은 원문 줄 그대로, 판 안내 26.03)
- **I4-1(2026-10-16 — IJ3-02 다시, 4차)**: **압축 검사**(☰·목록 우클릭 '시험하기'·`--test <압축…>` — 풀지 않고 모든 항목의 CRC·크기, 진행·취소, '문제없어요. 파일 N개를 확인했어요.'/'문제가 있어요…', 손상 ZIP·나눠진 ZIP 이면 [ZIP 고치기 시도]),
  **오류 원인 문제 창**(손상·잘림·조각 없음·암호 틀림·다른 프로그램이 씀·공간 부족·경로가 너무 김·권한 없음을 쉬운 한국어로 — 영어 원문은 '자세히'·로그에만, 창 없이 풀기 stderr 도 한국어),
  **깨진 ZIP 고치기 — 정직한 보수 모드**(☰ ▸ ZIP 고치기...·문제 창: 원본 그대로 새 '이름_복구.zip' — 끝의 목록을 믿을 수 있으면 목록 기록 수가 원래 개수 '전체 N개 중 M개를 살렸어요'·'모두'는 모든 기록을 CRC 로 확인했을 때만,
  목록이 없으면 맨 앞부터 끝이 데이터로 확인된 사슬만 '원래 몇 개였는지 확실하지 않아요 — M개를 살렸어요'·믿기 어려운 조각은 'K개'로 수만, 확인 못 한 항목·0바이트 '살리기' 없음)
- **I5-A(2026-10-20 — 감사 X3)**: 압축 속에서 연 파일을 편집기에서 **이름을 바꿔도**(Light PDF 탭 '이름 바꾸기') 따라가 묻고 압축에는 원래 이름으로 넣음, 연 파일을 다른 프로그램이 아직 잡은 채 일집을 닫으면
  '아직 열려 있어요' 알림 [닫기][취소], 닫은 뒤(또는 일집이 갑자기 꺼진 뒤) 저장한 파일은 **지우지 않고** 다음 시작 때 '압축에 아직 넣지 않은 고친 파일이 있어요' [압축에 넣기][폴더 열기][버리기][나중에]
- **I5-B(2026-10-20 — 최종 감사 안전·정직 5건)**: 풀기가 **풀고 있는 압축 파일(과 그 조각)을 덮지 않음**(같은 이름 항목은 '이름 (1)'로 비켜 풀고 알림 — 같은 이름 창에서 덮어쓰기를 골라도),
  덮어쓸 파일을 다른 프로그램(엑셀 등)이 열어 두었으면 **그 파일만 건너뛰고 나머지는 다 풀고** '다른 프로그램이 쓰는 중이라 못 바꾼 파일 N개' 알림(관리자 권유 없음 — 진짜 권한 문제만),
  ZIP·TAR·GZ·BZ2 의 [바꾸기]는 옆 임시 파일에 다 쓴 뒤에만 바꿔 넣음(**취소·실패면 기존 파일 그대로**), tar 링크는 내용 없이(0바이트) 만들고 결과 창·여러 개 풀기 요약·검사 결과에 알림
  (링크 되살리기는 안 함 — 결정), 창 없이 풀기(`--extract-to`)에 없는 경로·폴더·빈 인자를 주면 창 없이 한국어 한 줄·종료 1(예전엔 창이 떠서 끝나지 않음), 링크가 든 tar 는 표준 오류 한 줄·종료 1
- **5차 통합 점검(2026-10-20)**: 여러 개 풀기는 **같이 푸는 다른 압축도 덮지 않음**(가.zip 안의 '나.zip' 은 '나 (1).zip' — 같이 푸는 나.zip 은 원래 내용 그대로 풀림),
  압축 고치기(넣기·다시 넣기)에서 같은 이름을 바꿔 넣는데 **새 파일을 못 읽으면 원래 항목을 그대로 둠**(예전엔 원래 항목만 지워짐), 다음 시작 [압축에 넣기]는 먼저 사본을 떠서 넣고
  (그 순간 고친 파일을 못 읽으면 압축·파일 모두 그대로 + 안내) **같은 암호 압축은 암호를 한 번만** 물음
- C++ 셸 확장 메뉴: (W2-12 부터) '일집 ▶'(하위 메뉴)·'일집 열기'·오른쪽 끌기 메뉴·드롭 처리기. 예전 5개 메뉴("일집으로 열기" / "여기에 풀기 (일집)" / "\"이름\"\ 에 풀기 (일집)" / "압축 풀기... (일집)" / "일집으로 압축")는 DLL 에만 남고 매니페스트에서는 뺐다

## 현재 진행 단계 (2026-10-20 5차 통합 점검 후)

**5차 통합 점검 끝 — 커밋·푸시**(코드·시험 7eff574, 이 문서 갱신은 그다음 커밋 — 아래 변경 이력 '5차 통합 점검'). 5차 묶음은 I5-A(압축 속 파일 고쳐 다시 넣기 — 이름 바뀜·일집 먼저 닫기, 커밋 7e0903d)·
I5-B(안전·정직 5건, 커밋 875b9e6) 둘 — 둘 다 main 에 커밋·푸시돼 있었다(되돌린 묶음 없음 — stash 다섯은 그대로, 손대지 않음).
통합 점검에서 한 것: ① 묶음 사이·앞 기능과의 교차 시험 **`Wave5IntegrationTests`(38)** — 다시 넣기 추적(I5-A) × 여러 파일 열기·같은 이름(같은 이름 항목·다른 항목 이름으로 바꾸기·이름이 같은 두 압축)·
암호 압축(ZIP·7Z)·나눔 압축(.z01·.7z.001), 자기 자신 덮어쓰기 막기(I5-B) × 알아서 풀기·여러 개 풀기·창 없이 풀기(실제 exe)·압축된 TAR, tar 링크 알림 × 검사 요약(여러 개 검사·bzip2·xz 로 감싼 tar)·여러 개 풀기 요약.
처음 돌림에서 28개 중 **5개 실패 → 고친 것 셋**(아래 변경 이력 '5차 통합 점검'):
(가) 여러 개 '여기에 풀기'에서 한 압축 안의 같은 이름 항목이 **같이 푸는 다른 압축**을 덮음(I5-B 가 남긴 minor — 나.zip 이 안쪽 것으로 바뀐 뒤 그 내용이 풀리고, '다 푼 뒤 휴지통으로'면 원래 나.zip 이 사라짐) → 묶음의 압축 전부(+조각)를 지킴.
(나) 다음 시작 [압축에 넣기]에서 그 순간 고친 파일을 다른 프로그램이 혼자 잡아 못 읽으면 **압축의 원래 항목이 빠지고 '넣었어요' + 고친 파일까지 지움**(I5-A 가 남긴 minor) — 뿌리는 고치기 엔진(ZIP·7Z)의 바꿔 넣기:
덮어쓰기로 넣을 새 파일을 못 읽으면 원래 항목만 지운 채 바꿨다(일반 넣기의 [덮어쓰기]도 같음) → 엔진은 원래 항목을 되살리고, 다음 시작 넣기는 먼저 옆 폴더에 사본을 떠서 넣는다(못 읽으면 압축·파일 그대로, 안내).
(다) 암호 압축의 남은 파일 여럿을 다음 시작에 넣을 때 **파일마다 암호를 다시 물음** → 그 알림 동안 한 번만(메모리에만).
② 실제 Release exe(스크래치 사본)로 재사용장치 약속 다시 확인 — 18가지·로그 확인 5가지 **모두 기대대로**(종료 0·1·2, 표준 오류 한 줄, 실패 로그 자리) — I5-B 판과 같아 **재사용장치에 바뀌는 점 없음**(아래 '일집-dist 재배포' 절 5차 줄). ③ 계약 테스트: 전체 2960 → 2998(+38) — 통과 2998·건너뜀 0·**실패 0**(Debug 28분 34초·Release 29분 23초·`ILJIP_TEST_TZ=UTC` 30분 32초 — 2026-10-20 20:13~21:48, 한 번에 하나씩. 상태 줄·같은 이름 둘 넣기를 더하기 전 2990 판도 Debug 29분 10초 실패 0).
Release 빌드(`Iljip.csproj`) 경고 0·오류 0. 세 번 돌리는 동안 `%LOCALAPPDATA%\Iljip\logs` 두 파일(10/12 624,749·10/16 397,374) 크기·시각 그대로, `%LOCALAPPDATA%\Iljip\temp\open` 없음.
**새 파일 1개**: `Iljip.Tests/Contract/Wave5IntegrationTests.cs`. **고친 파일 8개**: `Iljip/Services/BatchExtractor.cs`·`Iljip/Services/CollisionResolver.cs`·`Iljip/Services/Editing/EditPlanner.cs`·
`Iljip/Services/Editing/ZipArchiveEditor.cs`·`Iljip/Services/Editing/SevenZipArchiveEditor.cs`·`Iljip/ViewModels/MainViewModel.Edit.cs`·`Iljip/ViewModels/MainViewModel.Leftovers.cs`, 시험 `Iljip.Tests/Contract/HeadlessNoticeTests.cs`(`RunIljip` 를 internal 로 — 같이 씀)
(+ 문서 셋 이 파일·`Iljip.Tests/README.md`·`Iljip.Tests/MANUAL_CHECKLIST.md` — 문서 갱신 커밋). 기대를 바꾸거나 약하게 한 기존 시험 없음.
**설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음, 셸 확장·파일 연결·레지스트리 손대지 않음). 사람이 볼 것: MANUAL_CHECKLIST **27절**(5차 통합 — 15분) + 25·26절.
아래는 그 전(I5-B·I5-A 작업 끝 — 이제 각각 875b9e6·7e0903d 로 커밋됨) 기록 — '커밋 전'은 그때 기준.

**I5-B(최종 감사 안전·정직 5건 — 압축 자신 덮어쓰기·[바꾸기] 취소·창 없이 풀기 인자·tar 링크·열린 파일 덮어쓰기) 작업 끝 — 커밋 전**(→ 875b9e6 으로 커밋·푸시됨)
(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함. 출발점 main 7e0903d = I5-A 커밋). 최종 감사의 지적 다섯을 고쳤다(아래 변경 이력 'I5-B').
① **IA-SELFNAME**: '여기에 풀기'(우클릭·일집 창·여러 개)가 압축 안의 같은 이름 항목(예: `첨부.zip` 안의 `첨부.zip`)으로 **풀고 있는 압축 파일 자신·그 조각**을 덮지 않는다 —
그 항목만 '첨부 (1).zip'로 비켜 풀고 결과 창에 알린다(같은 이름 창에서 덮어쓰기를 골라도, 설정이 '같은 이름이면 덮어쓰기'여도). '다 푼 뒤 압축 파일을 휴지통으로'는 원래 압축(바이트 그대로)을 보내고 안쪽 것은 '첨부 (1).zip'로 남는다
(예전 7z 는 바깥 압축이 안쪽 것으로 바뀐 뒤 그것이 휴지통으로 가서 안쪽 압축을 잃었다). 링크를 내용 없이 만들었거나 잠긴 파일을 건너뛴 압축은 휴지통으로 보내지 않는다(압축 파일에만 온전한 것이 있음).
② **IB-zip-replace-cancel-deletes-old**: ZIP(그리고 같은 길의 TAR·GZ·BZ2)을 [바꾸기]로 만들 때 옆 임시 파일(`.~이름.8글자.tmp`)에 다 쓴 뒤에만 바꿔 넣는다 — 취소·쓰기 실패면 기존 파일 바이트 그대로, 임시 파일 0개.
③ **IX-F5**: `--extract-to` 에 없는 경로·폴더·빈 문자열(또는 인자 모자람)을 주면 **창 없이** 쉬운 한국어 한 줄(표준 오류)·종료 코드 1·오류 로그 `Headless extract failed: <경로>` + 이유(예전엔 일집 창과 '문제가 생겼어요' 창이 떠서 끝나지 않음).
④ **IX-F2**: tar 하드·심볼릭 링크는 예전처럼 내용 없는 파일로 만들되(되살리기는 결정 기록대로 제외) 결과 창·여러 개 풀기 요약·검사 결과에 '링크 N개는 내용 없이(0바이트) 만들었어요…'를 알린다.
창 없이 풀기는 표준 오류 한 줄 + **종료 코드 1**(★ 결정 — 아래 '일집-dist 재배포' 절 I5-B 줄: 재사용장치가 반디집으로 다시 풀어 링크를 살림).
⑤ **IA-OPENFILE-OVERWRITE**: 덮어쓸 파일을 다른 프로그램(엑셀 등)이 열어 두었으면 그 파일만 건너뛰고(원래 파일 그대로) 나머지는 다 풀고 '다른 프로그램이 쓰는 중이라 못 바꾼 파일 N개(이름)'를 알린다 —
관리자 권한으로 다시 하자는 권유는 진짜 권한 문제(접근 거부)일 때만.
**I5-B 새 파일 4개**: `Iljip/Services/ExtractionNotes.cs`, 시험 `Iljip.Tests/Contract/ExtractSafetyNoticeTests.cs`(22)·`Iljip.Tests/Contract/CompressReplaceTests.cs`(16)·`Iljip.Tests/Contract/HeadlessNoticeTests.cs`(3 — 실제 exe 2).
고친 파일 18개: `Iljip/App.xaml.cs`·`Iljip/Services/ArchiveTestService.cs`·`Iljip/Services/BatchExtractor.cs`·`Iljip/Services/BZip2ArchiveService.cs`·`Iljip/Services/CollisionResolver.cs`·`Iljip/Services/CompressedTarArchiveService.cs`·
`Iljip/Services/ElevationService.cs`·`Iljip/Services/GZipArchiveService.cs`·`Iljip/Services/OutputFiles.cs`·`Iljip/Services/SharpCompressArchiveServiceBase.cs`·`Iljip/Services/StartupDecision.cs`·`Iljip/Services/TarArchiveService.cs`·
`Iljip/Services/ZipArchiveService.cs`·`Iljip/ViewModels/MainViewModel.cs`·`Iljip/ViewModels/MainViewModel.CommandLine.cs`·`Iljip/ViewModels/MainViewModel.Test.cs`, 시험 `Iljip.Tests/Contract/CollisionTests.cs`·`Iljip.Tests/Contract/StartupDecisionTests.cs`
(+ 문서 셋 이 파일·`Iljip.Tests/README.md`·`Iljip.Tests/MANUAL_CHECKLIST.md`).
**기대를 바꾼 기존 시험 둘(요청된 동작 바뀜 — 약하게 한 것 아님)**: `StartupDecisionTests` — '없는 압축'·빈 폴더·인자 둘을 '창 없이 풀기가 아님'으로 보던 줄을 '창 없이 풀기(그리고 `HeadlessArgumentProblem` 한 줄)'로(IX-F5 가 바로 이 판정을 바꾸라는 지적),
`CollisionTests.덮어쓸_파일이_잠겨_있으면_…` — 예전 '한국어 오류로 멈춤'을 '그 파일만 건너뛰고 나머지는 다 풂·건너뜀 1·알림에 이름·남은 임시 파일 0'으로(IA-OPENFILE-OVERWRITE 가 바로 이 동작을 바꾸라는 지적 — 잠긴 파일 그대로·남은 것 없음은 그대로 확인).
계약 테스트: 전체 2960 — 통과 2960·건너뜀 0·**실패 0**(Debug 52분 30초 — 2919 + I5-B 41). 전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 그대로. **설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음). 사람이 볼 것: MANUAL_CHECKLIST 26절.
아래는 그 전(I5-A — 이제 main 7e0903d 로 커밋됨) 기록 — '커밋 전'은 그때 기준.

**I5-A(압축 속에서 연 파일 — 이름 바꾸기·일집 먼저 닫기에도 고친 내용을 잃지 않게) 작업 끝 — 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함. 출발점 main f61b472).
감사 X3 의 지적 셋(X3-F2 이름을 바꾸면 놓침·X3-F1 연 채로 일집을 닫으면 그 뒤 저장이 다음 시작 청소로 말없이 지워짐·I3-3 닫기)을 고쳤다(아래 변경 이력 'I5-A').
① 연 파일을 같은 순번 폴더 안에서 이름을 바꾸면(Light PDF 탭 '이름 바꾸기') 새 이름을 따라가 묻고, **압축에는 원래 항목 이름 그대로** 넣는다(묻지 않음 — 결정).
② 일집을 닫을 때 연 파일이 다른 프로그램에 아직 잡혀 있으면 '압축 속에서 연 파일 N개가 아직 다른 프로그램에서 열려 있어요 … 일집을 닫을까요?' [닫기][취소], 고친 파일은 예전처럼 [넣기][넣지 않기][취소].
③ 닫은 뒤 저장한 파일은 지우지 않고 남기고(작업 폴더의 owner.lock 안에 연 파일 목록), 다음 시작에 '압축 속에서 열어 고친 파일 중 압축 파일에 아직 넣지 않은 것이 있어요' [압축에 넣기][폴더 열기][버리기][나중에].
**I5-A 새 파일 3개**: `Iljip/ViewModels/MainViewModel.Leftovers.cs`, 시험 `Iljip.Tests/Contract/EntryRenameExitTests.cs`(12)·`Iljip.Tests/Contract/EntryLeftoverTests.cs`(15).
고친 파일 6개: `Iljip/Services/IFileChangeSource.cs`·`Iljip/Services/ArchiveWorkspace.cs`·`Iljip/Services/TempJanitor.cs`·`Iljip/ViewModels/MainViewModel.EntryOpen.cs`·`Iljip/App.xaml.cs`·`Iljip.Tests/TestSupport/FakeServices.cs`(+ 문서 셋 이 파일·`Iljip.Tests/README.md`·`Iljip.Tests/MANUAL_CHECKLIST.md`).
계약 테스트: 전체 2919 — 통과 2919·건너뜀 0·**실패 0**(Debug 36분 37초 — 2892 + I5-A 27). 기대를 바꾸거나 약하게 한 기존 시험 없음. 전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 네 파일 크기·시각 그대로. 감사 X3 다시 돌림(실제 Light PDF 코드 + 고친 Debug 빌드, 저장소 밖 하네스) — 잃은 편집 0.
**설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음). Light PDF 저장소(`D:\Python\PDF_editor`·`PDF_editor-wt3`)는 읽기만. 사람이 볼 것: MANUAL_CHECKLIST 25절.
아래는 그 전(I4-1 들임) 기록.

**2026-10-18 I4-1 을 main 에 들이고, 흘려 쓴 암호 ZIP 고침을 그 위에 얹었다**(아래 변경 이력 '2026-10-18 I4-1 들임'·'흘려 쓴 암호 ZIP'). I4-1 은 4차 3차 검증에서 제품 동작 수락 기준(C0~F3·G2~G4)을 모두 통과했고,
되돌린 까닭은 이 문서의 '새 파일' 목록·보고의 바뀐 파일 목록 누락 하나뿐이어서 오케스트레이터가 보관분 `iljip-wave4-I4-1-failed` 를 4차 통합 점검(73d7fbc) 위에 `git stash apply` 로 얹었다(코드 충돌 없음 — 문서 셋 충돌만 손으로 합침).
I4-1 새 파일 19개: `Iljip/Services/ArchiveTestService.cs`·`Iljip/Services/ErrorCatalog.cs`·`Iljip/Services/ZipRepairer.cs`·`Iljip/ViewModels/MainViewModel.Test.cs`·`Iljip/Views/ProblemDialog.xaml`·`Iljip/Views/ProblemDialog.xaml.cs`, 시험 `Iljip.Tests/Contract/ArchiveTestReview2Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReview3Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReview4Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReview5Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReview6Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReview7Tests.cs`·`Iljip.Tests/Contract/ArchiveTestReviewTests.cs`·`Iljip.Tests/Contract/ArchiveTesterTests.cs`·`Iljip.Tests/Contract/ErrorCatalogTests.cs`·`Iljip.Tests/Contract/UiStringAuditTests.cs`·`Iljip.Tests/Contract/ZipRepairHonestTests.cs`·`Iljip.Tests/Contract/ZipRepairTests.cs`·`Iljip.Tests/TestSupport/RepairOracle.cs`.
흘려 쓴 암호 ZIP 고침(별도 세션 2026-10-16 — 패치 `D:\Python\일집-build\zipcrypto-fix` 1·2번을 이 위에 얹음): 새 `Iljip/Services/StreamedZipHeaders.cs`, 시험 `Iljip.Tests/Contract/StreamedEncryptedZipTests.cs`(19)·`StreamedEncryptedZipCheckTests.cs`(8).
계약 테스트: 전체 2892 — 통과 2892·건너뜀 0·**실패 0**(Debug 35분 55초·Release 29분 58초·`ILJIP_TEST_TZ=UTC` 26분 15초 세 번 모두 — 4차 통합 점검 2411 + I4-1 454 + 흘려 쓴 암호 ZIP 27). 기대를 바꾸거나 약하게 한 기존 시험 없음(`ArchiveEditReview2Tests` 두 시험은 재료만, `ZipRepairHonestTests` 는 설명만)
**stash 다섯 그대로**(drop 안 함 — `@{0}` `iljip-wave4-I4-1-failed` 는 이제 들어왔으니 다시 apply 하지 말 것). **설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음).
아래는 I4-1 작업 기록(커밋 전 시점)과 4차 통합 점검 기록(I4-1 들이기 전 시점) — 'stash 에 있다'·'커밋 전'·'다시 할지는 사람이 정함' 같은 말은 그때 기준.

**I4-1 작업 기록(2026-10-17 검증 지적 수정 2차 후 — 커밋 전 시점)**

**I4-1(IJ3-02 다시 — 4차) 작업 끝 — 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함, 출발점 `git stash apply` 만). `stash@{0}` `iljip-wave3-IJ3-02-failed` 를 main(f49c672) 위에 얹고
(drop 안 함 — stash 넷 그대로), 압축 검사(I2-7)·오류 원인(I2-8)은 보관분 그대로 들이고(영어 문구 하나만 한국어로), 깨진 ZIP 고치기(I3-12)는 **'정직한 보수 모드'로 다시 짰다**
(원래 개수는 목록이, 살렸다는 증거는 데이터 검사값이, 훑기는 맨 앞에서 이어지고 끝이 확인된 사슬만 — 아래 변경 이력 'I4-1'). 시험의 로그는 사용자 로그 폴더가 아니라 시험 전용 폴더로(B).
검토로 **망가뜨림 시험**(저장소 밖 도구 — 표본 ZIP 마다 바이트·칸 망가뜨림 수천 가지를 오라클로 판정)을 돌려 찾은 거짓 성공을 고쳤다(고친 ZIP 을 다른 프로그램이 못 엶·검사의 거짓 '문제없어요'·0바이트 풀기·
손상을 '암호가 틀림'으로·깨진 유니코드 이름으로 '살렸어요' — 아래 변경 이력 'I4-1' 의 '검토'). 마지막 코드로 고치기 22,000가지(씨앗 11)·검사 22,000가지 두 번(씨앗 10·11) 모두 오라클 통과(그 전 돌림들이 찾은 것은 모두 고치고 계약 시험 한 줄씩).
계약 테스트: 전체 2821 — 통과 2821·건너뜀 0·**실패 0**(Debug — 3차의 2408 에서 +413: I4-1 검토 전 2803, 검토에서 +18). Release 빌드 경고 0·오류 0. 전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 그대로(확인 — 11:26·12:22 두 번 모두 `error-20261012.log` 624,749·`error-20261015.log` 26,081,797·`error-20261016.log` 397,374(09:19:02)로 같음. 10/16 로그는 08:27~09:19 에 다른 세션의 작업 폴더 `D:\Python\일집-build\wt-zipcrypto`(로그 격리가 없는 main 갈래)의 시험이 쓴 것 — 기록 224개 — 스택에 나온 소스 경로 882줄이 모두 그 폴더이고 I4-1 작업 폴더의 것은 없음). 예전 시험 기록이 섞인 10/13·10/14 로그는 `D:\Python\일집-build\시험로그-보관\` 로 옮김(지우지 않음 —
10/15 로그(26MB)도 시험 기록이 대부분이지만 지시 범위 밖이라 그대로 둠, 옮길지는 사람이 정함).
**설치·등록·재배포는 하지 않았다**: `D:\Python\일집-dist` 그대로, 셸 확장·파일 연결 반영 안 함, `Iljip\bin` 에 exe 없음. 사람이 볼 것: MANUAL_CHECKLIST 24절(특히 'I4-1' 줄).
**I4-1 검증 지적 수정(1차) 끝 — 커밋 전**: 검증이 찾은 major 넷(목록 이름 끝 '/' 로 깨진 파일을 검사 '문제없어요'·풀기 종료 코드 0, ZIPX 의 목록 이름 손상을 '문제없어요',
고치기 목록 모드가 끝 글자만 깨진 빈 파일을 빼고 '모두 살렸어요', 훑기 모드가 빈 항목의 깨진 길이 칸으로 저장 docx 안 부품을 '살렸어요')과 작은 지적 다섯을 고쳤다(아래 변경 이력 'I4-1 검증 지적 수정').
계약 테스트: 전체 2839 — 통과 2839·건너뜀 0·**실패 0**(Debug, 53분 — 2821 에서 +18 `ArchiveTestReview6Tests`). Release 빌드 경고 0·오류 0. 전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 세 파일 크기·시각 그대로.
저장소 밖 망가뜨림 시험(검증 도구 그대로): 작은 ZIP·서술자 ZIP·ZipCrypto 의 목록·로컬 머리 바이트마다 9가지 값 12,281가지에서 거짓 성공 0(검증 때 목록 8건·로컬 1건),
목록 없는 ZIP 4,197가지에서 안쪽 부품 '살렸어요' 0(남은 것은 cut 5·7 의 알려진 한계 — 검증 때와 같은 묶음, 새로 생긴 것 0), 반디집 ZIP·ZIPX 목록 이름·추가 필드·이름 길이·위치 칸 7,744·6,609가지에서
검사 '문제없어요'인데 다르게 풀림·풀기 종료 코드 0 인데 다르게 풀림 0, 형식별 무작위 420가지 0.
**I4-1 검증 지적 수정(2차, 2026-10-17) 끝 — 커밋 전**: 검증이 찾은 major 여섯을 고쳤다(아래 변경 이력 'I4-1 검증 지적 수정(2차)') — 폴더 기록을 빈 Deflate 흐름으로 쓰는 ZIP(Java·SharpZipLib·Python)의
폴더를 파일로 세던 '7개를 모두 살렸어요'(파일 6개), 맥 NFD 이름 반디집 ZIP 을 낱자 이름('ㅁㅐㄱ_…')으로 '모두 살렸어요', 목록 없는 서술자 ZIP 에서 이름 끝이 '/' 로 깨진 파일을 '살렸어요',
0 으로만 채운 .zip 을 검사 '문제없어요. 파일 0개'·창 없이 풀기 종료 코드 0, 끝 머리 '이 디스크 번호' 1바이트 손상을 '조각이 없어요', 새 복제본에서 sfx_default 픽스처를 요구하던 시험 한 줄(G2·G3).
계약 테스트: 전체 2862 — 통과 2862·건너뜀 0·**실패 0**(Debug, 28분 — 2839 에서 +23 `ArchiveTestReview7Tests`). Release 빌드 경고 0·오류 0. git 에 있는 파일만 복사한 새 복제본(로컬 픽스처 없음)에서
`ArchiveTesterTests`·`ArchiveTestReview7Tests` 통과 110·건너뜀 2·실패 0(G3). 전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 세 파일 크기·시각 그대로.
저장소 밖 망가뜨림 시험(검증 도구 그대로 다시): Java식 서술자 ZIP 목록 2,585가지 위반 0(검증 때 7,113), 작은·서술자·ZipCrypto 목록·로컬·끝 머리·0 덩어리·자르기·두 자리 7,640가지 위반 0
(검증 때 0 덩어리 'z0_4096' 이 '문제없어요. 파일 0개'), AES·한글 암호 ZipCrypto 끝 머리 373가지와 ZipCrypto 목록 2,153가지에서 '고치기를 권하지 않는 문제' 0(검증 때 30). 목록 없는 ZIP(훑기 모드)의 남은 위반은 모두 cut 5·7 의 알려진 한계
(bit 0·방식 칸이 깨진 항목을 '확인 못 함'으로 옮김, 글자로 읽히는 깨진 이름 — 검증 때와 같은 묶음, 폴더를 세던 것 0. 반디집 ZIP 목록 없음 로컬 머리는 앞 3,500가지만 다시 — 같은 묶음 + 판정기가 경로의 'data' 를 영어로 셈). 정상 ZIP 54종 C0 은 하네스 자체 한계 3줄(System.IO.Compression 이 CP949 이름·BZip2 를 못 읽음) 밖 0,
반디집 실제 특수 이름 10종(NFD 한글·폴더, 이모지, 중·일·베트남어, NFD 라틴) 모두 원본과 같은 이름, 실제 Iljip.exe --extract-to 16가지(0 파일 포함) 계약 그대로.
그 전(I4-1 작업 끝) 기록은 위.

**4차 통합 점검 기록(2026-10-18 — I4-1 들이기 전 시점)**

**4차 통합 점검 끝 — 커밋·푸시 완료**(코드·시험 bea2cd6, 이 문서 갱신은 그다음 커밋 — 아래 변경 이력 '4차 통합 점검'). 4차 묶음은 **I4-1 하나**(IJ3-02 다시 — 압축 검사 I2-7·오류 원인 문제 창 I2-8·
깨진 ZIP 고치기 I3-12 를 '정직한 보수 모드'로)였고, 구현 → 검증 지적 수정 1차·2차 뒤 **3차 검증에서 되돌려 `stash@{0}` `iljip-wave4-I4-1-failed`** 에 있다(drop 안 함).
되돌린 까닭은 major 하나 — 이 문서에 'I4-1 커밋에 넣을 새 파일' 목록이 없고(새 파일 19개, `Iljip/Views/ProblemDialog.xaml.cs` 는 어디에도 이름이 없음) 보고의 바뀐 파일 목록에
두 파일(`Wave3IntegrationTests.cs` 수정·`ArchiveTestReview6Tests.cs` 새 파일)이 빠진 **문서·보고 누락**이다. 제품 동작 쪽 수락 기준(C0~F3·G2~G4 — 멀쩡한 ZIP 오보 0, 검사·풀기·고치기 거짓 성공 오라클 0,
재사용장치 약속, 전체 시험 2862 실패 0)은 3차 검증에서 모두 통과했다 — 다시 할지는 사람이 정함(아래 '다음 단계' 4).
**stash 다섯**(번호가 하나씩 밀림): `@{0}` I4-1 · `@{1}` `iljip-wave3-IJ3-02-failed` · `@{2}` `iljip-wave2-W2-08-failed` · `@{3}` `iljip-wave2-W2-04-failed` · `@{4}` `iljip-wave1-W1-B-failed` — 모두 그대로.
**통합 점검에서 main 에 들인 것은 '시험 로그 격리' 하나**(I4-1 의 B 몫 — 보관분과 **같은 글자 그대로**라 보관분을 다시 얹어도 이 부분은 겹치지 않음, `git merge-tree` 로 확인):
계약 테스트와 시험이 띄우는 Iljip.exe 가 사용자 진짜 로그 `%LOCALAPPDATA%\Iljip\logs`(재사용장치 지켜보기 명령이 보는 곳)에 쓰지 않는다 — 로그 폴더를 `AppPaths.Root\logs` 로,
시험 프로세스는 `%TEMP%\IljipTests\appdata-<pid>`(끝나면 지우고, 남은 것은 다음 시험이 10분 뒤 치움), 시험이 띄우는 exe 는 환경 변수 `ILJIP_TEST_APPDATA`(`ModuleInit.ApplyTo`).
사용자 PC 에서는 예전과 같은 폴더(환경 변수가 없으면 `AppPaths.DefaultRoot()` = `%LOCALAPPDATA%\Iljip`). 새 시험 `Wave4IntegrationTests` 3개.
계약 테스트 실패 0(전체 2411 — 통과 2411·건너뜀 0, **Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번 모두**). Release 빌드 경고 0·오류 0(출력 `D:\Python\일집-build\dotnet\Iljip\bin\Release`).
세 번 돌리는 동안 `%LOCALAPPDATA%\Iljip\logs` 파일 크기·시각 그대로(확인). 실제 Release exe 로 창 없이 풀기 약속을 따로 확인(아래 변경 이력).
사용자 로그 폴더: 10/13·10/14 로그(85MB·255MB — IJ3-02 시험 기록)는 I4-1 때 `D:\Python\일집-build\시험로그-보관\` 로 옮겨져 있다(지우지 않음). 10/15(26MB)·10/18(380KB — 10/18 04:10~04:24
I4-1 을 되돌린 뒤 격리가 없던 main 으로 돈 시험 기록뿐) 로그는 그대로 — 지울지는 사람이 정함(일집 동작과 무관, 지켜보기 명령은 `IljipTests` 줄을 뺌).
**설치·등록·재배포는 하지 않았다**: `D:\Python\일집-dist` 는 아직 2026-06-23 판, 셸 확장·파일 연결 반영 안 함, `Iljip\bin` 에 exe 없음.
다른 세션의 `fix-zipcrypto-descriptor` 갈래(`D:\Python\일집-build\wt-zipcrypto` — 흘려 쓴 암호 ZIP 풀기)와 그 패치(`D:\Python\일집-build\zipcrypto-fix`)는 건드리지 않았다(이번에 바꾼 파일과 겹치지 않음 — 패치는 이 묶음 뒤에 얹을 예정).
그 전(3차 통합 점검) 기록:

**3차(IJ3-01·IJ3-03) 통합 점검 끝 — 커밋·푸시 완료**(코드·시험 5e534c1, 이 문서 갱신은 그다음 커밋). 3차 묶음: IJ3-01(W2-04 다시 — 끌어 꺼내기·Ctrl+C·그림 미리 보기·바로 열기 나머지) 커밋 f1ef006,
IJ3-03(인터넷 표시 MOTW·7-Zip 라이선스 원문) 커밋 981a048 — 둘 다 푸시됨. **IJ3-02(압축 검사·깨진 ZIP 고치기 — W2-08 다시)는 검증에서 되돌려 `stash@{0}` `iljip-wave3-IJ3-02-failed`** 에 있다
(drop 안 함 — 다시 할지는 사람이 정함, 아래 '향후 작업'). stash 넷(`stash@{0}` IJ3-02 · `@{1}` W2-08 · `@{2}` W2-04 · `@{3}` W1-B)은 그대로.
계약 테스트 실패 0(전체 2408 — 통과 2408·건너뜀 0, **Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번 모두**). Release 빌드 경고 0·오류 0(출력 `D:\Python\일집-build\dotnet\Iljip\bin\Release`).
통합 점검에서 한 것(아래 변경 이력 '3차 통합 점검'): 묶음 사이 상호작용 시험 `Wave3IntegrationTests`(13 — 모두 처음부터 통과, 제품 동작 어긋남 없음),
**재배포 뒤 진짜 egg 지켜보기**를 위해 창 없이 풀기 실패 로그에 압축 경로(`Headless extract failed: <경로>` — 표준 오류는 그대로)와 로그 쓰기 잠깐 막힘 다시 하기,
**셸 확장 설치 `-SystemVolume`**(이 PC 는 '새 앱 저장 위치'가 `D:\WindowsApps` — OneDrive 패키지가 0x80070032 로 실패 중임을 확인), 발행 시험의 보안 프로그램 잠깐 잠김(MSB3491) 다시 하기.
10/14 23:54·10/15 00:03 Windows 업데이트가 PC 를 두 번 다시 시작했다(시스템 로그 1074 — MoUsoCoreWorker·TrustedInstaller). 그 뒤 작업 폴더는 깨끗했고 IJ3-03 은 처음부터 다시 해 커밋 — 잃은 것 없음.
**설치·등록·재배포는 하지 않았다**: `D:\Python\일집-dist` 는 아직 2026-06-23 판, 셸 확장(지금 이 PC 에 `Iljip.ShellExtension` 패키지 없음)·파일 연결 반영 안 함, `Iljip\bin` 에 exe 없음.
그 전(IJ3-03) 기록:

**IJ3-03(인터넷 표시 MOTW 전파·7-Zip 라이선스 원문 대조 — 사용자 결정 I2-15) 작업 끝 → 커밋 981a048·푸시(3차 통합 점검 때)**(그때 기록: 커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함).
10/14 23:54·10/15 00:03 Windows 업데이트가 PC 를 두 번 다시 시작해, 10/15 재개 때 작업 폴더가 깨끗한 f1ef006(IJ3-01 커밋) 위에서 처음부터 했다.
stash 는 손대지 않음 — 지금 `stash@{0}` `iljip-wave3-IJ3-02-failed`(IJ3-02 — 검증에서 되돌림), `stash@{1}` `iljip-wave2-W2-08-failed`, `stash@{2}` `iljip-wave2-W2-04-failed`(IJ3-01 이 다시 함), `stash@{3}` `iljip-wave1-W1-B-failed`.
새 파일(아직 git 에 없음 — 커밋할 때 같이): `Iljip/licenses/7-Zip-License.txt`(원문 — 바이트 그대로), 시험 `Iljip.Tests/Contract/InternetMarkTests.cs`·`ThirdPartyLicenseTests.cs`.
계약 테스트 실패 0(전체 2394 — 통과 2394·건너뜀 0, Debug. 그 전 2345). 아래 변경 이력 'IJ3-03'. **진짜 오피스 '제한된 보기'·탐색기 우클릭은 MANUAL_CHECKLIST 21절, 창 없이 풀기는 8절 마지막 줄(실기기 확인 필요)**.
**설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음 — 발행 시험은 시험 임시 폴더에만). 그 전(IJ3-01) 기록:

**IJ3-01(W2-04 다시 — 끌어 꺼내기·Ctrl+C I1-4·그림 미리 보기 I3-7·바로 열기 나머지 I1-3) 작업 끝 → 커밋 f1ef006·푸시**(그때 기록: 커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함).
보관분 `stash@{1}` `iljip-wave2-W2-04-failed` 를 `git stash apply` 로 얹고 충돌은 main 의 뜻대로 손으로 풀었다 — **stash 셋은 drop 하지 않았다**(그대로). 충돌 표시를 지운 뒤
색인만 `git reset -q` 로 비웠다(작업 폴더 그대로 — 바뀐 파일은 모두 '스테이징 안 됨', 새 파일은 untracked). 새 파일(아직 git 에 없음 — 커밋할 때 같이):
`Iljip/Services/`(`ClipboardCopyPlan.cs`·`DragPayloadBuilder.cs`·`IClipboardService.cs`·`IImageDecoder.cs`·`LazyEntryExtractor.cs`·`TempJanitor.cs`·`VirtualFileDataObject.cs`)·`Iljip/ViewModels/`(`ImagePreviewViewModel.cs`·`MainViewModel.EntryCopy.cs`),
시험 `Iljip.Tests/Contract/`(`ClipboardCopyTests.cs`·`DragOutTests.cs`·`EntryOpenTests.cs`·`EntryWorkspaceTests.cs`·`ImagePreviewTests.cs`)·`Iljip.Tests/TestSupport/`(`DispatcherThread.cs`·`TestZip.cs`).
계약 테스트 실패 0(전체 2345 — 통과 2345·건너뜀 0, Debug. 그 전 2175). 아래 변경 이력 'IJ3-01'. **진짜 탐색기 끌어 놓기·붙여넣기·미리 보기 화면은 MANUAL_CHECKLIST 20절(실기기 확인 필요)**.
**설치·등록·재배포는 하지 않았다**(`D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음). 그 전(Wave 2 통합 점검) 기록:

**Wave 2 통합 점검 끝 — 커밋·푸시 완료**(W2-01·02·03·05·06·07·09·10·11·12 는 묶음마다 커밋돼 있었고, 통합 점검은 아래 변경 이력 'Wave 2 통합 점검').
계약 테스트 실패 0(전체 2175 — 통과 2175·건너뜀 0, **Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번 모두**). Release 빌드 경고 0·오류 0(출력은 `D:\Python\일집-build\dotnet\Iljip\bin\Release`).
통합 점검에서 고친 것은 셋 — 설정 저장이 보안 프로그램 검사 중 1175 로 그냥 실패하던 것, RAR4 시각이 7z.dll 과 .NET 의 시간대 편차가 다르면 어긋나던 것(`ILJIP_TEST_TZ=UTC` 실행에서 찾음 — 한국 PC 실사용에서는 같은 값),
7z 만들기(보통) 취소가 바쁜 PC 에서 3초를 넘던 것(솔리드 취소 = 블록 하나 압축 시간 → 블록 8m → 2m, 취소 0.3~0.7초).
새 시험 70개 — 묶음 사이 상호작용 61 + 실제 exe 1 + 설정 저장 1 + 시각 7(전체 2105 → 2175, 모두 통과). 자동 풀림 시각 시험 하나는 기준만 바로잡음(스텁은 따로 도는 프로세스라 OS 시간대 — 정확히 같은 값 비교 그대로).
**되돌린 묶음 둘은 stash 에 그대로**(drop 안 함 — 다시 할지는 사람이 정함): `stash@{0}` `iljip-wave2-W2-08-failed`(압축 검사 `--test` I2-7·오류 원인 문제 창 I2-8·깨진 ZIP 고치기 I3-12),
`stash@{1}` `iljip-wave2-W2-04-failed`(끌어 꺼내기·Ctrl+C I1-4·그림 미리 보기 I3-7·압축 속 파일 열기 I1-3 의 나머지 — 열기·다시 넣기 바탕은 W2-06 이 먼저 들임), `stash@{2}` `iljip-wave1-W1-B-failed`(W2-01 이 다시 함 — 이제 쓸 일 없음).
**설치·등록·재배포는 하지 않았다**: `D:\Python\일집-dist`(재사용장치 배포본)는 아직 2026-06-23 판, 셸 확장(MSIX 0.4.0)·파일 연결은 이 PC 에 반영 안 함, `Iljip\bin` 에 exe 없음.
그 전(W2-12) 기록:

**W2-12(파일 연결 I1-1·우클릭 메뉴 정비 I1-2·오른쪽 끌기 메뉴 I3-10) 작업 끝, 커밋 전**(커밋은 오케스트레이터 몫 — `git add`·`commit`·`stash` 안 함, stash 셋은 손대지 않음).
**설치·등록은 하지 않았다** — 반영은 아래 '셸 확장·파일 연결 반영(W2-12)' 절차(사람이 할 일). 새 파일(아직 git 에 없음 — 커밋할 때 같이):
`Iljip/Services/Shell/`(`IRegistryRoot.cs`·`FileAssociationRegistrar.cs`·`FileAssociationCommand.cs`·`ShellMenu.cs`·`ShellCommandLine.cs`)·`Iljip/Services/SmartExtractPlanner.cs`·`Iljip/ViewModels/MainViewModel.Shell.cs`·`Iljip/icons/*.ico`(22개)·`packaging/Icons/MakeIcons.ps1`,
C++ `Iljip.ShellExtension/`(`IljipLaunch.cpp`·`IljipMenu.cpp`·`IljipDragDrop.cpp` — vcxproj 에 넣음), 시험 `Iljip.Tests/Contract/`(`FileAssociationRegistrarTests.cs`·`ShellCommandLineTests.cs`·`ShellMenuTests.cs`·`SmartExtractTests.cs`·`QuickCompressTests.cs`)·`Iljip.Tests/TestSupport/FakeRegistryRoot.cs`.
계약 테스트 실패 0(전체 2105 — 통과 2105·건너뜀 0, W2-11 검증 지적 1차 뒤 2041 에서 — 빌드한 셸 확장 DLL 시험 넷 포함). 아래 변경 이력 'W2-12'. 그 전(W2-11) 기록:

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

**다음 단계**(5차 통합 점검 후 — 모두 사람이 할 일. 4차 뒤와 같고 1·4 에 5차 몫을 더함):
1. **재배포 전 실제 PC 확인**: MANUAL_CHECKLIST **27절**(5차 통합 — 15분: 여러 파일을 연 채 닫기·암호 압축의 다음 시작 넣기·같이 푸는 압축 지키기·잠긴 파일 바꿔 넣기)·**26절**(I5-B — 20분)·**25절**(I5-A — 진짜 Light PDF 로 15분)·**24절**(I4-1 — 압축 검사·문제 창·깨진 ZIP 고치기, 25분)·**23절**(4차 통합 — 5분: 이 PC 에 시험용 환경 변수가 없는지·새 판 실패 기록이 진짜 로그 폴더에 남는지) + **22절**(3차 통합 — 20분) + **21절**(인터넷 표시·7-Zip 원문) + **20절**(끌기·Ctrl+C·미리 보기) + **19절**(Wave 2 통합) + 0·2·6·8·9절(진짜 알집·WinRAR 파일, 재사용장치 egg 해제, 7z.dll 이 막힌 PC).
   시간이 되면 10~17절(새 기능 화면 — 7z 만들기·나눠 만들기·같은 이름 창·압축 고치기·한 화면 압축 창·풀기 창·자동 풀림 EXE·시작 화면)
2. 확인되면 **일집-dist 재배포**(아래 '일집-dist 재배포' 절 — 재사용장치가 돌지 않는 시간에) → **반디집은 1~2주 그대로 두고** MANUAL_CHECKLIST 8절 끝 '재배포 뒤 1~2주 지켜보기'
   (진짜 egg 견본이 없어 합성 egg 로만 시험 — 일집이 못 푼 첨부는 재사용장치가 반디집으로 풀고, 일집 오류 로그에 `Headless extract failed: <경로>` 와 이유가 남음)
3. **셸 확장·파일 연결 반영**(아래 '셸 확장·파일 연결 반영(W2-12)' 절 — 인증서·`Rebuild.ps1`·`--register-associations`) → MANUAL_CHECKLIST 18절. 일집-dist 와는 따로(셸 확장은 `일집-build\package\Iljip.exe` 를 부름).
   **이 PC 는 '새 앱 저장 위치'가 D: 라 `.\Rebuild.ps1 -SystemVolume`** 으로(그냥 하면 0x80070032 로 실패할 수 있음 — 그 절의 '설치 볼륨' 줄)
4. 결정할 것(아래 '향후 작업'): I4-1 범위에서 남긴 결정 셋(목록·로컬 이름이 다른 파일은 '못 살린 파일'로 둘지, 암호 없이 고칠 때 암호 파일을 옮길지, 시험이 남긴 10/15·10/18 로그를 지울지),
   '다른 이름으로 저장'·자동 풀림 exe 에도 인터넷 표시를 옮길지, 지켜보기 뒤 반디집을 지울지,
   Windows '새 앱 저장 위치'를 C: 로 되돌릴지(PC 전체 설정 — OneDrive 앱 패키지도 같은 0x80070032 로 설치 실패 중),
   (5차) 편집기의 '다른 이름으로 저장'으로 같은 임시 폴더에 만든 새 파일도 따라갈지(I5-A 남김 — X3 s5a), 다음 시작 알림을 창 없는 날에도 띄울지·남긴 파일을 언젠가 지울지(지금은 기한 없음),
   링크가 든 tar 를 같은 이름 [덮어쓰기]로 풀 때 있던 파일을 0바이트로 바꾸는 것을 막을지(지금은 알림만 — '향후 작업' 5차 줄). (W2-04 는 IJ3-01 에서 다시 함, MOTW·7-Zip 원문은 IJ3-03 에서 끝 — 사용자 결정 I2-15)
(예전 다음 단계 — 1~8절 확인·W1-B 다시 시도 — 는 위에 합침. W1-B 는 W2-01 에서, 7z 만들기 W2-02, 분할 만들기 W2-03 에서 끝)

### MSIX 셸 확장

(예전) WPF 빌드 통과, C++ DLL 빌드 통과, MSIX 패키징까지 도달. manifest 에서 `desktop4:ItemType Type="Directory"` 항목을 제거함(desktop4 스키마 거부).
**(W2-12) 폴더 우클릭 메뉴 복원** — `desktop5:ItemType Type="Directory"`(desktop6 아님 — desktop5 가 폴더 항목을 받음)·`Directory\Background`. 패키지 0.4.0.0, 외부 위치(`uap10:AllowExternalContent`·mediumIL·win32App).
2026-10-14 이 PC 에서 NuGet SDK 의 `makeappx.exe pack`(전체 검사)으로 이 매니페스트 + 빌드한 DLL·exe 를 묶어 **'Package creation succeeded'** 까지 확인(서명·설치는 안 함 — 확인용 폴더는 지움).
남은 검증(사람): 아래 '셸 확장·파일 연결 반영(W2-12)' 절차 → 탐색기 메뉴 표시(MANUAL_CHECKLIST 18절).
**(3차 통합 점검) 설치 볼륨**: 이 PC 는 `Get-AppxDefaultVolume` 이 `D:\WindowsApps`(시스템 볼륨 C: 가 아님)이고, 같은 PC 에서 OneDrive 앱 패키지(`Microsoft.OneDriveSync`)가
10/11·10/13·10/15 에 '0x80070032 — 시스템 볼륨에 설치되어 있지 않아…'로 설치 실패 중이다(AppXDeploymentServer 로그 401·404). 일집 셸 확장(COM·탐색기 메뉴 확장이 든 외부 위치 패키지)도
같은 이유로 막힐 수 있어 `Install.ps1`·`Rebuild.ps1` 에 `-SystemVolume`(C: 의 앱 저장소에 넣음 — `Add-AppxPackage -Volume`)을 더했다. 안 주면 예전과 같고, 0x80070032 로 실패하면 이 옵션을 쓰라고 쉬운 말로 알린다
(진짜 설치로는 확인 안 함 — 매개변수 연결·구문만 확인, `Wave3IntegrationTests.셸_확장_설치는_…`).

## 일집-dist 재배포 (재사용장치 영향) — 사람이 할 일, 자동으로 하지 않음

- 재사용장치(`D:\Python\재사용장치\System_code\재사용장치_processor.py` `_find_iljip`)는 `config.json` 의 `iljip_path` 가 비어 있으면 이 차례로 찾는다(2026-10-14 Wave 2 통합 점검에서 다시 읽음):
  ① `D:\Python\일집\Iljip\bin\Release\net8.0-windows\win-x64\publish\Iljip.exe` ② `…\bin\Release\net8.0-windows\Iljip.exe` ③ `…\bin\Debug\net8.0-windows\Iljip.exe`
  ④ **`D:\Python\일집-dist\Iljip.exe`**(지금 쓰는 것) ⑤ `D:\Python\일집-build\package\Iljip.exe`(셸 확장 패키지 — 일집-dist 가 없을 때만) ⑥ `C:\Program Files\Iljip\Iljip.exe`.
  빌드 산출물은 `Directory.Build.props` 때문에 `D:\Python\일집-build\dotnet\…` 로 가서 ①~③ 은 비어 있다(`BuildOutputTests` 가 `Iljip\bin\Release·Debug` 아래를 통째로 봄).
  ★ 그래서 `Iljip\bin\Release` 에 exe 를 만들면(빌드 설정을 바꾸거나 `-o` 없이 RID 발행) 재배포 없이도 재사용장치가 그 판으로 바뀐다 — 조심.
  ★ 일집-dist 를 이름만 바꿔 두고(되돌릴 곳) 새 폴더를 아직 안 넣은 사이에는 ⑤(셸 확장 패키지 판)가 쓰일 수 있다 — 바꾸기는 재사용장치가 돌지 않는 시간에 한 번에.
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
- **Wave 2 판에서 재사용장치에 더 바뀌는 점**(2026-10-14 통합 점검 — 위 표에 더해, 픽스처로 예전 일집-dist 와 새 Release 빌드를 나란히 `--extract-to` 실행해 비교):
  | 첨부(반디집 픽스처) | 예전 일집-dist | 새 판(Wave 2) |
  |---|---|---|
  | 7z(빈 파일 든 반디집 7z) | **종료 1**('File does not have a stream.') — 결과 폴더에 9개 중 8개만 남김 → 반디집으로 | 종료 0, 9개 다(내용·이름 그대로) |
  | 암호 7z(이름 보임)·목록까지 잠긴 7z | 종료 1(영어 'Encrypted 7Zip archive has no password specified.') | 종료 1(쉬운 문구 '암호가 걸린…'·'파일 목록까지 암호로 잠긴…') → 반디집으로 |
  | 끝이 잘린 7z | 종료 1(영어 'nextHeaderOffset is invalid') | 종료 1('압축 파일이 손상됐어요. 다시 받아 보세요.'), 아무것도 안 남김 |
  | ZIP(CP949 한글 이름) | 종료 0, 9개 | 종료 0, 9개(같음) |
  | iso·lzh·tgz·tar.xz·zipx | 종료 2('Unsupported archive type' — 재사용장치는 '압축 아님'으로 봄) | 종료 0, 9개 다. 끝이 잘린 tgz 는 종료 1('끝부분이 잘려 있어요…') — 재사용장치는 이 확장자를 일집에 넘기지 않아 지금은 해당 없음 |
  (egg·alz·rar 의 예전/새 판 차이는 위 2026-10-12 표 그대로 — Wave 2 에서 egg·alz 처리기는 바뀌지 않았고 rar 는 7z.dll 로 바뀜. 새 판의 egg·alz·rar·7z·tgz·lzh 는
  `ReuseDeviceContractTests` 가 실제 exe 로 시험. 비교는 일집-dist 를 **실행만** 했고 전후 파일 목록·시각이 같음을 확인.)
  - 7z·rar 는 재사용장치가 파이썬 py7zr·rarfile 이 없을 때만 일집에 넘긴다 — 넘기면 이제 **7z.dll(7-Zip 26.03, `x64\7z.dll`)** 로 푼다. 7z.dll 을 보안 프로그램이 막거나 지웠으면 7z·rar 는 예전처럼 SharpCompress 로 대신 풀고,
    zipx·iso·cab·lzh·wim·xz 는 종료 1 + '이 형식을 여는 부품(7z.dll)을 불러오지 못했어요…'(→ 반디집으로). egg·alz 는 7z.dll 과 상관없이 EggDotNet 그대로.
  - 창 없이 풀기는 **일집 설정(settings.json)을 읽지 않는다** — '같은 이름이 있을 때'·'다 푼 뒤 압축 파일을 휴지통으로'·'폴더 열기'·풀기 창 기억이 재사용장치에 새지 않음(같은 이름은 늘 '이름 (1)', 첨부는 그대로).
    `ReuseDeviceContractTests`(실제 exe)·`RecycleAfterExtractTests.창_없이_풀기는_설정과_휴지통을_쓰지_않는다`(소스)가 지킴.
  - (IJ3-03) 창 없이 풀기는 **'인터넷에서 받은 파일' 표시(Zone.Identifier)를 풀린 파일에 붙이지 않는다** — 메일 첨부를 엑셀 COM 으로 여는 자동 처리가 '제한된 보기'·매크로 차단에 막히지 않게
    (일집 창·우클릭 풀기만 붙임 — 사용자 결정 I2-15). `ReuseDeviceContractTests.창없이_풀기는_인터넷에서_받은_표시를_…`(실제 exe — EGG·ALZ·RAR5·ZIP·7Z·TGZ)가 지킴. 재배포 전 손 확인은 MANUAL_CHECKLIST 8절 마지막 줄.
  - 7z.dll·압축된 TAR 는 결과 폴더(재사용장치의 `mkdtemp`) 안 숨김 `.일집-임시-…` 폴더에 먼저 풀고 다 맞을 때만 옮긴다 — 실패면 아무것도 안 남김(재사용장치는 실패면 그 폴더를 통째로 버리므로 영향 없음).
  - 확장자를 모르는 파일은 내용(서명)으로 알아본다(이름이 바뀐 압축·자동 풀림 exe). 재사용장치는 확장자(egg·alz·7z·rar)로 골라 넘기므로 지금은 해당 없음. 속이 ZIP 인 문서(.xlsx 등)는 그대로 종료 2.
- **3차 판(IJ3-01·IJ3-03 + 3차 통합 점검)에서 재사용장치에 더 바뀌는 점** — 종료 코드·표준 오류 문구는 Wave 2 판과 같다(`ReuseDeviceContractTests` 8개 — 실제 exe 7·소스 1):
  - 창 없이 풀기는 인터넷 표시를 풀린 파일에 붙이지 않는다(IJ3-03 — 위). 임시 폴더 청소(IJ3-01 `TempJanitor`)도 창 없이 풀기에서는 돌지 않는다(빨리 끝나게 — `EntryWorkspaceTests` 소스 확인).
  - **실패하면 일집 오류 로그 `%LOCALAPPDATA%\Iljip\logs\error-날짜.log` 에 `Headless extract failed: <첨부 경로>` + 이유**(예전엔 경로 없이 'Headless extract failed'). 재사용장치의 수동 모드 화면 로그는 파일로 남지 않아
    (자동 16시 모드만 `D:\Python\재사용장치\System_code\logs\batch_날짜.log`), 재배포 뒤 반디집으로 넘어간 진짜 egg 를 이 로그로 찾는다 — MANUAL_CHECKLIST 8절 끝 '재배포 뒤 1~2주 지켜보기'.
    로그 쓰기가 다른 일집 프로세스와 겹쳐 잠깐 막히면 5번까지 다시 쓴다(예전엔 그 기록을 말없이 잃음).
  - 주의: 이 PC 에서 개발 시험을 돌리면 같은 로그에 시험 기록이 쌓인다(경로에 `IljipTests` — 지켜보기 명령은 그 줄을 뺌). 10/13·10/14 로그는 IJ3-02 손상 훑기 시험 때문에 85MB·255MB — 지워도 된다(사람이 정함).
    → **4차 통합 점검부터 main 의 시험은 이 로그에 쓰지 않는다**(아래 4차 판 줄). 10/13·10/14 로그는 `D:\Python\일집-build\시험로그-보관\` 로 옮겨져 있다. 격리 전 판에서 갈라진 작업 폴더(예: `wt-zipcrypto`)의 시험은 그 갈래가 main 을 합치기 전까지 여전히 쓴다.
- **4차 판(4차 통합 점검 — 그때는 I4-1 이 stash)에서 재사용장치에 더 바뀌는 점**: 종료 코드·표준 오류 문구·풀린 결과는 3차 판과 같다(`ReuseDeviceContractTests` 8개 — 실제 exe, 그리고 실제 Release exe 로 따로 확인 — 아래 변경 이력 '4차 통합 점검').
  - 오류 로그는 예전과 같은 `%LOCALAPPDATA%\Iljip\logs\error-날짜.log`(같은 `Headless extract failed: <첨부 경로>` 줄). 로그·설정·임시 폴더의 뿌리를 `AppPaths.Root` 한 곳에서 정하게 바꿨고,
    시험만 쓰는 환경 변수 **`ILJIP_TEST_APPDATA`** 가 있으면(비지 않은 절대 경로일 때만) 그 폴더를 쓴다 — 사용자 PC 에는 없어야 한다(MANUAL_CHECKLIST 23절 첫 줄로 확인. 있으면 지켜보기 명령이 실패 기록을 못 찾음).
  - 발행 파일 수·이름은 그대로(46개 — 아래 발행 파일 줄).
- **4차 판 + I4-1·흘려 쓴 암호 ZIP(2026-10-18 들임)에서 재사용장치에 더 바뀌는 점**: 종료 코드 약속은 같다(`ReuseDeviceContractTests` — 이 판 계약 테스트 통과). 창 없이 풀기 실패의 표준 오류 한 줄은
  I4-1 의 `ErrorCatalog.HeadlessMessage` 쉬운 한국어(위 4차 판 줄의 받다 만 `.zip` '알 수 없는 오류가 났어요: Failed to locate the Zip Header' 같은 영어 섞인 문구 대신), 0 으로만 채운 1,000바이트 넘는 `.zip` 은 종료 0·0개 대신 손상(재사용장치는 `.zip` 을 넘기지 않음),
  흘려 쓴 암호 ZIP(서술자)은 맞는 암호면 풀림. 오류 로그 줄 `Headless extract failed: <경로>` 와 로그 위치는 4차 판 그대로. 재배포 전 실제 exe 로 8절을 다시 볼 것.
- **I5-B 판(2026-10-20 — 최종 감사 IX-F5·IX-F2)에서 재사용장치에 더 바뀌는 점**: 종료 코드 0·1·2 의 뜻은 그대로다(새 코드 없음 — `ReuseDeviceContractTests` 통과, '압축 아님' 표지 없음).
  - 창 없이 풀기 판정은 **첫 인자가 `--extract-to` 인가**만 본다(예전엔 둘째 인자가 있는 파일이어야 해서, 없는 경로면 일집 창과 '문제가 생겼어요' 창이 떠서 끝나지 않았음 — 재사용장치라면 사용자 화면에 창 + 제한 시간 1800초까지 그 첨부에서 멈춤).
    이제 인자 문제는 창 없이 표준 오류 한국어 한 줄·**종료 1**·오류 로그 `Headless extract failed: <준 경로>` + 다음 줄에 그 한 줄:
    없는 압축 '풀 압축 파일을 찾을 수 없어요: <경로>', 폴더 '풀 압축 파일 대신 폴더를 받았어요: <경로>', 빈 문자열·인자 모자람 '창 없이 풀기에는 압축 파일과 풀 폴더를 모두 주어야 해요. (Iljip.exe --extract-to <압축 파일> <풀 폴더>)'.
    풀 폴더를 만들지 않는다. 재사용장치는 있는 첨부와 `mkdtemp` 폴더만 넘기므로 평소엔 해당 없음(첨부가 그 사이 지워졌을 때 — 이제 반디집으로 넘어가 반디집이 실패를 알림).
  - **링크가 든 tar(하드·심볼릭)**: 파일은 예전처럼 다 풀고(링크는 0바이트 파일 — 일집은 링크를 되살리지 않음, 결정) 표준 오류 한 줄 '링크 N개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.'·
    오류 로그 `Headless extract failed: <경로>` + 그 한 줄, **종료 1**. ★ **결정 — 0 이 아니라 1**: 0 이면 재사용장치가 0바이트 파일을 그대로 가져가 말없는 손실(감사 재현), 2 는 'Unsupported archive type' 줄과 짝이라 쓸 수 없고,
    새 코드(3 등)는 재사용장치가 모르는 값이라 1(그 밖의 실패)과 같게 다뤄진다 — 1 이면 재사용장치가 반디집으로 다시 풀어 링크를 살린다(감사 실측: `bz x` 는 하드 링크 b.txt 를 a.txt 내용 12바이트로 풂). 링크 대상이 압축 밖이면(끊긴 심볼릭) 반디집도 실패해 재사용장치가 원본을 보관.
    재사용장치는 tar 를 일집에 넘기지 않아(egg·alz·7z·rar) 지금은 해당 없음 — 7z.dll 로 푸는 형식(RAR5·7z)의 링크는 이 알림 범위 밖(남긴 한계).
  - 그 밖(같은 이름 비켜 풀기·잠긴 파일 건너뛰기)은 화면 흐름 몫 — 창 없이 풀기는 결과 폴더(`mkdtemp`, 빈 폴더)가 비어 있어 해당 없음. 창 없이 풀기는 여전히 설정·같은 이름 범위·휴지통을 쓰지 않는다(소스 계약 그대로).
- **5차 판(5차 통합 점검 — I5-A + I5-B + 이 점검의 고침 셋)에서 재사용장치에 더 바뀌는 점**: **I5-B 판에서 더 바뀌는 것 없음** — 종료 코드 0·1·2 의 뜻, 표준 오류 한 줄(쉬운 한국어·'압축 아님' 표지 없음 —
  2 만 영어 'Unsupported archive type: <확장자>'), 실패 로그 자리 `%LOCALAPPDATA%\Iljip\logs\error-날짜.log` 의 `Headless extract failed: <첨부 경로>` 줄 모두 그대로다.
  이 점검에서 고친 셋(같이 푸는 압축 지키기·고치기 엔진의 바꿔 넣기·다음 시작 넣기)은 모두 일집 창·우클릭 흐름 몫이라 창 없이 풀기 길(서비스만 부름 — 여러 개 풀기·같은 이름 범위·고치기를 타지 않음)과 상관없다.
  새로 확인한 것(실제 exe — `Wave5IntegrationTests`): bzip2·xz 로 감싼 tar(`.tbz2`·`.txz`)의 링크도 tar·tgz 처럼 표준 오류 한 줄 + 종료 1, 압축이 있는 폴더로 풀어도 압축 자신을 덮지 않고 '이름 (1)'·종료 0
  (재사용장치는 늘 빈 `mkdtemp` 라 평소엔 해당 없음 — 재사용장치는 tar 를 넘기지도 않음).
  실제 Release exe(스크래치 사본, `ILJIP_TEST_APPDATA` 스크래치, 자식마다 120초 제한)로 따로 다시 확인: 18가지 모두 기대대로(스크립트 `w5_reuse\w5_contract.py` — 감사 I-X 재료를 읽기만, 결과 `w5_contract_result.json`, 자식 하나 0.9~4.7초).
  성공 길(zip·7z·tgz·링크 없는 tar) 종료 0·표준 오류 없음 / 손상(HTML 을 .egg 로 저장·0바이트 egg·무작위 alz·잘린 7z — 반쪽 파일 안 남김)·목록까지 암호인 7z 는 종료 1·쉬운 한국어 한 줄('압축 아님' 표지 없음 → 재사용장치는 반디집으로) /
  압축 아닌 .xyz·속이 zip 인 .xlsx 는 종료 2·'Unsupported archive type: .xyz'·'….xlsx' / 인자 문제(없는 압축·폴더·빈 경로)는 종료 1·한 줄·풀 폴더를 만들지 않음 /
  링크 tar·tgz·tbz2 는 종료 1·'링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). …'(tbz2 결과 a.txt 12바이트·b.txt 0바이트) / 압축이 있는 폴더에 tgz(안에 같은 이름 `첨부.tgz`)를 풀어도
  종료 0·바깥 SHA 그대로·안쪽은 `첨부 (1).tgz`. 로그: `ILJIP_TEST_APPDATA\logs\error-날짜.log` 에 `Headless extract failed: <경로>` + 다음 줄 이유(없는 압축·링크 tbz2), 잘린 7z 줄 있음,
  성공한 zip 은 줄 없음. 확인 뒤에도 사용자 `%LOCALAPPDATA%\Iljip\logs` 그대로.
- **진짜 egg 지켜보기 계획(재배포 뒤 1~2주)**: 진짜 알집 egg 견본이 없어 egg 는 합성 파일로만 시험했다(크기·CRC 확인이 진짜 파일에서 어긋날 위험 — 위 '위험'). 그래서 재배포한 뒤 **반디집을 지우지 않고 1~2주**
  (egg 첨부 5~10개 — 6월 배치 로그로는 2주에 1~2개) 지켜본다: 재사용장치 화면의 '(Iljip 해제 실패: … → Bandizip 폴백)' 줄 + 일주일에 한 번 오류 로그 명령(MANUAL_CHECKLIST 8절 끝).
  암호 egg 실패는 정상(창 없이는 암호를 못 넣음 — 반디집이 풂). 그 밖의 이유가 2주 동안 없으면 반디집을 지울지 정한다(사람). 있으면 그 egg 를 보관해 다음 판에서 고친다.
- 발행 파일(2026-10-14 Release 발행으로 확인 — 임시 폴더에 발행해 세고 지움, 발행한 exe 로 7z·ZIP 창 없이 풀기 종료 0): 맨 위 11개 — Iljip.exe·Iljip.dll·Iljip.pdb·Iljip.deps.json·Iljip.runtimeconfig.json, SharpCompress·SharpSevenZip·EggDotNet·ICSharpCode.SharpZipLib·CommunityToolkit.Mvvm(.dll), THIRD-PARTY-NOTICES.txt,
  하위 폴더 `x64\7z.dll`(**없으면 7z·rar 는 대신 길, 나머지 새 형식은 못 풂** — 꼭 같이), `licenses\`(라이선스 전문 11개 — IJ3-03 부터 `7-Zip-License.txt` 원문 포함, LGPL 상 같이 둘 것), `icons\`(형식 아이콘 22개 — 파일 연결용), `sfx\Iljip.SfxStub.exe`(자동 풀림 만들기 부품 —
  재사용장치 풀기와는 상관없고, 없으면 압축 창 형식 목록에서 '자동 풀림 EXE' 만 빠진다. 회사 백신이 지울 수 있음 — MANUAL_CHECKLIST 16절). 예전 판의 `ZstdSharp.dll` 은 필요 없다.
- 절차(재사용장치가 돌지 않는 시간에 — 바꾸는 동안 일집-dist 가 비면 셸 확장 패키지 판이 쓰일 수 있으니 한 번에):
  ```powershell
  # 1) 새 판 발행(저장소 bin 이 아니라 일집-build 아래로 — -o 를 꼭 줄 것)
  dotnet publish "D:\Python\일집\Iljip\Iljip.csproj" -c Release -o "D:\Python\일집-build\publish-새판"
  Get-ChildItem "D:\Python\일집-build\publish-새판" -Recurse -File | Measure-Object    # 파일 46개(맨 위 11 + x64\7z.dll 1·sfx 1·licenses 11·icons 22 — IJ3-03 Debug --no-build 발행으로 셈)
  # 2) 새 판으로 진짜 egg 하나 미리 풀어 보기(MANUAL_CHECKLIST 8절) — 종료 코드 0
  # 3) 바꾸기
  Rename-Item "D:\Python\일집-dist" "일집-dist-20260623-예전판"     # 되돌릴 곳(지우지 말 것)
  Copy-Item "D:\Python\일집-build\publish-새판" "D:\Python\일집-dist" -Recurse
  # 4) 확인: 파일·폴더가 다 왔는지, 새 판이 재사용장치 자리에서 egg 를 푸는지
  Test-Path "D:\Python\일집-dist\x64\7z.dll"                          # True
  ```
  다음 날 재사용장치 로그: '(Iljip으로 .egg 해제)' 뒤 '(Iljip 해제 실패: … → Bandizip 폴백)' 이 예전보다 늘지 않았는지, 7z·rar 줄도 같은지.
  되돌리기: 재사용장치가 돌지 않을 때 `D:\Python\일집-dist` 를 지우고(또는 `일집-dist-새판-문제` 로 이름 바꾸고) `일집-dist-20260623-예전판` 을 `일집-dist` 로 되돌린다.

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
이 PC 에는 Windows SDK 헤더가 없다. NuGet 의 SDK 패키지로 DLL 을 빌드할 수 있다(시스템 설치 없음).
(W2-12) `Build.ps1` 도 설치된 SDK(`Windows Kits\10\Include`)가 없으면 이 NuGet SDK 로 C++ 를 빌드하고 그 패키지의 `makeappx.exe`·`signtool.exe` 를 쓴다(`-NoSign` 이면 서명 없이 .msix 만 — 설치는 안 됨).
```powershell
dotnet restore "D:\Python\일집\packaging\WinSdk\WinSdk.csproj"
& "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" "D:\Python\일집\Iljip.ShellExtension\Iljip.ShellExtension.vcxproj" /p:Configuration=Release /p:Platform=x64 /p:UseNuGetWindowsSdk=true "/p:OutDir=D:\Python\일집-build\Release\x64\Iljip.ShellExtension\\" "/p:IntDir=D:\Python\일집-build\obj\Release\x64\Iljip.ShellExtension\\"
```
빌드된 DLL 이 있으면 계약 테스트 `ShellExtensionContractTests` 의 `DLL_…` 넷(옛 메뉴 제목·'일집 ▶' 하위 메뉴·오른쪽 끌기 메뉴·드롭 처리기)이 실제 DLL 을 불러 확인한다
(없거나 C++ 소스 — `.cpp` 넷·`dllmain.cpp`·`IljipCommands.h`·`pch.h` — 보다 오래되면 건너뜀. **C++ 를 고치면 이 명령으로 다시 빌드**해야 그 넷이 돈다).
형식 아이콘(`Iljip\icons\*.ico`)은 `packaging\Icons\MakeIcons.ps1` 이 만든다(PNG 를 담은 ICO 16·24·32·48·256 — 다시 만들 때만).

### 셸 확장·파일 연결 반영(W2-12) — 사람이 할 일, 자동으로 하지 않음
이 묶음은 **설치·등록·파일 연결 쓰기·탐색기 재시작을 하지 않았다**(코드·빌드·가짜 레지스트리 시험까지). 반영은 아래 순서로(일집 창은 모두 닫고, 재사용장치가 돌지 않는 시간에).
`일집-dist`(재사용장치 배포본)와는 따로다 — 셸 확장은 `D:\Python\일집-build\package\Iljip.exe` 를 부른다.
```powershell
# 0) 지금 상태 적어 두기(되돌린 뒤 비교용)
Get-AppxPackage Iljip.ShellExtension | Select-Object Name, Version, InstallLocation
reg query "HKCU\Software\Iljip" /s
reg query "HKCU\Software\Classes\.zip" /s
reg export "HKCU\Software\Classes" "$env:USERPROFILE\Desktop\일집반영전-HKCU-Classes.reg" /y     # (선택) 통째 백업 — 되돌릴 때 비교만, 다시 넣지는 말 것

# 1) (최초 1회 — 관리자 PowerShell) 인증서. packaging\cert\Iljip.pfx 가 이미 있으면 건너뜀
cd "D:\Python\일집\packaging"; .\CreateCert.ps1

# 1-1) (3차 통합 점검) 설치 볼륨 확인 — 'PackageStorePath' 가 C:\Program Files\WindowsApps 가 아니면 2) 에 -SystemVolume
Get-AppxDefaultVolume | Select-Object PackageStorePath, IsSystemVolume       # 이 PC(2026-10-15): D:\WindowsApps, False → -SystemVolume
#      같은 이유로 막힌 기록이 있는지(이 PC 는 OneDrive 패키지가 10/11·10/13·10/15 에 막힘):
Get-WinEvent -LogName "Microsoft-Windows-AppXDeploymentServer/Operational" -MaxEvents 500 | Where-Object { $_.Message -match '0x80070032' } | Select-Object -First 5 TimeCreated, Message

# 2) 셸 확장 빌드·설치(보통 PowerShell): 예전 패키지 제거 → 빌드(NuGet SDK 자동) → 서명 → Add-AppxPackage → 탐색기 재시작
cd "D:\Python\일집\packaging"; .\Rebuild.ps1 -SystemVolume   # 기본 볼륨이 C: 인 PC 는 -SystemVolume 없이. 탐색기 재시작이 싫으면 -SkipExplorerRestart 후 로그오프·로그온
#    -SystemVolume 없이 했다가 '설치하지 못했어요(0x80070032)…' 가 나오면: 빌드는 끝났으므로 .\Install.ps1 -SystemVolume 만 다시 → 탐색기 다시 시작
#    그래도 0x80070032 면(드묾): 설정 ▸ 시스템 ▸ 저장소 ▸ 고급 저장소 설정 ▸ '새 콘텐츠가 저장되는 위치' ▸ '새 앱 저장 위치'를 C: 로 바꾸고(PC 전체 설정 — 사람이 정함,
#    OneDrive 앱 설치 실패도 같이 풀릴 수 있음) .\Install.ps1 다시. 관리자 'Set-AppxDefaultVolume' 은 스크립트가 하지 않음
Get-AppxPackage Iljip.ShellExtension | Select-Object Name, Version, InstallLocation   # Version 0.4.0.0

# 3) 파일 연결 넣기 — 셸 확장과 같은 폴더의 일집으로(연결은 '이 명령을 실행한 Iljip.exe' 경로로 들어감)
& "D:\Python\일집-build\package\Iljip.exe" --register-associations --drop-handler    # 끌어 놓아 넣기를 원치 않으면 --drop-handler 빼기
#    결과 창 '일집 — 파일 연결'이 뜸(종료 코드 0 = 됨, 1 = 못 함). 또는 그 일집을 켜고 ☰ ▸ 설정 ▸ '파일 연결' [연결 프로그램에 넣기]
# 4) (선택) 두 번 눌러 바로 일집으로: 설정 창 [기본 앱 설정 열기] 또는 Win+R → ms-settings:defaultapps?registeredAppUser=일집 → 형식마다 '일집'(사람이 고름)

# 5) 확인(reg query) — 값이 아래와 같으면 정상
reg query "HKCU\Software\Classes\Iljip.zip" /s
#    (기본값) ZIP 압축 파일 · FriendlyTypeName · DefaultIcon = …\package\icons\zip.ico · shell (기본값) open · shell\open (기본값) 일집으로 열기
#    shell\open\command = "D:\Python\일집-build\package\Iljip.exe" --open "%1" · (드롭 처리기면) shellex\DropHandler = {61C3F459-E40B-441D-9F76-F56C70DFF8C1}
reg query "HKCU\Software\Classes\.zip\OpenWithProgids"        # Iljip.zip REG_NONE(다른 프로그램 값도 그대로)
reg query "HKCU\Software\Classes\.zip" /ve                     # 0)과 같음 — 일집은 형식의 기본값을 쓰지 않음
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.zip\UserChoice"   # 0)과 같음 — 기본 앱은 일집이 바꾸지 않음
reg query "HKCU\Software\Classes\Applications\Iljip.exe" /s   # FriendlyAppName 일집 · shell\open\command · SupportedTypes 21개
reg query "HKCU\Software\Iljip\Capabilities" /s               # ApplicationName 일집 · FileAssociations 20개(.iso 는 없음)
reg query "HKCU\Software\RegisteredApplications" /v 일집        # Software\Iljip\Capabilities
reg query "HKCU\Software\Iljip\RegisteredKeys"                 # 일집이 쓴 것의 기록(ExePath·OwnedKeys·SharedValues·CreatedKeys) — 빼기는 이것만 지움
reg query "HKCU\Software\Classes\CLSID\{61C3F459-E40B-441D-9F76-F56C70DFF8C1}" /s   # (드롭 처리기면) InprocServer32 = …\package\Iljip.ShellExtension.dll, ThreadingModel Apartment
# 6) 탐색기에서 MANUAL_CHECKLIST 18절
```
되돌리기(순서대로 — 연결을 먼저 빼야 탐색기가 드롭 처리기 DLL 을 놓는다):
```powershell
# a) 파일 연결 빼기 — 기록(RegisteredKeys)에 적힌 것만 지움(다른 프로그램 연결·.zip 기본값은 그대로)
& "D:\Python\일집-build\package\Iljip.exe" --unregister-associations                 # 또는 ☰ ▸ 설정 [연결 빼기]
reg query "HKCU\Software\Classes\Iljip.zip"                     # '지정된 레지스트리 키 또는 값을 찾을 수 없습니다'
reg query "HKCU\Software\Iljip"                                 # 같음(일집이 만든 키는 비면 지움 — settings 는 %LOCALAPPDATA% 라 상관없음)
#    기본 앱으로 일집을 골라 뒀다면: 설정 ▸ 앱 ▸ 기본 앱에서 원래 프로그램(반디집 등)을 다시 고름(Windows 가 '연결 프로그램' 창을 띄우기도 함)
# b) 셸 확장 빼기 + 탐색기 다시 시작(작업 관리자 ▸ Windows 탐색기 ▸ 다시 시작)
cd "D:\Python\일집\packaging"; .\Uninstall.ps1
Get-AppxPackage Iljip.ShellExtension                             # 아무것도 안 나옴
# c) 예전(0.3.0) 메뉴로 돌아가려면: 이 묶음의 작업 폴더 변경을 버린 판(git)으로 다시 .\Rebuild.ps1
# d) Inno 설치 프로그램(다음 배포 — 이 묶음에서 ISCC 안 돌림)으로 넣은 연결은 '일집 제거'가 [Registry] 의 uninsdelete* 로 지움(관리자 설치면 HKLM\Software\Classes)
```
위험: 드롭 처리기를 넣은 채 `Rebuild.ps1`(package 폴더를 지우고 다시 만듦)을 돌리면 탐색기가 DLL 을 잡고 있어 지우기에 실패할 수 있다 — 먼저 a) + 탐색기 다시 시작.
회사 그룹 정책이 사이드로드(서명된 MSIX 설치)나 HKCU\Software\Classes 쓰기를 막으면 2)·3)이 실패한다(3)은 '파일 연결을 넣지 못했어요…' 쉬운 문구). 기록 없이 남은 `Iljip.*` 키(손으로 넣은 것 등)는 빼기가 건드리지 않는다.

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
7. **desktop4 ItemType**: Type 속성은 `*` 또는 `.확장자`만 허용. `Directory`/`Folder` 거부 → (W2-12) 같은 `desktop4:FileExplorerContextMenus` 안에 `desktop5:ItemType Type="Directory"`·`"Directory\Background"` 로 해결
8. **빌드 폴더**: OneDrive 안에서 C++ 빌드는 PDB 동기화 충돌. 무조건 `D:\Python\일집-build`로
9. **(W2-12) 외부 위치 패키지**: `uap10:RuntimeBehavior="win32App"` 이면 `EntryPoint` 를 두면 안 됨(makeappx 가 거부). makeappx 는 매니페스트의 `Iljip.exe` 가 패키지 폴더에 **있어야** 통과(Build.ps1 은 publish 를 package 폴더에 하므로 됨)
10. **(W2-12) 확장자마다 ItemType 을 두지 말 것**: '*' 와 겹치면 '일집 ▶' 이 두 번 나온다 — 압축 확장자는 DLL(`IljipMenu.cpp ArchiveExtensions` = C# `ShellMenu.ArchiveExtensions`)이 고른다
11. **(W2-12) 기본 앱(UserChoice)은 쓰지 말 것**: Windows 가 해시로 보호 — 쓰면 '연결이 초기화됨' 알림만. 일집은 '연결 프로그램' 후보·기본 앱 목록 칸만 넣고 `ms-settings:defaultapps?registeredAppUser=일집` 을 연다
12. **(IJ3-03) 대체 스트림(`:Zone.Identifier`)을 쓰면 NTFS 가 파일의 수정 시각을 '지금'으로 바꾼다**(2026-10-15 실측) — 표식을 붙인 뒤 원래 수정 시각으로 되돌려야 함(`ZoneIdentifier.TryMark`).
    읽기 전용 파일에는 스트림도 못 씀(잠깐 풀었다 되돌림). 없는 파일에 `파일:스트림` 으로 쓰면 빈 본문 파일이 새로 생기므로 있는지 먼저 볼 것.
    7z.dll 의 `PropagateMarkOfTheWeb` 는 켜지 말 것 — 서비스(창 없이 풀기 포함)가 붙이게 되어 재사용장치 약속이 깨짐(붙이는 일은 화면만)
13. **(3차 통합 점검) 이 PC 의 '새 앱 저장 위치'는 D:**(`Get-AppxDefaultVolume` → `D:\WindowsApps`). MSIX 를 기본대로 넣으면 0x80070032('시스템 볼륨에 설치되어 있지 않아…')로 실패할 수 있다
    (OneDrive 앱 패키지가 실제로 그렇게 실패 중) — 셸 확장은 `.\Rebuild.ps1 -SystemVolume`. 로그 머리 `Headless extract failed: ` 는 MANUAL_CHECKLIST 8절 지켜보기 명령이 찾는 글이라 바꾸지 말 것
    (`App.HeadlessFailureLogHead` — `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_…`)
14. **(4차 통합 점검) 시험이 Iljip.exe 를 띄울 때는 꼭 `ModuleInit.ApplyTo(psi)`** — 안 하면 그 exe 가 사용자 진짜 로그(`%LOCALAPPDATA%\Iljip\logs`)에 쓴다(`Wave4IntegrationTests` 가 시험 소스를 훑어 막음).
    로그를 새로 쓰는 코드는 `Logger`(→ `AppPaths.Root\logs`)만 쓸 것 — `Environment.GetFolderPath(LocalApplicationData)` 를 직접 부르면 시험 격리가 깨진다(`AppPaths.DefaultRoot()` 만 그 값을 읽음)
    (5차 통합 점검) 실제 exe 를 띄우는 새 시험은 `HeadlessNoticeTests.RunIljip`(창 없이 풀기 판정 먼저 확인·`ApplyTo`·60초)을 같이 쓰면 된다.
15. **(5차 통합 점검) 고치기 엔진에서 넣기로 한 파일을 뺄 때는 `planner.Drop(a)`** — `a.Removed = true` 로 빼면 그 파일이 '덮어쓰기'로 대신한 원래 항목은 지운 채로 남아 압축에서 사라진다
    (`EditPlanner.AddedFile.UndoReplace` 가 되살림 — 앞서 넣기로 한 파일을 덮어쓴 새 파일은 그 파일의 되살릴 일을 이어받음, 앞의 파일 자체는 되살리지 않음). 여러 개 풀기의 같은 이름 범위는 `ExtractionScope.Begin(resolver, 압축 여럿)` — 같이 푸는 압축 전부를 지킨다.

## 향후 작업

- [ ] **일집-dist 재배포** (위 '일집-dist 재배포' 절 — MANUAL_CHECKLIST 확인 뒤)
- [x] **폴더 우클릭 메뉴 복원** — W2-12 에서 desktop5 ItemType 으로(코드·매니페스트 검사까지 — 설치·탐색기 확인은 아래 W2-12 남은 확인)
- [x] **W2-12**: 파일 연결(I1-1)·우클릭 '일집 ▶' 정비·알아서 풀기·창 없이 바로 압축(I1-2)·오른쪽 끌기 메뉴·zip/7z 끌어 놓아 넣기(I3-10)(아래 변경 이력)
- [ ] **W2-12 반영(사람)**: '셸 확장·파일 연결 반영(W2-12)' 절차 → MANUAL_CHECKLIST 18절(진짜 탐색기 '일집 ▶'·오른쪽 끌기·드롭 처리기·연결 프로그램·기본 앱 화면·.z01·많이 골랐을 때 목록 파일, 회사 그룹 정책)
- [ ] (W2-12 남김) '시험하기'(압축 시험) 하위 메뉴 — `--test` 명령은 IJ3-02 에서 main 에 들어옴. `ShellMenu`·`IljipMenu.cpp` 에 '시험하기'를 더하는 일만 남음(셸 확장 재배포 범위 — IJ3-02 는 C++ 를 건드리지 않음)
- [ ] (W2-12 남김) 다음 Inno 설치 프로그램 만들기 때 `[Registry]`·`ChangesAssociations`·아이콘 줄이 ISCC 로 실제 컴파일·설치·제거되는지(이 묶음은 ISCC 안 돌림 — 줄 모양·키·값은 계약 시험이 C# 등록기와 맞춰 봄). 버전 `AppVersion` 0.2.0 은 그대로 둠
- [x] W1-B 다시 → **W2-01(해제 쪽) 끝**: 7z.dll 로 7z·RAR·ISO/UDF·CAB·LZH·ZIPX·WIM·XZ·숫자 분할·압축된 TAR·자동 풀림 exe 풀기(아래 변경 이력)
- [x] **W2-02**: 7z 만들기(I2-1)·파일 이름 암호화·솔리드·옵션 창 7Z 칸·저장 형식 7Z, 목록까지 잠긴 7z·RAR 열기(I1-7) — 보관 시험 되살림(아래 변경 이력)
- [ ] W2-02 남은 확인: MANUAL_CHECKLIST 10절(옵션 창 모양, 반디집·7-Zip 으로 일집 7z 열기, 작업 관리자 메모리, 진짜 7-Zip -mhe·WinRAR 머리 암호 파일)
- [x] **재배포 전 필수 → IJ3-03 끝**: 7-Zip `License.txt` 원문(사용자가 허락해 받은 `D:\Python\일집-build\licenses-source\7-Zip_license.txt` — 6031바이트, SHA-256 3A184AA1…)을
      `Iljip/licenses/7-Zip-License.txt` 로 바이트 그대로 넣고, BSD 3·2조항 파일과 `THIRD-PARTY-NOTICES.txt` 의 7-Zip 부분을 원문 줄 그대로로 바로잡음(아래 변경 이력 'IJ3-03').
      GPL-3·LGPL-2.1 전문은 예전대로 이 PC 의 Git for Windows 사본(FSF 원문)
- [x] ALZ / EGG 지원 (EggDotNet 0.4.0, W1-A 에서 빈 항목·ALZ 한글 이름·잠김·내용 확인까지)
- [x] 새 형식·.egg/.alz 우클릭 메뉴 — W2-12 에서 '일집 ▶' 하나(DLL 이 압축 확장자로 고름)로 바뀜, `Rebuild.ps1` 재설치 때 반영(위 W2-12 반영)
- [x] 보고 안건 → **IJ3-03 끝(사용자 결정 I2-15)**: 압축을 풀 때 '웹에서 받은 파일 표시(MOTW)' — 일집 창·우클릭 풀기·열기·끌기·Ctrl+C 는 붙이고(설정으로 끔, 기본 켬), 창 없이 풀기(재사용장치)는 붙이지 않음
- [ ] IJ3-03 남은 확인: MANUAL_CHECKLIST 21절(진짜 오피스 '제한된 보기'·풀린 파일 표시 내용·수정 날짜·같은 폴더의 원래 파일은 안 붙음·설정 칸·수천 개 시간·USB/NAS·매크로 파일·일집 정보 창의 7-Zip 원문) + 8절 마지막 줄(창 없이 풀기는 안 붙음)
- [ ] (IJ3-03 남김) '다른 이름으로 저장'(같은 형식 복사·ZIP↔7Z 바꾸기)으로 만든 새 압축에는 원본의 인터넷 표시가 따라가지 않음(내용 스트림으로 복사) — 그 사본을 일집으로 풀면 표시 없음. 옮길지는 사람이 정할 일
- [ ] (IJ3-03 남김) 일집이 만든 자동 풀림 exe(SFX 스텁)는 받는 사람 PC 에서 풀 때 exe 의 인터넷 표시를 풀린 파일에 옮기지 않음(스텁은 따로 된 프로그램 — 바꾸려면 `Iljip.SfxStub`)
      (같은 exe 를 **일집 창으로** 풀면 붙음 — 3차 통합 점검 `Wave3IntegrationTests.일집이_만든_자동풀림_exe를_…`)
- [ ] **3차 통합 점검 남은 확인**: MANUAL_CHECKLIST 22절(받은 압축 고치기·다시 넣기·[이름 바꾸기]·나눠진 압축·자동 풀림 exe·두 창의 청소·끌어 놓은 사진의 표시·설치 볼륨) + 재배포 뒤 8절 끝 '1~2주 지켜보기'
- [ ] (3차 통합 점검 남김) 압축 고치기의 바꿔 넣기가 `File.Replace` 를 못 쓰는 드라이브(일부 NAS — `ArchiveSwap` '예비 이름 방식')로 넘어가면 고친 압축 파일의 인터넷 표시가 빠진다
      (`File.Replace` 길은 Windows 가 표시를 이어 줌 — `Wave3IntegrationTests.표시가_있는_압축을_고쳐도_…`). 그런 드라이브는 대개 표시 자체를 못 담아 실제 영향은 작음 — 예비 이름 길에서 표식을 옮길지는 다음에
- [ ] (3차 통합 점검 기록) I2-8 의 나머지 — 영어 엔진 문구 뒤에 따옴표 없는 한글 경로가 붙으면 `ErrorMessages` 가 한국어 문구로 보고 그대로 보임(드묾). 고친 판정(`LooksKorean`·`FromEngine`)은 IJ3-02 보관분에 있음 —
      IJ3-02 를 다시 할 때 같이(→ I4-1 이 보관분 그대로 들임). 3차 기능의 화면·예외 문구에 영어만으로 된 글은 없음(`Wave3IntegrationTests.화면과_예외_문구에_…` — 본체 전체 소스·XAML 훑기)
- [x] (결정) **IJ3-02 다시 할지** → **I4-1(2026-10-16~17)에서 다시 함** → **2026-10-18 main 에 들임**(4차 3차 검증에서 제품 기준은 통과, 걸린 것은 문서·보고 누락 — '현재 진행 단계'에 새 파일 목록을 넣음. 보관분은 drop 안 함)
- [x] (I4-1 남김) 일집 풀기 엔진이 **흘려 쓴 ZipCrypto(서술자) ZIP**(SharpZipLib 을 되감지 못하는 스트림에 암호로 쓴 것 — Java·서버 모양)을 온전한 원본부터 맞는 암호로 풀지 못함
      → **고침(2026-10-16 별도 세션, 2026-10-18 얹음 — 변경 이력 '흘려 쓴 암호 ZIP')**. main 에서도 같았음(재현). 원인은 SharpCompress 0.50.5(암호 + 서술자 + 로컬 머리 압축 크기 0 을
      목록으로 여는 길에서도 거절 — 형식 알아보기까지 실패), 일집 암호 판정은 맞았음. 흘려 쓴 AES ZIP 도 같은 원인이라 같이 풀림
- [ ] (흘려 쓴 암호 ZIP 남김) 진짜 Java·서버(zip4j 등)가 흘려 쓴 암호 ZIP 으로는 확인 못 함 — 시험은 SharpZipLib 로 만든 합성 재료(ZIP64 있음·없음·로컬 0xFFFFFFFF 표시)만
- [ ] (I4-1 검토 남김) Deflate 마지막 블록 표시만 깨진 항목(내용·CRC 는 맞음): 검사는 '문제없어요'(일집은 원래대로 풂), 고치기는 못 살림 — 다시 압축해 살릴지·검사도 알릴지 정하기(반디집·7-Zip 이 어떻게 보는지 먼저)
- [ ] (I4-1 남김) ZIP 고치기 목록 모드: 흘려 쓴 항목(서술자)의 목록 CRC·크기만 깨지면 서술자를 세 번째 증인으로 쓰지 않아 그 파일을 못 살림(정직하게 '내용이 원래와 달라요' — 덜 살림). 목록이 없는 ZIP 에서 가운데 머리가 깨지면
      그 뒤는 목록 조각이 가리키지 않는 한 조각 수로만(반디집이 더 살리는지 MANUAL_CHECKLIST 24절 'I4-1' 줄로 비교)
- [x] (결정) I4-1 다시 할지 → 2026-10-18 들임(위). (I4-1 이 main 에 없어 남았던 '0 으로만 채운 .zip 을 창 없이 풀면 종료 0·0개' 도 I4-1 로 손상 처리)
- [ ] (I4-1 4차 3차 검증 minor — 고치지 않음, 변경 이력 '2026-10-18 I4-1 들임') 폴더 기록 칸 손상 시 N 이 1 큼, 훑기 모드의 글자로 읽히는 깨진 이름('?' 포함), 첫 암호 항목 확인 바이트·salt 손상 시 다시 묻기,
      .z01 을 창 없이 풀기에 주면 '손상' 종료 1, 큰 항목 확인 중 진행 표시 멈춤, '확인 못 함'만 있을 때 요약 문구
- [x] **W2-03**: 분할 압축(나눠 만들기 — I2-2 만들기) — ZIP PKZIP 나눔·7Z 숫자 조각·옵션 창 '나누기' 칸(아래 변경 이력)
- [ ] W2-03 남은 확인: MANUAL_CHECKLIST 11절(옵션 창 '나누기' 칸, Windows 탐색기·알집으로 나눔 ZIP 열기, 진짜 메일 첨부, OneDrive 폴더)
- [x] **W2-05**: 같은 이름 묻기(I1-5)·메인 창 없는 우클릭 풀기(I1-6)·풀고 나서 압축 파일 휴지통으로(I2-5)·여러 개 풀기(I2-6), 설정 창(☰ ▸ 설정)
- [ ] W2-05 남은 확인: MANUAL_CHECKLIST 12절(같은 이름 창·설정 창 모양, 메인 창이 정말 안 뜨는지·창이 뒤에 숨지 않는지, 진짜 휴지통·네트워크 드라이브, 여러 개 골라 우클릭이 하나로 묶이는지)
- [x] 셸 확장이 고른 파일들을 한 번에 넘기기 — W2-12 '일집 ▶' 하위 메뉴는 고른 것을 한 명령줄로(30,000자 넘으면 `--list-file`). 예전 5개 메뉴(DLL 에만 남음)는 그대로 파일마다
- [x] W2-04 다시 → **IJ3-01 끝**: 끌어 꺼내기·Ctrl+C(I1-4)·그림 미리 보기(I3-7)·바로 열기 나머지(I1-3 — 실행 파일 경고 설정·임시 폴더 청소)(아래 변경 이력 'IJ3-01')
- [ ] IJ3-01 남은 확인: MANUAL_CHECKLIST 20절(진짜 탐색기로 끌어 놓기 — 한글·폴더·큰 파일·비동기 모드·솔리드 7z 사진 폴더, 붙여넣기 — 이름이 바뀌는 항목 알림·수천 개 진행 창·취소,
  미리 보기 화면·WebP 코덱·아주 큰 PNG, 오피스 '제한된 보기', 끌어 놓은 파일의 인터넷 표식, 다음 시작 때 청소)
- [x] **W2-06**: 압축 고치기(I2-3 — ZIP·7Z 넣기·지우기·이름 바꾸기·새 폴더, `--add-to`)·고친 파일 다시 넣기(I3-3)
- [ ] W2-06 남은 확인: MANUAL_CHECKLIST 13절(도구 막대·☰·우클릭 메뉴·F2·Del 창 모양, 오피스로 고쳐 저장했을 때 한 번만 묻는지, 닫을 때 묻기, 회사 PC DLP·OneDrive 폴더에서 바꿔 넣기, 큰 압축 시간)
- [x] **I5-A**: 압축 속에서 연 파일 — 편집기의 이름 바꾸기 따라가기(원래 항목 이름으로 넣음)·닫을 때 '아직 열려 있어요' 알림·닫은 뒤 저장한 파일을 지우지 않고 다음 시작 때 알림(감사 X3-F2·X3-F1·I3-3-close — 아래 변경 이력 'I5-A')
- [ ] I5-A 남은 확인: MANUAL_CHECKLIST 25절(진짜 Light PDF 탭 '이름 바꾸기'·연 채 일집 닫기·닫은 뒤 저장 → 다음 시작 알림의 모양과 단추, [폴더 열기]의 탐색기·[버리기]의 진짜 휴지통, 워드·엑셀·한글·메모장으로 같은 흐름)
- [ ] (I5-A 남김) 편집기의 '다른 이름으로 저장'으로 순번 폴더(`temp\open\…\1\`) 안에 **새 이름의 파일**을 만들고 원래 파일도 남아 있으면 따라가지 않음 — 닫을 때 원래 파일이 그대로면 순번 폴더째 지움(감사 X3 s5a — 별도 지적.
      편집기가 그 새 파일을 잡고 있으면 '잡힌 파일' 판단은 원래 파일만 봐서 못 지킴). 따라갈 후보를 '원래 파일이 없을 때'에서 '순번 폴더에 새로 생긴 같은 확장자 파일'로 넓힐지·묻는 문구를 정할 것
- [ ] (I5-A 남김) 파일을 잡지 않는 편집기(메모장 등)는 닫을 때 '아직 열려 있어요'를 알 수 없음 — 고치지 않은 채 일집을 닫으면 순번 폴더를 지움(그 뒤 편집기 저장은 실패로 드러남 — 말없이 잃지는 않음). 메모장 실제 동작은 25절에서 확인
- [ ] (I5-A 남김) 다음 시작 알림은 메인 창이 뜨는 시작에서만 — 창 없이 풀기(재사용장치)·우클릭 풀기만 쓰는 날은 묻지 않고 파일은 남겨 둠([나중에]·[폴더 열기]도 남김 — 오래 쌓이면 사람이 [버리기]). 자동으로 지우는 기한은 두지 않음(결정 — 고친 내용을 말없이 지우지 않게)
- [x] **I5-B**: 최종 감사 안전·정직 5건 — 풀고 있는 압축 자신·조각 덮어쓰기 막기(IA-SELFNAME)·ZIP·TAR·GZ·BZ2 [바꾸기] 취소·실패에 기존 파일 지키기(IB-zip-replace-cancel-deletes-old)·
  창 없이 풀기의 없는 경로·폴더·빈 인자(IX-F5)·tar 링크 알림(IX-F2)·열린 파일은 건너뛰고 계속(IA-OPENFILE-OVERWRITE) — 아래 변경 이력 'I5-B'
- [ ] I5-B 남은 확인: MANUAL_CHECKLIST 26절(진짜 엑셀로 연 파일 위에 풀기·이중 압축 첨부 여기에 풀기·[바꾸기] 중 취소의 진짜 창 모양, 창 없이 풀기 없는 경로를 진짜 Release exe 로)
- [ ] (I5-B 남김) 링크 알림은 **풀기(일집 창·우클릭·여러 개·창 없이)와 검사**에만 — 압축 속 링크 항목 하나를 두 번 눌러 열기·끌어 꺼내기·Ctrl+C 는 예전처럼 0바이트 파일을 말없이 만든다(드묾 — 링크가 든 tar 를 일집 창에서 항목째 꺼낼 때만).
  7z.dll 로 푸는 형식(RAR·7z 등)의 링크 항목은 알림 범위 밖(SharpCompress 로 푸는 tar·tgz·tbz2·txz 만 — 감사 지적 범위. 7z.dll 이 링크를 어떻게 푸는지는 확인 안 함). 링크 되살리기·같은 압축 안 대상 내용 복사는 하지 않음(결정 기록 IX-5·B-041 '제외' 그대로)
- [ ] (I5-B 남김) '다른 프로그램이 쓰는 중' 판정은 덮어쓰기 실패 뒤 그 파일을 혼자 열어 보는 것(공유·잠금 위반 0x80070020·21) — 그 사이 다른 프로그램이 파일을 놓으면 예전 오류(멈춤 + 문구)로 남음(드묾, 데이터 손실 아님).
  `ExtractionScope.Begin` 에 선택 인자(풀고 있는 압축)를 더해 이진 모양이 바뀜 — 저장소 밖에서 예전 Iljip.dll 로 빌드한 하네스는 다시 빌드해야 함(제품·시험 영향 없음)
- [x] **5차 통합 점검**: 묶음 사이 교차 시험 `Wave5IntegrationTests`(38) — 고친 것 셋: 여러 개 풀기가 같이 푸는 다른 압축을 덮던 것(I5-B 남김), 고치기 엔진의 바꿔 넣기가 새 파일을 못 읽으면 원래 항목을 지우던 것
  (다음 시작 [압축에 넣기]가 '넣었어요' 하며 고친 파일까지 지우던 I5-A 남김의 뿌리 — 일반 넣기 [덮어쓰기]도), 다음 시작 넣기가 같은 암호 압축의 파일마다 암호를 묻던 것(아래 변경 이력 '5차 통합 점검')
- [ ] 5차 남은 확인: MANUAL_CHECKLIST 27절(진짜 창으로 — 여러 파일 연 채 닫기·암호 zip 의 다음 시작 넣기·같이 푸는 압축·잠긴 파일 바꿔 넣기)
- [ ] (5차 남김) 링크가 든 tar 를 같은 이름 **[덮어쓰기]**로 풀면 있던 같은 이름 파일(예: 반디집으로 먼저 풀어 내용이 든 `b.txt`)이 0바이트로 바뀜 — 알림('링크 1개는 내용 없이…')은 나오지만 막지는 않음
  (코드로 확인 — 드묾: 링크 tar 를 같은 곳에 두 번 풀 때). 링크 항목은 덮어쓰기에서 빼고 '건너뜀'으로 할지 정할 것
- [ ] (5차 남김) 이름이 같은 두 항목(`자료/보고서.txt`·`사본/보고서.txt`)을 연 채 닫으면 '아직 열려 있어요'·다음 시작 알림에 '‘보고서.txt’ — 받은 자료.zip' 이 두 줄로 보여 어느 폴더 것인지 알 수 없음
  (넣는 자리는 각자 원래 항목으로 맞음 — `Wave5IntegrationTests.이름이_같은_두_항목은_…`). 이름이 겹칠 때만 압축 안 폴더를 붙여 보일지
- [ ] (I5-A 남김, 5차에서 다시 봄) 오래된 남은 파일을 다음 시작에 넣으면 그 사이 같은 항목에 새로 넣은 내용을 덮을 수 있음(알림에 날짜가 없음) — 고친 때(파일 수정 시각)를 알림에 보일지
- [ ] 탐색기 우클릭 'zip에 추가 (일집)': C++ 셸 확장에 메뉴를 더해 `Iljip.exe --add-to <압축> <고른 파일…>` 을 부르게(명령줄 쪽은 W2-06 에서 끝, 재배포 범위).
      '기존 zip 고르기' 메뉴는 `Iljip.exe --add-to-pick <고른 파일…>`(일집이 압축 고르기 창을 띄움 — W2-06 검증 지적에서 명령줄 끝).
      (W2-12) zip·7z 아이콘 위에 끌어 놓기(드롭 처리기 — `--add-to`)는 들어감. 우클릭 'zip에 추가' 메뉴 자체는 아직(I1-2 범위 밖으로 둠)
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
- [ ] (IJ3-02 남김) '관리자 권한으로 다시 시도'는 여전히 `MainViewModel.OfferElevatedRetry`(W2-11 의 `Choose` 질문)가 먼저 물음 — 문제 창(ShowProblem·ErrorCatalog)은 IJ3-02 로 들어왔지만 그 '권한 없음' 분류의 `ErrorCatalog.ElevateAction` 버튼은 화면이 걸러 냄(`MainViewModel.Test.UsableActions`). 하나로 합치려면 그 버튼을 `ElevationService.RetryActionId` 흐름에 잇기
- [ ] (선택) 압축 고치기(넣기·지우기·이름 바꾸기)의 권한 오류에도 '관리자 권한으로 다시 시도' — 지금은 `ArchiveSwap.EnsureWritable` 이 권한 오류를 '다른 프로그램이 압축 파일을 쓰고 있어…'(LockedMessage)로 바꿔 대부분 권하지 않음(기존 시험이 그 문구에 기댐). `--add-to` 만 인자(`--elevated --add-to`)를 넘기게 해 둠
- [x] 임시 폴더 청소기(`TempJanitor`): 바꿔 저장하는 도중 일집이 강제로 끝나면 `temp\convert\guid` 가 남던 것 — IJ3-01 청소기가 24시간 지난 것을 지움(`temp\args` 도)
- [x] 탐색기 오른쪽 끌기 메뉴('일집: 여기에 압축'·'일집: 여기에 풀기') — W2-12(desktop9 고전 끌어 놓기 처리기, `--compress-to`·`--extract-to-folder-in`)
- [ ] (IJ3-01 남김) 끌기에서 한 번에 꺼내기가 **실패**하면(손상 항목 하나) 그 뒤는 항목마다 따로 꺼냄 — 솔리드 압축이면 이 실패 경우만 아직 느림(취소·경로 공격 항목 섞임은 한 번에로 고침)
- [ ] (IJ3-01 남김) Ctrl+C 의 '이름을 바꿔 복사했어요' 알림은 폴더 안의 대소문자만 다른 두 항목('report (1).txt')은 세지 않음(고른 것 자신은 셈)
- [x] W2-08 다시 → **IJ3-02 끝**: 압축 검사(I2-7 — ☰·우클릭 '시험하기'·`--test`)·오류 원인 한국어 구분과 문제 창(I2-8)·깨진 ZIP 고치기(I3-12) + 3차 검증의 (a)~(h)(아래 변경 이력 'IJ3-02')
- [ ] IJ3-02 남은 확인: MANUAL_CHECKLIST 21절(문제 창 모양·'자세히'·복사·로그 폴더, 진짜 깨진 메일 첨부 ZIP 고치기와 반디집 비교, 꽉 찬 USB·잠긴 파일·긴 경로 문구,
  IJ3-02 줄 — 다른 프로그램이 만든 멀쩡한 ZIP 에 '목록 손상' 오보가 없는지, 따옴표로 시작하는 폴더의 문구, **진짜 알집 ALZ 의 끝 표시 'CLZ\2'**, 저장 방식 문서가 든 ZIP 고치기)
- [ ] (IJ3-02 남김) ALZ 끝 표시: 'CLZ\1' 뒤 'CLZ\2' 를 4바이트씩 16칸 안에서 찾음 — 진짜 알집 파일로 확인 못 함(이 PC 에 알집·진짜 .alz 없음, 시험은 합성 ALZ 와 EggDotNet 의 읽는 방식 기준). 멀쩡한 파일에 '잘려 있어요'가 나오면 `AlzNameScanner.EndMarkFollows` 를 진짜 파일 모양에 맞출 것
- [ ] (IJ3-02 남김) ZIP 고치기: 끝 머리가 파일 끝에 딱 맞지 않고(뒤에 덧붙은 바이트) 위치 칸이 앞 데이터 기준인 자동 풀림 ZIP 은 바깥 목록을 안쪽 압축으로 볼 수 있음(드묾 — 그러면 '찾은 파일'로 훑기만), 다른 손상이 둘 이상 겹친 경우는 시험하지 않음(바이트 하나씩만)
- [ ] 탐색기 우클릭 '압축 파일 시험 (일집)': 셸 확장에 메뉴를 더해 `Iljip.exe --test <고른 압축…>` 을 부르게(명령줄 쪽은 IJ3-02 에서 main 에 들어옴, 재배포 범위)
- [ ] 다크 모드
- [ ] 미리보기 (압축 내부 텍스트) — 그림은 IJ3-01(W2-04 다시)에서 됨
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
  빠름 `mt;d=4m;c=4m`·보통 `mt;d=16m;c=8m`(**Wave 2 통합 점검에서 `c=2m` 으로 — 아래 변경 이력**)·최대 `mt=2;d=32m;c=16m`(mt=min(4, 논리 CPU)), 파일 이름 암호화(`he`)는 암호가 있을 때만. **실측(이 PC 논리 CPU 8·RAM 7.8GB,
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

- **2026-10-14 W2-12 — 파일 연결(I1-1)·우클릭 메뉴 정비(I1-2)·오른쪽 끌기 메뉴(I3-10)(커밋 전, 설치·등록·배포 안 함)**:
  ① **파일 연결**: `Services/Shell/FileAssociationRegistrar.cs`(+ 레지스트리 틀 `IRegistryRoot.cs` — 진짜는 HKCU, 시험은 메모리 `FakeRegistryRoot`)가 HKCU\Software\Classes 에 확장자 21개(zip·7z·rar·tar·gz·tgz·bz2·tbz2·tbz·xz·txz·zipx·lzh·lha·cab·wim·egg·alz·001·z01·iso)의
  ProgId `Iljip.<ext>`(이름·`<설치 폴더>\icons\<ext>.ico`·'일집으로 열기' = `"<exe>" --open "%1"`)·`.<ext>\OpenWithProgids` 의 우리 값(REG_NONE)·`Applications\Iljip.exe`·`Software\Iljip\Capabilities`(iso 는 빼고 — Windows 디스크 이미지 기본 연결)·`RegisteredApplications` '일집'을 넣는다.
  **형식의 기본값·UserChoice(기본 앱)는 절대 쓰지 않음** — 기본 앱은 [기본 앱 설정 열기](`IShellLauncher.OpenDefaultAppsSettings` → `ms-settings:defaultapps?registeredAppUser=일집`, 안 되면 기본 앱 첫 화면). 쓴 것은 `Software\Iljip\RegisteredKeys`(OwnedKeys·SharedValues·CreatedKeys)에 **먼저** 적고,
  빼기는 그 기록에 적힌 것 중 허용된 모양(Iljip.* ProgId·Applications\Iljip.exe·우리 CLSID·Software\Iljip 아래·OpenWithProgids 의 Iljip.* 값·RegisteredApplications 의 '일집')만 지운다 — 기록이 없으면 아무것도 안 지움, 우리가 새로 만든 빈 키(.egg 등)까지 지워 처음과 같음.
  다시 넣기는 멱등(우리 키를 지우고 다시 씀, 드롭 처리기를 끄면 그 키도 지움). 넣고 빼면 `SHChangeNotify(SHCNE_ASSOCCHANGED)`(`IShellLauncher.NotifyAssociationsChanged`). 들어오는 길: ☰ ▸ 설정 '파일 연결' 칸(새 — 일집 창을 받은 설정 창에서만 보임, 단추는 누르는 즉시 적용·[확인]과 상관없음 —
  `MainViewModel.Shell.cs`), 명령줄 `--register-associations [--drop-handler]`·`--unregister-associations`(`FileAssociationCommand` — App 이 창 없이 풀기 다음·단일 인스턴스 전에 자체 프로세스로 돌고 결과 창 하나 뒤 종료 코드 0/1).
  설정 `shell.associationsRegistered`(기록용 — 실제 상태는 RegisteredKeys, 설정 창 [확인]의 옛 사본이 되돌리지 않게 `ApplySettings` 패치에서 뺌). 형식 아이콘 22개 `Iljip/icons/*.ico`(`packaging/Icons/MakeIcons.ps1` — PNG 담은 ICO, csproj 가 출력·발행 폴더 icons\ 로 — 단일 파일에서도 따로).
  설치 프로그램 `installer/Iljip.iss`: UTF-8 BOM, `ChangesAssociations=yes`, `[Files]` 아이콘 줄, `[Registry]`(Root: HKA — 관리자 설치면 HKLM) 같은 키·값(ProgId·Applications·Capabilities 는 uninsdeletekey, OpenWithProgids·RegisteredApplications 는 uninsdeletevalue) — **ISCC 로 만들어 보지 않음**.
  ② **'일집 ▶' 하나**(Windows 11 새 메뉴): 매니페스트(0.4.0.0 — 0.3.0 의 옛 메뉴를 업그레이드로 바꿔 끼움)는 `*`(desktop4)·`Directory`(desktop5 — 폴더 우클릭 복원)에 Root CLSID 하나, `Directory\Background` 에 '일집 열기'. 확장자마다 두지 않음('*' 와 겹쳐 두 번 나옴).
  C++ `IljipMenu.cpp`: Root(ECF_HASSUBCOMMANDS·아이콘 `<DLL 폴더>\icons\iljip.ico` 없으면 `Iljip.exe,0`)가 GetState 에서 고른 것(확장자·폴더인가만 — 파일 내용은 안 읽음)을 보고 하위 메뉴를 줌: 압축 하나 '일집으로 열기·여기에 풀기·"이름"\ 에 풀기·**알아서 풀기**·다른 곳에 풀기...',
  압축 여럿 '압축 풀기...·각각 알아서 풀기·각각 이름 폴더에 풀기', 그 밖 '압축하기...·**"이름.zip"으로 압축**·**"이름.7z"로 압축**'. 규칙의 C# 거울 `Services/Shell/ShellMenu.cs`(압축 확장자 목록·제목·동사·이름 — 계약 시험이 C++ 소스와 실제 DLL 둘 다로 맞춰 봄).
  예전 5개 클래스는 DLL 에만 남김(매니페스트에서 뺌 — 업그레이드 중 옛 등록이 불러도 깨지지 않게). **고른 것을 한 명령줄로**(`IljipLaunch.cpp` — 예전 파일마다 일집을 띄우던 것), 늘 큰따옴표·닫는 따옴표 앞 역슬래시 두 배(예전엔 `C:\` 가 다음 인자를 먹었음),
  30,000자가 넘으면 `%LOCALAPPDATA%\Iljip\temp\args\<guid>.lst`(UTF-8·CRLF·CREATE_NEW)로 `--list-file` — 처리하는 일집이 해석 전에 펼치고 지움(`CommandLineParser.ExpandListFile` — App 첫 실행·`OnSecondInstanceLaunched`, 남의 파일은 읽기만). C# 거울 `Services/Shell/ShellCommandLine.cs`.
  **알아서 풀기**(`--extract-smart`): `SmartExtractPlanner`(맨 위가 폴더 하나·파일 하나면 압축 파일 옆에 바로, 여럿·비었음·'..'·압축 자기 이름이면 '이름' 폴더) — `BatchExtractor` 의 새 규칙 `ExtractTargetKind.Smart` 가 목록을 읽은 뒤(목록까지 잠긴 7z 는 암호를 먼저 물음) 정하고,
  같은 이름은 W2-05 처리기, 끝나면 맨 위 폴더·'이름' 폴더를 엶(바로 푼 파일 하나는 안 엶). **창 없이 바로 압축**(`--compress-zip`·`--compress-7z`): 압축 창 없이 첫 원본 폴더에 '이름.zip/.7z'(이름은 압축 창 제안과 같은 규칙, 있으면 '이름 (2)'), 수준·솔리드·빼기·'다 만들고 시험하기'는 마지막 설정(바꾸지 않음),
  암호·나누기·날짜·원본 지우기 없음, 되면 조용히(뺀·건너뛴 파일이 있을 때만 안내), 권한 오류면 관리자 권한으로 다시 시도(압축 창으로). 나눔 ZIP 의 `.z01` 을 열면 같은 세트의 `.zip` 으로(`ArchiveVolumes.OpenTargetFor`).
  ③ **오른쪽 끌기**(`IljipDragDrop.cpp`, 매니페스트 desktop9 `fileExplorerClassicDragDropContextMenuHandler` — 폴더·드라이브): 끈 것이 모두 압축이면 '일집: 여기에 풀기'(`--extract-to-folder-in 놓은폴더 압축…`)를 먼저, '일집: 여기에 압축'(`--compress-to 놓은폴더 원본…`)은 늘.
  **zip·7z 아이콘 위에 끌어 놓아 넣기**(드롭 처리기 — IPersistFile·IDropTarget, 복사만 허락, 압축 자신은 빼고 `--add-to`): 매니페스트로 선언할 수 없어 ①의 등록기가 `Iljip.zip/.7z\shellex\DropHandler` + `CLSID\{61C3F459-…}\InprocServer32`(Apartment)로 넣음 — 셸 확장 DLL 이 일집 옆에 있을 때만(설정 칸 확인란·`--drop-handler`).
  `packaging/Build.ps1`: 설치된 SDK 가 없으면 NuGet SDK 로 C++·makeappx·signtool, 아이콘을 package 폴더로, `-NoSign`. 이 PC 에서 C++ DLL 빌드(경고는 예전 WRL C4324 하나) + makeappx 전체 검사 통과(서명·설치 안 함).
  덧붙이기만 한 것: `AppSettings.Shell`·`ShellSettings`, `AppServices.Registry`, `IShellLauncher.OpenDefaultAppsSettings`·`NotifyAssociationsChanged`, `CommandVerb`·키워드 다섯(키워드 11→16), `ExtractTargetKind.Smart`·`ArchiveOutcome.ShowFolder`, `StartupKind.Associations`, 시험 쪽 `FakeShellLauncher`·`TestServices.Registry`·`FakeUserInteraction.ProgressTitles`. `ArchiveExceptions`·`IUserInteraction` 은 바꾸지 않음.
  시험: 새 64개(`FileAssociationRegistrarTests` 15·`ShellCommandLineTests` 7·`ShellMenuTests` 6·`SmartExtractTests` 9·`QuickCompressTests` 6·`ShellExtensionContractTests` +7(DLL 셋 포함)·`CommandLineParserTests` +12·`StartupDecisionTests` +1·`CompressDialogXamlTests` +1). 푼 Skip 없음(0 → 0).
  기대가 바뀐 기존 시험(설계가 바뀐 곳 — 약하게 한 것 없음, Iljip.Tests/README 'W2-12'): `ShellExtensionContractTests` 의 CLSID(5 → 9개 표·매니페스트 3개)·COM 등록(모든 .cpp)·매니페스트 메뉴(확장자별 ItemType → '*'·Directory·Background·desktop9·패키지 속성)·키워드(모든 .cpp, `--list-file` 따로),
  `LocatorTests.우클릭_메뉴에_풀_수_있는_확장자가_모두_있다`(매니페스트 대신 `ShellMenu.ArchiveExtensions`·파일 연결 목록 — C++ 목록은 소스 대조), `CommandLineParserTests.키워드_목록은_모두_해석된다`(11→16)·`ToArgs는_…`(새 동사 6줄). DLL 시험 건너뛰기 판정에 새 .cpp 셋 더함(더 엄격).
  계약 테스트: 전체 2105 — 통과 2105·건너뜀 0·**실패 0**(빌드한 DLL 시험 넷 포함).
  위험·못 본 것: **진짜 탐색기·MSIX 설치·HKCU 등록·기본 앱 화면은 한 번도 안 띄움**(DLL 은 시험 프로세스에 직접 불러 글만 봄 — Invoke·InvokeCommand·Drop 은 안 부름). 패키지 셸 확장은 Windows 11 옛 메뉴('더 많은 옵션 표시')에는 안 나옴(Windows 동작).
  파일 연결은 넣은 일집 exe 경로로 들어감(셸 확장 package 폴더의 exe 로 넣을 것). 드롭 처리기를 넣은 채 `Rebuild.ps1` 이면 탐색기가 DLL 을 잡아 package 폴더 지우기가 실패할 수 있음. '시험하기' 하위 메뉴는 `--test`(W2-08 — stash)가 없어 뺌. 반영·되돌리기 절차는 '셸 확장·파일 연결 반영(W2-12)', 손 확인은 MANUAL_CHECKLIST 18절.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-14 Wave 2 통합 점검(W2-01·02·03·05·06·07·09·10·11·12 를 합친 뒤 — 커밋·푸시, 설치·등록·배포 안 함)**:
  Release 빌드(`dotnet build Iljip\Iljip.csproj -c Release` — 출력 `D:\Python\일집-build\dotnet\Iljip\bin\Release`) 경고 0·오류 0, 계약 테스트 Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번.
  ① **찾아서 고친 것 하나 — 설정 저장이 보안 프로그램 검사 중에 그냥 실패**: 첫 Release 전체 실행에서 `RecentFilesTests.설정_파일은_저장_직전에_다시_읽어_바꾼_칸만_얹는다` 가 실패(2104/2105).
  로그(`%LOCALAPPDATA%\Iljip\logs`)에 `File.Replace` 의 'IOException: 바꿀 파일을 제거할 수 없습니다'(1175 ERROR_UNABLE_TO_REMOVE_REPLACED) — 회사 PC DLP 가 막 쓴 settings.json 을 검사하는 동안 Windows 가 바꿀 파일을 못 치움.
  설정 저장의 다시 하기(`JsonSettingsStore.Retry`)는 공유·잠금 위반·접근 거부만 다시 해 보고 1175 는 곧장 실패(Update → null — 다음 저장 때 다시 하므로 잃지는 않지만 최근 파일·설정 저장이 한 번 밀림)였다.
  → `JsonSettingsStore.IsTransientReplace`(새, internal): 1175·1176·1177 도 200ms 간격 3번 다시(다시 할 때 '원래 파일이 있으면 바꾸기, 없으면 옮기기'라 1176·1177 처럼 원래 파일이 치워진 경우도 맞음). 디스크 가득 등은 그대로 실패.
  시험 `RecentFilesTests.설정_바꾸기가_잠깐_막혀도_다시_해_본다`(판정 표 + 실제 파일을 0.25초 잡고 있어도 저장). 압축 고치기의 바꾸기(`ArchiveSwap`)는 예전부터 이 경우 예비 이름 방식으로 넘어가 그대로.
  ② **상호작용 시험 `Contract/Wave2IntegrationTests.cs`(61개) — 모두 처음부터 통과(어긋남 없음)**: 우클릭 풀기('"이름"\ 에'·'여기에'·알아서 풀기)·풀기 창(`--extract`)의 암호 흐름이 ZIP(ZipCrypto·AES·한글 암호)·7z(이름 보임·목록 잠김)·EGG·RAR4·RAR5(파일 암호·머리 암호) 모두 같은 차례,
  형식이 다른 같은 암호 압축 다섯(목록 잠긴 7z 가 맨 앞)을 한꺼번에 풀면 한 번만 묻기, ZIP(SharpCompress)·7z(7z.dll 임시 폴더)·EGG 를 한 폴더에 바로 풀 때 같은 이름은 묶음 전체에 한 번만 묻고 [건너뛰기]면 건너뛴 압축은 휴지통으로 안 보냄([덮어쓰기]면 셋 다),
  나눠 만든 ZIP(AES)·7Z(한글 암호 + 파일 이름 암호)도 '다 만들고 시험하기'·'원본을 휴지통으로'가 진짜 시험기로 조각을 이어 시험(망가진 가운데 조각은 오류), 압축 속 파일을 연 채로 넣기·이름 바꾸기를 해도 다시 넣기가 그 고치기를 잃지 않음(ZIP·7Z),
  확장자가 바뀐 ZIP(CP949)·EGG·ALZ·RAR4 첨부도 창 없이 풀기의 처리기 고르기가 7z.dll 이 아니라 원래 처리기(한글 이름 그대로). `ReuseDeviceContractTests` +1: 실제 exe 로 ZIP CP949·7z·tgz·LZH·RAR5 를 같은 폴더에 두 번 풀어도 묻지 않고 '이름 (1)', 임시 파일 없음.
  설정 기억(같은 이름 기본 동작·풀고 지우기·폴더 열기·풀기 창 기억)이 창 없이 풀기(재사용장치)에 새지 않는 것: 창 없이 풀기는 설정·같은 이름 범위(`ExtractionScope`)·휴지통을 아예 안 씀(`RecycleAfterExtractTests.창_없이_풀기는_설정과_휴지통을_쓰지_않는다` 소스 확인) — 서비스 쪽에 설정을 읽는 전역 값 없음도 이번에 훑어 확인.
  확인한 차이(버그 아님 — 기록): 목록 잠긴 7z·RAR 에 틀린 암호면 메인 창 흐름은 '암호가 맞지 않아요' 경고 창 한 번 + 다시 묻기, 우클릭 풀기(메인 창 없이)는 경고 창 없이 암호 창 문구로만 다시 묻는다(각 길 안에서는 형식과 상관없이 같음).
  ③ 재사용장치 `_find_iljip` 의 찾는 차례를 다시 읽어 '일집-dist 재배포' 절을 고침(맨 앞에 `Iljip\bin\Release\net8.0-windows\win-x64\publish\Iljip.exe` 가 있고, 일집-dist 뒤에 `일집-build\package\Iljip.exe`(셸 확장 패키지)·`C:\Program Files\Iljip\Iljip.exe`).
  `BuildOutputTests` 는 `Iljip\bin\Release·Debug` 아래를 통째로 보므로 publish 폴더도 막는다. 발행 파일 목록·Wave 2 판이 재사용장치에 주는 영향도 그 절에.
  ④ **찾아서 고친 것 둘 — RAR4 시각(시간대 흉내 실행에서)**: `ILJIP_TEST_TZ=UTC` 전체 실행에서 `RarTests` RAR4 시각 8개(목록·풀린 파일)가 9시간 어긋나 실패 — Wave 2 동안 UTC 실행을 안 해 W2-01(RAR → 7z.dll)부터 숨어 있던 것.
  7-Zip 의 RAR4 처리기는 DOS 벽시계를 Win32 `LocalFileTimeToFileTime`(이 PC 의 '지금' 편차)으로 UTC 로 바꾸고 .NET 은 그 날짜의 규칙으로 되돌려, 둘이 다르면 그만큼 어긋난다
  (흉내, 실제로는 서머타임이 있는 나라에서 계절이 다른 파일 — 한국 PC 실사용에서는 같은 값). → `Services/DosWallClock.cs`(새): 같은 '지금' 편차의 `FileTimeToLocalFileTime` 으로 정확히 되돌림.
  `SevenZipEngine.BuildItems` 가 RAR4(`InArchiveFormat.Rar4` — RAR 파일·자동 풀림 exe 속 RAR4)의 목록 시각에, `Finish` 가 옮기기 전 임시 폴더의 풀린 파일 시각에 쓴다(SharpCompress 대신 길과 같은 값).
  시험 `RarTests.RAR4_DOS_벽시계는_7z_dll이_쓴_시간대_편차를_되돌려_그대로`(5줄 — 서머타임 시작·끝 시각)·`RAR4_벽시계_되돌리기는_이상한_시각에서_멈추지_않는다`. 기존 `RarTests` 기대는 그대로, 이제 UTC·'Pacific Standard Time' 흉내에서도 통과.
  ⑤ 기준을 바로잡은 시험 하나: `SfxCreateTests.만든_자동풀림_exe를_무인으로_풀면_원본과_같다` 의 시각 줄이 UTC 흉내에서 9시간 어긋나 실패 — 자동 풀림 스텁은 **따로 도는 프로세스**라 흉내가 닿지 않고 OS 시간대로 ZIP DOS 벽시계를 쓴다.
  풀린 시각을 `TestTimeZone.OsLocal`(흉내 전 OS 시간대 — 새)로 읽어 원본 벽시계와 정확히 같은지 본다(흉내가 없으면 예전과 똑같은 비교 — 단언·허용 범위 그대로). 기준이 정말 OS 시간대인지는 새 시험 `TimeZoneSwitchTests.OS_시간대는_흉내와_상관없이_이_PC의_시간대다`.
  ⑥ **찾아서 고친 것 셋 — 7z 만들기 취소가 바쁜 PC 에서 3초를 넘음(W2-03 부터 '바쁠 때만 실패'로 적어 두던 것)**: 이번 전체 실행 세 번 모두(Debug 3.1초·Release 6.4초·UTC 3.1초)
  `SevenZipCreateLargeTests.도중에_취소하면_3초_안에_…` 실패(빈 메모리 0.2GB). 따로 재 보니(시험용 작은 프로그램 — 저장소 밖) 취소하면 원본 읽기는 바로 멈추는데 7z.dll 이 **솔리드일 때만**
  스레드마다 이미 맡은 블록을 끝까지 압축한 뒤에 돌아왔다(그동안 진행 콜백도 없어 Compressing 에서 던지는 것도 소용없음 — 실측). 취소 시간 ≈ 블록 하나 압축 시간: c=8m 2.3~3.3초 / c=4m 1.0~1.3초 /
  c=2m 0.5~0.6초, 솔리드가 아니면 0.2초 안. → `SevenZipArchiveService.EngineParameters` 보통의 블록 `c=8m` → `c=2m`(압축 고치기는 W2-06 에서 같은 이유로 이미 2m).
  같은 PC·같은 때 실측: 160MB(무작위 120 + 글자 40) 124.92MB → 125.04MB(+0.1%)·32~36초 → 32초·메모리 +204MB → +62MB, 글자만 40MB 4.91MB → 5.03MB(+2.4%)·18초 → 10초,
  일집 서비스로 30% 지점 취소 2.4~3.3초 → 0.3~0.7초. 계약 시험의 3초 한도는 그대로, 기록 시험 `SevenZipCreateTests.기록_수준별_7z_dll_설정` 의 보통 값만 2m 으로.
  고친 뒤 전체 실행 기록(`기록_7z_…`): 30% 지점 취소 498·263·581ms, 처음 바이트 취소 17~27ms, 보통 160MB 32.4초·125.0MB·메모리 증가 61MB·100% 도달 31.5초(Release).
  계약 테스트: 전체 2175 — 통과 2175·건너뜀 0·**실패 0**(Debug·Release·UTC 세 번, 빌드한 DLL 시험 넷 포함). W2-04·W2-08 은 stash 그대로(되돌린 묶음 — 다시 할지는 사람이 정함), 세 stash 모두 drop 안 함.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-13 W2-04 — 압축 속 파일 바로 열기(I1-3)·끌어 꺼내기·Ctrl+C(I1-4)·그림 미리 보기(I3-7)(보관본 기록 — 검증에서 되돌려 `iljip-wave2-W2-04-failed` 에 두었던 것.
  2026-10-14 IJ3-01 이 W2-06·Wave 2 위에 다시 들이며 아래와 다르게 바꾼 것은 그 문단에. 전체 시험 수는 그때 기준)**: 화면 쪽은 `ViewModels/MainViewModel.EntryOpen.cs`(partial)·
  `ViewModels/ImagePreviewViewModel.cs`, 바탕은 새 서비스들. **열기**: 목록에서 파일 두 번 누르기·Enter(우클릭 '열기') → `Services/ArchiveWorkspace.cs` 가 압축마다
  `%LOCALAPPDATA%\Iljip\temp\open\<key16>\<순번>\`(key16 = SHA-256(전체 경로|길이|수정 시각) 앞 16글자, `owner.lock` 을 혼자 열어 '살아 있음', 같은 항목을 다시 열면 새 순번 —
  앞 파일을 워드가 잠가도 안 겹침)에 그 파일만 꺼내(`ExtractEntriesAsync`, 진행 창·암호는 열 때·앞서 맞은 것 → 모르면 묻고 틀리면 다시) `IShellLauncher.OpenFile`(ShellExecute).
  실행 파일(`Services/RiskyFileTypes.cs` — .exe .com .bat .cmd .ps1 .psm1 .vbs .vbe .js .jse .wsf .wsh .msi .msp .scr .pif .lnk .url .hta .cpl .jar .reg .application .appref-ms .msix .appx,
  1차 검증 뒤 .chm .msc .scf .xll .wsc .sct .inf .library-ms .search-ms .searchconnector-ms .settingcontent-ms .ps1xml .psd1 .psc1 .vb .ws .msh* .iso .img .vhd .vhdx 등 보강)은
  먼저 "'x.exe'는 실행 파일이에요. 보낸 사람을 확실히 알 때만 여세요. 그래도 열까요?"([열기]/[취소], 기본 [취소] — 설정 `open.warnExecutables` 로 끌 수 있음).
  원본 압축의 `:Zone.Identifier`(인터넷 표식)를 꺼낸 파일마다 그대로 붙여 오피스 '제한된 보기' 유지(`Services/ZoneIdentifier.cs` — 열기·미리 보기·복사는 꺼낸 파일에,
  끌기는 탐색기가 내용 스트림으로 새 파일을 만들어 임시 파일의 표식이 안 옮겨지므로 끌기 데이터에 셸 형식 'ZoneIdentifier'(영역 번호)를 실음 — 1차 검증 뒤, 실제 반영은 손 점검;
  NTFS 아니면 조용히 넘어감; 보통 '압축 풀기'에는 아직 안 옮김 — 보고 안건 그대로). 연 파일은 처음 크기·시각·SHA-256 을 기록(`OpenedEntry` — W2-06 의 고친 파일 감시가 씀,
  열기 전에 뒤 스레드에서 잼), 일집을 닫을 때 고친 파일이 있으면
  "압축 안에서 열어 고친 파일이 있어요 … 일집을 닫으면 고친 내용은 … 지워져요"([닫지 않기] 기본/[그냥 닫기]). 압축을 닫거나 바꿔도 작업 폴더는 앱을 닫을 때까지 둠
  (1차 검증 뒤 — 고쳤든 안 고쳤든, 같은 압축을 다시 열면 그 폴더를 다시 씀).
  앱을 닫으면 지우고(다른 프로그램이 잡은 파일만 남김), 다음 시작 때 `Services/TempJanitor.cs` 가 뒤에서 청소(temp\open·drag·preview·list 중 owner.lock 이 안 잡힌 것, temp\clip 은 24시간 지난 것,
  청소 시작 뒤 생긴 폴더는 안 건드림 — 창 없이 풀기·두 번째 실행에서는 안 부름). **끌어 꺼내기**: `Services/VirtualFileDataObject.cs` 가 COM IDataObject 를 직접 구현 —
  `FileGroupDescriptorW`(상대 경로·폴더 속성·크기·수정 시각·FD_PROGRESSUI, 한글 UTF-16), `FileContents`(GetData 를 받은 순간에만 `Services/LazyEntryExtractor.cs` 가 그 항목만
  `temp\drag\<guid>\i\` 에 꺼내 FileStream 을 감싼 IStream — 같은 항목은 한 번만, 압축 서비스는 끌기 전용 인스턴스, 큰 항목(8MB 이상)은 진행 창·[취소]면 탐색기에 E_ABORT·반쪽 없음),
  'Preferred DropEffect' = 복사, `IDataObjectAsyncCapability` 처음부터 켬(탐색기가 놓은 뒤 자기 스레드에서 당겨 감 — EndOperation 에서 정리). 서술자는 `Services/DragPayloadBuilder.cs`
  (폴더 재귀, 폴더 항목 없는 압축도, 이름 겹침 '(2)', '..'·금지 글자 다듬기, 259글자 넘으면 빼고 '경로가 너무 긴 항목 N개는 끌어낼 수 없어요'). CF_HDROP 지연 렌더링은 안 씀.
  암호는 끌기 전에만(`ArchiveEntry.IsEncrypted` 를 새로 — ZIP·7z·RAR·EGG 목록이 채움): 모르면 가장 작은 암호 파일로 확인하며 묻고 "암호를 확인했어요. 다시 끌어 주세요."(암호 창 때문에 마우스를 놓았으므로).
  UI 스레드로 들어온 GetData 는 꺼내는 동안 디스패처를 돌려(DispatcherFrame) 진행 창이 그려지고 취소가 먹게. **Ctrl+C**: 고른 것을 `temp\clip\<guid>\` 에 꺼내(진행 창·취소) 표식 붙이고
  `IClipboardService.SetFileDrop`(FileDrop + 'Preferred DropEffect' 01 00 00 00, OleFlushClipboard 라 일집을 꺼도 붙여넣기 됨) — 사본은 켜 둔 동안 최근 2개, 닫을 때 마지막 1개만 남김
  (24시간 뒤 청소 — 1차 검증 뒤). 클립보드 경로는 풀기 엔진이 실제로 쓴 자리(`LazyEntryExtractor.LocateExtracted` — 바뀐 이름·'(1)' 비켜 쓰기까지), 못 찾으면 알리고 안 넣음.
  **미리 보기**: 툴바 그림 단추로 오른쪽 칸(320px — 짜임 최소 변경) 켜기·끄기(설정 `ui.previewVisible`), 고른 그림(.jpg .jpeg .png .bmp .gif .tif .tiff .ico .webp)만 `temp\preview\<guid>\` 에
  꺼내 `IImageDecoder`(진짜 `WicImageDecoder` — 프레임 크기를 먼저 읽고 4,000만 화소를 넘으면 가로 4,096·4,000만 화소 이하로 줄여, OnLoad + Freeze) 로 읽고 바로 지움.
  ←→·◀▶ 는 지금 폴더 그림만 목록 차례로 돌고(끝 → 처음) 목록 선택도 따라감, 확대 맞춤/100%/±25%(10~800%), 압축 크기나 원래 크기가 50MB 넘으면 '미리 보기에는 너무 커요',
  최근 3장·합계 200MB 기억, 암호를 모르면 묻지 않고 '암호를 넣으면 볼 수 있어요' + [암호 넣기], 못 읽으면(WebP 코덱 없음 등) '이 그림은 미리 볼 수 없어요'.
  (1차 검증 뒤: 그림 읽기는 한 번에 하나·기다리다 건너뛴 것은 안 읽음, 다른 압축을 열기 전에 시작한 읽기는 기억 안 함, [암호 넣기] 뒤 암호와 상관없는 실패는 '미리 볼 수 없어요')
  **설정 파일 처음 들임**(설계 §4): `Services/AppSettings.cs` — `AppSettings`(schemaVersion 1·ui·open + 모르는 필드 [JsonExtensionData] 보존)·`ISettingsStore`·`JsonSettingsStore`
  (`settings.json.tmp` 에 쓰고 File.Replace, 잠김 200ms×3, 깨지면 `settings.bad-yyyyMMddHHmmss.json` 으로 옮기고 기본값, 한글 그대로)·`InMemorySettingsStore`. VM 은 처음 쓸 때 읽고
  (시험 VM 은 진짜 settings.json 을 안 건드림) 창을 닫을 때 한 번 저장. 주입: `Services/AppServices.cs`(Shell·Clipboard·ImageDecoder·Clock·Settings) — `MainViewModel(ui, locator, services = null)`
  (null 이면 진짜, 기존 생성자 호출은 그대로 컴파일). `AppPaths` 에 OpenRoot·DragRoot·ClipRoot·PreviewRoot·SettingsFile 덧붙임, `IClock`/`SystemClock` 새로. 창: `MainWindow.xaml(.cs)` 에
  두 번 누르기(파일)·Enter·Ctrl+C·←→·우클릭 '열기'/'복사 (Ctrl+C)'·끌기 시작(끌기 임계값, 여러 개 고른 채 잡아도 선택 유지)·닫기 확인·미리 보기 칸. `App.OnStartup` 에 청소(뒤에서).
  **설계와 다르게 한 것**: 끌기는 설계의 `DragDrop.DoDragDrop(list, new DataObject(virtualObj), Copy)` 대신 ole32 `DoDragDrop` 을 직접 부름(우리 IDropSource) — WPF DataObject 로 감싸면
  탐색기가 QueryInterface 로 비동기 모드(IDataObjectAsyncCapability)를 찾지 못한다(WPF 래퍼가 그 인터페이스를 안 가짐). 끌기 전 암호 확인은 '첫 끌기 때 묻고 다시 끌게'로(암호 창 때문에
  마우스를 놓아 그 끌기는 이어 갈 수 없음). 시험: 새 계약 `Contract/EntryWorkspaceTests.cs`·`EntryOpenTests.cs`·`DragOutTests.cs`·`ClipboardCopyTests.cs`·`ImagePreviewTests.cs`(93개 —
  가짜 `TestSupport/FakeServices.cs` 의 FakeShellLauncher·FakeClipboard·FakeImageDecoder·FakeClock·`TestServices`, 작은 ZIP `TestZip`; 창·실제 실행·진짜 클립보드 없음, 진짜 WIC 는 STA 로 작은 PNG 1개).
  기존 테스트 기대는 안 바꿈. 창 XAML 이 실제로 읽히는지는 scratchpad 의 별도 프로그램으로 창을 '만들기만' 해 확인(보이지 않음). 위험·실기기 확인 필요(MANUAL_CHECKLIST 12절):
  COM 직접 구현은 진짜 탐색기와 주고받기를 자동 시험 못 함(FORMATETC·STGMEDIUM 해제·lindex·비동기 모드에서 탐색기가 어느 스레드로 부르는지), PCFILTER·백신이 꺼낸 임시 파일을 잠깐 잠가
  열기가 늦거나 청소가 다음으로 밀릴 수 있음, Zone.Identifier 로 오피스가 '제한된 보기'로 엶(의도 — 안내 필요할 수 있음), 큰 WebP·TIFF 디코딩 메모리, WebP 코덱 없는 PC,
  임시 폴더 파일을 고쳐 저장하고 '그냥 닫기'면 고친 내용이 사라짐(W2-06 감시 전까지). 계약 테스트 실패 0(전체 1398 — 통과 1397·건너뜀 1: I2-10, 계약만 1241 — 통과 1240·건너뜀 1, 기록 157.
  W2-03 후 1305 — 통과 1304·건너뜀 1 에서). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-04 1차 검증 수정(커밋 전, 배포 안 함)** — 지적 10건(major 2·minor 8) 모두 직접 확인해 맞아서 고침:
  ① (major) 미리 보기: 그림을 읽는 도중 다른 압축을 열면 끝난 옛 읽기가 비운 기억에 다시 들어가 새 압축의 같은 경로 그림 대신 옛 그림을 보임 →
  `ImagePreviewViewModel` 에 압축 세대(`_generation`, `Reset` 마다 +1) — 그 전에 시작한 읽기 결과는 기억하지 않음. ② (major) Ctrl+C: 클립보드 경로를 '꺼낸 곳\원래 이름' 으로
  다시 만들어 디스크 이름이 바뀌는 항목(':' '?' '*' 장치 이름 — `EntryPaths.ToSafeRelative`)은 꺼내 놓고도 실패, 여럿이면 말없이 빠짐 → 새 `LazyEntryExtractor.LocateExtracted`
  (ToSafeRelative + GetFullPath 로 예상 자리 → 같은 폴더에서 ①대소문자까지 같은 이름 ②엔진이 비켜 쓴 '이름 (n).ext' ③대소문자 무시, 한 파일 자리는 한 항목에만), 하나라도 못 찾으면
  '꺼낸 파일을 찾지 못했어요:\n- 이름'으로 알리고 아무것도 안 넣음(사본도 지움). `FindExtracted`(열기·미리 보기·끌기)도 이것을 먼저 씀 — 대소문자만 다른 두 파일은 고른 열쇠가
  대소문자를 무시해 하나만 골라도 둘 다 꺼내져 열기·끌기가 앞엣것 내용을 줄 수 있던 같은 뿌리의 버그도 고쳐짐. ③ Ctrl+C 열쇠를 대소문자 무시로 합치던 것 → 그대로(Ordinal) — 둘 다
  넣음(뒤엣것은 엔진처럼 'report (1).txt' — 끌기 서술자는 예전대로 '(2)'). 폴더는 엔진이 합치므로 같은 자리면 한 번만. ④ 미리 보기 읽기는 한 번에 하나(`SemaphoreSlim`),
  기다리다 건너뛴 것은 읽지 않음(시작한 WIC 읽기는 못 멈춤 — 80ms 간격 12장에 동시 7장이던 것). ⑤ 연 파일의 처음 SHA-256 은 열기 전에 `Task.Run` 으로(화면이 굳지 않게);
  압축을 바꿀 때 고쳤는지 재던 해시도 없어짐(⑦). ⑥ 끌기 인터넷 표식: 탐색기는 FileContents 스트림으로 새 파일을 만들어 임시 파일의 ADS 가 안 옮겨짐 → 원본에 표식이 있으면
  끌기 데이터에 셸 형식 `ZoneIdentifier`(CFSTR_ZONEIDENTIFIER — 영역 번호 DWORD, `ZoneIdentifier.ParseZoneId`)를 실음. 이 PC 의 shell32·windows.storage 에 그 형식 이름이 있음은 확인했으나
  **탐색기가 실제로 붙이는지는 손 점검(MANUAL_CHECKLIST 12절 새 줄)**. ⑦ 압축을 바꾸거나 같은 압축을 다시 열면 안 고친 파일의 작업 폴더를 바로 지우던 것(메모장에서 저장 전이면
  저장 실패) → 요구('앱 종료·다음 시작 때')대로 모든 작업 폴더를 앱을 닫을 때까지 두고, 같은 압축을 다시 열면 그 폴더를 다시 씀(`EnsureWorkspace`). ⑧ 실행 파일 경고 목록 보강
  (.chm .msc .scf .xll .wsc .sct .inf .library-ms .search-ms .searchconnector-ms .settingcontent-ms .ps1xml .psd1 .psc1 .vb .ws .msh* .diagcab .appinstaller .gadget .xbap .iso .img .vhd .vhdx 등).
  ⑨ Ctrl+C 사본: 켜 둔 동안 최근 2개(지금 것 + 붙여넣기가 아직 돌 수 있는 바로 앞 것), 닫을 때 마지막 1개만(그 뒤 24시간 청소). ⑩ 미리 보기 [암호 넣기] 뒤 암호와 상관없는 실패(손상 등 —
  오류 창은 이미 보임)는 '이 그림은 미리 볼 수 없어요'(예전엔 암호 단추를 또 보임) — `IPreviewHost.ExtractWithPromptAsync` 가 `UseOutcome`(Done·Cancelled·Failed)을 돌려줌.
  **기대가 바뀐 기존 테스트 1개(계약 변경 — 약화 아님)**: `EntryOpenTests.다른_압축으로_바꿔도_고친_파일은_…` 의 '안 고친 작업 폴더는 압축을 바꿀 때 지워야 함' →
  `다른_압축으로_바꿔도_연_파일은_앱을_닫을_때까지_남는다`(같은 압축 다시 열기·돌아오면 같은 작업 폴더 새 순번·닫을 때 고친 것만 알림·앱을 닫으면 모두 지움까지 단언).
  새 시험 42개(미리 보기 5·Ctrl+C 9·열기 2·끌기 2·표식 번호 6·실행 파일 판정 18행), 시험 도우미 `TestSupport/DispatcherThread.cs`(창 없는 WPF 디스패처 스레드 — 화면 스레드 흉내),
  `FakeImageDecoder` 에 막아 두기(Gate)·동시 읽기 수. 덤으로 본 것(안 고침): 화면의 암호 오류 판정(`MainViewModel.IsPasswordRelated`)이 예외 문구 속 '암호'를 보기 때문에
  경로에 '암호'가 든 압축(예: `암호사진.zip`)이 사라지거나 못 읽히면 오류 대신 암호 창이 뜸. 계약 테스트 실패 0(전체 1440 — 통과 1439·건너뜀 1: I2-10, 계약만 1283 — 통과 1282·건너뜀 1, 기록 157.
  W2-04 후 1398 — 통과 1397·건너뜀 1 에서). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-13 W2-04 2차 검증 수정(커밋 전, 배포 안 함)** — 지적 5건(major 1·minor 4) 모두 직접 확인(검증 하네스 A12·A17·A19 재현)해 맞아서 고침:
  ① (major) 끌어 꺼내기: 탐색기가 FileContents(i) 를 달라고 할 때마다 그 항목만 꺼내, 솔리드 7z(일집 7z 기본값·반디집/7-Zip 도 기본 솔리드)의 폴더를 끌면
  파일마다 묶음 처음부터 다시 풀어 파일 수의 제곱으로 느려짐(재현: 100KB 솔리드 7z 100개 21.5초·200개 47초) → `LazyEntryExtractor` 가 파일이 둘 이상이면 첫 요청 때
  끌어낸 파일을 `temp\drag\<guid>\all\` 에 `ExtractEntriesAsync` 한 번으로 꺼내고(넘기는 열쇠는 고른 것만 — `BatchSelectors`, 파일마다 넘기면 엔진 고르기가 느려짐),
  자리는 `LocateExtracted`(+ 이미 준 자리 건너뛰기)로 짝지은 뒤 나머지 요청은 디스크에서 바로 줌('놓은 순간에 꺼내기'는 그대로). 진행 창은 이번에 꺼낼 파일의 합이 8MB 이상이면
  하나("끌어낸 파일 N개를 꺼내는 중..."), 진행 창 [취소]면 그 요청은 E_ABORT·반쪽 없음. 한 번에 꺼내기가 실패(폴더 안 손상·경로 공격 항목 하나)·취소되면 그 뒤 요청은 예전처럼
  항목마다 따로(멀쩡한 파일은 받음). 고친 뒤 같은 하네스: 100개 1.1초·200개 2.3초·300개 3.3초(폴더째 한 번에 풀기 3.2초와 같음 — 전엔 95초), ZIP 300개 3.8초 → 1.7초.
  파일 하나만 끌면 예전처럼 그것만.
  ② 미리 보기: 기억 열쇠·자리 찾기가 대소문자를 무시해 'A.png' 를 본 뒤 'a.png' 를 고르면 'A.png' 를 보이고 → 로 'a.png' 에서 멈춤 → 열쇠는 경로 그대로(Ordinal), 자리는 같은 객체 먼저.
  ③ 끌기 중 실패가 여럿이면 항목마다 오류 창 → 끌기 한 번에 오류 창 한 번, 알림줄 "오류 발생 — 끌어 꺼내지 못한 파일 N개".
  ④ 실행 파일 경고에 .rdp .py .pyw .pyz .pyzw .pyc .website .jnlp .msu 더함.
  ⑤ 안의 파일 열기가 압축 안 폴더 구조째 꺼내(`…\open\key16\1\폴더\…\파일`) 깊은 폴더 파일은 엑셀(전체 218자)이 못 열 수 있었음 → 옆 폴더 `…\1.part\` 에 꺼낸 뒤
  파일만 `…\1\파일 이름`(고른 항목 이름 — 엔진이 'report (1).txt' 로 비켜 써도 'report.txt')으로 옮김(`OutputFiles.MoveNew` — 잠김 재시도, 못 옮기면 꺼낸 자리에서 엶),
  '열 프로그램을 찾지 못했어요'는 ERROR_NO_ASSOCIATION(1155)일 때만·그 밖엔 "'이름'을(를) 열지 못했어요." `OpenedEntry.EntryKey` 는 압축 안 경로 그대로.
  **고치다 찾은 것**: 경로에 '~' 가 있으면 .NET `Path.GetFullPath` 가 짧은 이름을 풀며 이미 있는 파일의 실제 대소문자를 돌려줘, 엔진이 대소문자만 다른 두 파일의 뒤엣것을
  'Report (1).txt' 로 쓰고 `LocateExtracted` 도 찾을 이름을 'Report.txt' 로 바꿔 앞엣것을 줌(옆 폴더를 처음에 '1~' 로 했다가 시험이 잡음) → 옆 폴더는 '.part',
  `LocateExtracted` 는 찾을 이름을 열쇠에서 만들고 비켜 쓴 '(n)' 이름은 대소문자 무시로도 찾음(사용자 폴더 경로에 '~' 가 있는 PC 대비).
  **기대가 바뀐 기존 테스트(끌기 계약 변경 — 약화 아님)**: '부른 항목만 하나씩' → '첫 요청에 한 번에' — `DragOutTests` 5개의 '꺼낸 파일이 하나' 단언을 '끌어낸 파일 전부(이름·내용)'로,
  미리 보기 기억 열쇠 4곳 대문자 → 원래 이름, `EntryOpenTests.파일을_열면_…` 의 경로 단언을 새 구조로(작업 폴더 전체 — 더 엄격). 자세히는 `Iljip.Tests/README.md` 'W2-04 2차 검증 뒤 고친 것'.
  새 시험 9개(끌기 7 — 솔리드 7z 한 번에·실패 뒤 항목마다·열쇠 고르기·파일 하나·합쳐 큰 진행 창·오류 창 한 번·'~' 자리 찾기, 열기 2 — 깊은 폴더·열기 실패 문구, 미리 보기 1)와 실행 파일 판정 10행.
  `LocateExtracted` 에 폴더 목록 함께 쓰기(`listings` — 한 번에 꺼낸 파일 수천 개를 짝지을 때 폴더를 파일마다 다시 읽지 않게)도 더함.
  손 점검 12절에 2줄(솔리드 7z 사진 폴더 끌기·깊은 폴더 엑셀 열기). 덤으로 본 것(안 고침): Ctrl+C 사본도 `temp\clip\<guid>\압축 안 폴더\…` 구조라 아주 깊은 항목은
  탐색기 붙여넣기가 경로 길이에 걸릴 수 있음(실기기 확인 못 함); 끌기 서술자는 맨 위에서 대소문자만 다른 두 파일을 함께 고르면 뒤엣것을 뺌(`DragPayloadBuilder` 의 seenKeys 가 대소문자 무시).
  계약 테스트 실패 0(전체 1460 — 통과 1459·건너뜀 1: I2-10, 계약만 1303 — 통과 1302·건너뜀 1, 기록 157. 1차 검증 수정 후 1440 — 통과 1439·건너뜀 1 에서).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-14 IJ3-01 — W2-04 다시: 끌어 꺼내기·Ctrl+C(I1-4)·그림 미리 보기(I3-7)·바로 열기 나머지(I1-3)(커밋 f1ef006·푸시, 설치·배포 안 함)**:
  보관분 `iljip-wave2-W2-04-failed`(W2-03 4a4b093 위에서 만든 것)를 `git stash apply` 로 지금 main 위에 얹고(충돌 7파일 + 이미 있던 새 파일 9개는 손으로 — main 의 뜻이 이김, stash 셋은 drop 안 함),
  겹치는 바탕은 main 것을 썼다: 설정·`AppServices`(W2-05 — `Open.WarnExecutables`·`Ui.PreviewVisible`·Clipboard·ImageDecoder 만 덧붙임), 작업 폴더·열기·다시 넣기(W2-06 — 열기 길은 그대로,
  `ExtractForUseAsync` 를 여러 열쇠·진행 창 없음·'꺼낸 뒤 이어서 할 일'로 넓혀 열기·Ctrl+C·끌기 전 암호 확인·미리 보기 [암호 넣기]가 같이 씀, 꺼낸 자리는 `LazyEntryExtractor.FindExtracted`),
  닫기는 W2-06 의 `ConfirmExitAsync` 하나(보관분의 '닫지 않기/그냥 닫기' 창 `ConfirmClose`·XAML `Closing`/`Closed` 연결은 버림), 설정 저장은 `SaveOnExit`(미리 보기 칸 켜 둠을 트리 너비·최근 파일과 함께 한 번 —
  보관분처럼 Shutdown 이 저장하지 않음). 화면 쪽 새 partial `ViewModels/MainViewModel.EntryCopy.cs`(미리 보기·Ctrl+C·끌기), 창은 미리 보기 칸(4번째 열 320px)·툴바 그림 단추·우클릭 '복사 (Ctrl+C)'·끌기 시작·Ctrl+C·←→.
  **검증 지적 네 가지**: (a) 미리 보기 옛 그림 — 보관분은 세대를 미리 보기 안에서만 세고 목록을 읽은 직후(지금 압축 경로를 바꾸기 **전에**) 올려, 그 틈(옛 목록을 비울 때 선택이 바뀌어 미리 보기가 불림)에
  시작한 읽기가 옛 압축에서 꺼낸 그림을 새 세대로 기억했다(새 압축의 같은 경로 그림 대신 보임 — 시험으로 재현). 고친 뒤 목록 다시 읽기(같은 압축)는 아예 비우지 않았다.
  → 세대는 화면 한 곳(`MainViewModel._archiveGeneration`·`BumpArchiveGeneration` — `OpenArchiveAsync` 가 경로·암호를 바꾼 **뒤**, 고친 뒤 `ReloadArchiveListAsync`, `CloseView`)에서 올리고,
  `IPreviewHost.ArchiveGeneration`·`ExtractQuietAsync(generation, …)`(지났으면 꺼내지 않음)·`ExtractWithPromptAsync(generation, …)`, 기억은 (세대, 경로) 열쇠, 끝났을 때 세대가 다르면 기억·표시 안 하고 '불러오는 중'이면 비움.
  (b) Ctrl+C 이름 바뀜 — 새 `Services/ClipboardCopyPlan.cs`(순수 논리): 자리 찾기(`LocateExtracted` + 폴더 목록 한 번), 바뀐 이름 모으기(고른 것은 원래 NFC 이름 vs 디스크 이름 — '(1)' 비켜 쓰기 포함,
  고른 폴더 안은 `EntryPaths.SafeComponent` + 끝 점·공백 떼기로 처음 바뀐 조각 하나씩), 넣은 뒤 `ShowInfo` '복사했어요. 다만 아래 이름은 Windows 에서 그대로 쓸 수 없어서(…) 바꿔서 복사했어요: - 원래 → 바뀐'(5개 + 외 N개),
  상태줄 'N개 항목을 복사했어요(이름 M개는 바꿈) — …'. `: ? * | " < >`·CON·'...'·이름이 바뀌는 폴더를 한꺼번에 골라도 빠짐없이(시험). 못 찾은 것이 있으면 예전처럼 알리고 아무것도 안 넣음.
  (c) 솔리드 7z 폴더 끌기 — 보관분의 '첫 요청에 한 번에'는 두고 남은 제곱을 없앰: `DragPayloadBuilder.Build`(고른 폴더마다 압축 전체를 다시 훑음 → 한 번 훑으며 조상 경로로 나눠 담기, 결과 같음),
  `LazyEntryExtractor.BatchSelectors`(폴더마다 StartsWith → 해시 조상 찾기, **경로 공격 항목이 섞이면 안전한 파일 열쇠만** — 보관분은 그때 한 번에 꺼내기가 통째로 거부돼 모든 파일을 하나씩),
  진행 창 [취소] 뒤 남은 요청은 꺼내지 않고 E_ABORT(`IsCancelled` — 보관분은 탐색기가 계속 달라는 파일을 하나씩, 큰 것은 진행 창도 또), 끌기 전 암호 확인 `ExpandFiles` 한 번 훑기,
  엔진 열쇠 고르기 `SharpCompressArchiveServiceBase.EntrySelector`(SharpCompress·7z.dll·EGG 공용, `EggAlzArchiveService` 도 — 열쇠마다 비교 → 해시 + 조상 찾기, 대소문자·'\'·NFD 무시 그대로).
  (d) Ctrl+C 수천 개 — 꺼낸 뒤의 정리를 같은 진행 창을 띄운 채 `Task.Run` 에서(단계 알림 '복사할 파일을 정리하는 중...'·[취소] — `ExtractForUseAsync(…, afterExtract)`), `ZoneIdentifier.Apply(…, ct)` 파일마다 취소 확인,
  사본 지우기도 뒤에서, 도는 중 또 누르면 겹치지 않고 '앞의 복사가 아직 끝나지 않았어요…'. 클립보드에는 화면 스레드에서 넣음(시험: 창 없는 디스패처 스레드로 3,000개).
  **덤**: 그림 폭탄 — `WicImageDecoder` 가 2억 5천만 화소(JPEG 10억) 넘으면 풀기 전에 `ImageTooLargeException` → '미리 보기에는 너무 커요'(PNG 는 줄여 읽어도 WIC 가 원래 크기를 다 풀 수 있어서 — 실제 메모리는 재지 않음, 보수적 상한).
  청소기 `TempJanitor` — 보관분은 잠금 없는 preview·list 도 '주인 없음'으로 지워 다른 일집 창이 쓰는 것까지 지울 수 있었음 → open·drag 만 잠금으로, preview 1시간·list·convert·args·clip 24시간 나이로
  ('향후 작업'의 convert 찌꺼기도 이것으로). 실행 파일 경고는 설정 `open.warnExecutables`(설정 창 칸은 없음 — settings.json 만).
  **시험**: 전체 2175 → 2345(+170 — 보관분 5파일 + 새 시험: 미리 보기 세대 4·그림 폭탄 2, Ctrl+C 이름 3·수천 개 3·열쇠 고르기 1, 끌기 묶음 빠르기·공격 항목 섞임·진짜 손상 실패 3 등).
  기대가 바뀐 보관분 시험과 이유는 `Iljip.Tests/README.md` 'IJ3-01 에서 더한 것'(약화 없음 — 공격 항목 실패 시험은 새 동작으로 바꾸고 진짜 손상으로 같은 이름 시험을 다시 만듦, 취소 시험은 더 엄격).
  main 기존 시험은 `MainWindowEditUiTests` 의 목록 우클릭 메뉴 기대에 '복사 (Ctrl+C)' 한 줄만 더함. 손 점검은 MANUAL_CHECKLIST 20절(보관분 12절을 옮기고 IJ3-01 줄 9개 더함 — 12절 번호는 W2-05 가 씀).
  위험·못 본 것: 진짜 탐색기와의 끌어 놓기·붙여넣기·미리 보기 화면(손 점검), 손상 항목 하나로 한 번에 꺼내기가 **실패**한 끌기만 아직 항목마다(솔리드면 느림 — 향후 작업), 이름 바뀜 알림은 폴더 안의 대소문자 겹침 '(1)' 은 세지 않음,
  알림 창은 바뀐 이름이 있을 때만 뜸(작은 창 하나). 계약 테스트 실패 0(전체 2345 — 통과 2345·건너뜀 0, Debug). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-15 IJ3-03 — 인터넷 표시(MOTW) 전파·7-Zip 라이선스 원문 대조(사용자 결정 I2-15)(커밋 981a048·푸시, 설치·배포 안 함)**: (재개 1015 — Windows 업데이트 재부팅 뒤 깨끗한 f1ef006 위에서 처음부터)
  ① **MOTW**: 압축에 `:Zone.Identifier` 가 있으면 일집 창·탐색기 우클릭 풀기(`MainViewModel.ExtractToFolderCoreAsync` — 풀기 창·고른 항목·명령줄, `BatchExtractor` — 여기에·'이름' 폴더·알아서·여러 개·관리자 권한으로 다시 시도)가
  다 푼 뒤 **이번 풀기가 만든(덮어쓴) 파일에만** 원본 바이트 그대로 붙인다(`ZoneIdentifier.MarkExtractedAsync` — 같은 이름 처리 범위의 새 `ExtractionScope.CreatedFiles` 를 씀: 풀 곳에 원래 있던 파일·[건너뛰기]한 파일·같은 폴더의 압축은 안 건드림).
  표식은 풀기 전에 읽고(다 풀고 휴지통으로 보내도), 손상·취소로 멈춰 앞서 풀린 파일이 남아도 붙이며(finally — 암호 오류로 되돌린 것은 이미 없음), 뒤 스레드에서 붙이는 동안 진행 창 단계 알림 '인터넷에서 받은 파일 표시를 붙이는 중'
  (여러 개 풀기 `BatchProgress` 가 `IsStage` 도 넘기게). raw bz2 는 장부 없이 써서 범위에 안 적혔음 → `OutputTarget.Commit` 이 덮어쓰기가 아닐 때도 `NoteCreated`.
  설정 `extract.keepInternetMark`(기본 켬 — ☰ ▸ 설정 '압축 풀기' 칸 "인터넷에서 받은 압축 파일이면 푼 파일에도 '인터넷에서 받은 파일' 표시 붙이기") — 끄면 열기·끌기(`LazyEntryExtractor.KeepInternetMark` — 끌기 데이터의 'ZoneIdentifier' 형식도 안 줌)·Ctrl+C 도 안 붙임.
  **창 없이 풀기(`App.RunHeadlessExtractAsync` — 서비스만 부름)는 붙이지 않는다**(엔진은 그대로 — 7z.dll `PropagateMarkOfTheWeb=false`, 주석에 '켜지 말 것').
  **고치다 찾은 것**: 대체 스트림을 쓰면 NTFS 가 파일 수정 시각을 '지금'으로 바꾼다(실측) — IJ3-01 의 열기·Ctrl+C 도 꺼낸 파일 시각이 '지금'이 되던 것 → `ZoneIdentifier.TryMark` 가 원래 시각으로 되돌리고,
  읽기 전용 파일은 잠깐 풀었다 되돌린다. 64KB 넘는 비정상 표식은 통째로 옮기지 않고 영역 번호만(풀린 파일마다 큰 스트림으로 디스크를 채우지 않게).
  ② **7-Zip 라이선스**: 사용자가 허락해 받은 원문(`D:\Python\일집-build\licenses-source\7-Zip_license.txt` — 6031바이트, SHA-256 3A184AA1…5DDB, 'Copyright (C) 1999-2025')을 `Iljip\licenses\7-Zip-License.txt` 로 **바이트 그대로**.
  대조해 바로잡은 것: BSD 3·2조항 파일 — 예전 기억본은 저작권 줄에 연도 없음('Copyright (c) Igor Pavlov.' → 원문 'Copyright (c) 2023-2025 Igor Pavlov.'), 원문에 없는 소스 파일 이름(LzfseDecoder.cpp·ZstdDec.c·Xxh64.c)·머리말을 덧붙였고 줄바꿈이 달랐음 → 원문 절을 그대로 자름.
  `THIRD-PARTY-NOTICES.txt` 7-Zip 부분 — 'Copyright (C) 1999-2026'(원문은 1999-2025), 원문에 없는 'Some files are in the public domain…'·'Rar* files'·요약 문장 → 원문의 파일별 라이선스 머리와 unRAR 제한을 **줄 그대로** 싣고,
  원문 연도(1999-2025)와 동봉 7z.dll(26.03)이 다르다는 판 안내를 영어·한국어로 덧붙임(원문은 고치지 않음). 표에 `licenses\7-Zip-License.txt`. csproj 에 `CopyToPublishDirectory` 를 명시(예전에도 기본값으로 발행엔 들어갔음).
  (바로잡는 파일은 원문을 읽어 줄을 잘라 붙이는 스크립트로 만들어 옮겨 적기 틀림이 없게 했고, 시험이 원문과 글자 단위로 대조)
  **시험**: 새 `Contract/InternetMarkTests.cs`(37 — Theory 줄 포함: 해제 길 13가지, 범위·설정·수정 시각·고른 항목·암호·손상·휴지통·우클릭 3동사·열기·Ctrl+C·끌기), `Contract/ThirdPartyLicenseTests.cs`(7 — 원문 해시, BSD 절, 안내 대조, 빌드 출력, **발행 출력**(`dotnet publish --no-build` 를 시험 임시 폴더에 — 약 3초, NuGet 없음), csproj),
  `ReuseDeviceContractTests` +1(**실제 exe** — EGG·ALZ·RAR5·ZIP·7Z·TGZ 를 표시 붙은 채 창 없이 풀어도 표시 없음), `EntryWorkspaceTests` +3(시각·읽기 전용, 큰 표식, 없는 파일), `CompressDialogXamlTests` +1(설정 칸), `SevenZipLibraryTests` 목록에 한 줄.
  기존 기대는 바꾸지 않음(`KnownGapTests` 기록은 본문 그대로 설명만 '서비스는 옮기지 않음 — 창 경로는 화면이 붙임'). 손으로 '고치면 실패하나' 확인: `OutputTarget.Commit` 의 NoteCreated 를 빼면 bz2 시험 2개 실패.
  손 점검 MANUAL_CHECKLIST 21절(새 — 13줄)·8절 한 줄. 위험·못 본 것: 진짜 오피스 '제한된 보기'·매크로 차단(업무 파일이 막히면 설정으로 끔), 수천 개 풀기 뒤 표시를 붙이는 시간(회사 DLP — 파일마다 작은 쓰기 3번),
  USB(exFAT)·NAS 는 표시를 붙일 수 없어 조용히 넘어감(로그만), '다른 이름으로 저장'·자동 풀림 exe 는 표시를 옮기지 않음(향후 작업). 계약 테스트 실패 0(전체 2394 — 통과 2394·건너뜀 0, Debug. 그 전 2345).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-15 3차 통합 점검(IJ3-01 f1ef006·IJ3-03 981a048 을 합친 뒤 — 코드·시험 커밋 5e534c1·문서 커밋·푸시, 설치·등록·배포 안 함)**: IJ3-02 는 검증에서 되돌려 `stash@{0}`(drop 안 함).
  Release 빌드(`dotnet build Iljip\Iljip.csproj -c Release`) 경고 0·오류 0, 계약 테스트 Debug·Release·`ILJIP_TEST_TZ=UTC` 세 번. 10/14 23:54·10/15 00:03 Windows 업데이트 재부팅 두 번(시스템 로그 1074) — 작업 손실 없음.
  ① **상호작용 시험 `Contract/Wave3IntegrationTests.cs`(13) — 제품 동작은 모두 처음부터 통과(어긋남 없음)**: 같은 이름 [이름 바꾸기](W2-05)로 비켜 푼 'a (1).txt' 에만 인터넷 표시(IJ3-03) — ZIP·7z.dll·EGG 길,
  표시가 있는 압축에 파일을 넣어 고쳐도(W2-06 — `File.Replace`) 압축의 표시가 남고 다시 풀면 넣은 파일까지 붙음(ZIP·7Z), 받은 압축 속 파일을 열어(IJ3-01) 고쳐 다시 넣어도 묻기 한 번·압축 표시 그대로,
  나눠 만든 조각 묶음(W2-03 — ZIP .z01…·7Z .001…)·자동 풀림 exe(W2-10 일집 스텁·7-Zip SFX)도 창에서 풀면 붙음.
  **오류 문구 전수(소스)**: 본체 전체 .cs 의 화면 호출(ShowError·ShowWarning·ShowInfo·StatusText·Choose·ShowProgress·MessageBox)·던지는 예외와 XAML 글자에 영어만으로 된 문구가 없음 —
  예외는 'Unsupported archive type'(재사용장치 약속)·로그 전용 예외 하나·정규식 조각 하나(허용 목록에 이유). 일부러 영어 문구 둘을 심으면 실패하는 것도 확인(심은 파일은 지움).
  ② **재배포 뒤 진짜 egg 지켜보기 준비**(오케스트레이터 요청 — 진짜 egg 견본이 없음): 재사용장치 수동 모드의 화면 로그는 파일로 남지 않아(`재사용장치_processor.py` `log` = 화면 글상자, 자동 모드만 `System_code\logs\batch_*.log` —
  마지막 파일 6/19) 반디집으로 넘어간 첨부를 나중에 찾을 길이 없었다 → `App.RunHeadlessExtractAsync` 의 실패 로그 머리를 `Headless extract failed: <압축 경로>`(`App.HeadlessFailureLogHead`)로 —
  표준 오류·종료 코드는 그대로. `Logger.Write` 가 공유·잠금 위반이면 5번까지 다시 씀(다른 일집 프로세스와 겹쳐 기록을 말없이 잃던 것). 시험 `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_압축_경로를_남긴다`
  (실제 exe — 끝이 잘린 egg, 한글 경로, 로그 파일은 exe 의 OS 시간대 날짜로 찾음. 머리를 예전 것으로 되돌리면 실패하는 것 확인). 지켜보기 절차는 MANUAL_CHECKLIST 8절 끝.
  ③ **셸 확장 설치 볼륨**: 이 PC 는 `Get-AppxDefaultVolume` = `D:\WindowsApps`(시스템 볼륨 아님), AppXDeploymentServer 로그에 OneDrive 앱 패키지 0x80070032('시스템 볼륨에 설치되어 있지 않아…') 실패가 10/11·10/13·10/15 —
  `packaging\Install.ps1`·`Rebuild.ps1` 에 `-SystemVolume`(C: 앱 저장소에 `Add-AppxPackage -Volume`)을 더하고, 안 주고 0x80070032 면 다시 할 명령을 쉬운 말로. 기본 동작은 그대로, PC 전체 설정(`Set-AppxDefaultVolume`)은 건드리지 않음.
  설치는 하지 않음(구문 검사·없는 MSIX 로 매개변수 연결만 확인, 시험 `Wave3IntegrationTests.셸_확장_설치는_…` — BOM 유지 포함).
  ④ **시험 안정성 하나**: 첫 UTC 전체 실행에서 `ThirdPartyLicenseTests.발행_출력에도_…` 가 'MSB3491 … obj\…\PublishOutputs.*.txt — 다른 프로세스가 사용 중'으로 한 번 실패(같은 시험이 Debug·Release 는 통과 — 회사 PC 보안 프로그램이
  막 쓴 파일을 검사하는 순간으로 봄) → 그 오류일 때만 1초 뒤 두 번까지 다시 발행(판정은 그대로).
  확인만 한 것(고칠 것 없음): 창 없이 풀기 분기는 `TempJanitor` 보다 앞(재사용장치 쪽은 청소 안 함), 여러 개·우클릭 풀기의 `BatchExtractor` 는 한 곳에서만 만들어지고 `KeepInternetMark` 를 받음,
  열기는 표식을 붙인 **뒤** 처음 내용(SHA-256)을 재고 감시를 시작(표식 붙이기를 '고침'으로 보지 않음), 새 기능의 오류 창은 모두 `ErrorMessages.ToUserMessage` 를 거침, 검사→고치기 흐름(W2-08·IJ3-02)은 main 에 없음(주석 둘만).
  계약 테스트: 전체 2394 → 2408(+14 — `Wave3IntegrationTests` 13·`ReuseDeviceContractTests` +1) — 통과 2408·건너뜀 0·**실패 0**(Debug·Release·UTC 세 번). 기대를 바꾸거나 약하게 한 기존 시험 없음.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-13 W2-08 — 압축 검사(I2-7)·오류 원인 한국어 구분(I2-8)·깨진 ZIP 고치기(I3-12)(보관본 기록 — 검증에서 되돌렸다가 IJ3-02 가 다시 들임, 바뀐 것은 아래 'IJ3-02'. 시험 수는 그때 기준)**:
  ① 압축 검사: 새 `Services/ArchiveTestService.cs`(`IArchiveTestService`·결과 `TestReport` — '문제없어요. 파일 128개를 확인했어요.' / '문제가 있어요. 파일 10개 중 2개에 문제가 있어요.' + '이름 — 이유').
  풀지 않고(디스크에 쓰지 않음) 항목을 끝까지 읽어 CRC·크기 확인 — ZIP·TAR·GZ·BZ2 는 풀기와 같은 준비(한글 암호 바이트·AES·CRC)로 읽기만(항목 하나가 깨져도 다음 항목 계속, `SharpCompressArchiveServiceBase.TestArchive`),
  7z.dll 형식은 `SevenZipEngine.Test`(7z.dll 시험 → 실패라고 한 항목은 솔리드가 아니면 그 항목만 다시 꺼내 길이·CRC 를 세어 LZH·ISO 의 거짓 오류를 가림 — `ExtractionVerifier.Check` 새 오버로드·`MeasuringStream`, 솔리드면 '손상된 부분이 있어요…(솔리드)' 하나),
  EGG·ALZ 는 항목마다 풀며 크기·CRC, 순수 BZ2 는 끝까지 풀어 버림, 압축된 TAR 는 바깥을 목록용 임시 파일(`%LOCALAPPDATA%\Iljip\temp\list` — 목록 보기와 같은 것)로 푼 뒤 안쪽 TAR 검사.
  암호 없음·틀림은 암호 예외(화면이 묻고 다시), 취소·환경 문제(권한·잠금·없는 파일)는 예외 그대로, 그 밖의 압축 문제는 결과로. 화면: ☰ ▸ 시험하기, 목록 우클릭 '시험하기', `--test 압축…`(메인 창 없이 — `CommandVerb.Test`, 키워드 9개),
  `ViewModels/MainViewModel.Test.cs`(새 partial). 만든 압축 시험(`CreatedArchiveTester` — W2-07 '다 만들고 시험하기'·원본 지우기 전 시험)도 이 검사기로 바꿈(문제면 새 `ArchiveTestFailedException` — 손상의 한 종류, BZ2 도 디스크에 안 씀).
  ② 오류 원인: 새 `Services/ErrorCatalog.cs`(`ProblemKind`·`UserProblem`·`Describe`·`HeadlessMessage`) — 깨짐·잘림·검사값 불일치·조각 빠짐/덜 받아짐·암호 필요/틀림·다른 프로그램이 잠금(0x80070020·21, '보안 프로그램이 검사 중일 수 있어요')·
  공간 부족(0x80070070·27)·경로가 너무 김(0x800700CE)·권한 없음(0x80070005 — '관리자 권한으로 다시 시도'는 W2-11 까지 화면이 거름)·모르는 오류('알 수 없는 문제가 생겼어요.' + 로그 안내). 영어 원문(형식·문구·HResult·안쪽 예외·호출 위치 다섯 줄)은 `Details` 에만.
  `ErrorMessages` 는 이것을 감싸는 예전 이름. 화면 오류는 새 문제 창(`Views/ProblemDialog.xaml` — 큰 글씨·문구·안내, 접힌 '자세히'·[자세한 내용 복사]·[로그 폴더 열기]·고를 일 버튼)으로:
  `IUserInteraction.ShowProblem`(덧붙임 — `WpfUserInteraction` 의 `ShowError` 도 이 창), 열기·풀기·여러 개 풀기 하나 실패·명령줄·고치기·압축 속 파일·압축 실패 자리. 창 없이 풀기(재사용장치) stderr 도 같은 한국어 한 줄(종료 코드·'Unsupported archive type' 그대로).
  화면 문구에서 영어 'CRC' 를 뺌: 검사값 불일치는 화면에서 '‘이름’의 내용이 원래와 달라요(검사값 불일치)…'(예외 문구 자체는 그대로), EGG '내용 검사값이 원래와 달라요', GZ '내용 검사값·크기가 원래와 달라요'.
  처리 못 한 화면 예외(`DispatcherUnhandledException`)도 영어 원문 대신 문제 창으로(창을 못 띄우면 한국어 메시지 상자).
  ③ 깨진 ZIP 고치기: 새 `Services/ZipRepairer.cs` — 앞에서부터 로컬 머리를 훑어(목록이 없거나 깨져도) 살릴 수 있는 항목만 '이름_복구.zip'(원본과 같은 경로 거절, 숨김 임시 파일에 다 쓰고 목록 수를 다시 읽어 본 뒤 바꿔 넣음 — 원본은 읽기만).
  데이터 서술자(bit 3, 서명 있음·없음)·ZIP64 크기, 확인은 저장·Deflate·BZip2 CRC·크기, 암호를 주면 ZipCrypto(UTF-8·CP949 두 후보)·WinZip AES(HMAC) 까지 — 모르면 그대로 옮기고 '확인 못 함'.
  원래 목록이 읽히면 그 기록(속성·시각)을 쓰고 목록에만 있는 항목도 '못 살린 파일'로. 기록 쓰기는 W2-06 의 `ZipRawRewriter.Writer`. 화면: ☰ ▸ ZIP 고치기…, 손상·잘림·검사값 불일치 ZIP 의 문제 창 [ZIP 고치기 시도] →
  저장 창(기본 '이름_복구.zip') → 암호 항목이 있으면 암호 한 번 → '128개 중 120개를 살렸어요. 못 살린 파일: …'.
  시험: 새 209개(`ArchiveTesterTests` 89·`ErrorCatalogTests` 71·`UiStringAuditTests` 23·`ZipRepairTests` 25·`MainWindowEditUiTests` 문제 창 1 — Theory 줄 포함). 기대가 바뀐 기존 시험 8개(설계 문구를 따름 — `Iljip.Tests/README.md` 'W2-08' 표:
  `LocatorTests` 4·`ExtractSettingsTests` 1·`CommandLineTests` 1·`CommandLineParserTests` 키워드 9개·`MainWindowEditUiTests` 메뉴 목록). 푼 Skip 없음. 가짜에 더한 것: `FakeUserInteraction.ShowProblem`(`Problems`·`ProblemActionAnswers`).
  계약 테스트: 전체 1956 — 통과 1955·건너뜀 1(I2-10)·**실패 0**(W2-07 의 1747 에서 새 시험 209개).
  위험·못 본 것: 문제 창의 실제 모양·복사·로그 폴더, 진짜 메일 첨부의 깨진 ZIP(반디집보다 덜 살리는지), 꽉 찬 USB·긴 경로·잠긴 파일의 실제 HResult 는 MANUAL_CHECKLIST 15절.
  솔리드 7z 손상은 어느 파일인지 가려내지 않음. 압축된 TAR 시험은 일집 임시 폴더에 바깥 압축을 한 번 풂(압축 파일 폴더에는 안 씀 — 검증 지적 수정 1차에서 gzip·bzip2 는 흘려 읽기로 바꿈, xz 만 그대로). 고친 ZIP 은 원래 목록이 없으면 폴더 속성·주석이 빠질 수 있음.
  탐색기 우클릭 '압축 파일 시험' 메뉴는 아직 없음(셸 확장 재배포 범위 — 명령줄 `--test` 만).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-08 검증 지적 수정(1차)(보관본 기록)**: 지적 10개를 검토자 시험 틀로 모두 재현 — 10개 모두 맞아 고침(자세한 표는 `Iljip.Tests/README.md` 'W2-08 검증 지적 수정(1차)').
  ① 잘린 EGG·ALZ(중대): EggDotNet 이 항목 경계·앞부분에서 잘린 것의 뒤쪽 항목을 말없이 빼서 시험 '문제없어요'·`--extract-to` 종료 코드 0(재사용장치가 파일을 잃음).
  `ArchiveCompleteness.CheckEggAlz` 가 끝 표시(EGG 보관 끝 0x08E28222·ALZ 'CLZ\1')까지 머리·블록 구조를 따라가 봄(분할 EGG 는 조각을 이어서, 마지막 조각이 잘렸으면 `VolumeIncompleteException`).
  `EggAlzArchiveService` 시험은 전체 문제('…끝부분에 있던 파일은 목록에 보이지 않아…')를 더하고, 풀기는 남은 온전한 파일을 다 푼 뒤 잘림 예외(고른 파일이 모두 목록의 파일 하나하나면 알리지 않음 — 항목 열기). 목록 보기는 예전처럼 너그럽게.
  끝 표시만 빠진 ALZ(항목은 온전)도 잘림 예외 — 경계에서 잘린 것과 겉모습이 같아 가를 수 없음(`EggAlzTests.잘린_ALZ도_…` 기대를 바꿈). ALZ 끝 표시 둘째 칸('CLZ\2') 안에서 잘린 것만 문제없음으로 봄.
  ② `ZipRepairer`: 머리 압축 크기가 커져 다음 항목을 삼킨 것을 짧게 잘라 확인(로컬 머리 크기 칸도 고쳐 씀 — `ZipRawRewriter.Writer.CopyEntryWithHeader`), 원래 목록이 있으면 목록의 머리만 진짜 항목,
  목록이 없을 때 끝이 잘린 항목의 뒤는 그 데이터(안쪽 ZIP 항목을 바깥 것으로 살리지 않음), 서명 찾기는 읽은 구간을 들고 다니는 `Scanner`(예전: 찾을 때마다 1MB), 저장 위치가 열려 있으면 '다른 프로그램이 씀',
  운영 체제 입출력 오류는 '못 살린 파일'이 아니라 예외. 암호 항목 훑기는 작업 스레드 `HasEncryptedEntriesAsync`(진행·취소 — 화면은 'ZIP 살펴보는 중...' 진행 창).
  ③ `ErrorCatalog`: 운영 체제 문구(한국어 Windows 의 '디스크 공간이 부족합니다. : …')는 설계 문구로(`IsOsMessage`), 엔진의 'Unsupported compression method: N' 은 `UnsupportedMethod`(방식 이름 — 고치기 권하지 않음),
  운영 체제가 알린 그 밖의 입출력 오류(0x8007xxxx IOException)는 `IsEnvironmentProblem`(시험 결과로 적지 않음). `TestProblem.Kind` 를 더해 시험 결과의 'ZIP 고치기 시도'는 손상·잘림·검사값 불일치일 때만(`TestReport.HasDamage`).
  ④ 시험 요약: 전체 문제가 함께 있으면 '확인한 파일 N개 중 M개', 솔리드 EGG 에서 멈추면 '…뒤의 파일 N개는 확인하지 못했어요.'.
  ⑤ 압축된 TAR 시험: gzip·bzip2 는 흘려 풀며 SharpCompress `TarReader`(되감기 버퍼에 담아 형식 알아보기)로 읽어 디스크에 쓰지 않음 — xz 만 예전처럼 일집 임시 폴더(7z.dll 이 흘려 주지 않음).
  시험: 새 31개(`ArchiveTestReviewTests`), 기대가 바뀐 기존 시험 2개(`EggAlzTests.잘린_ALZ도_…` 끝 표시만 빠진 ALZ = 잘림 예외 + 두 파일 온전, `ReuseDeviceContractTests.창없이_풀기가_실패하면_…` 경계에서 잘린 egg·alz 2경우 더함). 푼 Skip 없음.
  계약 테스트: 전체 1987 — 통과 1986·건너뜀 1(I2-10)·**실패 0**(1956 에서 새 시험 31개).
  위험·못 본 것: 진짜 알집 EGG·ALZ(솔리드·분할·암호)의 끝 표시가 합성 파일과 같은지 — 다르면 멀쩡한 파일이 '잘려 있어요'로 나옴(MANUAL_CHECKLIST 15절 검증 지적 줄, 재배포 전 꼭).
  재사용장치는 이제 경계에서 잘린 egg 를 종료 코드 1 로 받아 반디집으로 다시 풂(재배포 뒤). 솔리드 EGG 의 '확인하지 못한 파일 N개'는 합성 재료가 없어 자동 시험 없음.
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-13 W2-08 검증 지적 수정(2차)(보관본 기록 — 이 뒤 3차 검증에서 되돌려짐)**: 지적 9개를 검토자 시험 틀·git 상태로 모두 재현 — 9개 모두 맞음. 8개 고침, 5번(커밋에 빠진 파일 5개)은 위 '현재 진행 단계'(W2-09 기록)에 커밋 때 넣을 목록으로 적음
  (자세한 표는 `Iljip.Tests/README.md` 'W2-08 검증 지적 수정(2차)').
  ① `ZipRepairer` 다시 짬: 서술자는 4·8바이트 크기를 모두 맞춰 보고 로컬 머리에 ZIP64 추가 필드가 있으면 8바이트부터(SharpZipLib 기본 흘려쓰기 0/4 → 4/4). 원래 목록은 읽을 수 있는 데까지 읽고
  (`ZipDirectory.ParseCentralPrefix` — 서명이 틀린 기록에서 멈춤), 기록이 가리키는 자리에 같은 항목의 머리(이름이 같거나, 이름만 깨졌으면 방식·시각·검사값이 같은)가 있을 때만 믿음 — 그 밖의 머리도 항목으로 살펴
  끝에서 이름으로 짝지음(짝 없는 목록 파일 = '못 살린 파일'). 맞는 기록의 크기·검사값·이름도 후보(로컬 머리의 크기·이름만 깨진 것 — 머리를 고쳐 씀). 끝을 모르는 항목은 서술자 항목·이 압축의 목록 기록까지 따라가는
  사슬로 다시 훑을 곳을 찾음(사슬이 없으면 바로 뒤부터 — 안쪽 ZIP 만 예외), 크기가 작게 깨진 항목도 다음 머리까지로 다시 확인. 나눠진 ZIP(.z01…·.zip.001…)은 조각을 이어서(빠진 조각은 그 이름으로 `VolumeMissingException`,
  제안 이름은 세트 이름, 결과에 '나눠진 조각 N개를 이어서 살폈어요.' — `RepairReport.VolumeCount`). `WinZipAes.OpenEntry` 는 스트림을 받는 오버로드를 더함(이어 붙인 조각에서도 AES 확인).
  ② 목록이 깨진 ZIP 의 검사·풀기: `SharpCompressArchiveServiceBase.CheckStructure`(덧붙일 수 있는 확인 — ZIP 만 `ZipArchiveService` 가 씀)가 목록을 직접 읽어 끝 머리 항목 수·실제 머리와 맞춰 봄.
  새 `ZipListDamagedException`(손상의 한 종류 — '압축 파일 안의 파일 목록이 손상돼 일부 파일을 찾지 못했어요.'/'…맞지 않아 일부 파일을 풀지 않았어요.' — '압축 아님' 말 없음):
  검사는 '문제가 있어요'(고치기 권함), 풀기는 맞지 않는 파일은 쓰지 않고 맞는 파일을 다 푼 뒤 이 예외(창 없이 풀기 종료 코드 1). 나눠진 ZIP·목록 256MB 초과는 보지 않음.
  ③ 오류 원인: bz2 형식 확인(`HasBzip2Signature`·`OpensAsContainer`)이 환경 오류는 그대로 던짐(잠긴 .bz2 = '다른 프로그램이 씀'). `ErrorCatalog` 가 엔진이 감싼 예외의 안쪽 원인(환경·운영 체제 입출력·취소)으로 가름,
  그 밖의 운영 체제 오류는 번호 묶음별 문구(네트워크 '네트워크 위치(공유 폴더)에 연결할 수 없어요'·장치 '디스크나 USB 에서 읽거나 쓰지 못했어요'·이름 '…쓸 수 없는 글자'), 묶음 밖은 '알 수 없는 문제가 생겼어요.'(예전 계약),
  123 이면서 경로에 255자 넘는 조각 = '경로가 너무 길어요'. 우클릭·여러 개 풀기 문구(`ToPlainMessage`)도 운영 체제 오류는 '손상됐거나…'가 아님. 압축 실패는 새 `DescribeCompressFailure`·`ErrorMessages.ToCompressMessage`
  ('압축하는 중에 알 수 없는 문제가 생겼어요.' — `CompressFlow` 요약 이유·하나로 만들기 실패 문제 창 '압축하지 못했어요.').
  ④ `CompressedTarArchiveService.ListCache`: 캐시 항목을 빌려 쓰는 동안(`Lease`) 지우지 않고, 같은 열쇠는 하나만 만듦(열쇠별 잠금), 치우기도 쓰는 것은 건너뜀(.tar.xz 동시 검사 '찾을 수 없어요: …\inner.tar').
  시험: 새 49개(`ArchiveTestReview2Tests`). 기대가 바뀐 기존 시험 없음, 푼 Skip 없음. 계약 테스트: 전체 2036 — 통과 2035·건너뜀 1(I2-10)·**실패 0**(1987 에서 새 시험 49개).
  검토자 시험 틀로 다시 봄: 목록·끝 머리 바이트 357개 뒤집기 — 고치기 357번 모두 5개를 맞게(예전 317), 풀기가 말없이 파일을 잃음 0번(예전 130), 검사 '문제없어요' 158번(모두 파일이 그대로 풀리는 경우);
  로컬 기록 뒤집기 — 고친 ZIP 에 원래와 다른 항목 0번(예전 26); .tar.xz 6개 × 6번 동시 검사 — 예전 코드 36번 중 2번 실패, 지금 0번(세 번씩).
  위험·못 본 것: 새 목록 맞춰 보기가 진짜 반디집·알집·윈도 ZIP 에서 멀쩡한 파일을 손상으로 보는지(합성·픽스처 ZIP 은 모두 문제없음 — MANUAL_CHECKLIST 15절 2차 줄), 진짜 네트워크 끊김·USB 뽑기의 오류 번호,
  분할 ZIP 고치기를 반디집으로 열어 보기. 목록이 256MB 를 넘는 ZIP 은 목록 맞춰 보기를 하지 않음(예전처럼).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음(`BuildOutputTests`).
- **2026-10-14 IJ3-02 — W2-08 다시: 압축 검사(I2-7)·오류 원인 한국어 구분(I2-8)·깨진 ZIP 고치기(I3-12) + 3차 검증에서 막힌 (a)~(h)(커밋 전, 설치·배포 안 함)**:
  보관분 `iljip-wave2-W2-08-failed` 를 `git stash apply` 로 지금 main(IJ3-01 뒤) 위에 얹고 충돌은 main 의 뜻대로(W2-11 관리자 권한 질문·W2-12 셸 명령·IJ3-01 열기/끌기는 그대로 두고 문제 창·검사·고치기만 덧붙임, `SfxArchiveService` 는 3-튜플 `Detect` 에 맞춰 ZIP 자동 풀림 검사를 `_zip.TestArchive` 로,
  `EggAlzArchiveService` 고른 항목 열쇠는 IJ3-01 의 해시 `IReadOnlySet`), 3차 검증 지적 여덟 가지를 바이트 하나하나 망가뜨리는 새 시험 틀(`ArchiveTestReview3Tests`)로 모두 재현한 뒤 고쳤다:
  (a) ALZ 는 'CLZ\1' 뒤 마지막 끝 표시 'CLZ\2' 까지 봐야 온전(`AlzNameScanner.EndMarkFollows` — 4바이트씩 16칸, 그 안에 없고 뒤가 더 있으면 모르는 꼬리로 예전처럼 온전);
  (b)·(d) `ZipRepairer` 는 못 살린 항목 뒤를 머리 바로 뒤부터 다시 훑고(맞는 목록 기록이 크기를 알려 주거나 안쪽 압축일 때만 그 끝으로 — 깨진 이름·추가 필드 길이 칸이 밀어 놓은 자리로 건너뛰지 않게),
  확인할 수 없는 항목(암호 모름)은 뒤가 기록 경계일 때만 옮기고, 크기 칸이 몇 바이트만 커져 끝이 다음 머리 서명 안에 떨어진 것도 찾음(`FindChain`); (c) 목록 기록의 데이터 구간은 앞뒤가 맞을 때만 쓰고(ZIP64 칸을 못 읽은 0xFFFFFFFF 크기가 파일 끝까지 가리던 것),
  같은 이름 둘은 로컬 이름으로 되돌리고(`ResolveDuplicateNames`), 목록 이름이 폴더 모양(끝 '/')에서 로컬 머리와 어긋나면 로컬 이름(`ChooseName` — 폴더를 빈 파일로 쓰지 않게),
  유니코드 이름 추가 필드(0x7075)는 적힌 CRC 가 이름 바이트와 맞을 때만 씀(`UnicodePathOf` — 깨진 이름 칸 대신 옛 이름을 보여 주고 깨진 바이트로 쓰던 것),
  목록에만 있는 기록은 글자로 읽히는 이름만 '못 살린 파일', 서술자 표시(bit 3)·서술자는 실제 경계·확인한 값으로 고쳐 씀(`Salvage.Flags`·`DescriptorFor`),
  나눠진 ZIP 은 끝 머리의 조각 번호 칸이 깨져도 있는 조각·목록 시작 조각 칸이 맞으면 그 조각들로(`OpenSource`); 원래 파일 수를 모르면(끝 머리 없음·목록이 빈틈없이 읽히지 않고 찾은 수 ≠ 항목 수) '모두'라고 하지 않고
  '찾은 파일 N개를 살렸어요.' + '…찾지 못한 파일이 더 있을 수 있어요.'(`RepairReport.MayHaveMore`); (e)·(f) `ZipArchiveService.CheckStructure` — 엔진이 직접 읽은 기록보다 더 읽은 항목은 '목록과 맞지 않음'(풀지 않음),
  위치 칸이 끝 머리 뒤·파일 밖, 기록 0개인데 항목이 있다고 함, 어느 기록도 가리키지 않는 로컬 머리(맨 앞·맞는 항목 데이터·서술자 바로 뒤), 목록 크기·위치가 끝 머리와 어긋나고 기록이 모자람 = '목록에서 파일이 빠짐'(검사 문제·풀기 손상 오류);
  (g) `ErrorCatalog.LooksKorean`·`ErrorMessages.QuotedPath` — 경로는 첫 따옴표부터 마지막 따옴표까지 통째로(폴더 `'26년 견적` 의 따옴표 뒤 한글을 서비스 문구로 착각해 영어 .NET 문구가 화면·stderr 에 나가던 것), 영어 낱말 넷 이상이 이어지면 영어 문구;
  (h) 파일 안의 안쪽 끝 머리로 저장해 담은 docx·xlsx·hwpx·zip(Deflate 저장 블록 안의 것도)의 자리를 먼저 찾아 그 안의 머리는 바깥 항목으로 보지 않음(`EmbeddedRegions` — 파일 끝에 딱 맞는 끝 머리만 바깥 목록 후보).
  시험: 새 `ArchiveTestReview3Tests`(Fact 9 + Theory 5줄 — 망가뜨림 수만 가지를 나눠 동시에, CPU 를 크게 써 시간을 재는 시험 `LocatorTests.옮기기는_잠깐_잠긴_…`·`RecentFilesTests.설정_바꾸기가_잠깐_막혀도_…` 가
  함께 돌 때 한 번씩 흔들려 `DisableParallelization` 묶음 `DamageProbeCollection` 으로 따로 — 그 둘은 따로 돌리면 통과) + 실제 exe `CommandLineTests.실행파일_extract_to_따옴표와_한글이_든_경로의_오류도_한국어다`. 기대가 바뀐 기존 시험(목록 없는 ZIP 고치기 요약 '모두' → '찾은 파일',
  ALZ 끝 4바이트도 잘림)은 `Iljip.Tests/README.md` 'IJ3-02 에서 더한 것'. 계약 테스트: 전체 2649 — 통과 2649·건너뜀 0·**실패 0**(Debug, 약 11분 — IJ3-01 의 2345 에서 +304: 보관분 시험 6파일 + 새 시험, Theory 줄 포함).
  위험·못 본 것: 진짜 알집 ALZ 의 'CLZ\2' 모양(합성·EggDotNet 기준), 다른 프로그램이 만든 멀쩡한 ZIP 에 새 목록 검사가 오보를 내는지, 진짜 문제 창 모양·메일 첨부 고치기와 반디집 비교(MANUAL_CHECKLIST 21절 — 보관본 15절을 옮김),
  망가뜨림은 바이트 하나씩만(둘 이상 겹친 손상은 시험 안 함). 셸 확장 '시험하기' 메뉴는 아직(C++ 안 건드림). `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-14 IJ3-02 4차 — 4차 검증 지적 여덟 가지 수정(커밋 전, 설치·배포 안 함)**: 지적마다 망가뜨림으로 직접 재현한 뒤 고쳤다(여덟 가지 모두 재현됨 — 건너뛴 것 없음).
  (f) 흘려 쓴 ZIP 의 끝 머리 항목 수·목록 크기·위치 칸이 0 이면 검사 '문제없어요. 파일 0개'·풀기 0개 종료 0 → `ZipArchiveService.CheckStructure` 가 엔진이 0개를 읽어도 목록 앞 맨 앞의 로컬 머리를 보고
  '목록에서 파일이 빠짐'(항목 수를 모를 때는 이름·방식·암호 표시까지 맞는 머리만 — `ReadMatchingLocalHead(strictFields)`); (h) 저장해 담은 문서의 바깥 머리 손상이 안쪽 첫 바이트까지 닿거나 안쪽에서 바이트가 빠지거나 끼면
  안쪽 부품을 바깥 파일로 살리던 것 → `EmbeddedRegions` 가 안쪽 목록 기록이 가리키는 이름의 로컬 머리로 기준점을 맞추고(±16 바이트), 자리 바로 앞 머리도 안쪽 것으로(`Near`), 안쪽 끝 머리 주석 길이가 넘치면 첫 서명에서 끊음,
  기록 하나뿐이면 목록에 가까운 같은 이름 머리를 기준으로; (d) 목록 없는 ZIP 에서 꼬리가 깨진 저장 문서 뒤 파일을 잃던 것 → 안쪽 압축 뒤 파일 끝까지 이어지는 머리 사슬부터(`Undelimited`·`ResumeAfterLost`·`AfterInner`),
  안쪽 목록 기록의 길이 칸이 커져 끝이 뒤 바깥 머리를 넘으면 기록 안의 다음 목록 서명에서 이어 읽음; 길이 0 Deflate 를 '확인됨'으로 받아 멀쩡한 파일을 0바이트로 살리던 것 → 0바이트는 뒤가 기록 경계이고 안쪽 압축이 시작하지 않을 때만,
  크기·검사값이 모두 0 이면 서술자도 찾음(`Pick`·`BoundsOf`); 목록 기록의 추가 필드(AES 0x9901·유니코드 0x7075)만 깨진 기록을 그대로 옮기던 것 → 블록이 온전하고 AES 정보·유니코드 이름이 확인한 값과 같을 때만 원래 기록(`Matches`),
  목록 쪽 AES 정보로도 확인(`Variants`), AE-2 인데 CRC 칸이 0 이 아니면 그 값도 맞아야(`VerifyAes`); (I2-8) 목록·끝 머리 손상을 맞는 암호에도 '암호가 맞지 않아요'로 → 구조 검사가 문제를 찾으면 암호 오류를 손상으로
  (`SharpCompressArchiveServiceBase.PasswordOrDamage`·`PrepareChecked`), 암호 판정은 목록 AES 정보가 로컬 머리와 어긋난 항목을 빼고(`ZipPasswordProbe.LocalAesAgrees`) 목록을 항목 수 칸 대신 크기만큼 읽고 크기·위치 칸 하나만 깨졌으면 다른 칸으로;
  검사가 목록 0x7075 손상을 놓치던 것 → 목록·로컬 유니코드 이름이 다르거나 적힌 CRC 가 맞지 않으면 그 항목을 목록 손상으로(`ZipArchiveService.ExtraAgrees`); 'ZIP 고치기 시도'가 '.zip' 끝 이름에만 → `ZipRepairer.IsRepairableName`
  ('.zip.001'·'.z01' — 문제 창·`ErrorCatalog`·시험 화면·파일 고르기). 무작위 망가뜨림(보조 도구 — 결과물 아님)으로 더 찾은 것도 고침: 목록 없는 AES ZIP 의 로컬 AES 정보 손상(추가 필드 안의 AES 정보 모양을 찾거나
  암호를 알면 세기·안쪽 방식을 바꿔 맞춰 봄, 끝내 못 읽으면 풀 수 없는 항목을 옮기지 않고 '못 살린 파일'), 짧아진 유니코드 이름 블록·어긋난 추가 필드는 빼고 머리를 고쳐 씀(엔진 예외 '알 수 없는 문제'),
  목록의 압축 크기가 커져 다음 항목 안쪽 머리에 닿으면 다음 항목을 삼키던 것(`SwallowsListed`). 시험: 새 `ArchiveTestReview4Tests`(Fact 12 + Theory 7개 32줄 = 44), `ArchiveTestReview3Tests` 도우미를 `internal` 로 열고
  `CheckRepair` 에 `testOutput`(고친 ZIP 일집 검사) 더함 — 기존 시험 기대는 바꾸지 않음. 계약 테스트: 전체 2693 — 통과 2693·건너뜀 0·**실패 0**(Debug, 약 22분 — 2649 에서 +44).
  위험·못 본 것: 망가뜨림은 바이트 하나·한 구간씩(겹친 손상 안 봄), 표본 내용이 실행마다 조금씩 달라(`R3.Text` 의 문자열 해시 씨앗) 드문 모양은 실행마다 다를 수 있음, 로컬 머리의 짧은 유니코드 이름 블록이 든 원본을
  검사하면 '알 수 없는 문제'(문제로는 알리지만 손상 분류·고치기 권함은 아님 — 고치면 됨), 진짜 프로그램이 만든 AES·나눠진 ZIP 의 화면 확인은 MANUAL_CHECKLIST 21절 'IJ3-02 4차' 두 줄. `D:\Python\일집-dist`·`Iljip\bin` 그대로.
- **2026-10-14 IJ3-02 5차 — 5차 검증 지적 일곱 가지 수정(커밋 전, 설치·배포 안 함)**: 지적마다 직접 재현한 뒤 고쳤다(일곱 가지 모두 맞는 지적 — 건너뛴 것 없음).
  (c) 로컬 머리의 이름 길이 칸 한 바이트(zip_default 162^FF·AES 2858^FF)만 깨지면 깨진 길이로 읽은 이름의 끝이 우연히 '/'·'\' 여서 파일을 폴더로 세어 보고에서 빠뜨리고 '8개를 모두 살렸어요'
  → 맞는 목록 기록의 이름이 머리 뒤에 그대로 있으면 그 길이로 다시 읽어 확인(`ZipRepairer.WithCentralNameLength` — AES 확인도 읽은 데이터 시작으로: `WinZipAes.OpenEntry(…, dataStartAt)`, 목록 쪽 데이터 구간도),
  폴더인지는 고른 이름으로(`Header.DirectoryOverride`·`NameFor`), 고쳐 쓸 머리는 칸 값이 아니라 읽은 이름·추가 필드 길이로(`NeedsHeaderPatch`·`LocalHeaderFor`);
  (c) 목록 이름 바이트만 깨진 반디집 ZIP 을 깨진 이름('긋적서'·'.bi')으로 살리던 것 → 로컬·목록 유니코드 이름(0x7075)에 적힌 CRC 가 한쪽 이름 바이트와만 맞으면 그쪽(`UnicodeEvidence`) —
  0x7075 가 없는 ZIP(.NET 등)은 예전대로 목록 쪽(가를 증거가 없음); (e)/(f) 끝 머리 디스크 번호·주석 길이 칸이 깨지면 구조 검사가 꺼져 목록 기록 서명까지 깨진 ZIP 을 '문제없어요. 파일 0개'·종료 0
  → 엔진처럼 그 칸을 보지 않음(`ZipDirectory.FindEnd(lenient)`·`ZipArchiveService.CheckStructure`); 폴더·깨진 목록 기록을 파일로 세던 것 → 폴더는 고른 이름으로, 이름 칸에서 바이트가 빠지거나 낀 머리는
  0x7075 가 맞는 자리로 바로잡고(`Shifted`·`NameFromUnicode` — 목록 없는 ZIP 의 폴더 머리도), 위치·이름이 함께 깨진 목록 기록은 검사값·크기나 한두 바이트만 다른 이름으로 찾은 항목과 짝지음(`Reconcile`);
  빈 파일 바로 뒤 머리가 깨지면 빈 파일을 버리던 것 → 목록의 어느 기록이 가리키는 자리는 서명이 깨져도 기록 경계(`CentralIndex.Lists`·`IsBoundary`); 목록 없는 ZIP 에서 방식 칸이 확인할 수 없는 방식(0→1·8→9)으로
  깨진 항목을 '확인 못 함'으로 옮기던 것 → 저장(크기가 맞을 때)·Deflate 로도 맞춰 보고 맞으면 머리를 고쳐 씀(`Header.GuessMethod` — 안 맞은 것은 로그에 남기지 않음);
  (I2-8) 따옴표 없이 한글 경로·이름이 든 짧은 엔진 영어 문구가 그대로 나가던 것 → 엔진 예외 문구는 한국어로 보지 않고(`FromEngine`), `LooksKorean` 은 큰따옴표·따옴표 없는 경로('C:\'·'\\' 부터 끝)·파일 이름 같은 낱말을 빼고
  영어 문장 낱말(is·not·the·Unknown·corrupt …)이 있거나 영어 낱말이 더 많으면 영어로. 시험: 새 `ArchiveTestReview5Tests`(Fact 5 + Theory 7개 27줄 = 32) + 실제 exe
  `CommandLineTests.실행파일_extract_to_끝머리_디스크_칸과_목록이_깨진_ZIP은_1` — 고친 것을 하나씩 되돌려 이 시험들이 실패하는 것을 확인함. 기존 시험의 기대는 바꾸지 않음.
  계약 테스트: 전체 2726 — 통과 2726·건너뜀 0·**실패 0**(Debug — 2693 에서 +33).
  위험·못 본 것: 0x7075 가 없는 ZIP 에서 목록·로컬 이름 중 어느 쪽이 깨졌는지는 가를 수 없어 목록 쪽(다른 프로그램이 보여 주는 이름), 머리 바로잡기는 앞뒤 8바이트까지·0x7075 가 있을 때만,
  방식을 바꿔 맞춰 보기는 일집이 확인할 수 없는 방식(LZMA·PPMd 등)마다 Deflate 를 한 번 더 풀어 봄(7-Zip LZMA ZIP 고치기가 조금 느려질 수 있음 — MANUAL_CHECKLIST 21절 'IJ3-02 5차'),
  `LooksKorean` 은 서비스 한국어 문구에 영어 문장 낱말이 따옴표 없이 섞이면(이름 'Report of the year' 등) 쉬운 일반 문구로 바뀜(영어는 여전히 안 보임). `D:\Python\일집-dist`·`Iljip\bin` 그대로.
- **2026-10-16 I4-1 — IJ3-02 다시(4차): 압축 검사(I2-7)·오류 원인 한국어 구분(I2-8)·깨진 ZIP 고치기(I3-12) '정직한 보수 모드' + 시험 로그 격리(커밋 전, 설치·배포 안 함)**:
  보관분 `stash@{0}` `iljip-wave3-IJ3-02-failed` 를 `git stash apply` 로 main(f49c672) 위에 얹고(drop 안 함) 충돌(`App.xaml.cs` 창 없이 풀기 실패 — main 의 로그 머리 `Headless extract failed: <경로>` + 보관분의 한국어 한 줄 `ErrorCatalog.HeadlessMessage`)은 main 의 뜻대로,
  검사·오류 원인 코드는 그대로 두고 `ArchiveTestService` 의 영어 예외 문구 하나만 한국어로('압축 파일을 찾을 수 없어요.'). **고치기 핵심(`ZipRepairer`)은 2·3차의 바이트 조합별 추측을 버리고 구조를 바꿨다**:
  ② 목록을 믿는 조건(`CentralDirectoryTrust` — 끝 머리 후보의 목록이 끝 머리 바로 앞에서 정확히 끝나고, 항목 수·목록 크기·위치 칸 가운데 둘 이상이 맞고, 위치가 밀렸으면 'MZ',
  가장 앞 항목보다 앞에 머리 서명 비슷한 자리가 없고, **기록 하나가 뒤 기록들을 삼킨 모양(이름·추가 필드·주석 칸 안에 목록 서명)이 아닐 때**);
  ③ 목록 모드(`RepairListed`) — 원래 개수 N = 폴더가 아닌 목록 기록 수(이름을 못 읽는 기록 포함), 기록마다 가리키는 로컬 머리와 이름 바이트·UTF-8 표시·유니코드 이름(0x7075)이 같아야 하고
  (다르면 '이름 정보가 서로 달라요' — 어느 쪽이 맞는지 가르지 않음), 목록 값·로컬 값 두 증인 가운데 데이터 CRC(AES 는 인증값)로 확인되는 쪽으로, 데이터는 다음 맞는 항목 자리·목록 시작을 넘지 않음,
  로컬 머리를 훑지 않음(목록에 없는 머리 — 저장해 담은 문서 속 부품 — 는 보지도 세지도 않음), 크기 0 은 두 증인이 모두 0 이고 끝이 목록이 가리키는 자리에 정확히 닿을 때만;
  ④ 훑기 모드(`ScanChains`) — 파일 맨 앞(나눠진 ZIP 이면 조각 표시 뒤)이 머리일 때만 사슬을 시작, 끝을 데이터로 믿을 수 있을 때만 다음 머리로(확인됨·서술자 맞음·크기 0 + 바로 뒤 서명·확인 못 함 + 바로 뒤 서명,
  Deflate 는 압축 바이트를 남김없이 썼는지 — SharpZipLib `Inflater.TotalIn`, 암호 데이터 안에 말이 되는 머리가 있으면 크기가 뒤 항목을 삼킨 것), 멈춘 뒤 다시 찾은 머리는 읽히는 목록 기록이 그 자리·이름을 가리킬 때만,
  나머지는 '목록에 없는 조각 K개 — 믿기 어려워 넣지 않았어요.'로 수만; ⑤ 쓰기(`RepairWriter`) — 확인한 값으로 머리·목록 기록을 새로 쓰고 데이터만 옮김(ZIP64·AES·유효한 0x7075 블록만, ZipCrypto 서술자 항목은 서술자 유지,
  깨진 표시 bit 13 등은 옮기지 않음), 숨김 임시 파일 → 다시 읽어 수·이름 확인 → 바꿔 넣기; ⑥ 보고(`RepairReport`) — `CountKnown`·N·살린 것 M(검사값으로 확인한 것만)·확인 못 함 U·못 살림 L(이유)·조각 K·조각 파일 수,
  요약 'N개를 모두 살렸어요.'(목록 모드에서 모두 확인했을 때만)·'전체 N개 중 M개를 살렸어요.'·'원래 몇 개였는지 확실하지 않아요 — M개를 살렸어요.', 결과 창은 다 살렸을 때만 안내 창·그 밖은 주의 창(F2),
  고치기 한 번에 로그 한 기록(못 살린 이름 처음 50개). 검사의 항목 문제 로그는 처음 20개까지(`TestProblemLog`).
  만들며 정한 것: 맨 앞 머리의 '목록까지 암호'(bit 13)는 다른 쓰지 않는 표시가 꺼져 있을 때만 '고칠 수 없어요'(바이트가 깨져 켜진 것은 머리 손상), 검사값이 압축 바이트를 모두 덮는 항목(저장·Deflate 다 씀·AES·서술자 크기 맞음)은
  끝 뒤 머리가 깨져도 그 항목까지는 살리고 사슬은 멈춤, 파일이 서명 앞부분(1~3바이트)에서 끝나도 경계, 뒤 머리가 깨진 폴더는 넣되 '못 살린 파일'로 세지 않음, 압축·문서 이름의 크기 0 머리는 '크기 정보를 믿을 수 없어요'(0바이트로 살리지 않음),
  목록 모드에서 로컬 머리의 이름 길이 칸만 깨지면 목록의 이름 길이로 다시 읽어(이름 바이트가 같고 데이터 CRC·크기 0 증인으로 확인될 때만) 살림, '목록 이름의 끝 글자만 깨진 폴더'는 두 이름의 길이가 같고 끝 바이트만 다를 때만
  (이름 길이 칸이 커진 빈 파일이 뒤 폴더 머리 이름까지 읽혀 폴더로 빠지고 '8개를 모두 살렸어요'가 되던 거짓 성공 — 오라클이 찾음).
  **시험 로그 격리(B)**: 로그 폴더를 `AppPaths.Root\logs` 로(사용자 PC 는 예전과 같은 `%LOCALAPPDATA%\Iljip\logs`), 시험 프로세스는 `%TEMP%\IljipTests\appdata-<pid>`(끝나면 지우고 하루 넘은 남은 폴더는 다음 시험이 치움),
  시험이 띄우는 exe 는 환경 변수 `ILJIP_TEST_APPDATA`(`ModuleInit.ApplyTo` — `CommandLineTests`·`ReuseDeviceContractTests`). 10/13·10/14 로그(85MB·255MB — IJ3-02 손상 훑기 시험 기록)는 `D:\Python\일집-build\시험로그-보관\` 로 옮김.
  시험: 고치기 시험은 조합별 기대 대신 판정 오라클 `TestSupport/RepairOracle.cs`(불변식 — README 'I4-1 에서 더한 것')로 바꾸고, 보관분의 '추측으로 살린' 기대(목록이 깨져도 로컬 머리로 모두·로컬 이름이 깨지면 목록 이름으로·
  끝을 모르는 항목 뒤를 다시 찾아 살림·머리 크기·방식·폴더 이름을 짐작해 고쳐 씀)를 정직한 기대로 바꿈(목록 모드: 그 기록의 파일만 이유와 함께 못 살림, 훑기 모드: 깨진 항목 앞은 꼭 살리고 뒤는 조각 수).
  새 `ZipRepairHonestTests`(11). 오라클이 찾은 거짓 성공 둘(목록 첫 기록의 추가 필드·주석 길이 칸 +256 이 뒤 기록 넷을 삼키고도 끝 머리에 정확히 닿아 '1개를 모두 살렸어요', 빈 파일 머리의 이름 길이 칸이 커져 폴더로 빠짐 — 위)을 고침.
  **검토(망가뜨림 시험)에서 찾아 고친 것** — 시험 도구(저장소 밖)가 표본 ZIP(.NET·SharpZipLib·흘려 쓰기·ZipCrypto·AES·ZIP64·반디집 픽스처 등 22개)마다 바이트 뒤집기·칸 0/FF·자르기·끼우기를 수천 가지 만들어
  고치기는 `RepairOracle`, 검사는 '실제로 원래대로 풀리는가'와 맞춰 봄. 고치기: ① 로컬 머리의 '필요한 판' 칸이 깨지면 그 값을 옮겨 SharpZipLib 가 '풀 수 없는 판'으로 거절(→ 방식·AES·ZIP64 로 새로 정함 `NeededVersion`),
  ② ZipCrypto 서술자 항목(암호 확인 바이트가 시각)의 로컬 머리 bit 3 만 깨지면 bit 3 을 지워 다른 프로그램이 '암호가 틀림'(→ 확인 바이트가 시각에서 왔으면 bit 3·서술자를 남기고, 로컬 시각이 깨졌으면 목록 시각으로도 확인),
  ③ 암호 항목의 크기 0 을 빈 파일로 살림(→ 암호 항목은 크기 0 으로 살리지 않음), ④ 서술자 항목의 로컬 머리 CRC·크기를 0 으로 남겨 일집 엔진이 못 읽음(→ 머리에도 확인한 값 — 고친 흘려 쓴 ZipCrypto ZIP 은 이제 일집 검사도 통과),
  ⑤ Deflate 마지막 블록 표시만 깨지면 .NET 풀기가 그냥 끝내 '살렸어요'인데 다른 프로그램은 'Unexpected EOF'(→ SharpZipLib `Inflater.IsFinished` 까지 확인), ⑥ UTF-8 이름인데 UTF-8 표시(bit 11)가 꺼진 머리를 그대로 써 다른 프로그램에
  깨진 이름(→ 올바른 UTF-8 이고 0x7075 가 없으면 표시를 켬 `NameFlag`), ⑦ 0x7075 의 CRC 는 이름 바이트만 덮어 그 속 UTF-8 글자가 깨져도('견적서'→'견적쐜') 깨진 이름으로 '살렸어요'(→ 이름 바이트를 만든 코드 페이지로 다시 적어
  맞을 때만 씀 `SpelledAsName`, 목록 모드는 보고 이름과 같은 목록 쪽 0x7075), ⑦-2 목록 기록 외부 속성의 '볼륨 이름'(0x08) 표시가 켜진 채 옮겨 SharpZipLib 가 그 파일을 건너뜀(→ 그 칸은 기본값으로). 검사·풀기: ⑧ 엔진이 읽는 값(서술자 표시가 없으면 로컬 CRC·크기, 있으면 목록 값)이 0 으로 깨져 '문제없어요'·0바이트 풀기(→ 구조 검사가
  그 값이 목록과 맞는지·서술자가 그 자리에 있는지 봄 `EngineValuesAgree`), ⑨ 목록과 로컬 머리의 방식·암호·강한 암호 표시가 다른데 '문제없어요'(→ 그 항목 손상, 목록에만 암호 표시가 있으면 암호를 묻지 않음),
  ⑩ 한 파일의 암호 머리만 깨지면 맞는 암호에도 '암호가 맞지 않아요'가 되풀이(→ 나머지 항목의 확인값이 여러 번 비교를 보정하고도 16비트 이상이면 암호를 받고 그 파일만 손상, 모자라면 '(파일이 손상된 경우에도 이렇게 보일 수 있어요)' 덧말),
  ⑪ 목록 기록 하나의 AES·강한 암호 표시·크기 칸이 깨지면 판정을 통째로 못 해 한글 암호를 글자로 넘김(→ 그 기록만 빼고 판정), 판정 못 한 AES 를 엔진(인증값을 보지 않음)이 틀린 내용으로 '문제없어요'(→ 일집 AES 로 읽고 인증값 확인),
  ⑫ 암호를 확인한 뒤 엔진이 '암호 틀림'이라 하면 손상으로, 목록 첫 서명이 한 바이트 밀리거나 목록 뒤에 쓰레기가 붙어도 판정, 끝 머리의 디스크 번호 칸만 깨지거나 주석 길이 칸이 파일 끝을 넘어도 판정(예전엔 나눔 ZIP 으로 여기거나 주석을 읽다 그만두고
  한글 암호를 UTF-8 로 넘겨 맞는 암호에 '암호가 맞지 않아요' — 나눔 세트는 이어 붙인 스트림이 한 디스크로 고쳐 주므로 그 칸은 보지 않음), 목록에만 암호 표시가 있는 항목은 판정에서 뺌(→ 암호 없는 ZIP 에 암호를 묻지 않음),
  ⑬ 깨진 ZIP64 크기로 엔진이 파일의 음수 자리를 찾다 운영 체제가 '매개 변수가 틀립니다'로 거절하면 사용자 PC 문제로 보아 검사가 '알 수 없는 문제가 생겼어요'로 멈춤(→ 엔진이 부른 Seek 의 그 거절은 손상 `EngineErrorTranslator.IsBadSeek`).
  새 시험 `ZipRepairHonestTests` +18 줄(판·서술자 표시·볼륨 속성, 엔진 CRC·크기 0 두 가지, 한 파일 암호 머리, 방식·암호 표시 두 가지, Deflate 끝, UTF-8 표시, 0x7075 글자 네 가지, 끝 머리 디스크·주석 칸 세 가지, 엔진의 엉뚱한 자리 찾기) — 모두 29.
  기존 시험의 기대는 바꾸지 않음.
  계약 테스트: 전체 2821 — 통과 2821·건너뜀 0·**실패 0**(Debug, 54분 — 망가뜨림 시험과 함께 돌림. `ArchiveTestReview3Tests.잘린_EGG_ALZ는_어디서_잘라도_…` 하나가 혼자 11분쯤 — 멈춘 것 아님, `--blame-hang` 8분 감시에 걸릴 수 있음).
  위험·못 본 것: 목록이 없는 ZIP 에서 가운데 머리가 깨지면 그 뒤는 (목록 조각이 가리키지 않는 한) 살리지 않음 — IJ3-02 보다 덜 살리는 경우가 있음(대신 틀린 개수·이름·0바이트 없음, 반디집 비교는 MANUAL_CHECKLIST 23절),
  흘려 쓴 항목의 목록 CRC 만 깨지면(목록 모드) 서술자를 세 번째 증인으로 쓰지 않아 그 파일을 못 살림,
  **일집 풀기 엔진이 흘려 쓴 ZipCrypto(서술자) ZIP 을 원본부터 맞는 암호로 풀지 못함**(I4-1 이 바꾼 것 아님 — 고친 ZIP 은 머리에 확인한 값을 적어 일집·SharpZipLib 모두 풂, '향후 작업'), 진짜 문제 창·결과 창 모양·메일 첨부 비교는 MANUAL_CHECKLIST 23절.
  검토에서 정한 것: Deflate 마지막 블록 표시만 깨진 파일(내용·CRC 는 맞음)은 고치기가 '내용이 원래와 달라요'로 못 살림(다시 압축해 살리지 않음 — 원본은 그대로)·검사는 일집이 원래대로 풀므로 '문제없어요'(다른 프로그램은 못 열 수 있음),
  0x7075 이름은 949·932·936·950·437·850·866·874·1250~1258 코드 페이지나 UTF-8 로 이름 바이트와 맞을 때만 씀(그 밖의 코드 페이지로 만든 이름은 이름 바이트로 보임 — 내용은 그대로), 망가뜨림 시험 도구는 저장소에 넣지 않음(계약 시험은 찾은 경우마다 한 줄씩).
  `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-16 I4-1 검증 지적 수정(1차) — 남은 거짓 성공 넷 + 작은 지적(커밋 전, 설치·배포 안 함)**:
  ① 검사·풀기(`SharpCompressArchiveServiceBase`): 목록 이름(또는 엔진이 쓰는 0x7075 이름)의 끝 바이트가 '/' 로 깨지거나 마지막 기록의 이름 길이 칸이 줄어 이름이 '폴더/' 에서 끝나면
  엔진이 그 파일을 폴더로 보고, 세 루프(검사·풀기·고친 압축 시험)가 구조 검사 결과(`BadEntries`)를 보기 전에 폴더를 건너뛰어 '문제없어요. 파일 N-1개'·종료 코드 0·빈 폴더('견적서.tx')였다
  → 목록과 맞지 않는 항목은 폴더로 보여도 파일 하나로 세고 문제로(검사 '파일 N개 중 1개에 문제'), 풀기는 그 이름으로 폴더를 만들지 않고 맞는 파일을 다 푼 뒤 손상(종료 코드 1).
  폴더 기록의 유니코드 이름이 '/…' 로 깨진 것도 '안전하지 않은 경로' 대신 손상으로. ② ZIPX(7z.dll 길 — `SevenZipEngine.Source.ListCheck`·`SevenZipFormatsArchiveService.ZipListCheck`):
  ZIP 과 같은 목록·로컬 머리 대조(`ZipArchiveService.CheckZipStructure` — 스트림을 받는 정적 본체로 뺌)를 하고, 7z.dll 항목 순번은 문제없는 기록의 크기·CRC 가 모두 맞을 때만 목록 차례로 씀
  (못 맞추면 모든 항목을 문제로 — 풀지 않음). 검사는 그 항목을 문제로, 풀기는 그 항목을 빼고 푼 뒤 손상. 예전: 목록 이름이 NUL 로 잘리거나 0x7075 글자가 깨져도 '문제없어요'·종료 코드 0(파일 빠짐·'…tx�').
  ③ 고치기 목록 모드(`RepairListed`): 빈 기록의 목록·로컬 이름이 어긋나 폴더인지 빈 파일인지 이름으로 가를 수 없으면 목록 기록의 속성 칸으로 가름 — 폴더 속성이면 폴더(쓰지 않고 세지도 않음),
  파일 속성이면 '못 살린 파일(이름 정보가 서로 달라요)', 속성이 없으면(.NET·탐색기 압축) 끝 글자만 다를 때 '못 살린 파일'(cut 8 — 하나 더 셀 수 있음). 예전: 세지 않아 'N-1개를 모두 살렸어요'.
  ④ 고치기 훑기 모드(`ScanItem`): 이름이 깨졌거나(이름 길이 칸이 깨졌을 수 있음) 추가 필드가 블록으로 나뉘지 않으면(추가 필드 길이 칸) 데이터 시작 자리를 믿지 않는다 —
  데이터 1바이트 이상을 검사값으로 확인한 항목만 그 끝을 믿고, 데이터가 없는 폴더·빈 파일·확인 못 한 항목은 '못 살린 파일'로 적고 사슬을 멈춤(다시 훑기는 그 머리 서명 바로 뒤부터 — 깨진 이름 속 머리도 조각 수로).
  예전: 빈 파일 머리의 이름 길이 칸이 커져 데이터 시작이 뒤 저장 docx 속 첫 부품 머리에 닿자 '크기 0 + 바로 뒤 서명'으로 끝을 믿고 'word/document.xml' 을 바깥 파일로 '살렸어요'.
  작은 지적: 고친 ZIP 에 암호 없는 항목의 강한 암호 표시(bit 6)를 옮기지 않음(`KeptBits` — Python zipfile 이 거절하던 것), 나눔 ZIP 의 .z01 을 골라 시험하면 열기처럼 .zip 으로(`ArchiveVolumes.OpenTargetFor`),
  같은 암호로 다른 파일이 확인됐는데 한 파일만 암호 확인값이 안 맞으면 이유에 '…이 파일이 손상됐을 수 있어요', 깨진 이름의 제어 문자·이진 바이트는 결과 글에서 '?' 로 바꾸고 줄임(`ErrorMessages.DisplayName`),
  git 제외 픽스처 sfx_default 를 요구하던 시험 두 줄을 `FixtureFact` 로(G2 — `ArchiveTesterTests`·`Wave3IntegrationTests`), 시험 자료 폴더 `appdata-<pid>` 는 10분 넘고 pid 가 없으면 다음 시험이 치움(예전 하루).
  건너뛴 지적(기준상 minor·다른 갈래): 훑기 모드에서 bit 0·방식 칸이 깨진 항목을 '확인 못 함'으로 옮기는 것(cut 5 — 살린 수에 안 넣음), 흘려 쓴 ZipCrypto 정상 ZIP 풀기 실패(main 과 같음 — `fix-zipcrypto-descriptor` 갈래),
  손상 ZIP 풀기가 main 보다 엄격해진 것(정직한 거절 — 거짓 종료 0 을 훨씬 많이 막음).
  새 시험 `ArchiveTestReview6Tests` 18줄 — 계약 테스트 전체 2839·실패 0(Debug, 53분). 기존 시험의 기대는 바꾸지 않음(처음 고친 판이 폴더 머리가 깨진 반디집 ZIP 에서 폴더를
  '못 살린 파일'로 세어 `ArchiveTestReview5Tests` 둘이 실패 → 폴더 속성으로 가르게 고쳐 통과).
- **2026-10-17 I4-1 검증 지적 수정(2차) — 남은 거짓 성공·원인 틀림 다섯 + G2(커밋 전, 설치·배포 안 함)**:
  ① 고치기(`ZipRepairer`): 폴더 기록을 빈 Deflate 흐름(방식 8·압축 크기 2·원래 크기 0)으로 쓰는 ZIP(Java ZipOutputStream·SharpZipLib·Python ZIP_DEFLATED — 업무 시스템 일괄 내려받기에 흔함)에서
  폴더를 파일로 세어 '7개를 모두 살렸어요'(파일 6개)·폴더만 든 ZIP 에 '2개를 모두 살렸어요' → 목록 모드는 '이름 끝 '/'·'\' + CRC·원래 크기 0'이면 압축 크기와 상관없이 폴더(`Listed.NoContent`),
  목록 값으로는 폴더가 아니던 '…/' 기록을 살렸는데 확인한 내용이 비면 폴더로 바꾸고 원래 수에서 뺌·내용이 있으면 '못 살린 파일(이름이 깨졌어요)'(`KeepFolderNamed`),
  훑기 모드는 그런 폴더를 '이름이 깨졌어요'로 세지 않고(`ScanName`) 데이터를 확인한 뒤 폴더로 넣음(`KeepScanned`). ② 같은 자리: 목록 없는 서술자 ZIP 에서 이름 끝이 '/'·'\' 로 깨진 파일
  (머리 칸이 0 이라 이름 검사를 지나옴)을 '살렸어요'에 넣었는데 고친 ZIP 에서는 폴더로 보여 내용이 사라짐 → 서술자로 확인한 내용이 있으면 '못 살린 파일(이름이 깨졌어요)' — 끝은 데이터로 확인됐으니 사슬은 이어 감.
  ③ 맥 NFD 이름이 든 반디집 기본 ZIP(이름 바이트는 CP949 호환 낱자 'ㅁㅐㄱ', 0x7075 에 NFD UTF-8)을 고치면 0x7075 를 버리고 'ㅁㅐㄱ_ㅅㅐㅁㅍㅡㄹ/…' 로 '모두 살렸어요'
  → `SpelledAsName` 이 조합형 낱자(U+1100~)를 호환 낱자(U+3131~)로 바꿔 이름 바이트와 맞춰 봄(`CompatibilityJamo` — 현대 낱자 67자, 유니코드 이름으로 대조함).
  ④ 검사·풀기(`ZipArchiveService.CheckZipStructure`): 0 으로만 채운 .zip(1,000바이트 이상 — 엔진이 빈 tar 로 보고 0개)을 '문제없어요. 파일 0개'·창 없이 풀기 종료 코드 0
  → 끝 머리 서명이 아예 없는데 엔진이 0개면 손상(`CorruptArchiveException` — '압축 파일이 손상됐어요. 다시 받아 보세요.', 종료 코드 1, 고치기 권함). 끝 머리만 있는 진짜 빈 ZIP 은 그대로 '문제없어요. 파일 0개'.
  ⑤ 나눔 판정(`SpannedZipStream.DiskCount`): 보통 ZIP 의 끝 머리 '이 디스크 번호'(+4·+5) 1바이트가 깨지면 "'a.z01' 조각이 없어요"(고치기도 안 권함) → 목록 시작 디스크가 0 이고 이 파일 안에서
  목록 위치부터 끝 머리 바로 앞까지 기록이 빈틈없이 읽히며 기록이 모두 디스크 0 을 가리키면 보통 ZIP(`SelfContained` — 그 칸만 깨진 것, 엔진은 그 칸을 보지 않음). 그래서 '이 디스크 번호를 크게'(0x7777 —
  `ArchiveTestReview5Tests`)와 같게 검사 '문제없어요'·모두 풀림. 진짜 나눔 세트(목록이 마지막 조각에 있음·앞 조각에서 시작)는 그대로 조각 수·'조각이 없어요'.
  ⑥ G2·G3: `ArchiveTesterTests.정상_압축은_문제없고_파일_수가_맞다` 의 픽스처 목록을 커밋되는 픽스처(`commit_ok`)로 — git 제외 sfx_default 는 새 `자동풀림_exe도_문제없고_파일_수가_맞다`(`FixtureFact`, 없으면 건너뜀)가
  같은 계약을 봄(예전: 새 복제본에서 이 줄이 FileNotFound 로 실패).
  건너뛴 지적(기준상 minor — 솔직한 한계·문구): 훑기 모드에서 bit 0·방식 칸(8→9)이 깨진 항목을 '확인 못 함'으로 옮겨 고친 ZIP 풀기가 암호·검사값으로 멈추는 것(cut 5 — 살린 수에 안 넣고 알림),
  목록 이름 첫 바이트 '/'·'\' 의 풀기 문구('위험한 경로'), 0x7075 블록 크기 0 이 '알 수 없는 문제'(영어 원문은 안 나옴), 못 살린 이름의 이진 조각 표시, .zip.002 를 골라 검사하면 지원 안 함,
  받다 만 ZIP 의 문구가 '목록이 손상돼…', `%TEMP%\IljipTests` 의 옛 TempDir 폴더(시험 환경 — 그 전부터).
  새 시험 `Iljip.Tests/Contract/ArchiveTestReview7Tests.cs` 23줄(새 파일 — 아직 git 에 없음, README 'I4-1 검증 지적 수정(2차)') — 계약 테스트 전체 2862·실패 0(Debug, 28분). 기존 시험의 기대는 바꾸지 않음.
- **2026-10-18 4차 통합 점검 — 시험 로그 격리(커밋 bea2cd6·푸시, 설치·배포 안 함)**: 4차 묶음 I4-1 은 3차 검증에서 되돌려 `stash@{0}` `iljip-wave4-I4-1-failed`(위 '현재 진행 단계') — main 에 들어온 제품 기능은 없다.
  ① **고친 것 — 시험이 사용자 진짜 로그에 쓰던 것**: 계약 테스트(그리고 시험이 띄운 Iljip.exe)가 `%LOCALAPPDATA%\Iljip\logs` 에 시험 기록을 쌓아(3차까지 전체 시험 한 번에 수백 KB, IJ3-02 시험은 하루 85MB·255MB —
  10/18 에도 I4-1 을 되돌린 뒤 돈 main 시험이 04:10~04:24 에 380KB) 재배포 뒤 '진짜 egg 지켜보기' 로그를 흐리던 것. I4-1 보관분의 B 몫을 **같은 글자 그대로** 들였다(보관분을 다시 얹을 때 겹치지 않게 —
  `git merge-tree --merge-base f49c672 HEAD stash@{0}` 로 코드 파일 충돌 없음 확인 — 얹은 결과는 보관분 그대로 + 이번 새 시험 파일 하나):
  `AppPaths.Root` 찾는 순서 = 시험 흐름(AsyncLocal) → 시험 프로세스 전체 → 환경 변수 `ILJIP_TEST_APPDATA`(비지 않은 절대 경로일 때만) → `DefaultRoot()`(`%LOCALAPPDATA%\Iljip`, 없으면 `%TEMP%\Iljip`),
  `Logger.LogDirectory` = `AppPaths.Root\logs`(사용자 PC 에서는 예전과 같은 폴더), 시험 `ModuleInit` 이 시험 자료 폴더를 끝날 때 지우고 앞선 실행이 남긴 10분 넘은 `appdata-<pid>`(그 pid 의 시험이 돌지 않을 때만)를 치움,
  시험이 Iljip.exe 를 띄우는 두 곳(`CommandLineTests.RunIljipCapture`·`ReuseDeviceContractTests.RunIljip`)에 `ModuleInit.ApplyTo`. `ReuseDeviceContractTests.창없이_풀기가_실패하면_오류_로그에_…` 는
  시험 자료 폴더의 logs 에서 같은 `Headless extract failed: <경로>` 줄을 찾고, 사용자 PC 의 폴더(`DefaultRoot`)가 `%LOCALAPPDATA%\Iljip` 그대로임을 함께 확인(기대는 더 엄격 — 예전엔 사용자 로그를 읽기만 함).
  새 시험 `Wave4IntegrationTests` 3개: 시험의 자료·로그 폴더가 사용자 폴더가 아니고 실제로 쓴 줄이 시험 폴더에 남음, `ApplyTo` 가 환경 변수를 넘김, 시험 소스에서 Iljip.exe 를 띄우는 곳이 모두 `ApplyTo` 를 거침.
  ② **교차 확인**(고칠 것 없음): 실제 Release exe(`--extract-to` — 자식마다 120초 제한, 실패 로그는 `ILJIP_TEST_APPDATA` 로 스크래치 폴더에) 22가지 — 인터넷 표시를 붙인 반디집 ZIP·7Z·TGZ·CP949 ZIP·맥 NFD ZIP·
  나눠진 ZIP(.zip 을 골라)은 종료 0·내용이 매니페스트 SHA-256 그대로·풀린 파일에 표시 없음(ZIP 하나는 환경 변수 없이 — 성공은 로그를 쓰지 않음), 받다 만 ZIP·7Z, 0 으로 채운 EGG·ALZ·7Z·RAR(4,096·300바이트 —
  재사용장치가 일집에 넘기는 확장자), 암호 ZIP, CRC 손상 ZIP 은 종료 1·한 줄·반쪽 파일 없음('압축 파일이 손상됐어요. 다시 받아 보세요.' 등), 내용이 압축이 아닌 `.xyz` 는 종료 2 'Unsupported archive type: .xyz',
  실패 로그 `Headless extract failed: <경로>` 는 환경 변수 폴더에, 사용자 로그 네 파일 그대로. 예전부터 있던 것 둘(I4-1 이 고쳤지만 stash — 회귀 아님): 0 으로만 채운 4KB `.zip` 은 종료 0·파일 0개
  (재사용장치는 `.zip` 을 넘기지 않음 — '향후 작업'), 받다 만 `.zip` 의 메인 창 오류·창 없이 풀기 stderr 는 '알 수 없는 오류가 났어요: Failed to locate the Zip Header'(우클릭·여러 개 풀기 창은 쉬운 한국어 —
  `ExtractSettingsTests.모르는_엔진_오류는_쉬운_한국어로` 가 지금 동작으로 고정, 고친 분류는 I4-1 보관분의 `ErrorCatalog`). Release 발행(`--no-build`, 스크래치 폴더) 46개·맨 위 11개·`x64\7z.dll` 그대로.
  검사→고치기 흐름은 main 에 없음(I4-1 stash). 끌어 꺼내기·바로 열기·Ctrl+C·미리 보기·설정의 폴더(`AppPaths.Root` 아래)는 사용자 PC 에서 같은 곳이고, 시험에서는 예전부터 시험 폴더(기존 시험 그대로 통과).
  로그 폴더를 직접 읽는 본체 코드는 `Logger` 하나뿐(소스 확인).
  계약 테스트: 전체 2408 → 2411(+3 `Wave4IntegrationTests`) — 통과 2411·건너뜀 0·**실패 0**(Debug·Release·UTC 세 번, 걸린 시간 Debug 7분 46초·Release 6분 50초·UTC 6분 23초). 기대를 바꾸거나 약하게 한 기존 시험 없음.
  세 번 돌리는 동안·실제 exe 확인 뒤에도 `%LOCALAPPDATA%\Iljip\logs` 네 파일(10/12·10/15·10/16·10/18) 크기·시각 그대로. `D:\Python\일집-dist` 는 건드리지 않았고 `Iljip\bin\Release·Debug` 에 exe 없음.
- **2026-10-18 I4-1 들임(오케스트레이터 — 보관분 `iljip-wave4-I4-1-failed` 를 4차 통합 점검 73d7fbc 위에, 설치·배포 안 함)**: 4차 3차 검증에서 제품 동작 수락 기준(C0~F3·G2~G4)은 모두 통과했고
  되돌린 까닭은 이 문서의 '새 파일' 목록·보고의 바뀐 파일 목록 누락(문서·보고)뿐이라, 보관분을 `git stash apply` 로 얹었다(코드 충돌 없음 — 시험 로그 격리 몫은 4차 통합 점검이 같은 글자로 들였음.
  문서 셋 `HANDOFF.md`·`Iljip.Tests/README.md`·`Iljip.Tests/MANUAL_CHECKLIST.md` 충돌만 손으로 합침 — 보관분의 손 점검 23절은 24절로). '현재 진행 단계'에 새 파일 19개 목록을 넣었다.
  `ZipRepairHonestTests.흘려_쓴_ZipCrypto_ZIP도_…` 의 설명(‘일집 풀기 엔진은 이 모양을 원본부터 풀지 못한다’)은 아래 '흘려 쓴 암호 ZIP' 고침 뒤로 맞지 않아 고쳐 씀(시험 내용·기대는 그대로).
  3차 검증이 남긴 minor(고치지 않음): 목록 모드에서 폴더 기록의 CRC·크기 칸이 깨지면 그 폴더를 '못 살린 파일'로 세어 원래 개수가 1 큼, 훑기 모드에서 로컬 이름 한 바이트가 글자로 읽히게 깨지면 틀린 이름으로 살림('?' 같은 Windows 금지 글자도),
  목록을 못 믿을 때 첫 암호 항목의 확인 바이트·salt 가 깨지면 맞는 암호에도 다시 묻기, 나눠진 ZIP 의 .z01 을 창 없이 풀기에 주면 '손상'으로 종료 1(검사는 '문제없어요'), 큰 항목 하나를 확인하는 동안 고치기 진행 표시가 멈춤(취소는 됨),
  '확인 못 함'만 있을 때 요약 문구·깨진 이름 표시. (정상인 흘려 쓴 ZipCrypto ZIP 을 검사가 '손상'으로 보던 것은 아래 '흘려 쓴 암호 ZIP' 이 고침.)
- **2026-10-16 흘려 쓴 암호 ZIP 풀기(I4-1 남김 — 별도 세션이 main 기준으로 고치고 I4-1 사본 위에서도 확인, 2026-10-18 오케스트레이터가 I4-1 위에 얹음, 설치·배포 안 함)**:
  SharpZipLib 를 되감지 못하는 스트림에 암호로 쓴 ZIP(Java·서버가 흘려 쓴 모양 — ZipCrypto·AES + 데이터 서술자 bit 3, 로컬 머리 크기 칸 0·ZIP64 추가 필드도 0)을 온전한 원본인데도
  '파일이 손상됐거나 끝까지 받아지지 않은 것 같아요'(안쪽 'Cannot determine compressed stream type')로 못 풀던 것 — **main(f49c672)에서도 같았음**(별도 worktree 에서 재현).
  원인: SharpCompress 0.50.5 `ZipHeaderFactory.LoadHeader` 가 '암호 + bit 3 + 로컬 압축 크기 0' 이면 목록으로 여는(되감을 수 있는) 길에서도 NotSupportedException
  ('…non-seekable Zip Streams with encrypted data…' — 크기·CRC 는 바로 뒤에서 목록 값으로 덮어쓰는데도). 첫 항목이 그 모양이면 `ArchiveFactory.IsArchive` 부터 false →
  `OpenTyped` 가 `ArchiveFactory.OpenArchive` 로 넘겨 그 문구. 일집 쪽(`ZipArchiveService`·`ZipPasswordProbe`)은 원인 아님 — 판정은 UTF-8·확인됨으로 맞았다.
  고침: 새 `Services/StreamedZipHeaders.cs` — 목록이 가리키는 그 모양 로컬 머리의 **압축 크기 칸(4바이트)만 목록 값으로 덮어 보이는 읽기 전용 스트림**(파일은 그대로. bit 3 항목은 엔진이
  크기·CRC 를 목록 값으로 바꿔 쓰고 ZipCrypto 확인 바이트도 시각으로 봄 — 내용 확인(CRC·AES 인증값)은 그대로). `ZipArchiveService.OpenForRead` 가 그 모양이 있을 때만 이 스트림으로 연다
  (목록·풀기·고른 파일 풀기·시험·**압축 검사**·자동 풀림 ZIP 이 모두 이 길 — 그 모양이 없으면 예전 길 그대로, 목록에 '암호 + 서술자' 기록이 없으면 기록을 나눠 담지도 않음).
  압축 고치기 뒤 검사(`Editing/ArchiveTester.TestZip`)의 '엔진이 ZIP 으로 알아보는가'도 같은 모양으로 묻는다(`StreamedZipHeaders.EngineRecognizes` — 예전엔 이 모양을 '일집이 풀 수 없는 방식…고치지 않았어요'로 막음).
  시험: 새 `StreamedEncryptedZipTests`(19 — 재현 재료와 변형 8: 수준 0·ZIP64 끔·빈 파일과 빈 폴더·한글 암호 CP949/UTF-8·AES-256·로컬 0xFFFFFFFF 표시, 고른 파일만 풀기,
  틀린 암호/암호 없음은 암호 문제로, 데이터 손상은 손상으로(앞 파일은 남음), 시험, 압축 고치기(지우기), 보정 범위 — 수정 전 코드로는 15개 실패),
  I4-1 위 새 `StreamedEncryptedZipCheckTests`(8 — 압축 검사 '문제없어요. 파일 N개를 확인했어요.'·틀린 암호는 암호 예외·손상된 파일만 '파일 3개 중 1개').
  ★ **기존 시험 재료를 바꿈**: `ArchiveEditReview2Tests` 의 '일집이 못 푸는 정상 ZIP' 두 시험(`일집이_못_푸는_정상_ZIP은_손상이라고_하지_않는다`·`결과만_못_풀면_…`)은 이 모양을
  '일집 풀기도 실패'하는 재료로 썼다(이제 풀림) → **모르는 압축 방식(97) ZIP** 으로 바꿈(계약·기대 문구는 그대로, '반디집은 정상으로 봄' 줄은 반디집도 못 푸는 방식이라 일집 구조 검사로 바꿈).
  패치를 만든 세션의 확인(Debug 한 번씩): main + 이 수정 — 통과 2427(기존 2408 + 19)·건너뜀 0·**실패 0**,
  I4-1(작업 폴더 10:47 사본 — 다른 세션이 계속 고치는 중) + 이 수정 + 압축 검사 시험 — 통과 2842(I4-1 2819 + 27)·**실패 0**(건너뜀 4 는 worktree 의 C++ 소스 시각 탓에 건너뛴 셸 확장 DLL 시험 — 시각을 맞춰 다시 돌려 14개 모두 통과).
  **얹은 뒤(이 판 — 4차 통합 점검 + I4-1 + 이 고침) 계약 테스트: 전체 2892 — 통과 2892·건너뜀 0·**실패 0**(Debug 35분 55초·Release 29분 58초·`ILJIP_TEST_TZ=UTC` 26분 15초 세 번 모두 — 4차 통합 점검 2411 + I4-1 454 + 흘려 쓴 암호 ZIP 27). 기대를 바꾸거나 약하게 한 기존 시험 없음(`ArchiveEditReview2Tests` 두 시험은 재료만, `ZipRepairHonestTests` 는 설명만)**
  기대를 바꾸거나 약하게 한 기존 시험 없음(위 ★ 두 시험은 재료만 바꿈).
- **2026-10-20 I5-A — 압축 속에서 연 파일의 이름 바꾸기·일집 먼저 닫기(감사 X3-F2·X3-F1·I3-3-close, 커밋 7e0903d·푸시 — 아래 글의 '커밋 전'은 그때 기준, 설치·배포 안 함)**:
  감사 X3(실제 Light PDF 코드로 압축 속 PDF 를 열고 고침)이 찾은 손실 셋을 고쳤다. 고치기 전에 감사 드라이버(그대로)를 main f61b472 Debug 빌드로 다시 돌려 재현 확인 —
  s4(탭 '이름 바꾸기' 뒤 저장): 질문 0·zip 그대로(고친 내용 놓침), s6a(Light PDF 로 연 채 일집 닫기 → 그 뒤 저장): 닫을 때 질문 0·다음 시작 청소가 저장한 파일을 말없이 지움,
  s3(자동 저장만 한 채 닫기 → 탭 닫으며 저장): 같음.
  ① **이름 바꾸기 따라가기(X3-F2)**: 감시를 파일 하나에서 **그 순번 폴더 전체**로(`FileSystemWatcherChangeSource` — 바뀜·생김·지움·이름 바꿈 모두, 2초 엿보기도 폴더의 '이름|크기|시각' 목록 비교).
  `OpenedEntry` 가 원래 경로를 기억하고, 지금 파일이 없어지면 같은 순번 폴더에서 **같은 확장자·편집기 임시 파일이 아닌**(이름이 `~`·`.~` 로 시작, `.tmp`·`.autosave` 로 끝남, 숨김·시스템 속성 — 제외) 가장 최근 파일을 따라간다
  (원래 이름이 다시 생기면 그쪽). 질문: ‘새 이름.pdf’(압축 안 이름 ‘한글 이름.pdf’)가 바뀌었어요. 압축 파일에 다시 넣을까요? 압축 파일에는 원래 이름(‘한글 이름.pdf’) 그대로 넣어요.
  ★ **결정 — 늘 원래 항목 이름으로 넣는다(묻지 않음)**: 편집기의 이름 바꾸기는 대개 임시 사본에서 일어나는 일이고, 압축 안 이름까지 바꾸면 다른 항목·받는 사람 쪽 이름이 함께 바뀌어
  '고친 내용만 넣기'보다 큰 변화라서. 압축 안 이름을 바꾸고 싶으면 일집의 [이름 바꾸기](F2)로. 넣는 자리·이름은 늘 원래 항목(`SplitEntryKey`) — 다른 항목·바이트 그대로.
  ② **닫을 때 알림(X3-F1·I3-3-close)**: `ConfirmExitAsync` — 고친 파일은 예전처럼 하나씩 [넣기][넣지 않기][취소](그 파일을 아직 다른 프로그램이 잡고 있으면 '이 파일은 아직 다른 프로그램에서 열려 있어요. 일집을 닫은 뒤 …
  다음에 일집을 켤 때 넣을지 물어볼게요.'를 덧붙임), 그 뒤 고치지 않았지만 **다른 프로그램이 아직 잡고 있는** 연 파일이 있으면 ‘압축 속에서 연 파일 N개가 아직 다른 프로그램에서 열려 있어요.(이름 다섯까지)
  … 일집을 닫을까요?’ [닫기][취소]. ★ **결정 — 잡혀 있는 파일만 알린다**: 편집기를 이미 닫은 고치지 않은 파일은 잃을 것이 없어 묻지 않고, 파일을 잡지 않는 프로그램(메모장 등)은 알 길이 없다
  (잡힘 = `FileShare.None` 으로 열기가 공유 위반·잠금 위반(0x80070020·21)으로 실패 — Light PDF(MuPDF)·워드·엑셀·한글은 연 동안 잡음).
  ★ **계약 바뀜**: 닫을 때 [넣지 않기]를 고르면 그 물어본 내용을 '넘긴 것'으로 기준을 옮긴다(`Rebase` — 예전엔 그냥 닫음). 넘긴 내용을 다음 시작 때 '넣지 않은 고친 파일'로 다시 알리지 않게 —
  넘긴 뒤 그 프로그램에서 **또** 저장한 것만 다음 시작 때 알린다.
  ③ **닫은 뒤 저장도 지우지 않기(X3-F1)**: 작업 폴더의 `owner.lock`(일집이 살아 있는 동안 혼자 잡는 파일) 안에 **연 파일 목록**(`WorkspaceManifest` — 표시 `iljip-open-v1`, 압축 경로·항목 열쇠·꺼낸 이름·지금 이름·
  기준 크기·시각·SHA-256 — 경로는 작업 폴더 안의 상대 경로)을 연 때·넣거나 넘긴 때·이름을 따라간 때·닫을 때 쓴다(일집이 갑자기 꺼져도 남음). 닫을 때(`ArchiveWorkspace.Dispose`) 다른 프로그램이 잡은 파일·
  넣지도 넘기지도 않은 고친 파일은 **그 순번 폴더째**(편집기의 `.autosave`·`~$` 같은 곁 파일도) 남기고 목록도 남기며 나머지만 지운다(`TempFolders.DeleteAllBut`).
  다음 시작 청소(`TempJanitor`)는 목록이 있는 꺼진 작업 폴더를 파일마다 본다 — 아직 잡혀 있으면 그대로(다음에 다시, 알리지 않음), 기준과 다르면(크기·시각이 같으면 그대로, 다르면 SHA-256 — 못 읽으면 '다름'으로)
  **지우지 않고** 알릴 목록에 넣고, 다 그대로면 예전처럼 지운다. 목록이 없는 작업 폴더(예전 판이 남긴 것)는 예전 그대로. 같은 압축을 다시 열 때 목록이 남은 작업 폴더는 건너뛰고 `<key16>-2` 를 쓴다
  (예전 `RemoveLeftovers` 가 남긴 고친 파일을 지우지 않게).
  ④ **다음 시작 알림**: 메인 창을 띄운 **뒤** 청소 결과를 기다려(`App.OfferLeftoversAsync` — 창 없이 풀기 등 창 없는 시작에서는 묻지 않음, 파일은 그대로 남아 다음 창 시작에) `MainViewModel.OfferLeftoverChangesAsync`(새 `MainViewModel.Leftovers.cs`):
  ‘압축 속에서 열어 고친 파일 중 압축 파일에 아직 넣지 않은 것이 있어요(일집을 닫은 뒤에 저장한 것). - ‘이름’ — 압축.zip … [압축에 넣기]를 누르면 원래 압축 파일의 원래 자리에 넣어요. 어떻게 할까요?’
  [압축에 넣기](원래 항목 자리·이름으로 바꿔 넣기 — 일반 고치기 길 `RunEditAsync`, 암호는 기억한 것·물음, 넣은 뒤 그 순번 폴더·빈 작업 폴더 지움) · [폴더 열기](탐색기에서 그 파일을 골라 보임 — 순번 폴더 셋까지, 파일은 그대로 → 다음 시작에 다시) ·
  [버리기](휴지통으로 — 실패하면 '고친 파일을 휴지통으로 옮기지 못했어요. 파일은 그대로 있어요' 알림) · [나중에](그대로, 다음 시작에 다시). 압축 파일이 없어졌거나 고칠 수 없는 형식이면 '‘x’를 압축 파일에 넣을 수 없어요 … 다른 곳에 저장할까요?' [다른 곳에 저장][나중에].
  ⑤ 시험 가짜 `FakeFileChangeSource.Raise(path)` 가 **같은 폴더**를 지켜보는 구독에 알리게(진짜 폴더 감시와 같은 뜻 — 예전 시험은 그대로 통과).
  시험: 새 `Contract/EntryRenameExitTests.cs` 12 — **고치기 전 코드로 11개 실패**(원래 API 만 씀 — 저장소 밖 f61b472 사본에서 돌림, 통과한 1개는 '넘기거나 넣었거나 안 고친 파일은 예전처럼 지운다' 유지 시험):
  이름 바꾼 뒤 고치면 묻고 원래 항목에(다른 항목 CRC·압축 크기·SHA 그대로), 저장하고 곧바로 이름 바꿔도, 닫을 때도, 진짜 폴더 감시로도, 잡힌 파일이 있으면 닫을 때 묻기, 고친 파일 [넣기][넣지 않기][취소](Theory 3),
  닫은 뒤 저장한 파일을 다음 시작 청소가 지우지 않음(편집기 흉내 = `FileShare.ReadWrite` 로 잡기 — Python·MuPDF 처럼 지우기 공유 없음), 이름 바꿔 저장하고 닫아도, 묻지 않고 끝나도(Shutdown 만) 지우지 않음.
  새 `Contract/EntryLeftoverTests.cs` 15(새 API — 가짜 시계·가짜 질문 창·가짜 셸·가짜 휴지통): 다음 시작 [압축에 넣기] → 원래 자리(다른 항목 그대로), 꺼진 동안 이름 바꿔 저장한 것도 원래 이름으로, [폴더 열기]·[나중에]는 다음에 다시,
  [버리기]는 휴지통·실패면 그대로, 압축이 없어졌으면 다른 곳에 저장, 안 바뀌었으면 지우고 아직 열려 있으면 다음에, 닫을 때 잡힌 순번 폴더 통째(.autosave), 남은 작업 폴더가 있는 압축 다시 열기(-2),
  갑자기 꺼져도 목록이 남음, 목록 없는 작업 폴더는 예전처럼 지움, 편집기 임시 파일 무시, 진짜 폴더 감시가 이름 바꾸기·새 이름 저장을 알림, App 이 메인 창을 띄운 뒤 묻기(소스 계약).
  **감사 X3 다시(A4 — 감사 드라이버 그대로 + 더한 경우 넷, 실제 Light PDF 코드 + 고친 Debug 빌드, `ILJIP_TEST_APPDATA` 스크래치, 저장소 밖 하네스)**: s4 — 이름 바꾼 뒤 저장마다 질문·[넣기]로 zip 에 들어감,
  s6a·s3 — 닫을 때 알림이 뜨고 파일이 남음, s6a_wait — 닫기를 미루고 Light PDF 저장 → 질문 [넣기] → zip 에(메모.txt 그대로), s6a_close — [닫기] 뒤 Light PDF 저장 → 청소가 지운 것 0 → 다음 시작 알림 [압축에 넣기] → zip 에
  (작업 폴더 지워짐·다른 항목 그대로·zip 의 인터넷 표시 ZoneId=3 그대로), s3_close — 닫은 뒤에도 `.autosave` 남음 → 탭 닫으며 저장 → 다음 시작에 넣음, s4_close — 이름 바꾸기·닫을 때 질문·닫은 뒤 또 저장 → 다음 시작에
  원래 이름으로 넣음(세 번의 고침이 모두 zip 에, 항목 이름 그대로). **잃은 편집 0**, 하네스 오류 0. Light PDF 저장소는 읽기만(이름 바꾸기·저장은 Light PDF 코드를 불러 흉내 — 저장소 파일 안 바꿈).
  남긴 한계(minor — 정직한 한계, 말없는 손실 아님): 파일을 잡지 않는 편집기(메모장)는 닫을 때 알 수 없음 — 고치지 않은 채 닫으면 순번 폴더를 지우고, 그 뒤 메모장에서 저장하면 폴더가 없어 저장이 실패함(메모장이 오류를 띄우리라 봄 — 진짜 메모장으로는 확인 안 함, MANUAL_CHECKLIST 25절. 고쳐 둔 것은 닫을 때 질문으로 지킴),
  편집기의 '다른 이름으로 저장'으로 순번 폴더에 **새 파일**을 만들고 원래 파일도 남아 있으면 따라가지 않음(X3 s5a — 별도 지적, '향후 작업'), 닫는 순간 백신·검색 색인이 파일을 잠깐 잡으면 '아직 열려 있어요'가 한 번 더 뜰 수 있음(질문만 하나 더).
  계약 테스트: 전체 2892 → 2919(+27) — 통과 2919·건너뜀 0·**실패 0**(Debug 36분 37초, 2026-10-20 09:24). 기대를 바꾸거나 약하게 한 기존 시험 없음(계약 바뀜은 위 ★ [넣지 않기(닫기)] 하나 — 닫을 때 그 단추를 누르는 기존 시험은 모두 그대로 통과).
  관련 묶음(EntryReinsert·EntryOpen·EntryWorkspace·Wave2/3/4·StartScreen·InternetMark·UiStringAudit·DragOut·ClipboardCopy·MainWindowEditUi) 328개 따로도 통과. 빌드 경고 0(본체).
  전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 네 파일(10/12·10/15·10/16·10/18) 크기·시각 그대로, `%LOCALAPPDATA%\Iljip\temp\open` 없음(시험이 사용자 임시 폴더를 쓰지 않음).
  `D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음. 저장소 밖에 둔 것(지워도 됨): 스크래치의 f61b472 사본·X3 재검 하네스.
- **2026-10-20 I5-B — 최종 감사 안전·정직 5건(IA-SELFNAME·IB-zip-replace-cancel-deletes-old·IX-F5·IX-F2·IA-OPENFILE-OVERWRITE, 커밋 875b9e6·푸시 — 아래 글의 '커밋 전'은 그때 기준, 설치·배포 안 함)** — 출발점 main 7e0903d(I5-A 커밋).
  감사 재현(고치기 전 — 감사 하네스·스크립트 그대로, 스크래치 사본): IA self — 7z '여기에 풀기' + 덮어쓰기면 바깥 `첨부.7z`(348B)가 안쪽 것(164B)으로 바뀌고 '성공', selfvm — 설정 덮어쓰기 + '다 푼 뒤 휴지통'이면 방금 풀어 낸 안쪽 압축이 휴지통으로
  (결과 폴더에 설명.txt 만), zip 은 멈추고 '관리자 권한으로 다시 시도'를 권함, openxl — 엑셀로 연 `견적서 1.xlsx` 위에 풀면 나머지(메모.txt·빈파일.txt)를 안 풀고 멈추며 관리자 권유,
  IB i21_cancel — zip_aes·plain·store·zipcrypto [바꾸기] 중 700ms 취소면 'old intact: MISSING'(기존 zip 도 새 zip 도 없음), IX sec1 — 하드 링크 tar 는 a.txt 12B·b.txt 0B·종료 0, 검사 '문제없어요. 파일 2개를 확인했어요.',
  IX-F5 — `--extract-to <없는 경로>` 는 일집 창 + '문제가 생겼어요' 창(재사용장치 제한 시간 1800초).
  ① **자기 압축 지키기(IA-SELFNAME)**: `ExtractionScope.Begin(resolver, sourceArchive)`(선택 인자 — 화면의 두 풀기 길 `BatchExtractor.ExtractOneAsync`·`MainViewModel.ExtractToFolderCoreAsync` 가 넘김)가
  풀고 있는 압축의 전체 경로와 조각 전부(`ArchiveVolumes.AllParts` — `.7z.002`·`.z01`·`.part2.rar` 등)를 '지킬 경로'로 둔다. `Decide` 는 그 경로면 묻지도 설정을 보지도 않고 **늘 '이름 (1)'**(Rename)
  — 직접 쓰기(`OutputFiles.OpenTarget`)·임시 폴더 옮기기(`MoveResolved` — 7z.dll·압축된 TAR) 둘 다 같은 판정을 거치므로 형식과 상관없음. 비켜 쓴 것은 `ExtractionNotes.SelfRenamed` 에 적고 결과 창에
  '풀고 있는 압축 파일과 이름이 같은 파일 1개는 그 압축 파일을 덮지 않게 이름을 바꿔 풀었어요(원래 압축 파일은 그대로예요):\n- 첨부.7z → 첨부 (1).7z'. ★ **결정 — 묻지 않고 비켜 쓴다**: 덮어쓰기를 고를 수 있게 두면
  7z 는 풀고 있는 압축이 안쪽 것과 바뀌고 zip 은 일집 자신이 읽는 중인 파일이라 멈추며 맞지 않는 관리자 권유까지 하므로, 그 항목은 같은 이름 창도 띄우지 않는다(다른 같은 이름 항목은 예전처럼 묻고 고른 대로).
  ② **[바꾸기]는 다 쓴 뒤에만(IB)**: 새 `OutputFiles.WriteReplacing(target, write)` — 대상이 없으면 예전처럼 바로 쓰고, 있으면 옆 임시 파일(`TempSibling` — `.~이름.8글자.tmp`, 숨김)에 다 쓴 뒤
  `ReplaceExisting`(잠깐 잠김은 다시 — 예전 규칙)으로 바꿔 넣는다. 취소·실패면 임시 파일만 지우고(finally) 기존 파일은 손대지 않음 — `CompressFlow` 의 '시작 전과 달라진 파일 지우기'도 기존 파일이 그대로라 지우지 않는다.
  나누지 않는 ZIP(`ZipArchiveService`)·TAR(`TarArchiveService`)·GZ·BZ2(`GZipArchiveService`·`BZip2ArchiveService`)에 같은 포장(감사는 ZIP 만 짚었지만 TAR·GZ·BZ2 도 같은 길로 바로 써서 같은 손실 — 함께 고침).
  7z·나눔·자동 풀림은 원래 임시 파일이라 그대로. 기존 파일을 원본 폴더 안에 두고 [바꾸기]해도 새 압축에 기존 압축을 담지 않게 ZIP·TAR 의 '자기 자신 건너뛰기'에 원래 경로도 넣음(`alsoSkip`).
  ③ **창 없이 풀기 인자(IX-F5)**: `StartupDecision.IsHeadlessExtract` 는 이제 **첫 인자가 `--extract-to`** 인가만 본다(둘째가 있는 파일인지 안 봄 — `fileExists` 매개변수는 부르는 모양 그대로 두려고 남기고 안 씀).
  새 `StartupDecision.HeadlessArgumentProblem(args)` — 압축·풀 폴더가 비었거나 없음 → '창 없이 풀기에는 압축 파일과 풀 폴더를 모두 주어야 해요. (Iljip.exe --extract-to <압축 파일> <풀 폴더>)',
  폴더 → '풀 압축 파일 대신 폴더를 받았어요: <경로>', 없는 파일 → '풀 압축 파일을 찾을 수 없어요: <경로>'. `App.RunHeadlessExtractAsync` 가 맨 앞에서 보고 문제면 오류 로그
  `Headless extract failed: <준 경로>` + 다음 줄 그 문구, 표준 오류 한 줄, **종료 1**(풀 폴더를 만들기 전 — 아무것도 안 남김). 재사용장치 약속('압축 아님' 표지 없음 → 반디집으로)과 맞음.
  ④ **tar 링크 알림(IX-F2)**: `SharpCompressArchiveServiceBase.IsEmptyLink`(항목의 `LinkTarget` 이 있고 폴더가 아니며 크기 0)인 항목을 내용 없이 만든 뒤 `ExtractionNotes.Current` 에 적는다
  (tar·그리고 압축된 TAR 의 안쪽 tar 길 — 덮어쓰기로 정했다가 잠겨 건너뛴 항목은 안 적음). 화면: 하나 풀기면 알림 창 '‘링크묶음.tar’을 풀었어요.\n\n링크 1개는 내용 없이(0바이트) 만들었어요. 일집은 링크를 되살리지 않아요:\n- b.txt (원래 파일: a.txt)',
  여러 개면 요약 창 알림 줄에 '‘이름’: …'. 검사(`TestReport.Notes` — 새 속성): 손상이 없어도 '압축 파일은 멀쩡해요(파일 N개 확인). 다만 알아 둘 것이 있어요.' + '링크 N개는 일집으로 풀면 내용 없이(0바이트) 만들어져요.
  일집은 링크를 되살리지 않아요(압축 파일이 손상된 것은 아니에요):' + 이름 스물까지(알림 창 — 상태 줄 '시험 완료: 손상 없음 — 알림 있음'). 압축된 TAR 의 흘려 읽는 검사(`CompressedTarArchiveService.TestStreaming`)도 같은 줄.
  창 없이 풀기: `ExtractionNotes.Begin()`(같은 이름 범위·설정 없이 알림만 모음 — 소스 계약의 금지 낱말 없음)으로 감싸 풀고, 링크가 있으면 오류 로그 + 표준 오류 한 줄
  '링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.'(셋까지 '외 N개') + **종료 1**(풀린 파일은 그대로 둠). ★ 종료 코드 결정은 위 '일집-dist 재배포' 절 I5-B 줄(0 은 말없는 손실, 2 는 'Unsupported' 짝, 새 코드는 재사용장치가 1 과 같게 봄 → 1).
  링크를 내용 없이 만든 압축은 '다 푼 뒤 압축 파일을 휴지통으로'를 하지 않는다(`ExtractionScope.KeepArchiveCount` = 건너뜀 + 링크 — 압축 파일에만 온전한 것이 있음).
  ⑤ **열린 파일은 건너뛰고 계속(IA-OPENFILE-OVERWRITE)**: `ReplaceExisting` 이 다시 해 봐도 못 바꾸면 원래 파일을 혼자 열어 보고(`IsOpenElsewhere` — `FileShare.None`, 공유·잠금 위반 0x80070020·21 이면 다른 프로그램이 엶)
  새 `TargetInUseException`('‘x’ 파일을 다른 프로그램이 쓰고 있어 바꾸지 못했어요. 그 프로그램을 닫고 다시 해 주세요.')을 던진다. 풀기 범위 안이면 `MoveResolved`·`OutputTarget.Commit`(이제 `string?` — 건너뛰면 null)이
  받아 임시 파일을 지우고 `ExtractionScope.NoteInUseSkipped`(덮어씀 −1·건너뜀 +1·`ExtractionNotes.InUse`)로 그 파일만 건너뛴다 — 원래 파일 바이트 그대로, 나머지는 다 풂, 결과 창 '다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개
  (원래 파일 그대로예요 — 그 프로그램을 닫고 다시 풀어 주세요):\n- 견적서 1.xlsx'. `ElevationService.IsAccessDenied` 는 `TargetInUseException` 을 권한 문제로 보지 않음(관리자 권유 없음 — 권하는 것은 진짜 접근 거부뿐, 읽기 전용 파일·압축 문제·다른 프로그램이 쓰는 중은 권하지 않음).
  건너뛴 파일이 있으면 휴지통으로 보내지 않음(예전 '건너뜀' 규칙). 화면 밖(압축 만들기 [바꾸기] 등)에서는 그 예외가 그대로 실패 이유(쉬운 한국어)가 된다.
  알림 모음: 새 `Iljip/Services/ExtractionNotes.cs`(AsyncLocal — 화면은 `ExtractionScope.Notes`, 창 없이 풀기는 `ExtractionNotes.Begin()`, 아무도 안 열었으면 적지 않음). `BatchExtractor.ArchiveOutcome.Notice`(새) →
  `RunExtractBatchAsync` 가 하나면 알림 창, 여럿이면 요약 창 알림 줄. `ExtractToFolderCoreAsync`(일집 창 풀기)도 끝에 알림 창.
  시험: 새 `Contract/ExtractSafetyNoticeTests.cs` 22 — 이중 압축 여기에 풀기(zip·7z — 바깥 SHA 그대로·안쪽 '첨부 (1)'·알림), 같은 이름 창에서 [덮어쓰기]·모두에 적용을 골라도(zip·7z — 자기 이름은 묻지 않고 다른 같은 이름은 덮어씀),
  우클릭 + 설정 덮어쓰기·휴지통(zip·7z — 휴지통엔 바깥 원래 바이트, 오류·관리자 권유 없음), 나눠진 7z 조각 이름(`자료.7z.002`), 일집 창 같은 폴더 풀기, 잠긴 파일 건너뛰기(zip·7z·rar·tar·egg — `FileShare.None` 과 엑셀처럼
  쓰기 핸들 + 읽기 공유, 원래 바이트 그대로·나머지 다 풂·건너뜀 1·알림·남은 임시 0), 우클릭 풀기에서 잠긴 파일(zip·7z — 관리자 권유 없음·휴지통 안 감), 열린 파일은 권한 오류 아님, tar·tgz 링크 알림·링크 없으면 알림 없음,
  검사 링크 알림(tar·tgz), 우클릭 결과·여러 개 요약에 링크 알림(휴지통 안 감). 새 `Contract/CompressReplaceTests.cs` 16 — ZIP(보통·AES·ZipCrypto·저장만)·TAR·GZ·BZ2 × 취소·쓰기 실패(가짜 진행 창이 쓴 바이트가 처음 0을 넘을 때
  [취소]하거나 '디스크 공간이 모자라요' 0x80070070 을 던짐 — 실제 디스크를 채우지 않음): 기존 SHA 그대로·만든 곳 파일은 기존 하나뿐(숨김 임시 0), 실패 이유 한국어. [바꾸기]가 끝까지 되면 새 내용·임시 0·원본 폴더 안 기존 압축은 안 담음(ZIP·TAR).
  새 `Contract/HeadlessNoticeTests.cs` 3 — 판정(순수), 실제 exe 7가지(없는 zip·egg·폴더·빈 압축·빈 폴더·인자 둘·하나 → 창 없이 종료 1·한국어 한 줄 그대로·'압축 아님' 표지 없음·풀 폴더 안 만듦·
  시험 로그 폴더에 `Headless extract failed:` + 다음 줄 이유), 실제 exe tar·tgz 링크 → 종료 1·그 한 줄, 보통 tar → 종료 0·표준 오류 없음(시험은 띄우기 전에 `IsHeadlessExtract` 로 창 없는 길인지 먼저 확인, 60초에 끔).
  **고치기 전 코드에서 실패 확인**: 위 새 시험은 새 API(`ExtractionNotes`·`Begin(…, 압축)` 등)를 써서 예전 코드에서 빌드되지 않으므로, 같은 약속을 **예전 API 만으로** 쓴 재현 시험 10개(B1 zip·7z, B2, B3, B4 검사·실제 exe, B5 zip·7z·tar·egg)를
  저장소 밖 7e0903d 사본에서 돌려 **10개 모두 실패**(B1 바깥 SHA 다름, B2 '기존 zip 이 사라짐', B3 판정 false, B4 검사 '문제없어요' 포함·exe '종료 코드 0, stderr=''', B5 잠긴 파일에서 멈춤), 같은 파일을 고친 코드에 넣어 10개 모두 통과한 뒤 저장소에서 뺌.
  **감사 다시(저장소 밖 하네스 사본 — 원본 감사 폴더는 손대지 않음)**: I-A self(zip·7z — 바깥 그대로·'첨부 (1)')·selfvm(휴지통엔 바깥 원래 것, 안쪽 '자료 (1)' 남음, 알림 창)·openxl(엑셀처럼 잡은 파일만 그대로, 빈파일.txt·하위\메모.txt 풀림, 관리자 권유 없음) —
  7z.dll 길과 SharpCompress 길 둘 다. I-B i21_cancel(zip_aes·plain·store·zipcrypto — 'old intact: True', 남은 것 기존 파일 하나), I-X sec1(하드 링크 tar 종료 1·한 줄, 결과 폴더 밖 쓰기 0)·검사('압축 파일은 멀쩡해요… 다만 알아 둘 것이 있어요.' + 링크 줄).
  스크래치 **Release 사본**(`ILJIP_TEST_APPDATA` 스크래치, 120초 제한)으로 B3 7가지 모두 종료 1·2~3.5초·한국어 한 줄·표지 없음, B4 GNU·PAX tar·GNU tgz 종료 1·a.txt 12B·b.txt 0B·로그 두 줄.
  **기대를 바꾼 기존 시험 둘**(위 '현재 진행 단계' — 지적이 바꾸라고 한 바로 그 동작): `StartupDecisionTests`(창 없이 풀기 판정 — 이름 `창_없이_풀기_판정은_첫_인자로_하고_elevated가_앞에_붙으면_아니다`, '일반 동사' 목록에서 없는 압축 줄 뺌),
  `CollisionTests.덮어쓸_파일이_잠겨_있으면_그_파일만_건너뛰고_원본은_그대로`(예전 이름 `…_한국어_오류이고_원본은_그대로` — 잠긴 파일 바이트 그대로·남은 임시 0 은 그대로 확인하고, 멈춤 대신 나머지 풀림·건너뜀 1·알림에 이름을 확인).
  남긴 한계(minor): 링크 알림은 풀기·검사에만(항목 열기·끌기·Ctrl+C 는 말없이 0바이트), 7z.dll 형식의 링크는 범위 밖, 잠김 판정 순간 파일이 풀리면 예전 오류, `ExtractionScope.Begin` 이진 모양 바뀜(저장소 밖 예전 하네스는 다시 빌드) — '향후 작업' I5-B 줄.
  계약 테스트: 전체 2919 → 2960(+41) — 통과 2960·건너뜀 0·**실패 0**(Debug 52분 30초, 2026-10-20 15:01 — 이 PC 남은 메모리 0.8GB 안팎이라 I5-A 때보다 느림). `ReuseDeviceContractTests` 포함 전부 통과(재사용장치 약속 회귀 0). 빌드 경고 0(본체 Debug 다시 빌드).
  전체 시험 전후 `%LOCALAPPDATA%\Iljip\logs` 두 파일(10/12 624,749·10/16 397,374) 크기·시각 그대로, `%LOCALAPPDATA%\Iljip\temp\open` 없음.
  `D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음, 셸 확장·파일 연결·레지스트리 손대지 않음. 저장소 밖에 둔 것(지워도 됨): 스크래치 `i5b\`(7e0903d 사본·재현 시험·감사 하네스 사본·Release 사본).
- **2026-10-20 5차 통합 점검 — I5-A(7e0903d)·I5-B(875b9e6) 를 합친 뒤(커밋 7eff574·푸시, 설치·배포 안 함)**: 출발점 main 875b9e6, 작업 폴더 깨끗(stash 다섯 그대로).
  ① **교차 시험 `Wave5IntegrationTests`(새 파일 — 38, 실제 exe 2)** — 묶음마다의 시험이 자기 기능만 보는 것을 겹치는 길로:
  다시 넣기 추적(I5-A) × 여러 파일 열기(하나는 이름을 바꿔도 질문은 파일마다·각자 원래 항목, 연 채 닫으면 '연 파일 2개' 질문 하나 → 다음 시작에 둘 다), × 같은 이름(같은 이름 두 항목 `자료/보고서.txt`·`사본/보고서.txt` 는 각자 자리,
  다른 항목 이름(`메모.txt`)으로 바꿔 고쳐도 그 항목은 그대로, 다른 폴더의 같은 이름 두 압축은 각자의 압축에), × 암호 압축(ZIP ZipCrypto·7Z 이름까지 잠김 — 이름을 바꿔 고쳐도 다시 묻지 않고 잠긴 채 원래 항목,
  남은 파일 여럿은 다음 시작에 암호 한 번, 틀린 암호·[취소]면 압축·파일 그대로), × 나눔 압축(ZIP .z01·7Z .001 — 고칠 수 없으니 '다른 곳에 저장'(새 이름으로 저장 창), 남은 파일은 다음 시작에 다른 곳에 저장, 조각 바이트 그대로),
  다음 시작 넣는 순간 그 파일을 혼자 잡음(아래 (나)), 고치기 엔진의 [덮어쓰기] + 못 읽는 원본(ZIP·7Z — 아래 (나));
  자기 자신 덮어쓰기 막기(I5-B) × 알아서 풀기(자기 이름 하나뿐인 첨부는 '이름' 폴더로·휴지통엔 바깥 원래 바이트, 맨 위 하나가 나눈 7z 의 다른 조각 이름이어도 '자료.7z (1).002'), × 여러 개 풀기(아래 (가) — 순서 둘,
  우클릭 + 설정 덮어쓰기·휴지통이면 두 원래 압축이 휴지통으로·요약 알림), × 압축된 TAR(tgz·tbz2 — 임시 폴더에서 옮기는 길도 '첨부 (1).tgz'), × 창 없이 풀기(실제 exe — zip·7z·tgz 를 압축이 있는 폴더로 풀어도 바깥 그대로·종료 0·표준 오류 없음);
  tar 링크 알림 × bzip2·xz 로 감싼 tar(풀기 알림·검사 '압축 파일은 멀쩡해요…'·실제 exe 종료 1·한 줄), × 검사 요약(`--test` 셋 — 링크 든 tar·tbz2 는 주의 창, 보통 zip 은 '문제없어요' 안내 창, 문제 창 0),
  × 여러 개 풀기 요약(이중 압축 첨부와 링크 tar 를 같이 — 압축마다 따로 알림, 휴지통엔 첨부만).
  **처음 돌림: 28개 중 5개 실패**(나머지 23개는 처음부터 통과 — 그 교차는 고칠 것 없음). 고친 것 셋:
  (가) **같이 푸는 압축 지키기**(I5-B 남긴 minor 'IA-SELFNAME 은 풀고 있는 압축 하나만'): `가.zip`(안에 다른 `나.zip`)과 `나.zip` 을 함께 '여기에 풀기' + 덮어쓰기면 아직 안 푼 `나.zip` 이 안쪽 것으로 바뀐 뒤
  그 내용(`안쪽.txt`)이 풀렸고, 순서가 반대면 이미 푼 `나.zip` 이 바뀌어 '다 푼 뒤 휴지통으로'가 바뀐 것을 보냈다(원래 `나.zip` 은 덮여 사라짐 — 휴지통에도 없음). → 새 `ExtractionScope.Begin(resolver, IEnumerable<string?>)`
  (예전 `Begin(resolver, string?)` 은 그대로 — 이것을 부름)이 여러 압축(+조각)을 지키고, `BatchExtractor.ExtractOneAsync` 가 지금 푸는 압축과 이 묶음의 다른 압축 전부(돌아가는 중에 더해진 것 포함)를 넘긴다.
  '나.zip → 나 (1).zip' 는 예전 알림 그대로('풀고 있는 압축 파일과 이름이 같은 파일 1개는 …').
  (나) **바꿔 넣기의 원래 항목 지키기**(I5-A 남긴 minor '다음 시작 [압축에 넣기] 중 그 파일을 못 읽으면 원래 항목이 빠지는데 넣었어요'): 뿌리는 고치기 엔진 — `EditPlanner.AddFile` 의 '덮어쓰기'가 원래 항목을 먼저 지우고,
  새 파일을 못 읽어 빼면(ZIP `ZipArchiveEditor` 의 임시 ZIP 쓰기에서 빠짐·`NothingToCompressException`, 7Z `SevenZipArchiveEditor` 의 미리 열어 보기·`SourceUnreadableException`) `a.Removed = true` 만 해서
  원래 항목은 지운 채 압축을 바꿨다(일반 넣기에서 같은 이름 [덮어쓰기]를 고르고 그 원본을 다른 프로그램이 혼자 잡았을 때도 같음). → `AddedFile.UndoReplace`(덮어쓰기로 대신한 것을 되살리는 일)·`EditPlanner.Drop(a)`,
  두 엔진의 빼는 자리 넷을 `Drop` 으로, ZIP 은 뺀 뒤 남길 항목을 다시 셈·바뀌는 것이 없으면 '넣을 수 있는 파일이 하나도 없어서 압축 파일을 그대로 두었어요'(압축 바이트 그대로).
  한 번에 넣는 같은 이름 둘(다른 폴더의 `보고서.txt` 둘 — 둘째가 첫째를 덮어쓰기)에서 둘째를 못 읽으면: 첫째가 대신했던 원래 항목을 둘째가 이어받아 되살림(첫째 자체는 예전처럼 빠짐 —
  임시 압축·길이 확인을 지나 다시 넣을 수 없음. 처음엔 앞서 넣기로 한 파일도 되살리게 했다가 ZIP 은 임시 압축에서 못 찾는 길이 보여 이렇게 바꿈).
  다음 시작 넣기(`MainViewModel.InsertLeftoversAsync`)는 다시 넣기(`ReinsertAsync`)처럼 먼저 옆 폴더 `순번.reinsert` 에 사본을 떠서 넣는다 — 그 순간 못 읽으면 압축은 건드리지 않고 문제 창
  '‘보고서.txt’를 읽지 못해 압축 파일에 넣지 못했어요. 고친 파일은 그대로 두었어요 — 그 파일을 연 프로그램을 닫고 다음에 일집을 켤 때 다시 넣어 주세요.'(다른 곳에 저장은 권하지 않음 — 읽지 못하는 파일), 파일 그대로·다음 시작에 다시.
  넣는 동안 그 파일을 또 저장했으면(크기·시각이 사본 뜬 때와 다름) 지우지 않고 다음 시작에 다시 알림. 상태 줄(`MainViewModel.LeftoverStatus`)은 넣은 것·넣는 동안 또 저장돼 남긴 것·다른 곳에 저장한 것·그대로 둔 것을
  따로: 하나도 못 넣었으면 '고친 파일 N개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요', 다른 곳에 저장만 했으면 '고친 파일 N개를 다른 곳에 저장했어요', 또 저장된 것은 '그중 N개는 넣는 동안 또 저장돼서 지우지 않았어요'
  (예전 '고친 파일 0개를 압축 파일에 넣었어요. 1개는 …'·다른 곳에 저장만 해도 '0개를 넣었어요', 또 저장된 하나를 '1개를 넣었어요. 1개는 그대로…'로 둘로 셈. 넣은 것 + 그대로 둔 것은 예전 문구 그대로).
  (다) **암호 한 번만**: 다음 시작에는 열린 작업 폴더가 없어 기억한 암호가 없으므로 같은 암호 압축의 파일마다 암호를 다시 물었다 → `RunEditAsync` 의 새 선택 인자 `usedPassword`(됐을 때 쓴 암호)로
  그 알림 동안만 압축별로 기억해 둘째 파일부터 묻지 않음(메모리에만, 끝나면 지움).
  **고치기 전 코드에서 실패 확인**: 위 5개는 이 점검의 첫 돌림(고치기 전)에서 실패(같이 푸는 압축 셋 — '진짜.txt' 대신 안쪽 내용·휴지통 확인 실패, 다음 시작 잠김 — 압축 SHA 다름, 암호 — 암호 창 2번).
  엔진 시험 `덮어쓰기로_넣을_원본을_못_읽으면_원래_항목을_지우지_않는다`·`같은_이름_둘을_넣다가_뒤의_것을_못_읽어도_원래_항목이_남는다`(각 zip·7z — 고친 뒤 더함)는
  엔진 세 파일만 875b9e6 판으로 잠깐 되돌려 **넷 모두 실패**(예전 엔진은 원래 `보고서.txt` 를 뺀 채 압축을 바꿈)를 확인하고 되살림.
  ② **재사용장치 약속(실제 Release exe — 스크래치 사본)**: 18가지 모두 기대대로(스크립트 `w5_reuse\w5_contract.py` — 감사 I-X 재료를 읽기만, 결과 `w5_contract_result.json`, 자식 하나 0.9~4.7초).
  성공 길(zip·7z·tgz·링크 없는 tar) 종료 0·표준 오류 없음 / 손상(HTML 을 .egg 로 저장·0바이트 egg·무작위 alz·잘린 7z — 반쪽 파일 안 남김)·목록까지 암호인 7z 는 종료 1·쉬운 한국어 한 줄('압축 아님' 표지 없음 → 재사용장치는 반디집으로) /
  압축 아닌 .xyz·속이 zip 인 .xlsx 는 종료 2·'Unsupported archive type: .xyz'·'….xlsx' / 인자 문제(없는 압축·폴더·빈 경로)는 종료 1·한 줄·풀 폴더를 만들지 않음 /
  링크 tar·tgz·tbz2 는 종료 1·'링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). …'(tbz2 결과 a.txt 12바이트·b.txt 0바이트) / 압축이 있는 폴더에 tgz(안에 같은 이름 `첨부.tgz`)를 풀어도
  종료 0·바깥 SHA 그대로·안쪽은 `첨부 (1).tgz`. 로그: `ILJIP_TEST_APPDATA\logs\error-날짜.log` 에 `Headless extract failed: <경로>` + 다음 줄 이유(없는 압축·링크 tbz2), 잘린 7z 줄 있음,
  성공한 zip 은 줄 없음. 확인 뒤에도 사용자 `%LOCALAPPDATA%\Iljip\logs` 그대로.
  ③ 계약 테스트: 전체 2960 → 2998(+38 `Wave5IntegrationTests`) — 통과 2998·건너뜀 0·**실패 0**(Debug 28분 34초·Release 29분 23초·`ILJIP_TEST_TZ=UTC` 30분 32초 — 2026-10-20 20:13~21:48, 한 번에 하나씩. 상태 줄·같은 이름 둘 넣기를 더하기 전 2990 판도 Debug 29분 10초 실패 0). 기대를 바꾸거나 약하게 한 기존 시험 없음
  (`HeadlessNoticeTests.RunIljip` 은 같이 쓰려고 private → internal 만). 관련 묶음(EntryLeftover·EntryRenameExit·EntryReinsert·ArchiveEdit*·ExtractSafetyNotice·Collision·Batch*·MainViewModelEdit·EntryOpen·
  EntryWorkspace·RecycleAfterExtract·SmartExtract·HeadlessNotice) 426개 따로도 통과(마지막 고침 — 같은 이름 둘 넣기·상태 줄 — 뒤에는 Wave5·ArchiveEdit*·MainViewModelEdit·
  EntryLeftover·EntryReinsert·EntryRenameExit·EntryWorkspace 339개). Release 빌드(`dotnet build Iljip\Iljip.csproj -c Release --no-incremental`) 경고 0·오류 0.
  세 번 돌리는 동안·실제 exe 확인 뒤에도 `%LOCALAPPDATA%\Iljip\logs` 두 파일(10/12 624,749·10/16 397,374) 크기·시각 그대로, `%LOCALAPPDATA%\Iljip\temp\open` 없음.
  남긴 한계(minor — '향후 작업' 5차 줄): 링크 tar 를 같은 이름 [덮어쓰기]로 풀면 있던 파일이 0바이트로 바뀜(알림만), 이름이 같은 두 항목의 닫기·다음 시작 알림 줄이 같아 보임, 오래된 남은 파일이 새로 넣은 내용을 덮을 수 있음(I5-A 남김 그대로).
  `D:\Python\일집-dist` 그대로, `Iljip\bin` 에 exe 없음, 셸 확장·파일 연결·레지스트리·Light PDF 저장소 손대지 않음. 저장소 밖에 둔 것(지워도 됨): 스크래치 `w5_reuse\`(Release 사본·약속 확인 스크립트·결과)·`w5_proof\`.

## 사용자 선호

- 한국어, 사근사근한 톤
- 방향성 무조건 따르지 말고 조언할 게 있으면 먼저 물어볼 것
- 코딩 검증 통과 + 질문 없으면 다음 단계 자동 진행
- 매번 칠 명령어는 코드 블록으로 정리해서 제공
