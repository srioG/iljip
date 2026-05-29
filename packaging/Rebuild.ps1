# 셸 확장 풀 재배포 (제거 → [Clean] → 빌드 → 설치 → 탐색기 재시작)
#
# 사용:
#   .\Rebuild.ps1              # 일반 재배포
#   .\Rebuild.ps1 -Clean       # 빌드 캐시 전부 비우고 클린 빌드
#   .\Rebuild.ps1 -SkipExplorerRestart   # 탐색기 재시작 생략
[CmdletBinding()]
param(
    [switch]$SkipExplorerRestart,
    [switch]$Clean,
    [string]$BuildDir = $(if ($env:IljipBuildDir) { $env:IljipBuildDir } else { "D:\Python\일집-build" })
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

Write-Host "==> 1/5 기존 패키지 제거" -ForegroundColor Cyan
& "$PSScriptRoot\Uninstall.ps1"

if ($Clean) {
    Write-Host ""
    Write-Host "==> 2/5 클린 (빌드 캐시 전부 삭제)" -ForegroundColor Cyan

    $toRemove = @(
        $BuildDir,
        (Join-Path $root "Iljip\bin"),
        (Join-Path $root "Iljip\obj"),
        (Join-Path $root "Iljip.ShellExtension\build"),
        (Join-Path $root "Iljip.ShellExtension\x64"),
        (Join-Path $root "build")
    )
    foreach ($p in $toRemove) {
        if (Test-Path $p) {
            try {
                Remove-Item -Recurse -Force $p -ErrorAction Stop
                Write-Host "  ✓ 삭제: $p" -ForegroundColor DarkGray
            } catch {
                Write-Warning "  ! 삭제 실패(무시): $p — $($_.Exception.Message)"
            }
        }
    }
} else {
    Write-Host ""
    Write-Host "==> 2/5 (Clean 건너뜀; -Clean 옵션으로 강제 가능)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "==> 3/5 빌드" -ForegroundColor Cyan
& "$PSScriptRoot\Build.ps1" -BuildDir $BuildDir
if ($LASTEXITCODE -ne 0 -and -not $?) { throw "빌드 실패 — 중단" }

Write-Host ""
Write-Host "==> 4/5 설치" -ForegroundColor Cyan
& "$PSScriptRoot\Install.ps1" -BuildDir $BuildDir
if ($LASTEXITCODE -ne 0 -and -not $?) { throw "설치 실패 — 중단" }

if (-not $SkipExplorerRestart) {
    Write-Host ""
    Write-Host "==> 5/5 탐색기 재시작 (셸 확장 DLL 캐시 해제)" -ForegroundColor Cyan
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    Start-Process explorer
    Write-Host "  ✓ 탐색기 재시작됨"
} else {
    Write-Host ""
    Write-Host "(탐색기 재시작 건너뜀: -SkipExplorerRestart)" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "✓ 완료. 탐색기에서 .zip 파일 우클릭해서 확인해줘." -ForegroundColor Green
