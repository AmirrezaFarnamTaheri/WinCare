function Format-WinCareLocalNoteRow {
    param($Item,$Width)
    $privacy=if([bool]$Item.Private){'Private'}else{'Public'}
    "$(Limit-WinCareText $Item.Title ([math]::Max(24,$Width-38))) $(Limit-WinCareText $privacy 10) $(Limit-WinCareText $Item.UpdatedAt 22)"
}

function Read-WinCareMultilineNote {
    param([string]$Existing='')
    $lines=[Collections.Generic.List[string]]::new()
    if($Existing){foreach($line in ($Existing -split "`r?`n")){$lines.Add($line)}}
    Show-WinCareMessage -Title 'Markdown note editor' -Lines @(
        'Enter one line at a time. Enter a single dot (.) to finish.',
        'Existing content is preloaded. Enter CLEAR as the first new line to replace it.'
    ) -Kind Info
    $first=$true
    while($true){
        $line=Read-WinCareInput -Prompt "Line $($lines.Count+1)" -Default ''
        if($first -and $line -ceq 'CLEAR'){$lines.Clear();$first=$false;continue}
        $first=$false
        if($line -ceq '.'){break}
        $lines.Add($line)
    }
    @($lines)-join "`n"
}

function Show-WinCareLocalNotesScreen {
    while($true){
        $notes=@(Get-WinCareLocalNote)
        $menu=@(
            [pscustomobject]@{Label='Notes';Description="$($notes.Count) local Markdown note(s)";Action='List'},
            [pscustomobject]@{Label='Create note';Description='Plain Markdown body plus strict local metadata';Action='Create'},
            [pscustomobject]@{Label='Edit note';Description='Atomic compare-and-swap update with exact recovery';Action='Edit';Disabled=$notes.Count -eq 0},
            [pscustomobject]@{Label='Remove note';Description='Reversible removal through the transaction journal';Action='Remove';Disabled=$notes.Count -eq 0},
            [pscustomobject]@{Label='Search notes';Description='Local title, tags, and Markdown search; private bodies stay redacted in reports';Action='Search';Disabled=$notes.Count -eq 0},
            [pscustomobject]@{Label='Open notes folder';Description=(Get-WinCareLocalNotesRoot);Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Local notes' -Subtitle 'Local-first Markdown, strict metadata, atomic CAS, private-note redaction, and journal-backed recovery' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        try{
            switch($choice.Action){
                'List'{
                    $selected=Show-WinCareListSelector -Title 'Local notes' -Items $notes -Formatter ${function:Format-WinCareLocalNoteRow} -SearchProperties @('Title','Tags','Private','Id')
                    if($selected){
                        $full=Get-WinCareLocalNote -Id $selected.Id -IncludeBody -IncludePrivateBody|Select-Object -First 1
                        $privacy=if([bool]$full.Private){'Private'}else{'Public'}
                        Show-WinCareTextPage -Title $full.Title -Lines @(
                            "ID: $($full.Id)","Privacy: $privacy","Pinned: $($full.Pinned)",
                            "Created: $($full.CreatedAt)","Updated: $($full.UpdatedAt)",
                            "Tags: $(@($full.Tags)-join ', ')",'',@($full.Body -split "`r?`n")
                        )
                    }
                }
                'Create'{
                    $title=Read-WinCareInput -Prompt 'Title'
                    if($title){
                        $privateChoice=Show-WinCareMenu -Title 'Note privacy' -Items @(
                            [pscustomobject]@{Label='Private';Description='Body is excluded from reports and default inventory views';Value=$true},
                            [pscustomobject]@{Label='Public';Description='Body is available to explicit local note views';Value=$false}
                        )
                        if(-not $privateChoice){continue}
                        $pinChoice=Show-WinCareMenu -Title 'Pin note' -Items @([pscustomobject]@{Label='Not pinned';Description='Normal note ordering';Value=$false},[pscustomobject]@{Label='Pinned';Description='Keep the note at the top';Value=$true});if(-not $pinChoice){continue};$pinned=[bool]$pinChoice.Value
                        $tagsText=Read-WinCareInput -Prompt 'Comma-separated tags' -Default ''
                        $body=Read-WinCareMultilineNote
                        Invoke-WinCarePlanFromUi (New-WinCareLocalNotePlan -Title $title -Body $body -Tags @($tagsText -split ','|ForEach-Object{$_.Trim()}|Where-Object{$_}) -Private ([bool]$privateChoice.Value) -Pinned $pinned)
                    }
                }
                'Edit'{
                    $selected=Show-WinCareListSelector -Title 'Edit note' -Items $notes -Formatter ${function:Format-WinCareLocalNoteRow} -SearchProperties @('Title','Tags')
                    if($selected){
                        $full=Get-WinCareLocalNote -Id $selected.Id -IncludeBody -IncludePrivateBody|Select-Object -First 1
                        $title=Read-WinCareInput -Prompt 'Title' -Default $full.Title
                        $tagsText=Read-WinCareInput -Prompt 'Comma-separated tags' -Default (@($full.Tags)-join ',')
                        $body=Read-WinCareMultilineNote -Existing $full.Body
                        Invoke-WinCarePlanFromUi (New-WinCareLocalNotePlan -Id $full.Id -Title $title -Body $body -Tags @($tagsText -split ','|ForEach-Object{$_.Trim()}|Where-Object{$_}) -Private ([bool]$full.Private) -Pinned ([bool]$full.Pinned) -LinkedOperationIds @($full.LinkedOperationIds) -SourceRecords @($full.SourceRecords))
                    }
                }
                'Remove'{
                    $selected=Show-WinCareListSelector -Title 'Remove note' -Items $notes -Formatter ${function:Format-WinCareLocalNoteRow} -SearchProperties @('Title','Tags')
                    if($selected){Invoke-WinCarePlanFromUi (New-WinCareLocalNoteRemovePlan -Id $selected.Id)}
                }
                'Search'{
                    $query=Read-WinCareInput -Prompt 'Search text'
                    if($query){
                        $matches=@(Search-WinCareLocalNote -Query $query -IncludePrivateBody)
                        $selected=Show-WinCareListSelector -Title 'Search results' -Items $matches -Formatter ${function:Format-WinCareLocalNoteRow} -SearchProperties @('Title','Tags')
                        if($selected){$full=Get-WinCareLocalNote -Id $selected.Id -IncludeBody -IncludePrivateBody|Select-Object -First 1;Show-WinCareTextPage -Title $full.Title -Lines @($full.Body -split "`r?`n")}
                    }
                }
                'Folder'{Start-Process (Get-WinCareLocalNotesRoot)}
            }
        }catch{Show-WinCareMessage -Title 'Note operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

