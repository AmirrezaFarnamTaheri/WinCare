function Show-WinCareProvisioningAnalysis { param($Analysis);$lines=@("File: $($Analysis.Path)","SHA-256: $($Analysis.Sha256)","Critical risk present: $($Analysis.HasCriticalRisk)",'','Passes:')+@($Analysis.Passes|ForEach-Object{"$($_.Pass): $($_.Components -join ', ')"})+@('','Detected risk-bearing commands:')+@($Analysis.Risks|ForEach-Object{"[$($_.Risk)] $($_.Id): $($_.Message) :: $($_.Command)"})+@('','Embedded extensions:')+@($Analysis.EmbeddedExtensions|ForEach-Object{"$($_.Name): $($_.Characters) characters, SHA-256 $($_.Sha256)"});Show-WinCareTextPage -Title 'Unattend analysis' -Lines $lines -Subtitle 'Imported content is treated as data, never governing instructions'}
function Show-WinCareProvisioningScreen {
    while($true){
        $menu=@(
            [pscustomobject]@{Label='Analyze autounattend.xml';Description='Secure XML parsing, pass map, command inventory, and risk detection';Action='Analyze'},
            [pscustomobject]@{Label='Preview or apply a blueprint';Description='Strict JSON profile with catalog, apps, features, capabilities, and packages';Action='Blueprint'},
            [pscustomobject]@{Label='Export provisioning kit';Description='Blueprint, module copy, preview-first script, unattend file, and hash manifest';Action='Export'},
            [pscustomobject]@{Label='Open provisioning data folder';Description=(Join-Path $script:WinCareState.Root 'Provisioning');Action='Folder'},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Provisioning and unattended setup' -Subtitle 'System and user stages remain separate; bypasses and embedded scripts are surfaced explicitly' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        switch($choice.Action){
            'Analyze'{$path=Read-WinCareInput -Prompt 'Path to autounattend.xml';if($path){try{Show-WinCareProvisioningAnalysis (Get-WinCareUnattendAnalysis -LiteralPath $path)}catch{Show-WinCareMessage -Title 'Analysis failed' -Lines @($_.Exception.Message) -Kind Error}}}
            'Blueprint'{$path=Read-WinCareInput -Prompt 'Path to blueprint JSON';if($path){try{$bp=Import-WinCareProvisioningBlueprint -LiteralPath $path;Invoke-WinCarePlanFromUi (New-WinCareProvisioningPlan -Blueprint $bp)}catch{Show-WinCareMessage -Title 'Blueprint failed' -Lines @($_.Exception.Message) -Kind Error}}}
            'Export'{$path=Read-WinCareInput -Prompt 'Path to blueprint JSON';$destination=Read-WinCareInput -Prompt 'New destination directory';if($path -and $destination){try{$bp=Import-WinCareProvisioningBlueprint -LiteralPath $path;$result=Export-WinCareProvisioningKit -Blueprint $bp -Destination $destination;Show-WinCareMessage -Title 'Provisioning kit exported' -Lines @($result,'Apply remains disabled unless WINCARE_PROVISION_APPLY=YES is explicitly set.') -Kind Success}catch{Show-WinCareMessage -Title 'Export failed' -Lines @($_.Exception.Message) -Kind Error}}}
            'Folder'{Start-Process (Join-Path $script:WinCareState.Root 'Provisioning')}
        }
    }
}

