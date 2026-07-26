function Get-WinCareRecentJournal {
    $folder=Join-Path $script:WinCareState.Root 'Journal'
    if (-not (Test-Path -LiteralPath $folder)) { return @() }
    Get-ChildItem -LiteralPath $folder -Filter '*.json' -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 100 | ForEach-Object {
            try {
                $data=Read-WinCareBoundedJson -LiteralPath $_.FullName -MaximumBytes 16777216 -Depth 50
                [pscustomobject]@{ Name=$data.Title; Success=$data.Success; StartedAt=$data.StartedAt; Path=$_.FullName; FullName=$_.FullName }
            } catch { Write-Verbose 'A best-effort operation was unavailable.' }
        }
}

function Format-WinCareJournalRow {
    param([object]$Item,[int]$Width)
    $dateWidth=24; $statusWidth=10; $rest=$Width-$dateWidth-$statusWidth-2
    "$(Limit-WinCareText ([string]$Item.StartedAt) $dateWidth) $(Limit-WinCareText $(if ($Item.Success) {'Success'} else {'Failed'}) $statusWidth) $(Limit-WinCareText $Item.Name $rest)"
}


function Show-WinCareJournalEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Item)
    try {
        $lines=Read-WinCareBoundedLines -LiteralPath $Item.Path -MaximumBytes 16777216 -MaximumLines 250000 -MaximumLineCharacters 262144
        Show-WinCareTextPage -Title 'Journal entry' -Subtitle $Item.Name -Lines @($lines)
    } catch {
        Show-WinCareMessage -Title 'Journal entry' -Lines @('The journal could not be opened safely.', $_.Exception.Message) -Kind Error
    }
}

function Show-WinCareReportsScreen {
    while ($true) {
        $menu=@(
            [pscustomobject]@{ Label='Export JSON report'; Description='Structured system, app, cleanup, startup, and network inventory'; Action='Json' },
            [pscustomobject]@{ Label='Export Markdown report'; Description='Readable support and audit report'; Action='Markdown' },
            [pscustomobject]@{ Label='Export HTML report'; Description='Standalone styled report'; Action='Html' },
            [pscustomobject]@{ Label='Operation history'; Description='Review WinCare journal entries'; Action='History' },
            [pscustomobject]@{ Label='Open data folder'; Description=$script:WinCareState.Root; Action='Folder' },
            [pscustomobject]@{ Label='Back'; Description='Return to main menu'; Action='Back' }
        )
        $choice=Show-WinCareMenu -Title 'Reports and history' -Subtitle 'Auditable output with optional user-name redaction' -Items $menu
        if (-not $choice -or $choice.Action -eq 'Back') { return }
        if ($choice.Action -in @('Json','Markdown','Html')) {
            $ext=switch ($choice.Action) {'Json'{'json'}'Markdown'{'md'}'Html'{'html'}}
            $default=Join-Path (Join-Path $script:WinCareState.Root 'Reports') ("wincare-report-$([datetime]::Now.ToString('yyyyMMdd-HHmmss')).$ext")
            Clear-WinCareScreen; Write-WinCareHeader -Title 'Export report'
            $path=Read-WinCareInput -Prompt 'Output path' -Default $default
            if ($path) { Show-WinCareResult -Result (Export-WinCareSystemReport -Path $path -Format $choice.Action) -Title 'Report export' }
        } elseif ($choice.Action -eq 'History') {
            $journal=@(Get-WinCareRecentJournal)
            if (-not $journal.Count) { Show-WinCareMessage -Title 'Operation history' -Lines @('No operation journal entries exist yet.') -Kind Info }
            else {
                $item=Show-WinCareListSelector -Title 'Operation history' -Items $journal -Formatter ${function:Format-WinCareJournalRow} -SearchProperties @('Name','StartedAt','Success')
                if ($item) { Show-WinCareJournalEntry -Item $item }
            }
        } elseif ($choice.Action -eq 'Folder') { $null=Start-WinCareDetachedProcess -FilePath 'explorer.exe' -ArgumentList @($script:WinCareState.Root) }
    }
}

