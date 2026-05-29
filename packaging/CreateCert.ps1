# 자체 서명 인증서 생성 + 신뢰 저장소 등록
# **관리자 권한 PowerShell**로 실행해주세요.
#
# 생성 결과:
#   - Cert:\CurrentUser\My 에 인증서
#   - cert\Iljip.pfx 로 내보내기 (Build.ps1이 사용)
#   - Cert:\LocalMachine\TrustedPeople 에 등록 (MSIX 신뢰)
[CmdletBinding()]
param(
    [string]$Subject = "CN=Iljip",
    [string]$Password = "Iljip2026!",
    [string]$OutputDir = "$PSScriptRoot\cert"
)

$ErrorActionPreference = "Stop"

# 관리자 권한 확인
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "이 스크립트는 관리자 권한으로 실행해야 해요. PowerShell을 '관리자 권한으로 실행' 해주세요."
    exit 1
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host "==> 자체 서명 인증서 생성: $Subject" -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -FriendlyName "Iljip 셸 확장 인증서" `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

Write-Host "  Thumbprint: $($cert.Thumbprint)"

Write-Host "==> PFX 파일로 내보내기" -ForegroundColor Cyan
$pfxPath = Join-Path $OutputDir "Iljip.pfx"
$securePwd = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null

Write-Host "==> 신뢰 저장소에 등록 (LocalMachine\TrustedPeople)" -ForegroundColor Cyan
$store = New-Object System.Security.Cryptography.X509Certificates.X509Store("TrustedPeople", "LocalMachine")
$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$store.Add($cert)
$store.Close()

Write-Host ""
Write-Host "✓ 인증서 준비 완료" -ForegroundColor Green
Write-Host "  PFX: $pfxPath"
Write-Host "  비밀번호: $Password (Build.ps1에서 자동 사용)"
