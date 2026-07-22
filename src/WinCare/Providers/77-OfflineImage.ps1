function Test-WinCareOfflineImagePolicy {
    [CmdletBinding()]
    param()
    if(-not [bool](Get-WinCarePolicy 'AllowOfflineImageServicing')){throw 'Offline image servicing is disabled by policy.'}
    if(-not $IsWindows){throw 'Offline Windows image servicing requires Windows.'}
    return $true
}

function Resolve-WinCareMountedImagePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[switch]$RequireReadWrite)
    $path=Assert-WinCareSafePath -LiteralPath $ImagePath
    if(-not(Test-Path -LiteralPath $path -PathType Container)){throw 'Offline image path must be a mounted image directory.'}
    $mounted=@(Get-WindowsImage -Mounted -ErrorAction Stop)
    $match=$mounted|Where-Object{(Resolve-WinCareCanonicalPath -LiteralPath ([string]$_.Path) -AllowMissing) -eq $path}|Select-Object -First 1
    if(-not $match){throw "Path is not registered as a mounted Windows image: $path"}
    if([string]$match.MountStatus -ne 'Ok'){throw "Mounted image must have status Ok before servicing: $($match.MountStatus)"}
    if($RequireReadWrite -and [string]$match.MountMode -ne 'ReadWrite'){throw "Mounted image must be mounted read-write; current mode is $($match.MountMode)."}
    return $path
}

function Get-WinCareMountedWindowsImage {
    [CmdletBinding()]
    param()
    if(-not $IsWindows -or -not (Get-Command Get-WindowsImage -ErrorAction SilentlyContinue)){return @()}
    foreach($image in @(Get-WindowsImage -Mounted -ErrorAction Stop)){
        [pscustomobject]@{Path=[string]$image.Path;ImageName=[string]$image.ImageName;ImageIndex=[int]$image.ImageIndex;MountStatus=[string]$image.MountStatus;ReadWrite=([string]$image.MountMode -eq 'ReadWrite');ImageFile=[string]$image.ImageFile}
    }
}

function Get-WinCareOfflineImageDriver {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath)
    $path=Resolve-WinCareMountedImagePath $ImagePath
    foreach($driver in @(Get-WindowsDriver -Path $path -All -ErrorAction Stop)){
        [pscustomobject]@{ImagePath=$path;PublishedName=[string]$driver.Driver;OriginalFileName=[string]$driver.OriginalFileName;ProviderName=[string]$driver.ProviderName;ClassName=[string]$driver.ClassName;ClassGuid=[string]$driver.ClassGuid;Version=[string]$driver.Version;Date=$driver.Date;BootCritical=[bool]$driver.BootCritical;Inbox=[bool]$driver.Inbox;Signature=[string]$driver.DigitalSigner}
    }
}

function Get-WinCareOfflineImagePackage {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath)
    $path=Resolve-WinCareMountedImagePath $ImagePath
    foreach($package in @(Get-WindowsPackage -Path $path -ErrorAction Stop)){
        [pscustomobject]@{ImagePath=$path;PackageName=[string]$package.PackageName;PackageState=[string]$package.PackageState;ReleaseType=[string]$package.ReleaseType;InstallTime=$package.InstallTime;Applicable=[bool]$package.Applicable;RestartRequired=[bool]$package.RestartRequired}
    }
}

function Get-WinCareOfflineImageFeature {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath)
    $path=Resolve-WinCareMountedImagePath $ImagePath
    foreach($feature in @(Get-WindowsOptionalFeature -Path $path -ErrorAction Stop)){
        [pscustomobject]@{ImagePath=$path;FeatureName=[string]$feature.FeatureName;State=[string]$feature.State;RestartRequired=[bool]$feature.RestartRequired;CustomProperties=@($feature.CustomProperties)}
    }
}

function New-WinCareOfflineDriverAddPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$DriverPath,[switch]$Recurse,[switch]$ForceUnsigned)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    $driver=Assert-WinCareSafePath -LiteralPath $DriverPath
    if(-not(Test-Path -LiteralPath $driver)){throw 'Driver source does not exist.'}
    if(-not $Recurse -and (Test-Path -LiteralPath $driver -PathType Leaf) -and [IO.Path]::GetExtension($driver) -ne '.inf'){throw 'A single driver source must be an INF file.'}
    if($ForceUnsigned -and -not [bool](Get-WinCarePolicy 'AllowCriticalActions')){throw 'ForceUnsigned requires critical actions to be enabled by policy.'}
    $action=New-WinCareAction -Type OfflineImageAddDriver -Label "Add driver to offline image: $([IO.Path]::GetFileName($driver))" -Risk $(if($ForceUnsigned){'Critical'}else{'High'}) -Parameters @{ImagePath=$image;DriverPath=$driver;Recurse=[bool]$Recurse;ForceUnsigned=[bool]$ForceUnsigned} -RequiresAdmin $true -Reversible $true -Tags @('OfflineImage','Driver','Servicing') -SourceRecords @('D20-DRIVER-HARDENING','D37-PLAYBOOK-DRIVERS') -RecoveryDescription 'Remove newly added OEM driver packages detected by before/after inventory.' -RestartPossible $false -TimeoutSeconds 21600
    New-WinCarePlan -Title 'Add driver to offline image' -Description 'Service a mounted Windows image using the supported DISM PowerShell provider.' -Actions @($action) -SourceRecords @('D20-DRIVER-HARDENING','D37-PLAYBOOK-DRIVERS')
}

function New-WinCareOfflineDriverRemovePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$PublishedName)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    $driver=Get-WinCareOfflineImageDriver -ImagePath $image|Where-Object PublishedName -eq $PublishedName|Select-Object -First 1
    if(-not $driver){throw "Offline driver was not found: $PublishedName"}
    if($driver.Inbox -or $driver.BootCritical){throw 'Inbox or boot-critical drivers cannot be removed by WinCare.'}
    $action=New-WinCareAction -Type OfflineImageRemoveDriver -Label "Remove offline driver: $PublishedName" -Risk High -Parameters @{ImagePath=$image;PublishedName=$PublishedName} -RequiresAdmin $true -Reversible $false -Tags @('OfflineImage','Driver','Irreversible') -SourceRecords @('D20-DRIVER-HARDENING','D37-PLAYBOOK-DRIVERS') -RecoveryDescription 'No automatic rollback. Export the source driver before removal and remount/reapply if needed.' -TimeoutSeconds 21600
    New-WinCarePlan -Title 'Remove driver from offline image' -Description 'Remove a non-inbox, non-boot-critical OEM driver package.' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('D20-DRIVER-HARDENING','D37-PLAYBOOK-DRIVERS')
}

function New-WinCareOfflinePackageAddPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$PackagePath,[switch]$IgnoreCheck)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    $package=Assert-WinCareSafePath -LiteralPath $PackagePath
    if(-not(Test-Path -LiteralPath $package -PathType Leaf) -or [IO.Path]::GetExtension($package).ToLowerInvariant() -notin @('.cab','.msu')){throw 'Package source must be an existing CAB or MSU file.'}
    $action=New-WinCareAction -Type OfflineImageAddPackage -Label "Add package to offline image: $([IO.Path]::GetFileName($package))" -Risk Critical -Parameters @{ImagePath=$image;PackagePath=$package;IgnoreCheck=[bool]$IgnoreCheck} -RequiresAdmin $true -Reversible $false -Tags @('OfflineImage','Package','Irreversible') -SourceRecords @('D20-UPDATE-HARDENING','D37-PLAYBOOK-PACKAGES') -RecoveryDescription 'No generic rollback is promised; discard the mounted image or restore its captured image backup.' -RestartPossible $true -TimeoutSeconds 21600
    New-WinCarePlan -Title 'Add package to offline image' -Description 'Apply a CAB or MSU package to a mounted image. This is intentionally non-reversible.' -Actions @($action) -RollbackOnFailure $false -SourceRecords @('D20-UPDATE-HARDENING','D37-PLAYBOOK-PACKAGES')
}

function New-WinCareOfflineFeaturePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[Parameter(Mandatory)][string]$FeatureName,[Parameter(Mandatory)][ValidateSet('Enabled','Disabled')][string]$State)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    $feature=Get-WinCareOfflineImageFeature -ImagePath $image|Where-Object FeatureName -eq $FeatureName|Select-Object -First 1
    if(-not $feature){throw "Offline optional feature was not found: $FeatureName"}
    $before=if([string]$feature.State -match '^Enabled'){'Enabled'}else{'Disabled'}
    if($before -eq $State){return New-WinCarePlan -Title 'Offline feature already in desired state' -Description "$FeatureName is already $State." -SourceRecords @('D37-PLAYBOOK-FEATURES')}
    $action=New-WinCareAction -Type OfflineImageSetFeature -Label "$State offline feature: $FeatureName" -Risk High -Parameters @{ImagePath=$image;FeatureName=$FeatureName;State=$State;BeforeState=$before} -RequiresAdmin $true -Reversible $true -Compensator @{Type='OfflineImageSetFeature';RequiresAdmin=$true;Parameters=@{ImagePath=$image;FeatureName=$FeatureName;State=$before;BeforeState=$State}} -Tags @('OfflineImage','Feature','Idempotent') -SourceRecords @('D20-OPTIONAL-FEATURES','D37-PLAYBOOK-FEATURES') -RecoveryDescription "Restore feature state to $before." -RestartPossible $true -TimeoutSeconds 21600
    New-WinCarePlan -Title "$State offline Windows feature" -Description 'Change a supported optional feature through the DISM PowerShell provider.' -Actions @($action) -SourceRecords @('D20-OPTIONAL-FEATURES','D37-PLAYBOOK-FEATURES')
}

function Invoke-WinCareOfflineImageAddDriverAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite
    $driver=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.DriverPath)
    $beforeInventory=@(Get-WinCareOfflineImageDriver -ImagePath $image)
    $beforeNames=@($beforeInventory.PublishedName)
    $params=@{Path=$image;Driver=$driver;ErrorAction='Stop'}
    if([bool]$Action.Parameters.Recurse){$params.Recurse=$true}
    if([bool]$Action.Parameters.ForceUnsigned){$params.ForceUnsigned=$true}
    try{
        $null=Add-WindowsDriver @params
        $afterInventory=@(Get-WinCareOfflineImageDriver -ImagePath $image)
        $added=@($afterInventory|Where-Object{[string]$_.PublishedName -notin $beforeNames})
        if($added.Count -eq 0){throw 'No new offline driver package was detected after servicing.'}
        $protected=@($added|Where-Object{$_.Inbox -or $_.BootCritical})
        if($protected.Count){throw "Servicing unexpectedly produced protected driver identities: $($protected.PublishedName -join ', ')"}
        $compensator=@{Type='OfflineImageRemoveDriverBatch';RequiresAdmin=$true;Parameters=@{ImagePath=$image;PublishedNames=@($added.PublishedName)}}
        New-WinCareResult -Success $true -Code 'OfflineDriverAdded' -Message "Added and verified $($added.Count) offline driver package(s)." -Data @{ImagePath=$image;PublishedNames=@($added.PublishedName);Compensator=$compensator}
    }catch{
        $primary=$_.Exception.Message;$rollbackWarnings=[Collections.Generic.List[string]]::new();$removed=[Collections.Generic.List[string]]::new()
        $afterFailure=try{@(Get-WinCareOfflineImageDriver -ImagePath $image)}catch{$rollbackWarnings.Add("Post-failure inventory failed: $($_.Exception.Message)");@()}
        $newDrivers=@($afterFailure|Where-Object{[string]$_.PublishedName -notin $beforeNames})
        foreach($candidate in $newDrivers){
            if($candidate.Inbox -or $candidate.BootCritical){$rollbackWarnings.Add("Refused to remove protected package during rollback: $($candidate.PublishedName)");continue}
            try{$null=Remove-WindowsDriver -Path $image -Driver ([string]$candidate.PublishedName) -ErrorAction Stop;$removed.Add([string]$candidate.PublishedName)}catch{$rollbackWarnings.Add("Rollback failed for $($candidate.PublishedName): $($_.Exception.Message)")}
        }
        $final=try{@(Get-WinCareOfflineImageDriver -ImagePath $image)}catch{$rollbackWarnings.Add("Final rollback inventory failed: $($_.Exception.Message)");@()}
        $remaining=@($final|Where-Object{[string]$_.PublishedName -notin $beforeNames})
        $rolledBack=$remaining.Count -eq 0 -and $rollbackWarnings.Count -eq 0
        $code=if($newDrivers.Count -eq 0 -and $rollbackWarnings.Count -eq 0){'OfflineDriverAddFailedBeforeMutation'}elseif($rolledBack){'OfflineDriverAddFailedRolledBack'}else{'OfflineDriverAddRollbackFailed'}
        $message=if($rolledBack){"$primary Newly added driver packages were removed."}elseif($remaining.Count -or $rollbackWarnings.Count){"$primary Rollback was incomplete; $($remaining.Count) added package(s) remain."}else{$primary}
        New-WinCareResult -Success $false -Status Failed -Code $code -Message $message -ExitCode 1 -Data @{ImagePath=$image;DetectedAfterFailure=@($newDrivers.PublishedName);Removed=@($removed);Remaining=@($remaining.PublishedName)} -Warnings @($rollbackWarnings)
    }
}

function Invoke-WinCareOfflineImageRemoveDriverAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite;$name=[string]$Action.Parameters.PublishedName
    $driver=Get-WinCareOfflineImageDriver -ImagePath $image|Where-Object PublishedName -eq $name|Select-Object -First 1
    if(-not $driver){return New-WinCareResult -Success $true -Code 'OfflineDriverAlreadyAbsent' -Message 'Offline driver is already absent.'}
    if($driver.Inbox -or $driver.BootCritical){return New-WinCareResult -Success $false -Status Blocked -Code 'ProtectedOfflineDriver' -Message 'Inbox or boot-critical drivers cannot be removed.' -ExitCode 5}
    $null=Remove-WindowsDriver -Path $image -Driver $name -ErrorAction Stop
    $remaining=Get-WinCareOfflineImageDriver -ImagePath $image|Where-Object PublishedName -eq $name
    New-WinCareResult -Success (-not $remaining) -Code 'OfflineDriverRemoved' -Message 'Offline driver removal completed and was verified.' -Data @{ImagePath=$image;PublishedName=$name}
}


function Invoke-WinCareOfflineImageRemoveDriverBatchAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite
    $removed=[Collections.Generic.List[string]]::new();$warnings=[Collections.Generic.List[string]]::new()
    foreach($name in @($Action.Parameters.PublishedNames)){
        $driver=Get-WinCareOfflineImageDriver -ImagePath $image|Where-Object PublishedName -eq ([string]$name)|Select-Object -First 1
        if(-not $driver){continue}
        if($driver.Inbox -or $driver.BootCritical){$warnings.Add("Skipped protected driver: $name");continue}
        try{$null=Remove-WindowsDriver -Path $image -Driver ([string]$name) -ErrorAction Stop;$removed.Add([string]$name)}catch{$warnings.Add("Failed to remove ${name}: $($_.Exception.Message)")}
    }
    $remaining=@(Get-WinCareOfflineImageDriver -ImagePath $image|Where-Object PublishedName -in @($Action.Parameters.PublishedNames))
    $success=$remaining.Count -eq 0 -and $warnings.Count -eq 0
    New-WinCareResult -Success $success -Status $(if($success){'Succeeded'}elseif($removed.Count -gt 0){'SucceededWithWarnings'}else{'Failed'}) -Code 'OfflineDriverBatchRemoval' -Message "Removed $($removed.Count) offline driver package(s); $($remaining.Count) remain." -Data @{ImagePath=$image;Removed=@($removed);Remaining=@($remaining.PublishedName)} -Warnings @($warnings)
}

function Invoke-WinCareOfflineImageAddPackageAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite;$package=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.PackagePath)
    $before=@(Get-WinCareOfflineImagePackage -ImagePath $image|ForEach-Object{[string]$_.PackageName})
    $params=@{Path=$image;PackagePath=$package;ErrorAction='Stop'};if([bool]$Action.Parameters.IgnoreCheck){$params.IgnoreCheck=$true}
    $result=Add-WindowsPackage @params
    $after=@(Get-WinCareOfflineImagePackage -ImagePath $image)
    $added=@($after|Where-Object{[string]$_.PackageName -notin $before})
    $ok=$added.Count -gt 0
    New-WinCareResult -Success $ok -Code $(if($ok){'OfflinePackageAdded'}else{'OfflinePackageVerificationFailed'}) -Message $(if($ok){"Offline package servicing added and verified $($added.Count) package identity record(s)."}else{'Package servicing returned without a newly verifiable package identity.'}) -Data @{ImagePath=$image;PackagePath=$package;AddedPackages=@($added.PackageName);Result=$result} -RestartRequired ([bool](Get-WinCarePropertyValue $result 'RestartNeeded' $false))
}

function Invoke-WinCareOfflineImageSetFeatureAction {
    param([Parameter(Mandatory)][object]$Action)
    $null=Test-WinCareOfflineImagePolicy
    $image=Resolve-WinCareMountedImagePath ([string]$Action.Parameters.ImagePath) -RequireReadWrite
    $feature=[string]$Action.Parameters.FeatureName;$state=[string]$Action.Parameters.State;$beforeState=[string]$Action.Parameters.BeforeState
    $current=Get-WinCareOfflineImageFeature -ImagePath $image|Where-Object FeatureName -eq $feature|Select-Object -First 1
    if(-not $current){return New-WinCareResult -Success $false -Status Blocked -Code 'OfflineFeatureNotFound' -Message "Offline feature was not found: $feature" -ExitCode 2}
    $currentState=if([string]$current.State -match '^Enabled'){'Enabled'}else{'Disabled'}
    if($currentState -ne $beforeState){return New-WinCareResult -Success $false -Status Blocked -Code 'OfflineFeatureChanged' -Message "Offline feature changed after preview. Expected $beforeState; actual $currentState." -ExitCode 74}
    $mutationAttempted=$false
    try{
        $mutationAttempted=$true
        if($state -eq 'Enabled'){$result=Enable-WindowsOptionalFeature -Path $image -FeatureName $feature -All -NoRestart -ErrorAction Stop}
        else{$result=Disable-WindowsOptionalFeature -Path $image -FeatureName $feature -NoRestart -ErrorAction Stop}
        $actual=Get-WinCareOfflineImageFeature -ImagePath $image|Where-Object FeatureName -eq $feature|Select-Object -First 1
        $actualState=if([string]$actual.State -match '^Enabled'){'Enabled'}else{'Disabled'}
        if($actualState -ne $state){throw "Offline feature verification failed. Expected $state; actual $actualState."}
        New-WinCareResult -Success $true -Code 'OfflineFeatureChanged' -Message "Offline feature is $actualState." -Data @{ImagePath=$image;FeatureName=$feature;DesiredState=$state;ActualState=$actualState;Result=$result} -RestartRequired ([bool](Get-WinCarePropertyValue $result 'RestartNeeded' $false))
    }catch{
        $primary=$_.Exception.Message;$rollbackError='';$rollbackActual='unknown'
        if($mutationAttempted){
            try{
                if($beforeState -eq 'Enabled'){$null=Enable-WindowsOptionalFeature -Path $image -FeatureName $feature -All -NoRestart -ErrorAction Stop}
                else{$null=Disable-WindowsOptionalFeature -Path $image -FeatureName $feature -NoRestart -ErrorAction Stop}
                $rolledBackFeature=Get-WinCareOfflineImageFeature -ImagePath $image|Where-Object FeatureName -eq $feature|Select-Object -First 1
                $rollbackActual=if([string]$rolledBackFeature.State -match '^Enabled'){'Enabled'}else{'Disabled'}
                if($rollbackActual -ne $beforeState){$rollbackError="Rollback verification failed. Expected $beforeState; actual $rollbackActual."}
            }catch{$rollbackError=$_.Exception.Message}
        }
        $rolledBack=$mutationAttempted -and -not $rollbackError
        $code=if(-not $mutationAttempted){'OfflineFeatureChangeFailedBeforeMutation'}elseif($rolledBack){'OfflineFeatureChangeFailedRolledBack'}else{'OfflineFeatureChangeRollbackFailed'}
        $message=if($rolledBack){"$primary The previous feature state was restored."}elseif($rollbackError){"$primary Rollback failed: $rollbackError"}else{$primary}
        New-WinCareResult -Success $false -Status Failed -Code $code -Message $message -ExitCode 1 -Data @{ImagePath=$image;FeatureName=$feature;BeforeState=$beforeState;DesiredState=$state;RollbackActual=$rollbackActual;RolledBack=$rolledBack}
    }
}



