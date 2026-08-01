Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:WinCareModuleRoot = $PSScriptRoot
$moduleVersion = $ExecutionContext.SessionState.Module.Version
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    $moduleManifestPath = Join-Path $PSScriptRoot 'WinCare.psd1'
    $moduleManifest = Import-PowerShellDataFile -LiteralPath $moduleManifestPath
    $moduleVersion = [version][string]$moduleManifest.ModuleVersion
}
if ($null -eq $moduleVersion -or $moduleVersion -eq [version]'0.0') {
    throw 'WinCare module version could not be resolved from the module session or manifest.'
}
$script:WinCareVersion = $moduleVersion.ToString()
$script:WinCareSourceManifestFiles = @(
    'SourceManifests/Core.psd1'
    'SourceManifests/Providers.psd1'
    'SourceManifests/UI.psd1'
)
$script:WinCareSourceGroups = @(
    [pscustomobject]@{ Name='Core'; Manifest='SourceManifests/Core.psd1'; Folders=@('Core') }
    [pscustomobject]@{ Name='Providers'; Manifest='SourceManifests/Providers.psd1'; Folders=@('Providers') }
    [pscustomobject]@{ Name='UI'; Manifest='SourceManifests/UI.psd1'; Folders=@('UI','UI/Gui','UI/Screens') }
)

# h01: Validation receipt cache — skip the full manifest validation loop when the
# source manifests are unchanged since the last verified load. The receipt stores
# each manifest's fingerprint (LastWriteTime ticks + Length) and the canonical
# dot-source order. Any fingerprint mismatch falls back to full validation + receipt update.
# Security: the receipt is keyed to actual file metadata on disk, not a user value.
$receiptPath = Join-Path $PSScriptRoot '.wincare-load-receipt.json'
$expectedModuleFiles = [Collections.Generic.List[string]]::new()
$receiptHit = $false
try {
    if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        $receipt = [IO.File]::ReadAllText($receiptPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($receipt.SchemaVersion -eq 2 -and [string]$receipt.ModuleVersion -eq $script:WinCareVersion) {
            $fingerprintsMatch = $true
            foreach ($group in $script:WinCareSourceGroups) {
                $manifestPath = Join-Path $PSScriptRoot ([string]$group.Manifest)
                $item = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
                $stored = $receipt.Fingerprints.($group.Name)
                if ($null -eq $stored -or
                    [long]$stored.Ticks   -ne $item.LastWriteTimeUtc.Ticks -or
                    [long]$stored.Length  -ne $item.Length) {
                    $fingerprintsMatch = $false
                    break
                }
            }
            if ($fingerprintsMatch) {
                foreach ($path in $receipt.Files) { $expectedModuleFiles.Add([string]$path) }
                $receiptHit = $true
            }
        }
    }
} catch {
    # Receipt read/parse failure is non-fatal — fall through to full validation.
    Write-Verbose "WinCare: load receipt unreadable ($($_.Exception.Message)); running full validation."
    $expectedModuleFiles.Clear()
    $receiptHit = $false
}

if (-not $receiptHit) {
    # Full validation path (first boot or manifest changed).
    $newFingerprints = [ordered]@{}
    foreach ($group in $script:WinCareSourceGroups) {
        if ([string]$group.Manifest -notin $script:WinCareSourceManifestFiles) {
            throw "Source group $($group.Name) is not declared in WinCareSourceManifestFiles."
        }
        $manifestPath = Join-Path $PSScriptRoot ([string]$group.Manifest)
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            throw "Required source manifest is missing: $($group.Manifest)"
        }
        $manifestItem = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
        $newFingerprints[$group.Name] = @{
            Ticks  = $manifestItem.LastWriteTimeUtc.Ticks
            Length = $manifestItem.Length
        }
        $sourceManifest = Import-PowerShellDataFile -LiteralPath $manifestPath
        if ([int]($sourceManifest.SchemaVersion) -ne 1 -or [string]($sourceManifest.Group) -ne [string]($group.Name)) {
            throw "Source manifest identity is invalid: $($group.Manifest)"
        }
        $declared = @($sourceManifest.Files | ForEach-Object { ([string]$_).Replace('\','/') })
        $duplicates = @($declared | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
        if ($duplicates.Count) { throw "Duplicate source entries in $($group.Manifest): $($duplicates -join ', ')" }
        $actual = [Collections.Generic.List[string]]::new()
        foreach ($folder in @($group.Folders)) {
            $folderPath = Join-Path $PSScriptRoot ([string]$folder)
            if (-not (Test-Path -LiteralPath $folderPath -PathType Container)) { throw "Required module source directory is missing: $folder" }
            foreach ($file in Get-ChildItem -LiteralPath $folderPath -Filter '*.ps1' -File | Sort-Object Name) {
                $actual.Add([IO.Path]::GetRelativePath($PSScriptRoot, $file.FullName).Replace('\','/'))
            }
        }
        $missing = @($declared | Where-Object { $_ -notin $actual })
        $unexpected = @($actual | Where-Object { $_ -notin $declared })
        if ($missing.Count -or $unexpected.Count) {
            $parts = [Collections.Generic.List[string]]::new()
            if ($missing.Count) { $parts.Add('missing=' + ($missing -join ',')) }
            if ($unexpected.Count) { $parts.Add('unexpected=' + ($unexpected -join ',')) }
            throw "WinCare $($group.Name) source manifest mismatch: $($parts -join '; ')"
        }
        if (($declared -join "`n") -ne (@($actual) -join "`n")) {
            throw "WinCare $($group.Name) source manifest order is not canonical."
        }
        foreach ($relativePath in $declared) { $expectedModuleFiles.Add($relativePath) }
    }
    $duplicateModuleFiles = @($expectedModuleFiles | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicateModuleFiles.Count) { throw ('Source files assigned to multiple groups: ' + ($duplicateModuleFiles -join ', ')) }
    # Write receipt for next load.
    try {
        $receiptObj = [ordered]@{
            SchemaVersion   = 2
            ModuleVersion   = $script:WinCareVersion
            WrittenUtc      = [datetime]::UtcNow.ToString('o')
            Fingerprints    = $newFingerprints
            Files           = @($expectedModuleFiles)
        }
        $receiptJson = $receiptObj | ConvertTo-Json -Depth 5 -Compress
        [IO.File]::WriteAllText($receiptPath, $receiptJson, [Text.Encoding]::UTF8)
    } catch {
        # Receipt write failure is non-fatal — module still loads correctly.
        Write-Verbose "WinCare: could not write load receipt ($($_.Exception.Message))."
    }
}

foreach ($relativePath in $expectedModuleFiles) {
    $sourcePath = Join-Path $PSScriptRoot $relativePath
    . $sourcePath
}
# T2.1 (ponytail/shrink): replaced ~330-line manual list. Public API surface is
# enforced by tools/validate_module_manifest.py and source manifest PSD1 files.
# To make a function private, prefix with lowercase verb (not Verb-Noun) and add
# an explicit Export-ModuleMember deny list after this comment.
Export-ModuleMember -Function *

