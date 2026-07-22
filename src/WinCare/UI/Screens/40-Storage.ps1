function Format-WinCareLargeFileRow {
    param([object]$File,[int]$Width)
    $sizeWidth=12; $dateWidth=12; $nameWidth=$Width-$sizeWidth-$dateWidth-2
    "$(Limit-WinCareText $File.FullName $nameWidth) $(Limit-WinCareText (Format-WinCareBytes $File.Length) $sizeWidth) $(Limit-WinCareText $File.LastWriteTime.ToString('yyyy-MM-dd') $dateWidth)"
}

function Show-WinCareDriveOverview {
    $drives=@(Get-WinCareDriveSummary)
    $lines=[System.Collections.Generic.List[string]]::new()
    foreach ($drive in $drives) {
        $lines.Add("$($drive.DeviceId)  $($drive.Label)  $($drive.FileSystem)")
        $lines.Add("  Used: $(Format-WinCareBytes $drive.Used) ($($drive.UsedPercent)%)")
        $lines.Add("  Free: $(Format-WinCareBytes $drive.Free) of $(Format-WinCareBytes $drive.Size)")
        $lines.Add('')
    }
    Show-WinCareTextPage -Title 'Drive overview' -Lines @($lines)
}

function Show-WinCareLargeFileScreen {
    Clear-WinCareScreen
    Write-WinCareHeader -Title 'Large file finder'
    $defaultPath=if ($env:USERPROFILE) {$env:USERPROFILE} else {$env:SystemDrive}
    $path=Read-WinCareInput -Prompt 'Folder to scan' -Default $defaultPath
    if (-not (Test-Path -LiteralPath $path)) { Show-WinCareMessage -Title 'Invalid path' -Lines @("The path does not exist: $path") -Kind Error; return }
    $thresholdText=Read-WinCareInput -Prompt 'Minimum size in GB' -Default ([string](Get-WinCareConfig 'LargeFileThresholdGB'))
    $threshold=1.0
    if (-not [double]::TryParse($thresholdText,[ref]$threshold) -or $threshold -le 0) { $threshold=1.0 }
    Clear-WinCareScreen; Write-WinCareHeader -Title 'Large file finder'; Write-WinCareText " Scanning $path ..." Accent
    $files=@(Find-WinCareLargeFile -Path $path -MinimumBytes ([long]($threshold*1GB)))
    if (-not $files.Count) { Show-WinCareMessage -Title 'Large files' -Lines @("No files at least $threshold GB were found.") -Kind Info; return }
    $file=Show-WinCareListSelector -Title 'Large files' -Subtitle "$($files.Count) result(s); no files are deleted automatically" -Items $files -Formatter ${function:Format-WinCareLargeFileRow} -SearchProperties @('Name','FullName','Extension')
    if ($file) {
        $menu=@(
            [pscustomobject]@{ Label='Open containing folder'; Description='Select the file in File Explorer'; Action='Open' },
            [pscustomobject]@{ Label='Move to another folder'; Description='Create a reversible move operation'; Action='Move' },
            [pscustomobject]@{ Label='Back'; Description='Leave the file unchanged'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'File action' -Subtitle $file.FullName -Items $menu
        if (-not $choice) { return }
        if ($choice.Action -eq 'Open') { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('/select,', $file.FullName) }
        elseif ($choice.Action -eq 'Move') {
            Clear-WinCareScreen; Write-WinCareHeader -Title 'Move file'
            $destinationFolder=Read-WinCareInput -Prompt 'Destination folder'
            if ($destinationFolder) {
                $source=Assert-WinCareSafePath -LiteralPath $file.FullName
                $sourceRoot=Assert-WinCareSafePath -LiteralPath (Split-Path -Parent $source)
                $destinationRoot=Assert-WinCareSafePath -LiteralPath $destinationFolder
                $destination=Assert-WinCareSafePath -LiteralPath (Join-Path $destinationRoot $file.Name) -AllowedRoots @($destinationRoot) -AllowMissing
                if(Test-WinCarePathWithin -Child $destination -Parent $source -AllowEqual){Show-WinCareMessage -Title 'Move blocked' -Message 'The destination cannot be the source or a child of it.' -Kind Error;continue}
                $contentHash=Get-WinCarePathContentHash -LiteralPath $source
                $allowedRoots=@($sourceRoot,$destinationRoot|Sort-Object -Unique)
                $parameters=@{Source=$source;Destination=$destination;ExpectedSourceHash=$contentHash;AllowedRoots=$allowedRoots}
                $compensator=@{Type='MoveFile';Parameters=@{Source=$destination;Destination=$source;ExpectedSourceHash=$contentHash;AllowedRoots=$allowedRoots}}
                $action=New-WinCareAction -Type 'MoveFile' -Label "Move $($file.Name)" -Risk Moderate -Parameters $parameters -Reversible $true -Compensator $compensator -EstimatedBytes $file.Length -Verification 'Source is absent and the destination content hash matches the approved source.' -RecoveryDescription 'Move the exact content-verified item back to its original location.'
                Invoke-WinCarePlanFromUi (New-WinCarePlan -Title 'Move large file' -Actions @($action))
            }
        }
    }
}

function Show-WinCareStorageSenseScreen {
    $state=Get-WinCareStorageSenseState
    $menu=@(
        [pscustomobject]@{ Label=$(if ($state.Enabled) {'Disable Storage Sense'} else {'Enable Storage Sense'}); Description="Current state: $(if ($state.Enabled) {'Enabled'} else {'Disabled'})"; Action='Toggle' },
        [pscustomobject]@{ Label='Open Storage Sense settings'; Description='Configure retention and schedule in Windows Settings'; Action='Settings' },
        [pscustomobject]@{ Label='Back'; Description='Return'; Action='Back' }
    )
    $choice=Show-WinCareMenu -Title 'Storage Sense' -Subtitle 'Use the Windows-owned automatic cleanup engine' -Items $menu
    if (-not $choice) { return }
    if ($choice.Action -eq 'Toggle') { Invoke-WinCarePlanFromUi (New-WinCareStorageSensePlan -Enable (-not $state.Enabled)) }
    elseif ($choice.Action -eq 'Settings') { Open-WinCareStorageSettings }
}

function Show-WinCareStorageScreen {
    while ($true) {
        $menu=@(
            [pscustomobject]@{ Label='Drive overview'; Description='Capacity, free space, file system, and utilization'; Action='Drives' },
            [pscustomobject]@{ Label='Find large files'; Description='Search a selected folder with a configurable threshold'; Action='Large' },
            [pscustomobject]@{ Label='Storage Sense'; Description='Inspect, toggle, or open native configuration'; Action='Sense' },
            [pscustomobject]@{ Label='Open Windows Storage settings'; Description='Open the native storage overview'; Action='Settings' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Storage' -Subtitle 'Analyze before moving or removing data' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) { 'Drives'{Show-WinCareDriveOverview} 'Large'{Show-WinCareLargeFileScreen} 'Sense'{Show-WinCareStorageSenseScreen} 'Settings'{Start-Process 'ms-settings:storage'} }
    }
}

