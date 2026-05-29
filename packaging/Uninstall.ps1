# Iljip 셸 확장 제거
[CmdletBinding()]
param(
    [string]$PackageName = "Iljip.ShellExtension"
)

$ErrorActionPreference = "Stop"

$pkg = Get-AppxPackage -Name $PackageName -ErrorAction SilentlyContinue
if (-not $pkg) {
    Write-Host "설치된 패키지가 없어요." -ForegroundColor Yellow
    exit 0
}

Write-Host "==> 패키지 제거: $($pkg.PackageFullName)" -ForegroundColor Cyan
Remove-AppxPackage -Package $pkg.PackageFullName

Write-Host "✓ 제거 완료" -ForegroundColor Green
Write-Host "  탐색기를 재시작하면 메뉴가 완전히 사라져요 (작업 관리자에서 '탐색기' 다시 시작)"
