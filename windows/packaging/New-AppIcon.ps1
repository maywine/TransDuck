[CmdletBinding()]
param(
    [string]$InputPath,
    [string]$OutputDirectory,
    [string]$IcoPath,
    [string]$IcnsPath
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

$sourcePath = [IO.Path]::GetFullPath($InputPath)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$iconPath = [IO.Path]::GetFullPath($IcoPath)
$macIconPath = [IO.Path]::GetFullPath($IcnsPath)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw 'icon_source_missing'
}

Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $iconPath)) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $macIconPath)) | Out-Null
$pngSizes = @(16, 32, 64, 128, 256, 512, 1024)
$icoSizes = @(16, 32, 64, 128, 256)
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
    Sizes = $pngSizes
}
