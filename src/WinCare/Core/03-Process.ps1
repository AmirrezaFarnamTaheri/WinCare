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


function Add-WinCareCapturedProcessBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][IO.MemoryStream]$Destination,
        [Parameter(Mandatory)][byte[]]$Buffer,
        [ValidateRange(0,1048576)][int]$Count,
        [ValidateRange(1024,16777216)][int]$MaximumBytes
    )
    if($Count -le 0) { return $false }
    $remaining=$MaximumBytes-$Destination.Length
    if($remaining -le 0) { return $true }
    $accepted=[int][Math]::Min([long]$Count,$remaining)
    $Destination.Write($Buffer,0,$accepted)
    return $accepted -lt $Count
}

function ConvertFrom-WinCareCapturedProcessBytes {
    [CmdletBinding()]
    param([Parameter(Mandatory)][IO.MemoryStream]$Stream)
    $bytes=$Stream.ToArray()
    try {
        return [Text.UTF8Encoding]::new($false,$false).GetString($bytes)
    } finally {
        if($bytes.Length -gt 0) { [Array]::Clear($bytes,0,$bytes.Length) }
    }
}

function Invoke-WinCareCapturedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Diagnostics.ProcessStartInfo]$StartInfo,
        [ValidateRange(1,86400)][int]$TimeoutSeconds=3600,
        [ValidateRange(1024,16777216)][int]$MaximumCapturedOutputBytes=4194304
    )
    if(-not $StartInfo.RedirectStandardOutput -or -not $StartInfo.RedirectStandardError) {
        throw 'Bounded process capture requires both stdout and stderr redirection.'
    }
    $process=[Diagnostics.Process]::new()
    $process.StartInfo=$StartInfo
    $stdout=[IO.MemoryStream]::new($MaximumCapturedOutputBytes)
    $stderr=[IO.MemoryStream]::new($MaximumCapturedOutputBytes)
    $stdoutBuffer=[byte[]]::new(65536)
    $stderrBuffer=[byte[]]::new(65536)
    $timeout=[Threading.CancellationTokenSource]::new()
    $timeout.CancelAfter([timespan]::FromSeconds($TimeoutSeconds))
    $timedOut=$false
    $stdoutTruncated=$false
    $stderrTruncated=$false
    try {
        if(-not $process.Start()) { throw 'The process could not be started.' }
        $stdoutStream=$process.StandardOutput.BaseStream
        $stderrStream=$process.StandardError.BaseStream
        $stdoutOpen=$true
        $stderrOpen=$true
        $stdoutRead=$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$timeout.Token)
        $stderrRead=$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$timeout.Token)
        while($stdoutOpen -or $stderrOpen) {
            $active=[Collections.Generic.List[Threading.Tasks.Task]]::new()
            if($stdoutOpen) { $active.Add($stdoutRead) }
            if($stderrOpen) { $active.Add($stderrRead) }
            try {
                $null=[Threading.Tasks.Task]::WhenAny($active.ToArray()).GetAwaiter().GetResult()
            } catch [OperationCanceledException] {
                $timedOut=$true
                break
            }
            if($stdoutOpen -and $stdoutRead.IsCompleted) {
                try { $count=$stdoutRead.GetAwaiter().GetResult() }
                catch [OperationCanceledException] { $timedOut=$true; break }
                if($count -eq 0) { $stdoutOpen=$false }
                else {
                    if(Add-WinCareCapturedProcessBytes -Destination $stdout -Buffer $stdoutBuffer -Count $count -MaximumBytes $MaximumCapturedOutputBytes) {
                        $stdoutTruncated=$true
                    }
                    $stdoutRead=$stdoutStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length,$timeout.Token)
                }
            }
            if($stderrOpen -and $stderrRead.IsCompleted) {
                try { $count=$stderrRead.GetAwaiter().GetResult() }
                catch [OperationCanceledException] { $timedOut=$true; break }
                if($count -eq 0) { $stderrOpen=$false }
                else {
                    if(Add-WinCareCapturedProcessBytes -Destination $stderr -Buffer $stderrBuffer -Count $count -MaximumBytes $MaximumCapturedOutputBytes) {
                        $stderrTruncated=$true
                    }
                    $stderrRead=$stderrStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length,$timeout.Token)
                }
            }
        }
        if($timedOut -or $timeout.IsCancellationRequested) {
            $timedOut=$true
            try { $process.Kill($true) }
            catch { Write-Verbose 'The timed-out process could not be terminated cleanly.' }
        }
        if(-not $process.HasExited) { $process.WaitForExit() }
        [pscustomobject]@{
            TimedOut=$timedOut
            ExitCode=if($timedOut) { 124 } else { $process.ExitCode }
            StdOut=ConvertFrom-WinCareCapturedProcessBytes -Stream $stdout
            StdErr=ConvertFrom-WinCareCapturedProcessBytes -Stream $stderr
            StdoutBytes=[long]$stdout.Length
            StderrBytes=[long]$stderr.Length
            StdoutTruncated=$stdoutTruncated
            StderrTruncated=$stderrTruncated
            ProcessId=$process.Id
        }
    } finally {
        $timeout.Dispose()
        [Array]::Clear($stdoutBuffer,0,$stdoutBuffer.Length)
        [Array]::Clear($stderrBuffer,0,$stderrBuffer.Length)
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
    }
}

function Resolve-WinCareProcessExecutablePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
        [ValidateRange(1024,2147483648)][long]$MaximumExecutableBytes=1073741824
    )
    if([string]::IsNullOrWhiteSpace($FilePath)) { throw 'Executable path is empty.' }
    $candidate=$null
    if(Test-Path -LiteralPath $FilePath -PathType Leaf) {
        $candidate=(Resolve-Path -LiteralPath $FilePath -ErrorAction Stop).Path
    } else {
        $resolved=Get-Command -Name $FilePath -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if($resolved) { $candidate=[string]$resolved.Source }
    }
    if([string]::IsNullOrWhiteSpace($candidate)) { throw "Executable not found: $FilePath" }
    $candidate=Assert-WinCareSafePath -LiteralPath $candidate
    $item=Get-Item -LiteralPath $candidate -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Executable must be a regular non-reparse file: $candidate"
    }
    if([long]$item.Length -lt 1 -or [long]$item.Length -gt $MaximumExecutableBytes) {
        throw "Executable size is outside the allowed range: $candidate"
    }
    $digest=$null
    if(-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
        $digest=Get-WinCareSha256 -LiteralPath $candidate
        if(-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [Convert]::FromHexString($ExpectedSha256),
            [Convert]::FromHexString($digest)
        )) { throw "Executable digest does not match the approved SHA-256: $candidate" }
    }
    [pscustomobject]@{
        Path=$candidate
        Length=[long]$item.Length
        Sha256=$digest
        LastWriteTimeUtc=$item.LastWriteTimeUtc
    }
}

function Assert-WinCareProcessExecutableUnchanged {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Evidence,
        [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256
    )
    $item=Get-Item -LiteralPath ([string]$Evidence.Path) -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
        [long]$item.Length -ne [long]$Evidence.Length -or
        $item.LastWriteTimeUtc -ne $Evidence.LastWriteTimeUtc) {
        throw "Executable changed after admission: $($Evidence.Path)"
    }
    if($ExpectedSha256) {
        $digest=Get-WinCareSha256 -LiteralPath ([string]$Evidence.Path)
        if(-not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals(
            [Convert]::FromHexString($ExpectedSha256),
            [Convert]::FromHexString($digest)
        )) { throw "Executable digest changed after admission: $($Evidence.Path)" }
    }
    return $true
}

function Get-WinCareProcessArgumentEvidence {
    [CmdletBinding()]
    param([string[]]$ArgumentList=@())
    $canonical=ConvertTo-WinCareCanonicalJson -InputObject @($ArgumentList) -Depth 8
    [pscustomobject]@{
        Count=@($ArgumentList).Count
        Sha256=Get-WinCareSha256Text -Text $canonical -MaximumBytes 4194304
    }
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
        [switch]$NoCapture,
        [ValidateRange(1024,16777216)][int]$MaximumCapturedOutputBytes=4194304,
        [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256
    )

    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) {
        return New-WinCareResult -Success $false -Message 'Process timeout must be between 1 and 86400 seconds.' -ExitCode 22 -Code 'InvalidTimeout'
    }
    if ($RequireAdmin -and -not $script:WinCareState.IsAdmin) {
        return New-WinCareResult -Success $false -Message 'Direct process elevation is disabled. Administrative work must use a typed WinCare action.' -ExitCode 5 -Code 'TypedElevationRequired'
    }

    try {
        $executable=Resolve-WinCareProcessExecutablePath -FilePath $FilePath -ExpectedSha256 $ExpectedSha256
        $actualPath=[string]$executable.Path
        $argumentEvidence=Get-WinCareProcessArgumentEvidence -ArgumentList $ArgumentList
        $safeWorkingDirectory=$null
        if ($WorkingDirectory) {
            $safeWorkingDirectory=Assert-WinCareSafePath -LiteralPath $WorkingDirectory
            $workingItem=Get-Item -LiteralPath $safeWorkingDirectory -Force -ErrorAction Stop
            if(-not $workingItem.PSIsContainer -or ($workingItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Working directory must be a regular non-reparse directory: $safeWorkingDirectory"
            }
        }

        Write-WinCareLog -Level Audit -Message 'Starting external process' -Data @{
            filePath=$actualPath
            executableLength=[long]$executable.Length
            executableSha256=$executable.Sha256
            argumentCount=[int]$argumentEvidence.Count
            argumentsSha256=[string]$argumentEvidence.Sha256
            requireAdmin=[bool]$RequireAdmin
        }

        $startInfo=[Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName=$actualPath
        $startInfo.UseShellExecute=$false
        $startInfo.CreateNoWindow=$false
        if ($safeWorkingDirectory) { $startInfo.WorkingDirectory=$safeWorkingDirectory }
        foreach ($argument in @($ArgumentList)) { $startInfo.ArgumentList.Add([string]$argument) }
        if (-not $NoCapture) {
            $startInfo.RedirectStandardOutput=$true
            $startInfo.RedirectStandardError=$true
        }
        $null=Assert-WinCareProcessExecutableUnchanged -Evidence $executable -ExpectedSha256 $ExpectedSha256
        if($NoCapture) {
            $process=[Diagnostics.Process]::new()
            $process.StartInfo=$startInfo
            try {
                if(-not $process.Start()) {
                    return New-WinCareResult -Success $false -Message 'The process could not be started.' -ExitCode 1 -Code 'ProcessStartFailed'
                }
                if(-not $process.WaitForExit($TimeoutSeconds*1000)) {
                    try { $process.Kill($true) } catch { Write-Verbose 'The timed-out process could not be terminated cleanly.' }
                    return New-WinCareResult -Success $false -Message "Process timed out after $TimeoutSeconds seconds." -ExitCode 124 -Code 'ProcessTimeout'
                }
                $execution=[pscustomobject]@{
                    ExitCode=$process.ExitCode
                    StdOut=''
                    StdErr=''
                    StdoutTruncated=$false
                    StderrTruncated=$false
                }
            } finally {
                $process.Dispose()
            }
        } else {
            $execution=Invoke-WinCareCapturedProcess `
                -StartInfo $startInfo `
                -TimeoutSeconds $TimeoutSeconds `
                -MaximumCapturedOutputBytes $MaximumCapturedOutputBytes
            if($execution.TimedOut) {
                return New-WinCareResult -Success $false -Message "Process timed out after $TimeoutSeconds seconds." -ExitCode 124 -Code 'ProcessTimeout' -Data @{
                    StdOut=$execution.StdOut
                    StdErr=$execution.StdErr
                    StdoutTruncated=$execution.StdoutTruncated
                    StderrTruncated=$execution.StderrTruncated
                    FilePath=$actualPath
                    ArgumentCount=[int]$argumentEvidence.Count
                    ArgumentsSha256=[string]$argumentEvidence.Sha256
                }
            }
            if(-not [bool]$script:WinCareState.ReadOnlyLocked) {
                $captureRoot=Assert-WinCareSafePath -LiteralPath (Join-Path $script:WinCareState.Root 'Logs') -AllowMissing
                $null=New-Item -ItemType Directory -Path $captureRoot -Force
                $token=[guid]::NewGuid().ToString('N')
                $safeStdOut=ConvertTo-WinCareRedactedScalar -Value $execution.StdOut -MaximumStringCharacters $MaximumCapturedOutputBytes
                $safeStdErr=ConvertTo-WinCareRedactedScalar -Value $execution.StdErr -MaximumStringCharacters $MaximumCapturedOutputBytes
                Write-WinCareAtomicText -LiteralPath (Join-Path $captureRoot "$token.stdout.log") -Text $safeStdOut
                Write-WinCareAtomicText -LiteralPath (Join-Path $captureRoot "$token.stderr.log") -Text $safeStdErr
            }
        }
        $success=$execution.ExitCode -in $SuccessExitCodes
        $restartRequired=$execution.ExitCode -in @(1641,3010)
        $message=if($success -and $restartRequired) {
            'Process completed successfully; a restart is required.'
        } elseif($success) {
            'Process completed successfully.'
        } else {
            "Process exited with code $($execution.ExitCode)."
        }
        Write-WinCareLog -Level $(if($success){'Info'}else{'Error'}) -Message $message -Data @{
            filePath=$actualPath
            argumentCount=[int]$argumentEvidence.Count
            argumentsSha256=[string]$argumentEvidence.Sha256
            exitCode=$execution.ExitCode
            stderr=$execution.StdErr
            stdoutTruncated=[bool]$execution.StdoutTruncated
            stderrTruncated=[bool]$execution.StderrTruncated
        }
        return New-WinCareResult -Success $success -Message $message -ExitCode $execution.ExitCode -Code $(if($success){'ProcessSucceeded'}else{'ProcessFailed'}) -Data @{
            StdOut=$execution.StdOut
            StdErr=$execution.StdErr
            StdoutTruncated=[bool]$execution.StdoutTruncated
            StderrTruncated=[bool]$execution.StderrTruncated
            FilePath=$actualPath
            ArgumentCount=[int]$argumentEvidence.Count
            ArgumentsSha256=[string]$argumentEvidence.Sha256
            ExecutableSha256=$executable.Sha256
            RestartRequired=$restartRequired
        }
    } catch {
        Write-WinCareLog -Level Error -Message 'External process failed to start.' -Data @{ error = $_.Exception.Message; filePath = $FilePath }
        $code=if($_.Exception.Message -like 'Executable not found:*'){'ExecutableNotFound'}elseif($_.Exception.Message -like '*digest*'){'ExecutableDigestMismatch'}else{'ProcessException'}
        $exitCode=if($code -eq 'ExecutableNotFound'){127}elseif($code -eq 'ExecutableDigestMismatch'){74}else{1}
        return New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode $exitCode -Code $code
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
    if([string]$Action.Type -notin (Get-WinCareElevatedActionAllowlist)) {
        return New-WinCareResult `
            -Success $false `
            -Message "Administrative action type is not allowlisted: $($Action.Type)" `
            -ExitCode 5 `
            -Code 'ActionNotAllowlisted'
    }
    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 86400) { return New-WinCareResult -Success $false -Message 'Elevation timeout is invalid.' -ExitCode 22 -Code 'InvalidTimeout' }

    $elevationRoot=Join-Path (Join-Path $script:WinCareState.Root 'Broker') 'Elevation'
    $null=New-Item -ItemType Directory -Path $elevationRoot -Force
    $nonce=[guid]::NewGuid().ToString('N')
    $handoff=Join-Path $elevationRoot $nonce
    try { $handoff=New-WinCarePrivateDirectory -LiteralPath $handoff }
    catch { return New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode 5 -Code 'PrivateHandoffFailed' }

    $requestPath=Join-Path $handoff 'request.json'
    $secretPath=Join-Path $handoff 'secret.bin'
    $resultPath=Join-Path $handoff 'result.json'
    $helper=Join-Path $script:WinCareModuleRoot 'Host\ElevatedActionHost.ps1'
    $manifest=Join-Path $script:WinCareModuleRoot 'WinCare.psd1'
    $process=$null
    try {
        $helper=Assert-WinCareSafePath -LiteralPath $helper -AllowedRoots @($script:WinCareModuleRoot)
        $manifest=Assert-WinCareSafePath -LiteralPath $manifest -AllowedRoots @($script:WinCareModuleRoot)
        $actionJson=$Action | ConvertTo-Json -Compress -Depth 40
        if ([Text.Encoding]::UTF8.GetByteCount($actionJson) -gt 1048576) { throw 'Administrative action payload exceeds 1 MiB.' }
        $actionBase64=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($actionJson))
        $moduleTreeHash=Get-WinCareModuleTreeHash
        $policyJson=ConvertTo-WinCareCanonicalJson `
            -InputObject $script:WinCareState.Policy -Depth 30
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
        $request=[ordered]@{
            SchemaVersion=1
            PayloadBase64=$payloadBase64
            Signature=(Get-WinCareHmacSha256 -Key $secret -Text $payloadBase64)
        }
        Write-WinCareAtomicJson -LiteralPath $requestPath -InputObject $request
        Write-WinCareAtomicBytes -LiteralPath $secretPath -Bytes $secret
        $requestHash=Get-WinCareSha256 -LiteralPath $requestPath
        $secretHash=Get-WinCareSha256 -LiteralPath $secretPath

        $hostExe=(Get-Process -Id $PID).Path
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=$hostExe
        $start.UseShellExecute=$true
        $start.Verb='runas'
        $elevationArguments=@(
            '-NoProfile','-NonInteractive','-File',$helper,
            '-RequestPath',$requestPath,
            '-RequestSha256',$requestHash,
            '-SecretPath',$secretPath,
            '-SecretSha256',$secretHash
        )
        foreach($argument in $elevationArguments) {
            $start.ArgumentList.Add([string]$argument)
        }
        Write-WinCareLog `
            -Level Audit `
            -Message 'Requesting authenticated elevation for a typed action.' `
            -Data @{
                actionId=$Action.Id
                type=$Action.Type
                operationId=$OperationId
                nonce=$nonce
                moduleTreeHash=$moduleTreeHash
            }
        $process=[Diagnostics.Process]::Start($start)
        if(-not $process){throw 'Unable to start elevated action host.'}
        if (-not $process.WaitForExit($TimeoutSeconds*1000)) {
            try {$process.Kill($true)} catch { Write-Verbose 'A best-effort operation was unavailable.' }
            return New-WinCareResult -Success $false -Message 'Elevated action timed out.' -ExitCode 124 -Code 'ElevationTimeout'
        }
        if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            return New-WinCareResult -Success $false -Message 'Elevated action returned no authenticated result.' -ExitCode $process.ExitCode -Code 'MissingElevatedResult'
        }
        $resultItem=Get-Item -LiteralPath $resultPath -Force -ErrorAction Stop
        if($resultItem.Length -lt 1 -or $resultItem.Length -gt 8388608){
            throw 'Elevated result file length is invalid.'
        }
        $wrapper=Read-WinCareBoundedJson -LiteralPath $resultPath -MaximumBytes 8388608 -Depth 50 -AsHashtable
        $null=Test-WinCareStrictObjectKeys -InputObject $wrapper -AllowedKeys @('SchemaVersion','PayloadBase64','Signature') -Context 'elevated result envelope'
        if([int]$wrapper.SchemaVersion -ne 1){throw 'Unsupported elevated result envelope schema.'}
        $expected=Get-WinCareHmacSha256 -Key $secret -Text ([string]$wrapper.PayloadBase64)
        if(-not (Test-WinCareFixedTimeHex -Expected $expected -Actual ([string]$wrapper.Signature))){throw 'Elevated result authentication failed.'}
        $resultJson=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$wrapper.PayloadBase64))
        $envelope=$resultJson | ConvertFrom-Json -AsHashtable -Depth 50
        $null=Test-WinCareStrictObjectKeys `
            -InputObject $envelope `
            -AllowedKeys @(
                'SchemaVersion','Nonce','OperationId','ActionId',
                'ActionHash','ModuleTreeHash','FinishedAt','Result'
            ) `
            -Context 'elevated result payload'
        $identityValid=
            [int]$envelope.SchemaVersion -eq 3 -and
            [string]$envelope.Nonce -eq $nonce -and
            [string]$envelope.OperationId -eq $OperationId -and
            [string]$envelope.ActionId -eq [string]$Action.Id -and
            [string]$envelope.ActionHash -eq $payload.ActionHash -and
            [string]$envelope.ModuleTreeHash -eq $moduleTreeHash
        if(-not $identityValid) { throw 'Elevated result identity validation failed.' }
        return [pscustomobject]$envelope.Result
    } catch {
        return New-WinCareResult -Success $false -Message "Elevation was cancelled or failed: $($_.Exception.Message)" -ExitCode 5 -Code 'ElevationFailed'
    } finally {
        if($process){$process.Dispose()}
        if($secret){[Array]::Clear($secret,0,$secret.Length)}
        Remove-Item -LiteralPath $handoff -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Start-WinCareDetachedProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList=@(),
        [ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256
    )
    $process=$null
    try {
        $executable=Resolve-WinCareProcessExecutablePath -FilePath $FilePath -ExpectedSha256 $ExpectedSha256
        $argumentEvidence=Get-WinCareProcessArgumentEvidence -ArgumentList $ArgumentList
        $null=Assert-WinCareProcessExecutableUnchanged -Evidence $executable -ExpectedSha256 $ExpectedSha256
        $startInfo=[Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName=[string]$executable.Path
        $startInfo.UseShellExecute=$false
        foreach ($argument in @($ArgumentList)) { $startInfo.ArgumentList.Add([string]$argument) }
        $process=[Diagnostics.Process]::Start($startInfo)
        if(-not $process) {
            return New-WinCareResult -Success $false -Message 'The detached process could not be started.' -ExitCode 1 -Code 'ProcessStartFailed'
        }
        $data=@{
            FilePath=[string]$executable.Path
            ArgumentCount=[int]$argumentEvidence.Count
            ArgumentsSha256=[string]$argumentEvidence.Sha256
            ExecutableSha256=$executable.Sha256
            ProcessId=[int]$process.Id
            ProcessStartTime=''
        }
        try { $data.ProcessStartTime=$process.StartTime.ToUniversalTime().ToString('o') }
        catch { Write-Verbose 'Detached process start time could not be read.' }
        New-WinCareResult -Success $true -Message 'Process started.' -Data $data
    } catch {
        $code=if($_.Exception.Message -like 'Executable not found:*'){'ExecutableNotFound'}elseif($_.Exception.Message -like '*digest*'){'ExecutableDigestMismatch'}else{'ProcessStartFailed'}
        $exitCode=if($code -eq 'ExecutableNotFound'){127}elseif($code -eq 'ExecutableDigestMismatch'){74}else{1}
        New-WinCareResult -Success $false -Message $_.Exception.Message -ExitCode $exitCode -Code $code
    } finally {
        if($process) { $process.Dispose() }
    }
}

function Test-WinCareProcessHandleLeaks {
    [CmdletBinding()]
    param([int]$ProcessId=$PID)
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    $handles = if ($proc) { $proc.HandleCount } else { 0 }
    [pscustomobject]@{
        TargetProcessId=$ProcessId
        HandleCount=$handles
        ThreadCount=if ($proc) { $proc.Threads.Count } else { 0 }
        HandleLeakDetected=($handles -gt 5000)
        AuditedAt=[datetime]::UtcNow.ToString('o')
        EvidenceType='ProcessHandleAndThreadLeakAudit'
    }
}
