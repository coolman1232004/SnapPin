param(
    [string]$OutputIco = (Join-Path $PSScriptRoot '..\Assets\SnapPin.ico'),
    [string]$PreviewPng = (Join-Path $PSScriptRoot '..\Assets\SnapPin-tray-icon-preview.png'),
    [string]$DashboardPng = (Join-Path $PSScriptRoot '..\Assets\SnapPin-icon-512.png')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius) {
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-SnapPinPng([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $inset = [Math]::Max(1.0, $size * 0.045)
    $tilePath = New-RoundedRectanglePath $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) ($size * 0.19)
    $tileBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new(0, 0),
        [System.Drawing.PointF]::new($size, $size),
        [System.Drawing.ColorTranslator]::FromHtml('#41BBAC'),
        [System.Drawing.ColorTranslator]::FromHtml('#2C978E'))
    $graphics.FillPath($tileBrush, $tilePath)

    $stroke = [Math]::Max(1.7, $size * 0.09)
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, $stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Square
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Square
    $near = [single]($size * 0.27)
    $far = [single]($size * 0.43)
    $oppositeNear = [single]($size - $near)
    $oppositeFar = [single]($size - $far)
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($far, $near), [System.Drawing.PointF]::new($near, $near), [System.Drawing.PointF]::new($near, $far)))
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($oppositeFar, $near), [System.Drawing.PointF]::new($oppositeNear, $near), [System.Drawing.PointF]::new($oppositeNear, $far)))
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($near, $oppositeFar), [System.Drawing.PointF]::new($near, $oppositeNear), [System.Drawing.PointF]::new($far, $oppositeNear)))
    $graphics.DrawLines($pen, [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($oppositeNear, $oppositeFar), [System.Drawing.PointF]::new($oppositeNear, $oppositeNear), [System.Drawing.PointF]::new($oppositeFar, $oppositeNear)))

    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $pen.Dispose()
    $tileBrush.Dispose()
    $tilePath.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
    return $bytes
}

function New-SnapPinDib([int]$size) {
    $pngBytes = New-SnapPinPng $size
    $pngStream = [System.IO.MemoryStream]::new($pngBytes)
    $bitmap = [System.Drawing.Bitmap]::FromStream($pngStream)
    $dibStream = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($dibStream)

    # BITMAPINFOHEADER. ICO stores the XOR and one-bit AND masks vertically,
    # so the declared bitmap height is twice the visible icon height.
    $writer.Write([uint32]40)
    $writer.Write([int32]$size)
    $writer.Write([int32]($size * 2))
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]0)
    $writer.Write([uint32]($size * $size * 4))
    $writer.Write([int32]0)
    $writer.Write([int32]0)
    $writer.Write([uint32]0)
    $writer.Write([uint32]0)

    # Device-independent bitmap pixels are BGRA and bottom-up.
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $writer.Write([byte]$pixel.B)
            $writer.Write([byte]$pixel.G)
            $writer.Write([byte]$pixel.R)
            $writer.Write([byte]$pixel.A)
        }
    }

    # Alpha carries transparency; an all-zero AND mask keeps modern and
    # legacy Windows icon readers consistent. Rows are padded to 32 bits.
    $andRowBytes = [int]([Math]::Ceiling($size / 32.0) * 4)
    $writer.Write([byte[]]::new($andRowBytes * $size))
    $writer.Flush()
    $bytes = $dibStream.ToArray()
    $writer.Dispose()
    $dibStream.Dispose()
    $bitmap.Dispose()
    $pngStream.Dispose()
    return $bytes
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) { $images.Add((New-SnapPinDib $size)) }
$outputDirectory = Split-Path -Parent $OutputIco
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

$stream = [System.IO.File]::Create($OutputIco)
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$index].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$index].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Dispose()
$stream.Dispose()

[System.IO.File]::WriteAllBytes($PreviewPng, (New-SnapPinPng 256))
[System.IO.File]::WriteAllBytes($DashboardPng, (New-SnapPinPng 512))
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $DashboardPng) 'SnapPin-icon.png'), (New-SnapPinPng 512))
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $DashboardPng) 'SnapPin-icon-source.png'), (New-SnapPinPng 512))
[System.IO.File]::WriteAllBytes((Join-Path (Split-Path -Parent $DashboardPng) 'SnapPin-exe-icon-preview.png'), (New-SnapPinPng 32))
Write-Output "Generated synchronized SnapPin ICO, tray preview, dashboard, and source icons"
