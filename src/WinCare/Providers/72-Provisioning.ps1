function Read-WinCareSecureXml {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=50MB;$settings.MaxCharactersFromEntities=0
    $reader=[Xml.XmlReader]::Create($path,$settings)
    try{$document=[Xml.XmlDocument]::new();$document.XmlResolver=$null;$document.Load($reader);return $document}finally{$reader.Dispose()}
}

function Get-WinCareUnattendAnalysis {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $xml=Read-WinCareSecureXml -LiteralPath $LiteralPath;$manager=[Xml.XmlNamespaceManager]::new($xml.NameTable);$manager.AddNamespace('u','urn:schemas-microsoft-com:unattend')
    $passes=[Collections.Generic.List[object]]::new();$risks=[Collections.Generic.List[object]]::new();$commands=[Collections.Generic.List[object]]::new()
    foreach($settings in $xml.SelectNodes('//u:settings',$manager)){$pass=[string]$settings.GetAttribute('pass');$components=@($settings.SelectNodes('./u:component',$manager)|ForEach-Object{[string]$_.GetAttribute('name')});$passes.Add([pscustomobject]@{Pass=$pass;Components=$components})}
    $riskRules=@(
        @{Pattern='Bypass(TPM|CPU|RAM|SecureBoot)Check|LabConfig';Id='hardware-requirement-bypass';Risk='Critical';Message='Bypasses Windows hardware requirements.'},
        @{Pattern='DisableAntiSpyware|DisableRealtimeMonitoring|Set-MpPreference.*Disable';Id='defender-disable';Risk='Critical';Message='Disables or weakens Microsoft Defender.'},
        @{Pattern='netsh\s+interface.*disable|Disable-NetAdapter';Id='network-disable';Risk='High';Message='Disables network interfaces.'},
        @{Pattern='bcdedit\s+/(set|delete|import)';Id='boot-modification';Risk='Critical';Message='Modifies boot configuration.'},
        @{Pattern='Remove-AppxPackage|Remove-AppxProvisionedPackage';Id='app-removal';Risk='Moderate';Message='Removes installed or provisioned applications.'},
        @{Pattern='reg(\.exe)?\s+(add|delete)|Set-ItemProperty|Remove-ItemProperty';Id='registry-change';Risk='Moderate';Message='Changes the registry.'},
        @{Pattern='Invoke-WebRequest|curl(\.exe)?|bitsadmin|Start-BitsTransfer';Id='external-content';Risk='High';Message='Retrieves external content.'}
    )
    foreach($node in $xml.SelectNodes('//*[local-name()="Path" or local-name()="CommandLine" or local-name()="Arguments"]')){
        $text=[string]$node.InnerText;if([string]::IsNullOrWhiteSpace($text)){continue};$text=$text.Trim();$commands.Add([pscustomobject]@{Element=$node.LocalName;Command=$text})
        foreach($rule in $riskRules){if($text -match $rule.Pattern){$risks.Add([pscustomobject]@{Id=$rule.Id;Risk=$rule.Risk;Message=$rule.Message;Command=$text})}}
    }
    $extensions=@($xml.SelectNodes('//*[local-name()="Extensions"]/*')|ForEach-Object{[pscustomobject]@{Name=$_.LocalName;Characters=$_.InnerText.Length;Sha256=(Get-WinCareSha256Text -Text $_.InnerText)}})
    [pscustomobject]@{SchemaVersion=1;Path=(Resolve-WinCareCanonicalPath $LiteralPath);Sha256=Get-WinCareSha256 $LiteralPath;Passes=@($passes);Commands=@($commands);EmbeddedExtensions=$extensions;Risks=@($risks);HasCriticalRisk=[bool]($risks|Where-Object Risk -eq 'Critical'|Select-Object -First 1);AnalyzedAt=[datetime]::UtcNow}
}

function Test-WinCareProvisioningStateItem {
    param([object]$Item,[ValidateSet('Feature','Capability')][string]$Kind)
    if($Item -is [Collections.IDictionary]){
        $null=Test-WinCareStrictObjectKeys -InputObject $Item -AllowedKeys @('name','state') -Context "provisioning $Kind"
        $name=[string]$Item.name;$state=[string]$Item.state
    }else{$name=[string]$Item;$state=if($Kind -eq 'Feature'){'Enabled'}else{'Installed'}}
    if($name -notmatch '^[A-Za-z0-9][A-Za-z0-9._~+-]{1,255}$'){throw "Invalid $Kind name: $name"}
    if($Kind -eq 'Feature' -and $state -notin @('Enabled','Disabled')){throw "Invalid feature state: $state"}
    if($Kind -eq 'Capability' -and $state -notin @('Installed','Removed')){throw "Invalid capability state: $state"}
    [pscustomobject]@{Name=$name;State=$state}
}

function Test-WinCareProvisioningBlueprint {
    param([Parameter(Mandatory)][Collections.IDictionary]$Blueprint)
    $allowed=@('schemaVersion','name','description','catalogRules','removeInstalledAppx','removeProvisionedAppx','capabilities','optionalFeatures','wingetPackages','userStage','systemStage','sourceRecords')
    $null=Test-WinCareStrictObjectKeys -InputObject $Blueprint -AllowedKeys $allowed -Context 'provisioning blueprint'
    if([int]$Blueprint.schemaVersion -ne 1){throw 'Unsupported provisioning blueprint schema.'}
    if([string]::IsNullOrWhiteSpace([string]$Blueprint.name) -or ([string]$Blueprint.name).Length -gt 160){throw 'Provisioning blueprint name is invalid.'}
    if(([string]$Blueprint.description).Length -gt 2000){throw 'Provisioning blueprint description is too long.'}
    $counts=@($Blueprint.catalogRules).Count+@($Blueprint.userStage).Count+@($Blueprint.systemStage).Count+@($Blueprint.removeInstalledAppx).Count+@($Blueprint.removeProvisionedAppx).Count+@($Blueprint.capabilities).Count+@($Blueprint.optionalFeatures).Count+@($Blueprint.wingetPackages).Count
    if($counts -gt 500){throw 'Provisioning blueprint exceeds the 500-item limit.'}
    foreach($id in @($Blueprint.catalogRules)+@($Blueprint.userStage)+@($Blueprint.systemStage)){if([string]$id -notmatch '^[a-z0-9._-]+$'){throw "Invalid catalog rule ID: $id"}}
    foreach($package in @($Blueprint.removeInstalledAppx)){if([string]$package -notmatch '^[A-Za-z0-9_.-]+_[A-Za-z0-9.]+_[A-Za-z0-9]+__?[A-Za-z0-9]+$'){throw "Installed AppX removal requires an exact package full name: $package"}}
    foreach($package in @($Blueprint.removeProvisionedAppx)){if([string]$package -notmatch '^[A-Za-z0-9_.-]+_[A-Za-z0-9.]+_[A-Za-z0-9]+__?[A-Za-z0-9]+$'){throw "Provisioned AppX removal requires an exact package name: $package"}}
    foreach($package in @($Blueprint.wingetPackages)){if([string]$package -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{1,127}$'){throw "Invalid WinGet package ID: $package"}}
    foreach($item in @($Blueprint.optionalFeatures)){$null=Test-WinCareProvisioningStateItem -Item $item -Kind Feature}
    foreach($item in @($Blueprint.capabilities)){$null=Test-WinCareProvisioningStateItem -Item $item -Kind Capability}
    foreach($record in @($Blueprint.sourceRecords)){if([string]$record -notmatch '^[A-Za-z0-9._:-]{2,160}$'){throw "Invalid source record: $record"}}
    return $true
}

function Import-WinCareProvisioningBlueprint {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $blueprint=Read-WinCareJsonHashtable -LiteralPath (Assert-WinCareSafePath -LiteralPath $LiteralPath);$null=Test-WinCareProvisioningBlueprint -Blueprint $blueprint;return $blueprint
}

function Add-WinCareProvisioningCatalogRules {
    param([object]$Plan,[string[]]$RuleId,[string]$Title)
    if(@($RuleId).Count -eq 0){return}
    $catalogPlan=New-WinCareCatalogPlan -RuleId @($RuleId|Sort-Object -Unique) -Title $Title
    foreach($action in $catalogPlan.Actions){$Plan.Actions.Add($action)}
}

function New-WinCareProvisioningPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Blueprint,[ValidateSet('All','System','User')][string]$Stage='All')
    $null=Test-WinCareProvisioningBlueprint -Blueprint $Blueprint
    $plan=New-WinCarePlan -Title ("{0} [{1}]" -f [string]$Blueprint.name,$Stage) -Description ([string]$Blueprint.description) -SourceRecords @($Blueprint.sourceRecords)+@('D03-UNATTENDED-PASSES')
    Add-WinCareProvisioningCatalogRules -Plan $plan -RuleId @($Blueprint.catalogRules) -Title 'Provisioning common configuration'
    if($Stage -in @('All','System')){
        Add-WinCareProvisioningCatalogRules -Plan $plan -RuleId @($Blueprint.systemStage) -Title 'Provisioning system-stage configuration'
        foreach($name in @($Blueprint.removeProvisionedAppx)){$plan.Actions.Add((New-WinCareAction -Type RemoveProvisionedAppx -Label "Remove provisioned package $name" -Risk High -Parameters @{PackageName=[string]$name} -RequiresAdmin $true -Reversible $false -Tags @('Provisioning','Appx','SystemStage') -SourceRecords @('D02-APPX-REMOVAL','D03-APP-REMOVAL') -RecoveryDescription 'Reinstall from the Microsoft Store or installation media where available.'))}
        foreach($item in @($Blueprint.optionalFeatures)){$state=Test-WinCareProvisioningStateItem -Item $item -Kind Feature;$plan.Actions.Add((New-WinCareAction -Type SetOptionalFeatureState -Label "$($state.State) optional feature $($state.Name)" -Risk Moderate -Parameters @{Name=$state.Name;State=$state.State} -RequiresAdmin $true -Reversible $false -RestartPossible $true -Tags @('Provisioning','Feature','SystemStage') -SourceRecords @('D03-WINDOWS-FEATURES')))}
        foreach($item in @($Blueprint.capabilities)){$state=Test-WinCareProvisioningStateItem -Item $item -Kind Capability;$plan.Actions.Add((New-WinCareAction -Type SetCapabilityState -Label "$($state.State) capability $($state.Name)" -Risk Moderate -Parameters @{Name=$state.Name;State=$state.State} -RequiresAdmin $true -Reversible $false -RestartPossible $true -Tags @('Provisioning','Capability','SystemStage') -SourceRecords @('D02-CAPABILITIES','D03-WINDOWS-CAPABILITIES')))}
    }
    if($Stage -in @('All','User')){
        Add-WinCareProvisioningCatalogRules -Plan $plan -RuleId @($Blueprint.userStage) -Title 'Provisioning user-stage configuration'
        foreach($name in @($Blueprint.removeInstalledAppx)){$plan.Actions.Add((New-WinCareAction -Type RemoveAppxPackage -Label "Remove installed package $name" -Risk Moderate -Parameters @{PackageFullName=[string]$name;Name=[string]$name} -RequiresAdmin $false -Reversible $false -Tags @('Provisioning','Appx','UserStage') -SourceRecords @('D02-APPX-REMOVAL','D03-APP-REMOVAL') -RecoveryDescription 'Reinstall from the Microsoft Store or installation media where available.'))}
        foreach($id in @($Blueprint.wingetPackages)){$plan.Actions.Add((New-WinCareAction -Type InstallWingetPackage -Label "Install WinGet package $id" -Risk Moderate -Parameters @{PackageId=[string]$id} -RequiresAdmin $false -Reversible $false -TimeoutSeconds 7200 -Tags @('Provisioning','WinGet','UserStage') -SourceRecords @('D03-APP-INSTALL')))}
    }
    $validation=Test-WinCarePlanContract -Plan $plan;if(-not $validation.Success){throw "Provisioning plan is invalid: $($validation.Message)"}
    return $plan
}

function Export-WinCareProvisioningKit {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Blueprint,[Parameter(Mandatory)][string]$Destination)
    $null=Test-WinCareProvisioningBlueprint -Blueprint $Blueprint;$destination=Assert-WinCareSafePath -LiteralPath $Destination -AllowMissing
    if(Test-Path -LiteralPath $destination){throw 'Provisioning destination already exists.'}
    $programDataRoot=Join-Path $destination '$OEM$\$1\ProgramData\WinCareProvisioning'
    $setupRoot=Join-Path $destination '$OEM$\$$\Setup\Scripts'
    $null=New-Item -ItemType Directory -Path $programDataRoot -Force;$null=New-Item -ItemType Directory -Path $setupRoot -Force
    Write-WinCareAtomicJson -LiteralPath (Join-Path $programDataRoot 'blueprint.json') -InputObject $Blueprint
    Copy-Item -LiteralPath $script:WinCareModuleRoot -Destination (Join-Path $programDataRoot 'WinCare') -Recurse
    $runner=@'
#requires -Version 7.2
[CmdletBinding()]param([ValidateSet('System','User')][string]$Stage,[switch]$Apply)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw|ConvertFrom-Json
foreach($file in $manifest.files){$path=Join-Path $root $file.path;if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Provisioning kit file missing: $($file.path)"};$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();if($hash -ne $file.sha256){throw "Provisioning kit file changed: $($file.path)"}}
Import-Module (Join-Path $root 'WinCare\WinCare.psd1') -Force
$blueprint=Get-Content -LiteralPath (Join-Path $root 'blueprint.json') -Raw|ConvertFrom-Json -AsHashtable -Depth 40
Initialize-WinCareState -SkipConfigSave
$plan=New-WinCareProvisioningPlan -Blueprint $blueprint -Stage $Stage
$preview=Invoke-WinCarePlan -Plan $plan -PreviewOnly
$result=if($Apply -and $preview.Success){Invoke-WinCarePlan -Plan $plan -Approved}else{$preview}
$result|ConvertTo-Json -Depth 60|Set-Content -LiteralPath (Join-Path $root ("{0}-{1}.json" -f $Stage,$(if($Apply){'result'}else{'preview'}))) -Encoding utf8
if(-not $result.Success){exit 1}
'@
    $runner|Set-Content -LiteralPath (Join-Path $programDataRoot 'Invoke-Provisioning.ps1') -Encoding utf8NoBOM
    @'
@echo off
set ROOT=%ProgramData%\WinCareProvisioning
where pwsh.exe >nul 2>&1 || (echo WinCare requires PowerShell 7.2+>>"%ROOT%\setup.log" & exit /b 50)
pwsh.exe -NoProfile -NonInteractive -File "%ROOT%\Invoke-Provisioning.ps1" -Stage System
if exist "%ROOT%\apply-system.marker" pwsh.exe -NoProfile -NonInteractive -File "%ROOT%\Invoke-Provisioning.ps1" -Stage System -Apply
'@|Set-Content -LiteralPath (Join-Path $setupRoot 'SetupComplete.cmd') -Encoding ascii
    @'
@echo off
set ROOT=%ProgramData%\WinCareProvisioning
where pwsh.exe >nul 2>&1 || (echo WinCare requires PowerShell 7.2+>>"%ROOT%\first-logon.log" & exit /b 50)
pwsh.exe -NoProfile -NonInteractive -File "%ROOT%\Invoke-Provisioning.ps1" -Stage User
if exist "%ROOT%\apply-user.marker" pwsh.exe -NoProfile -NonInteractive -File "%ROOT%\Invoke-Provisioning.ps1" -Stage User -Apply
'@|Set-Content -LiteralPath (Join-Path $programDataRoot 'FirstLogon.cmd') -Encoding ascii
    @'
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
      <FirstLogonCommands>
        <SynchronousCommand wcm:action="add"><Order>1</Order><Description>WinCare user-stage provisioning preview</Description><CommandLine>cmd.exe /d /c "%ProgramData%\WinCareProvisioning\FirstLogon.cmd"</CommandLine></SynchronousCommand>
      </FirstLogonCommands>
    </component>
  </settings>
</unattend>
'@|Set-Content -LiteralPath (Join-Path $destination 'autounattend.xml') -Encoding utf8NoBOM
    $manifestFiles=@(Get-ChildItem -LiteralPath $programDataRoot -File -Recurse|Where-Object Name -ne 'manifest.json'|Sort-Object FullName|ForEach-Object{[pscustomobject]@{path=[IO.Path]::GetRelativePath($programDataRoot,$_.FullName).Replace('\','/');sha256=Get-WinCareSha256 $_.FullName;bytes=$_.Length}})
    Write-WinCareAtomicJson -LiteralPath (Join-Path $programDataRoot 'manifest.json') -InputObject ([ordered]@{schemaVersion=2;generatedAt=[datetime]::UtcNow.ToString('o');blueprintHash=Get-WinCareSha256 (Join-Path $programDataRoot 'blueprint.json');files=$manifestFiles;applyMarkers=@('apply-system.marker','apply-user.marker');powerShellRequirement='7.2+'})
    return [pscustomobject]@{Path=$destination;OemRoot=(Join-Path $destination '$OEM$');Manifest=(Join-Path $programDataRoot 'manifest.json');Files=@(Get-ChildItem -LiteralPath $destination -File -Recurse).Count}
}


