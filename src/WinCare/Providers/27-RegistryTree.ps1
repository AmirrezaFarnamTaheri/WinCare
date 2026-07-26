<#
.SYNOPSIS
Exports an observed registry subtree and atomically commits a verified snapshot file.
#>
function Save-WinCareRegistrySnapshot {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^(HKLM|HKCU|HKCR|HKU|HKCC)\\')]
        [string]$KeyPath,
        [Parameter(Mandatory)][string]$BackupPath,
        [ValidateRange(1,268435456)][long]$MaximumSnapshotBytes=67108864
    )

    if(-not $IsWindows -or -not (Get-Command reg.exe -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'RegistryExportUnavailable' -Message 'reg.exe is unavailable.' -ExitCode 78
    }
    try {
        $full=Resolve-WinCareCanonicalPath -LiteralPath $BackupPath -AllowMissing
        $parent=Split-Path -Parent $full
        if([string]::IsNullOrWhiteSpace($parent)) {
            throw 'BackupPath must include a parent directory.'
        }
        $null=New-Item -ItemType Directory -Path $parent -Force
        $null=Assert-WinCareSafePath -LiteralPath $parent
        $null=Assert-WinCareSafePath -LiteralPath $full -AllowMissing
    } catch {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'InvalidRegistryBackupPath' -Message $_.Exception.Message -ExitCode 22
    }
    if(-not $PSCmdlet.ShouldProcess($KeyPath,"Export registry snapshot to $full")) {
        return New-WinCareResult -Success $true -Status Preview `
            -Code 'RegistrySnapshotPreview' -Message 'Registry export preview generated.' `
            -Data @{
                KeyPath=$KeyPath
                BackupPath=$full
                MaximumSnapshotBytes=$MaximumSnapshotBytes
            }
    }
    $temporary=Join-Path $parent ('.wincare-registry-{0}.reg' -f [guid]::NewGuid().ToString('N'))
    try {
        # ponytail: native reg.exe export -> Win32 RegSaveKeyEx API
        $process=Invoke-WinCareProcess -FilePath 'reg.exe' `
            -ArgumentList @('export',$KeyPath,$temporary,'/y') `
            -TimeoutSeconds 300 -MaximumCapturedOutputBytes 262144
        if(-not $process.Success -or
            -not (Test-Path -LiteralPath $temporary -PathType Leaf)) {
            return New-WinCareResult -Success $false -Status Failed `
                -Code 'RegistryExportFailed' -Message $process.Message `
                -ExitCode $process.ExitCode -Data @{Process=$process.Data}
        }
        $item=Get-Item -LiteralPath $temporary -Force -ErrorAction Stop
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Registry export produced a reparse point.'
        }
        if($item.Length -le 0 -or $item.Length -gt $MaximumSnapshotBytes) {
            throw "Registry snapshot length is outside the configured byte bound."
        }
        $bytes=Read-WinCareBoundedFileBytes -LiteralPath $temporary -MaximumBytes $MaximumSnapshotBytes
        try { Write-WinCareAtomicBytes -LiteralPath $full -Bytes $bytes }
        finally { [Array]::Clear($bytes,0,$bytes.Length) }
        $observed=Get-Item -LiteralPath $full -Force -ErrorAction Stop
        $hash=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        New-WinCareResult -Success ($observed.Length -eq $item.Length) `
            -Code 'RegistrySnapshotSaved' `
            -Message 'Registry snapshot was bounded, atomically committed, and hash-verified.' `
            -Data @{
                KeyPath=$KeyPath
                BackupPath=$full
                Length=[long]$observed.Length
                Sha256=$hash
                RegExitCode=$process.ExitCode
                EvidenceType='BoundedRegExitAtomicLengthAndSha256'
            }
    } catch {
        New-WinCareResult -Success $false -Status Failed `
            -Code 'RegistryExportFailed' -Message $_.Exception.Message -ExitCode 1
    } finally {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
if($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function Save-WinCareRegistrySnapshot
}
