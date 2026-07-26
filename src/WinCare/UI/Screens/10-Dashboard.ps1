function Show-WinCareDashboardScreen {
    [CmdletBinding()]
    param()
    Clear-WinCareScreen
    Write-WinCareHeader -Title 'Dashboard' -Subtitle 'System status and recommended next actions'
    Write-WinCareText ' Scanning current system state...' Accent
    $summary = Get-WinCareSystemSummary
    $apps = @(Get-WinCareInstalledApplication)
    $cleanup = @(Get-WinCareCleanupTarget)
    $reclaimable = [long](($cleanup | Measure-Object EstimatedBytes -Sum).Sum)
    $findings = @(Get-WinCareHealthFinding)
    $widgets = @(Get-WinCareWidgetSnapshot -Id @('maintenance-state','security-posture','bluetooth-devices'))

    Clear-WinCareScreen
    Write-WinCareHeader -Title 'Dashboard' -Subtitle 'System status and recommended next actions'
    Write-WinCareText " Computer       $($summary.ComputerName)" Primary
    Write-WinCareText " Windows        $($summary.Windows)  Build $($summary.Build)" Text
    Write-WinCareText " Uptime         $([math]::Floor($summary.Uptime.TotalDays))d $($summary.Uptime.Hours)h $($summary.Uptime.Minutes)m" Text
    Write-WinCareText " CPU            $($summary.Cpu)" Text
    Write-WinCareText " Memory         $(Format-WinCareBytes $summary.MemoryUsed) / $(Format-WinCareBytes $summary.MemoryTotal)" Text
    Write-WinCareText " Applications   $($apps.Count) detected" Text
    Write-WinCareText " Reclaimable    $(Format-WinCareBytes $reclaimable) across $($cleanup.Count) categories" Success
    Write-WinCareText " Defender       $($summary.Defender)" $(if ($summary.Defender -eq 'Protected') {'Success'} else {'Warning'})
    Write-WinCareText " Restart        $(if ($summary.PendingRestart) {'Pending'} else {'Not pending'})" $(if ($summary.PendingRestart) {'Warning'} else {'Success'})
    Write-WinCareText ''
    Write-WinCareText ' LIVE OPERATIONS' Primary
    foreach($widget in $widgets){Write-WinCareText " $($widget.Title) [$($widget.Status)]  $($widget.Summary)" $(if($widget.Status -in @('Critical','Warning','Unavailable')){'Warning'}else{'Text'})}
    Write-WinCareText ''
    Write-WinCareText ' STORAGE' Primary
    foreach ($drive in $summary.Drives) {
        $barWidth = 24
        $filled = [math]::Min($barWidth,[math]::Round($barWidth * $drive.UsedPercent / 100))
        $bar = ('#' * $filled) + ('-' * ($barWidth - $filled))
        Write-WinCareText " $($drive.DeviceId) [$bar] $($drive.UsedPercent)% used  $(Format-WinCareBytes $drive.Free) free" $(if ($drive.UsedPercent -ge 90) {'Danger'} elseif ($drive.UsedPercent -ge 80) {'Warning'} else {'Text'})
    }
    Write-WinCareText ''
    Write-WinCareText ' FINDINGS' Primary
    foreach ($finding in $findings | Select-Object -First 6) {
        $role = switch ($finding.Severity) { 'Critical' {'Danger'} 'Warning' {'Warning'} default {'Text'} }
        Write-WinCareText " [$($finding.Severity)] $($finding.Finding)" $role
        Write-WinCareText "   $($finding.Recommendation)" Muted
    }
    Write-WinCareText ''
    Write-WinCareText ' Shortcuts: A Applications   C Cleanup   S Storage   H Health   U Updates   W Widgets   M Maintenance' Muted
    Write-WinCareFooter -Text 'A/C/S/H/U/W/M Open section   R Refresh   Esc Back'
    while ($true) {
        $key = Read-WinCareKey
        switch ($key.Key) {
            'Escape' { return }
            default {
                switch ([char]::ToUpperInvariant($key.KeyChar)) {
                    'A' { Show-WinCareApplicationsScreen; return }
                    'C' { Show-WinCareCleanupScreen; return }
                    'S' { Show-WinCareStorageScreen; return }
                    'H' { Show-WinCareHealthScreen; return }
                    'U' { Show-WinCareUpdatesScreen; return }
                    'W' { Show-WinCareWidgetsScreen; return }
                    'M' { Show-WinCareMaintenanceScreen; return }
                    'R' { return Show-WinCareDashboardScreen }
                }
            }
        }
    }
}

