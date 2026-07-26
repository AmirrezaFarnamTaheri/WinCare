<#
.SYNOPSIS
Audits saved wireless profile authentication and cipher configuration without requesting cleartext keys.
#>
function Get-WinCareWirelessBaselineAudit {
    [CmdletBinding()]param()
    if(-not $IsWindows -or -not(Get-Command netsh.exe -ErrorAction SilentlyContinue)){return New-WinCareResult -Success $false -Status Blocked -Code 'WirelessAuditUnavailable' -Message 'netsh wlan is unavailable.' -ExitCode 78}
    try{
        $profilesOutput=@(& netsh.exe wlan show profiles 2>&1);if($LASTEXITCODE -ne 0){throw ($profilesOutput -join "`n")}
        $profiles=@($profilesOutput|ForEach-Object{if([string]$_ -match '^\s*All User Profile\s*:\s*(.+)$'){$Matches[1].Trim()}}|Where-Object{$_})
        $results=foreach($profile in $profiles){
            $output=@(& netsh.exe wlan show profile "name=$profile" 2>&1);$exit=$LASTEXITCODE
            $auth=@($output|ForEach-Object{if([string]$_ -match '^\s*Authentication\s*:\s*(.+)$'){$Matches[1].Trim()}}|Where-Object{$_})|Select-Object -First 1
            $cipher=@($output|ForEach-Object{if([string]$_ -match '^\s*Cipher\s*:\s*(.+)$'){$Matches[1].Trim()}}|Where-Object{$_})|Select-Object -First 1
            $rating=if($exit -ne 0){'Unknown'}elseif($auth -match '(?i)WPA3|WPA2-Enterprise'){ 'Strong' }elseif($auth -match '(?i)WPA2-Personal'){ 'Acceptable' }elseif($auth -match '(?i)WEP|Open'){ 'Weak' }else{'Unknown'}
            [pscustomobject]@{ProfileName=$profile;Authentication=$auth;Cipher=$cipher;SecurityRating=$rating;NetshExitCode=$exit;CleartextKeyRequested=$false;EvidenceType='NetshProfileMetadata'}
        }
        New-WinCareResult -Success $true -Code 'WirelessProfilesAudited' -Message 'Wireless profile security metadata was observed without exposing stored keys.' -Data @{Profiles=@($results);Count=@($results).Count;EvidenceType='NetshProfileMetadata'}
    }catch{New-WinCareResult -Success $false -Status Failed -Code 'WirelessAuditFailed' -Message $_.Exception.Message -ExitCode 1}
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareWirelessBaselineAudit }
