function Format-WinCareKnowledgeRow {
    param($Item,$Width)
    $title=Limit-WinCareText $Item.title ([math]::Max(28,$Width-18))
    $keywords=Limit-WinCareText ($Item.keywords -join ',') 16
    "$title $keywords"
}

function Show-WinCareKnowledgeScreen {
    while($true) {
        $topic=Show-WinCareListSelector `
            -Title 'Guided troubleshooting' `
            -Subtitle 'Local, source-attributed operational knowledge' `
            -Items @(Get-WinCareKnowledgeBase) `
            -Formatter ${function:Format-WinCareKnowledgeRow} `
            -SearchProperties @('title','keywords','id')
        if(-not $topic) { return }

        $lines=@('Recommended sequence:')
        $lines+=@($topic.steps|ForEach-Object { "- $_" })
        $lines+=@('','Warnings:')
        $lines+=@($topic.warnings|ForEach-Object { "- $_" })
        $lines+=@('','Implementation source: '+($topic.sourceRecords -join ', '))
        Show-WinCareTextPage `
            -Title $topic.title `
            -Lines $lines `
            -Subtitle 'Guidance is version-sensitive; WinCare checks local capabilities before executing actions'
    }
}
