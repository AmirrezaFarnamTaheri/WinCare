function New-WinCareExplorerQuickPlan {
    [CmdletBinding()]
    param(
        [Nullable[bool]]$ShowExtensions,
        [Nullable[bool]]$ShowHidden,
        [Nullable[bool]]$LaunchThisPc
    )
    if(-not $IsWindows){throw 'Explorer quick settings require Windows.'}
    $path='HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
    $current=Get-ItemProperty -LiteralPath $path -ErrorAction SilentlyContinue
    $actions=[Collections.Generic.List[object]]::new()
    $specs=@(
        @{Bound=$PSBoundParameters.ContainsKey('ShowExtensions');Name='HideFileExt';Value=if($ShowExtensions){0}else{1};Label=$(if($ShowExtensions){'Show file extensions'}else{'Hide file extensions'});Source='D106-EXPLORER-VISIBILITY'},
        @{Bound=$PSBoundParameters.ContainsKey('ShowHidden');Name='Hidden';Value=if($ShowHidden){1}else{2};Label=$(if($ShowHidden){'Show hidden files'}else{'Hide hidden files'});Source='D106-EXPLORER-VISIBILITY'},
        @{Bound=$PSBoundParameters.ContainsKey('LaunchThisPc');Name='LaunchTo';Value=if($LaunchThisPc){1}else{2};Label=$(if($LaunchThisPc){'Open Explorer to This PC'}else{'Open Explorer to Home'});Source='D106-EXPLORER-NAVIGATION'}
    )
    foreach($spec in $specs){
        if(-not [bool]$spec.Bound){continue}
        $before=if($current -and $null -ne $current.($spec.Name)){[int]$current.($spec.Name)}else{$null}
        if($before -eq [int]$spec.Value){continue}
        $compensator=if($null -eq $before){@{Type='RemoveRegistryValue';RequiresAdmin=$false;Parameters=@{Path=$path;Name=[string]$spec.Name;Before=$null}}}else{@{Type='SetRegistryValue';RequiresAdmin=$false;Parameters=@{Path=$path;Name=[string]$spec.Name;Value=$before;ValueType='DWord';Before=[int]$spec.Value}}}
        $actions.Add((New-WinCareAction -Type SetRegistryValue -Label ([string]$spec.Label) -Risk Low -Parameters @{Path=$path;Name=[string]$spec.Name;Value=[int]$spec.Value;ValueType='DWord';Before=$before} -Reversible $true -Compensator $compensator -Tags @('Explorer','Visibility','PeerConvergence') -SourceRecords @([string]$spec.Source) -RecoveryDescription 'Restore the exact previous Explorer preference. Explorer may need to be reopened.'))
    }
    New-WinCarePlan -Title 'Explorer quick settings' -Description 'Applies explicit Explorer visibility and launch preferences without installing global hotkeys or terminating Explorer.' -Actions @($actions) -SourceRecords @('D106-EXPLORER-VISIBILITY','D106-EXPLORER-NAVIGATION')
}

function Get-WinCareDeepCleanupProfile {
    [CmdletBinding()]param([string]$Id='')
    $profiles=@(
        [pscustomobject]@{Id='donor-safe-deep-clean';Title='Donor-derived safe deep clean';Risk='High';Description='Cleans bounded temporary, crash, cache, and diagnostic-log locations while preserving event logs, Prefetch, Defender evidence, and drive-root files.';TargetIds=@('user-temp','windows-temp','thumbnails','icon-cache','d3d-cache','psreadline-history','crash-dumps','windows-minidumps','windows-caches','wer-archive','wer-queue','wer-temp','locallow-temp','cbs-persist-logs','mosetup-logs','panther-logs','webcache-logs','inetcache-logs');RecycleBin=$true;WindowsUpdateCache=$false;ComponentCleanup=$false;SourceRecords=@('D107-BOUNDED-DEEP-CLEAN','D107-ROOT-SWEEP-GUARDRAIL')},
        [pscustomobject]@{Id='windows-cleaner-utility-all';Title='Windows Cleaner Utility — bounded aggressive';Risk='Critical';Description='Executes the donor aggressive cleanup intent through bounded WinCare targets, coordinated Windows Update cache deletion, Recycle Bin cleanup, and supported component-store cleanup. Root-drive sweeps, event-log deletion, Prefetch purge, Defender evidence deletion, and network reset remain excluded.';TargetIds=@('user-temp','windows-temp','thumbnails','icon-cache','d3d-cache','psreadline-history','crash-dumps','windows-minidumps','memory-dump','windows-caches','wer-archive','wer-queue','wer-temp','locallow-temp','cbs-persist-logs','mosetup-logs','panther-logs','webcache-logs','inetcache-logs');RecycleBin=$true;WindowsUpdateCache=$true;ComponentCleanup=$true;SourceRecords=@('D107-BOUNDED-DEEP-CLEAN','D107-UPDATE-CACHE-COORDINATION','D107-COMPONENT-CLEANUP','D107-ROOT-SWEEP-GUARDRAIL')}
    )
    if($Id){return @($profiles|Where-Object Id -eq $Id|Select-Object -First 1)}
    $profiles
}

function New-WinCareDeepCleanupPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$ProfileId)
    $profile=Get-WinCareDeepCleanupProfile -Id $ProfileId|Select-Object -First 1
    if(-not $profile){throw "Unknown deep cleanup profile: $ProfileId"}
    $catalog=@(Get-WinCareCleanupTarget -Refresh)
    $missing=@($profile.TargetIds|Where-Object{$_ -notin @($catalog.Id)})
    if($missing.Count){throw "Cleanup target(s) unavailable: $($missing -join ', ')"}
    $selected=@($catalog|Where-Object Id -in @($profile.TargetIds))
    $base=New-WinCareCleanupPlan -Targets $selected -IncludeRecycleBin:([bool]$profile.RecycleBin)
    $metadata=@{}
    if([string]$profile.Risk -eq 'Critical'){$metadata=@{LegacyUnsafeProfile=$ProfileId;Acknowledgement='I ACCEPT LEGACY UNSAFE MUTATIONS';IrreversibleCleanup=$true}}
    foreach($action in @($base.Actions)){$action.SourceRecords=@($profile.SourceRecords)}
    $plan=New-WinCarePlan -Title ([string]$profile.Title) -Description ([string]$profile.Description) -Actions @($base.Actions) -RollbackOnFailure $false -Metadata $metadata -SourceRecords @($profile.SourceRecords)
    if([bool]$profile.WindowsUpdateCache){$plan.Actions.Add((New-WinCareAction -Type ClearWindowsUpdateDownloadCache -Label 'Clear Windows Update download cache' -Risk Critical -Parameters @{} -RequiresAdmin $true -Reversible $false -TimeoutSeconds 900 -Tags @('Cleanup','WindowsUpdate','LegacyUnsafe','OneClick') -SourceRecords @('D107-UPDATE-CACHE-COORDINATION') -RecoveryDescription 'Deleted update payloads are reacquired by Windows Update; the removed bytes are not restored.'))}
    if([bool]$profile.ComponentCleanup){$plan.Actions.Add((New-WinCareAction -Type StartComponentCleanup -Label 'Run supported online component-store cleanup' -Risk High -Parameters @{} -RequiresAdmin $true -Reversible $false -TimeoutSeconds 21600 -Tags @('Cleanup','Servicing','LegacyUnsafe','OneClick') -SourceRecords @('D107-COMPONENT-CLEANUP') -RecoveryDescription 'Component-store cleanup is not automatically reversible. ResetBase is not used.'))}
    $plan
}

function Test-WinCareAppxSelectionPattern {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$Pattern,[switch]$AllowMatchAll)
    if($Pattern.Length -lt 1 -or $Pattern.Length -gt 200 -or $Pattern -notmatch '^[A-Za-z0-9*?_.-]+$'){return $false}
    if($Pattern -eq '*'){return [bool]$AllowMatchAll}
    if($Pattern -notmatch '[A-Za-z0-9]'){return $false}
    try{$null=[Management.Automation.WildcardPattern]::new($Pattern,[Management.Automation.WildcardOptions]::IgnoreCase);$true}catch{$false}
}

function Import-WinCareAppxSelectionList {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $resolved=Resolve-WinCareCanonicalPath -LiteralPath $LiteralPath
    $item=Get-Item -LiteralPath $resolved -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'AppX selection list must be a regular file.'}
    if($item.Length -gt 65536){throw 'AppX selection list exceeds 64 KiB.'}
    if([IO.Path]::GetExtension($item.Name) -notin @('.txt','.list')){throw 'AppX selection list must use .txt or .list.'}
    $patterns=@(Get-Content -LiteralPath $resolved -ErrorAction Stop|ForEach-Object{([string]$_).Trim()}|Where-Object{$_ -and -not $_.StartsWith('#')}|Sort-Object -Unique)
    if($patterns.Count -lt 1 -or $patterns.Count -gt 200){throw 'AppX selection list must contain 1..200 unique patterns.'}
    foreach($pattern in $patterns){if(-not(Test-WinCareAppxSelectionPattern $pattern)){throw "Unsafe AppX selection pattern: $pattern"}}
    [pscustomobject]@{Path=$resolved;Sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant();Patterns=$patterns}
}

function Get-WinCareAppxSelectionProfile {
    [CmdletBinding()]param([string]$Id='')
    $w10=@('Microsoft.549981C3F5F10','Microsoft.BingWeather','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.Microsoft3DViewer','Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.MicrosoftStickyNotes','Microsoft.MixedReality.Portal','Microsoft.MSPaint','Microsoft.Office.OneNote','Microsoft.People','Microsoft.SkypeApp','Microsoft.Wallet','Microsoft.Windows.Photos','Microsoft.WindowsAlarms','Microsoft.WindowsCamera','microsoft.windowscommunicationsapps','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.Xbox.TCUI','Microsoft.XboxApp','Microsoft.XboxGameOverlay','Microsoft.XboxGamingOverlay','Microsoft.XboxIdentityProvider','Microsoft.XboxSpeechToTextOverlay','Microsoft.YourPhone','Microsoft.ZuneMusic','Microsoft.ZuneVideo')
    $w11=@('Clipchamp.Clipchamp','Microsoft.549981C3F5F10','Microsoft.BingNews','Microsoft.BingWeather','Microsoft.GamingApp','Microsoft.GetHelp','Microsoft.Getstarted','Microsoft.MicrosoftOfficeHub','Microsoft.MicrosoftSolitaireCollection','Microsoft.MicrosoftStickyNotes','Microsoft.People','Microsoft.PowerAutomateDesktop','Microsoft.Todos','Microsoft.Windows.Photos','Microsoft.WindowsAlarms','Microsoft.WindowsCalculator','Microsoft.WindowsCamera','microsoft.windowscommunicationsapps','Microsoft.WindowsFeedbackHub','Microsoft.WindowsMaps','Microsoft.Xbox.TCUI','Microsoft.XboxGameOverlay','Microsoft.XboxGamingOverlay','Microsoft.XboxIdentityProvider','Microsoft.XboxSpeechToTextOverlay','Microsoft.YourPhone','Microsoft.ZuneMusic','Microsoft.ZuneVideo','MicrosoftCorporationII.QuickAssist','MicrosoftWindows.Client.WebExperience')
    $core=@('Microsoft.WindowsStore','Microsoft.StorePurchaseApp','Microsoft.DesktopAppInstaller','Microsoft.WindowsCalculator','Microsoft.Windows.Photos','Microsoft.WindowsTerminal','Microsoft.SecHealthUI','Microsoft.Windows.ShellExperienceHost','Microsoft.Windows.StartMenuExperienceHost','Microsoft.AAD.BrokerPlugin','Microsoft.AccountsControl','Microsoft.LockApp','MicrosoftWindows.Client.CBS','MicrosoftWindows.Client.Core','MicrosoftWindows.Client.FileExp','MicrosoftWindows.Client.OOBE','MicrosoftWindows.Client.Photon')
    $profiles=@(
        [pscustomobject]@{Id='windows-10-curated';Title='Windows 10 curated Store-app removal';Risk='High';Mode='Include';Patterns=$w10;Description='Removes the donor Windows 10 curated application set.';SourceRecords=@('D108-LIST-REMOVE')},
        [pscustomobject]@{Id='windows-11-curated';Title='Windows 11 curated Store-app removal';Risk='High';Mode='Include';Patterns=$w11;Description='Removes the donor Windows 11 curated application set.';SourceRecords=@('D108-LIST-REMOVE')},
        [pscustomobject]@{Id='keep-core-apps-only';Title='Keep core apps only';Risk='Critical';Mode='Exclude';Patterns=$core;Description='Allowlist mode: remove every removable application that is not in the WinCare core keep set.';SourceRecords=@('D108-ALLOWLIST-REMOVE','D108-ALL-USERS-PROVISIONED')},
        [pscustomobject]@{Id='remove-every-removable-appx';Title='Remove every removable Store application';Risk='Critical';Mode='Include';Patterns=@('*');Description='Removes every package WinCare can identify as removable, including Store and recovery-facing apps. Framework, resource, and explicitly non-removable packages are excluded.';SourceRecords=@('D109-APPX-CAPABILITY-REDUCTION','D108-ALL-USERS-PROVISIONED');AllowMatchAll=$true},
        [pscustomobject]@{Id='remove-copilot';Title='Remove Copilot packages';Risk='High';Mode='Include';Patterns=@('*Microsoft.Copilot*','MicrosoftWindows.Client.WebExperience');Description='Removes Copilot and the Web Experience package when removable.';SourceRecords=@('D109-COPILOT-REMOVAL')}
    )
    foreach($profile in $profiles){foreach($pattern in @($profile.Patterns)){if(-not(Test-WinCareAppxSelectionPattern $pattern -AllowMatchAll:([bool](Get-WinCarePropertyValue $profile 'AllowMatchAll' $false)))){throw "Invalid built-in AppX selection pattern: $pattern"}}}
    if($Id){return @($profiles|Where-Object Id -eq $Id|Select-Object -First 1)}
    $profiles
}

function Test-WinCareAppxSelectionMatch {
    param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][string[]]$Patterns)
    foreach($pattern in $Patterns){if($Name -like $pattern){return $true}}
    $false
}

function Get-WinCareAppxSelectionAssessment {
    [CmdletBinding()]
    param(
        [string]$ProfileId,
        [string[]]$Pattern,
        [ValidateSet('Include','Exclude')][string]$Mode='Include',
        [ValidateSet('CurrentUser','AllUsers')][string]$Scope='CurrentUser',
        [switch]$IncludeProvisioned
    )
    if($ProfileId){$profile=Get-WinCareAppxSelectionProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown AppX selection profile: $ProfileId"};$Pattern=@($profile.Patterns);$Mode=[string]$profile.Mode;$risk=[string]$profile.Risk;$sources=@($profile.SourceRecords);$title=[string]$profile.Title}else{$profile=$null;$risk='Critical';$sources=@('D108-LIST-REMOVE','D108-ALLOWLIST-REMOVE');$title='Custom AppX selection'}
    $patterns=@($Pattern|ForEach-Object{([string]$_).Trim()}|Where-Object{$_}|Sort-Object -Unique)
    if($patterns.Count -lt 1 -or $patterns.Count -gt 200){throw 'AppX selection requires 1..200 patterns.'}
    foreach($value in $patterns){if(-not(Test-WinCareAppxSelectionPattern $value -AllowMatchAll:([bool](Get-WinCarePropertyValue $profile 'AllowMatchAll' $false)))){throw "Unsafe AppX selection pattern: $value"}}
    $params=@{PackageTypeFilter=@('Main','Bundle');ErrorAction='SilentlyContinue'};if($Scope -eq 'AllUsers'){$params.AllUsers=$true}
    $installed=@(Get-AppxPackage @params|Where-Object{
        $name=[string]$_.Name;$framework=[bool](Get-WinCarePropertyValue $_ 'IsFramework' $false);$resource=[bool](Get-WinCarePropertyValue $_ 'IsResourcePackage' $false);$nonRemovable=[bool](Get-WinCarePropertyValue $_ 'NonRemovable' $false)
        $matched=Test-WinCareAppxSelectionMatch -Name $name -Patterns $patterns
        -not $framework -and -not $resource -and -not $nonRemovable -and $(if($Mode -eq 'Include'){$matched}else{-not $matched})
    }|Sort-Object PackageFullName -Unique)
    $provisioned=@()
    if($IncludeProvisioned){
        if(-not $script:WinCareState.IsAdmin){throw 'Provisioned package assessment requires administrator privileges.'}
        $provisioned=@(Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue|Where-Object{$matched=Test-WinCareAppxSelectionMatch -Name ([string]$_.DisplayName) -Patterns $patterns;if($Mode -eq 'Include'){$matched}else{-not $matched}}|Sort-Object PackageName -Unique)
    }
    [pscustomobject]@{Title=$title;Profile=$profile;Patterns=$patterns;Mode=$Mode;Scope=$Scope;Risk=$risk;Installed=$installed;Provisioned=$provisioned;SourceRecords=$sources;IrreversibleCount=$installed.Count+$provisioned.Count}
}

function New-WinCareAppxSelectionPlan {
    [CmdletBinding()]
    param(
        [string]$ProfileId,
        [string[]]$Pattern,
        [ValidateSet('Include','Exclude')][string]$Mode='Include',
        [ValidateSet('CurrentUser','AllUsers')][string]$Scope='CurrentUser',
        [switch]$IncludeProvisioned
    )
    $assessment=Get-WinCareAppxSelectionAssessment -ProfileId $ProfileId -Pattern $Pattern -Mode $Mode -Scope $Scope -IncludeProvisioned:$IncludeProvisioned
    $maximumActions=[int](Get-WinCarePolicy 'MaximumPlanActions')
    if([int]$assessment.IrreversibleCount -gt $maximumActions){throw "AppX selection resolves to $($assessment.IrreversibleCount) actions, exceeding the policy maximum of $maximumActions."}
    $metadata=@{AppxSelectionMode=$assessment.Mode;AppxSelectionScope=$assessment.Scope;IrreversibleAppxCount=$assessment.IrreversibleCount}
    if([string]$assessment.Risk -eq 'Critical'){$metadata.LegacyUnsafeProfile=if($ProfileId){$ProfileId}else{'custom-appx-selection'};$metadata.Acknowledgement='I ACCEPT LEGACY UNSAFE MUTATIONS'}
    $actions=[Collections.Generic.List[object]]::new()
    foreach($package in @($assessment.Installed)){
        $type=if($Scope -eq 'AllUsers'){'RemoveAppxPackageAllUsers'}else{'RemoveAppxPackage'}
        $actions.Add((New-WinCareAction -Type $type -Label "Remove $($package.Name) $(if($Scope -eq 'AllUsers'){'for all users'}else{'for current user'})" -Risk ([string]$assessment.Risk) -Parameters @{PackageFullName=[string]$package.PackageFullName;Name=[string]$package.Name} -RequiresAdmin:($Scope -eq 'AllUsers') -Reversible $false -Tags @('Appx','Debloat',$Scope,'Irreversible') -SourceRecords @($assessment.SourceRecords) -RecoveryDescription 'Reinstall from Microsoft Store or trusted installation media where available.'))
    }
    foreach($package in @($assessment.Provisioned)){$actions.Add((New-WinCareAction -Type RemoveProvisionedAppx -Label "Remove provisioned $($package.DisplayName)" -Risk ([string]$assessment.Risk) -Parameters @{PackageName=[string]$package.PackageName} -RequiresAdmin $true -Reversible $false -Tags @('Appx','Debloat','Provisioned','Irreversible') -SourceRecords @($assessment.SourceRecords) -RecoveryDescription 'Reapply the package from trusted media or Microsoft Store where available.'))}
    New-WinCarePlan -Title ([string]$assessment.Title) -Description "Selected $($assessment.Installed.Count) installed and $($assessment.Provisioned.Count) provisioned package(s). Removals are not automatically reversible." -Actions @($actions) -RollbackOnFailure $false -Metadata $metadata -SourceRecords @($assessment.SourceRecords)
}

function New-WinCareOfflineAppxSelectionPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ImagePath,[string]$ProfileId,[string[]]$Pattern,[ValidateSet('Include','Exclude')][string]$Mode='Include')
    $null=Test-WinCareOfflineImagePolicy
    if(-not [bool](Get-WinCarePolicy 'AllowOfflineImageReduction')){throw 'Offline image reduction is disabled by policy.'}
    $image=Resolve-WinCareMountedImagePath $ImagePath -RequireReadWrite
    if($ProfileId){$profile=Get-WinCareAppxSelectionProfile -Id $ProfileId|Select-Object -First 1;if(-not $profile){throw "Unknown AppX selection profile: $ProfileId"};$Pattern=@($profile.Patterns);$Mode=[string]$profile.Mode;$risk=[string]$profile.Risk;$sources=@($profile.SourceRecords);$title=[string]$profile.Title}else{$profile=$null;$risk='Critical';$sources=@('D108-OFFLINE-MOUNTED-IMAGE');$title='Custom offline AppX selection'}
    $patterns=@($Pattern|ForEach-Object{([string]$_).Trim()}|Where-Object{$_}|Sort-Object -Unique);if($patterns.Count -lt 1 -or $patterns.Count -gt 200){throw 'Offline AppX selection requires 1..200 patterns.'};foreach($value in $patterns){if(-not(Test-WinCareAppxSelectionPattern $value -AllowMatchAll:([bool](Get-WinCarePropertyValue $profile 'AllowMatchAll' $false)))){throw "Unsafe AppX selection pattern: $value"}}
    $packages=@(Get-WinCareOfflineProvisionedAppx -ImagePath $image|Where-Object{$matched=Test-WinCareAppxSelectionMatch -Name ([string]$_.DisplayName) -Patterns $patterns;if($Mode -eq 'Include'){$matched}else{-not $matched}})
    $maximumActions=[int](Get-WinCarePolicy 'MaximumPlanActions')
    if($packages.Count -gt $maximumActions){throw "Offline AppX selection resolves to $($packages.Count) actions, exceeding the policy maximum of $maximumActions."}
    $actions=@($packages|ForEach-Object{New-WinCareAction -Type OfflineImageRemoveProvisionedAppx -Label "Remove offline provisioned package: $($_.DisplayName)" -Risk $risk -Parameters @{ImagePath=$image;PackageName=[string]$_.PackageName;DisplayName=[string]$_.DisplayName} -RequiresAdmin $true -Reversible $false -TimeoutSeconds 21600 -Tags @('OfflineImage','Appx','Irreversible') -SourceRecords @($sources+'D108-OFFLINE-MOUNTED-IMAGE') -RecoveryDescription 'Discard the mounted-image changes or reapply the package from trusted media.'})
    $metadata=@{OfflineAppxSelection=$true;IrreversibleAppxCount=$actions.Count};if($risk -eq 'Critical'){$metadata.LegacyUnsafeProfile=if($ProfileId){"offline-$ProfileId"}else{'offline-custom-appx-selection'};$metadata.Acknowledgement='I ACCEPT LEGACY UNSAFE MUTATIONS'}
    New-WinCarePlan -Title "Offline $title" -Description "Removes $($actions.Count) provisioned package(s) from the already mounted read-write image. WinCare does not mount or commit a WIM implicitly." -Actions $actions -RollbackOnFailure $false -Metadata $metadata -SourceRecords @($sources+'D108-OFFLINE-MOUNTED-IMAGE')
}

function Invoke-WinCareRemoveAppxPackageAllUsersAction {
    param([Parameter(Mandatory)][object]$Action)
    $full=[string]$Action.Parameters.PackageFullName
    try{
        $current=Get-AppxPackage -AllUsers -PackageTypeFilter Main,Bundle -ErrorAction Stop|Where-Object PackageFullName -eq $full|Select-Object -First 1
        if(-not $current){return New-WinCareResult -Success $true -Code 'AppxAlreadyAbsentAllUsers' -Message 'The package is already absent for all users.'}
        if([string]$current.Name -cne [string]$Action.Parameters.Name){return New-WinCareResult -Success $false -Status Blocked -Code 'AppxAllUsersIdentityChanged' -Message 'The exact AppX package identity changed after preview.' -ExitCode 74}
        if([bool](Get-WinCarePropertyValue $current 'IsFramework' $false) -or [bool](Get-WinCarePropertyValue $current 'IsResourcePackage' $false) -or [bool](Get-WinCarePropertyValue $current 'NonRemovable' $false)){return New-WinCareResult -Success $false -Status Blocked -Code 'AppxAllUsersProtectedPackage' -Message 'Framework, resource, or non-removable AppX packages are not admitted for all-user removal.' -ExitCode 5}
        Remove-AppxPackage -Package $full -AllUsers -ErrorAction Stop
        $remaining=Get-AppxPackage -AllUsers -PackageTypeFilter Main,Bundle -ErrorAction SilentlyContinue|Where-Object PackageFullName -eq $full
        New-WinCareResult -Success (-not $remaining) -Code $(if($remaining){'AppxAllUsersRemovalUnverified'}else{'AppxRemovedAllUsers'}) -Message $(if($remaining){'Package still appears installed for at least one user.'}else{'App package removed for all users.'})
    }catch{New-WinCareResult -Success $false -Code 'AppxAllUsersRemovalFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Invoke-WinCareClearWindowsUpdateDownloadCacheAction {
    param([Parameter(Mandatory)][object]$Action)
    $root=Join-Path $env:WINDIR 'SoftwareDistribution\Download'
    try{$root=Resolve-WinCareCanonicalPath -LiteralPath $root -AllowMissing;if(Test-Path -LiteralPath $root){$null=Assert-WinCareSafePath -LiteralPath $root}}catch{return New-WinCareResult -Success $false -Status Blocked -Code 'WindowsUpdateCachePathRejected' -Message $_.Exception.Message -ExitCode 5}
    $serviceStates=@{};$removed=0L;$failed=0L;$bytes=0L;$primary=''
    try{
        foreach($name in @('bits','wuauserv')){$service=Get-Service -Name $name -ErrorAction SilentlyContinue;if($service){$serviceStates[$name]=[string]$service.Status;if($service.Status -ne 'Stopped'){Stop-Service -Name $name -Force -ErrorAction Stop}}}
        if(Test-Path -LiteralPath $root){
            $files=@(Get-ChildItem -LiteralPath $root -File -Recurse -Force -ErrorAction SilentlyContinue|Where-Object{-not($_.Attributes -band [IO.FileAttributes]::ReparsePoint)})
            foreach($file in $files){try{$length=[long]$file.Length;Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop;$removed++;$bytes+=$length}catch{$failed++}}
            Remove-WinCareEmptyDirectory -Root $root
        }
    }catch{$failed++;$primary=$_.Exception.Message}
    finally{foreach($name in @($serviceStates.Keys)){try{switch([string]$serviceStates[$name]){'Running'{Start-Service -Name $name -ErrorAction Stop};'Paused'{Start-Service -Name $name -ErrorAction Stop;Suspend-Service -Name $name -ErrorAction Stop}}}catch{$failed++}}}
    $message="Removed $removed Windows Update cache file(s); $failed operation(s) failed."
    if($primary){$message="$message $primary"}
    New-WinCareResult -Success ($failed -eq 0) -Code $(if($failed -eq 0){'WindowsUpdateCacheCleared'}else{'WindowsUpdateCachePartiallyCleared'}) -Message $message -Data @{Removed=$removed;Failed=$failed;Bytes=$bytes;ServiceStates=$serviceStates}
}

function Invoke-WinCareStartComponentCleanupAction {
    param([Parameter(Mandatory)][object]$Action)
    if(-not(Get-Command Repair-WindowsImage -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Code 'RepairWindowsImageUnavailable' -Message 'Repair-WindowsImage is unavailable.' -ExitCode 127}
    try{$result=Repair-WindowsImage -Online -StartComponentCleanup -ErrorAction Stop;New-WinCareResult -Success $true -Code 'OnlineComponentCleanupCompleted' -Message 'Supported online component-store cleanup completed without ResetBase.' -Data $result -RestartRequired ([bool](Get-WinCarePropertyValue $result 'RestartNeeded' $false))}catch{New-WinCareResult -Success $false -Code 'OnlineComponentCleanupFailed' -Message $_.Exception.Message -ExitCode 1}
}


