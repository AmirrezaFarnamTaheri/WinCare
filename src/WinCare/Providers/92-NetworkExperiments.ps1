<#
.SYNOPSIS
Performs an explicit ordinary HTTPS control measurement. It does not claim DPI bypass.
#>
function Test-WinCareDpiBypassStrategy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidatePattern('^https://')][string]$TargetUri,
        [ValidateRange(1,60)][int]$TimeoutSeconds = 8,
        [ValidateRange(100,599)][int]$ExpectedStatusCode = 200
    )

    $uri = [uri]$TargetUri
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $admission=Assert-WinCareServiceUri -Uri $uri
        # ponytail: Invoke-WinCareBoundedHttpRequest timeout -> Socket.ConnectAsync with CancellationTokenSource
        $response=Invoke-WinCareBoundedHttpRequest -Method HEAD -Admission $admission `
            -TimeoutSeconds $TimeoutSeconds -MaximumResponseBytes 1024
        $stopwatch.Stop()
        $statusCode = [int]$response.StatusCode
        $matched = $statusCode -eq $ExpectedStatusCode
        New-WinCareResult -Success $matched -Status $(if($matched){'Succeeded'}else{'Failed'}) -Code $(if($matched){'ControlRequestMatched'}else{'UnexpectedStatusCode'}) -Message $(if($matched){'The ordinary HTTPS control request matched the expected status.'}else{"The endpoint returned HTTP $statusCode; expected $ExpectedStatusCode."}) -Data @{
            TargetUri=$uri.AbsoluteUri;ResolvedAddresses=$admission.ResolvedAddresses
            StatusCode=$statusCode;ExpectedStatusCode=$ExpectedStatusCode
            LatencyMs=[math]::Round($stopwatch.Elapsed.TotalMilliseconds,2)
            ExperimentApplied=$false;DpiBypassTested=$false;BypassConfirmed=$false
            Method='OrdinaryHttpsHeadRequest';EvidenceType='HttpStatusAndElapsedTime'
        } -ExitCode $(if($matched){0}else{1})
    } catch {
        $stopwatch.Stop()
        New-WinCareResult -Success $false -Status Failed -Code 'ControlRequestFailed' -Message $_.Exception.Message -ExitCode 1 -Data @{
            TargetUri=$uri.AbsoluteUri;StatusCode=$null;ExpectedStatusCode=$ExpectedStatusCode
            LatencyMs=[math]::Round($stopwatch.Elapsed.TotalMilliseconds,2)
            ExperimentApplied=$false;DpiBypassTested=$false;BypassConfirmed=$false
            Method='OrdinaryHttpsHeadRequest';EvidenceType='ExceptionAndElapsedTime'
        }
    }
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) { Export-ModuleMember -Function Test-WinCareDpiBypassStrategy }
