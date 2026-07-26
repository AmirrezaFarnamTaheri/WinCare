#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Console compatibility helpers, reports, DNS validation, installer wrappers, and host entry point.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Write-WinCareFooter {
    [CmdletBinding()]
    param([string]$Text='')
    Write-Host ('-'*72) -ForegroundColor DarkGray
    if (-not [string]::IsNullOrWhiteSpace($Text)) { Write-Host $Text -ForegroundColor DarkGray }
}
function Read-WinCareKey {[CmdletBinding()]param();[Console]::ReadKey($true)}
function Read-WinCareInput {
    [CmdletBinding()]
    param([string]$Prompt='',[AllowEmptyString()][string]$Default='')
    $displayPrompt = if ($PSBoundParameters.ContainsKey('Default') -and $Default -ne '') { "$Prompt [$Default]" } else { $Prompt }
    $value = Read-Host -Prompt $displayPrompt
    if ([string]::IsNullOrEmpty($value) -and $PSBoundParameters.ContainsKey('Default')) { return $Default }
    return $value
}
function Wait-WinCareKey {[CmdletBinding()]param();Write-Host 'Press any key to continue...' -NoNewline;[void][Console]::ReadKey($true);Write-Host}
function Show-WinCareMessage {[CmdletBinding()]param([Parameter(Mandatory)][string]$Message,[ValidateSet('Info','Warning','Error')][string]$Level='Info');$color=switch($Level){'Error'{'Red'}'Warning'{'Yellow'}default{'Cyan'}};Write-Host $Message -ForegroundColor $color}
function Show-WinCareResult {[CmdletBinding()]param([Parameter(Mandatory)][object]$Result);$color=if($Result.Success){'Green'}elseif($Result.Status -eq 'Blocked'){'Yellow'}else{'Red'};Write-Host ("[{0}] {1}: {2}" -f $Result.Status,$Result.Code,$Result.Message) -ForegroundColor $color;if($Result.Data){$Result.Data|Format-List|Out-Host}}
function Show-WinCareTextPage {[CmdletBinding()]param([Parameter(Mandatory)][string]$Text,[string]$Title='');if($Title){Write-Host $Title -ForegroundColor Cyan;Write-WinCareFooter};Write-Host $Text}
function Get-WinCareMainMenuItems {[CmdletBinding()]param();@([pscustomobject]@{Id='dashboard';Title='Dashboard'},[pscustomobject]@{Id='security';Title='Security'},[pscustomobject]@{Id='cleanup';Title='Cleanup'},[pscustomobject]@{Id='storage';Title='Storage'},[pscustomobject]@{Id='network';Title='Network'},[pscustomobject]@{Id='updates';Title='Updates'},[pscustomobject]@{Id='reports';Title='Reports'},[pscustomobject]@{Id='exit';Title='Exit'})}
function New-WinCareSystemReportData {
    [CmdletBinding()]
    param()
    [ordered]@{
        SchemaVersion=2
        GeneratedAt=[datetime]::UtcNow.ToString('o')
        ComputerName=[Environment]::MachineName
        Privacy=[ordered]@{BodiesIncluded=$false;CredentialMaterialIncluded=$false;SecretValuesIncluded=$false}
        BodiesIncluded=$false
        System=Get-WinCareSystemSummary
        Security=Get-WinCareSecuritySummary
        Network=Get-WinCareNetworkSummary
        Health=@(Get-WinCareHealthFinding)
        Storage=@(Get-WinCareDriveSummary)
        Updates=@(Get-WinCareWuaUpdate)
        UpdateHistory=@(Get-WinCareWuaHistory -Limit 50)
        ListeningPorts=@(Get-WinCareListeningPort)
        StartupItems=if(Get-Command Get-WinCareStartupItem -ErrorAction SilentlyContinue){@(Get-WinCareStartupItem)}else{@()}
        Applications=if(Get-Command Get-WinCareInstalledApplication -ErrorAction SilentlyContinue){@(Get-WinCareInstalledApplication)}else{@()}
        Telemetry=Get-WinCareTelemetrySnapshot
        Capabilities=if(Get-Command Get-WinCareCapabilityRegistry -ErrorAction SilentlyContinue){@(Get-WinCareCapabilityRegistry)}else{@()}
    }
}
function Export-WinCareSystemReport {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,[ValidateSet('Json','Markdown','Html')][string]$Format='Json')
    try{
        $html1='<!doctype html><html><head><meta charset="utf-8"><title>WinCare System Report</title><style>body{font-family:system-ui,sans-serif;margin:2rem}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f4f4f4;padding:1rem;border-radius:.5rem}</style></head><body><h1>WinCare System Report</h1><p>Generated: '
        $html2='</p><pre>'
        $html3='</pre></body></html>'
        $nl=[Environment]::NewLine
        $mdHeader='# WinCare System Report' + $nl + $nl + 'Generated: ' + $data.GeneratedAt + $nl + $nl + '```json' + $nl + $json + $nl + '```'
        $content=switch($Format){
            'Markdown'{$mdHeader}
            'Html'{$html1 + [Net.WebUtility]::HtmlEncode([string]$data.GeneratedAt) + $html2 + [Net.WebUtility]::HtmlEncode($json) + $html3}
            default{$json}
        }
        if(-not (Get-Command Write-WinCareAtomicText -ErrorAction SilentlyContinue)){throw 'The WinCare atomic text writer is unavailable.'};Write-WinCareAtomicText -LiteralPath $full -Text $content
        $success=Test-Path $full -PathType Leaf;$hash=if($success){Get-WinCareSha256 -LiteralPath $full}else{$null};New-WinCareBridgeResult -Success $success -Code $(if($success){'SystemReportExported'}else{'SystemReportExportFailed'}) -Message $(if($success){'System report was exported and file-verified.'}else{'System report file was not created.'}) -Data @{Path=$full;Format=$Format;Sha256=$hash;Bytes=if($success){(Get-Item $full).Length}else{0};EvidenceType='ReportFile'} -ExitCode $(if($success){0}else{74})
    }catch{New-WinCareBridgeResult -Success $false -Code 'SystemReportExportFailed' -Message $_.Exception.Message -ExitCode 1}
}
function Test-WinCareDnsHostName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$HostName)
    if($HostName.Length -gt 253 -or $HostName.EndsWith('.')){$HostName=$HostName.TrimEnd('.')};if($HostName.Length -lt 1 -or $HostName.Length -gt 253){return $false};foreach($label in $HostName.Split('.')){if($label.Length -lt 1 -or $label.Length -gt 63 -or $label -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?$'){return $false}};return $true
}
function Fix-WinCareVulnerability {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Id,[switch]$Approved)
    $catalogPath=if($script:WinCareModuleRoot){Join-Path $script:WinCareModuleRoot 'Data\Security\vulnerability-remediations.json'}else{''};$catalog=if($catalogPath -and (Test-Path $catalogPath)){@(Read-WinCareBridgeJson $catalogPath)}else{@()};$entry=$catalog|Where-Object{$_.Id -eq $Id}|Select-Object -First 1;if(-not $entry){return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'RemediationNotCataloged' -Message ('No reviewed remediation is cataloged for ' + $Id + '. WinCare will not synthesize or execute an unreviewed fix.') -ExitCode 2 -Data @{CatalogPath=$catalogPath}};$plan=New-WinCarePlaybookPlan -Id $entry.PlaybookId;if(-not $Approved){return New-WinCareBridgeResult -Success $true -Status Preview -Code 'RemediationPlanReady' -Message 'Reviewed remediation plan is ready for explicit approval.' -Data @{Plan=$plan;Vulnerability=$entry}};Invoke-WinCarePlan -Plan $plan -Approved
}
function Install-WinCare {
    [CmdletBinding()]
    param([string[]]$ArgumentList=@())
    $scriptPath=[IO.Path]::GetFullPath((Join-Path $script:WinCareModuleRoot '..\..\Install-WinCare.ps1'));if(-not (Test-Path $scriptPath)){return New-WinCareBridgeResult -Success $false -Code 'InstallerNotFound' -Message 'Install-WinCare.ps1 was not found.' -ExitCode 2};$pwsh=Get-WinCareCurrentPowerShellExecutable;Invoke-WinCareBridgeProcess -FilePath $pwsh -ArgumentList @('-NoProfile','-File',$scriptPath)+$ArgumentList -TimeoutSeconds 3600 -SuccessExitCodes @(0)
}
function Uninstall-WinCare {
    [CmdletBinding()]
    param([string[]]$ArgumentList=@())
    $scriptPath=[IO.Path]::GetFullPath((Join-Path $script:WinCareModuleRoot '..\..\Uninstall-WinCare.ps1'));if(-not (Test-Path $scriptPath)){return New-WinCareBridgeResult -Success $false -Code 'UninstallerNotFound' -Message 'Uninstall-WinCare.ps1 was not found.' -ExitCode 2};$pwsh=Get-WinCareCurrentPowerShellExecutable;Invoke-WinCareBridgeProcess -FilePath $pwsh -ArgumentList @('-NoProfile','-File',$scriptPath)+$ArgumentList -TimeoutSeconds 3600 -SuccessExitCodes @(0)
}
function Invoke-WinCare {
    [CmdletBinding()]
    param([string]$Command='',[hashtable]$Parameters=@{})
    if($Command){if(-not (Get-Command Invoke-WinCareHeadlessCommand -ErrorAction SilentlyContinue)){return New-WinCareBridgeResult -Success $false -Code 'HeadlessHostUnavailable' -Message 'Headless command host is unavailable.' -ExitCode 127};return Invoke-WinCareHeadlessCommand -Command $Command -Parameters $Parameters};if(Get-Command Start-WinCare -ErrorAction SilentlyContinue){return Start-WinCare};New-WinCareBridgeResult -Success $false -Code 'WinCareHostUnavailable' -Message 'No WinCare host is available.' -ExitCode 127
}
