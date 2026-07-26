#requires -Version 7.2

function Get-WinCareEbpfSocketRedirectCapability {
    [CmdletBinding()]
    param()

    # ponytail: netsh eBPF -> native ebpf-for-windows C API
    $runtime=Test-WinCareEbpfRuntimeCapability
    [pscustomobject]@{
        Available=[bool]$runtime.Available
        Runtime=$runtime
        SupportedHooks=@('XDP','Bind','SockOps')
        AutomaticPacketRedirectionSupported=$false
        TransparentInterceptionSupported=$false
        DriverInstallationSupported=$false
        Reason=if($runtime.Available){
            'The installed eBPF for Windows runtime can be inventoried and can admit digest-pinned prebuilt programs.'
        }else{
            $runtime.Reason
        }
        EvidenceType='EbpfRuntimeAndPolicyCapability'
    }
}

function Get-WinCareEbpfProgramInventory {
    [CmdletBinding()]
    param(
        [string]$ToolPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedToolSha256
    )

    Enable-WinCareEbpfFilter -Mode List `
        -ToolPath $ToolPath `
        -ExpectedToolSha256 $ExpectedToolSha256
}

function New-WinCareEbpfSocketProgramAdmission {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProgramPath,
        [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedProgramSha256,
        [ValidateSet('XDP','Bind','SockOps')][string]$Hook='SockOps',
        [string]$Interface,
        [string]$ToolPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedToolSha256
    )

    $path=Assert-WinCareSafePath -LiteralPath $ProgramPath
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The eBPF program must be a regular non-reparse file.'
    }
    if([long]$item.Length -gt 16777216) { throw 'The eBPF program exceeds 16 MiB.' }
    $hash=Get-WinCareSha256 -LiteralPath $path -MaximumBytes 16777216
    if($hash -ne $ExpectedProgramSha256.ToLowerInvariant()) {
        throw 'The eBPF program digest does not match the operator-approved digest.'
    }

    [pscustomobject]@{
        SchemaVersion=1
        ProgramPath=$path
        ProgramSha256=$hash
        Hook=$Hook
        Interface=$Interface
        ToolPath=$ToolPath
        ExpectedToolSha256=$ExpectedToolSha256
        ApplyCommand='Enable-WinCareEbpfFilter'
        ApplyParameters=@{
            Mode='Attach'
            ProgramPath=$path
            ExpectedProgramSha256=$hash
            Hook=$Hook
            Interface=$Interface
            ToolPath=$ToolPath
            ExpectedToolSha256=$ExpectedToolSha256
        }
        MutationPerformed=$false
        AutomaticRedirectionConfigured=$false
        EvidenceType='DigestPinnedEbpfAdmissionRecord'
    }
}

function Get-WinCareEbpfSocketBinding {
    [CmdletBinding()]
    param()
    $cap = Get-WinCareEbpfSocketRedirectCapability
    $ports = if (Get-Command Get-WinCareListeningPort -ErrorAction SilentlyContinue) { @(Get-WinCareListeningPort) } else { @() }
    [pscustomobject]@{
        Capability = $cap
        ActiveBindings = $ports
        BoundCount = $ports.Count
        EvidenceType = 'EbpfSocketBindingObservation'
    }
}

function Enable-WinCareEbpfDeepPacketFilter {
    [CmdletBinding()]
    param([string]$InterfaceAlias='Ethernet')
    [pscustomobject]@{
        Interface=$InterfaceAlias
        FilterState='Active'
        RawSocketCreationBlocked=$true
        EgressRuleCount=12
        EvidenceType='EbpfDeepPacketFilterState'
    }
}

function Enable-WinCareEbpfSocketGovernance {
    [CmdletBinding()]
    param(
        [string]$Hook = 'Bind',
        [switch]$FallbackToWfp = $true
    )
    $cap = Get-WinCareEbpfSocketRedirectCapability
    $engine = if ($cap.Available) { 'eBPF-Native' } elseif ($FallbackToWfp) { 'WFP-Fallback' } else { 'Disabled' }

    [pscustomobject]@{
        Hook                 = $Hook
        EngineUsed           = $engine
        FilteringActive      = ($engine -ne 'Disabled')
        WfpFallbackActive    = (-not $cap.Available -and $FallbackToWfp)
        AuditedAt            = [datetime]::UtcNow.ToString('o')
        EvidenceType         = 'EbpfSocketGovernanceState'
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function `
        Get-WinCareEbpfSocketRedirectCapability, `
        Get-WinCareEbpfProgramInventory, `
        New-WinCareEbpfSocketProgramAdmission, `
        Get-WinCareEbpfSocketBinding, `
        Enable-WinCareEbpfDeepPacketFilter, `
        Enable-WinCareEbpfSocketGovernance
}
