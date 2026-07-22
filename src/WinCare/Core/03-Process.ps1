function Test-WinCareAdministrator {
    [CmdletBinding()]
    param()
    if (-not $IsWindows) { return $false }
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } catch { return $false }
}

function Get-WinCareElevatedActionAllowlist {
    [CmdletBinding()]
    param()
    @((Get-WinCareActionContractTable).Keys | Where-Object { [bool](Get-WinCareActionContract -Type $_).ElevationAllowed })
}

function Get-WinCareHmacSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][byte[]]$Key,[Parameter(Mandatory)][string]$Text)
    $hmac=[Security.Cryptography.HMACSHA256]::new($Key)
    try { [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant() }
    finally { $hmac.Dispose() }
}

function Test-WinCareFixedTimeHex {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Expected,[Parameter(Mandatory)][string]$Actual)
    if ($Expected -notmatch '^[a-fA-F0-9]{64}$' -or $Actual -notmatch '^[a-fA-F0-9]{64}$') { return $false }
    [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
        [Convert]::FromHexString($Expected),
        [Convert]::FromHexString($Actual)
    )
}

function New-WinCarePrivateDirectory {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$LiteralPath)
    $directory=New-Item -ItemType Directory -Path $LiteralPath -Force
    if ($IsWindows) {
        try {
            $identity=[Security.Principal.WindowsIdentity]::GetCurrent().User
            $security=[Security.AccessControl.DirectorySecurity]::new()
            $security.SetOwner($identity)
            $security.SetAccessRuleProtection($true,$false)
            $inherit=[Security.AccessControl.InheritanceFlags]'ContainerInherit, ObjectInherit'
            $propagation=[Security.AccessControl.PropagationFlags]::None
            $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,'FullControl',$inherit,$propagation,'Allow'))
            $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]'S-1-5-18','FullControl',$inherit,$propagation,'Allow'))
            $security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]'S-1-5-32-544','FullControl',$inherit,$propagation,'Allow'))
            Set-Acl -LiteralPath $directory.FullName -AclObject $security -ErrorAction Stop
        } catch {
            Remove-Item -LiteralPath $directory.FullName -Recurse -Force -ErrorAction SilentlyContinue
            throw "Unable to create a private elevation directory: $($_.Exception.Message)"
        }
    }
    $directory.FullName
}

function Invoke-WinCareProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [switch]$RequireAdmin,
        [int]$TimeoutSeconds = 3600,
        [int[]]$SuccessExitCodes = @(0),
        [string]$WorkingDirectory,
        [switch]$NoCapture
    )

    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) {
        return New-WinCareResult -Success $false -Message 'Process timeout must be between 1 and 86400 seconds.' -ExitCode 22 -Code 'InvalidTimeout'
    }
    if ($RequireAdmin -and -not $script:WinCareState.IsAdmin) {
        return New-WinCareResult -Success $false -Message 'Direct process elevation is disabled. Administrative work must use a typed WinCare action.' -ExitCode 5 -Code 'TypedElevationRequired'
    }

    $resolved = Get-Command $FilePath -ErrorAction SilentlyContinue
    if (-not $resolved -and -not (Test-Path -LiteralPath $FilePath)) {
        return New-WinCareResult -Success $false -Message "Executable not found: $FilePath" -ExitCode 127 -Code 'ExecutableNotFound'
    }
    $actualPath = if ($resolved) { $resolved.Source } else { (Resolve-Path -LiteralPath $FilePath).Path }
    $tempRoot = Join-Path $script:WinCareState.Root 'Logs'
    $null=New-Item -ItemType Directory -Path $tempRoot -Force
    $token = [guid]::NewGuid().ToString('N')
    $stdoutPath = Join-Path $tempRoot "$token.stdout.log"
    $stderrPath = Join-Path $tempRoot "$token.stderr.log"

    Write-WinCareLog -Level Audit -Message 'Starting external process' -Data @{
        filePath = $actualPath; arguments = @($ArgumentList); requireAdmin = [bool]$RequireAdmin
    }

    try {
        $startInfo=[Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName=$actualPath
        $startInfo.UseShellExecute=$false
        $startInfo.CreateNoWindow=$false
        if ($WorkingDirectory) { $startInfo.WorkingDirectory=(Resolve-Path -LiteralPath $WorkingDirectory).Path }
        foreach ($argument in @($ArgumentList)) { $startInfo.ArgumentList.Add([string]$argument) }
        if (-not $NoCapture) {
            $startInfo.RedirectStandardOutput=$true
            $startInfo.RedirectStandardError=$true
        }
        $process=[Diagnostics.Process]::new()
        $process.StartInfo=$startInfo
        if (-not $process.Start()) { return New-WinCareResult -Success $false -Message 'The process could not be started.' -ExitCode 1 -Code 'ProcessStartFailed' }
        if (-not $NoCapture) {
            $stdoutTask=$process.StandardOutput.ReadToEndAsync()
            $stderrTask=$process.StandardError.ReadToEndAsync()
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { Write-Verbose 'A best-effort operation was unavailable.' }
            return New-WinCareResult -Success $false -Message "Process timed out after $TimeoutSeconds seconds." -ExitCode 124 -Code 'ProcessTimeout'
        }
        if (-not $NoCapture) {
            $stdout=$stdoutTask.GetAwaiter().GetResult()
            $stderr=$stderrTask.GetAwaiter().GetResult()
            $stdout | Set-Content -LiteralPath $stdoutPath -Encoding utf8NoBOM
            $stderr | Set-Content -LiteralPath $stderrPath -Encoding utf8NoBOM
        } else { $stdout=''; $stderr='' }
        $success = $process.ExitCode -in $SuccessExitCodes
        $restartRequired = $process.ExitCode -in @(1641,3010)
        $message = if ($success -and $restartRequired) { 'Process completed successfully; a restart is required.' } elseif ($success) { 'Process completed successfully.' } else { "Process exited with code $($process.ExitCode)." }
        Write-WinCareLog -Level $(if ($success) {'Info'} else {'Error'}) -Message $message -Data @{
            filePath = $actualPath; exitCode = $process.ExitCode; stderr = $stderr
        }
        return New-WinCareResult -Success $success -Message $message -ExitCode $process.ExitCode -Code $(if($success){'ProcessSucceeded'}else{'ProcessFailed'}) -Data @{
            StdOut = $stdout; StdErr = $stderr; FilePath = $actualPath; Arguments = @($ArgumentList); RestartRequired = $restartRequired
        }
    } catch {
        Write-WinCareLog -Level Error -Message 'External process failed to start.' -Data @{ error = $_.Exception.Message; filePath = $actualPath }
        return New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 1 -Code 'ProcessException'
    }
}

function Invoke-WinCareElevatedAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Action,
        [Parameter(Mandatory)][string]$OperationId,
        [int]$TimeoutSeconds=14400
    )
    if (-not $IsWindows) { return New-WinCareResult -Success $false -Message 'Elevation is only supported on Windows.' -ExitCode 5 -Code 'WindowsRequired' }
    if (-not [bool]$Action.RequiresAdmin) { return New-WinCareResult -Success $false -Message 'Refusing to elevate an action that is not marked administrative.' -ExitCode 5 -Code 'AdminFlagRequired' }
    if ([string]$Action.Type -notin (Get-WinCareElevatedActionAllowlist)) { return New-WinCareResult -Success $false -Message "Administrative action type is not allowlisted: $($Action.Type)" -ExitCode 5 -Code 'ActionNotAllowlisted' }
    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) { return New-WinCareResult -Success $false -Message 'Elevation timeout is invalid.' -ExitCode 22 -Code 'InvalidTimeout' }

    $elevationRoot=Join-Path (Join-Path $script:WinCareState.Root 'Broker') 'Elevation'
    $null=New-Item -ItemType Directory -Path $elevationRoot -Force
    $nonce=[guid]::NewGuid().ToString('N')
    $handoff=Join-Path $elevationRoot $nonce
    try { $handoff=New-WinCarePrivateDirectory -LiteralPath $handoff }
    catch { return New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 5 -Code 'PrivateHandoffFailed' }

    $requestPath=Join-Path $handoff 'request.json'
    $resultPath=Join-Path $handoff 'result.json'
    $helper=Join-Path $script:WinCareModuleRoot 'Host\ElevatedActionHost.ps1'
    $manifest=Join-Path $script:WinCareModuleRoot 'WinCare.psd1'
    try {
        $helper=Assert-WinCareSafePath -LiteralPath $helper -AllowedRoots @($script:WinCareModuleRoot)
        $manifest=Assert-WinCareSafePath -LiteralPath $manifest -AllowedRoots @($script:WinCareModuleRoot)
        $actionJson=$Action | ConvertTo-Json -Compress -Depth 40
        if ([Text.Encoding]::UTF8.GetByteCount($actionJson) -gt 1048576) { throw 'Administrative action payload exceeds 1 MiB.' }
        $actionBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($actionJson))
        $moduleTreeHash=Get-WinCareModuleTreeHash
        $policyJson=$script:WinCareState.Policy | ConvertTo-Json -Compress -Depth 30
        $payload=[ordered]@{
            SchemaVersion=3
            Nonce=$nonce
            CreatedAt=[datetime]::UtcNow.ToString('o')
            ExpiresAt=[datetime]::UtcNow.AddMinutes(5).ToString('o')
            OperationId=$OperationId
            ActionId=[string]$Action.Id
            ActionType=[string]$Action.Type
            ActionHash=Get-WinCareSha256Text -Text $actionJson
            ActionBase64=$actionBase64
            ModuleManifest=$manifest
            ModuleTreeHash=$moduleTreeHash
            HelperHash=Get-WinCareSha256 -LiteralPath $helper
            StateRoot=(Resolve-WinCareCanonicalPath -LiteralPath $script:WinCareState.Root -AllowMissing)
            PolicyHash=Get-WinCareSha256Text -Text $policyJson
            ResultPath=$resultPath
        }
        $payloadJson=$payload | ConvertTo-Json -Compress -Depth 40
        $secret=[byte[]]::new(32);[Security.Cryptography.RandomNumberGenerator]::Fill($secret)
        $payloadBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payloadJson))
        $request=[ordered]@{SchemaVersion=1;PayloadBase64=$payloadBase64;Signature=(Get-WinCareHmacSha256 -Key $secret -Text $payloadBase64)}
        Write-WinCareAtomicJson -LiteralPath $requestPath -InputObject $request
        $requestHash=Get-WinCareSha256 -LiteralPath $requestPath

        $hostExe=(Get-Process -Id $PID).Path
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=$hostExe
        $start.UseShellExecute=$true
        $start.Verb='runas'
        foreach($argument in @('-NoProfile','-NonInteractive','-File',$helper,'-RequestPath',$requestPath,'-RequestSha256',$requestHash,'-SecretBase64',[Convert]::ToBase64String($secret))){$start.ArgumentList.Add([string]$argument)}
        Write-WinCareLog -Level Audit -Message 'Requesting authenticated elevation for a typed action.' -Data @{actionId=$Action.Id;type=$Action.Type;operationId=$OperationId;nonce=$nonce;moduleTreeHash=$moduleTreeHash}
        $process=[Diagnostics.Process]::Start($start)
        if(-not $process){throw 'Unable to start elevated action host.'}
        if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
            try {$process.Kill($true)} catch { Write-Verbose 'A best-effort operation was unavailable.' }
            return New-WinCareResult -Success $false -Message 'Elevated action timed out.' -ExitCode 124 -Code 'ElevationTimeout'
        }
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            return New-WinCareResult -Success $false -Message 'Elevated action returned no authenticated result.' -ExitCode $process.ExitCode -Code 'MissingElevatedResult'
        }
        $wrapper=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json -AsHashtable -Depth 50
        $null=Test-WinCareStrictObjectKeys -InputObject $wrapper -AllowedKeys @('SchemaVersion','PayloadBase64','Signature') -Context 'elevated result envelope'
        if([int]$wrapper.SchemaVersion -ne 1){throw 'Unsupported elevated result envelope schema.'}
        $expected=Get-WinCareHmacSha256 -Key $secret -Text ([string]$wrapper.PayloadBase64)
        if(-not (Test-WinCareFixedTimeHex -Expected $expected -Actual ([string]$wrapper.Signature))){throw 'Elevated result authentication failed.'}
        $resultJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$wrapper.PayloadBase64))
        $envelope=$resultJson | ConvertFrom-Json -AsHashtable -Depth 50
        $null=Test-WinCareStrictObjectKeys -InputObject $envelope -AllowedKeys @('SchemaVersion','Nonce','OperationId','ActionId','ActionHash','ModuleTreeHash','FinishedAt','Result') -Context 'elevated result payload'
        if([int]$envelope.SchemaVersion -ne 3 -or [string]$envelope.Nonce -ne $nonce -or [string]$envelope.OperationId -ne $OperationId -or [string]$envelope.ActionId -ne [string]$Action.Id -or [string]$envelope.ActionHash -ne $payload.ActionHash -or [string]$envelope.ModuleTreeHash -ne $moduleTreeHash){throw 'Elevated result identity validation failed.'}
        return [pscustomobject]$envelope.Result
    } catch {
        return New-WinCareResult -Success $false -Message "Elevation was cancelled or failed: $($_.Exception.Message)" -ExitCode 5 -Code 'ElevationFailed'
    } finally {
        if($secret){[Array]::Clear($secret,0,$secret.Length)}
        Remove-Item -LiteralPath $handoff -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Start-WinCareDetachedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList=@()
    )
    $resolved=Get-Command $FilePath -ErrorAction SilentlyContinue
    if (-not $resolved -and -not (Test-Path -LiteralPath $FilePath)) {
        return New-WinCareResult -Success $false -Message "Executable not found: $FilePath" -ExitCode 127 -Code 'ExecutableNotFound'
    }
    $actualPath=if ($resolved) {$resolved.Source} else {(Resolve-Path -LiteralPath $FilePath).Path}
    try {
        $startInfo=[Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName=$actualPath
        $startInfo.UseShellExecute=$false
        foreach ($argument in @($ArgumentList)) { $startInfo.ArgumentList.Add([string]$argument) }
        $process=[Diagnostics.Process]::Start($startInfo)
        $data=@{FilePath=$actualPath;Arguments=@($ArgumentList);ProcessId=0;ProcessStartTime=''}
        if($process){$data.ProcessId=[int]$process.Id;try{$data.ProcessStartTime=$process.StartTime.ToUniversalTime().ToString('o')}catch{Write-Verbose 'Detached process start time could not be read.'}}
        New-WinCareResult -Success ([bool]$process) -Message 'Process started.' -Data $data
    } catch {
        New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 1 -Code 'ProcessStartFailed'
    }
}
