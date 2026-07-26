#requires -Version 7.2
Set-StrictMode -Version Latest

function Get-WinCareToolingRegularFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,4294967296)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $item = Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "$Purpose is not a regular non-reparse file: $LiteralPath"
    }
    if ([long]$item.Length -lt 0 -or [long]$item.Length -gt $MaximumBytes) {
        throw "$Purpose exceeds the $MaximumBytes-byte limit: $LiteralPath"
    }
    $item
}

function Read-WinCareToolingBoundedBytes {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,67108864)][int]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $before = Get-WinCareToolingRegularFile -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose $Purpose
    $length = [long]$before.Length
    $writeTicks = $before.LastWriteTimeUtc.Ticks
    $stream = [IO.FileStream]::new($before.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,65536,[IO.FileOptions]::SequentialScan)
    try {
        if ($stream.Length -ne $length) { throw "$Purpose changed before reading: $LiteralPath" }
        $bytes = [byte[]]::new([int]$length)
        $offset = 0
        while ($offset -lt $bytes.Length) {
            $read = $stream.Read($bytes,$offset,$bytes.Length-$offset)
            if ($read -le 0) { throw "$Purpose ended before its declared length: $LiteralPath" }
            $offset += $read
        }
        if ($stream.ReadByte() -ne -1) { throw "$Purpose grew while reading: $LiteralPath" }
    } finally {
        $stream.Dispose()
    }
    $after = Get-WinCareToolingRegularFile -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose $Purpose
    if ([long]$after.Length -ne $length -or $after.LastWriteTimeUtc.Ticks -ne $writeTicks) {
        if ($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) }
        throw "$Purpose changed while reading: $LiteralPath"
    }
    $bytes
}

function Read-WinCareToolingBoundedUtf8Text {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,67108864)][int]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $bytes = Read-WinCareToolingBoundedBytes -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose $Purpose
    try {
        [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
    } finally {
        if ($bytes.Length) { [Array]::Clear($bytes,0,$bytes.Length) }
    }
}

function Read-WinCareToolingJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,16777216)][int]$MaximumBytes = 1048576,
        [ValidateRange(2,100)][int]$MaximumDepth = 32
    )
    Read-WinCareToolingBoundedUtf8Text -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose 'Tooling JSON input' |
        ConvertFrom-Json -Depth $MaximumDepth -ErrorAction Stop
}

function Get-WinCareToolingFileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [ValidateRange(1,4294967296)][long]$MaximumBytes,
        [Parameter(Mandatory)][string]$Purpose
    )
    $before = Get-WinCareToolingRegularFile -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose $Purpose
    $length = [long]$before.Length
    $writeTicks = $before.LastWriteTimeUtc.Ticks
    $stream = [IO.FileStream]::new($before.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,131072,[IO.FileOptions]::SequentialScan)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
    $buffer = [byte[]]::new(131072)
    $observed = 0L
    try {
        if ($stream.Length -ne $length) { throw "$Purpose changed before hashing: $LiteralPath" }
        while (($read = $stream.Read($buffer,0,$buffer.Length)) -gt 0) {
            $observed += $read
            if ($observed -gt $MaximumBytes) { throw "$Purpose exceeded its byte limit while hashing: $LiteralPath" }
            $hash.AppendData($buffer,0,$read)
        }
        if ($observed -ne $length) { throw "$Purpose changed length while hashing: $LiteralPath" }
        $digest = $hash.GetHashAndReset()
        try { $hex = [Convert]::ToHexString($digest).ToLowerInvariant() }
        finally { [Array]::Clear($digest,0,$digest.Length) }
    } finally {
        [Array]::Clear($buffer,0,$buffer.Length)
        $hash.Dispose()
        $stream.Dispose()
    }
    $after = Get-WinCareToolingRegularFile -LiteralPath $LiteralPath -MaximumBytes $MaximumBytes -Purpose $Purpose
    if ([long]$after.Length -ne $length -or $after.LastWriteTimeUtc.Ticks -ne $writeTicks) {
        throw "$Purpose changed while hashing: $LiteralPath"
    }
    [pscustomobject]@{ Sha256=$hex; Bytes=$length }
}

function Stop-WinCareToolingProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Diagnostics.Process]$Process)
    if (-not $Process.HasExited) {
        try { $Process.Kill($true) } catch { try { $Process.Kill() } catch { } }
        try { $Process.WaitForExit(5000) | Out-Null } catch { }
    }
}

function Invoke-WinCareToolingProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [ValidateRange(1,7200)][int]$TimeoutSeconds,
        [ValidateRange(1024,134217728)][int]$MaximumCapturedOutputBytes,
        [string]$WorkingDirectory,
        [switch]$WriteCapturedOutput
    )
    $executableItem = Get-WinCareToolingRegularFile -LiteralPath $Executable -MaximumBytes 1073741824L -Purpose 'Tool executable'
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $executableItem.FullName
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($WorkingDirectory) { $info.WorkingDirectory = $WorkingDirectory }
    foreach ($argument in $Arguments) { [void]$info.ArgumentList.Add([string]$argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $stdout = [IO.MemoryStream]::new()
    $stderr = [IO.MemoryStream]::new()
    $stdoutBuffer = [byte[]]::new(16384)
    $stderrBuffer = [byte[]]::new(16384)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw "Failed to start tooling process: $Executable" }
        $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length)
        $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length)
        $stdoutDone = $false
        $stderrDone = $false
        $captured = 0L
        while (-not ($stdoutDone -and $stderrDone -and $process.HasExited)) {
            if ($clock.Elapsed.TotalSeconds -gt $TimeoutSeconds) {
                Stop-WinCareToolingProcess -Process $process
                throw "Tooling process exceeded the $TimeoutSeconds-second timeout: $Executable"
            }
            $progress = $false
            foreach ($channel in @('stdout','stderr')) {
                if ($channel -eq 'stdout') { $done=$stdoutDone; $task=$stdoutTask; $buffer=$stdoutBuffer; $target=$stdout }
                else { $done=$stderrDone; $task=$stderrTask; $buffer=$stderrBuffer; $target=$stderr }
                if ($done -or -not $task.IsCompleted) { continue }
                $read = $task.GetAwaiter().GetResult()
                if ($read -eq 0) {
                    if ($channel -eq 'stdout') { $stdoutDone=$true } else { $stderrDone=$true }
                } else {
                    $captured += $read
                    if ($captured -gt $MaximumCapturedOutputBytes) {
                        Stop-WinCareToolingProcess -Process $process
                        throw "Tooling process exceeded the $MaximumCapturedOutputBytes-byte output limit: $Executable"
                    }
                    $target.Write($buffer,0,$read)
                    $next = if ($channel -eq 'stdout') { $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer,0,$stdoutBuffer.Length) } else { $process.StandardError.BaseStream.ReadAsync($stderrBuffer,0,$stderrBuffer.Length) }
                    if ($channel -eq 'stdout') { $stdoutTask=$next } else { $stderrTask=$next }
                }
                $progress = $true
            }
            if (-not $progress) { Start-Sleep -Milliseconds 10 }
        }
        $process.WaitForExit()
        $encoding = [Text.UTF8Encoding]::new($false,$false)
        $stdoutBytes = $stdout.ToArray()
        $stderrBytes = $stderr.ToArray()
        try {
            $outText = $encoding.GetString($stdoutBytes)
            $errText = $encoding.GetString($stderrBytes)
            if ($WriteCapturedOutput -and $outText) { [Console]::Out.Write($outText) }
            if ($WriteCapturedOutput -and $errText) { [Console]::Error.Write($errText) }
            [pscustomobject]@{ ExitCode=$process.ExitCode; StandardOutput=$outText; StandardError=$errText; CapturedBytes=$captured }
        } finally {
            if ($stdoutBytes.Length) { [Array]::Clear($stdoutBytes,0,$stdoutBytes.Length) }
            if ($stderrBytes.Length) { [Array]::Clear($stderrBytes,0,$stderrBytes.Length) }
        }
    } finally {
        Stop-WinCareToolingProcess -Process $process
        $clock.Stop()
        [Array]::Clear($stdoutBuffer,0,$stdoutBuffer.Length)
        [Array]::Clear($stderrBuffer,0,$stderrBuffer.Length)
        $stdout.Dispose()
        $stderr.Dispose()
        $process.Dispose()
    }
}

function Resolve-WinCareToolingSourceDateEpoch {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $epoch = 0L
    if ($env:SOURCE_DATE_EPOCH -and [long]::TryParse($env:SOURCE_DATE_EPOCH,[ref]$epoch) -and $epoch -ge 315532800L) { return $epoch }
    $git = Get-Command git -ErrorAction Stop
    $result = Invoke-WinCareToolingProcess -Executable $git.Source -Arguments @('-C',$RepositoryRoot,'show','-s','--format=%ct','HEAD') -TimeoutSeconds 30 -MaximumCapturedOutputBytes 4096
    if ($result.ExitCode -ne 0 -or -not [long]::TryParse($result.StandardOutput.Trim(),[ref]$epoch) -or $epoch -lt 315532800L) {
        throw 'SOURCE_DATE_EPOCH is invalid and the Git commit timestamp could not be resolved.'
    }
    $env:SOURCE_DATE_EPOCH = [string]$epoch
    $epoch
}

function Write-WinCareToolingAtomicJson {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [Parameter(Mandatory)]$Value,
        [ValidateRange(1,16777216)][int]$MaximumBytes = 1048576,
        [ValidateRange(2,100)][int]$Depth = 16
    )
    $text = $Value | ConvertTo-Json -Depth $Depth
    $bytes = [Text.UTF8Encoding]::new($false,$true).GetBytes($text + [Environment]::NewLine)
    if ($bytes.Length -gt $MaximumBytes) {
        [Array]::Clear($bytes,0,$bytes.Length)
        throw "Tooling JSON output exceeds $MaximumBytes bytes: $LiteralPath"
    }
    $temporary = "$LiteralPath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        $stream = [IO.FileStream]::new($temporary,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None,65536,[IO.FileOptions]::WriteThrough)
        try { $stream.Write($bytes,0,$bytes.Length); $stream.Flush($true) }
        finally { $stream.Dispose() }
        Move-Item -LiteralPath $temporary -Destination $LiteralPath -Force -ErrorAction Stop
    } finally {
        [Array]::Clear($bytes,0,$bytes.Length)
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
