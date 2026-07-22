function Show-WinCareHealthFindings {
    $findings=@(Get-WinCareHealthFinding)
    $lines=[System.Collections.Generic.List[string]]::new()
    foreach ($finding in $findings) {
        $lines.Add("[$($finding.Severity)] $($finding.Finding)")
        $lines.Add("  Recommendation: $($finding.Recommendation)")
        $lines.Add('')
    }
    Show-WinCareTextPage -Title 'Health findings' -Lines @($lines)
}

function Show-WinCareHealthScreen {
    while ($true) {
        $menu=@(
            [pscustomobject]@{ Label='Health findings'; Description='Disk pressure, restart state, and security status'; Action='Findings' },
            [pscustomobject]@{ Label='Check Windows image health'; Description='Fast, read-only DISM check'; Action='DismCheck' },
            [pscustomobject]@{ Label='Scan Windows image health'; Description='Deeper, read-only DISM scan'; Action='DismScan' },
            [pscustomobject]@{ Label='Restore Windows image health'; Description='Repair the component image; high-risk servicing operation'; Action='DismRestore' },
            [pscustomobject]@{ Label='Run System File Checker'; Description='Verify and repair protected Windows files'; Action='SfcScan' },
            [pscustomobject]@{ Label='Scan system drive'; Description='Online CHKDSK scan without scheduling repair'; Action='CheckDisk' },
            [pscustomobject]@{ Label='Open Windows Security'; Description='Use the native Microsoft security interface'; Action='Security' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Windows health' -Subtitle 'Check first, repair only when evidence supports it' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Findings' { Show-WinCareHealthFindings }
            'Security' { Start-Process 'windowsdefender:' }
            default { Invoke-WinCarePlanFromUi (New-WinCareHealthPlan $choice.Action) }
        }
    }
}

