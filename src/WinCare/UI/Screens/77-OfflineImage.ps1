function Format-WinCareMountedImageRow {param($Item,$Width);"$(Limit-WinCareText $Item.ImageName ([math]::Max(22,$Width-42))) $($Item.ImageIndex) $($Item.MountStatus) $(Limit-WinCareText $Item.Path 32)"}
function Show-WinCareOfflineImageScreen {
    while($true){
        $images=try{@(Get-WinCareMountedWindowsImage)}catch{@()}
        $menu=@(
            [pscustomobject]@{Label='Mounted images';Description="$($images.Count) registered mounted image(s)";Action='Images'},
            [pscustomobject]@{Label='Inspect drivers';Description='All inbox and third-party driver packages';Action='Drivers';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Inspect packages';Description='Offline package state, applicability, and restart flags';Action='Packages';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Inspect optional features';Description='Feature names and current states';Action='Features';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Add driver';Description='Supported DISM provider with before/after package discovery';Action='AddDriver';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Remove driver';Description='Blocks inbox and boot-critical packages';Action='RemoveDriver';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Add CAB or MSU package';Description='Critical, image-backup recovery only';Action='AddPackage';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Enable or disable feature';Description='Reversible exact feature-state transition';Action='Feature';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Reduction assessment';Description='Exact provisioned-AppX and optional-feature impact for governed profiles';Action='ReductionAssess';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Apply reduction profile';Description='Supported AppX/DISM operations; irreversible removals disclosed';Action='ReductionApply';Disabled=$images.Count -eq 0},
            [pscustomobject]@{Label='Back';Description='Return to main menu';Action='Back'}
        )
        $choice=Show-WinCareMenu -Title 'Offline Windows image servicing' -Subtitle 'Only registered mounted images are accepted; servicing is disabled by default policy' -Items $menu
        if(-not $choice -or $choice.Action -eq 'Back'){return}
        $image=$null;if($choice.Action -ne 'Images'){$image=Show-WinCareListSelector -Title 'Select mounted image' -Items $images -Formatter ${function:Format-WinCareMountedImageRow} -SearchProperties @('ImageName','Path','ImageFile');if(-not $image){continue}}
        try{
            switch($choice.Action){
                'Images'{$lines=@($images|ForEach-Object{"$($_.ImageName) [$($_.ImageIndex)] | $($_.MountStatus) | readWrite=$($_.ReadWrite) | $($_.Path) | $($_.ImageFile)"});Show-WinCareTextPage -Title 'Mounted Windows images' -Lines $(if($lines.Count){$lines}else{@('No mounted Windows images were found.')})}
                'Drivers'{$rows=@(Get-WinCareOfflineImageDriver -ImagePath $image.Path|ForEach-Object{"$($_.PublishedName) | $($_.ProviderName) | $($_.ClassName) | $($_.Version) | inbox=$($_.Inbox) | bootCritical=$($_.BootCritical)"});Show-WinCareTextPage -Title 'Offline drivers' -Lines $rows}
                'Packages'{$rows=@(Get-WinCareOfflineImagePackage -ImagePath $image.Path|ForEach-Object{"$($_.PackageState) | $($_.ReleaseType) | restart=$($_.RestartRequired) | $($_.PackageName)"});Show-WinCareTextPage -Title 'Offline packages' -Lines $rows}
                'Features'{$rows=@(Get-WinCareOfflineImageFeature -ImagePath $image.Path|ForEach-Object{"$($_.State) | restart=$($_.RestartRequired) | $($_.FeatureName)"});Show-WinCareTextPage -Title 'Offline features' -Lines $rows}
                'AddDriver'{$path=Read-WinCareInput -Prompt 'INF file or driver directory';$recurse=Read-WinCareInput -Prompt 'Recurse directory? (y/N)' -Default 'N';if($path){Invoke-WinCarePlanFromUi (New-WinCareOfflineDriverAddPlan -ImagePath $image.Path -DriverPath $path -Recurse:($recurse -match '^(?i)y'))}}
                'RemoveDriver'{$name=Read-WinCareInput -Prompt 'Published driver name (oemN.inf)';if($name){Invoke-WinCarePlanFromUi (New-WinCareOfflineDriverRemovePlan -ImagePath $image.Path -PublishedName $name)}}
                'AddPackage'{$path=Read-WinCareInput -Prompt 'CAB or MSU package path';if($path){Invoke-WinCarePlanFromUi (New-WinCareOfflinePackageAddPlan -ImagePath $image.Path -PackagePath $path)}}
                'Feature'{$name=Read-WinCareInput -Prompt 'Feature name';$state=Read-WinCareInput -Prompt 'Desired state (Enabled/Disabled)' -Default 'Enabled';if($name -and $state -in @('Enabled','Disabled')){Invoke-WinCarePlanFromUi (New-WinCareOfflineFeaturePlan -ImagePath $image.Path -FeatureName $name -State $state)}}
                'ReductionAssess'{$profiles=@(Get-WinCareOfflineReductionProfile);$profile=Show-WinCareListSelector -Title 'Reduction profile' -Items $profiles -Formatter {param($Item,$Width);"$(Limit-WinCareText $Item.Id 20) $(Limit-WinCareText $Item.Name 24) $(Limit-WinCareText $Item.Description ([math]::Max(30,$Width-48)))"} -SearchProperties @('Id','Name','Description');if($profile){$assessment=Get-WinCareOfflineReductionAssessment -ImagePath $image.Path -ProfileId $profile.Id;Show-WinCareTextPage -Title "Reduction assessment: $($profile.Name)" -Lines @("Provisioned packages selected: $($assessment.ProvisionedAppx.Count)","Enabled optional features selected: $($assessment.Features.Count)","Irreversible package removals: $($assessment.IrreversibleAppxCount)","Component cleanup: $($assessment.ComponentCleanup)",'Packages:')+@($assessment.ProvisionedAppx|ForEach-Object{"  $($_.DisplayName) | $($_.PackageName)"})+@('Features:')+@($assessment.Features|ForEach-Object{"  $($_.FeatureName) | $($_.State)"})}}
                'ReductionApply'{$profiles=@(Get-WinCareOfflineReductionProfile);$profile=Show-WinCareListSelector -Title 'Apply reduction profile' -Items $profiles -Formatter {param($Item,$Width);"$(Limit-WinCareText $Item.Id 20) $(Limit-WinCareText $Item.Name 24) $(Limit-WinCareText $Item.Description ([math]::Max(30,$Width-48)))"} -SearchProperties @('Id','Name','Description');if($profile){Invoke-WinCarePlanFromUi (New-WinCareOfflineReductionPlan -ImagePath $image.Path -ProfileId $profile.Id)}}
            }
        }catch{Show-WinCareMessage -Title 'Offline servicing operation failed' -Lines @($_.Exception.Message) -Kind Error}
    }
}

