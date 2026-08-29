[CmdletBinding()]
param(
    [string]$InputPath,
    [string]$OutputDirectory,
    [string]$IcoPath
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

$sourcePath = [IO.Path]::GetFullPath($InputPath)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$iconPath = [IO.Path]::GetFullPath($IcoPath)
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw 'icon_source_missing'
}

Add-Type -AssemblyName System.Drawing
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
[IO.Directory]::CreateDirectory((Split-Path -Parent $iconPath)) | Out-Null
$sizes = @(16, 32, 64, 128, 256)
$source = [Drawing.Image]::FromFile($sourcePath)
try {
    if ($source.Width -ne $source.Height) {
        throw 'icon_source_not_square'
    }

    foreach ($size in $sizes) {
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

$frames = @($sizes | ForEach-Object {
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

[pscustomobject]@{
    Source = $sourcePath
    PngDirectory = $outputRoot
    Ico = $iconPath
    Sizes = $sizes
}
