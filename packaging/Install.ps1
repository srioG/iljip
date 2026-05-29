# Iljip MSIX Sparse Package 설치
[CmdletBinding()]
param(
    [string]$BuildDir = $(if ($env:IljipBuildDir) { $env:IljipBuildDir } else { "D:\Python\일집-build" }),
    [string]$MsixPath = "",
    [string]$ExternalLocation = ""
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

Add-AppxPackage -Path $MsixPath -ExternalLocation $ExternalLocation -AllowUnsigned:$false

Write-Host ""
Write-Host "✓ 설치 완료" -ForegroundColor Green
Write-Host "  탐색기에서 .zip 파일을 우클릭해 '일집' 메뉴를 확인해주세요."
