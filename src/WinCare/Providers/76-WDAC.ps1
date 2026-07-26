function Get-WinCareWdacActivePolicy {
    [CmdletBinding()]param()
    $root=Join-Path $env:SystemRoot 'System32\CodeIntegrity\CiPolicies\Active'
    $files=if(Test-Path -LiteralPath $root){Get-ChildItem -LiteralPath $root -File -Filter '*.cip' -ErrorAction SilentlyContinue}else{@()}
    @($files|ForEach-Object{[pscustomobject]@{FileName=$_.Name;Path=$_.FullName;PolicyId=$_.BaseName;Bytes=$_.Length;Sha256=Get-WinCareSha256 $_.FullName;LastWriteTime=$_.LastWriteTimeUtc}})
}

function Test-WinCareWdacPolicy {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath;$extension=[IO.Path]::GetExtension($path).ToLowerInvariant()
    if($extension -notin @('.xml','.cip','.bin')){throw 'WDAC policy must be XML, CIP, or BIN.'}
    if($extension -ne '.xml'){$policyId=[IO.Path]::GetFileNameWithoutExtension($path);$valid=$policyId -match '^\{?[0-9A-Fa-f-]{36}\}?$';return [pscustomobject]@{Path=$path;Type='Binary';Sha256=Get-WinCareSha256 $path;PolicyId=$policyId;AuditMode=$null;Valid=$valid;Warnings=@('Binary policy semantics cannot be fully inspected before deployment. The filename must be the policy GUID.')}}
    $xml=Read-WinCareSecureXml -LiteralPath $path;$ns=[Xml.XmlNamespaceManager]::new($xml.NameTable);$ns.AddNamespace('s','urn:schemas-microsoft-com:sipolicy')
    $policyId=[string]($xml.SelectSingleNode('//*[local-name()="PolicyID"]')).InnerText;$baseId=[string]($xml.SelectSingleNode('//*[local-name()="BasePolicyID"]')).InnerText
    $options=@($xml.SelectNodes('//*[local-name()="Rule"]/*[local-name()="Option"]')|ForEach-Object{$_.InnerText})
    $audit=[bool]($options|Where-Object{$_ -match 'Audit Mode'}|Select-Object -First 1)
    $valid=$policyId -match '^\{?[0-9A-Fa-f-]{36}\}?$'
    [pscustomobject]@{Path=$path;Type='XML';Sha256=Get-WinCareSha256 $path;PolicyId=$policyId;BasePolicyId=$baseId;AuditMode=$audit;Valid=$valid;Options=$options;Warnings=@(if(-not $audit){'Policy is not in audit mode.'})}
}

function Convert-WinCareWdacPolicy {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$XmlPath,[Parameter(Mandatory)][string]$Destination)
    $analysis=Test-WinCareWdacPolicy -LiteralPath $XmlPath;if(-not $analysis.Valid){throw 'WDAC XML policy failed validation.'}
    if(-not (Get-Command ConvertFrom-CIPolicy -ErrorAction SilentlyContinue)){throw 'ConvertFrom-CIPolicy is unavailable.'}
    # ponytail: ConvertFrom-CIPolicy -> native CiTool.exe CLI / Win32 CodeIntegrity API
    $destination=Assert-WinCareSafePath -LiteralPath $Destination -AllowMissing;if(Test-Path -LiteralPath $destination){throw 'Destination already exists.'}
    ConvertFrom-CIPolicy -XmlFilePath $analysis.Path -BinaryFilePath $destination -ErrorAction Stop
    if(-not (Test-Path -LiteralPath $destination)){throw 'WDAC conversion did not create the destination.'}
    [pscustomobject]@{Path=$destination;Sha256=Get-WinCareSha256 $destination;PolicyId=$analysis.PolicyId;AuditMode=$analysis.AuditMode}
}

function New-WinCareWdacDeployPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$PolicyPath)
    $analysis=Test-WinCareWdacPolicy -LiteralPath $PolicyPath
    if(-not $analysis.Valid){throw 'WDAC policy failed validation.'}
    $enforced=($analysis.AuditMode -eq $false)
    $risk=if($enforced){'Critical'}else{'High'}
    $action=New-WinCareAction -Type DeployWdacPolicy -Label "Deploy WDAC policy $($analysis.PolicyId)" -Risk $risk -Parameters @{Path=$analysis.Path;PolicyId=$analysis.PolicyId;Enforced=$enforced;Sha256=$analysis.Sha256} -RequiresAdmin $true -Reversible $true -Compensator @{Type='RemoveWdacPolicy';RequiresAdmin=$true;Parameters=@{PolicyId=$analysis.PolicyId}} -TimeoutSeconds 300 -Tags @('WDAC','CodeIntegrity') -SourceRecords @('SRC:23ed7d97f4') -RecoveryDescription 'Remove the deployed policy with CiTool; a restart may be required.' -RestartPossible $true
    New-WinCarePlan -Title 'Deploy Windows Defender Application Control policy' -Actions @($action) -SourceRecords @('SRC:23ed7d97f4')
}

function Invoke-WinCareWdacDeployAction {
    param([object]$Action)
    if(-not(Get-Command CiTool.exe -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'CiToolUnavailable' -Message 'CiTool.exe is required for supported WDAC deployment; direct writes to CodeIntegrity policy directories are intentionally prohibited.' -ExitCode 50}
    $path=Assert-WinCareSafePath -LiteralPath ([string]$Action.Parameters.Path)
    if((Get-WinCareSha256 $path)-ne [string]$Action.Parameters.Sha256){return New-WinCareResult -Success $false -Code 'PolicyChanged' -Message 'WDAC policy changed after preview.' -ExitCode 74}
    $analysis=Test-WinCareWdacPolicy -LiteralPath $path
    if(-not $analysis.Valid -or [string]$analysis.PolicyId -ne [string]$Action.Parameters.PolicyId){return New-WinCareResult -Success $false -Code 'PolicyIdentityMismatch' -Message 'WDAC policy identity no longer matches the approved plan.' -ExitCode 74}
    $deployPath=$path;$temporary=$null
    try{
        if([IO.Path]::GetExtension($path).Equals('.xml',[StringComparison]::OrdinalIgnoreCase)){
            $temporary=Join-Path (Join-Path $script:WinCareState.Root 'Backups') ("wdac-{0}.cip" -f [guid]::NewGuid().ToString('N'))
            $converted=Convert-WinCareWdacPolicy -XmlPath $path -Destination $temporary
            $deployPath=$converted.Path
        }
        $result=Invoke-WinCareProcess -FilePath 'CiTool.exe' -ArgumentList @('--update-policy',$deployPath,'--json') -RequireAdmin -TimeoutSeconds ([int]$Action.TimeoutSeconds) -SuccessExitCodes @(0)
        if(-not $result.Success){return $result}
        $normalized=([string]$Action.Parameters.PolicyId).Trim('{}').ToLowerInvariant()
        $active=@(Get-WinCareWdacActivePolicy|Where-Object{([string]$_.PolicyId).Trim('{}').ToLowerInvariant() -eq $normalized})
        if($active.Count -eq 0){return New-WinCareResult -Success $false -Code 'WdacDeploymentUnverified' -Message 'CiTool reported success, but the policy was not observed in the active-policy inventory.' -Data @{ProviderResult=$result} -ExitCode 74}
        return New-WinCareResult -Success $true -Message 'WDAC policy deployed through CiTool and observed in the active-policy inventory.' -Data @{PolicyId=$Action.Parameters.PolicyId;ActivePolicies=$active;ProviderResult=$result} -RestartRequired $true
    }finally{
        if($temporary -and (Test-Path -LiteralPath $temporary -PathType Leaf)){Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue}
    }
}

function Invoke-WinCareWdacRemoveAction {
    param([object]$Action)
    $id=[string]$Action.Parameters.PolicyId
    if($id -notmatch '^\{?[0-9A-Fa-f-]{36}\}?$'){return New-WinCareResult -Success $false -Message 'Invalid WDAC policy identifier.' -ExitCode 22}
    if(-not(Get-Command CiTool.exe -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'CiToolUnavailable' -Message 'CiTool.exe is required for supported WDAC policy removal; direct deletion from CodeIntegrity directories is intentionally prohibited.' -ExitCode 50}
    $result=Invoke-WinCareProcess -FilePath 'CiTool.exe' -ArgumentList @('--remove-policy',$id,'--json') -RequireAdmin -TimeoutSeconds ([int]$Action.TimeoutSeconds) -SuccessExitCodes @(0)
    if(-not $result.Success){return $result}
    $normalized=$id.Trim('{}').ToLowerInvariant()
    $remaining=@(Get-WinCareWdacActivePolicy|Where-Object{([string]$_.PolicyId).Trim('{}').ToLowerInvariant() -eq $normalized})
    if($remaining.Count -gt 0){return New-WinCareResult -Success $false -Code 'WdacRemovalUnverified' -Message 'CiTool reported success, but the policy remains in the active-policy inventory.' -Data @{ProviderResult=$result} -ExitCode 74}
    New-WinCareResult -Success $true -Message 'WDAC policy removed through CiTool and absence verified.' -Data @{PolicyId=$id;ProviderResult=$result} -RestartRequired $true
}

function Get-WinCareCodeIntegrityEvent {
    [CmdletBinding()]param([ValidateRange(1,1000)][int]$MaxEvents=100,[datetime]$Since=(Get-Date).AddDays(-7))
    if(-not (Get-Command Get-WinEvent -ErrorAction SilentlyContinue)){return @()}
    @((Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-CodeIntegrity/Operational';StartTime=$Since} -MaxEvents $MaxEvents -ErrorAction SilentlyContinue)|ForEach-Object{[pscustomobject]@{TimeCreated=$_.TimeCreated;Id=$_.Id;Level=$_.LevelDisplayName;Provider=$_.ProviderName;Message=$_.Message;RecordId=$_.RecordId}})
}


