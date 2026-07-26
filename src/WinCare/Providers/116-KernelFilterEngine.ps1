#requires -Version 7.2

function Resolve-WinCareExecutablePath {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ProcessName)

    if(Test-Path -LiteralPath $ProcessName -PathType Leaf) {
        return (Resolve-Path -LiteralPath $ProcessName).Path
    }
    $name=[IO.Path]::GetFileNameWithoutExtension($ProcessName)
    $process=Get-Process -Name $name -ErrorAction SilentlyContinue|Select-Object -First 1
    if($process -and $process.Path) { return $process.Path }
    $command=Get-Command $ProcessName -ErrorAction SilentlyContinue|Select-Object -First 1
    if($command -and $command.Source -and
        (Test-Path -LiteralPath $command.Source -PathType Leaf)) {
        return $command.Source
    }
    $null
}

function Enable-WinCareEbpfFilter {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [ValidateSet('Attach','Detach','List')][string]$Mode='List',
        [string]$ProgramPath,
        [ValidateSet('XDP','Bind','SockOps')][string]$Hook='XDP',
        [string]$Interface,
        [string]$ToolPath,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedToolSha256,
        [ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$ExpectedProgramSha256
    )

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' -Message 'eBPF for Windows requires Windows.' `
            -ExitCode 78
    }
    $command=if($ToolPath) {
        Get-Command $ToolPath -CommandType Application -ErrorAction SilentlyContinue|
            Select-Object -First 1
    } else {
        Get-Command 'netsh.exe' -CommandType Application -ErrorAction SilentlyContinue|
            Select-Object -First 1
    }
    if(-not $command) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'EbpfRuntimeMissing' `
            -Message 'No supported eBPF for Windows command-line runtime was found.' `
            -ExitCode 78 -Data @{
                Required='eBPF for Windows netsh helper'
                EvidenceType='DependencyProbe'
            }
    }
    try {
        $tool=Assert-WinCareSafePath -LiteralPath $command.Source
        $toolHash=(Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash.ToLowerInvariant()
        if($ToolPath -and -not $ExpectedToolSha256) {
            throw 'ExpectedToolSha256 is required for a custom eBPF runtime path.'
        }
        if($ExpectedToolSha256 -and
            $toolHash -ne $ExpectedToolSha256.ToLowerInvariant()) {
            throw 'The eBPF runtime digest does not match the operator-approved digest.'
        }
        if([IO.Path]::GetFileName($tool) -ine 'netsh.exe') {
            throw 'Only the eBPF for Windows netsh command contract is supported.'
        }
        $arguments=[Collections.Generic.List[string]]::new()
        $arguments.Add('ebpf')
        $program=$null
        $programHash=$null
        switch($Mode) {
            'List' {
                $arguments.Add('show')
                $arguments.Add('programs')
            }
            'Attach' {
                if(-not $ProgramPath -or -not $ExpectedProgramSha256) {
                    throw 'Attach requires ProgramPath and ExpectedProgramSha256.'
                }
                $program=Assert-WinCareSafePath -LiteralPath $ProgramPath
                if((Get-Item -LiteralPath $program -Force).Length -gt 16777216) {
                    throw 'The eBPF program exceeds the 16 MiB bound.'
                }
                $programHash=(Get-FileHash -LiteralPath $program -Algorithm SHA256).Hash.ToLowerInvariant()
                if($programHash -ne $ExpectedProgramSha256.ToLowerInvariant()) {
                    throw 'The eBPF program digest does not match the approved digest.'
                }
                $arguments.Add('add')
                $arguments.Add('program')
                $arguments.Add($program)
            }
            'Detach' {
                if(-not $ProgramPath) {
                    throw 'Detach requires the exact program identifier accepted by the runtime.'
                }
                if($ProgramPath.Length -gt 260 -or $ProgramPath -match '[\r\n]') {
                    throw 'The eBPF program identifier is invalid.'
                }
                $arguments.Add('delete')
                $arguments.Add('program')
                $arguments.Add($ProgramPath)
            }
        }
    } catch {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'EbpfAdmissionFailed' -Message $_.Exception.Message -ExitCode 78
    }
    if($Mode -ne 'List' -and
        -not $PSCmdlet.ShouldProcess("eBPF runtime $tool","$Mode program")) {
        return New-WinCareResult -Success $false -Status Cancelled `
            -Code 'Cancelled' -Message 'The eBPF operation was cancelled.' -ExitCode 125
    }
    if($program -and (Get-WinCareSha256 -LiteralPath $program) -ne $programHash){throw 'The eBPF program changed after admission.'}
    $result=Invoke-WinCareProcess -FilePath $tool -ArgumentList @($arguments) `
        -TimeoutSeconds 120 -MaximumCapturedOutputBytes 1048576 `
        -ExpectedSha256 $toolHash
    $result.Code=if($result.Success) {
        'EbpfRuntimeOperationCompleted'
    } else {
        'EbpfRuntimeOperationFailed'
    }
    if($result.Success) {
        $result.Message='The eBPF for Windows runtime accepted the requested operation.'
    }
    $result.Data.Mode=$Mode
    $result.Data.RequestedHook=$Hook
    $result.Data.RequestedInterface=$Interface
    $result.Data.HookAndInterfaceAppliedByThisContract=$false
    $result.Data.ProgramPath=$program
    $result.Data.ProgramSha256=$programHash
    $result.Data.ToolSha256=$toolHash
    $result.Data.EvidenceType='BoundedDigestPinnedEbpfRuntimeOutput'
    $result
}

function Set-WinCareMicrosegmentationRule {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='High')]
    param(
        [Parameter(Mandatory)][string]$ProcessName,
        [ValidateSet('Block','Allow','Remove')][string]$Action='Block',
        [ValidateSet('Inbound','Outbound')][string]$Direction='Outbound',
        [ValidateSet('Any','TCP','UDP')][string]$Protocol='Any',
        [string]$RemoteAddress='Any',
        [string]$RemotePort='Any',
        [string]$RuleName
    )

    if(-not $IsWindows) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'WindowsRequired' `
            -Message 'Windows Defender Firewall rules require Windows.' -ExitCode 78
    }
    if(-not (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'FirewallModuleMissing' -Message 'The NetSecurity module is unavailable.' `
            -ExitCode 78
    }
    $path=Resolve-WinCareExecutablePath -ProcessName $ProcessName
    if(-not $path) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'ExecutableNotResolved' `
            -Message "The executable for '$ProcessName' could not be resolved." -ExitCode 2
    }
    $hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if(-not $RuleName) {
        $name=[IO.Path]::GetFileNameWithoutExtension($path)
        $RuleName="WinCare-Microsegment-$name-$Direction-$($hash.Substring(0,12))"
    }
    if(-not $PSCmdlet.ShouldProcess($RuleName,"$Action Windows Defender Firewall rule")) {
        return New-WinCareResult -Success $false -Status Cancelled `
            -Code 'Cancelled' -Message 'The firewall operation was cancelled.' -ExitCode 125
    }
    try {
        $existing=Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
        if($Action -eq 'Remove') {
            if($existing) { $existing|Remove-NetFirewallRule -ErrorAction Stop }
            $remaining=Get-NetFirewallRule -DisplayName $RuleName -ErrorAction SilentlyContinue
            $success=-not $remaining
            return New-WinCareResult -Success $success `
                -Code $(if($success){'MicrosegmentationRuleRemoved'}else{'MicrosegmentationRemovalFailed'}) `
                -Message $(if($success){'The process-scoped firewall rule was removed and absence was verified.'}else{'The firewall rule remains present.'}) `
                -ExitCode $(if($success){0}else{74}) -Data @{
                    RuleName=$RuleName
                    Program=$path
                    ProgramSha256=$hash
                    EvidenceType='ObservedFirewallRuleAbsence'
                }
        }
        if($existing) { $existing|Remove-NetFirewallRule -ErrorAction Stop }
        $parameters=@{
            DisplayName=$RuleName
            Program=$path
            Direction=$Direction
            Action=$Action
            Enabled='True'
            Profile='Any'
            RemoteAddress=$RemoteAddress
            Protocol=$Protocol
            ErrorAction='Stop'
        }
        if($RemotePort -ne 'Any') { $parameters.RemotePort=$RemotePort }
        $null=New-NetFirewallRule @parameters
        $observed=Get-NetFirewallRule -DisplayName $RuleName -ErrorAction Stop|
            Get-NetFirewallApplicationFilter
        $success=@($observed|Where-Object Program -eq $path).Count -gt 0
        New-WinCareResult -Success $success `
            -Code $(if($success){'MicrosegmentationRuleApplied'}else{'MicrosegmentationVerificationFailed'}) `
            -Message $(if($success){'A process-scoped firewall rule was applied and verified.'}else{'The firewall rule could not be verified.'}) `
            -ExitCode $(if($success){0}else{74}) -Data @{
                RuleName=$RuleName
                Program=$path
                ProgramSha256=$hash
                Action=$Action
                Direction=$Direction
                Protocol=$Protocol
                RemoteAddress=$RemoteAddress
                RemotePort=$RemotePort
                EvidenceType='ObservedFirewallRule'
            }
    } catch {
        New-WinCareResult -Success $false -Code 'MicrosegmentationRuleFailed' `
            -Message $_.Exception.Message -ExitCode 1
    }
}
