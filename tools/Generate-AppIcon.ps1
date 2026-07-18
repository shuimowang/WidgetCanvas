param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\WidgetCanvas\Assets'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\assets\widgetcanvas.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedRectangle($graphics, [System.Drawing.Color]$color, [float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = New-RoundedPath $x $y $width $height $radius
    $brush = [System.Drawing.SolidBrush]::new($color)
    $graphics.FillPath($brush, $path)
    $brush.Dispose()
    $path.Dispose()
}

function New-IconBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 256.0
    Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 14, 24, 40)) `
        (16 * $scale) (16 * $scale) (224 * $scale) (224 * $scale) (54 * $scale)
    Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 83, 160, 246)) `
        (43 * $scale) (45 * $scale) (91 * $scale) (91 * $scale) (25 * $scale)
    Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 139, 112, 239)) `
        (145 * $scale) (45 * $scale) (68 * $scale) (68 * $scale) (21 * $scale)
    Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 65, 192, 190)) `
        (61 * $scale) (148 * $scale) (152 * $scale) (63 * $scale) (23 * $scale)

    $dotSize = [Math]::Max(2.0, 22 * $scale)
    $dotBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(245, 244, 248, 255))
    $graphics.FillEllipse($dotBrush, 190 * $scale - $dotSize / 2, 132 * $scale - $dotSize / 2, $dotSize, $dotSize)
    $dotBrush.Dispose()
    $graphics.Dispose()
    return $bitmap
}

$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$previewPath = [System.IO.Path]::GetFullPath($PreviewPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($previewPath)) | Out-Null

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = [System.Collections.Generic.List[object]]::new()
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    if ($size -eq 256) {
        $bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    $pngStream = [System.IO.MemoryStream]::new()
    $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames.Add([PSCustomObject]@{
        Size = $size
        Bytes = $pngStream.ToArray()
    })
    $pngStream.Dispose()
    $bitmap.Dispose()
}

$iconPath = Join-Path $outputDirectory 'WidgetCanvas.ico'
$stream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$frames.Count)

$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) {
    $writer.Write([byte[]]$frame.Bytes)
}
$writer.Dispose()

Write-Output $iconPath
