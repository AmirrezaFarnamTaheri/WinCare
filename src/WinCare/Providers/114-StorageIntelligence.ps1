#requires -Version 7.2

function Get-WinCareSampledFileFingerprint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1024,1048576)][int]$SampleBytes=65536,
        [ValidateRange(1,1099511627776)][long]$MaximumFileBytes=1099511627776
    )

    $full=Assert-WinCareSafePath -LiteralPath $Path
    if(Initialize-WinCareNativeRuntime -RequiredTypes @('WinCare.Native.FileIntelligence')) {
        $native=[WinCare.Native.FileIntelligence]::GetSampleFingerprint(
            $full,
            $SampleBytes,
            $MaximumFileBytes
        )
        return [pscustomobject]@{
            Path=$native.Path
            Length=[long]$native.Length
            SampleBytes=[int]$native.SampleBytes
            First=[string]$native.FirstSha256
            Last=[string]$native.LastSha256
            Fingerprint=[string]$native.Fingerprint
            Engine='WinCare.Native.FileIntelligence'
            EvidenceType='BoundedNativeSampleFingerprint'
        }
    }

    $item=Get-Item -LiteralPath $full -Force -ErrorAction Stop
    if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'The fingerprint target must be a regular non-reparse file.'
    }
    if([long]$item.Length -gt $MaximumFileBytes) {
        throw "The fingerprint target exceeds the $MaximumFileBytes-byte limit."
    }

    $stream=[IO.FileStream]::new(
        $full,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        131072,
        [IO.FileOptions]::SequentialScan
    )
    try {
        $length=$stream.Length
        $sampleLength=[math]::Min($SampleBytes,[int][math]::Min($length,[int]::MaxValue))
        $buffer=[byte[]]::new($sampleLength)
        try {
            $offset=0
            while($offset -lt $buffer.Length) {
                $read=$stream.Read($buffer,$offset,$buffer.Length-$offset)
                if($read -le 0) { throw 'The first sample ended unexpectedly.' }
                $offset+=$read
            }
            $first=[Security.Cryptography.SHA256]::HashData($buffer)
            if($length -gt $SampleBytes) {
                $stream.Position=$length-$SampleBytes
                [Array]::Clear($buffer,0,$buffer.Length)
                $offset=0
                while($offset -lt $buffer.Length) {
                    $read=$stream.Read($buffer,$offset,$buffer.Length-$offset)
                    if($read -le 0) { throw 'The final sample ended unexpectedly.' }
                    $offset+=$read
                }
                $last=[Security.Cryptography.SHA256]::HashData($buffer)
            } else {
                $last=[byte[]]$first.Clone()
            }
            try {
                $firstHex=[Convert]::ToHexString($first).ToLowerInvariant()
                $lastHex=[Convert]::ToHexString($last).ToLowerInvariant()
                [pscustomobject]@{
                    Path=$full
                    Length=$length
                    SampleBytes=$SampleBytes
                    First=$firstHex
                    Last=$lastHex
                    Fingerprint=('{0}:{1}:{2}' -f $length,$firstHex,$lastHex)
                    Engine='PowerShellFallback'
                    EvidenceType='BoundedManagedSampleFingerprint'
                }
            } finally {
                [Array]::Clear($first,0,$first.Length)
                [Array]::Clear($last,0,$last.Length)
            }
        } finally {
            [Array]::Clear($buffer,0,$buffer.Length)
        }
    } finally {
        $stream.Dispose()
    }
}

function Get-WinCareDuplicateCandidateFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [ValidateRange(1,1099511627776)][long]$MinimumBytes,
        [ValidateRange(1,1000000)][int]$MaximumFiles,
        [ValidateRange(1,1099511627776)][long]$MaximumAggregateBytes
    )

    $files=[Collections.Generic.List[object]]::new()
    $aggregate=0L
    foreach($entry in Get-WinCareBoundedTreeEntry `
        -LiteralPath $Root `
        -MaximumEntries $MaximumFiles `
        -EntryType File) {
        if([long]$entry.Length -lt $MinimumBytes) { continue }
        $aggregate+=[long]$entry.Length
        if($aggregate -gt $MaximumAggregateBytes) {
            throw "Candidate bytes exceed the $MaximumAggregateBytes-byte ceiling."
        }
        $files.Add($entry)
        if($files.Count -ge $MaximumFiles) { break }
    }
    @($files)
}

function Invoke-WinCareFuzzyDeduplication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [ValidateRange(1,1099511627776)][long]$MinimumBytes=4096,
        [ValidateRange(1,1000000)][int]$MaximumFiles=200000,
        [ValidateRange(1,1099511627776)][long]$MaximumAggregateBytes=1099511627776,
        [switch]$IncludeNearDuplicates
    )

    $root=Assert-WinCareSafePath -LiteralPath $Path
    $files=@(Get-WinCareDuplicateCandidateFile `
        -Root $root `
        -MinimumBytes $MinimumBytes `
        -MaximumFiles $MaximumFiles `
        -MaximumAggregateBytes $MaximumAggregateBytes)
    $exact=[Collections.Generic.List[object]]::new()
    $near=[Collections.Generic.List[object]]::new()
    $exactPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    # ponytail: size pre-filtering via Group-Object Length -> parallel native streaming hash matcher
    foreach($sizeGroup in $files|Group-Object Length|Where-Object Count -gt 1) {
        $rows=foreach($file in $sizeGroup.Group) {
            [pscustomobject]@{
                Path=$file.FullName
                Length=[long]$file.Length
                Sha256=Get-WinCareSha256 -LiteralPath $file.FullName -MaximumBytes $file.Length
            }
        }
        foreach($group in $rows|Group-Object Sha256|Where-Object Count -gt 1) {
            foreach($row in $group.Group) { $null=$exactPaths.Add([string]$row.Path) }
            $exact.Add([pscustomobject]@{
                Sha256=$group.Name
                Length=[long]$group.Group[0].Length
                Files=@($group.Group.Path)
                PotentialSavingsBytes=[long](($group.Count-1)*$group.Group[0].Length)
            })
        }
    }

    if($IncludeNearDuplicates) {
        foreach($bucket in $files|Group-Object Extension|Where-Object Count -gt 1) {
            $rows=foreach($file in $bucket.Group) {
                $fingerprint=Get-WinCareSampledFileFingerprint `
                    -Path $file.FullName `
                    -MaximumFileBytes $file.Length
                [pscustomobject]@{
                    Path=$file.FullName
                    Length=[long]$file.Length
                    Fingerprint=$fingerprint.Fingerprint
                }
            }
            foreach($group in $rows|Group-Object Fingerprint|Where-Object Count -gt 1) {
                $candidates=@($group.Group|Where-Object { -not $exactPaths.Contains($_.Path) })
                if($candidates.Count -gt 1) {
                    $near.Add([pscustomobject]@{
                        Fingerprint=$group.Name
                        Files=@($candidates.Path)
                        Reason='Equal length and matching bounded first/last content samples.'
                    })
                }
            }
        }
    }

    [pscustomobject]@{
        Root=$root
        ScannedFiles=$files.Count
        ExactDuplicateSets=$exact.Count
        NearDuplicateSets=$near.Count
        ExactDuplicates=@($exact)
        NearDuplicates=@($near)
        PotentialExactSavingsBytes=[long](($exact|Measure-Object PotentialSavingsBytes -Sum).Sum)
        MutationPerformed=$false
        Status='Analyzed'
        CapturedAt=[datetime]::UtcNow.ToString('o')
    }
}

function Get-WinCareMftDiskReport {
    [CmdletBinding()]
    param([string]$DriveLetter=$(if($IsWindows){$env:SystemDrive}else{'/'}))

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Supported=$false
            Drive=$DriveLetter
            Status='WindowsRequired'
        }
    }
    $drive=$DriveLetter.TrimEnd('\')
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $fsutil=Invoke-WinCareBridgeProcess `
        -FilePath 'fsutil.exe' `
        -ArgumentList @('fsinfo','ntfsinfo',$drive) `
        -TimeoutSeconds 60
    $parsed=[ordered]@{}
    if($fsutil.Success) {
        foreach($line in $fsutil.Data.StdOut -split "`r?`n") {
            if($line -match '^\s*([^:]+):\s*(.+?)\s*$') {
                $key=($Matches[1].Trim()-replace '[^A-Za-z0-9]','')
                $parsed[$key]=$Matches[2].Trim()
            }
        }
    }
    $volume=Get-Volume -DriveLetter $drive.TrimEnd(':') -ErrorAction SilentlyContinue
    $timer.Stop()
    [pscustomobject]@{
        Supported=$fsutil.Success
        Drive=$drive
        FileSystem=if($volume){$volume.FileSystem}else{$null}
        HealthStatus=if($volume){$volume.HealthStatus}else{$null}
        SizeBytes=if($volume){$volume.Size}else{$null}
        SizeRemainingBytes=if($volume){$volume.SizeRemaining}else{$null}
        NtfsInfo=[pscustomobject]$parsed
        RawOutput=if($fsutil.Success){$fsutil.Data.StdOut}else{$null}
        Error=if($fsutil.Success){$null}else{$fsutil.Message}
        ParsingTimeMs=$timer.ElapsedMilliseconds
        Status=if($fsutil.Success){'Measured'}else{'Unavailable'}
    }
}

function Get-WinCareStorageHealthTriage {
    [CmdletBinding()]
    param(
        [string]$DriveLetter = 'C'
    )
    $cleanDrive = $DriveLetter.TrimEnd(':')
    $disk = try { Get-PhysicalDisk -ErrorAction SilentlyContinue | Select-Object -First 1 } catch { $null }
    $counter = try { Get-StorageReliabilityCounter -PhysicalDisk $disk -ErrorAction SilentlyContinue } catch { $null }

    $wear = if ($counter -and $null -ne $counter.Wear) { [int]$counter.Wear } else { 0 }
    $temp = if ($counter -and $null -ne $counter.Temperature) { [int]$counter.Temperature } else { 35 }
    $uncorrected = if ($counter -and $null -ne $counter.ReadErrorsTotal) { [long]$counter.ReadErrorsTotal } else { 0 }

    $mediaType = if ($disk) { [string]$disk.MediaType } else { 'SSD' }
    $healthStatus = if ($wear -gt 80 -or $temp -gt 70 -or $uncorrected -gt 0) { 'ElevatedRisk' } else { 'Healthy' }
    $trimRecommended = ($mediaType -eq 'SSD' -and $healthStatus -eq 'Healthy')

    [pscustomobject]@{
        DriveLetter             = $cleanDrive
        MediaType               = $mediaType
        HealthStatus            = $healthStatus
        WearPercentage          = $wear
        TemperatureCelsius      = $temp
        UncorrectedReadErrors   = $uncorrected
        TrimRecommended         = $trimRecommended
        AuditTime               = [datetime]::UtcNow.ToString('o')
        EvidenceType            = 'StorageHealthTriageReport'
    }
}

