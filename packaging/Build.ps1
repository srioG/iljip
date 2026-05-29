# Iljip MSIX Sparse Package 빌드 스크립트
#
# 동작:
#   1. Iljip (WPF) 를 Release / win-x64 로 publish
#   2. Iljip.ShellExtension (C++ DLL) 빌드
#   3. 결과를 $BuildDir\package\ 로 모음
#   4. Package.appxmanifest 를 AppxManifest.xml 이름으로 복사
#   5. MakeAppx.exe 로 .msix 생성
#   6. SignTool.exe 로 자체 서명 인증서로 서명
#
# 빌드 산출물은 OneDrive 동기화 충돌 회피를 위해 외부 폴더(기본: D:\Python\일집-build)에 저장.
# 환경 변수 IljipBuildDir 또는 -BuildDir 파라미터로 변경 가능.
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$BuildDir = $(if ($env:IljipBuildDir) { $env:IljipBuildDir } else { "D:\Python\일집-build" }),
    [string]$CertPath = "$PSScriptRoot\cert\Iljip.pfx",
    [string]$CertPassword = "Iljip2026!"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

$packageDir = Join-Path $BuildDir "package"
$objDir = Join-Path $BuildDir "obj"
$msixPath = Join-Path $BuildDir "Iljip.msix"

Write-Host "==> 빌드 폴더: $BuildDir" -ForegroundColor Cyan
if (Test-Path $packageDir) { Remove-Item -Recurse -Force $packageDir }
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
New-Item -ItemType Directory -Force -Path $objDir | Out-Null

Write-Host "==> Iljip (WPF) publish" -ForegroundColor Cyan
& dotnet publish "$root\Iljip\Iljip.csproj" `
    -c $Configuration `
    -r win-$($Platform.ToLower()) `
    --self-contained false `
    -p:PublishSingleFile=false `
    -o "$packageDir"
if ($LASTEXITCODE -ne 0) { throw "Iljip publish 실패" }

Write-Host "==> Iljip.ShellExtension (C++) 빌드" -ForegroundColor Cyan
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vsWhere)) {
    throw "vswhere를 찾을 수 없어요. Visual Studio 2022 또는 Build Tools가 설치되어 있나요?`n   → https://visualstudio.microsoft.com/downloads/"
}

# 1차: C++ 워크로드 + MSBuild 조건으로 탐색
$msbuild = & $vsWhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1

# 2차: 조건 느슨하게
if (-not $msbuild) {
    $msbuild = & $vsWhere -latest -products * -find "MSBuild\**\Bin\MSBuild.exe" 2>$null | Select-Object -First 1
}

# 3차: 알려진 경로 직접 탐색
if (-not $msbuild) {
    $candidates = @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )
    $msbuild = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $msbuild) {
    Write-Host ""
    Write-Host "MSBuild(C++ 빌드용)를 찾을 수 없어요." -ForegroundColor Red
    Write-Host "Visual Studio Installer를 열고 'C++를 사용한 데스크톱 개발' 워크로드를 추가해주세요." -ForegroundColor Yellow
    throw "MSBuild를 찾을 수 없어요."
}

Write-Host "  MSBuild: $msbuild" -ForegroundColor DarkGray

# C++ 빌드 산출물을 외부 폴더로 보냄 (/p:OutDir, /p:IntDir)
$cppOut = Join-Path $BuildDir "$Configuration\$Platform\Iljip.ShellExtension"
$cppInt = Join-Path $objDir "$Configuration\$Platform\Iljip.ShellExtension"

& $msbuild "$root\Iljip.ShellExtension\Iljip.ShellExtension.vcxproj" `
    /p:Configuration=$Configuration `
    /p:Platform=$Platform `
    "/p:OutDir=$cppOut\" `
    "/p:IntDir=$cppInt\" `
    /m
if ($LASTEXITCODE -ne 0) { throw "ShellExtension 빌드 실패" }

# DLL 재귀 탐색 (위치가 어디든 잡힘)
$dllSrc = Get-ChildItem -Path $BuildDir -Recurse -Filter "Iljip.ShellExtension.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch "\\obj\\" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $dllSrc) { throw "빌드된 Iljip.ShellExtension.dll을 찾을 수 없어요." }
Write-Host "  DLL: $dllSrc" -ForegroundColor DarkGray
Copy-Item $dllSrc -Destination $packageDir -Force

Write-Host "==> manifest + Assets 복사" -ForegroundColor Cyan
# MakeAppx 는 정확히 'AppxManifest.xml' 이름을 요구함
Copy-Item "$PSScriptRoot\Package.appxmanifest" -Destination (Join-Path $packageDir "AppxManifest.xml") -Force

$assetsDir = Join-Path $packageDir "Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
if (Test-Path "$PSScriptRoot\Assets") {
    Copy-Item "$PSScriptRoot\Assets\*" -Destination $assetsDir -Recurse -Force
} else {
    Write-Warning "Assets 폴더가 없어요."
}

Write-Host "==> MakeAppx.exe로 .msix 패키징" -ForegroundColor Cyan
$sdkRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
$makeAppx = Get-ChildItem "$sdkRoot\*\x64\MakeAppx.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeAppx) { throw "MakeAppx.exe를 찾을 수 없어요. Windows SDK가 설치되어 있나요?" }

& $makeAppx pack /d $packageDir /p $msixPath /nv /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx 실패" }

Write-Host "==> SignTool로 서명" -ForegroundColor Cyan
$signTool = Get-ChildItem "$sdkRoot\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signTool) { throw "SignTool.exe를 찾을 수 없어요." }

if (-not (Test-Path $CertPath)) {
    throw "인증서 파일이 없어요: $CertPath`n   먼저 packaging\CreateCert.ps1을 관리자 권한으로 실행해주세요."
}

& $signTool sign /fd SHA256 /a /f $CertPath /p $CertPassword $msixPath
if ($LASTEXITCODE -ne 0) { throw "SignTool 실패" }

Write-Host ""
Write-Host "✓ 빌드 완료: $msixPath" -ForegroundColor Green
Write-Host "  외부 위치: $packageDir"
Write-Host "  → 설치하려면 packaging\Install.ps1 실행"
