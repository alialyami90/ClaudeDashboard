# Draws assets\ClaudeDashboard.ico: a Midnight tile with a Lime race car, head-on with its headlights on, in front of a
# Glacier pit garage. The car and garage are the app's own pixel art (PitArt and PitRaster in src\widgets\PitArt.cs,
# decision 017), loaded from the built exe, so the icon always matches the dashboard. Build first (scripts\build.ps1),
# then run with -STA.
param([string]$PreviewDirectory, [string]$AppDirectory = 'artifacts\app')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path (Join-Path $projectRoot $AppDirectory) 'ClaudeDashboard.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw "$appPath is missing. Run scripts\build.ps1 first." }
$app = [System.Reflection.Assembly]::LoadFrom($appPath)
# The types by name in the app's namespace (ClaudeDashboard since v0.47.3).
function AppType([string]$name) {
    $type = @($app.GetTypes() | Where-Object { $_.Name -eq $name -and $_.Namespace -eq 'ClaudeDashboard' }) | Select-Object -First 1
    if (-not $type) { throw "$name isn't in $appPath." }
    $type
}
$pitArt = AppType 'PitArt'; $pitRaster = AppType 'PitRaster'; $pitState = AppType 'PitState'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256

function Brush([string]$hex) { $b = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($hex)); $b.Freeze(); $b }
$surface = Brush '#161A22'; $border = Brush '#252B36'
# ARGB colours as the sprites take them (PowerShell reads 0xFF... literals as negative Int32).
$lime = [Convert]::ToUInt32('FFBAF28B', 16); $glacier = [Convert]::ToUInt32('FF8ED8F8', 16)

# A sprite drawn by PitRaster at w × h hard pixels, then repeated k times each way (nearest neighbour), so the pixels
# stay square at every icon size.
function Sprite($ops, [double]$boxWidth, [double]$boxHeight, [int]$w, [int]$h, [int]$k) {
    # Reflection takes the arguments unwrapped: a New-Object result would arrive as a PSObject.
    $arguments = New-Object 'object[]' 4
    $arguments[0] = $ops.psobject.BaseObject; $arguments[1] = [System.Windows.Rect]::new(0, 0, $boxWidth, $boxHeight); $arguments[2] = [int]$w; $arguments[3] = [int]$h
    $bitmap = $pitRaster.GetMethod('Render').Invoke($null, $arguments)
    $small = New-Object byte[] ($w * $h * 4)
    $bitmap.CopyPixels($small, $w * 4, 0)
    $big = New-Object byte[] ($w * $k * $h * $k * 4)
    for ($y = 0; $y -lt $h * $k; $y++) {
        for ($x = 0; $x -lt $w * $k; $x++) {
            [Array]::Copy($small, ([int][Math]::Floor($y / $k) * $w + [int][Math]::Floor($x / $k)) * 4, $big, ($y * $w * $k + $x) * 4, 4)
        }
    }
    [System.Windows.Media.Imaging.BitmapSource]::Create($w * $k, $h * $k, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32, $null, $big, $w * $k * 4)
}

function Draw-Icon([int]$size) {
    # Laid out on a 32-unit grid. The sprites' pixel grid is the icon's own size up to 48, and 32 above that (each
    # pixel then 2, 4 or 8 icon pixels), so large sizes keep the pixel-art look.
    $k = [Math]::Max(1, [Math]::Floor($size / 32)); $grid = $size / $k; $u = $size / 32.0
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $outline = if ($size -ge 48) { New-Object System.Windows.Media.Pen ($border, [Math]::Max(1, $u)) } else { $null }
    $inset = if ($outline) { $outline.Thickness / 2 } else { 0 }
    $dc.DrawRoundedRectangle($surface, $outline, (New-Object System.Windows.Rect ($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))), (7 * $u), (7 * $u))
    # The garage (100 × 100 art, lit with the shutter part open) and, in front, the car seen head-on (80 × 56 art).
    $garage = $pitArt.GetMethod('Garage').Invoke($null, @($glacier, [Enum]::Parse($pitState, 'Done'), $false, 0, 0))
    $car = $pitArt.GetMethod('Front').Invoke($null, @($lime, $true))
    foreach ($part in @(@($garage, 100, 100, 5, 2, 22, 22), @($car, 80, 56, 7, 15, 18, 12.6))) {
        $w = [Math]::Max(1, [int][Math]::Round($part[5] * $grid / 32)); $h = [Math]::Max(1, [int][Math]::Round($part[6] * $grid / 32))
        $image = Sprite $part[0] $part[1] $part[2] $w $h $k
        $dc.DrawImage($image, (New-Object System.Windows.Rect ([Math]::Round($part[3] * $u), [Math]::Round($part[4] * $u), ($w * $k), ($h * $k))))
    }
    $dc.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap ($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

$images = @($sizes | ForEach-Object { , (Draw-Icon $_) })
$icon = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $icon
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32); $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
$iconPath = Join-Path $projectRoot 'assets\ClaudeDashboard.ico'
[System.IO.File]::WriteAllBytes($iconPath, $icon.ToArray())
Write-Output "Icon: $iconPath"
if ($PreviewDirectory) {
    New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
    for ($i = 0; $i -lt $sizes.Count; $i++) { [System.IO.File]::WriteAllBytes((Join-Path $PreviewDirectory "icon-$($sizes[$i]).png"), $images[$i]) }
}
