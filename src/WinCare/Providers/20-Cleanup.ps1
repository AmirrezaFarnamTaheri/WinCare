#requires -Version 7.2

function New-WinCareCleanupPlan {
    [CmdletBinding()]
    param(
        [object[]]$Targets = @(),
        [switch]$IncludeRecycleBin
    )
    if (-not $Targets.Count) {
        $Targets = @(Get-WinCareCleanupTarget -Refresh | Where-Object { $_.FileCount -gt 0 })
    }
    $actions = [System.Collections.Generic.List[object]]::new()
    foreach ($target in $Targets) {
        $name = if ($target.Title) { [string]$target.Title } elseif ($target.Name) { [string]$target.Name } else { [string]$target.Id }
        $actions.Add((New-WinCareAction -Type 'DeleteCleanupTarget' -Label "Clean $name" -Risk Low -Parameters @{
            Id = [string]$target.Id
            Name = $name
            Path = [string]$target.Path
            Pattern = if ($target.Pattern) { [string]$target.Pattern } else { '*' }
            MinimumAgeHours = [int]$target.MinimumAgeHours
            Kind = if ($target.Kind) { [string]$target.Kind } else { 'Files' }
        } -RequiresAdmin:$false -Reversible:$false -EstimatedBytes ([long]$target.EstimatedBytes) -Verification 'Every candidate must remain inside the canonical cleanup root, satisfy the age and pattern filters, and be absent after deletion.' -Tags @('Cleanup','Bounded','NonReversible') -RecoveryDescription 'Deleted temporary files are not automatically recoverable.'))
    }
    if ($IncludeRecycleBin) {
        $actions.Add((New-WinCareAction -Type 'ClearRecycleBin' -Label 'Empty Recycle Bin' -Risk High -Parameters @{} -RequiresAdmin:$false -Reversible:$false -Verification 'The platform recycle-bin provider must report completion.' -Tags @('Cleanup','NonReversible') -RecoveryDescription 'Recycle Bin contents are not automatically recoverable.'))
    }
    New-WinCarePlan -Title 'Bounded cleanup' -Description "Deletes files from $($actions.Count) explicitly selected cleanup actions." -Actions @($actions) -StopOnFailure:$true -RollbackOnFailure:$false
}

function Invoke-WinCareCleanupAction {
    [CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]
    param([Parameter(Mandatory)][object]$Action)

    $parameters = ConvertTo-WinCareParameterDictionary $Action.Parameters
    $root = Assert-WinCareSafePath -LiteralPath ([string]$parameters.Path) -AllowedRoots @([string]$parameters.Path)
    $pathRoot = [IO.Path]::GetPathRoot($root)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ([string]::Equals($root.TrimEnd([IO.Path]::DirectorySeparatorChar), $pathRoot.TrimEnd([IO.Path]::DirectorySeparatorChar), $comparison)) {
        return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupRootDenied' -Message 'Drive and filesystem roots cannot be cleanup targets.' -ExitCode 77
    }
    $pattern = [string]$parameters.Pattern
    if ([string]::IsNullOrWhiteSpace($pattern)) { $pattern = '*' }
    $minimumAge = [int]$parameters.MinimumAgeHours
    $cutoff = [datetime]::UtcNow.AddHours(-$minimumAge)
    $kind = [string]$parameters.Kind
    $maximum = [long](Get-WinCarePolicy 'MaximumEstimatedBytes')
    if ($maximum -le 0) { $maximum = 2GB }

    $candidates = [System.Collections.Generic.List[object]]::new()
    $totalBytes = 0L
    try {
        foreach ($item in Get-ChildItem -LiteralPath $root -Filter $pattern -File -Recurse -Force -ErrorAction SilentlyContinue) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $canonical = Assert-WinCareSafePath -LiteralPath $item.FullName -AllowedRoots @($root)
            if ($item.LastWriteTimeUtc -gt $cutoff) { continue }
            $totalBytes += [long]$item.Length
            if ($totalBytes -gt $maximum) {
                return New-WinCareResult -Success $false -Status Blocked -Code 'CleanupSizeExceedsPolicy' -Message "Cleanup candidates exceed the configured maximum of $maximum bytes." -ExitCode 5 -Data @{Root=$root;CandidateBytes=$totalBytes;EvidenceType='BoundedCleanupInventory'}
            }
            $candidates.Add([pscustomobject]@{Path=$canonical;Length=[long]$item.Length;LastWriteTimeUtc=$item.LastWriteTimeUtc;Sha256=(Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash.ToLowerInvariant()})
        }
    } catch {
        return New-WinCareResult -Success $false -Code 'CleanupInventoryFailed' -Message $_.Exception.Message -ExitCode 1
    }

    if (-not $candidates.Count) {
        return New-WinCareResult -Success $true -Code 'CleanupAlreadySatisfied' -Message 'No files matched the bounded cleanup target.' -Data @{Root=$root;Pattern=$pattern;MinimumAgeHours=$minimumAge;DeletedFiles=0;DeletedBytes=0;EvidenceType='BoundedCleanupInventory'}
    }
    if (-not $PSCmdlet.ShouldProcess($root, "Delete $($candidates.Count) files totaling $totalBytes bytes")) {
        return New-WinCareResult -Success $false -Status Cancelled -Code 'Cancelled' -Message 'Cleanup was cancelled.' -ExitCode 125
    }

    $deleted = 0; $deletedBytes = 0L; $failures = [System.Collections.Generic.List[string]]::new()
    foreach ($candidate in $candidates) {
        try {
            $current = Get-Item -LiteralPath $candidate.Path -Force -ErrorAction Stop
            if ($current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Candidate is no longer a regular file.' }
            if ($current.Length -ne $candidate.Length -or $current.LastWriteTimeUtc -ne $candidate.LastWriteTimeUtc) { throw 'Candidate changed after inventory.' }
            $currentHash = (Get-FileHash -LiteralPath $candidate.Path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($currentHash -ne $candidate.Sha256) { throw 'Candidate content changed after inventory.' }
            Remove-Item -LiteralPath $candidate.Path -Force -ErrorAction Stop
            if (Test-Path -LiteralPath $candidate.Path) { throw 'Candidate remained after deletion.' }
            $deleted++; $deletedBytes += $candidate.Length
        } catch { $failures.Add("$($candidate.Path): $($_.Exception.Message)") }
    }
    $success = $failures.Count -eq 0
    New-WinCareResult -Success $success -Status $(if($success){'Succeeded'}else{'Failed'}) -Code $(if($success){'CleanupTargetDeleted'}else{'CleanupTargetPartialFailure'}) -Message $(if($success){'All bounded cleanup candidates were deleted and absence-verified.'}else{'One or more bounded cleanup candidates could not be safely deleted.'}) -ExitCode $(if($success){0}else{1}) -Warnings @($failures) -Data @{
        TargetId=[string]$parameters.Id;TargetName=[string]$parameters.Name;Root=$root;Pattern=$pattern;Kind=$kind;MinimumAgeHours=$minimumAge
        CandidateFiles=$candidates.Count;CandidateBytes=$totalBytes;DeletedFiles=$deleted;DeletedBytes=$deletedBytes;EvidenceType='HashCheckedDeletionResults'
    }
}
