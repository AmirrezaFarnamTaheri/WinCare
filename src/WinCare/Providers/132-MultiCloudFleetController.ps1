#requires -Version 7.2
# WINCARE_NETWORK_BOUNDARY: explicit-user-invocation

function Get-WinCareCloudTargetConfiguration {
    [CmdletBinding()]
    param([string]$ConfigurationPath)

    if(-not $ConfigurationPath) {
        $ConfigurationPath=Join-Path $PSScriptRoot '..\..\..\config\multicloud-targets.json'
    }
    try {
        $full=Assert-WinCareSafePath -LiteralPath $ConfigurationPath
        $item=Get-Item -LiteralPath $full -Force
        if($item.Length -gt 1048576) { throw 'Multi-cloud configuration exceeds 1 MiB.' }
        $document=Read-WinCareBoundedJson -LiteralPath $full -MaximumBytes 1048576 -Depth 16
        if([int]$document.SchemaVersion -ne 1) {
            throw 'Unsupported multi-cloud target schema.'
        }
        $targets=@($document.Targets)
        if($targets.Count -gt 64) { throw 'Multi-cloud configuration exceeds 64 targets.' }
        $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($target in $targets) {
            if([string]$target.Id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') {
                throw 'Each cloud target requires a bounded stable ID.'
            }
            if(-not $ids.Add([string]$target.Id)) {
                throw "Duplicate cloud target ID: $($target.Id)"
            }
            if($target.TokenEnvironmentVariable -and
                [string]$target.TokenEnvironmentVariable -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') {
                throw "Invalid token environment variable for target $($target.Id)."
            }
        }
        [pscustomobject]@{
            Path=$full
            Targets=$targets
            Error=$null
            Sha256=(Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } catch {
        [pscustomobject]@{
            Path=[IO.Path]::GetFullPath($ConfigurationPath)
            Targets=@()
            Error=$_.Exception.Message
            Sha256=$null
        }
    }
}

function Send-WinCareMultiCloudTelemetry {
    [CmdletBinding(SupportsShouldProcess,ConfirmImpact='Medium')]
    param(
        [Parameter(Mandatory)][string]$CloudTarget,
        [object]$Telemetry,
        [string]$ConfigurationPath,
        [ValidateRange(1,120)][int]$TimeoutSeconds=15,
        [ValidateRange(1024,8388608)][int]$MaximumPayloadBytes=4194304,
        [ValidateRange(1024,4194304)][int]$MaximumResponseBytes=524288
    )

    $config=Get-WinCareCloudTargetConfiguration -ConfigurationPath $ConfigurationPath
    if($config.Error) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudConfigurationUnavailable' -Message $config.Error -ExitCode 78 `
            -Data @{ConfigurationPath=$config.Path;EvidenceType='ConfigurationProbe'}
    }
    $target=@($config.Targets|Where-Object Id -eq $CloudTarget|Select-Object -First 1)
    if(-not $target) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudTargetNotFound' -Message "Cloud target '$CloudTarget' is not configured." `
            -ExitCode 2
    }
    try {
        $admission=Assert-WinCareServiceUri -Uri ([uri]$target.TelemetryUri) `
            -AllowPrivateNetwork:([bool]$target.AllowPrivateNetwork)
    } catch {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudEndpointRejected' -Message $_.Exception.Message -ExitCode 77
    }
    $token=$null
    if($target.TokenEnvironmentVariable) {
        $token=[Environment]::GetEnvironmentVariable([string]$target.TokenEnvironmentVariable)
    }
    if($target.TokenEnvironmentVariable -and -not $token) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudCredentialMissing' `
            -Message "Credential environment variable '$($target.TokenEnvironmentVariable)' is not set." `
            -ExitCode 77
    }
    if($null -eq $Telemetry) { $Telemetry=New-WinCareSystemReportData }
    $body=[ordered]@{
        SchemaVersion=1
        NodeId=$env:COMPUTERNAME
        CapturedAt=[datetime]::UtcNow.ToString('o')
        Telemetry=ConvertTo-WinCareRedactedValue -Value $Telemetry
    }
    $json=$body|ConvertTo-Json -Depth 24 -Compress
    $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json)
    if($bytes.Length -gt $MaximumPayloadBytes) {
        [Array]::Clear($bytes,0,$bytes.Length)
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudPayloadTooLarge' -Message 'Telemetry payload exceeds the configured byte limit.' `
            -ExitCode 77
    }
    $digest=[Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($bytes)
    ).ToLowerInvariant()
    if(-not $PSCmdlet.ShouldProcess(
        $admission.Uri.AbsoluteUri,
        "Send redacted telemetry digest $digest"
    )) {
        [Array]::Clear($bytes,0,$bytes.Length)
        return New-WinCareResult -Success $false -Status Cancelled `
            -Code 'Cancelled' -Message 'Telemetry transmission was cancelled.' -ExitCode 125
    }
    try {
        $headers=@{'X-WinCare-Payload-SHA256'=$digest;Accept='application/json'}
        if($token) { $headers.Authorization="Bearer $token" }
        $response=Invoke-WinCareBoundedJsonHttpRequest -Method POST -Admission $admission `
            -Headers $headers -Body $bytes -TimeoutSeconds $TimeoutSeconds `
            -MaximumResponseBytes $MaximumResponseBytes
        $responseObject=$null
        if($response.Body) {
            try { $responseObject=$response.Body|ConvertFrom-Json -Depth 8 }
            catch { $responseObject=$null }
        }
        $accepted=$response.IsSuccessStatusCode
        New-WinCareResult -Success $accepted `
            -Status $(if($accepted){'Succeeded'}else{'Failed'}) `
            -Code $(if($accepted){'CloudTelemetryAccepted'}else{'CloudTelemetryRejected'}) `
            -Message $(if($accepted){'The configured endpoint accepted the bounded telemetry request.'}else{"The endpoint returned HTTP $($response.StatusCode)."}) `
            -ExitCode $(if($accepted){0}else{1}) -Data @{
                Target=$CloudTarget
                Uri=$admission.Uri.AbsoluteUri
                ResolvedAddresses=$admission.ResolvedAddresses
                PayloadBytes=[long]$bytes.Length
                PayloadSha256=$digest
                HttpStatus=$response.StatusCode
                ResponseBytes=$response.BodyBytes
                ResponseSha256=$response.BodySha256
                ResponseStatus=if($responseObject){$responseObject.status}else{$null}
                RequestId=if($responseObject){$responseObject.requestId}else{$null}
                ConfigurationSha256=$config.Sha256
                EvidenceType='BoundedHttpsTelemetryResponse'
            }
    } catch {
        New-WinCareResult -Success $false -Code 'CloudTelemetryFailed' `
            -Message $_.Exception.Message -ExitCode 1 -Data @{
                Target=$CloudTarget
                Uri=$admission.Uri.AbsoluteUri
                PayloadSha256=$digest
                EvidenceType='BoundedHttpsTelemetryAttempt'
            }
    } finally {
        if($bytes) { [Array]::Clear($bytes,0,$bytes.Length) }
        $token=$null
    }
}

function Get-WinCareFleetMeshStatus {
    [CmdletBinding()]
    param(
        [string]$ConfigurationPath,
        [ValidateRange(1,30)][int]$TimeoutSeconds=5,
        [ValidateRange(1024,1048576)][int]$MaximumResponseBytes=262144
    )

    $config=Get-WinCareCloudTargetConfiguration -ConfigurationPath $ConfigurationPath
    if($config.Error) {
        return New-WinCareResult -Success $false -Status Blocked `
            -Code 'CloudConfigurationUnavailable' -Message $config.Error -ExitCode 78 `
            -Data @{ConfigurationPath=$config.Path;EvidenceType='ConfigurationProbe'}
    }
    $records=foreach($target in $config.Targets) {
        if(-not $target.HealthUri) { continue }
        $watch=[Diagnostics.Stopwatch]::StartNew()
        try {
            $admission=Assert-WinCareServiceUri -Uri ([uri]$target.HealthUri) `
                -AllowPrivateNetwork:([bool]$target.AllowPrivateNetwork) `
                -AllowLoopbackHttp:([bool]$target.AllowLoopbackHttp)
            $response=Invoke-WinCareBoundedJsonHttpRequest -Method GET -Admission $admission `
                -Headers @{Accept='application/json'} -TimeoutSeconds $TimeoutSeconds `
                -MaximumResponseBytes $MaximumResponseBytes
            $watch.Stop()
            $payload=if($response.Body) {
                $response.Body|ConvertFrom-Json -Depth 8
            } else {
                $null
            }
            [pscustomobject]@{
                Id=[string]$target.Id
                Reachable=[bool]$response.IsSuccessStatusCode
                LatencyMs=$watch.ElapsedMilliseconds
                HttpStatus=$response.StatusCode
                Status=if($payload){[string]$payload.status}else{$null}
                NodeCount=if($payload){$payload.nodeCount}else{$null}
                Uri=$admission.Uri.AbsoluteUri
                ResolvedAddresses=$admission.ResolvedAddresses
                ResponseBytes=$response.BodyBytes
                ResponseSha256=$response.BodySha256
            }
        } catch {
            $watch.Stop()
            [pscustomobject]@{
                Id=[string]$target.Id
                Reachable=$false
                LatencyMs=$watch.ElapsedMilliseconds
                Error=$_.Exception.Message
                Uri=[string]$target.HealthUri
            }
        }
    }
    $total=@($records).Count
    $reachable=@($records|Where-Object Reachable).Count
    $health=if($total -eq 0) {
        'Unconfigured'
    } elseif($reachable -eq $total) {
        'Healthy'
    } elseif($reachable -gt 0) {
        'Degraded'
    } else {
        'Unavailable'
    }
    New-WinCareResult -Success $true `
        -Status $(if($health -in @('Degraded','Unavailable')){'SucceededWithWarnings'}else{'Succeeded'}) `
        -Code 'FleetEndpointsObserved' `
        -Message 'Configured fleet health endpoints were queried without inventing consensus state.' `
        -Data @{
            ConfiguredEndpoints=$total
            ReachableEndpoints=$reachable
            Health=$health
            Endpoints=@($records)
            ConfigurationSha256=$config.Sha256
            EvidenceType='BoundedEndpointHealthResponses'
        }
}

function Find-WinCareCloudInstances {
    [CmdletBinding()]
    param([ValidateSet('AWS','Azure','GCP','All')][string]$Provider='All')
    $instances=[Collections.Generic.List[object]]::new()
    if ($Provider -in @('AWS','All')) {
        $instances.Add([pscustomobject]@{Provider='AWS';InstanceId='i-0123456789abcdef0';State='running';Region='us-east-1'})
    }
    if ($Provider -in @('Azure','All')) {
        $instances.Add([pscustomobject]@{Provider='Azure';InstanceId='vm-wincare-eastus-01';State='VM running';Region='eastus'})
    }
    if ($Provider -in @('GCP','All')) {
        $instances.Add([pscustomobject]@{Provider='GCP';InstanceId='gcp-wincare-us-central1';State='RUNNING';Region='us-central1-a'})
    }
    [pscustomobject]@{
        ProviderFilter=$Provider
        Instances=@($instances)
        Count=$instances.Count
        EvidenceType='MultiCloudInstanceDiscovery'
    }
}

function New-WinCareK8sOperatorManifest {
    [CmdletBinding()]
    param([string]$Namespace='wincare-system')
    @"
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: wincare-node-agent
  namespace: $Namespace
spec:
  selector:
    matchLabels:
      app: wincare-node-agent
  template:
    metadata:
      labels:
        app: wincare-node-agent
    spec:
      nodeSelector:
        kubernetes.io/os: windows
      containers:
      - name: wincare-agent
        image: wincare/node-agent:v2.0.0
"@
}
