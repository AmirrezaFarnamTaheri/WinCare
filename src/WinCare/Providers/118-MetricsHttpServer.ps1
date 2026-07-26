#requires -Version 7.2

function Start-WinCareMetricsHttpServer {
    [CmdletBinding()]
    param(
        [ValidateRange(1024,65535)][int]$Port=8899,
        [Parameter(Mandatory)][ValidateLength(32,512)][string]$BearerToken,
        [ValidateRange(1,30)][int]$ReadinessTimeoutSeconds=5
    )
    if(-not (Get-Command Start-ThreadJob -ErrorAction SilentlyContinue)) {
        return New-WinCareResult `
            -Success $false `
            -Status Blocked `
            -Code 'ThreadJobMissing' `
            -Message 'ThreadJob is required for the local metrics server.' `
            -ExitCode 78
    }
    if($script:WinCareMetricsJob) {
        $existing=Get-Job -Id $script:WinCareMetricsJob -ErrorAction SilentlyContinue
        if($existing -and $existing.State -eq 'Running') {
            $existingData=@{
                Port=$script:WinCareMetricsPort
                JobId=$existing.Id
                Prefix="http://127.0.0.1:$($script:WinCareMetricsPort)/"
                StartedAt=$script:WinCareMetricsStartedAt
                EvidenceType='ThreadJobState'
            }
            return New-WinCareResult `
                -Success $true `
                -Code 'MetricsServerAlreadyRunning' `
                -Message 'The loopback metrics server is already running.' `
                -Data $existingData
        }
    }

    $prefix="http://127.0.0.1:$Port/"
    $startedAt=[datetimeoffset]::UtcNow
    $tokenBytes=[Text.Encoding]::UTF8.GetBytes($BearerToken)
    try {
        $tokenHash=[Security.Cryptography.SHA256]::HashData($tokenBytes)
    } finally {
        [Array]::Clear($tokenBytes,0,$tokenBytes.Length)
    }

    $job=Start-ThreadJob `
        -Name "WinCareMetrics-$Port" `
        -ArgumentList $prefix,$tokenHash,$startedAt.ToUnixTimeSeconds() `
        -ScriptBlock {
            param($Prefix,[byte[]]$ExpectedTokenHash,[long]$StartedUnix)
            function Test-RequestAuthorization {
                param([string]$Authorization,[byte[]]$ExpectedHash)
                if([string]::IsNullOrWhiteSpace($Authorization)) { return $false }
                if(-not $Authorization.StartsWith('Bearer ',[StringComparison]::Ordinal)) {
                    return $false
                }
                $candidateBytes=[Text.Encoding]::UTF8.GetBytes($Authorization.Substring(7))
                try {
                    $candidateHash=[Security.Cryptography.SHA256]::HashData($candidateBytes)
                    try {
                        return [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
                            $candidateHash,
                            $ExpectedHash
                        )
                    } finally {
                        [Array]::Clear($candidateHash,0,$candidateHash.Length)
                    }
                } finally {
                    [Array]::Clear($candidateBytes,0,$candidateBytes.Length)
                }
            }

            $listener=[Net.HttpListener]::new()
            $listener.Prefixes.Add($Prefix)
            try {
                $listener.Start()
                while($listener.IsListening) {
                    try { $context=$listener.GetContext() } catch { break }
                    $request=$context.Request
                    $response=$context.Response
                    try {
                        $response.Headers['Cache-Control']='no-store'
                        $response.Headers['X-Content-Type-Options']='nosniff'
                        $status=200
                        $body=''
                        $authorized=Test-RequestAuthorization `
                            -Authorization ([string]$request.Headers['Authorization']) `
                            -ExpectedHash $ExpectedTokenHash
                        if($request.HttpMethod -ne 'GET') {
                            $status=405
                            $body='method not allowed'
                            $response.Headers['Allow']='GET'
                        } elseif(-not $authorized) {
                            $status=401
                            $body='unauthorized'
                            $response.Headers['WWW-Authenticate']='Bearer'
                        } elseif($request.Url.AbsolutePath -eq '/healthz') {
                            $body='ok'
                        } elseif($request.Url.AbsolutePath -eq '/metrics') {
                            $process=[Diagnostics.Process]::GetCurrentProcess()
                            $body=@(
                                'wincare_metrics_server_up 1'
                                "wincare_metrics_server_process_working_set_bytes $($process.WorkingSet64)"
                                "wincare_metrics_server_started_timestamp_seconds $StartedUnix"
                            ) -join "`n"
                            $response.ContentType='text/plain; version=0.0.4; charset=utf-8'
                        } else {
                            $status=404
                            $body='not found'
                        }
                        $bytes=[Text.Encoding]::UTF8.GetBytes($body+"`n")
                        $response.StatusCode=$status
                        $response.ContentLength64=$bytes.Length
                        $response.OutputStream.Write($bytes,0,$bytes.Length)
                    } catch {
                        try { $response.StatusCode=500 } catch {}
                    } finally {
                        $response.Close()
                    }
                }
            } finally {
                [Array]::Clear($ExpectedTokenHash,0,$ExpectedTokenHash.Length)
                if($listener.IsListening) { $listener.Stop() }
                $listener.Close()
            }
        }

    $ready=$false
    $lastError=$null
    $healthAdmission=Assert-WinCareServiceUri -Uri ([uri]($prefix+'healthz')) `
        -AllowPrivateNetwork -AllowLoopbackHttp
    $deadline=[datetime]::UtcNow.AddSeconds($ReadinessTimeoutSeconds)
    while(-not $ready -and [datetime]::UtcNow -lt $deadline) {
        if($job.State -in @('Failed','Stopped','Completed')) { break }
        try {
            $probe=Invoke-WinCareBoundedHttpRequest -Method GET `
                -Admission $healthAdmission `
                -Headers @{Authorization='Bearer '+$BearerToken} `
                -TimeoutSeconds 1 -MaximumResponseBytes 4096
            $ready=$probe.StatusCode -eq 200 -and $probe.Body.Trim() -eq 'ok'
        } catch {
            $lastError=$_.Exception.Message
            Start-Sleep -Milliseconds 100
        }
    }
    if(-not $ready) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        $jobOutput=(Receive-Job -Job $job -Keep -ErrorAction SilentlyContinue|Out-String).Trim()
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        $message=if($lastError) {
            $lastError
        } elseif($jobOutput) {
            $jobOutput
        } else {
            'The authenticated health probe did not succeed before the deadline.'
        }
        return New-WinCareResult `
            -Success $false `
            -Status Failed `
            -Code 'MetricsServerReadinessFailed' `
            -Message $message `
            -ExitCode 1 `
            -Data @{Port=$Port;Prefix=$prefix;EvidenceType='AuthenticatedLoopbackHealthProbe'}
    }

    $script:WinCareMetricsJob=$job.Id
    $script:WinCareMetricsPort=$Port
    $script:WinCareMetricsStartedAt=$startedAt.ToString('o')
    $data=@{
        Listening=$true
        Port=$Port
        Prefix=$prefix
        JobId=$job.Id
        StartedAt=$script:WinCareMetricsStartedAt
        Routes=@('/healthz','/metrics')
        Authentication='Bearer-SHA256-FixedTime'
        EvidenceType='AuthenticatedLoopbackHealthProbe'
    }
    New-WinCareResult `
        -Success $true `
        -Code 'MetricsServerStarted' `
        -Message 'Authenticated loopback metrics server started and passed its readiness probe.' `
        -Data $data
}

function Get-WinCareMetricsHttpServerState {
    [CmdletBinding()]
    param()
    $job=if($script:WinCareMetricsJob) {
        Get-Job -Id $script:WinCareMetricsJob -ErrorAction SilentlyContinue
    } else {
        $null
    }
    [pscustomobject]@{
        Listening=$null -ne $job -and $job.State -eq 'Running'
        Port=$script:WinCareMetricsPort
        JobId=if($job) { $job.Id } else { $null }
        State=if($job) { [string]$job.State } else { 'Stopped' }
        StartedAt=$script:WinCareMetricsStartedAt
        EvidenceType='ThreadJobState'
    }
}

function Stop-WinCareMetricsHttpServer {
    [CmdletBinding()]
    param()
    $job=if($script:WinCareMetricsJob) {
        Get-Job -Id $script:WinCareMetricsJob -ErrorAction SilentlyContinue
    } else {
        $null
    }
    if($job) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    }
    $script:WinCareMetricsJob=$null
    $script:WinCareMetricsPort=$null
    $script:WinCareMetricsStartedAt=$null
    New-WinCareResult `
        -Success $true `
        -Code 'MetricsServerStopped' `
        -Message 'The metrics server is stopped.' `
        -Data @{Stopped=$true;EvidenceType='ThreadJobAbsence'}
}
