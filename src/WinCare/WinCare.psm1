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
    [pscustomobject]@{ Name='Core';      Manifest='SourceManifests/Core.psd1';      Folders=@('Core') }
    [pscustomobject]@{ Name='Providers'; Manifest='SourceManifests/Providers.psd1'; Folders=@('Providers') }
    [pscustomobject]@{ Name='UI';        Manifest='SourceManifests/UI.psd1';         Folders=@('UI','UI/Gui','UI/Screens') }
)

# h01+h05: Receipt cache (v3) — on warm loads, dot-source 3 merged group files instead
# of 172 individual source files. Cold path: validate manifests, merge each group into
# .wincare-merged-{Group}.ps1, write receipt. Warm path: fingerprint check → 3 dot-sources.
# Security: receipt keyed to actual manifest file metadata. Merged files regenerated on any change.
$receiptPath    = Join-Path $PSScriptRoot '.wincare-load-receipt.json'
$receiptHit     = $false
$mergedPaths    = [ordered]@{}   # group name → merged file path (warm path only)

try {
    if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
        $receipt = [IO.File]::ReadAllText($receiptPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
        if ($receipt.SchemaVersion -eq 3 -and [string]$receipt.ModuleVersion -eq $script:WinCareVersion) {
            $fingerprintsMatch = $true
            foreach ($group in $script:WinCareSourceGroups) {
                $manifestPath = Join-Path $PSScriptRoot ([string]$group.Manifest)
                $item = Get-Item -LiteralPath $manifestPath -Force -ErrorAction Stop
                $stored = $receipt.Fingerprints.($group.Name)

                # Deterministic path construction: ignore arbitrary path strings from receipt JSON to prevent path traversal/tampering.
                $mergedFile = Join-Path $PSScriptRoot ".wincare-merged-$($group.Name).ps1"

                if ($null -eq $stored -or
                    [long]$stored.Ticks  -ne $item.LastWriteTimeUtc.Ticks -or
                    [long]$stored.Length -ne $item.Length -or
                    -not (Test-Path -LiteralPath $mergedFile -PathType Leaf)) {
                    $fingerprintsMatch = $false
                    break
                }
                # Check for safe, non-reparse leaf file
                $mergedItem = Get-Item -LiteralPath $mergedFile -Force -ErrorAction Stop
                if ($mergedItem.PSIsContainer -or ($mergedItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    $fingerprintsMatch = $false
                    break
                }
                $mergedPaths[$group.Name] = $mergedFile
            }
            $receiptHit = $fingerprintsMatch
        }
    }
} catch {
    Write-Verbose "WinCare: load receipt unreadable ($($_.Exception.Message)); running full validation."
    $receiptHit = $false
}

if (-not $receiptHit) {
    # Full validation path (first boot or manifest changed).
    $newFingerprints  = [ordered]@{}
    $newMergedFiles   = [ordered]@{}
    $allSourcePaths   = [Collections.Generic.List[string]]::new()

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
        $missing    = @($declared | Where-Object { $_ -notin $actual })
        $unexpected = @($actual   | Where-Object { $_ -notin $declared })
        if ($missing.Count -or $unexpected.Count) {
            $parts = [Collections.Generic.List[string]]::new()
            if ($missing.Count)    { $parts.Add('missing='    + ($missing    -join ',')) }
            if ($unexpected.Count) { $parts.Add('unexpected=' + ($unexpected -join ',')) }
            throw "WinCare $($group.Name) source manifest mismatch: $($parts -join '; ')"
        }
        if (($declared -join "`n") -ne (@($actual) -join "`n")) {
            throw "WinCare $($group.Name) source manifest order is not canonical."
        }
        foreach ($relativePath in $declared) { $allSourcePaths.Add($relativePath) }

        # h05: Merge group files into a single cached .ps1 for fast warm loads.
        $mergedRelative = ".wincare-merged-$($group.Name).ps1"
        $mergedAbs      = Join-Path $PSScriptRoot $mergedRelative
        $mergedTmp      = $mergedAbs + '.tmp'
        try {
            $sb = [Text.StringBuilder]::new(1048576)
            foreach ($rel in $declared) {
                $srcPath = Join-Path $PSScriptRoot $rel
                $null = $sb.AppendLine("# --- merged: $rel ---")
                $null = $sb.AppendLine([IO.File]::ReadAllText($srcPath, [Text.Encoding]::UTF8))
            }
            # Atomic file creation: write to temp file then replace target file
            [IO.File]::WriteAllText($mergedTmp, $sb.ToString(), [Text.Encoding]::UTF8)
            [IO.File]::Move($mergedTmp, $mergedAbs, $true)
            $newMergedFiles[$group.Name] = $mergedRelative
        } catch {
            Remove-Item -LiteralPath $mergedTmp -Force -ErrorAction SilentlyContinue
            # Merge write failure: fall back to individual dot-source for this group.
            Write-Verbose "WinCare: could not write merged cache for $($group.Name) ($($_.Exception.Message)); using individual files."
            $newMergedFiles[$group.Name] = $null
        }
    }

    $duplicateModuleFiles = @($allSourcePaths | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    if ($duplicateModuleFiles.Count) { throw ('Source files assigned to multiple groups: ' + ($duplicateModuleFiles -join ', ')) }

    # Dot-source for this (cold) load using individual files (merged files just written).
    foreach ($relativePath in $allSourcePaths) {
        $sourcePath = Join-Path $PSScriptRoot $relativePath
        . $sourcePath
    }

    # Write receipt v3 ONLY if all group merges succeeded (no null entries).
    if ($newMergedFiles.Values -notcontains $null) {
        try {
            $receiptObj = [ordered]@{
                SchemaVersion = 3
                ModuleVersion = $script:WinCareVersion
                WrittenUtc    = [datetime]::UtcNow.ToString('o')
                Fingerprints  = $newFingerprints
                MergedFiles   = $newMergedFiles
            }
            [IO.File]::WriteAllText($receiptPath, ($receiptObj | ConvertTo-Json -Depth 5 -Compress), [Text.Encoding]::UTF8)
        } catch {
            Write-Verbose "WinCare: could not write load receipt ($($_.Exception.Message))."
        }
    }
} else {
    # Warm path: dot-source 3 merged files instead of 172 individual files.
    foreach ($group in $script:WinCareSourceGroups) {
        $mergedFile = $mergedPaths[$group.Name]
        if ($mergedFile -and (Test-Path -LiteralPath $mergedFile -PathType Leaf)) {
            . $mergedFile
        } else {
            # Merged file missing despite receipt — safety fallback, invalidate on next load.
            Write-Verbose "WinCare: merged cache for $($group.Name) missing; invalidating receipt."
            Remove-Item -LiteralPath $receiptPath -Force -ErrorAction SilentlyContinue
            throw "WinCare merged source cache is inconsistent. Re-run Import-Module to rebuild."
        }
    }
}

# T2.1 (ponytail/shrink): replaced ~330-line manual list. Public API surface is
# enforced by tools/validate_module_manifest.py and source manifest PSD1 files.
# To make a function private, prefix with lowercase verb (not Verb-Noun) and add
# an explicit Export-ModuleMember deny list after this comment.
Export-ModuleMember -Function *
