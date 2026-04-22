#Requires -Version 5.1
# Generates all app image assets: tray icons, UWP tiles, and Microsoft Store listing images.
# Uses the 2x2 window-grid glyph with an accent-colored active tile.
# Run from repo root: powershell -ExecutionPolicy Bypass -File scripts/generate-assets.ps1

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot

$traySizes = @(16, 20, 24, 32, 40, 48, 64, 256)
# Matches OverlayWindow.OverlayColorBgr (0x00FF7800, COLORREF layout 0x00BBGGRR -> RGB(0,120,255)).
$accent = [System.Drawing.Color]::FromArgb(255, 0, 120, 255)

$trayVariants = @{
    'dark'  = [System.Drawing.Color]::FromArgb(240, 245, 245, 245)  # bright tiles for dark taskbar
    'light' = [System.Drawing.Color]::FromArgb(235,  60,  60,  60)  # dark tiles for light taskbar
}

function Draw-Grid([System.Drawing.Graphics]$g, [int]$x, [int]$y, [int]$size,
                   [System.Drawing.Color]$tileColor, [System.Drawing.Color]$accentColor) {
    $pad     = [int][math]::Max(1, [math]::Floor($size / 16))
    $gap     = [int][math]::Max(1, [math]::Floor($size / 16))
    $colW    = [int][math]::Floor(($size - 2 * $pad - 2 * $gap) / 3)
    $colH    = $size - 2 * $pad

    $accentBrush = New-Object System.Drawing.SolidBrush($accentColor)
    $tileBrush   = New-Object System.Drawing.SolidBrush($tileColor)

    $g.FillRectangle($accentBrush, $x + $pad,                          $y + $pad, $colW, $colH)
    $g.FillRectangle($tileBrush,   $x + $pad + $colW + $gap,           $y + $pad, $colW, $colH)
    $g.FillRectangle($tileBrush,   $x + $pad + 2 * ($colW + $gap),     $y + $pad, $colW, $colH)

    $accentBrush.Dispose(); $tileBrush.Dispose()
}

function Build-Ico([int[]]$sizes, [System.Drawing.Color]$tileColor, [System.Drawing.Color]$accentColor) {
    $pngs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::Transparent)
        Draw-Grid $g 0 0 $size $tileColor $accentColor
        $g.Dispose()

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngs.Add($ms.ToArray())
        $ms.Dispose()
    }

    $ico = New-Object System.IO.MemoryStream
    $bw  = New-Object System.IO.BinaryWriter($ico)
    $bw.Write([uint16]0)            # reserved
    $bw.Write([uint16]1)            # type = ICO
    $bw.Write([uint16]$sizes.Count) # image count

    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]
        $dim = if ($sz -ge 256) { 0 } else { $sz }
        $bw.Write([byte]$dim)
        $bw.Write([byte]$dim)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$pngs[$i].Length)
        $bw.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($png in $pngs) { $bw.Write($png) }
    $bw.Flush()
    return ,$ico.ToArray()
}

function Build-AssetPng([int]$width, [int]$height, [System.Drawing.Color]$tileColor, [System.Drawing.Color]$accentColor) {
    $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

    $gridSize = [int][math]::Min($width, $height)
    $ox = [int][math]::Floor(($width  - $gridSize) / 2)
    $oy = [int][math]::Floor(($height - $gridSize) / 2)
    Draw-Grid $g $ox $oy $gridSize $tileColor $accentColor
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

function Build-StoreImage([int]$width, [int]$height, [double]$logoRatio, [bool]$showText) {
    $bgColor   = [System.Drawing.Color]::FromArgb(255, 20, 20, 36)
    $tileColor = [System.Drawing.Color]::White

    $bmp = New-Object System.Drawing.Bitmap($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($bgColor)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::None

    $gridSize = [int]([math]::Min($width, $height) * $logoRatio)

    if ($showText) {
        $fontSize = [int][math]::Max(14, $gridSize * 0.22)
        $font = New-Object System.Drawing.Font('Segoe UI Semibold', $fontSize, [System.Drawing.FontStyle]::Regular)
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
        $textSize = $g.MeasureString('Fenstr', $font)
        $textGap  = [int]($gridSize * 0.18)
        $totalH   = $gridSize + $textGap + $textSize.Height

        $ox = [int][math]::Floor(($width  - $gridSize) / 2)
        $oy = [int][math]::Floor(($height - $totalH) / 2)
        Draw-Grid $g $ox $oy $gridSize $tileColor $accent

        $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $g.DrawString('Fenstr', $font, $textBrush, [float]($width / 2), [float]($oy + $gridSize + $textGap), $sf)
        $font.Dispose(); $textBrush.Dispose(); $sf.Dispose()
    } else {
        $ox = [int][math]::Floor(($width  - $gridSize) / 2)
        $oy = [int][math]::Floor(($height - $gridSize) / 2)
        Draw-Grid $g $ox $oy $gridSize $tileColor $accent
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    $ms.Dispose()
    return ,$bytes
}

# ---------------------------------------------------------------------------
# Tray icons
# ---------------------------------------------------------------------------
foreach ($name in $trayVariants.Keys) {
    $bytes = Build-Ico -sizes $traySizes -tileColor $trayVariants[$name] -accentColor $accent
    $outPath = Join-Path $repoRoot "assets\tray-$name.ico"
    [System.IO.File]::WriteAllBytes($outPath, $bytes)
    Write-Host "Wrote $outPath ($($bytes.Length) bytes)"
}

# ---------------------------------------------------------------------------
# UWP app assets (transparent background)
# ---------------------------------------------------------------------------
$uwpTileColor = [System.Drawing.Color]::FromArgb(255, 200, 200, 200)

$uwpAssets = @(
    @{ Name = 'Square44x44Logo.scale-200.png';                       Width =  88; Height =  88 }
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png';   Width =  24; Height =  24 }
    @{ Name = 'Square150x150Logo.scale-200.png';                      Width = 300; Height = 300 }
    @{ Name = 'LockScreenLogo.scale-200.png';                         Width =  48; Height =  48 }
    @{ Name = 'StoreLogo.png';                                        Width =  50; Height =  50 }
    @{ Name = 'SplashScreen.scale-200.png';                           Width = 1240; Height = 600 }
    @{ Name = 'Wide310x150Logo.scale-200.png';                        Width = 620; Height = 300 }
)

foreach ($asset in $uwpAssets) {
    $bytes = Build-AssetPng $asset.Width $asset.Height $uwpTileColor $accent
    $outPath = Join-Path $repoRoot "Assets\$($asset.Name)"
    [System.IO.File]::WriteAllBytes($outPath, $bytes)
    Write-Host "Wrote $outPath ($($bytes.Length) bytes)"
}

# ---------------------------------------------------------------------------
# Microsoft Store listing images (dark background + white logo)
# ---------------------------------------------------------------------------
$storeDir = Join-Path $repoRoot 'store'
if (-not (Test-Path $storeDir)) { New-Item -ItemType Directory -Path $storeDir | Out-Null }

$storeAssets = @(
    # Store logos (high-res only)
    @{ Name = 'PosterArt_1440x2160.png';   Width = 1440; Height = 2160; Ratio = 0.45; Text = $true  }
    @{ Name = 'BoxArt_2160x2160.png';       Width = 2160; Height = 2160; Ratio = 0.45; Text = $true  }
    # Store display images
    @{ Name = 'AppTileIcon_300x300.png';    Width =  300; Height =  300; Ratio = 0.60; Text = $false }
    @{ Name = 'AppTileIcon_150x150.png';    Width =  150; Height =  150; Ratio = 0.60; Text = $false }
    @{ Name = 'AppTileIcon_71x71.png';      Width =   71; Height =   71; Ratio = 0.65; Text = $false }
)

foreach ($asset in $storeAssets) {
    $bytes = Build-StoreImage $asset.Width $asset.Height $asset.Ratio $asset.Text
    $outPath = Join-Path $storeDir $asset.Name
    [System.IO.File]::WriteAllBytes($outPath, $bytes)
    Write-Host "Wrote $outPath ($($bytes.Length) bytes)"
}

Write-Host "`nDone. Generated all assets."
