[CmdletBinding()]
param(
    [string]$ZipPath = (Join-Path $PSScriptRoot 'artifacts\TransDuck-Windows-x64.zip'),

    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:PayloadDirectoryName = 'TransDuck-Windows-x64'
$script:FixedZipTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$script:RequiredEntries = @(
    'TransDuck-Windows-x64/TransDuck.exe',
    'TransDuck-Windows-x64/D3DCompiler_47_cor3.dll',
    'TransDuck-Windows-x64/PenImc_cor3.dll',
    'TransDuck-Windows-x64/PresentationNative_cor3.dll',
    'TransDuck-Windows-x64/vcruntime140_cor3.dll',
    'TransDuck-Windows-x64/wpfgfx_cor3.dll',
    'TransDuck-Windows-x64/x64/tesseract50.dll',
    'TransDuck-Windows-x64/x64/leptonica-1.82.0.dll',
    'TransDuck-Windows-x64/tessdata/eng.traineddata',
    'TransDuck-Windows-x64/tessdata/chi_sim.traineddata',
    'TransDuck-Windows-x64/tessdata/model-manifest.json',
    'TransDuck-Windows-x64/tessdata/LICENSE',
    'TransDuck-Windows-x64/licenses/Apache-2.0.txt',
    'TransDuck-Windows-x64/licenses/Leptonica-BSD-2-Clause.txt',
    'TransDuck-Windows-x64/licenses/Microsoft-DotNet-Library-License.txt',
    'TransDuck-Windows-x64/licenses/Microsoft-DotNet-Third-Party-Notices.txt',
    'TransDuck-Windows-x64/LICENSE.txt',
    'TransDuck-Windows-x64/THIRD-PARTY-NOTICES.md',
    'TransDuck-Windows-x64/README.txt'
)
$script:RequiredWpfNativeEntries = @(
    'TransDuck-Windows-x64/D3DCompiler_47_cor3.dll',
    'TransDuck-Windows-x64/PenImc_cor3.dll',
    'TransDuck-Windows-x64/PresentationNative_cor3.dll',
    'TransDuck-Windows-x64/vcruntime140_cor3.dll',
    'TransDuck-Windows-x64/wpfgfx_cor3.dll'
)
$script:BundledManagedEntries = @(
    'TransDuck-Windows-x64/TransDuck.deps.json',
    'TransDuck-Windows-x64/TransDuck.runtimeconfig.json',
    'TransDuck-Windows-x64/TransDuck.dll',
    'TransDuck-Windows-x64/TransDuck.Core.dll',
    'TransDuck-Windows-x64/TransDuck.Infrastructure.dll',
    'TransDuck-Windows-x64/TransDuck.Platform.Windows.dll',
    'TransDuck-Windows-x64/Tesseract.dll',
    'TransDuck-Windows-x64/System.Private.CoreLib.dll',
    'TransDuck-Windows-x64/PresentationFramework.dll',
    'TransDuck-Windows-x64/coreclr.dll',
    'TransDuck-Windows-x64/hostfxr.dll',
    'TransDuck-Windows-x64/hostpolicy.dll'
)

function Write-SafeJson($Value) {
    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 8 -Compress))
}

function Test-SafeEntryName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('\') -or $Name.StartsWith('/') -or $Name.Contains(':')) {
        return $false
    }
    $parts = @($Name -split '/')
    if ($parts.Count -lt 2 -or $parts[0] -cne $script:PayloadDirectoryName) { return $false }
    return @($parts | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -eq 0
}

function Test-ForbiddenEntry([string]$Name) {
    $leaf = [IO.Path]::GetFileName($Name)
    return $Name -match '(?i)(^|/)(x86|assets|tests?|credentials)(/|$)' -or
        $Name -match '(?i)paddle' -or
        $leaf -match '(?i)^(appxmanifest\.xml|.*\.(pdb|cs|csproj|sln|xaml|ps1|psm1|msix|appx|appxbundle|pfx|p12|pem|key|cer|crt|der|p7b|pvk|ppk|jks))$' -or
        $leaf -match '(?i)^(configuration|provider-settings|hotkey-settings|proxy-settings|history|diagnostics)(\.|$)' -or
        $leaf -match '(?i)(private.?key|certificate|\.credential$)'
}

function Test-PeX64($Entry) {
    if ($null -eq $Entry) { return $false }
    $stream = $Entry.Open()
    try {
        $header = New-Object byte[] 64
        if ($stream.Read($header, 0, $header.Length) -ne $header.Length -or $header[0] -ne 0x4d -or $header[1] -ne 0x5a) {
            return $false
        }
        $peOffset = [BitConverter]::ToInt32($header, 60)
        if ($peOffset -lt 64 -or $peOffset -gt 1048576 -or $Entry.Length -lt $peOffset + 6) { return $false }
        $discard = New-Object byte[] 4096
        $remaining = $peOffset - 64
        while ($remaining -gt 0) {
            $want = [Math]::Min($remaining, $discard.Length)
            $read = $stream.Read($discard, 0, $want)
            if ($read -le 0) { return $false }
            $remaining -= $read
        }
        $pe = New-Object byte[] 6
        if ($stream.Read($pe, 0, $pe.Length) -ne $pe.Length -or
            $pe[0] -ne 0x50 -or $pe[1] -ne 0x45 -or $pe[2] -ne 0 -or $pe[3] -ne 0) {
            return $false
        }
        return [BitConverter]::ToUInt16($pe, 4) -eq 0x8664
    }
    finally { $stream.Dispose() }
}

function Sort-EntriesOrdinal($Entries) {
    $items = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($Entries)) { [void]$items.Add($entry) }
    $comparison = [Comparison[object]]{
        param($Left, $Right)
        return [StringComparer]::Ordinal.Compare([string]$Left.FullName, [string]$Right.FullName)
    }
    $items.Sort($comparison)
    return @($items)
}

function Get-ZipTreeHash($Entries) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $buffer = New-Object byte[] 131072
        foreach ($entry in $Entries) {
            $nameBytes = [Text.Encoding]::UTF8.GetBytes($entry.FullName)
            $nameLength = [BitConverter]::GetBytes([int]$nameBytes.Length)
            $contentLength = [BitConverter]::GetBytes([int64]$entry.Length)
            [void]$algorithm.TransformBlock($nameLength, 0, $nameLength.Length, $nameLength, 0)
            [void]$algorithm.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            [void]$algorithm.TransformBlock($contentLength, 0, $contentLength.Length, $contentLength, 0)
            $stream = $entry.Open()
            try {
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    [void]$algorithm.TransformBlock($buffer, 0, $read, $buffer, 0)
                }
            }
            finally { $stream.Dispose() }
        }
        [void]$algorithm.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return ([BitConverter]::ToString($algorithm.Hash)).Replace('-', '').ToLowerInvariant()
    }
    finally { $algorithm.Dispose() }
}

$report = [ordered]@{
    ArchiveExists = $false
    EntryCount = 0
    EntriesUnique = $false
    EntriesSafe = $false
    EntriesSortedOrdinal = $false
    EntriesFixedTimestamp = $false
    TopLevelDirectoryValid = $false
    RequiredEntriesPresent = $false
    ForbiddenEntriesAbsent = $false
    BundledManagedEntriesAbsent = $false
    MainExecutableX64 = $false
    WpfNativeRuntimeX64 = $false
    TesseractNativeX64 = $false
    LeptonicaNativeX64 = $false
    StagingDirectoryCount = -1
    StagingDirectoriesAbsent = $false
    ZipSha256 = $null
    TreeSha256 = $null
    Passed = $false
    Failures = @()
}

try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipFullPath = [IO.Path]::GetFullPath($ZipPath)
    $outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
    $report.ArchiveExists = Test-Path -LiteralPath $zipFullPath -PathType Leaf
    if (-not $report.ArchiveExists) { throw 'archive_missing' }
    if (-not (Test-Path -LiteralPath $outputFullPath -PathType Container)) { throw 'output_directory_missing' }
    $report.StagingDirectoryCount = @(
        Get-ChildItem -LiteralPath $outputFullPath -Directory -Filter '.transduck-zip-staging-*' -ErrorAction Stop
    ).Count
    $report.StagingDirectoriesAbsent = $report.StagingDirectoryCount -eq 0

    $archive = [IO.Compression.ZipFile]::OpenRead($zipFullPath)
    try {
        $directoryEntries = @($archive.Entries | Where-Object { $_.FullName.EndsWith('/') })
        $entries = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
        $report.EntryCount = $entries.Count
        $names = @($entries | ForEach-Object { $_.FullName })
        $set = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $report.EntriesUnique = $true
        foreach ($name in $names) {
            if (-not $set.Add($name)) { $report.EntriesUnique = $false; break }
        }
        $report.EntriesSafe = $directoryEntries.Count -eq 0 -and @($names | Where-Object { -not (Test-SafeEntryName $_) }).Count -eq 0
        $report.TopLevelDirectoryValid = $report.EntriesSafe
        $expectedOrder = [string[]]$names.Clone()
        [Array]::Sort($expectedOrder, [StringComparer]::Ordinal)
        $report.EntriesSortedOrdinal = $names.Count -eq $expectedOrder.Count
        for ($index = 0; $index -lt $names.Count -and $report.EntriesSortedOrdinal; $index++) {
            if ($names[$index] -cne $expectedOrder[$index]) { $report.EntriesSortedOrdinal = $false }
        }
        $report.EntriesFixedTimestamp = @($entries | Where-Object {
            $_.LastWriteTime.Year -ne 2000 -or $_.LastWriteTime.Month -ne 1 -or
            $_.LastWriteTime.Day -ne 1 -or $_.LastWriteTime.Hour -ne 0 -or
            $_.LastWriteTime.Minute -ne 0 -or $_.LastWriteTime.Second -ne 0
        }).Count -eq 0
        $report.RequiredEntriesPresent = @($script:RequiredEntries | Where-Object { -not $set.Contains($_) }).Count -eq 0
        $report.ForbiddenEntriesAbsent = @($names | Where-Object { Test-ForbiddenEntry $_ }).Count -eq 0
        $report.BundledManagedEntriesAbsent = @(
            $script:BundledManagedEntries | Where-Object { $set.Contains($_) }
        ).Count -eq 0
        $byName = @{}
        foreach ($entry in $entries) { $byName[$entry.FullName] = $entry }
        $report.MainExecutableX64 = Test-PeX64 $byName['TransDuck-Windows-x64/TransDuck.exe']
        $report.WpfNativeRuntimeX64 = @(
            $script:RequiredWpfNativeEntries | Where-Object { -not (Test-PeX64 $byName[$_]) }
        ).Count -eq 0
        $report.TesseractNativeX64 = Test-PeX64 $byName['TransDuck-Windows-x64/x64/tesseract50.dll']
        $report.LeptonicaNativeX64 = Test-PeX64 $byName['TransDuck-Windows-x64/x64/leptonica-1.82.0.dll']
        $orderedEntries = Sort-EntriesOrdinal $entries
        $report.TreeSha256 = Get-ZipTreeHash $orderedEntries
    }
    finally { $archive.Dispose() }
    $report.ZipSha256 = (Get-FileHash -LiteralPath $zipFullPath -Algorithm SHA256).Hash.ToLowerInvariant()

    foreach ($name in @(
        'EntriesUnique', 'EntriesSafe', 'EntriesSortedOrdinal', 'EntriesFixedTimestamp',
        'TopLevelDirectoryValid', 'RequiredEntriesPresent', 'ForbiddenEntriesAbsent',
        'BundledManagedEntriesAbsent', 'MainExecutableX64', 'WpfNativeRuntimeX64',
        'TesseractNativeX64', 'LeptonicaNativeX64', 'StagingDirectoriesAbsent'
    )) {
        if (-not $report.$name) { $report.Failures += 'zip_audit_failed' }
    }
    $report.Passed = $report.Failures.Count -eq 0
}
catch {
    $report.Failures += 'zip_audit_unavailable'
}

Write-SafeJson $report
exit $(if ($report.Passed) { 0 } else { 1 })
