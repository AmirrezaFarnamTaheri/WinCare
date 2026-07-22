
function Get-WinCareItemIdentity {
    param([Parameter(Mandatory)][object]$Item)
    if ($Item.PSObject.Properties['Id']) { return [string]$Item.Id }
    if ($Item.PSObject.Properties['FullName']) { return [string]$Item.FullName }
    return "object:$([Runtime.CompilerServices.RuntimeHelpers]::GetHashCode($Item))"
}

function Show-WinCareMenu {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Title,
        [string]$Subtitle='',
        [Parameter(Mandatory)][object[]]$Items,
        [int]$InitialIndex=0,
        [hashtable]$Context=@{}
    )
    $resolved=@(Get-WinCareAdaptiveItems -Items $Items -Context $Context)
    if($resolved.Count -eq 0){return $null}
    $selected=[math]::Min([math]::Max(0,$InitialIndex),$resolved.Count-1);$top=0
    while($true){
        Clear-WinCareScreen;Write-WinCareHeader -Title $Title -Subtitle $Subtitle
        $size=Get-WinCareConsoleSize;$pageSize=[math]::Max(5,$size.Height-9)
        if($selected-lt$top){$top=$selected};if($selected-ge$top+$pageSize){$top=$selected-$pageSize+1}
        $visible=@($resolved|Select-Object -Skip $top -First $pageSize)
        for($i=0;$i-lt$visible.Count;$i++){
            $absolute=$top+$i;$item=$visible[$i];$label=[string]$item.Label;$description=[string]$item.Description
            if($item.Badge){$label+=" [$($item.Badge)]"};if($item.Disabled -and $item.Reason){$description=$item.Reason}
            $prefix=if($absolute-eq$selected){Get-WinCareGlyph -Unicode '› ' -Ascii '> '}else{'  '};$text="$prefix$label";if($description){$text+="  -  $description"};$text=Limit-WinCareText $text ($size.Width-2)
            if($absolute-eq$selected){Write-Host $text -ForegroundColor (Get-WinCareColor SelectedText) -BackgroundColor (Get-WinCareColor SelectedBackground)}elseif($item.Disabled){Write-WinCareText $text Muted}else{Write-WinCareText $text Text}
        }
        Write-WinCareFooter -Text 'Up/Down Move   Enter Select   Ctrl+P Palette   Home/End   Esc Back'
        $key=Read-WinCareKey
        if(($key.Modifiers -band [ConsoleModifiers]::Control) -and $key.Key -eq 'P'){return [pscustomobject]@{Action='__CommandPalette';Label='Command palette'}}
        switch($key.Key){
            'UpArrow'{if($selected-gt 0){$selected--}};'DownArrow'{if($selected-lt$resolved.Count-1){$selected++}};'PageUp'{$selected=[math]::Max(0,$selected-$pageSize)};'PageDown'{$selected=[math]::Min($resolved.Count-1,$selected+$pageSize)};'Home'{$selected=0};'End'{$selected=$resolved.Count-1};'Escape'{return $null};'Enter'{$item=$resolved[$selected];if(-not $item.Disabled){return $item.Original}}
        }
    }
}

function Show-WinCareListSelector {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Title,
        [string]$Subtitle='',
        [Parameter(Mandatory)][object[]]$Items,
        [Parameter(Mandatory)][scriptblock]$Formatter,
        [string[]]$SearchProperties=@('Name'),
        [switch]$MultiSelect,
        [string]$EmptyMessage='No matching items were found.'
    )
    $source = @($Items)
    $filtered = @($source)
    $selected = 0
    $top = 0
    $search = ''
    $checked = [System.Collections.Generic.HashSet[string]]::new()

    while ($true) {
        Clear-WinCareScreen
        $sub = if ($search) { "$Subtitle  Filter: $search" } else { $Subtitle }
        Write-WinCareHeader -Title $Title -Subtitle $sub
        $size = Get-WinCareConsoleSize
        $pageSize = [math]::Max(5,$size.Height - 9)
        if ($filtered.Count -eq 0) {
            Write-WinCareText " $EmptyMessage" Warning
        } else {
            $selected = [math]::Min($selected,$filtered.Count-1)
            if ($selected -lt $top) { $top=$selected }
            if ($selected -ge $top+$pageSize) { $top=$selected-$pageSize+1 }
            $visible = @($filtered | Select-Object -Skip $top -First $pageSize)
            for ($i=0; $i -lt $visible.Count; $i++) {
                $absolute = $top+$i
                $item = $visible[$i]
                $identity = Get-WinCareItemIdentity $item
                $mark = if ($MultiSelect) { if ($checked.Contains($identity)) {'[x]'} else {'[ ]'} } else { '   ' }
                $prefix = if ($absolute -eq $selected) { Get-WinCareGlyph -Unicode '›' -Ascii '>' } else { ' ' }
                $body = & $Formatter $item ($size.Width - 8)
                $line = Limit-WinCareText "$prefix $mark $body" ($size.Width - 2)
                if ($absolute -eq $selected) {
                    Write-Host $line -ForegroundColor (Get-WinCareColor SelectedText) -BackgroundColor (Get-WinCareColor SelectedBackground)
                } else { Write-WinCareText $line Text }
            }
        }
        $footer = if ($MultiSelect) { 'Up/Down Move   Space Toggle   Enter Continue   / Search   Esc Back' } else { 'Up/Down Move   Enter Select   / Search   Esc Back' }
        Write-WinCareFooter -Text $footer
        $key = Read-WinCareKey
        switch ($key.Key) {
            'UpArrow' { if ($selected -gt 0) {$selected--} }
            'DownArrow' { if ($selected -lt $filtered.Count-1) {$selected++} }
            'PageUp' { $selected=[math]::Max(0,$selected-$pageSize) }
            'PageDown' { $selected=[math]::Min([math]::Max(0,$filtered.Count-1),$selected+$pageSize) }
            'Home' { $selected=0 }
            'End' { $selected=[math]::Max(0,$filtered.Count-1) }
            'Spacebar' {
                if ($MultiSelect -and $filtered.Count -gt 0) {
                    $item=$filtered[$selected]
                    $identity = Get-WinCareItemIdentity $item
                    if (-not $checked.Add($identity)) { $null=$checked.Remove($identity) }
                }
            }
            'Enter' {
                if ($filtered.Count -eq 0) { continue }
                if (-not $MultiSelect) { return $filtered[$selected] }
                if ($checked.Count -eq 0) { return @($filtered[$selected]) }
                return @($source | Where-Object {
                    $identity = Get-WinCareItemIdentity $_
                    $checked.Contains($identity)
                })
            }
            'Escape' { return $null }
            default {
                if ($key.KeyChar -eq '/') {
                    Clear-WinCareScreen
                    Write-WinCareHeader -Title $Title
                    $search = Read-WinCareInput -Prompt 'Search' -Default $search
                    if ([string]::IsNullOrWhiteSpace($search)) { $filtered=@($source) }
                    else {
                        $filtered = @($source | Where-Object {
                            $candidate=$_
                            [bool]($SearchProperties | Where-Object {
                                $property = $candidate.PSObject.Properties[$_]
                                $property -and [string]$property.Value -like "*$search*"
                            } | Select-Object -First 1)
                        })
                    }
                    $selected=0; $top=0
                }
            }
        }
    }
}

function Confirm-WinCarePlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Plan)
    Clear-WinCareScreen;Write-WinCareHeader -Title 'Operation preview' -Subtitle $Plan.Title
    $preview=Invoke-WinCarePlan -Plan $Plan -PreviewOnly
    if(-not $preview.Success){Write-WinCareText " Preview failed: $($preview.Message)" Danger;Wait-WinCareKey;return $false}
    $summary=$preview.Data.Summary;$policy=New-WinCareResult -Success $true -Message 'Preview policy checks passed.'
    Write-WinCareText " Actions:            $($summary.Count)" Text
    Write-WinCareText " Highest risk:       $($summary.HighestRisk)" $(if((Get-WinCareRiskRank $summary.HighestRisk)-ge 3){'Danger'}else{'Warning'})
    Write-WinCareText " Requires elevation: $($summary.RequiresAdmin)" Text
    Write-WinCareText " Reversible actions: $($summary.Reversible) / $($summary.Count)" Text
    Write-WinCareText " Estimated data:     $(Format-WinCareBytes $summary.EstimatedBytes)" Text
    Write-WinCareText " Restart possible:   $($summary.RestartPossible)" Text
    Write-WinCareText " Rollback on failure:$($Plan.RollbackOnFailure)" Text
    Write-WinCareText " Evidence records:   $($summary.SourceRecords -join ', ')" Muted
    Write-WinCareText ''
    foreach($action in @($Plan.Actions)){Write-WinCareText (Limit-WinCareText " [$($action.Risk)] $($action.Label) | recovery: $(if($action.Reversible){'available'}else{'not guaranteed'})" ((Get-WinCareConsoleSize).Width-2)) Text}
    Write-WinCareText ''
    if(-not $policy.Success){Write-WinCareText " Policy blocked this plan: $($policy.Message)" Danger;Wait-WinCareKey;return $false}
    if((Get-WinCareConfig 'ReadOnly') -and (Get-WinCareRiskRank $summary.HighestRisk)-gt 0){Write-WinCareText ' Read-only mode prevents modifying actions in this plan.' Warning;Wait-WinCareKey;return $false}
    $legacyProfile=[string](Get-WinCarePropertyValue $Plan.Metadata 'LegacyUnsafeProfile' '')
    if($legacyProfile){
        Write-WinCareText ' This profile can permanently reduce security or remove components. Automatic recovery is incomplete for irreversible AppX removals.' Danger
        $legacyAnswer=Read-WinCareInput -Prompt 'Type I ACCEPT LEGACY UNSAFE MUTATIONS to continue'
        if($legacyAnswer -cne 'I ACCEPT LEGACY UNSAFE MUTATIONS'){return $false}
    }
    $rank=Get-WinCareRiskRank $summary.HighestRisk
    if([bool](Get-WinCarePolicy 'RequireTypedHighRisk')){
        if($rank-ge 4){$answer=Read-WinCareInput -Prompt 'Type APPLY CRITICAL to continue';return $answer -ceq 'APPLY CRITICAL'}
        if($rank-ge 3){$answer=Read-WinCareInput -Prompt 'Type APPLY to continue';return $answer -ceq 'APPLY'}
    }
    $answer=Read-WinCareInput -Prompt 'Apply this plan? (y/N)' -Default 'N';return $answer -match '^(?i)y(es)?$'
}

function Invoke-WinCarePlanFromUi {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Plan)
    if (-not $Plan -or $Plan.Actions.Count -eq 0) {
        Show-WinCareMessage -Title 'No executable actions' -Lines @('The selected items do not expose a supported, safe action.') -Kind Warning
        return
    }
    if (Confirm-WinCarePlan $Plan) {
        Clear-WinCareScreen
        Write-WinCareHeader -Title 'Working' -Subtitle $Plan.Title
        Write-WinCareText ' The operation is running. Native installers may open their own windows.' Accent
        $result = Invoke-WinCarePlan -Plan $Plan -Approved
        Show-WinCareResult -Result $result -Title $Plan.Title
    }
}
