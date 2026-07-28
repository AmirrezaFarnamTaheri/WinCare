#requires -Version 7.2

function Assert-WinCareStagingPrefix {
    param([Parameter(Mandatory)][string]$Prefix)
    if ([string]::IsNullOrWhiteSpace($Prefix) -or $Prefix -ne $Prefix.Trim()) {
        throw 'Prefix must be non-empty and contain no surrounding whitespace.'
    }
    if ($Prefix.Length -gt 64 -or [IO.Path]::IsPathRooted($Prefix)) {
        throw 'Prefix must be at most 64 characters and must not be rooted.'
    }
    if ($Prefix -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9_-])?$') {
        throw 'Prefix contains an unsafe character or trailing period.'
    }
}

function Get-WinCareIsolatedStagingPath {
    param([Parameter(Mandatory)][string]$Prefix)
    $parent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $name = '{0}-{1}' -f $Prefix, [guid]::NewGuid().ToString('N')
    $path = [IO.Path]::GetFullPath([IO.Path]::Combine($parent, $name))
    $separator = [IO.Path]::DirectorySeparatorChar
    $parentPrefix = $parent.TrimEnd([char[]]'\/') + $separator
    $comparison = if ($IsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    if (-not $path.StartsWith($parentPrefix, $comparison)) {
        throw 'The staging path escaped the operating-system temporary directory.'
    }
    return $path
}

function Get-WinCareStagingPrincipals {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if ($null -eq $current) {
        throw 'The current Windows security identifier could not be resolved.'
    }
    $system = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::LocalSystemSid,
        $null
    )
    $admins = [Security.Principal.SecurityIdentifier]::new(
        [Security.Principal.WellKnownSidType]::BuiltinAdministratorsSid,
        $null
    )
    return [pscustomobject]@{
        Objects = @($current, $system, $admins)
        Values = @($current.Value, $system.Value, $admins.Value)
    }
}

function New-WinCareStagingAcl {
    param([Parameter(Mandatory)]$Principals)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $rights = [Security.AccessControl.FileSystemRights]::FullControl
    $inherit = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    foreach ($sid in $Principals.Objects) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            $rights,
            $inherit,
            $propagation,
            $allow
        )
        $null = $acl.AddAccessRule($rule)
    }
    return $acl
}

function Assert-WinCareStagingAcl {
    param(
        [Parameter(Mandatory)]$Acl,
        [Parameter(Mandatory)][string[]]$ExpectedSids
    )
    if (-not $Acl.AreAccessRulesProtected) {
        throw 'The staging directory DACL still permits inherited access rules.'
    }
    $sidType = [Security.Principal.SecurityIdentifier]
    $rules = @($Acl.GetAccessRules($true, $false, $sidType))
    $rights = [Security.AccessControl.FileSystemRights]::FullControl
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $unexpected = @($rules | Where-Object {
        $_.IsInherited -or $_.AccessControlType -ne $allow -or
        $_.IdentityReference.Value -notin $ExpectedSids -or
        (($_.FileSystemRights -band $rights) -ne $rights)
    })
    if ($unexpected.Count -gt 0) {
        throw 'The staging directory contains an unexpected explicit access rule.'
    }
    foreach ($sid in $ExpectedSids) {
        $matching = @($rules | Where-Object { $_.IdentityReference.Value -eq $sid })
        if ($matching.Count -eq 0) {
            throw "The staging directory is missing the required rule for SID $sid."
        }
    }
}

function Protect-WinCareWindowsStagingDirectory {
    param([Parameter(Mandatory)][IO.DirectoryInfo]$Directory)
    $principals = Get-WinCareStagingPrincipals
    $acl = New-WinCareStagingAcl -Principals $principals
    $Directory.SetAccessControl($acl)
    $section = [Security.AccessControl.AccessControlSections]::Access
    $verified = $Directory.GetAccessControl($section)
    Assert-WinCareStagingAcl -Acl $verified -ExpectedSids $principals.Values
}

function Protect-WinCareUnixStagingDirectory {
    param([Parameter(Mandatory)][string]$LiteralPath)
    $private = [IO.UnixFileMode]::UserRead -bor
        [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
    [IO.File]::SetUnixFileMode($LiteralPath, $private)
    $mode = [IO.File]::GetUnixFileMode($LiteralPath)
    $nonOwner = [IO.UnixFileMode]::GroupRead -bor
        [IO.UnixFileMode]::GroupWrite -bor [IO.UnixFileMode]::GroupExecute -bor
        [IO.UnixFileMode]::OtherRead -bor [IO.UnixFileMode]::OtherWrite -bor
        [IO.UnixFileMode]::OtherExecute
    if (($mode -band $nonOwner) -ne 0) {
        throw 'The staging directory is accessible to non-owner users.'
    }
}

function New-WinCareIsolatedStagingDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Prefix
    )
    Assert-WinCareStagingPrefix -Prefix $Prefix
    $path = Get-WinCareIsolatedStagingPath -Prefix $Prefix
    $directory = [IO.Directory]::CreateDirectory($path)
    try {
        if ($IsWindows) {
            Protect-WinCareWindowsStagingDirectory -Directory $directory
        } else {
            Protect-WinCareUnixStagingDirectory -LiteralPath $path
        }
        return $path
    } catch {
        try {
            if ([IO.Directory]::Exists($path)) {
                [IO.Directory]::Delete($path, $true)
            }
        } catch {
            # Preserve the original hardening failure.
        }
        throw "Failed to secure isolated staging directory '$path': $($_.Exception.Message)"
    }
}
