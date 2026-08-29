[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),

    [string]$DotnetPath = 'dotnet.exe',

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ArchiveFileName = 'TransDuck-Windows-x64.zip'
$script:PayloadDirectoryName = 'TransDuck-Windows-x64'
$script:FixedZipTimestamp = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$script:RequiredPayloadFiles = @(
    'TransDuck.exe', 'TransDuck.deps.json', 'TransDuck.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Tesseract.dll',
    'x64/tesseract50.dll', 'x64/leptonica-1.82.0.dll',
    'tessdata/eng.traineddata', 'tessdata/chi_sim.traineddata',
    'tessdata/model-manifest.json', 'tessdata/LICENSE',
    'licenses/Apache-2.0.txt', 'licenses/Leptonica-BSD-2-Clause.txt',
    'THIRD-PARTY-NOTICES.md', 'README.txt'
)

$windowsRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $windowsRoot 'src\TransDuck.App\TransDuck.App.csproj'
$readmeSource = Join-Path $PSScriptRoot 'zip-readme.txt'

function Resolve-DotnetPath([string]$Candidate) {
    if ([IO.Path]::IsPathRooted($Candidate)) {
        if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
            throw 'dotnet_path_missing'
        }
        return [IO.Path]::GetFullPath($Candidate)
    }
    return (Get-Command $Candidate -ErrorAction Stop).Source
}

function Test-ReparsePoint([string]$Path) {
    return ((Get-Item -LiteralPath $Path -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
}

function Get-SafeRelativePath([string]$Root, [string]$Path) {
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'publish_path_escaped_root'
    }
    $relative = $full.Substring($rootFull.Length).Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($relative) -or $relative.StartsWith('/') -or
        [IO.Path]::IsPathRooted($relative) -or @($relative -split '/' | Where-Object { $_ -eq '..' -or $_.Length -eq 0 }).Count -ne 0) {
        throw 'publish_path_not_relative'
    }
    return $relative
}

function Test-ExcludedPayloadPath([string]$RelativePath) {
    $segments = @($RelativePath -split '/')
    $leaf = $segments[-1]
    if ($segments | Where-Object { $_ -ieq 'x86' -or $_ -ieq 'assets' -or $_ -ieq 'tests' -or $_ -ieq 'test' -or $_ -match '(?i)paddle' -or $_ -ieq 'credentials' }) {
        return $true
    }
    if ($leaf -match '(?i)^(appxmanifest\.xml|.*\.(pdb|cs|csproj|sln|xaml|ps1|psm1|msix|appx|appxbundle|pfx|p12|pem|key|cer|crt|der|p7b|pvk|ppk|jks))$' -or
        $leaf -match '(?i)^(configuration|provider-settings|hotkey-settings|proxy-settings|history|diagnostics)(\.|$)' -or
        $leaf -match '(?i)(private.?key|certificate|\.credential$)') {
        return $true
    }
    return $false
}

function Sort-RelativeItemsOrdinal($InputItems) {
    $sortedItems = [Collections.Generic.List[object]]::new()
    foreach ($item in @($InputItems)) { [void]$sortedItems.Add($item) }
    $comparison = [Comparison[object]]{
        param($Left, $Right)
        return [StringComparer]::Ordinal.Compare([string]$Left.Relative, [string]$Right.Relative)
    }
    $sortedItems.Sort($comparison)
    return @($sortedItems)
}

function Get-SafePublishFiles([string]$PublishRoot) {
    if (-not (Test-Path -LiteralPath $PublishRoot -PathType Container) -or (Test-ReparsePoint $PublishRoot)) {
        throw 'publish_root_invalid'
    }
    $files = [Collections.Generic.List[object]]::new()
    function Visit-PublishDirectory([string]$Directory) {
        if (Test-ReparsePoint $Directory) { throw 'publish_reparse_point' }
        foreach ($entry in @(Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop)) {
            if (Test-ReparsePoint $entry.FullName) { throw 'publish_reparse_point' }
            if ($entry.PSIsContainer) {
                Visit-PublishDirectory $entry.FullName
                continue
            }
            $relative = Get-SafeRelativePath $PublishRoot $entry.FullName
            if (-not (Test-ExcludedPayloadPath $relative)) {
                [void]$files.Add([pscustomobject]@{ Source = $entry.FullName; Relative = $relative })
            }
        }
    }
    Visit-PublishDirectory $PublishRoot
    return Sort-RelativeItemsOrdinal $files
}

function Copy-PayloadFile([string]$Source, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    [IO.File]::Copy($Source, $Destination, $false)
}

function Assert-RequiredPayload([string]$PayloadRoot) {
    if (Test-ReparsePoint $PayloadRoot) { throw 'payload_reparse_point' }
    foreach ($relative in $script:RequiredPayloadFiles) {
        $path = Join-Path $PayloadRoot ($relative.Replace('/', '\'))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Test-ReparsePoint $path)) {
            throw 'required_payload_file_missing'
        }
    }
    foreach ($forbidden in @('x86', 'Assets', 'AppxManifest.xml')) {
        if (Test-Path -LiteralPath (Join-Path $PayloadRoot $forbidden)) {
            throw 'forbidden_payload_entry_present'
        }
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File -Force -ErrorAction Stop)) {
        $relative = Get-SafeRelativePath $PayloadRoot $file.FullName
        if ((Test-ReparsePoint $file.FullName) -or (Test-ExcludedPayloadPath $relative)) {
            throw 'forbidden_payload_entry_present'
        }
    }
}

function New-DeterministicZip([string]$PayloadRoot, [string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entries = @(
            Get-ChildItem -LiteralPath $PayloadRoot -Recurse -File -Force -ErrorAction Stop |
                ForEach-Object {
                    if (Test-ReparsePoint $_.FullName) { throw 'payload_reparse_point' }
                    [pscustomobject]@{ File = $_.FullName; Relative = Get-SafeRelativePath $PayloadRoot $_.FullName }
                }
        )
        $entries = Sort-RelativeItemsOrdinal $entries
        foreach ($item in $entries) {
            $entryName = ($script:PayloadDirectoryName + '/' + $item.Relative)
            if (-not (Test-SafeZipEntryName $entryName)) { throw 'zip_entry_not_safe' }
            $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $script:FixedZipTimestamp
            $input = [IO.File]::Open($item.File, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally {
                $output.Dispose()
                $input.Dispose()
            }
        }
    }
    finally { $archive.Dispose() }
}

function Test-SafeZipEntryName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('\') -or $Name.StartsWith('/') -or $Name.Contains(':')) {
        return $false
    }
    $parts = @($Name -split '/')
    if ($parts.Count -lt 2 -or $parts[0] -cne $script:PayloadDirectoryName) { return $false }
    return @($parts | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -eq 0
}

function Assert-TemporaryZipAudit([string]$ZipPath) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if (@($archive.Entries | Where-Object { $_.FullName.EndsWith('/') }).Count -ne 0) {
            throw 'temporary_zip_directory_entry_present'
        }
        $entries = @($archive.Entries)
        $names = @($entries | ForEach-Object { $_.FullName })
        $unique = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $names) {
            if (-not (Test-SafeZipEntryName $name) -or -not $unique.Add($name)) { throw 'temporary_zip_entry_invalid' }
            $relative = $name.Substring($script:PayloadDirectoryName.Length + 1)
            if (Test-ExcludedPayloadPath $relative) { throw 'temporary_zip_forbidden_entry' }
        }
        foreach ($required in $script:RequiredPayloadFiles) {
            $entryName = $script:PayloadDirectoryName + '/' + $required
            if (-not $unique.Contains($entryName)) { throw 'temporary_zip_required_entry_missing' }
        }
        $expectedOrder = [string[]]$names.Clone()
        [Array]::Sort($expectedOrder, [StringComparer]::Ordinal)
        for ($index = 0; $index -lt $names.Count; $index++) {
            if ($names[$index] -cne $expectedOrder[$index]) { throw 'temporary_zip_entry_order_invalid' }
        }
        foreach ($entry in $entries) {
            if ($entry.LastWriteTime.Year -ne 2000 -or $entry.LastWriteTime.Month -ne 1 -or
                $entry.LastWriteTime.Day -ne 1 -or $entry.LastWriteTime.Hour -ne 0 -or
                $entry.LastWriteTime.Minute -ne 0 -or $entry.LastWriteTime.Second -ne 0) {
                throw 'temporary_zip_timestamp_invalid'
            }
        }
    }
    finally { $archive.Dispose() }
}

function Remove-OwnedStaging([string]$StagingRoot) {
    if (-not (Test-Path -LiteralPath $StagingRoot)) { return }
    $leaf = [IO.Path]::GetFileName($StagingRoot)
    if ($leaf -notmatch '^\.transduck-zip-staging-[a-f0-9]{32}$' -or (Test-ReparsePoint $StagingRoot)) {
        throw 'staging_cleanup_refused'
    }
    function Remove-SafeDirectory([string]$Directory) {
        if (Test-ReparsePoint $Directory) { throw 'staging_cleanup_refused' }
        foreach ($entry in @(Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop)) {
            if (Test-ReparsePoint $entry.FullName) { throw 'staging_cleanup_refused' }
            if ($entry.PSIsContainer) { Remove-SafeDirectory $entry.FullName }
            else { [IO.File]::Delete($entry.FullName) }
        }
        [IO.Directory]::Delete($Directory, $false)
    }
    Remove-SafeDirectory $StagingRoot
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$archivePath = Join-Path $outputRoot $script:ArchiveFileName
$stagingRoot = $null
$result = $null
$failureCode = $null
$replacementBackup = $null
$replacementBackupRemoved = $false
try {
    if ($outputRoot -eq [IO.Path]::GetPathRoot($outputRoot)) { throw 'output_directory_is_root' }
    if (Test-Path -LiteralPath $outputRoot) {
        if (-not (Get-Item -LiteralPath $outputRoot -Force).PSIsContainer -or (Test-ReparsePoint $outputRoot)) { throw 'output_directory_invalid' }
    }
    else { [IO.Directory]::CreateDirectory($outputRoot) | Out-Null }
    if (Test-Path -LiteralPath $archivePath) {
        if (-not $Force -or (Get-Item -LiteralPath $archivePath -Force).PSIsContainer) { throw 'archive_exists_use_force' }
    }
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf) -or -not (Test-Path -LiteralPath $readmeSource -PathType Leaf)) {
        throw 'packaging_source_missing'
    }

    $stagingRoot = Join-Path $outputRoot ('.transduck-zip-staging-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    $publishRoot = Join-Path $stagingRoot 'publish'
    $payloadRoot = Join-Path $stagingRoot $script:PayloadDirectoryName
    $temporaryZip = Join-Path $stagingRoot $script:ArchiveFileName
    $dotnet = Resolve-DotnetPath $DotnetPath
    & $dotnet publish $projectPath --configuration $Configuration --runtime win-x64 --self-contained true --output $publishRoot -p:DebugSymbols=false -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw 'dotnet_publish_failed' }

    [IO.Directory]::CreateDirectory($payloadRoot) | Out-Null
    foreach ($file in @(Get-SafePublishFiles $publishRoot)) {
        Copy-PayloadFile $file.Source (Join-Path $payloadRoot ($file.Relative.Replace('/', '\')))
    }
    if (Test-ReparsePoint $readmeSource) { throw 'readme_reparse_point' }
    Copy-PayloadFile $readmeSource (Join-Path $payloadRoot 'README.txt')
    Assert-RequiredPayload $payloadRoot
    New-DeterministicZip $payloadRoot $temporaryZip
    Assert-TemporaryZipAudit $temporaryZip

    if (Test-Path -LiteralPath $archivePath) {
        $replacementBackup = Join-Path $outputRoot ('.transduck-zip-replace-backup-' + [guid]::NewGuid().ToString('N') + '.zip')
        [IO.File]::Replace($temporaryZip, $archivePath, $replacementBackup)
    }
    else {
        [IO.File]::Move($temporaryZip, $archivePath)
    }
    $archive = Get-Item -LiteralPath $archivePath -ErrorAction Stop
    $result = [pscustomobject]@{
        ArchiveFileName = $script:ArchiveFileName
        PayloadDirectoryName = $script:PayloadDirectoryName
        ArchiveSha256 = (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        ArchiveBytes = $archive.Length
        StagingRemoved = $false
        ReplacementBackupRemoved = $false
    }
}
catch {
    $failureCode = 'zip_packaging_failed'
}
finally {
    if ($null -ne $stagingRoot) {
        try { Remove-OwnedStaging $stagingRoot }
        catch { $failureCode = 'staging_cleanup_failed' }
    }
    if ($null -ne $replacementBackup -and (Test-Path -LiteralPath $replacementBackup)) {
        try {
            if ([IO.Path]::GetFileName($replacementBackup) -notmatch '^\.transduck-zip-replace-backup-[a-f0-9]{32}\.zip$' -or
                (Test-ReparsePoint $replacementBackup)) {
                throw 'replacement_backup_cleanup_refused'
            }
            [IO.File]::Delete($replacementBackup)
        }
        catch { $failureCode = 'replacement_backup_cleanup_failed' }
    }
    $replacementBackupRemoved = $null -eq $replacementBackup -or -not (Test-Path -LiteralPath $replacementBackup)
}

if ($null -ne $failureCode) {
    [Console]::Error.WriteLine($failureCode)
    exit 1
}
if ($null -ne $result) {
    $result.StagingRemoved = -not (Test-Path -LiteralPath $stagingRoot)
    $result.ReplacementBackupRemoved = $replacementBackupRemoved
    Write-Output $result
}
