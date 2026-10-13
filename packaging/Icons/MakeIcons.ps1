# 일집 형식 아이콘 만들기(W2-12 — 파일 연결 I1-1의 DefaultIcon '<설치 폴더>\icons\<ext>.ico')
#
# exe 리소스에 넣지 않고 .ico 파일로 둔다(rc 컴파일러 없이 바꿀 수 있게). 이미 만든 결과는 저장소의 Iljip\icons\*.ico 에 있다 —
# 모양을 바꿀 때만 다시 돌린다(설치·등록과 무관, 파일만 만듦):
#   powershell -ExecutionPolicy Bypass -File D:\Python\일집\packaging\Icons\MakeIcons.ps1
# 크기 16·24·32·48·256 을 PNG 로 담은 ICO(Windows Vista 이후 모두 읽음). 글꼴은 Segoe UI(없으면 Arial).
[CmdletBinding()]
param(
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
if (-not $OutDir) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $OutDir = Join-Path $here "..\..\Iljip\icons"
}
Add-Type -AssemblyName System.Drawing

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# 확장자 → 색(형식 묶음별)
$groups = @(
    @{ Color = "#2F6FD6"; Exts = @("zip", "zipx", "z01") },
    @{ Color = "#1E8C7E"; Exts = @("7z", "001") },
    @{ Color = "#7B4BB7"; Exts = @("rar") },
    @{ Color = "#C46A1C"; Exts = @("tar", "gz", "tgz", "bz2", "tbz2", "tbz", "xz", "txz") },
    @{ Color = "#5A6370"; Exts = @("iso", "wim") },
    @{ Color = "#4E8A2E"; Exts = @("cab", "lzh", "lha") },
    @{ Color = "#C8463D"; Exts = @("egg", "alz") }
)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconPng([int]$size, [string]$label, [string]$hex) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)
    $color = [System.Drawing.ColorTranslator]::FromHtml($hex)
    $m = [Math]::Max(1, [int]($size * 0.06))
    $body = New-RoundedPath $m ($m) ($size - 2 * $m) ($size - 2 * $m) ([Math]::Max(2, $size * 0.14))
    $g.FillPath((New-Object System.Drawing.SolidBrush($color)), $body)
    # 지퍼 줄(위쪽 가운데) — 압축 파일 느낌
    $zipW = [Math]::Max(2, [int]($size * 0.10))
    $zx = [int](($size - $zipW) / 2)
    $light = [System.Drawing.Color]::FromArgb(150, 255, 255, 255)
    $step = [Math]::Max(2, [int]($size * 0.07))
    for ($yy = $m; $yy -lt $size * 0.45; $yy += $step * 2) {
        $g.FillRectangle((New-Object System.Drawing.SolidBrush($light)), $zx, $yy, $zipW, $step)
    }
    if ($size -ge 24) {
        $fontName = "Segoe UI"
        $len = [Math]::Max(2, $label.Length)
        $em = $size * 0.62 / $len * 1.25
        $em = [Math]::Min($em, $size * 0.34)
        $font = New-Object System.Drawing.Font($fontName, [float]$em, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = [System.Drawing.StringAlignment]::Center
        $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF(0, ($size * 0.42), $size, ($size * 0.5))
        $g.DrawString($label, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
        $font.Dispose()
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

function Write-Ico([string]$path, [string]$label, [string]$hex) {
    $sizes = @(16, 24, 32, 48, 256)
    $images = @()
    foreach ($s in $sizes) { $images += , (New-IconPng $s $label $hex) }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter($fs)
    $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([UInt16]1); $bw.Write([UInt16]32)
        $bw.Write([UInt32]$images[$i].Length); $bw.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($img in $images) { $bw.Write($img) }
    $bw.Flush(); $bw.Dispose(); $fs.Dispose()
}

foreach ($grp in $groups) {
    foreach ($ext in $grp.Exts) {
        $file = Join-Path $OutDir "$ext.ico"
        Write-Ico $file $ext.ToUpperInvariant() $grp.Color
        Write-Host "  $file"
    }
}
# 일집 자체(셸 메뉴 '일집 ▶' 아이콘)
Write-Ico (Join-Path $OutDir "iljip.ico") "일집" "#2F6FD6"
Write-Host "  $(Join-Path $OutDir 'iljip.ico')"
Write-Host "아이콘을 만들었어요: $OutDir" -ForegroundColor Green
