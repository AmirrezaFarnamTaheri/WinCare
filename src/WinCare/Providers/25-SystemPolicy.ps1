<#
.SYNOPSIS
Reads local Group Policy .pol files through the source-built native policy parser and enforces Multi-Tenant RBAC governance.
#>
function Get-WinCarePolicyFileEntries {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$PolFilePath)
    try{$path=(Resolve-Path -LiteralPath $PolFilePath -ErrorAction Stop).Path}catch{return New-WinCareResult -Success $false -Status Failed -Code 'PolicyFileNotFound' -Message $_.Exception.Message -ExitCode 2}
    if(-not ('WinCare.Native.PolicyEngine' -as [type])){return New-WinCareResult -Success $false -Status Blocked -Code 'NativePolicyEngineUnavailable' -Message 'The source-built WinCare.Native.PolicyEngine assembly is not loaded.' -ExitCode 78 -Data @{Path=$path;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
    try{
        $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();$entries=@([WinCare.Native.PolicyEngine]::LoadPolFile($path))
        New-WinCareResult -Success $true -Code 'PolicyFileParsed' -Message 'The policy file was parsed by the native policy engine.' -Data @{Path=$path;Sha256=$hash;Entries=$entries;Count=$entries.Count;EvidenceType='FileSha256AndNativeParserOutput'}
    }catch{New-WinCareResult -Success $false -Status Failed -Code 'PolicyFileParseFailed' -Message $_.Exception.Message -ExitCode 1}
}

function Get-WinCareRbacMatrix {
    [CmdletBinding()]param()
    [pscustomobject]@{
        HelpdeskAdmin = [pscustomobject]@{ AllowedRiskCap = 1; Scope = 'Read, Diagnostics, Low-Risk Cleanup'; RequiresStepUp = $false }
        SecOpsAdmin   = [pscustomobject]@{ AllowedRiskCap = 2; Scope = 'Read, Diagnostics, Cleanup, Policy Audit, Dynamic Playbooks'; RequiresStepUp = $false }
        FleetLead     = [pscustomobject]@{ AllowedRiskCap = 3; Scope = 'Full Enterprise Scoping, High-Risk System Mutations, HVCI Changes'; RequiresStepUp = $true }
        EvidenceType  = 'MultiTenantRbacMatrixDefinition'
    }
}

function Assert-WinCareRolePermission {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$RequestedRole,[Parameter(Mandatory)][string]$ActionContractName,[Parameter(Mandatory)][string]$UserIdentity)
    $validRoles=@('HelpdeskAdmin','SecOpsAdmin','FleetLead');
        if($RequestedRole -notin $validRoles){return New-WinCareResult -Success $false -Status Blocked -Code 'InvalidRole' -Message "Role '$RequestedRole' is not recognized." -ExitCode 78}
    $roleInfo=(Get-WinCareRbacMatrix).$RequestedRole
    $actionRiskLevels=@{
        ClearTemporaryFiles=1
        OptimizeStorage=2
        TrimMemoryWorkingSets=2
        ApplyGroupPolicy=2
        DisableWdac=3
        UnloadKernelDriver=3
        ModifyHvci=3
        ForceSystemReboot=3
    }
    if(-not $actionRiskLevels.ContainsKey($ActionContractName)){
        Write-WinCareLog -Level Audit -Message 'Unknown RBAC action contract denied.' -Data @{
            requestedRole=$RequestedRole;action=$ActionContractName;user=$UserIdentity
        }
        return New-WinCareResult -Success $false -Status Blocked -Code 'UnknownActionContract' `
            -Message "Action contract '$ActionContractName' is not registered for RBAC evaluation." `
            -ExitCode 78 -Data @{RequestedRole=$RequestedRole;ActionContractName=$ActionContractName;
                UserIdentity=$UserIdentity;EvidenceType='UnknownRbacActionAuditRecord'}
    }
    $actionRiskLevel=[int]$actionRiskLevels[$ActionContractName]
    if($actionRiskLevel -gt $roleInfo.AllowedRiskCap){
        $auditEntry=[pscustomobject]@{Timestamp=[datetime]::UtcNow.ToString('o');
            EventType='UnauthorizedRoleActionAttempt';
            RequestedRole=$RequestedRole;
            ActionContractName=$ActionContractName;
            UserIdentity=$UserIdentity;
            ActionRiskLevel=$actionRiskLevel;
            AllowedRiskCap=$roleInfo.AllowedRiskCap;
            Status='BlockedByPolicy';
            EvidenceType='UnauthorizedRoleAttemptAuditRecord'}
        Write-WinCareLog -Level Audit -Message 'Role permission denied.' -Data @{requestedRole=$RequestedRole;
            action=$ActionContractName;
            user=$UserIdentity;
            risk=$actionRiskLevel;
            cap=$roleInfo.AllowedRiskCap}
        return New-WinCareResult -Success $false -Status Blocked -Code 'BlockedByPolicy' `
            -Message "Role '$RequestedRole' is unauthorized for action '$ActionContractName'." `
            -ExitCode 78 -Data $auditEntry
    }
    New-WinCareResult -Success $true -Code 'RolePermissionGranted' -Message "Role '$RequestedRole' authorized for action '$ActionContractName'." -Data @{RequestedRole=$RequestedRole;
        ActionContractName=$ActionContractName;
        UserIdentity=$UserIdentity;
        ActionRiskLevel=$actionRiskLevel;
        EvidenceType='RolePermissionAuthorizationRecord'}
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Get-WinCarePolicyFileEntries, Get-WinCareRbacMatrix, Assert-WinCareRolePermission
}

