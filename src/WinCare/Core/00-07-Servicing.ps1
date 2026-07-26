#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Health, winget, Defender, listening-port, and Windows Update operations.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function New-WinCareHealthPlan {
    [CmdletBinding()]
    param([ValidateSet('SfcScan','DismCheckHealth','DismScanHealth','DismRestoreHealth','ChkdskScan')][string]$Operation='SfcScan')
    $spec=switch($Operation){'SfcScan'{@{File='sfc.exe';Args=@('/scannow');Risk='High';Timeout=7200}}'DismCheckHealth'{@{File='dism.exe';Args=@('/Online','/Cleanup-Image','/CheckHealth');Risk='ReadOnly';Timeout=1800}}'DismScanHealth'{@{File='dism.exe';Args=@('/Online','/Cleanup-Image','/ScanHealth');Risk='ReadOnly';Timeout=7200}}'DismRestoreHealth'{@{File='dism.exe';Args=@('/Online','/Cleanup-Image','/RestoreHealth');Risk='High';Timeout=14400}}'ChkdskScan'{@{File='chkdsk.exe';Args=@($env:SystemDrive,'/scan');Risk='ReadOnly';Timeout=7200}}};$action=New-WinCareBridgeAction -Type 'RunHealthCommand' -Label "Run $Operation" -Risk $spec.Risk -RequiresAdmin $true -Parameters @{Operation=$Operation;FilePath=$spec.File;Arguments=$spec.Args;Timeout=$spec.Timeout} -TimeoutSeconds $spec.Timeout -Verification 'The command exit code and captured output must indicate completion.';New-WinCareBridgePlan -Title "Windows health operation: $Operation" -Actions @($action) -RollbackOnFailure $false
}
function Invoke-WinCareHealthAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $p=Get-WinCareActionParameters $Action;$allowed=@{'SfcScan'=@('sfc.exe','/scannow');'DismCheckHealth'=@('dism.exe','/Online','/Cleanup-Image','/CheckHealth');'DismScanHealth'=@('dism.exe','/Online','/Cleanup-Image','/ScanHealth');'DismRestoreHealth'=@('dism.exe','/Online','/Cleanup-Image','/RestoreHealth');'ChkdskScan'=@('chkdsk.exe',$env:SystemDrive,'/scan')};$operation=[string]$p.Operation
    if(-not $allowed.ContainsKey($operation)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'UnsupportedHealthOperation' -Message "Unsupported health operation: $operation" -ExitCode 22};$expected=$allowed[$operation];if([string]$p.FilePath -ne $expected[0] -or (@($p.Arguments) -join [char]0) -ne (@($expected[1..($expected.Count-1)]) -join [char]0)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'HealthCommandContractMismatch' -Message 'Health command does not match the allowlisted operation.' -ExitCode 22}
    $result=Invoke-WinCareBridgeProcess -FilePath $expected[0] -ArgumentList @($expected[1..($expected.Count-1)]) -TimeoutSeconds ([int]$p.Timeout) -SuccessExitCodes @(0,1,2,3) -RequireAdmin;if($result.Success){$result.Code='HealthCommandCompleted';$result.Data.EvidenceType='CapturedProcessExitAndOutput'};return $result
}
function Test-WinCareWinget {
    [CmdletBinding()]
    param()
    return $null -ne (Get-Command winget.exe -ErrorAction SilentlyContinue)
}
function Get-WinCareWingetVersion {
    [CmdletBinding()]
    param()
    if(-not (Test-WinCareWinget)){return $null};$result=Invoke-WinCareBridgeProcess -FilePath 'winget.exe' -ArgumentList @('--version') -TimeoutSeconds 30;if($result.Success){return ([string]$result.Data.StdOut).Trim()};return $null
}
function Invoke-WinCareWingetListUpgrades {
    [CmdletBinding()]
    param()
    if(-not (Test-WinCareWinget)){return @()};$result=Invoke-WinCareBridgeProcess -FilePath 'winget.exe' -ArgumentList @('upgrade','--accept-source-agreements','--disable-interactivity','--output','json') -TimeoutSeconds 120
    if($result.Success){try{$json=$result.Data.StdOut|ConvertFrom-Json -Depth 30;$packages=@(Get-WinCareBridgeProperty $json 'Data' (Get-WinCareBridgeProperty $json 'Sources' @()));return $packages}catch{}}
    $rows=[Collections.Generic.List[object]]::new();foreach($line in ([string]$result.Data.StdOut -split "`r?`n")){if($line -match '^(.+?)\s{2,}([^\s]+)\s{2,}([^\s]+)\s{2,}([^\s]+)\s{2,}(.+)$'){$rows.Add([pscustomobject]@{Name=$Matches[1].Trim();Id=$Matches[2];Version=$Matches[3];Available=$Matches[4];Source=$Matches[5].Trim()})}};return @($rows)
}
function New-WinCareWingetUpgradeAllPlan {
    [CmdletBinding()]
    param()
    $upgrades=@(Invoke-WinCareWingetListUpgrades);$actions=@($upgrades|Where-Object{$_.Id}|ForEach-Object{New-WinCareBridgeAction -Type 'UpgradeWingetPackage' -Label "Upgrade $($_.Name)" -Risk Moderate -Parameters @{Arguments=@('upgrade','--id',[string]$_.Id,'--exact','--silent','--accept-package-agreements','--accept-source-agreements','--disable-interactivity');Description="Upgrade $($_.Name)"} -TimeoutSeconds 21600 -Verification 'winget must exit successfully and subsequent upgrade inventory must not report the same available version.'});New-WinCareBridgePlan -Title 'Upgrade all winget packages' -Description "Creates one typed action per discovered upgrade ($($actions.Count))." -Actions $actions -RollbackOnFailure $false
}
function New-WinCareWingetUpgradePackagePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Id)
    if($Id -notmatch '^[A-Za-z0-9._+-]{1,200}$'){throw 'Invalid winget package identifier.'};$args=@('upgrade','--id',$Id,'--exact','--silent','--accept-package-agreements','--accept-source-agreements','--disable-interactivity');$action=New-WinCareBridgeAction -Type 'UpgradeWingetPackage' -Label "Upgrade winget package $Id" -Risk Moderate -Parameters @{Arguments=$args;Description="Upgrade $Id"} -TimeoutSeconds 21600 -Verification 'winget must return a documented success or restart-required exit code.';New-WinCareBridgePlan -Title "Upgrade $Id" -Actions @($action) -RollbackOnFailure $false
}
function Export-WinCareApplicationSet {
    [CmdletBinding()]
    param([string]$Path='')
    $apps=if(Get-Command Get-WinCareInstalledApplication -ErrorAction SilentlyContinue){@(Get-WinCareInstalledApplication)}else{@()};$record=[ordered]@{SchemaVersion=1;GeneratedAt=[datetime]::UtcNow.ToString('o');ComputerName=[Environment]::MachineName;WingetVersion=Get-WinCareWingetVersion;Applications=@($apps|Select-Object Name,Version,Publisher,Architecture,Source,PackageId)};if($Path){Write-WinCareBridgeJson -LiteralPath ([IO.Path]::GetFullPath($Path)) -InputObject $record};[pscustomobject]$record
}
function Invoke-WinCareWingetUpgradeAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    if(-not (Test-WinCareWinget)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'WingetUnavailable' -Message 'winget.exe is unavailable.' -ExitCode 127};$p=Get-WinCareActionParameters $Action;$args=@($p.Arguments);if($args.Count -lt 1 -or $args[0] -ne 'upgrade'){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidWingetUpgradeArguments' -Message 'Only winget upgrade operations are allowed.' -ExitCode 22};$result=Invoke-WinCareBridgeProcess -FilePath 'winget.exe' -ArgumentList $args -TimeoutSeconds ([int](Get-WinCareBridgeProperty $Action 'TimeoutSeconds' 21600)) -SuccessExitCodes @(0,-1978335189,-1978335212,1641,3010);if($result.Success){$result.Code='WingetUpgradeCompleted';$result.Data.EvidenceType='WingetExitAndOutput'};return $result
}
function Invoke-WinCareWingetInstallAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    if(-not (Test-WinCareWinget)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'WingetUnavailable' -Message 'winget.exe is unavailable.' -ExitCode 127};$p=Get-WinCareActionParameters $Action;$id=[string]$p.PackageId;if($id -notmatch '^[A-Za-z0-9._+-]{1,200}$'){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'InvalidPackageId' -Message 'Package identifier is invalid.' -ExitCode 22};$args=@('install','--id',$id,'--exact','--silent','--accept-package-agreements','--accept-source-agreements','--disable-interactivity');if($p.Version){$args+=@('--version',[string]$p.Version)};if($p.Scope){$args+=@('--scope',[string]$p.Scope)};$result=Invoke-WinCareBridgeProcess -FilePath 'winget.exe' -ArgumentList $args -TimeoutSeconds ([int](Get-WinCareBridgeProperty $Action 'TimeoutSeconds' 21600)) -SuccessExitCodes @(0,1641,3010);if($result.Success){$result.Code='WingetInstallCompleted';$result.Data.EvidenceType='WingetExitAndOutput'};return $result
}
function New-WinCareDefenderSignatureUpdatePlan {
    [CmdletBinding()]
    param()
    $action=New-WinCareBridgeAction -Type 'UpdateDefenderSignatures' -Label 'Update Microsoft Defender signatures' -Risk Low -RequiresAdmin $true -Parameters @{} -TimeoutSeconds 3600 -Verification 'Defender signature timestamp or version must advance, or the service must report already current.';New-WinCareBridgePlan -Title 'Update Defender signatures' -Actions @($action) -RollbackOnFailure $false
}
function New-WinCareDefenderScanPlan {
    [CmdletBinding()]
    param([Alias('ScanType')][ValidateSet('Quick','Full','Custom')][string]$Type='Quick',[string]$Path='')
    if($Type -eq 'Custom' -and [string]::IsNullOrWhiteSpace($Path)){throw 'Custom Defender scans require a path.'};$parameters=@{ScanType=$Type};if($Path){$parameters.Path=[IO.Path]::GetFullPath($Path)};$action=New-WinCareBridgeAction -Type 'RunDefenderScan' -Label "Run Defender $Type scan" -Risk Moderate -RequiresAdmin $true -Parameters $parameters -TimeoutSeconds 86400 -Verification 'Start-MpScan must complete without an exception and Defender operational events must be queryable.';New-WinCareBridgePlan -Title "Defender $Type scan" -Actions @($action) -RollbackOnFailure $false
}
function Invoke-WinCareDefenderSignatureUpdateAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Microsoft Defender';if($blocked){return $blocked};if(-not (Get-Command Update-MpSignature -ErrorAction SilentlyContinue)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'DefenderCmdletUnavailable' -Message 'Update-MpSignature is unavailable.' -ExitCode 127};try{$before=Get-MpComputerStatus -ErrorAction Stop;Update-MpSignature -ErrorAction Stop;$after=Get-MpComputerStatus -ErrorAction Stop;$success=$after.AntivirusSignatureVersion -ge $before.AntivirusSignatureVersion;New-WinCareBridgeResult -Success $success -Code $(if($success){'DefenderSignaturesUpdated'}else{'DefenderSignatureVerificationFailed'}) -Message $(if($success){'Defender signatures were updated or confirmed current.'}else{'Defender signature state could not be verified.'}) -Data @{BeforeVersion=$before.AntivirusSignatureVersion;AfterVersion=$after.AntivirusSignatureVersion;BeforeUpdated=$before.AntivirusSignatureLastUpdated;AfterUpdated=$after.AntivirusSignatureLastUpdated;EvidenceType='DefenderStatus'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'DefenderSignatureUpdateFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareDefenderScanAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Microsoft Defender';if($blocked){return $blocked};if(-not (Get-Command Start-MpScan -ErrorAction SilentlyContinue)){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'DefenderCmdletUnavailable' -Message 'Start-MpScan is unavailable.' -ExitCode 127};$p=Get-WinCareActionParameters $Action;$type=[string]$p.ScanType;try{$started=[datetime]::UtcNow;if($type -eq 'Custom'){Start-MpScan -ScanType CustomScan -ScanPath ([string]$p.Path) -ErrorAction Stop}else{Start-MpScan -ScanType ($type+'Scan') -ErrorAction Stop};$events=@(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-Windows Defender/Operational';StartTime=$started.AddMinutes(-1)} -MaxEvents 20 -ErrorAction SilentlyContinue|Select-Object TimeCreated,Id,LevelDisplayName,Message);New-WinCareBridgeResult -Success $true -Code 'DefenderScanCompleted' -Message "Defender $type scan command completed." -Data @{ScanType=$type;Path=$p.Path;StartedAt=$started.ToString('o');Events=$events;EvidenceType='DefenderCommandAndEvents'}}catch{New-WinCareBridgeResult -Success $false -Code 'DefenderScanFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Get-WinCareListeningPort {
    [CmdletBinding()]
    param()
    if($IsWindows -and (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)){return @(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue|ForEach-Object{$process=$null;try{$process=Get-Process -Id $_.OwningProcess -ErrorAction Stop}catch{};[pscustomobject]@{Protocol='TCP';LocalAddress=$_.LocalAddress;LocalPort=$_.LocalPort;ProcessId=$_.OwningProcess;ProcessName=if($process){$process.ProcessName}else{$null};CreationTime=$_.CreationTime}})}
    $listeners=[Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners();return @($listeners|ForEach-Object{[pscustomobject]@{Protocol='TCP';LocalAddress=$_.Address.ToString();LocalPort=$_.Port;ProcessId=$null;ProcessName=$null}})
}
function Get-WinCareWuaUpdate {
    [CmdletBinding()]
    param([string]$Criteria="IsInstalled=0 and Type='Software'",[ValidateRange(0,10000)][int]$Limit=0)
    if(-not $IsWindows){return @()};$updates=[Collections.Generic.List[object]]::new()
    try{$session=New-Object -ComObject Microsoft.Update.Session;$searcher=$session.CreateUpdateSearcher();$result=$searcher.Search($Criteria);$count=if($Limit -gt 0){[math]::Min($Limit,$result.Updates.Count)}else{$result.Updates.Count};for($i=0;$i -lt $count;$i++){$u=$result.Updates.Item($i);$updates.Add([pscustomobject]@{Index=$i;UpdateId=$u.Identity.UpdateID;RevisionNumber=$u.Identity.RevisionNumber;Title=$u.Title;Description=$u.Description;KBArticleIDs=@($u.KBArticleIDs);Categories=@($u.Categories|ForEach-Object{$_.Name});MsrcSeverity=$u.MsrcSeverity;IsDownloaded=$u.IsDownloaded;IsInstalled=$u.IsInstalled;IsHidden=$u.IsHidden;RebootRequired=$u.RebootRequired;MaxDownloadSize=[long]$u.MaxDownloadSize;EulaAccepted=$u.EulaAccepted;EvidenceType='WindowsUpdateAgentSearch'})}}catch{Write-Verbose "Windows Update search failed: $($_.Exception.Message)"};return @($updates)
}
function Get-WinCareWuaHistory {
    [CmdletBinding()]
    param([Alias('Count')][ValidateRange(0,10000)][int]$Limit=200)
    if(-not $IsWindows){return @()};$history=[Collections.Generic.List[object]]::new();try{$session=New-Object -ComObject Microsoft.Update.Session;$searcher=$session.CreateUpdateSearcher();$count=[math]::Min($Limit,$searcher.GetTotalHistoryCount());foreach($entry in @($searcher.QueryHistory(0,$count))){$history.Add([pscustomobject]@{Date=$entry.Date;Title=$entry.Title;Description=$entry.Description;Operation=$entry.Operation;ResultCode=$entry.ResultCode;HResult=$entry.HResult;UnmappedResultCode=$entry.UnmappedResultCode;ClientApplicationID=$entry.ClientApplicationID;UpdateId=$entry.UpdateIdentity.UpdateID;RevisionNumber=$entry.UpdateIdentity.RevisionNumber;EvidenceType='WindowsUpdateAgentHistory'})}}catch{};return @($history)
}
function New-WinCareWuaHiddenPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('UpdateId')][string[]]$KBs,[bool]$Hidden=$true)
    $updates=@(Get-WinCareWuaUpdate);$actions=[Collections.Generic.List[object]]::new();foreach($token in $KBs){$matches=@($updates|Where-Object{$_.UpdateId -eq $token -or $token -in @($_.KBArticleIDs) -or ('KB'+$token) -in @($_.KBArticleIDs)});foreach($u in $matches){$actions.Add((New-WinCareBridgeAction -Type 'WuaSetHidden' -Label "Set hidden=$Hidden for $($u.Title)" -Risk Moderate -RequiresAdmin $true -Parameters @{UpdateId=$u.UpdateId;Hidden=$Hidden} -Verification 'Windows Update Agent must report the requested IsHidden value.'))}};New-WinCareBridgePlan -Title 'Set Windows Update visibility' -Actions @($actions)
}
function New-WinCareWuaDownloadPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('UpdateId')][string[]]$KBs)
    $updates=@(Get-WinCareWuaUpdate);$ids=@($updates|Where-Object{$_.UpdateId -in $KBs -or @($_.KBArticleIDs|Where-Object{$_ -in $KBs -or ('KB'+$_) -in $KBs}).Count -gt 0}|Select-Object -ExpandProperty UpdateId -Unique);if(-not $ids.Count){throw 'No matching Windows updates were found.'};$action=New-WinCareBridgeAction -Type 'WuaDownload' -Label 'Download selected Windows updates' -Risk Moderate -RequiresAdmin $true -Parameters @{UpdateIds=$ids} -TimeoutSeconds 21600 -Verification 'Every selected update must report IsDownloaded=true or an explicit WUA result code.';New-WinCareBridgePlan -Title 'Download Windows updates' -Actions @($action) -RollbackOnFailure $false
}
function New-WinCareWuaInstallPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('UpdateId')][string[]]$KBs)
    $updates=@(Get-WinCareWuaUpdate);$ids=@($updates|Where-Object{$_.UpdateId -in $KBs -or @($_.KBArticleIDs|Where-Object{$_ -in $KBs -or ('KB'+$_) -in $KBs}).Count -gt 0}|Select-Object -ExpandProperty UpdateId -Unique);if(-not $ids.Count){throw 'No matching Windows updates were found.'};$action=New-WinCareBridgeAction -Type 'WuaInstall' -Label 'Install selected Windows updates' -Risk High -RequiresAdmin $true -Parameters @{UpdateIds=$ids} -TimeoutSeconds 43200 -Verification 'WUA installation result codes must be captured for every update.';New-WinCareBridgePlan -Title 'Install Windows updates' -Actions @($action) -RollbackOnFailure $false
}
function New-WinCareWuaUninstallPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Alias('UpdateId')][string[]]$KBs)
    if(-not $KBs.Count){throw 'At least one update identity is required.'};$action=New-WinCareBridgeAction -Type 'WuaUninstall' -Label 'Uninstall selected Windows updates' -Risk Critical -RequiresAdmin $true -Parameters @{UpdateIds=@($KBs)} -TimeoutSeconds 43200 -Verification 'Each selected update must produce an explicit uninstall exit code and post-state evidence.';New-WinCareBridgePlan -Title 'Uninstall Windows updates' -Actions @($action) -RollbackOnFailure $false
}
function Get-WinCareWuaComUpdatesById {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string[]]$UpdateIds,[string]$Criteria="IsInstalled=0")
    $session=New-Object -ComObject Microsoft.Update.Session;$searcher=$session.CreateUpdateSearcher();$result=$searcher.Search($Criteria);$collection=New-Object -ComObject Microsoft.Update.UpdateColl;for($i=0;$i -lt $result.Updates.Count;$i++){$u=$result.Updates.Item($i);if($u.Identity.UpdateID -in $UpdateIds){if(-not $u.EulaAccepted){$u.AcceptEula()};[void]$collection.Add($u)}};[pscustomobject]@{Session=$session;Collection=$collection}
}
function Invoke-WinCareWuaSetHiddenAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $blocked=Test-WinCareWindowsRequired 'Windows Update Agent';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;try{$session=New-Object -ComObject Microsoft.Update.Session;$searcher=$session.CreateUpdateSearcher();$search=$searcher.Search("UpdateID='$($p.UpdateId)'");if($search.Updates.Count -eq 0){return New-WinCareBridgeResult -Success $false -Code 'WuaUpdateNotFound' -Message 'The update was not found.' -ExitCode 2};$u=$search.Updates.Item(0);$before=$u.IsHidden;$u.IsHidden=[bool]$p.Hidden;$verify=$searcher.Search("UpdateID='$($p.UpdateId)'").Updates.Item(0).IsHidden;$success=$verify -eq [bool]$p.Hidden;New-WinCareBridgeResult -Success $success -Code $(if($success){'WuaVisibilityChanged'}else{'WuaVisibilityVerificationFailed'}) -Message $(if($success){'Update visibility was changed and verified.'}else{'Update visibility change could not be verified.'}) -Data @{UpdateId=$p.UpdateId;Before=$before;After=$verify;EvidenceType='WuaUpdateState'} -ExitCode $(if($success){0}else{74})}catch{New-WinCareBridgeResult -Success $false -Code 'WuaVisibilityFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareWuaDownloadAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $blocked=Test-WinCareWindowsRequired 'Windows Update Agent';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;try{$bundle=Get-WinCareWuaComUpdatesById -UpdateIds @($p.UpdateIds);if($bundle.Collection.Count -eq 0){return New-WinCareBridgeResult -Success $false -Code 'WuaUpdatesNotFound' -Message 'No selected updates were found.' -ExitCode 2};$downloader=$bundle.Session.CreateUpdateDownloader();$downloader.Updates=$bundle.Collection;$result=$downloader.Download();$details=for($i=0;$i -lt $bundle.Collection.Count;$i++){[pscustomobject]@{UpdateId=$bundle.Collection.Item($i).Identity.UpdateID;Title=$bundle.Collection.Item($i).Title;ResultCode=$result.GetUpdateResult($i).ResultCode;HResult=$result.GetUpdateResult($i).HResult;Downloaded=$bundle.Collection.Item($i).IsDownloaded}};$success=@($details|Where-Object{-not $_.Downloaded}).Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'WuaDownloadCompleted'}else{'WuaDownloadIncomplete'}) -Message $(if($success){'Selected updates were downloaded.'}else{'One or more updates were not downloaded.'}) -Data @{OperationId=$OperationId;ResultCode=$result.ResultCode;Updates=$details;EvidenceType='WuaDownloadResult'} -ExitCode $(if($success){0}else{1})}catch{New-WinCareBridgeResult -Success $false -Code 'WuaDownloadFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareWuaInstallAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $blocked=Test-WinCareWindowsRequired 'Windows Update Agent';if($blocked){return $blocked};$p=Get-WinCareActionParameters $Action;try{$bundle=Get-WinCareWuaComUpdatesById -UpdateIds @($p.UpdateIds);if($bundle.Collection.Count -eq 0){return New-WinCareBridgeResult -Success $false -Code 'WuaUpdatesNotFound' -Message 'No selected updates were found.' -ExitCode 2};$installer=$bundle.Session.CreateUpdateInstaller();$installer.Updates=$bundle.Collection;$result=$installer.Install();$details=for($i=0;$i -lt $bundle.Collection.Count;$i++){[pscustomobject]@{UpdateId=$bundle.Collection.Item($i).Identity.UpdateID;Title=$bundle.Collection.Item($i).Title;ResultCode=$result.GetUpdateResult($i).ResultCode;HResult=$result.GetUpdateResult($i).HResult;RebootRequired=$result.GetUpdateResult($i).RebootRequired}};$success=@($details|Where-Object{$_.ResultCode -notin @(2,3)}).Count -eq 0;New-WinCareBridgeResult -Success $success -Code $(if($success){'WuaInstallCompleted'}else{'WuaInstallFailed'}) -Message $(if($success){'Selected updates were installed.'}else{'One or more updates failed to install.'}) -Data @{OperationId=$OperationId;ResultCode=$result.ResultCode;Updates=$details;EvidenceType='WuaInstallResult'} -ExitCode $(if($success){0}else{1}) -RestartRequired ([bool]$result.RebootRequired)}catch{New-WinCareBridgeResult -Success $false -Code 'WuaInstallException' -Message $_.Exception.Message -ExitCode 1}
}
function Invoke-WinCareWuaUninstallAction {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action,[string]$OperationId='')
    $blocked=Test-WinCareWindowsRequired 'Windows update uninstall'
    if($blocked){return $blocked}
    $p=Get-WinCareActionParameters $Action
    $details=[Collections.Generic.List[object]]::new()
    $allSuccess=$true
    $restart=$false
    foreach($token in @($p.UpdateIds)){
        $kb=([string]$token).Replace('KB','').Trim()
        if($kb -notmatch '^\d{4,10}$'){
            $details.Add([pscustomobject]@{Update=$token;Success=$false;BeforeInstalled=$null;AfterInstalled=$null;Error='Only KB identifiers are accepted for uninstall.'})
            $allSuccess=$false
            continue
        }
        $id="KB$kb"
        $before=$null
        try{$before=Get-HotFix -Id $id -ErrorAction Stop}catch{}
        if($null -eq $before){
            $details.Add([pscustomobject]@{Update=$id;Success=$true;BeforeInstalled=$false;AfterInstalled=$false;ExitCode=$null;State='AlreadyAbsent'})
            continue
        }
        $result=Invoke-WinCareBridgeProcess -FilePath 'wusa.exe' -ArgumentList @('/uninstall',"/kb:$kb",'/quiet','/norestart') -TimeoutSeconds 43200 -SuccessExitCodes @(0,1641,3010) -RequireAdmin
        $restart=$restart -or $result.RestartRequired
        Start-Sleep -Seconds 2
        $after=$null
        try{$after=Get-HotFix -Id $id -ErrorAction Stop}catch{}
        $absent=$null -eq $after
        $success=[bool]($result.Success -and $absent)
        $details.Add([pscustomobject]@{
            Update=$id
            Success=$success
            BeforeInstalled=$true
            AfterInstalled=(-not $absent)
            ExitCode=$result.ExitCode
            StdErr=$result.Data.StdErr
            State=if($success){'RemovedAndVerified'}elseif($result.Success){'CommandSucceededButStillPresent'}else{'CommandFailed'}
        })
        if(-not $success){$allSuccess=$false}
    }
    New-WinCareBridgeResult -Success $allSuccess -Code $(if($allSuccess){'WuaUninstallVerified'}else{'WuaUninstallFailed'}) -Message $(if($allSuccess){'Selected updates are absent and the final state was verified.'}else{'One or more update uninstalls failed final-state verification.'}) -Data @{OperationId=$OperationId;Updates=@($details);EvidenceType='HotFixBeforeAfterAndWusaExit'} -ExitCode $(if($allSuccess){0}else{74}) -RestartRequired $restart
}
