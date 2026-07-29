#requires -Version 7.2

# Late-loaded hardening implementations keep the public command surface stable while
# enforcing the stricter security contracts introduced by the audit remediations.

$isolatedStagingImplementation = {
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

Set-Item -Path 'Function:\New-WinCareIsolatedStagingDirectory' -Value $isolatedStagingImplementation -Force
Remove-Variable isolatedStagingImplementation

$configVaultAuditImplementation = {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ConfigVaultPath
    )

    $safe = Assert-WinCareSafePath -LiteralPath $ConfigVaultPath -AllowMissing
    $tpmEnforced = $false
    try {
        $tpm = Get-CimInstance Win32_Tpm -ErrorAction SilentlyContinue
        if (-not $tpm -and $IsWindows) {
            $tpm = Get-CimInstance -Namespace 'root\cimv2\Security\MicrosoftTpm' -ClassName Win32_Tpm -ErrorAction SilentlyContinue
        }
        if ($tpm) {
            if ($null -ne $tpm.IsEnabled_InitialValue) {
                $tpmEnforced = [bool]$tpm.IsEnabled_InitialValue
            } elseif ($tpm.PSObject.Properties['IsEnabled']) {
                $tpmEnforced = [bool]$tpm.IsEnabled
            } else {
                $tpmEnforced = $true
            }
        }
    } catch {
        $tpmEnforced = $false
    }

    $vaultEncrypted = $false
    if (Test-Path -LiteralPath $safe -PathType Leaf) {
        $aclValid = -not $IsWindows
        if ($IsWindows) {
            try {
                $acl = Get-Acl -LiteralPath $safe -ErrorAction Stop
                if ($null -eq $acl) {
                    throw 'The vault ACL could not be read.'
                }

                $allowedSids = [System.Collections.Generic.HashSet[string]]::new(
                    [System.StringComparer]::OrdinalIgnoreCase
                )
                $null = $allowedSids.Add('S-1-5-18')
                $null = $allowedSids.Add('S-1-5-32-544')

                $currentUserSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
                if ($null -ne $currentUserSid) {
                    $null = $allowedSids.Add($currentUserSid.Value)
                }

                $ownerSid = $acl.GetOwner([System.Security.Principal.SecurityIdentifier])
                if ($null -eq $ownerSid) {
                    throw 'The vault owner SID could not be resolved.'
                }
                $null = $allowedSids.Add($ownerSid.Value)

                $fullControl = [System.Security.AccessControl.FileSystemRights]::FullControl
                $allow = [System.Security.AccessControl.AccessControlType]::Allow
                $rules = @($acl.GetAccessRules(
                    $true,
                    $true,
                    [System.Security.Principal.SecurityIdentifier]
                ))
                foreach ($rule in $rules) {
                    if ($rule.AccessControlType -ne $allow) {
                        continue
                    }
                    $hasFullControl = ($rule.FileSystemRights -band $fullControl) -eq $fullControl
                    if ($hasFullControl -and -not $allowedSids.Contains($rule.IdentityReference.Value)) {
                        throw "Vault FullControl is granted to unapproved SID $($rule.IdentityReference.Value)."
                    }
                }
                $aclValid = $true
            } catch {
                $aclValid = $false
            }
        }

        if ($aclValid) {
            try {
                $item = Get-Item -LiteralPath $safe -Force -ErrorAction Stop
                if (($item.Attributes -band [System.IO.FileAttributes]::Encrypted) -ne 0) {
                    $vaultEncrypted = $true
                } elseif ($IsWindows) {
                    $blob = $null
                    $clearText = $null
                    try {
                        $blob = Read-WinCareBoundedFileBytes -LiteralPath $safe -MaximumBytes 1048576
                        $providerHeader = [byte[]](
                            0x01,0x00,0x00,0x00,
                            0xD0,0x8C,0x9D,0xDF,0x01,0x15,0xD1,0x11,
                            0x8C,0x7A,0x00,0xC0,0x4F,0xC2,0x97,0xEB
                        )
                        $headerMatches = $blob.Length -ge $providerHeader.Length
                        for ($index = 0; $headerMatches -and $index -lt $providerHeader.Length; $index++) {
                            if ($blob[$index] -ne $providerHeader[$index]) {
                                $headerMatches = $false
                            }
                        }

                        if ($headerMatches) {
                            foreach ($scope in @(
                                [System.Security.Cryptography.DataProtectionScope]::CurrentUser,
                                [System.Security.Cryptography.DataProtectionScope]::LocalMachine
                            )) {
                                try {
                                    $clearText = [System.Security.Cryptography.ProtectedData]::Unprotect(
                                        $blob,
                                        $null,
                                        $scope
                                    )
                                    if ($null -ne $clearText) {
                                        $vaultEncrypted = $true
                                        break
                                    }
                                } catch {
                                    $vaultEncrypted = $false
                                } finally {
                                    if ($null -ne $clearText -and $clearText.Length -gt 0) {
                                        [System.Array]::Clear($clearText, 0, $clearText.Length)
                                    }
                                    $clearText = $null
                                }
                            }
                        }
                    } finally {
                        if ($null -ne $blob -and $blob.Length -gt 0) {
                            [System.Array]::Clear($blob, 0, $blob.Length)
                        }
                    }
                }
            } catch {
                $vaultEncrypted = $false
            }
        }
    }

    [pscustomobject]@{
        ConfigVaultPath = $safe
        TpmBindingEnforced = [bool]$tpmEnforced
        ZeroTrustVaultEncrypted = [bool]$vaultEncrypted
        AuditedAt = [datetime]::UtcNow.ToString('o')
        EvidenceType = 'ZeroTrustConfigVaultProtection'
    }
}

Set-Item -Path 'Function:\Protect-WinCareConfigVault' -Value $configVaultAuditImplementation -Force
Remove-Variable configVaultAuditImplementation
