param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\WidgetCanvas\Assets'),
    [string]$PreviewPath = (Join-Path $PSScriptRoot '..\assets\widgetcanvas.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

$size = 256
$bitmap = [System.Drawing.Bitmap]::new($size, $size)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)

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

Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 14, 24, 40)) 16 16 224 224 54
Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 83, 160, 246)) 43 45 91 91 25
Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 139, 112, 239)) 145 45 68 68 21
Fill-RoundedRectangle $graphics ([System.Drawing.Color]::FromArgb(255, 65, 192, 190)) 61 148 152 63 23
$dotBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235, 244, 248, 255))
$graphics.FillEllipse($dotBrush, 179, 121, 22, 22)
$dotBrush.Dispose()
$graphics.Dispose()

$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$previewPath = [System.IO.Path]::GetFullPath($PreviewPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($previewPath)) | Out-Null
$bitmap.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)

$pngStream = [System.IO.MemoryStream]::new()
$bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $pngStream.ToArray()
$pngStream.Dispose()
$bitmap.Dispose()

$iconPath = Join-Path $outputDirectory 'WidgetCanvas.ico'
$stream = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]1)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([byte]0)
$writer.Write([uint16]1)
$writer.Write([uint16]32)
$writer.Write([uint32]$pngBytes.Length)
$writer.Write([uint32]22)
$writer.Write($pngBytes)
$writer.Dispose()

Write-Output $iconPath
