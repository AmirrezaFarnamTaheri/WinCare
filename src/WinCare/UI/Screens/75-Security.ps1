function Show-WinCareSecurityOverview {
    $summary=Get-WinCareSecuritySummary
    if (-not $summary) { Show-WinCareMessage -Title 'Security overview' -Lines @('Security status is unavailable on this platform.') -Kind Warning; return }
    $lines=[System.Collections.Generic.List[string]]::new()
    $lines.Add('MICROSOFT DEFENDER')
    $lines.Add("  Available:                 $($summary.Defender.Available)")
    $lines.Add("  Antivirus enabled:        $($summary.Defender.AntivirusEnabled)")
    $lines.Add("  Real-time protection:     $($summary.Defender.RealTimeProtectionEnabled)")
    $lines.Add("  Security intelligence age: $(if ($null -eq $summary.Defender.SignatureAgeDays) {'Unknown'} else {"$($summary.Defender.SignatureAgeDays) day(s)"})")
    $lines.Add("  Quick scan age:           $(if ($null -eq $summary.Defender.QuickScanAgeDays) {'Unknown'} else {"$($summary.Defender.QuickScanAgeDays) day(s)"})")
    $lines.Add("  Full scan age:            $(if ($null -eq $summary.Defender.FullScanAgeDays) {'Unknown'} else {"$($summary.Defender.FullScanAgeDays) day(s)"})")
    $lines.Add('')
    $lines.Add('FIREWALL PROFILES')
    foreach ($profile in @($summary.Firewall)) {
        $lines.Add("  $($profile.Name): enabled=$($profile.Enabled), inbound=$($profile.DefaultInboundAction), outbound=$($profile.DefaultOutboundAction)")
    }
    if (-not @($summary.Firewall).Count) { $lines.Add('  Firewall profile status unavailable.') }
    $lines.Add('')
    $lines.Add("SECURE BOOT: $(if ($null -eq $summary.SecureBoot) {'Unknown or unsupported'} else {$summary.SecureBoot})")
    if ($summary.Tpm) { $lines.Add("TPM: present=$($summary.Tpm.Present), ready=$($summary.Tpm.Ready), enabled=$($summary.Tpm.Enabled)") }
    else { $lines.Add('TPM: status unavailable') }
    Show-WinCareTextPage -Title 'Security overview' -Lines @($lines)
}

function Show-WinCareFirewallProfiles {
    $summary=Get-WinCareSecuritySummary
    if (-not $summary) { Show-WinCareMessage -Title 'Windows Firewall profiles' -Lines @('Firewall status is unavailable on this platform.') -Kind Warning; return }
    $lines=[System.Collections.Generic.List[string]]::new()
    foreach ($profile in @($summary.Firewall)) {
        $lines.Add("$($profile.Name)")
        $lines.Add("  Enabled:          $($profile.Enabled)")
        $lines.Add("  Default inbound:  $($profile.DefaultInboundAction)")
        $lines.Add("  Default outbound: $($profile.DefaultOutboundAction)")
        $lines.Add('')
    }
    if (-not $lines.Count) { $lines.Add('Firewall profile status is unavailable.') }
    Show-WinCareTextPage -Title 'Windows Firewall profiles' -Lines @($lines)
}

function Show-WinCareSecurityScreen {
    while ($true) {
        $defenderAvailable=[bool](Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue)
        $scanAvailable=[bool](Get-Command Start-MpScan -ErrorAction SilentlyContinue)
        $menu=@(
            [pscustomobject]@{Label='Security overview';Description='Defender, firewall, Secure Boot, and TPM posture';Action='Overview'},
            [pscustomobject]@{Label='Update Defender security intelligence';Description='Request the latest available signature update';Action='Update';Disabled=-not $defenderAvailable},
            [pscustomobject]@{Label='Run Defender quick scan';Description='Scan common malware locations';Action='Quick';Disabled=-not $scanAvailable},
            [pscustomobject]@{Label='Run Defender full scan';Description='Scan all accessible files; may run for a long time';Action='Full';Disabled=-not $scanAvailable},
            [pscustomobject]@{Label='Run Defender custom scan';Description='Scan one explicitly selected path';Action='Custom';Disabled=-not $scanAvailable},
            [pscustomobject]@{Label='Firewall profiles';Description='Read-only profile state and default policy';Action='Firewall'},
            [pscustomobject]@{Label='Listening ports';Description='Open the Network port inventory';Action='Ports'},
            [pscustomobject]@{Label='Open Windows Security';Description='Use the native security and protection interface';Action='Windows'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Security and privacy' -Subtitle 'Use Windows-owned protection engines; no heuristic malware claims' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        switch ($choice.Action) {
            'Overview' { Show-WinCareSecurityOverview }
            'Update' { Invoke-WinCarePlanFromUi (New-WinCareDefenderSignatureUpdatePlan) }
            'Quick' { Invoke-WinCarePlanFromUi (New-WinCareDefenderScanPlan -ScanType QuickScan) }
            'Full' { Invoke-WinCarePlanFromUi (New-WinCareDefenderScanPlan -ScanType FullScan) }
            'Custom' {
                Clear-WinCareScreen; Write-WinCareHeader -Title 'Defender custom scan'
                $path=Read-WinCareInput -Prompt 'File or folder to scan' -Default $env:USERPROFILE
                if ($path -and (Test-Path -LiteralPath $path)) { Invoke-WinCarePlanFromUi (New-WinCareDefenderScanPlan -ScanType CustomScan -Path $path) }
                elseif ($path) { Show-WinCareMessage -Title 'Invalid scan path' -Lines @("The path does not exist: $path") -Kind Error }
            }
            'Firewall' { Show-WinCareFirewallProfiles }
            'Ports' {
                $ports=@(Get-WinCareListeningPort)
                $null=Show-WinCareListSelector -Title 'Listening TCP ports' -Subtitle "$($ports.Count) result(s)" -Items $ports -Formatter ${function:Format-WinCarePortRow} -SearchProperties @('Address','Port','Process','PID')
            }
            'Windows' { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @('windowsdefender:') }
        }
    }
}

