# Generates the application icon and the installer wizard images. Run with Windows PowerShell 5.1:
#   powershell -ExecutionPolicy Bypass -File tools\MakeAssets.ps1
# Output: GeoGuard\Assets\GeoGuard.ico, installer\WizardLarge.bmp, installer\WizardSmall.bmp, GeoGuard\Assets\Logo.png
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "GeoGuard\Assets"
$installer = Join-Path $root "installer"
New-Item -ItemType Directory -Force $assets, $installer | Out-Null

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# Draws the GeoGuard mark (globe + shield badge) into a square of side $s at offset ($ox,$oy).
function Draw-Mark($g, [single]$ox, [single]$oy, [single]$s, [bool]$withBackground) {
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'

    if ($withBackground) {
        $path = New-RoundedPath $ox $oy $s $s ($s * 0.22)
        $rect = New-Object System.Drawing.RectangleF($ox, $oy, $s, $s)
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(59, 130, 246), [System.Drawing.Color]::FromArgb(30, 58, 138), 55)
        $g.FillPath($bg, $path)
        $bg.Dispose(); $path.Dispose()
    }

    $small = $s -lt 28
    $cx = $ox + $s * 0.5
    $cy = $oy + $s * $(if ($small) { 0.5 } else { 0.46 })
    $r = $s * $(if ($small) { 0.34 } else { 0.29 })
    $lw = [Math]::Max(1.0, $s * $(if ($small) { 0.085 } else { 0.05 }))

    $fill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(45, 255, 255, 255))
    $g.FillEllipse($fill, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $lw)
    $g.DrawEllipse($pen, $cx - $r, $cy - $r, 2 * $r, 2 * $r)
    $g.DrawEllipse($pen, $cx - $r * 0.42, $cy - $r, $r * 0.84, 2 * $r)       # meridian
    $g.DrawLine($pen, $cx - $r, $cy, $cx + $r, $cy)                          # equator
    if (-not $small) {
        $thin = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 255, 255, 255), [Math]::Max(1.0, $lw * 0.7))
        $dy = $r * 0.52
        $half = [Math]::Sqrt($r * $r - $dy * $dy)
        $g.DrawLine($thin, $cx - $half, $cy - $dy, $cx + $half, $cy - $dy)    # latitudes
        $g.DrawLine($thin, $cx - $half, $cy + $dy, $cx + $half, $cy + $dy)
        $thin.Dispose()
    }

    if ($s -ge 24) {
        # Shield-style badge with a check mark, bottom-right
        $bx = $ox + $s * 0.70; $by = $oy + $s * 0.72; $br = $s * 0.215
        $ring = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 58, 138))
        $g.FillEllipse($ring, $bx - $br * 1.16, $by - $br * 1.16, $br * 2.32, $br * 2.32)
        $green = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(34, 197, 94))
        $g.FillEllipse($green, $bx - $br, $by - $br, 2 * $br, 2 * $br)
        $tick = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [Math]::Max(1.5, $s * 0.045))
        $tick.StartCap = 'Round'; $tick.EndCap = 'Round'; $tick.LineJoin = 'Round'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($bx - $br * 0.45), ($by + $br * 0.02))),
            (New-Object System.Drawing.PointF(($bx - $br * 0.1), ($by + $br * 0.38))),
            (New-Object System.Drawing.PointF(($bx + $br * 0.5), ($by - $br * 0.32))))
        $g.DrawLines($tick, $pts)
        $ring.Dispose(); $green.Dispose(); $tick.Dispose()
    }
    $fill.Dispose(); $pen.Dispose()
}

function New-MarkBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Mark $g 0 0 $size $true
    $g.Dispose()
    return $bmp
}

# ---- .ico (PNG-compressed frames) ----
$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-MarkBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($s, $ms.ToArray())
}
$icoPath = Join-Path $assets "GeoGuard.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s })); $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $bw.Write($f[1]) }
$bw.Close(); $fs.Close()

# ---- logo for the UI ----
$logo = New-MarkBitmap 256
$logo.Save((Join-Path $assets "Logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()

# ---- installer wizard images ----
$big = New-Object System.Drawing.Bitmap(164, 314, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'ClearTypeGridFit'
$rect = New-Object System.Drawing.Rectangle(0, 0, 164, 314)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, [System.Drawing.Color]::FromArgb(37, 99, 235), [System.Drawing.Color]::FromArgb(15, 31, 77), 90)
$g.FillRectangle($grad, $rect)
Draw-Mark $g 22 52 120 $false
$font = New-Object System.Drawing.Font("Segoe UI Semibold", 17, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'
$g.DrawString("GeoGuard", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 190, 164, 30)), $sf)
$font2 = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 219, 234, 254))
$g.DrawString("country + VPN guard", $font2, $muted, (New-Object System.Drawing.RectangleF(0, 220, 164, 20)), $sf)
$g.Dispose()
$big.Save((Join-Path $installer "WizardLarge.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$big.Dispose()

$smallImg = New-Object System.Drawing.Bitmap(55, 58, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($smallImg)
$g.Clear([System.Drawing.Color]::White)
Draw-Mark $g 4 5 48 $true
$g.Dispose()
$smallImg.Save((Join-Path $installer "WizardSmall.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$smallImg.Dispose()

Get-ChildItem $assets, $installer | Select-Object Name, Length | Format-Table -AutoSize | Out-String
