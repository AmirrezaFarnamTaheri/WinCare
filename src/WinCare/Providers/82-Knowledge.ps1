function Get-WinCareKnowledgeBase {
    [CmdletBinding()]param([string]$Topic)
    $path=Join-Path $script:WinCareModuleRoot 'Data\Knowledge\topics.json'
    $document=Read-WinCareJsonHashtable -LiteralPath $path
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','topics') -Context 'knowledge base'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported knowledge-base schema.'}
    $topics=@($document.topics);if(Get-Command Get-WinCareExtensionKnowledgeTopic -ErrorAction SilentlyContinue){$topics+=@(Get-WinCareExtensionKnowledgeTopic)}
    if($Topic){$topics=@($topics|Where-Object{$_.id -like "*$Topic*" -or $_.title -like "*$Topic*" -or @($_.keywords) -contains $Topic})}
    return $topics
}


