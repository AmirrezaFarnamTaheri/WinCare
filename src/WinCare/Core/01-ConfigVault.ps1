#requires -Version 7.2

function Protect-WinCareConfigVault {
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
