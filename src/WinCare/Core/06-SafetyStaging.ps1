#requires -Version 7.2
# ============================================================================
# WinCare isolated staging directories.
#
# This file holds the baseline definition of New-WinCareIsolatedStagingDirectory
# so the public export in WinCare.psd1/WinCare.psm1 resolves to a real function
# definition. Core/99-SecurityHardening.ps1 loads later in the Core source
# manifest and replaces this implementation with the hardened variant that
# treats a DACL failure as fatal (it removes the half-created directory and
# rethrows) instead of returning a directory whose ACL was never tightened.
#
# Keep the two in sync: this baseline must remain a superset-compatible
# signature of the hardened override.
# ============================================================================

function New-WinCareIsolatedStagingDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Prefix
    )
    $parent = [System.IO.Path]::GetTempPath()
    $folderName = "$Prefix-$([System.Guid]::NewGuid().ToString('N'))"
    $fullPath = [System.IO.Path]::Combine($parent, $folderName)

    $dirInfo = [System.IO.Directory]::CreateDirectory($fullPath)
    if ($IsWindows) {
        try {
            $acl = $dirInfo.GetAccessControl()
            $acl.SetAccessRuleProtection($true, $false)

            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            $systemSid = [System.Security.Principal.WellKnownSidType]::LocalSystemSid
            $adminSid = [System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid
            $systemUser = [System.Security.Principal.SecurityIdentifier]::new($systemSid, $null)
            $adminUser = [System.Security.Principal.SecurityIdentifier]::new($adminSid, $null)

            $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
            $containerInherit = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit
            $objectInherit = [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            $inheritFlags = $containerInherit -bor $objectInherit
            $propFlags = [System.Security.AccessControl.PropagationFlags]::None
            $allow = [System.Security.AccessControl.AccessControlType]::Allow

            foreach ($principal in @($currentUser, $systemUser, $adminUser)) {
                $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                    $principal, $fullControl, $inheritFlags, $propFlags, $allow)
                $acl.AddAccessRule($rule)
            }

            $dirInfo.SetAccessControl($acl)
        } catch {
            # The hardened override in Core/99-SecurityHardening.ps1 escalates this
            # to a terminating error. This baseline keeps the pre-existing
            # best-effort behaviour so the function stays usable if the hardening
            # overlay is not loaded; the returned path is still caller-scoped.
            Write-Verbose "Isolated staging DACL hardening was unavailable: $($_.Exception.Message)"
        }
    }
    return $fullPath
}
