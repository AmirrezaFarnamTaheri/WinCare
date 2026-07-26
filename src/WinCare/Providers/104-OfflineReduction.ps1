function Get-WinCareOfflineProvisionedAppx {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ImagePath)
    $path=Resolve-WinCareMountedImagePath $ImagePath
    if(-not(Get-Command Get-AppxProvisionedPackage -ErrorAction SilentlyContinue)){throw 'Offline AppX inventory requires Get-AppxProvisionedPackage.'}
    foreach($package in @(Get-AppxProvisionedPackage -Path $path -ErrorAction Stop)){
        [pscustomobject]@{
            ImagePath=$path;DisplayName=[string]$package.DisplayName;PackageName=[string]$package.PackageName
            Version=[string]$package.Version;PublisherId=[string]$package.PublisherId;Architecture=[string]$package.Architecture
            ResourceId=[string]$package.ResourceId;InstallLocation=[string]$package.InstallLocation
        }
    }
}

function Test-WinCareOfflineReductionProfile {
    param([Parameter(Mandatory)][object]$Profile)
    $map=ConvertTo-WinCareParameterDictionary $Profile
    $null=Test-WinCareStrictObjectKeys -InputObject $map -AllowedKeys @('Id','Name','Risk','Description','AppxDisplayNames','DisableFeatures','ComponentCleanup','SourceRecords') -Context 'offline reduction profile'
    foreach($required in @('Id','Name','Risk','Description','AppxDisplayNames','DisableFeatures','ComponentCleanup','SourceRecords')){if(-not $map.ContainsKey($required)){throw "Offline reduction profile is missing $required."}}
    if([string]$map.Id -notmatch '^[a-z][a-z0-9-]{2,63}$'){throw 'Offline reduction profile ID is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$map.Name) -or ([string]$map.Name).Length -gt 120){throw 'Offline reduction profile name is invalid.'}
    if([string]$map.Risk -notin @('High','Critical')){throw 'Offline reduction profile risk must be High or Critical.'}
    if(([string]$map.Description).Length -gt 1000){throw 'Offline reduction profile description is too long.'}
    if([string]$map.ComponentCleanup -notin @('None','Standard')){throw 'Offline reduction component cleanup mode is invalid.'}
    foreach($field in @('AppxDisplayNames','DisableFeatures','SourceRecords')){if($map[$field] -is [string] -or $map[$field] -isnot [Collections.IEnumerable]){throw "Offline reduction $field must be an array."}}
    $apps=@($map.AppxDisplayNames);$features=@($map.DisableFeatures);$sources=@($map.SourceRecords)
    if($apps.Count -gt 128 -or $features.Count -gt 64 -or $sources.Count -gt 64){throw 'Offline reduction profile exceeds a collection bound.'}
    foreach($value in $apps){if([string]$value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{1,255}$'){throw "Invalid exact AppX display name: $value"}}
    foreach($value in $features){if([string]$value -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{1,255}$'){throw "Invalid exact feature name: $value"}}
    foreach($collection in @($apps,$features,$sources)){if(@($collection|Sort-Object -Unique).Count -ne @($collection).Count){throw 'Offline reduction profile contains duplicate entries.'}}
    $true
}

function Get-WinCareOfflineReductionProfile {
    [CmdletBinding()]param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{
            Id='nano-safe';Name='Nano safe';Risk='High';Description='Remove a conservative set of consumer provisioned applications and disable optional legacy features while preserving servicing, security, Store, calculator, photos, terminal, and recovery.'
            AppxDisplayNames=@('Clipchamp.Clipchamp','Microsoft.BingNews','Microsoft.BingWeather','Microsoft.GamingApp','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.People','Microsoft.PowerAutomateDesktop','Microsoft.Todos','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.Xbox.TCUI','Microsoft.XboxGamingOverlay','Microsoft.XboxGameOverlay','Microsoft.XboxIdentityProvider','Microsoft.XboxSpeechToTextOverlay','Microsoft.YourPhone','MicrosoftCorporationII.MicrosoftFamily')
            DisableFeatures=@('Printing-XPSServices-Features','WorkFolders-Client','SMB1Protocol');ComponentCleanup='Standard';SourceRecords=@('SRC:de582ebaf6')
        },
        [pscustomobject]@{
            Id='developer-lean';Name='Developer lean';Risk='High';Description='Retain developer, virtualization, terminal, Store, security, and recovery components while removing consumer promotions and legacy optional features.'
            AppxDisplayNames=@('Clipchamp.Clipchamp','Microsoft.BingNews','Microsoft.BingWeather','Microsoft.GamingApp','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.People','Microsoft.PowerAutomateDesktop','Microsoft.Todos','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.YourPhone','MicrosoftCorporationII.MicrosoftFamily')
            DisableFeatures=@('Printing-XPSServices-Features','WorkFolders-Client','SMB1Protocol');ComponentCleanup='Standard';SourceRecords=@('SRC:de582ebaf6')
        },
        [pscustomobject]@{
            Id='component-cleanup';Name='Component cleanup only';Risk='High';Description='Run supported component-store cleanup without ResetBase. No applications or optional features are changed.'
            AppxDisplayNames=@();DisableFeatures=@();ComponentCleanup='Standard';SourceRecords=@('SRC:de582ebaf6')
        }
    )
    foreach($profile in $profiles){$null=Test-WinCareOfflineReductionProfile $profile}
    if($Id){return $profiles|Where-Object Id -eq $Id}
    $profiles
}

function Get-WinCareOfflineReductionAssessment {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$ProfileId)
    $image=Resolve-WinCareMountedImagePath $ImagePath
    $profile=Get-WinCareOfflineReductionProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown offline reduction profile: $ProfileId"}
    $appx=@(Get-WinCareOfflineProvisionedAppx -ImagePath $image)
    $features=@(Get-WinCareOfflineImageFeature -ImagePath $image)
    $appxMatches=@($appx|Where-Object DisplayName -in @($profile.AppxDisplayNames))
    $featureMatches=@($features|Where-Object{$_.FeatureName -in @($profile.DisableFeatures) -and [string]$_.State -match '^Enabled'})
    [pscustomobject]@{
        ImagePath=$image;Profile=$profile;ProvisionedAppx=@($appxMatches);Features=@($featureMatches)
        MissingAppx=@($profile.AppxDisplayNames|Where-Object{$_ -notin @($appx.DisplayName)})
        MissingFeatures=@($profile.DisableFeatures|Where-Object{$_ -notin @($features.FeatureName)})
        IrreversibleAppxCount=$appxMatches.Count;ReversibleFeatureCount=$featureMatches.Count
        ComponentCleanup=[string]$profile.ComponentCleanup
    }
}

function New-WinCareOfflineReductionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$ProfileId,[switch]$IncludeComponentCleanup)
    $null=Test-WinCareOfflineImagePolicy
    if(-not [bool](Get-WinCarePolicy 'AllowOfflineImageReduction')){throw 'Offline image reduction is disabled by policy.'}
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    $assessment=Get-WinCareOfflineReductionAssessment -ImagePath $image -ProfileId $ProfileId
    $actions=[Collections.Generic.List[object]]::new()
    foreach($feature in @($assessment.Features)){
        $actions.Add((New-WinCareAction -Type OfflineImageSetFeature -Label "Disable offline feature: $($feature.FeatureName)" -Risk High -Parameters @{ImagePath=$image;FeatureName=[string]$feature.FeatureName;State='Disabled';BeforeState='Enabled'} -RequiresAdmin $true -Reversible $true -Compensator @{Type='OfflineImageSetFeature';RequiresAdmin=$true;Parameters=@{ImagePath=$image;FeatureName=[string]$feature.FeatureName;State='Enabled';BeforeState='Disabled'}} -Tags @('OfflineImage','Reduction','Feature') -SourceRecords @($assessment.Profile.SourceRecords) -RecoveryDescription 'Restore the exact optional feature state.' -RestartPossible $true -TimeoutSeconds 21600))
    }
    foreach($package in @($assessment.ProvisionedAppx)){
        $actions.Add((New-WinCareAction -Type OfflineImageRemoveProvisionedAppx -Label "Remove provisioned package: $($package.DisplayName)" -Risk High -Parameters @{ImagePath=$image;PackageName=[string]$package.PackageName;DisplayName=[string]$package.DisplayName} -RequiresAdmin $true -Reversible $false -Tags @('OfflineImage','Reduction','Appx','Irreversible') -SourceRecords @($assessment.Profile.SourceRecords) -RecoveryDescription 'No automatic restoration is promised. Reapply the package from trusted media or discard the mounted-image changes.' -TimeoutSeconds 21600))
    }
    if($IncludeComponentCleanup -or [string]$assessment.Profile.ComponentCleanup -eq 'Standard'){
        $actions.Add((New-WinCareAction -Type OfflineImageStartComponentCleanup -Label 'Run offline component-store cleanup' -Risk High -Parameters @{ImagePath=$image;ResetBase=$false} -RequiresAdmin $true -Reversible $false -Tags @('OfflineImage','Reduction','ComponentStore') -SourceRecords @($assessment.Profile.SourceRecords) -RecoveryDescription 'Component cleanup is not automatically reversible; discard the mounted-image changes to recover.' -TimeoutSeconds 21600))
    }
    New-WinCarePlan -Title "Offline reduction: $($assessment.Profile.Name)" -Description "Apply only exact supported DISM/AppX operations. $($assessment.IrreversibleAppxCount) provisioned package removal(s) are not automatically reversible." -Actions @($actions) -RollbackOnFailure $false -SourceRecords @($assessment.Profile.SourceRecords)
}

function Invoke-WinCareOfflineImageRemoveProvisionedAppxAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite
    $packageName=[string]$Action.Parameters.PackageName
    try{
        $current=Get-WinCareOfflineProvisionedAppx -ImagePath $image|Where-Object PackageName -eq $packageName|Select-Object -First 1
        if(-not $current){return New-WinCareResult -Success $true -Code 'ProvisionedPackageAlreadyAbsent' -Message 'The provisioned package is already absent.'}
        $result=Remove-AppxProvisionedPackage -Path $image -PackageName $packageName -ErrorAction Stop
        $remaining=Get-WinCareOfflineProvisionedAppx -ImagePath $image|Where-Object PackageName -eq $packageName
        if($remaining){throw 'Provisioned package remained after the supported removal call.'}
        New-WinCareResult -Success $true -Code 'ProvisionedPackageRemoved' -Message "Removed and verified provisioned package $($current.DisplayName)." -Data $result
    }catch{New-WinCareResult -Success $false -Code 'ProvisionedPackageRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Invoke-WinCareOfflineImageStartComponentCleanupAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite
    if([bool]$Action.Parameters.ResetBase){return New-WinCareResult -Success $false -Status Blocked -Code 'ResetBaseDenied' -Message 'ResetBase is intentionally not exposed because it makes installed updates permanently non-removable.' -ExitCode 77}
    try{
        $result=Repair-WindowsImage -Path $image -StartComponentCleanup -ErrorAction Stop
        if([bool](Get-WinCarePropertyValue $result 'RestartNeeded' $false)){ $warnings=@('The serviced image reports that a restart may be required after deployment.') }else{$warnings=@()}
        New-WinCareResult -Success $true -Code 'ComponentCleanupCompleted' -Message 'Offline component-store cleanup completed through the supported servicing provider.' -Warnings $warnings -Data @{Result=$result;EvidenceType='OfflineComponentCleanupResult'}
    }catch{New-WinCareResult -Success $false -Code 'ComponentCleanupFailed' -Message $_.Exception.Message -ExitCode 1}
}


