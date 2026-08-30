[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageScript = Join-Path $PSScriptRoot 'Package-Zip.ps1'
$auditScript = Join-Path $PSScriptRoot 'Test-Package-Zip.ps1'
$readme = Join-Path $PSScriptRoot 'zip-readme.txt'
$finalArchive = Join-Path $PSScriptRoot 'artifacts\TransDuck-Windows-x64.zip'

function Write-SafeJson($Value) {
    [Console]::Out.WriteLine(($Value | ConvertTo-Json -Depth 6 -Compress))
}

function Test-AstClean([string]$Path) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    return @($errors).Count -eq 0
}

$report = [ordered]@{
    PackageScriptAstClean = $false
    AuditScriptAstClean = $false
    ExactArtifactAndRootPinned = $false
    ReleaseWinX64SelfContainedPinned = $false
    SingleFileManagedPayloadPinned = $false
    ApplicationAndDotNetLicensesPinned = $false
    DefaultNoOverwriteAndAtomicForce = $false
    UniqueStagingAndFinallyCleanup = $false
    PublishExclusionsPinned = $false
    ProxySettingsPayloadForbiddenPinned = $false
    RequiredRuntimeAndOcrClosurePinned = $false
    DeterministicEntrySafetyPinned = $false
    OrdinalSorterInputPreserved = $false
    TemporaryZipAuditedBeforeReplace = $false
    AuditCoversPeTreeAndStaging = $false
    PeInspectionAvoidsZipSeek = $false
    BilingualReadmeComplete = $false
    FinalArchiveAbsent = $false
    NoRecursiveRemoveItem = $false
    RuntimeZipAssembliesPinned = $false
    PackagingErrorsClosed = $false
    AtomicReplacementBackupCleanup = $false
    Passed = $false
    Failures = @()
}

try {
    $report.PackageScriptAstClean = Test-AstClean $packageScript
    $report.AuditScriptAstClean = Test-AstClean $auditScript
    $packageSource = Get-Content -LiteralPath $packageScript -Raw -Encoding UTF8
    $auditSource = Get-Content -LiteralPath $auditScript -Raw -Encoding UTF8
    $readmeSource = Get-Content -LiteralPath $readme -Raw -Encoding UTF8
    $report.ExactArtifactAndRootPinned = $packageSource.Contains("ArchiveFileName = 'TransDuck-Windows-x64.zip'") -and
        $packageSource.Contains("PayloadDirectoryName = 'TransDuck-Windows-x64'")
    $report.ReleaseWinX64SelfContainedPinned = $packageSource.Contains("ValidateSet('Release')") -and
        $packageSource.Contains('--runtime win-x64 --self-contained true') -and
        $packageSource.Contains('-p:PublishSingleFile=true')
    $report.SingleFileManagedPayloadPinned = $auditSource.Contains('BundledManagedEntriesAbsent') -and
        $auditSource.Contains('TransDuck-Windows-x64/TransDuck.Core.dll') -and
        $auditSource.Contains('TransDuck-Windows-x64/TransDuck.Infrastructure.dll') -and
        $auditSource.Contains('TransDuck-Windows-x64/TransDuck.Platform.Windows.dll') -and
        $auditSource.Contains('TransDuck-Windows-x64/Tesseract.dll')
    $report.ApplicationAndDotNetLicensesPinned = $packageSource.Contains("licenseSource = Join-Path `$repositoryRoot 'LICENSE'") -and
        $packageSource.Contains("dotnetLicenseSource = Join-Path `$dotnetRoot 'LICENSE.txt'") -and
        $packageSource.Contains("dotnetNoticesSource = Join-Path `$dotnetRoot 'ThirdPartyNotices.txt'") -and
        $packageSource.Contains('Microsoft-DotNet-Library-License.txt') -and
        $packageSource.Contains('Microsoft-DotNet-Third-Party-Notices.txt') -and
        $auditSource.Contains('TransDuck-Windows-x64/LICENSE.txt')
    $report.DefaultNoOverwriteAndAtomicForce = $packageSource.Contains('archive_exists_use_force') -and
        $packageSource.Contains('[IO.File]::Replace') -and $packageSource.Contains('[IO.File]::Move')
    $report.UniqueStagingAndFinallyCleanup = $packageSource.Contains('.transduck-zip-staging-') -and
        $packageSource.Contains('finally') -and $packageSource.Contains('Remove-OwnedStaging')
    $report.PublishExclusionsPinned = $packageSource.Contains("'x86'") -and $packageSource.Contains("'assets'") -and
        $packageSource.Contains('paddle') -and $packageSource.Contains('pdb') -and
        $packageSource.Contains('appxmanifest') -and $packageSource.Contains('credentials')
    $packageExclusions = ''
    $packageExclusionsStart = $packageSource.IndexOf('function Test-ExcludedPayloadPath')
    $packageExclusionsEnd = $packageSource.IndexOf('function Sort-RelativeItemsOrdinal')
    if ($packageExclusionsStart -ge 0 -and $packageExclusionsEnd -gt $packageExclusionsStart) {
        $packageExclusions = $packageSource.Substring(
            $packageExclusionsStart,
            $packageExclusionsEnd - $packageExclusionsStart)
    }
    $auditForbiddenEntries = ''
    $auditForbiddenStart = $auditSource.IndexOf('function Test-ForbiddenEntry')
    $auditForbiddenEnd = $auditSource.IndexOf('function Test-PeX64')
    if ($auditForbiddenStart -ge 0 -and $auditForbiddenEnd -gt $auditForbiddenStart) {
        $auditForbiddenEntries = $auditSource.Substring(
            $auditForbiddenStart,
            $auditForbiddenEnd - $auditForbiddenStart)
    }
    $report.ProxySettingsPayloadForbiddenPinned = $packageExclusions.Contains('proxy-settings') -and
        $auditForbiddenEntries.Contains('proxy-settings')
    $report.RequiredRuntimeAndOcrClosurePinned = $packageSource.Contains('D3DCompiler_47_cor3.dll') -and
        $packageSource.Contains('PenImc_cor3.dll') -and
        $packageSource.Contains('PresentationNative_cor3.dll') -and
        $packageSource.Contains('vcruntime140_cor3.dll') -and $packageSource.Contains('wpfgfx_cor3.dll') -and
        $packageSource.Contains('x64/tesseract50.dll') -and $packageSource.Contains('x64/leptonica-1.82.0.dll') -and
        $packageSource.Contains('tessdata/eng.traineddata') -and $packageSource.Contains('tessdata/chi_sim.traineddata') -and
        $packageSource.Contains('THIRD-PARTY-NOTICES.md')
    $report.DeterministicEntrySafetyPinned = $packageSource.Contains('FixedZipTimestamp') -and
        $packageSource.Contains('[StringComparer]::Ordinal') -and $packageSource.Contains('Test-ReparsePoint') -and
        $packageSource.Contains('Get-SafeRelativePath')
    $report.OrdinalSorterInputPreserved = $packageSource.Contains('Sort-RelativeItemsOrdinal($InputItems)') -and
        $packageSource.Contains('$sortedItems = [Collections.Generic.List[object]]::new()') -and
        -not $packageSource.Contains('Sort-RelativeItemsOrdinal($Items)')
    $auditPosition = $packageSource.IndexOf('Assert-TemporaryZipAudit $temporaryZip')
    $replacePosition = $packageSource.IndexOf('[IO.File]::Replace')
    $report.TemporaryZipAuditedBeforeReplace = $auditPosition -ge 0 -and $replacePosition -gt $auditPosition
    $report.AuditCoversPeTreeAndStaging = $auditSource.Contains('Test-PeX64') -and
        $auditSource.Contains('0x8664') -and $auditSource.Contains('Get-ZipTreeHash') -and
        $auditSource.Contains('StagingDirectoryCount') -and $auditSource.Contains('ForbiddenEntriesAbsent') -and
        $auditSource.Contains('WpfNativeRuntimeX64') -and $auditSource.Contains('BundledManagedEntriesAbsent')
    $report.PeInspectionAvoidsZipSeek = $auditSource.Contains('$remaining = $peOffset - 64') -and
        -not $auditSource.Contains('$stream.Position =')
    $chineseLabel = [string][char]0x4e2d + [char]0x6587
    $report.BilingualReadmeComplete = $readmeSource.Contains('English') -and $readmeSource.Contains($chineseLabel) -and
        $readmeSource.Contains('TransDuck.exe') -and $readmeSource.Contains('%LocalAppData%\TransDuck') -and
        $readmeSource.Contains('login-startup') -and $readmeSource.Contains('Tesseract') -and
        $readmeSource.Contains('LICENSE.txt') -and $readmeSource.Contains('Microsoft-DotNet-Library-License.txt')
    $report.FinalArchiveAbsent = -not (Test-Path -LiteralPath $finalArchive)
    $report.NoRecursiveRemoveItem = -not $packageSource.Contains('Remove-Item -Recurse')
    $report.RuntimeZipAssembliesPinned = $packageSource.Contains('Add-Type -AssemblyName System.IO.Compression') -and
        $packageSource.Contains('Add-Type -AssemblyName System.IO.Compression.FileSystem') -and
        $auditSource.Contains('Add-Type -AssemblyName System.IO.Compression')
    $report.PackagingErrorsClosed = $packageSource.Contains('$failureCode = ''zip_packaging_failed''') -and
        $packageSource.Contains('$failureCode = ''staging_cleanup_failed''') -and
        $packageSource.Contains('[Console]::Error.WriteLine($failureCode)')
    $report.AtomicReplacementBackupCleanup = $packageSource.Contains('.transduck-zip-replace-backup-') -and
        $packageSource.Contains('[IO.File]::Replace($temporaryZip, $archivePath, $replacementBackup)') -and
        $packageSource.Contains('ReplacementBackupRemoved') -and
        $packageSource.Contains('[IO.File]::Delete($replacementBackup)')
    foreach ($name in @(
        'PackageScriptAstClean', 'AuditScriptAstClean', 'ExactArtifactAndRootPinned',
        'ReleaseWinX64SelfContainedPinned', 'SingleFileManagedPayloadPinned',
        'ApplicationAndDotNetLicensesPinned',
        'DefaultNoOverwriteAndAtomicForce',
        'UniqueStagingAndFinallyCleanup', 'PublishExclusionsPinned',
        'ProxySettingsPayloadForbiddenPinned',
        'RequiredRuntimeAndOcrClosurePinned', 'DeterministicEntrySafetyPinned',
        'OrdinalSorterInputPreserved',
        'TemporaryZipAuditedBeforeReplace', 'AuditCoversPeTreeAndStaging', 'PeInspectionAvoidsZipSeek',
        'BilingualReadmeComplete', 'FinalArchiveAbsent', 'NoRecursiveRemoveItem',
        'RuntimeZipAssembliesPinned', 'PackagingErrorsClosed', 'AtomicReplacementBackupCleanup'
    )) {
        if (-not $report.$name) { $report.Failures += 'zip_static_check_failed' }
    }
    $report.Passed = $report.Failures.Count -eq 0
}
catch {
    $report.Failures += 'zip_static_check_unavailable'
}

Write-SafeJson $report
exit $(if ($report.Passed) { 0 } else { 1 })
