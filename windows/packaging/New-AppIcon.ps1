[CmdletBinding()]
param(
    [string]$InputPath,
    [string]$OutputDirectory,
    [string]$IcoPath,
    [string]$IcnsPath,
    [string]$MenuBarDuckColorPath,
    [string]$TrayIcoPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$windowsRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $windowsRoot
if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $InputPath = Join-Path $repositoryRoot 'assets\brand-source-icon\icon_source.png'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'assets\brand-source-icon'
}
if ([string]::IsNullOrWhiteSpace($IcoPath)) {
    $IcoPath = Join-Path $windowsRoot 'src\TransDuck.App\Assets\TransDuck.ico'
}
if ([string]::IsNullOrWhiteSpace($IcnsPath)) {
    $IcnsPath = Join-Path $repositoryRoot 'assets\brand-source-icon\TransDuck.icns'
}
if ([string]::IsNullOrWhiteSpace($MenuBarDuckColorPath)) {
    $MenuBarDuckColorPath = Join-Path $repositoryRoot `
        'assets\brand-source-icon\menu_bar_duck_color_46x34.png'
}
if ([string]::IsNullOrWhiteSpace($TrayIcoPath)) {
    $TrayIcoPath = Join-Path $windowsRoot `
        'src\TransDuck.Platform.Windows\Assets\TransDuck.Tray.ico'
}

$sourcePath = [IO.Path]::GetFullPath($InputPath)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$iconPath = [IO.Path]::GetFullPath($IcoPath)
$macIconPath = [IO.Path]::GetFullPath($IcnsPath)
$menuBarDuckIconPath = [IO.Path]::GetFullPath($MenuBarDuckColorPath)
$trayIconPath = [IO.Path]::GetFullPath($TrayIcoPath)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw 'icon_source_missing'
}

Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $iconPath)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $macIconPath)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $menuBarDuckIconPath)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $trayIconPath)) | Out-Null
$pngSizes = @(16, 32, 64, 128, 256, 512, 1024)
$icoSizes = @(16, 32, 64, 128, 256)
$traySizes = @(16, 20, 24, 32)
$source = [Drawing.Image]::FromFile($sourcePath)
try {
    if ($source.Width -ne $source.Height) {
        throw 'icon_source_not_square'
    }

    foreach ($size in $pngSizes) {
        $bitmap = New-Object Drawing.Bitmap(
            $size,
            $size,
            [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            }
            finally {
                $graphics.Dispose()
            }

            $pngPath = Join-Path $outputRoot ("icon_{0}x{0}.png" -f $size)
            $bitmap.Save($pngPath, [Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
    }
}
finally {
    $source.Dispose()
}

# Draw a simplified flat-color front view of the approved duck at the exact 2x
# menu-bar pixel size. The palette is sampled from the source icon; gradients,
# nostrils, the blue background, and translation-panel detail are omitted.
$menuBarDuckBitmap = New-Object Drawing.Bitmap(
    46,
    34,
    [Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    $menuBarDuckGraphics = [Drawing.Graphics]::FromImage($menuBarDuckBitmap)
    try {
        $menuBarDuckGraphics.Clear([Drawing.Color]::Transparent)
        $menuBarDuckGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
        $menuBarDuckGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $menuBarDuckGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $headPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 1, 12, 44), 2)
        $billPen = New-Object Drawing.Pen([Drawing.Color]::FromArgb(255, 205, 69, 1), 1.5)
        $headBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 254, 205, 40))
        $billBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 254, 114, 1))
        $eyeBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 1, 12, 44))
        $highlightBrush = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 252, 252, 253))
        $headPath = New-Object Drawing.Drawing2D.GraphicsPath
        $billPath = New-Object Drawing.Drawing2D.GraphicsPath
        $headFillPath = $null
        try {
            foreach ($pen in @($headPen, $billPen)) {
                $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
            }

            $headPath.StartFigure()
            $headPath.AddBezier(3.6, 28, 3.2, 17, 10, 8.5, 18.5, 6)
            $headPath.AddBezier(18.5, 6, 20, 1.2, 25, 1.2, 26.8, 6.5)
            $headPath.AddBezier(26.8, 6.5, 27, 6.7, 26.9, 6.8, 26.8, 7)
            $headPath.AddBezier(26.8, 7, 29.2, 5, 32, 4.3, 33.5, 5.5)
            $headPath.AddBezier(33.5, 5.5, 35.5, 7, 34.5, 9.5, 32, 10)
            $headPath.AddBezier(32, 10, 39, 13, 42.3, 20.5, 41.9, 28)
            $headFillPath = $headPath.Clone()
            $headFillPath.AddBezier(41.9, 28, 37, 32.3, 8.5, 32.3, 3.6, 28)
            $headFillPath.CloseFigure()
            $menuBarDuckGraphics.FillPath($headBrush, $headFillPath)
            $menuBarDuckGraphics.DrawPath($headPen, $headPath)

            $menuBarDuckGraphics.FillEllipse($eyeBrush, 10.9, 15, 5, 6)
            $menuBarDuckGraphics.FillEllipse($eyeBrush, 29.2, 15, 5, 6)
            $menuBarDuckGraphics.FillEllipse($highlightBrush, 11.8, 16, 1.7, 1.7)
            $menuBarDuckGraphics.FillEllipse($highlightBrush, 30.1, 16, 1.7, 1.7)

            $billPath.StartFigure()
            $billPath.AddBezier(13, 23.5, 17, 23.5, 18, 20.5, 23, 20.5)
            $billPath.AddBezier(23, 20.5, 28, 20.5, 29, 23.5, 33, 23.5)
            $billPath.AddBezier(33, 23.5, 35.3, 23.5, 35.8, 25.5, 34.7, 27.2)
            $billPath.AddBezier(34.7, 27.2, 33.2, 30, 29, 30.8, 23, 30.8)
            $billPath.AddBezier(23, 30.8, 17, 30.8, 12.8, 30, 11.3, 27.2)
            $billPath.AddBezier(11.3, 27.2, 10.2, 25.5, 10.7, 23.5, 13, 23.5)
            $billPath.CloseFigure()
            $menuBarDuckGraphics.FillPath($billBrush, $billPath)
            $menuBarDuckGraphics.DrawPath($billPen, $billPath)
        }
        finally {
            if ($null -ne $headFillPath) {
                $headFillPath.Dispose()
            }
            $billPath.Dispose()
            $headPath.Dispose()
            $highlightBrush.Dispose()
            $eyeBrush.Dispose()
            $billBrush.Dispose()
            $headBrush.Dispose()
            $billPen.Dispose()
            $headPen.Dispose()
        }

        $menuBarDuckBitmap.Save($menuBarDuckIconPath, [Drawing.Imaging.ImageFormat]::Png)

        $trayLayouts = @{
            16 = @{ X = 1; Y = 1; Width = 14; Height = 13 }
            20 = @{ X = 1; Y = 1; Width = 18; Height = 17 }
            24 = @{ X = 1; Y = 2; Width = 22; Height = 20 }
            32 = @{ X = 2; Y = 3; Width = 28; Height = 26 }
        }
        foreach ($size in $traySizes) {
            $trayBitmap = New-Object Drawing.Bitmap(
                $size,
                $size,
                [Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $trayGraphics = [Drawing.Graphics]::FromImage($trayBitmap)
                try {
                    $trayGraphics.Clear([Drawing.Color]::Transparent)
                    $trayGraphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $trayGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $trayGraphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $trayGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $layout = $trayLayouts[$size]
                    $destination = [Drawing.Rectangle]::new(
                        $layout.X,
                        $layout.Y,
                        $layout.Width,
                        $layout.Height)
                    $trayGraphics.DrawImage(
                        $menuBarDuckBitmap,
                        $destination,
                        2,
                        1,
                        42,
                        31,
                        [Drawing.GraphicsUnit]::Pixel)
                }
                finally {
                    $trayGraphics.Dispose()
                }

                $trayPngPath = Join-Path $outputRoot ("tray_duck_color_{0}x{0}.png" -f $size)
                $trayBitmap.Save($trayPngPath, [Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $trayBitmap.Dispose()
            }
        }
    }
    finally {
        $menuBarDuckGraphics.Dispose()
    }
}
finally {
    $menuBarDuckBitmap.Dispose()
}

$trayFrames = @($traySizes | ForEach-Object {
    $path = Join-Path $outputRoot ("tray_duck_color_{0}x{0}.png" -f $_)
    [pscustomobject]@{ Size = $_; Bytes = [IO.File]::ReadAllBytes($path) }
})
$trayStream = [IO.File]::Open(
    $trayIconPath,
    [IO.FileMode]::Create,
    [IO.FileAccess]::Write,
    [IO.FileShare]::None)
$trayWriter = New-Object IO.BinaryWriter($trayStream)
try {
    $trayWriter.Write([uint16]0)
    $trayWriter.Write([uint16]1)
    $trayWriter.Write([uint16]$trayFrames.Count)
    $offset = 6 + (16 * $trayFrames.Count)
    foreach ($frame in $trayFrames) {
        $trayWriter.Write([byte]$frame.Size)
        $trayWriter.Write([byte]$frame.Size)
        $trayWriter.Write([byte]0)
        $trayWriter.Write([byte]0)
        $trayWriter.Write([uint16]1)
        $trayWriter.Write([uint16]32)
        $trayWriter.Write([uint32]$frame.Bytes.Length)
        $trayWriter.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $trayFrames) {
        $trayWriter.Write([byte[]]$frame.Bytes)
    }
}
finally {
    $trayWriter.Dispose()
    $trayStream.Dispose()
}

$frames = @($icoSizes | ForEach-Object {
    $path = Join-Path $outputRoot ("icon_{0}x{0}.png" -f $_)
    [pscustomobject]@{ Size = $_; Bytes = [IO.File]::ReadAllBytes($path) }
})
$stream = [IO.File]::Open($iconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = New-Object IO.BinaryWriter($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$frames.Count)
    $offset = 6 + (16 * $frames.Count)
    foreach ($frame in $frames) {
        $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$encodedSize)
        $writer.Write([byte]$encodedSize)
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
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

function Write-BigEndianUInt32 {
    param(
        [Parameter(Mandatory)] [IO.BinaryWriter]$Writer,
        [Parameter(Mandatory)] [uint32]$Value
    )

    $Writer.Write([byte](($Value -shr 24) -band 0xff))
    $Writer.Write([byte](($Value -shr 16) -band 0xff))
    $Writer.Write([byte](($Value -shr 8) -band 0xff))
    $Writer.Write([byte]($Value -band 0xff))
}

$icnsTypes = [ordered]@{
    '16' = 'icp4'
    '32' = 'icp5'
    '64' = 'icp6'
    '128' = 'ic07'
    '256' = 'ic08'
    '512' = 'ic09'
    '1024' = 'ic10'
}
$icnsFrames = @($pngSizes | ForEach-Object {
    $path = Join-Path $outputRoot ("icon_{0}x{0}.png" -f $_)
    [pscustomobject]@{
        Type = $icnsTypes[[string]$_]
        Bytes = [IO.File]::ReadAllBytes($path)
    }
})
$icnsLength = [uint32](8 + (($icnsFrames | ForEach-Object { 8 + $_.Bytes.Length }) |
    Measure-Object -Sum).Sum)
$icnsStream = [IO.File]::Open(
    $macIconPath,
    [IO.FileMode]::Create,
    [IO.FileAccess]::Write,
    [IO.FileShare]::None)
$icnsWriter = New-Object IO.BinaryWriter($icnsStream)
try {
    $icnsWriter.Write([Text.Encoding]::ASCII.GetBytes('icns'))
    Write-BigEndianUInt32 -Writer $icnsWriter -Value $icnsLength
    foreach ($frame in $icnsFrames) {
        $icnsWriter.Write([Text.Encoding]::ASCII.GetBytes($frame.Type))
        Write-BigEndianUInt32 -Writer $icnsWriter -Value ([uint32](8 + $frame.Bytes.Length))
        $icnsWriter.Write([byte[]]$frame.Bytes)
    }
}
finally {
    $icnsWriter.Dispose()
    $icnsStream.Dispose()
}

[pscustomobject]@{
    Source = $sourcePath
    PngDirectory = $outputRoot
    Ico = $iconPath
    Icns = $macIconPath
    MenuBarDuckColor = $menuBarDuckIconPath
    TrayIco = $trayIconPath
    Sizes = $pngSizes
    TraySizes = $traySizes
}
