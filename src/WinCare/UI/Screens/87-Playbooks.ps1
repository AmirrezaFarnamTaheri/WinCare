function Format-WinCarePlaybookRow {
    param($Item,$Width)
    $title=Limit-WinCareText $Item.title ([math]::Max(24,$Width-38))
    "$title $($Item.risk) $(@($Item.steps).Count) step(s)"
}

function Show-WinCarePlaybooksScreen {
    while($true) {
        $playbooks=@(Get-WinCarePlaybook)
        $menu=@(
            [pscustomobject]@{
                Label='Playbook catalog'
                Description="$($playbooks.Count) strict declarative playbook(s)"
                Action='List'
            },
            [pscustomobject]@{
                Label='Preview or apply playbook'
                Description='Composes existing WinCare plans; no scripts, macros, or arbitrary commands'
                Action='Apply'
            },
            [pscustomobject]@{
                Label='Back'
                Description='Return to main menu'
                Action='Back'
            }
        )
        $choice=Show-WinCareMenu `
            -Title 'Declarative playbooks' `
            -Subtitle 'Catalog, app, menu, and exact package identities through one action engine' `
            -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back') { return }

        $selected=Show-WinCareListSelector `
            -Title 'Playbooks' `
            -Items $playbooks `
            -Formatter ${function:Format-WinCarePlaybookRow} `
            -SearchProperties @('id','title','description','risk')
        if(-not $selected) { continue }

        if($choice.Action -eq 'List') {
            $compatibility=Test-WinCarePlaybookCompatibility $selected
            $lines=@(
                $selected.description,
                "ID: $($selected.id)",
                "Reason: $($compatibility.Reason)",
                '',
                'Steps:'
            )
            $lines+=@($selected.steps|ForEach-Object { "$($_.type): $($_.id)" })
            $lines+=@('','Implementation source:')
            $lines+=@($selected.sourceRecords)
            Show-WinCareTextPage `
                -Title $selected.title `
                -Subtitle "risk=$($selected.risk), compatible=$($compatibility.Compatible)" `
                -Lines $lines
        } else {
            try {
                Invoke-WinCarePlanFromUi (New-WinCarePlaybookPlan -PlaybookId $selected.id)
            } catch {
                Show-WinCareMessage -Title 'Playbook blocked' -Lines @($_.Exception.Message) -Kind Error
            }
        }
    }
}
