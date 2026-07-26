function Show-WinCareUpdatesScreen {
    while ($true) {
        $winget=Test-WinCareWinget
        $version=Get-WinCareWingetVersion
        $menu=@(
            [pscustomobject]@{ Label='Check application upgrades'; Description='Show WinGet upgrade results without changing packages'; Action='Check'; Disabled=-not $winget },
            [pscustomobject]@{ Label='Upgrade all eligible applications'; Description='Review one high-risk batch plan before starting installers'; Action='All'; Disabled=-not $winget },
            [pscustomobject]@{ Label='Upgrade a package by exact ID'; Description='Example: Microsoft.PowerShell'; Action='One'; Disabled=-not $winget },
            [pscustomobject]@{ Label='Export application set'; Description='Create a WinGet JSON restoration manifest'; Action='Export'; Disabled=-not $winget },
            [pscustomobject]@{ Label='Open Windows Update'; Description='Open the native Windows Update page'; Action='WindowsUpdate' },
            [pscustomobject]@{ Label='Back'; Description="WinGet: $(if ($winget) {$version} else {'Unavailable'})"; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Updates and packages' -Subtitle 'Use native Windows and WinGet update paths' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Check' {
                Clear-WinCareScreen; Write-WinCareHeader -Title 'WinGet upgrades'; Write-WinCareText ' WinGet will render its current upgrade table below.' Accent
                $result=Invoke-WinCareWingetListUpgrades
                Wait-WinCareKey -Message "WinGet exit code: $($result.ExitCode). Press any key to return..."
            }
            'All' { Invoke-WinCarePlanFromUi (New-WinCareWingetUpgradeAllPlan) }
            'One' {
                Clear-WinCareScreen; Write-WinCareHeader -Title 'Upgrade package'
                $id=Read-WinCareInput -Prompt 'Exact WinGet package ID'
                if ($id -and $id -match '^[A-Za-z0-9][A-Za-z0-9._+-]{1,127}$') { Invoke-WinCarePlanFromUi (New-WinCareWingetUpgradePackagePlan $id) }
                elseif ($id) { Show-WinCareMessage -Title 'Invalid package ID' -Lines @('Only a valid exact WinGet package identifier is accepted.') -Kind Error }
            }
            'Export' {
                $default=Join-Path (Join-Path $script:WinCareState.Root 'Reports') ("applications-$([datetime]::Now.ToString('yyyyMMdd-HHmmss')).json")
                Clear-WinCareScreen; Write-WinCareHeader -Title 'Export application set'
                $path=Read-WinCareInput -Prompt 'Output JSON path' -Default $default
                if ($path) { Show-WinCareResult -Result (Export-WinCareApplicationSet -Path $path) -Title 'Application export' }
            }
            'WindowsUpdate' { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('ms-settings:windowsupdate') }
        }
    }
}

