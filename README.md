# 일집 (Iljip)

광고 없는 압축 프로그램. 반디집/알집을 대체하는 것이 목표.

## 현재 상태: v0.2

- **압축**: ZIP, TAR, GZIP, BZIP2
- **해제**: ZIP, **7Z**, TAR, GZIP, BZIP2, **RAR**
- 드래그 앤 드롭 (압축 파일 → 열기, 일반 파일/폴더 → 압축)
- **압축 옵션 다이얼로그**: 압축 수준(저장/빠름/보통/최대)
- **암호 압축/해제**: ZIP은 AES-256, 다른 포맷은 SharpCompress 한계로 미지원
- 한글 파일명(CP949/EUC-KR) 자동 판별
- 진행률 표시 및 취소
- Zip Slip 방어

## 사용 라이브러리

- **SharpCompress** — 다중 포맷 해제 및 ZIP 외 압축
- **SharpZipLib** — ZIP 암호 압축(ZipCrypto + AES)
- **CommunityToolkit.Mvvm** — MVVM 보조
- **System.Text.Encoding.CodePages** — CP949 디코딩

## 빌드 / 실행

요구사항: **.NET 8 SDK** (Windows)

```powershell
# 프로젝트 루트에서
dotnet restore
dotnet build -c Release
dotnet run --project Iljip
```

또는 Visual Studio 2022 / Rider 에서 `Iljip.sln` 열고 F5.

## 단일 실행 파일로 배포 (옵션)

```powershell
dotnet publish Iljip/Iljip.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

결과물: `Iljip/bin/Release/net8.0-windows/win-x64/publish/Iljip.exe`

## 프로젝트 구조

```
Iljip/
├── App.xaml(.cs)                # 진입점, 전역 예외/인코딩 등록
├── MainWindow.xaml(.cs)         # 메인 창 (드래그앤드롭)
├── Models/
│   ├── ArchiveEntry.cs          # 압축 항목 모델
│   └── ArchiveProgress.cs       # 진행률 모델
├── Services/
│   ├── IArchiveService.cs       # 포맷별 압축/해제 추상화
│   ├── ZipArchiveService.cs     # ZIP 구현 (SharpCompress)
│   ├── KoreanFileNameDecoder.cs # CP949 자동 디코딩
│   └── ArchiveServiceLocator.cs # 확장자별 라우팅
├── ViewModels/
│   └── MainViewModel.cs         # MVVM, CommunityToolkit.Mvvm
├── Views/
│   └── ProgressDialog.xaml(.cs) # 진행률 다이얼로그
├── Converters/
│   ├── BytesToHumanConverter.cs
│   └── BoolToVisibilityConverters.cs
└── Resources/
    └── Styles.xaml              # 공통 스타일/컬러
```

## 다음 단계 (계획)

- [x] 7Z / TAR / GZ / BZ2 지원
- [x] RAR 해제 지원
- [x] 암호 압축/해제 (ZIP AES-256)
- [x] 압축 옵션 다이얼로그 (압축 수준)
- [ ] 윈도우 탐색기 우클릭 메뉴 통합 (`IExplorerCommand` 셸 확장)
- [ ] 미리보기 (압축 내부 텍스트/이미지)
- [ ] ALZ / EGG 지원
- [ ] 분할 압축
- [ ] 다크 모드
