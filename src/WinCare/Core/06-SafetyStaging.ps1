#requires -Version 7.2
# ============================================================================
# WinCare isolated staging directories.
#
# The baseline implementation is intentionally fail-closed. It must provide the
# same security contract whether this file is loaded directly or through the
# complete Core source manifest.
# ============================================================================

function New-WinCareIsolatedStagingDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Prefix
    )

    if ($Prefix -ne $Prefix.Trim() -or $Prefix.Length -gt 64) {
        throw 'Prefix must be 1 to 64 characters and must not contain leading or trailing whitespace.'
    }
    if ([System.IO.Path]::IsPathRooted($Prefix)) {
        throw 'Prefix must be a file-name fragment, not a rooted path.'
    }
    if ($Prefix -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9_-])?$') {
        throw 'Prefix may contain only ASCII letters, digits, period, underscore, and hyphen, and may not end with a period.'
    }

    $parent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $folderName = '{0}-{1}' -f $Prefix, [System.Guid]::NewGuid().ToString('N')
    $fullPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($parent, $folderName))
    $parentWithSeparator = $parent.TrimEnd([char[]]'\/') + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    } else {
        [System.StringComparison]::Ordinal
    }

    if (-not $fullPath.StartsWith($parentWithSeparator, $comparison)) {
        throw 'The isolated staging path escaped the operating-system temporary directory.'
    }

    $dirInfo = [System.IO.Directory]::CreateDirectory($fullPath)
    try {
        if ($IsWindows) {
            $currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
            if ($null -eq $currentUser) {
                throw 'The current Windows security identifier could not be resolved.'
            }
            $systemUser = [System.Security.Principal.SecurityIdentifier]::new(
                [System.Security.Principal.WellKnownSidType]::LocalSystemSid,
                $null
            )
            $adminUser = [System.Security.Principal.SecurityIdentifier]::new(
                [System.Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
                $null
            )
            $expectedSids = @($currentUser.Value, $systemUser.Value, $adminUser.Value)

            $acl = [System.Security.AccessControl.DirectorySecurity]::new()
            $acl.SetAccessRuleProtection($true, $false)
            $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
            $inheritFlags = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
                [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
            $propagationFlags = [System.Security.AccessControl.PropagationFlags]::None
            $allow = [System.Security.AccessControl.AccessControlType]::Allow

            foreach ($sid in @($currentUser, $systemUser, $adminUser)) {
                $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
                    $sid,
                    $fullControl,
                    $inheritFlags,
                    $propagationFlags,
                    $allow
                )
                $null = $acl.AddAccessRule($rule)
            }

            $dirInfo.SetAccessControl($acl)
            $verifiedAcl = $dirInfo.GetAccessControl([System.Security.AccessControl.AccessControlSections]::Access)
            if (-not $verifiedAcl.AreAccessRulesProtected) {
                throw 'The staging directory DACL still permits inherited access rules.'
            }

            $rules = @($verifiedAcl.GetAccessRules(
                $true,
                $false,
                [System.Security.Principal.SecurityIdentifier]
            ))
            $unexpectedRules = @($rules | Where-Object {
                $_.IsInherited -or
                $_.AccessControlType -ne $allow -or
                $_.IdentityReference.Value -notin $expectedSids -or
                (($_.FileSystemRights -band $fullControl) -ne $fullControl)
            })
            if ($unexpectedRules.Count -gt 0) {
                throw 'The staging directory contains an unexpected explicit access rule.'
            }
            foreach ($expectedSid in $expectedSids) {
                if (-not ($rules | Where-Object { $_.IdentityReference.Value -eq $expectedSid })) {
                    throw "The staging directory is missing the required access rule for SID $expectedSid."
                }
            }
        } else {
            $privateMode = [System.IO.UnixFileMode]::UserRead -bor
                [System.IO.UnixFileMode]::UserWrite -bor
                [System.IO.UnixFileMode]::UserExecute
            [System.IO.File]::SetUnixFileMode($fullPath, $privateMode)
            $actualMode = [System.IO.File]::GetUnixFileMode($fullPath)
            $nonOwnerBits = [System.IO.UnixFileMode]::GroupRead -bor
                [System.IO.UnixFileMode]::GroupWrite -bor
                [System.IO.UnixFileMode]::GroupExecute -bor
                [System.IO.UnixFileMode]::OtherRead -bor
                [System.IO.UnixFileMode]::OtherWrite -bor
                [System.IO.UnixFileMode]::OtherExecute
            if (($actualMode -band $nonOwnerBits) -ne 0) {
                throw 'The staging directory is accessible to non-owner users.'
            }
        }
    } catch {
        try {
            if ([System.IO.Directory]::Exists($fullPath)) {
                [System.IO.Directory]::Delete($fullPath, $true)
            }
        } catch {
            # Best-effort cleanup; the original hardening failure remains authoritative.
        }
        throw "Failed to secure isolated staging directory '$fullPath': $($_.Exception.Message)"
    }

    return $fullPath
}
