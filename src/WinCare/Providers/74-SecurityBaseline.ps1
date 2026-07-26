<#
.SYNOPSIS
Observed Windows code-integrity, vulnerable-driver-blocklist, SMB, and LDAP-signing baseline.
#>
function Get-WinCareSecurityBaselineState {
    [CmdletBinding()]param()
    if(-not $IsWindows){return [pscustomobject]@{Supported=$false;OverallState='Unsupported';Controls=@();EvidenceType='PlatformCapability';Errors=@()}}
    $errors=[Collections.Generic.List[string]]::new();$controls=[ordered]@{}
    $nativeType='WinCare.Native.SecurityAuditor' -as [type]
    if($nativeType){
        try{$ci=[WinCare.Native.SecurityAuditor]::GetCodeIntegrityState();$controls.HvciEnabled=$ci.HvciEnabled;$controls.DriverSignatureEnforcementActive=$ci.DriverSignatureEnforcementActive;$controls.VulnerableDriverBlocklistEnabled=$ci.VulnerableDriverBlocklistEnabled}catch{$errors.Add($_.Exception.Message);$controls.HvciEnabled=$null;$controls.DriverSignatureEnforcementActive=$null;$controls.VulnerableDriverBlocklistEnabled=$null}
        try{$kerberos=[WinCare.Native.SecurityAuditor]::GetKerberosHardeningState();$controls.SmbSigningRequired=$kerberos.SmbSigningRequired;$controls.LdapSigningRequired=$kerberos.LdapSigningRequired}catch{$errors.Add($_.Exception.Message);$controls.SmbSigningRequired=$null;$controls.LdapSigningRequired=$null}
    }else{
        try{$dg=Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction Stop;$controls.HvciEnabled=@($dg.SecurityServicesRunning)-contains 2;$controls.DriverSignatureEnforcementActive=$null}catch{$errors.Add("Device Guard: $($_.Exception.Message)");$controls.HvciEnabled=$null;$controls.DriverSignatureEnforcementActive=$null}
        try{$block=(Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Config' -Name VulnerableDriverBlocklistEnable -ErrorAction Stop).VulnerableDriverBlocklistEnable;$controls.VulnerableDriverBlocklistEnabled=[int]$block -eq 1}catch{$controls.VulnerableDriverBlocklistEnabled=$null}
        try{$smb=Get-SmbServerConfiguration -ErrorAction Stop;$controls.SmbSigningRequired=[bool]$smb.RequireSecuritySignature}catch{$errors.Add("SMB: $($_.Exception.Message)");$controls.SmbSigningRequired=$null}
        try{$ldap=(Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Services\NTDS\Parameters' -Name LDAPServerIntegrity -ErrorAction Stop).LDAPServerIntegrity;$controls.LdapSigningRequired=[int]$ldap -ge 2}catch{$controls.LdapSigningRequired=$null}
    }
    $values=@($controls.Values);$known=@($values|Where-Object{$null -ne $_});$failed=@($known|Where-Object{-not [bool]$_})
    $overall=if($known.Count -eq 0){'Unknown'}elseif($known.Count -lt $values.Count){if($failed.Count){'NeedsReviewWithUnknowns'}else{'ObservedHardenedWithUnknowns'}}elseif($failed.Count){'NeedsReview'}else{'ObservedHardened'}
    [pscustomobject]@{Supported=$true;OverallState=$overall;KnownControlCount=$known.Count;UnknownControlCount=$values.Count-$known.Count;Controls=[pscustomobject]$controls;EvidenceType=if($nativeType){'SourceBuiltNativeAndWindowsObservation'}else{'WindowsCimRegistryAndCmdletObservation'};Errors=@($errors)}
}
if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Get-WinCareSecurityBaselineState }
