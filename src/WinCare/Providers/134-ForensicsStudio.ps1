#requires -Version 7.2

function Get-WinCareForensicTimeline {
    [CmdletBinding()]
    param(
        [datetime]$Since=[datetime]::Now.AddHours(-24),
        [datetime]$Until=[datetime]::Now,
        [ValidateRange(1,20000)][int]$MaximumEvents=5000,
        [ValidateRange(64,4096)][int]$MaximumMessageCharacters=512,
        [ValidateSet('System','Security','PowerShell','Defender','Sysmon')][string[]]$Source=@(
            'System','Security','PowerShell','Defender','Sysmon'
        )
    )

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Supported=$false
            Events=@()
            Errors=@('Windows Event Log is required.')
            EvidenceType='PlatformRequirement'
        }
    }
    if($Until -lt $Since) { throw 'Until must be greater than or equal to Since.' }

    $catalog=@{
        System=@{LogName='System';Ids=@(41,55,6005,6006,6008,7031,7045)}
        Security=@{LogName='Security';Ids=@(1102,4624,4625,4688,4697,4720,4728,4732)}
        PowerShell=@{LogName='Microsoft-Windows-PowerShell/Operational';Ids=@(4103,4104,4105,4106)}
        Defender=@{LogName='Microsoft-Windows-Windows Defender/Operational';Ids=@(1006,1007,1116,1117,5001,5007)}
        Sysmon=@{LogName='Microsoft-Windows-Sysmon/Operational';Ids=@(1,3,7,8,10,11,12,13,22,25)}
    }
    $events=[Collections.Generic.List[object]]::new()
    $errors=[Collections.Generic.List[string]]::new()
    # ponytail: Get-WinEvent lazy filter pipeline -> native System.Diagnostics.Eventing.Reader.EventLogReader
    $remaining=$MaximumEvents

    foreach($name in @($Source|Select-Object -Unique)) {
        if($remaining -le 0) { break }
        $definition=$catalog[$name]
        try {
            $query=@{
                LogName=$definition.LogName
                Id=$definition.Ids
                StartTime=$Since
                EndTime=$Until
            }
            foreach($event in @(Get-WinEvent -FilterHashtable $query -MaxEvents $remaining -ErrorAction Stop)) {
                $message=ConvertTo-WinCareRedactedScalar `
                    -Value ([string]$event.Message) `
                    -MaximumStringCharacters $MaximumMessageCharacters
                $events.Add([pscustomobject]@{
                    TimeCreated=$event.TimeCreated
                    Source=$name
                    LogName=[string]$event.LogName
                    ProviderName=[string]$event.ProviderName
                    EventId=[int]$event.Id
                    Level=[string]$event.LevelDisplayName
                    RecordId=[long]$event.RecordId
                    MachineName=[string]$event.MachineName
                    MessagePreview=$message
                    EvidenceType='BoundedWindowsEventLogObservation'
                })
                $remaining--
                if($remaining -le 0) { break }
            }
        } catch {
            $errors.Add("${name}: $($_.Exception.Message)")
        }
    }

    $ordered=@($events|Sort-Object TimeCreated,LogName,RecordId)
    [pscustomobject]@{
        Supported=$true
        Since=$Since.ToUniversalTime().ToString('o')
        Until=$Until.ToUniversalTime().ToString('o')
        Events=$ordered
        EventCount=$ordered.Count
        Truncated=$remaining -eq 0
        Errors=@($errors)
        EvidenceType='BoundedMultiLogForensicTimeline'
    }
}

function Get-WinCareMemoryAnomalySummary {
    [CmdletBinding()]
    param(
        [datetime]$Since=[datetime]::Now.AddHours(-24),
        [ValidateRange(1,5000)][int]$MaximumModules=1000,
        [ValidateRange(1,5000)][int]$MaximumRemoteThreadEvents=500
    )

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Supported=$false
            Findings=@()
            EvidenceType='PlatformRequirement'
        }
    }

    $findings=[Collections.Generic.List[object]]::new()
    $surfaces=@(Get-WinCareInjectionSurface -Limit 1000)
    foreach($surface in $surfaces) {
        $findings.Add([pscustomobject]@{
            Severity='High'
            Kind='PersistenceInjectionSurface'
            ProcessId=$null
            Path=[string]$surface.Path
            Detail="Observed $($surface.Kind) value $($surface.Name)."
            Evidence=$surface
        })
    }

    $remoteThreads=@(Get-WinCareRemoteThreadEvent -Since $Since -MaxEvents $MaximumRemoteThreadEvents)
    foreach($event in $remoteThreads) {
        $findings.Add([pscustomobject]@{
            Severity='High'
            Kind='RemoteThreadEvent'
            ProcessId=$event.TargetProcessId
            Path=$event.TargetImage
            Detail="Remote thread from $($event.SourceImage) to $($event.TargetImage)."
            Evidence=$event
        })
    }

    $modules=@(Get-WinCareProcessModuleInventory -Limit $MaximumModules)
    foreach($module in $modules) {
        if(-not $module.Accessible) {
            $findings.Add([pscustomobject]@{
                Severity='Informational'
                Kind='ProcessAccessFailure'
                ProcessId=$module.ProcessId
                Path=$null
                Detail=$module.Error
                Evidence=$module
            })
            continue
        }
        $path=[string]$module.FileName
        if([string]::IsNullOrWhiteSpace($path)) { continue }
        $writeablePath=$path -match '(?i)\\Users\\Public\\|\\AppData\\Local\\Temp\\|\\Windows\\Temp\\'
        $signature=$null
        try { $signature=Get-AuthenticodeSignature -LiteralPath $path -ErrorAction Stop } catch { }
        $signatureState=if($signature){[string]$signature.Status}else{'Unknown'}
        if($writeablePath -or $signatureState -notin @('Valid','NotSigned')) {
            $findings.Add([pscustomobject]@{
                Severity=if($writeablePath -and $signatureState -ne 'Valid'){'High'}else{'Medium'}
                Kind='ModuleTrustAnomaly'
                ProcessId=$module.ProcessId
                Path=$path
                Detail="Module path writable=$writeablePath; signature=$signatureState."
                Evidence=[pscustomobject]@{
                    Module=$module
                    SignatureStatus=$signatureState
                    Signer=if($signature -and $signature.SignerCertificate){$signature.SignerCertificate.Subject}else{$null}
                }
            })
        }
    }

    [pscustomobject]@{
        Supported=$true
        CapturedAt=[datetime]::UtcNow.ToString('o')
        InjectionSurfaceCount=$surfaces.Count
        RemoteThreadEventCount=$remoteThreads.Count
        InspectedModuleCount=$modules.Count
        Findings=@($findings)
        HighCount=@($findings|Where-Object Severity -eq 'High').Count
        MediumCount=@($findings|Where-Object Severity -eq 'Medium').Count
        MutationPerformed=$false
        EvidenceType='RegistryEventAndModuleTrustCorrelation'
    }
}

function New-WinCareWindowsSandboxConfigurationPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{2,63}$')][string]$Id,
        [Parameter(Mandatory)][string]$MappedFolder,
        [switch]$EnableNetworking,
        [switch]$EnableClipboard,
        [ValidateRange(1024,8192)][int]$MemoryInMB=4096
    )

    $folder=Assert-WinCareSafePath -LiteralPath $MappedFolder
    $folderItem=Get-Item -LiteralPath $folder -Force -ErrorAction Stop
    if(-not $folderItem.PSIsContainer -or ($folderItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The sandbox mapped folder must be a regular non-reparse directory.'
    }

    $dataRoot=[IO.Path]::GetFullPath((Get-WinCareDataRoot))
    $destination=Join-Path $dataRoot ("Forensics\Sandboxes\{0}.wsb" -f $Id)
    $settings=[ordered]@{
        vGPU='Disable'
        Networking=if($EnableNetworking){'Enable'}else{'Disable'}
        ClipboardRedirection=if($EnableClipboard){'Enable'}else{'Disable'}
        PrinterRedirection='Disable'
        AudioInput='Disable'
        VideoInput='Disable'
        ProtectedClient='Enable'
        MemoryInMB=$MemoryInMB
        HostFolder=$folder
        SandboxFolder='C:\WinCareInput'
        ReadOnly='true'
    }
    $escapedHost=[Security.SecurityElement]::Escape($folder)
    $xml=@"
<Configuration>
  <VGpu>$($settings.vGPU)</VGpu>
  <Networking>$($settings.Networking)</Networking>
  <MappedFolders>
    <MappedFolder>
      <HostFolder>$escapedHost</HostFolder>
      <SandboxFolder>$($settings.SandboxFolder)</SandboxFolder>
      <ReadOnly>$($settings.ReadOnly)</ReadOnly>
    </MappedFolder>
  </MappedFolders>
  <ClipboardRedirection>$($settings.ClipboardRedirection)</ClipboardRedirection>
  <PrinterRedirection>$($settings.PrinterRedirection)</PrinterRedirection>
  <AudioInput>$($settings.AudioInput)</AudioInput>
  <VideoInput>$($settings.VideoInput)</VideoInput>
  <ProtectedClient>$($settings.ProtectedClient)</ProtectedClient>
  <MemoryInMB>$MemoryInMB</MemoryInMB>
</Configuration>
"@
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($xml)
    try {
        $action=New-WinCareBridgeAction -Type 'WriteManagedFile' `
            -Label "Create Windows Sandbox configuration $Id" `
            -Risk Moderate `
            -Parameters @{
                Path=$destination
                ContentBase64=[Convert]::ToBase64String($bytes)
                ExpectedBeforeHash=if(Test-Path -LiteralPath $destination -PathType Leaf){
                    Get-WinCareSha256 -LiteralPath $destination -MaximumBytes 1048576
                }else{''}
                AllowedRoots=@($dataRoot)
            } `
            -Reversible $true `
            -Verification 'The WSB configuration must be atomically persisted with the exact planned hash.'
    } finally {
        [Array]::Clear($bytes,0,$bytes.Length)
    }

    New-WinCareBridgePlan `
        -Title "Create isolated Windows Sandbox configuration: $Id" `
        -Description 'Creates a no-auto-launch Windows Sandbox configuration. Networking and clipboard remain disabled unless explicitly requested.' `
        -Actions @($action) `
        -Metadata @{
            Destination=$destination
            Settings=$settings
            LaunchPerformed=$false
            EvidenceType='DeterministicWsbConfigurationPlan'
        }
}
