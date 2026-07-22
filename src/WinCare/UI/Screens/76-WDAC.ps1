function Show-WinCareWdacScreen {
    while($true){
        $active=@(Get-WinCareWdacActivePolicy);$menu=@(
            [pscustomobject]@{Label='Active policies';Description="$($active.Count) active CIP policy file(s)";Action='Active'},
            [pscustomobject]@{Label='Validate a policy';Description='Inspect ID, base policy, options, audit mode, and hash';Action='Validate'},
            [pscustomobject]@{Label='Convert XML to CIP';Description='Use the documented ConvertFrom-CIPolicy cmdlet';Action='Convert';Disabled=-not (Get-Command ConvertFrom-CIPolicy -ErrorAction SilentlyContinue)},
            [pscustomobject]@{Label='Deploy policy';Description='Audit mode is preferred; enforcement is policy-gated';Action='Deploy'},
            [pscustomobject]@{Label='Code Integrity events';Description='Operational allow/deny evidence';Action='Events'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Windows Defender Application Control' -Subtitle 'Validate -> audit -> event review -> explicit promotion -> recovery' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Active'{$lines=@($active|ForEach-Object{"$($_.PolicyId) | $(Format-WinCareBytes $_.Bytes) | $($_.Sha256) | $($_.Path)"});Show-WinCareTextPage -Title 'Active WDAC policies' -Lines $(if($lines.Count){$lines}else{@('No active CIP files were found.')})}
            'Validate'{$path=Read-WinCareInput -Prompt 'Policy path';if($path){try{$a=Test-WinCareWdacPolicy -LiteralPath $path;Show-WinCareTextPage -Title 'WDAC policy analysis' -Lines @("Path: $($a.Path)","Type: $($a.Type)","Policy ID: $($a.PolicyId)","Base policy ID: $($a.BasePolicyId)","Audit mode: $($a.AuditMode)","Valid: $($a.Valid)","SHA-256: $($a.Sha256)",'',"Options:")+@($a.Options)+@('')+@($a.Warnings)}catch{Show-WinCareMessage -Title 'Validation failed' -Lines @($_.Exception.Message) -Kind Error}}}
            'Convert'{$source=Read-WinCareInput -Prompt 'XML policy path';$destination=Read-WinCareInput -Prompt 'New CIP destination';if($source -and $destination){try{$r=Convert-WinCareWdacPolicy -XmlPath $source -Destination $destination;Show-WinCareTextPage -Title 'WDAC conversion' -Lines @("Path: $($r.Path)","SHA-256: $($r.Sha256)","Policy ID: $($r.PolicyId)","Audit mode: $($r.AuditMode)")}catch{Show-WinCareMessage -Title 'Conversion failed' -Lines @($_.Exception.Message) -Kind Error}}}
            'Deploy'{$path=Read-WinCareInput -Prompt 'Policy XML/CIP path';if($path){try{Invoke-WinCarePlanFromUi (New-WinCareWdacDeployPlan -PolicyPath $path)}catch{Show-WinCareMessage -Title 'Deployment blocked' -Lines @($_.Exception.Message) -Kind Error}}}
            'Events'{$lines=@(Get-WinCareCodeIntegrityEvent -MaxEvents 250|ForEach-Object{"$($_.TimeCreated.ToString('u')) | $($_.Id) | $($_.Level) | $($_.Message -replace '\s+',' ')"});Show-WinCareTextPage -Title 'Code Integrity events' -Lines $(if($lines.Count){$lines}else{@('No matching events were returned.')})}
        }
    }
}

