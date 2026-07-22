<#
    .SYNOPSIS
    WinCare v5.2.0 Registry Tree & Policy Modification Provider
    Module ID: 27-RegistryTree

    .DESCRIPTION
    Applies Group Policy registry keys and safety-first backup snapshots.
#>

function Save-WinCareRegistrySnapshot {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory=$true)]
        [string]$KeyPath,

        [Parameter(Mandatory=$true)]
        [string]$BackupPath
    )

    if ($PSCmdlet.ShouldProcess($KeyPath, "Export registry snapshot to $BackupPath")) {
        reg.exe export "$KeyPath" "$BackupPath" /y
        Write-Host "Registry snapshot saved to $BackupPath" -ForegroundColor Green
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Save-WinCareRegistrySnapshot }

