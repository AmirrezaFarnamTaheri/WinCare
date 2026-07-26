#requires -Version 7.2
# ============================================================================
# WinCare compatibility bridge subsystem: Bridge primitives, dependency guards, and connectivity target resolution.
# Mutating handlers must return typed, observable evidence and fail closed.
# ============================================================================

function Get-WinCareBridgeProperty {
    [CmdletBinding()]
    param([AllowNull()][object]$InputObject,[Parameter(Mandatory)][string]$Name,[AllowNull()][object]$Default=$null)
    if($null -eq $InputObject){return $Default}
    if($InputObject -is [Collections.IDictionary]){
        if($InputObject.Contains($Name)){return $InputObject[$Name]}
        return $Default
    }
    $property=$InputObject.PSObject.Properties[$Name]
    if($property){return $property.Value}
    return $Default
}
function ConvertTo-WinCareBridgeHashtable {
    [CmdletBinding()]
    param([AllowNull()][object]$InputObject)
    $result=[ordered]@{}
    if($null -eq $InputObject){return $result}
    if($InputObject -is [Collections.IDictionary]){
        foreach($key in $InputObject.Keys){$result[[string]$key]=$InputObject[$key]}
        return $result
    }
    foreach($property in $InputObject.PSObject.Properties){$result[$property.Name]=$property.Value}
    return $result
}
function New-WinCareBridgeResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][bool]$Success,
        [Parameter(Mandatory)][string]$Message,
        [string]$Code=$(if($Success){'Completed'}else{'Failed'}),
        [ValidateSet('Succeeded','SucceededWithWarnings','Failed','Cancelled','Preview','Blocked')][string]$Status=$(if($Success){'Succeeded'}else{'Failed'}),
        [AllowNull()][object]$Data=$null,
        [int]$ExitCode=$(if($Success){0}else{1}),
        [string[]]$Warnings=@(),
        [bool]$RestartRequired=$false
    )
    if(Get-Command New-WinCareResult -ErrorAction SilentlyContinue){
        return New-WinCareResult -Success $Success -Message $Message -Code $Code -Status $Status -Data $Data -ExitCode $ExitCode -Warnings $Warnings -RestartRequired $RestartRequired
    }
    return [pscustomobject]@{PSTypeName='WinCare.Result';Success=$Success;Status=$Status;Code=$Code;Message=$Message;Data=$Data;ExitCode=$ExitCode;Warnings=@($Warnings);RestartRequired=$RestartRequired;Timestamp=[datetime]::UtcNow}
}
function New-WinCareBridgeAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Label,
        [ValidateSet('ReadOnly','Low','Moderate','High','Critical')][string]$Risk='Low',
        [hashtable]$Parameters=@{},
        [bool]$RequiresAdmin=$false,
        [bool]$Reversible=$false,
        [object[]]$Postconditions=@(),
        [hashtable]$Compensator=@{},
        [int]$TimeoutSeconds=3600,
        [string[]]$Tags=@('Implemented'),
        [string]$Verification='Provider-specific verification is required.'
    )
    if(-not (Get-Command New-WinCareAction -ErrorAction SilentlyContinue)){throw 'WinCare action model is unavailable.'}
    New-WinCareAction -Type $Type -Label $Label -Risk $Risk -Parameters $Parameters -RequiresAdmin $RequiresAdmin -Reversible $Reversible -Postconditions $Postconditions -Compensator $Compensator -TimeoutSeconds $TimeoutSeconds -Tags $Tags -Verification $Verification
}
function New-WinCareBridgePlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Title,[string]$Description='',[object[]]$Actions=@(),[bool]$RollbackOnFailure=$true,[hashtable]$Metadata=@{})
    if(-not (Get-Command New-WinCarePlan -ErrorAction SilentlyContinue)){throw 'WinCare plan model is unavailable.'}
    New-WinCarePlan -Title $Title -Description $Description -Actions @($Actions) -RollbackOnFailure $RollbackOnFailure -Metadata $Metadata
}
function Get-WinCareBridgeStateRoot {
    [CmdletBinding()]param([string]$Child='')
    $root=$null
    if($script:WinCareState -and $script:WinCareState.ContainsKey('Root')) {
        $root=[string]$script:WinCareState.Root
    }
    if([string]::IsNullOrWhiteSpace($root)) {
        $base=if($env:LOCALAPPDATA){$env:LOCALAPPDATA}else{[IO.Path]::GetTempPath()}
        $root=Join-Path $base 'WinCare'
    }
    $root=[IO.Path]::GetFullPath($root)
    $path=if([string]::IsNullOrWhiteSpace($Child)){$root}else{[IO.Path]::GetFullPath((Join-Path $root $Child))}
    if($path -ne $root -and -not (Test-WinCarePathWithin -Child $path -Parent $root)) {
        throw "State path escaped the WinCare data root: $path"
    }
    $readOnly=[bool](
        $script:WinCareState -and
        $script:WinCareState.ContainsKey('ReadOnlyLocked') -and
        [bool]$script:WinCareState.ReadOnlyLocked
    )
    if(-not $readOnly) {
        $directory=New-Item -ItemType Directory -Path $path -Force -ErrorAction Stop
        if(($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "State directories cannot be reparse points: $path"
        }
    } elseif(Test-Path -LiteralPath $path) {
        $directory=Get-Item -LiteralPath $path -Force -ErrorAction Stop
        if(-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Read-only state path is not a regular directory: $path"
        }
    }
    return $path
}
function Read-WinCareBridgeJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[AllowNull()][object]$Default=$null,[ValidateRange(1,67108864)][long]$MaximumBytes=16777216)
    if(-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)){return $Default}
    try{return Read-WinCareBoundedJson -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Depth 80}
    catch{return $Default}
}
function Write-WinCareBridgeJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath,[Parameter(Mandatory)][object]$InputObject)
    $parent=Split-Path -Parent $LiteralPath
    if($parent){$null=New-Item -ItemType Directory -Path $parent -Force}
    if(-not (Get-Command Write-WinCareAtomicJson -ErrorAction SilentlyContinue)){throw 'The WinCare atomic JSON writer is unavailable.'}
    Write-WinCareAtomicJson -LiteralPath $LiteralPath -InputObject $InputObject -Depth 80
}
function Get-WinCareCurrentPowerShellExecutable {
    [CmdletBinding()]param()
    $path=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    if([string]::IsNullOrWhiteSpace($path)){throw 'The current PowerShell executable path is unavailable.'}
    $item=Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'The current PowerShell executable is not a regular non-reparse file.'}
    return $item.FullName
}
function Invoke-WinCareBridgeProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList=@(),
        [ValidateRange(1,86400)][int]$TimeoutSeconds=3600,
        [int[]]$SuccessExitCodes=@(0),
        [switch]$RequireAdmin,
        [ValidateRange(1024,16777216)][int]$MaximumCapturedOutputBytes=4194304
    )
    # ponytail: Invoke-WinCareBridgeProcess delegates to Invoke-WinCareProcess -> C# native Process broker pool
    if(-not (Get-Command Invoke-WinCareProcess -ErrorAction SilentlyContinue)) {
        return New-WinCareBridgeResult `
            -Success $false -Status Blocked -Code 'ProcessBrokerUnavailable' `
            -Message 'The bounded WinCare process broker is unavailable.' -ExitCode 78
    }
    Invoke-WinCareProcess `
        -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -TimeoutSeconds $TimeoutSeconds `
        -SuccessExitCodes $SuccessExitCodes `
        -RequireAdmin:$RequireAdmin `
        -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes
}
function Get-WinCareActionParameters {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Action)
    $parameters=Get-WinCareBridgeProperty $Action 'Parameters' @{}
    return ConvertTo-WinCareBridgeHashtable $parameters
}
function Test-WinCareWindowsRequired {
    [CmdletBinding()]
    param([string]$Capability='This capability')
    if($IsWindows){return $null}
    return New-WinCareBridgeResult -Success $false -Status Blocked -Code 'WindowsRequired' -Message "$Capability is available only on Windows." -ExitCode 5 -Data @{Platform=[Environment]::OSVersion.Platform.ToString()}
}
function Resolve-WinCareNetworkProbeTarget {
    [CmdletBinding()]
    param([string[]]$Targets,[switch]$IncludeLocalInfrastructure)
    $resolved=[Collections.Generic.List[string]]::new()
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $addTarget={
        param([object]$Candidate)
        $value=[string]$Candidate
        if([string]::IsNullOrWhiteSpace($value)){return}
        $value=$value.Trim()
        if($value.Length -gt 253 -or [Uri]::CheckHostName($value) -eq [UriHostNameType]::Unknown){throw "Invalid network probe target: $value"}
        if($seen.Add($value)){$resolved.Add($value)}
    }
    foreach($candidate in @($Targets)){& $addTarget $candidate}
    if($resolved.Count -eq 0){foreach($candidate in @($env:WINCARE_CONNECTIVITY_HOSTS -split '[,; ]+'|Where-Object{$_})){& $addTarget $candidate}}
    if($resolved.Count -eq 0){
        $stateVariable=Get-Variable -Name WinCareState -Scope Script -ErrorAction SilentlyContinue
        if($stateVariable -and $stateVariable.Value -is [Collections.IDictionary] -and $stateVariable.Value.ContainsKey('Config')){
            $config=$stateVariable.Value.Config
            if($config -is [Collections.IDictionary] -and $config.Contains('NetworkExperimentTargets')){foreach($candidate in @($config.NetworkExperimentTargets)){& $addTarget $candidate}}
        }
    }
    if($resolved.Count -eq 0 -and $IncludeLocalInfrastructure -and $IsWindows){
        if(Get-Command Get-DnsClientServerAddress -ErrorAction SilentlyContinue){
            try{foreach($candidate in @(Get-DnsClientServerAddress -ErrorAction Stop|ForEach-Object{$_.ServerAddresses}|Where-Object{$_})){& $addTarget $candidate}}catch{}
        }
        if(Get-Command Get-NetRoute -ErrorAction SilentlyContinue){
            try{foreach($candidate in @(Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop|Sort-Object RouteMetric|ForEach-Object{$_.NextHop}|Where-Object{$_ -and $_ -ne '0.0.0.0'})){& $addTarget $candidate}}catch{}
        }
    }
    if($resolved.Count -gt 32){throw 'At most 32 network probe targets are permitted.'}
    return @($resolved)
}
function Test-WinCareInternetConnection {
    [CmdletBinding()]
    param([ValidateRange(100,10000)][int]$TimeoutMs=1500,[string[]]$Targets)
    $probeTargets=@(Resolve-WinCareNetworkProbeTarget -Targets $Targets -IncludeLocalInfrastructure)
    if($probeTargets.Count -eq 0){return $false}
    foreach($target in $probeTargets){
        try{
            $ping=[Net.NetworkInformation.Ping]::new()
            try{$reply=$ping.Send($target,$TimeoutMs);if($reply.Status -eq [Net.NetworkInformation.IPStatus]::Success){return $true}}
            finally{$ping.Dispose()}
        }catch{}
    }
    return $false
}
function Get-WinCarePendingRestart {
    [CmdletBinding()]
    param()
    if(-not $IsWindows){return $false}
    $paths=@(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
        'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
    )
    if(Test-Path $paths[0]){return $true};if(Test-Path $paths[1]){return $true}
    try{$pending=(Get-ItemProperty -LiteralPath $paths[2] -Name PendingFileRenameOperations -ErrorAction Stop).PendingFileRenameOperations;return @($pending).Count -gt 0}catch{return $false}
}
