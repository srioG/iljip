# Iljip MSIX Sparse Package 설치
#
#   .\Install.ps1                 # Windows 의 '새 앱 저장 위치'(기본 볼륨)에 설치
#   .\Install.ps1 -SystemVolume   # C:(시스템 볼륨)에 설치 — 새 앱 저장 위치를 D: 로 바꿔 둔 PC 에서 0x80070032 로 실패할 때
#                                 # (3차 통합 점검 2026-10-15: 이 PC 는 기본 볼륨이 D:\WindowsApps 이고 OneDrive 패키지가 같은 오류로 실패 중)
[CmdletBinding()]
param(
    [string]$BuildDir = $(if ($env:IljipBuildDir) { $env:IljipBuildDir } else { "D:\Python\일집-build" }),
    [string]$MsixPath = "",
    [string]$ExternalLocation = "",
    [switch]$SystemVolume
)

$ErrorActionPreference = "Stop"

if (-not $MsixPath) { $MsixPath = Join-Path $BuildDir "Iljip.msix" }
if (-not $ExternalLocation) { $ExternalLocation = Join-Path $BuildDir "package" }

if (-not (Test-Path $MsixPath)) {
    throw "MSIX 파일이 없어요: $MsixPath`n   먼저 Build.ps1을 실행해주세요."
}
if (-not (Test-Path $ExternalLocation)) {
    throw "외부 위치 폴더가 없어요: $ExternalLocation"
}

$MsixPath = Resolve-Path $MsixPath
$ExternalLocation = Resolve-Path $ExternalLocation

Write-Host "==> Sparse Package 설치" -ForegroundColor Cyan
Write-Host "  MSIX: $MsixPath"
Write-Host "  외부 위치: $ExternalLocation"

$defaultVolume = $null
try { $defaultVolume = Get-AppxDefaultVolume } catch { }
if ($defaultVolume) { Write-Host "  앱 기본 설치 위치: $($defaultVolume.PackageStorePath)" }

$addArgs = @{ Path = $MsixPath; ExternalLocation = $ExternalLocation; AllowUnsigned = $false }
if ($SystemVolume) {
    $systemVolume = Get-AppxVolume | Where-Object { $_.IsSystemVolume } | Select-Object -First 1
    if (-not $systemVolume) { throw "시스템 볼륨(C:)의 앱 저장소를 찾지 못했어요(Get-AppxVolume)." }
    Write-Host "  설치할 곳: $($systemVolume.PackageStorePath) (-SystemVolume)"
    $addArgs.Volume = $systemVolume
}

try {
    Add-AppxPackage @addArgs
} catch {
    $text = "$($_.Exception.Message)"
    if (-not $SystemVolume -and ($text -match '0x80070032' -or $_.Exception.HResult -eq -2147024846)) {
        throw ("설치하지 못했어요(0x80070032). 이 PC 는 새 앱 저장 위치가 C: 가 아니라서(" + $(if ($defaultVolume) { $defaultVolume.PackageStorePath } else { "알 수 없음" }) +
               ") 셸 확장 패키지를 넣지 못한 것 같아요.`n   .\Rebuild.ps1 -SystemVolume (또는 .\Install.ps1 -SystemVolume) 로 다시 해 보세요.`n   원래 오류: $text")
    }
    throw
}

Write-Host ""
Write-Host "✓ 설치 완료" -ForegroundColor Green
Write-Host "  탐색기에서 .zip 파일을 우클릭해 '일집' 메뉴를 확인해주세요."
