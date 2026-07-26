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
    if (Test-Path -LiteralPath $safe) {
        $aclValid = $true
        if ($IsWindows) {
            try {
                $acl = Get-Acl -LiteralPath $safe -ErrorAction Stop
                foreach ($rule in $acl.Access) {
                    if ($rule.AccessControlType -ne [System.Security.AccessControl.AccessControlType]::Allow) {
                        continue
                    }
                    $hasFullControl = ($rule.FileSystemRights -band [System.Security.AccessControl.FileSystemRights]::FullControl) -eq
                        [System.Security.AccessControl.FileSystemRights]::FullControl
                    if (-not $hasFullControl) {
                        continue
                    }
                    $identity = [string]$rule.IdentityReference.Value
                    $isPrivileged = ($identity -match '(?i)SYSTEM|Administrators|S-1-5-18|S-1-5-32-544|S-1-5-500') -or
                        ($acl.Owner -and $identity -eq $acl.Owner)
                    if (-not $isPrivileged) {
                        $aclValid = $false
                        break
                    }
                }
            } catch {
                $aclValid = $false
            }
        }

        if ($aclValid) {
            $item = Get-Item -LiteralPath $safe -Force -ErrorAction SilentlyContinue
            if ($item) {
                if (($item.Attributes -band [System.IO.FileAttributes]::Encrypted) -ne 0) {
                    $vaultEncrypted = $true
                } elseif (-not $item.PSIsContainer) {
                    $buffer = [byte[]]::new(512)
                    $stream = $null
                    try {
                        $stream = [System.IO.FileStream]::new(
                            $safe,
                            [System.IO.FileMode]::Open,
                            [System.IO.FileAccess]::Read,
                            [System.IO.FileShare]::Read,
                            4096,
                            [System.IO.FileOptions]::SequentialScan
                        )
                        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
                        if ($bytesRead -ge 8 -and
                            $buffer[0] -eq 0x01 -and $buffer[1] -eq 0x00 -and
                            $buffer[2] -eq 0x00 -and $buffer[3] -eq 0x00 -and
                            $buffer[4] -eq 0xD0 -and $buffer[5] -eq 0x8C -and
                            $buffer[6] -eq 0x9D -and $buffer[7] -eq 0xDF) {
                            $vaultEncrypted = $true
                        }
                        if (-not $vaultEncrypted -and $bytesRead -gt 0) {
                            $headerText = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
                            if ($headerText -match '(?i)DPAPI|ENCRYPTED|ZeroTrustVault|AQAAANCMnd8') {
                                $vaultEncrypted = $true
                            }
                        }
                    } catch {
                        $vaultEncrypted = $false
                    } finally {
                        if ($stream) {
                            $stream.Dispose()
                        }
                        [System.Array]::Clear($buffer, 0, $buffer.Length)
                    }
                }
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
