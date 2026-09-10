#Requires -Version 5.1
<#
    生成安装器的 assets\icon.ico（多尺寸 PNG 编码 ICO）。
    不依赖任何第三方工具，纯 System.Drawing 绘制。
#>
[CmdletBinding()]
param(
    [string]$Output
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($Output)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Output = Join-Path $repoRoot 'assets\icon.ico'
}

$outDir = Split-Path -Parent $Output
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

function New-RoundedPath {
    param(
        [System.Drawing.RectangleF]$Rect,
        [single]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($Radius * 2)
    if ($d -gt $Rect.Width) { $d = $Rect.Width }
    if ($d -gt $Rect.Height) { $d = $Rect.Height }

    if ($d -lt 2) {
        $path.AddRectangle($Rect)
        return $path
    }

    $path.AddArc($Rect.X, $Rect.Y, $d, $d, 180, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Y, $d, $d, 270, 90)
    $path.AddArc($Rect.Right - $d, $Rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    try {
        $k = $Size / 256.0

        # ---- 背景：圆角渐变方块 ----
        $pad = [single](10 * $k)
        $rect = New-Object System.Drawing.RectangleF($pad, $pad, [single]($Size - 2 * $pad), [single]($Size - 2 * $pad))
        $bgPath = New-RoundedPath -Rect $rect -Radius ([single](62 * $k))

        $c1 = [System.Drawing.Color]::FromArgb(255, 96, 132, 255)
        $c2 = [System.Drawing.Color]::FromArgb(255, 168, 92, 255)
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 50.0)
        $g.FillPath($brush, $bgPath)
        $brush.Dispose()
        $bgPath.Dispose()

        # ---- 前景：像素网格（小尺寸退化为一个方块）----
        if ($Size -lt 48) {
            $cell = [single]($Size * 0.42)
            $r = New-Object System.Drawing.RectangleF(([single](($Size - $cell) / 2)), ([single](($Size - $cell) / 2)), $cell, $cell)
            $p = New-RoundedPath -Rect $r -Radius ([single](4 * $k))
            $b = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(245, 255, 255, 255))
            $g.FillPath($b, $p)
            $b.Dispose()
            $p.Dispose()
        }
        else {
            $gap = [single](14 * $k)
            $cell = [single](([single](168 * $k)))
            $grid = [single]($cell * 3 + $gap * 2)
            $ox = [single](($Size - $grid) / 2)
            $oy = [single](($Size - $grid) / 2)

            # 模拟「逐步上色」的疏密效果
            $alpha = @(255, 190, 140, 215, 255, 175, 130, 200, 250)

            $i = 0
            for ($row = 0; $row -lt 3; $row++) {
                for ($col = 0; $col -lt 3; $col++) {
                    $a = $alpha[$i]
                    $i++

                    $x = [single]($ox + $col * ($cell + $gap))
                    $y = [single]($oy + $row * ($cell + $gap))
                    $r = New-Object System.Drawing.RectangleF($x, $y, $cell, $cell)
                    $p = New-RoundedPath -Rect $r -Radius ([single](20 * $k))

                    $b = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb($a, 255, 255, 255))
                    $g.FillPath($b, $p)
                    $b.Dispose()
                    $p.Dispose()
                }
            }
        }
    }
    finally {
        $g.Dispose()
    }

    return $bmp
}

# ---- 渲染各尺寸并编码为 PNG ----
$sizes = @(16, 32, 48, 256)
$blobs = New-Object System.Collections.ArrayList

foreach ($s in $sizes) {
    $bmp = New-IconBitmap -Size $s
    $ms = New-Object System.IO.MemoryStream
    try {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        [void]$blobs.Add($ms.ToArray())
    }
    finally {
        $ms.Dispose()
        $bmp.Dispose()
    }
}

# ---- 组装 ICO ----
$fs = [System.IO.File]::Open($Output, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([UInt16]0)          # reserved
    $bw.Write([UInt16]1)          # type = icon
    $bw.Write([UInt16]$sizes.Count)

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]
        $wh = 0
        if ($s -lt 256) { $wh = $s }

        $bw.Write([Byte]$wh)
        $bw.Write([Byte]$wh)
        $bw.Write([Byte]0)         # color count
        $bw.Write([Byte]0)         # reserved
        $bw.Write([UInt16]1)       # planes
        $bw.Write([UInt16]32)      # bpp
        $bw.Write([UInt32]$blobs[$i].Length)
        $bw.Write([UInt32]$offset)

        $offset += $blobs[$i].Length
    }

    foreach ($blob in $blobs) { $bw.Write($blob) }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

$info = Get-Item $Output
Write-Host ("  OK 已生成图标 {0} ({1} 字节)" -f $info.FullName, $info.Length) -ForegroundColor Green
