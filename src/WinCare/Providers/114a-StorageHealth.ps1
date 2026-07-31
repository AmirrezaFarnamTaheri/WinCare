#requires -Version 7.2

function Resolve-WinCarePhysicalDiskForDrive {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$DriveLetter)

    if(-not $IsWindows) {
        return [pscustomobject]@{
            Success=$false
            Status='WindowsRequired'
            Reason='Storage reliability evidence requires Windows.'
            DiskNumber=$null
            LogicalDisk=$null
            PhysicalDisk=$null
        }
    }

    foreach($commandName in @('Get-Partition','Get-Disk','Get-PhysicalDisk')) {
        if(-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
            return [pscustomobject]@{
                Success=$false
                Status='ProviderUnavailable'
                Reason="Required storage provider command is unavailable: $commandName"
                DiskNumber=$null
                LogicalDisk=$null
                PhysicalDisk=$null
            }
        }
    }

    try {
        $partition=Get-Partition `
            -DriveLetter $DriveLetter `
            -ErrorAction Stop |
            Select-Object -First 1
        if($null -eq $partition) {
            throw "No partition is associated with drive $DriveLetter`:"
        }

        $diskNumber=[int]$partition.DiskNumber
        $logicalDisk=Get-Disk -Number $diskNumber -ErrorAction Stop
        $physicalDisks=@(Get-PhysicalDisk -ErrorAction Stop)
        $candidate=$null
        $logicalUniqueId=[string]$logicalDisk.UniqueId

        if(-not [string]::IsNullOrWhiteSpace($logicalUniqueId)) {
            $uniqueMatches=@(
                $physicalDisks |
                    Where-Object {
                        [string]$_.UniqueId -eq $logicalUniqueId
                    }
            )
            if($uniqueMatches.Count -eq 1) {
                $candidate=$uniqueMatches[0]
            }
        }

        if($null -eq $candidate) {
            $deviceMatches=@(
                $physicalDisks |
                    Where-Object {
                        [string]$_.DeviceId -eq [string]$diskNumber
                    }
            )
            if($deviceMatches.Count -eq 1) {
                $candidate=$deviceMatches[0]
            }
        }

        $logicalFriendlyName=[string]$logicalDisk.FriendlyName
        if(
            $null -eq $candidate -and
            -not [string]::IsNullOrWhiteSpace($logicalFriendlyName)
        ) {
            $nameMatches=@(
                $physicalDisks |
                    Where-Object {
                        [string]$_.FriendlyName -eq $logicalFriendlyName
                    }
            )
            if($nameMatches.Count -eq 1) {
                $candidate=$nameMatches[0]
            }
        }

        if($null -eq $candidate) {
            return [pscustomobject]@{
                Success=$false
                Status='DiskIdentityUnresolved'
                Reason=(
                    'The physical disk backing drive {0}: could not be ' +
                    'resolved unambiguously.'
                ) -f $DriveLetter
                DiskNumber=$diskNumber
                LogicalDisk=$logicalDisk
                PhysicalDisk=$null
            }
        }

        [pscustomobject]@{
            Success=$true
            Status='Resolved'
            Reason=$null
            DiskNumber=$diskNumber
            LogicalDisk=$logicalDisk
            PhysicalDisk=$candidate
        }
    } catch {
        [pscustomobject]@{
            Success=$false
            Status='DiskIdentityUnavailable'
            Reason=$_.Exception.Message
            DiskNumber=$null
            LogicalDisk=$null
            PhysicalDisk=$null
        }
    }
}

function Get-WinCareStorageReliabilityEvidence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$PhysicalDisk)

    if(
        -not (
            Get-Command `
                Get-StorageReliabilityCounter `
                -ErrorAction SilentlyContinue
        )
    ) {
        return [pscustomobject]@{
            Success=$false
            Status='ProviderUnavailable'
            Reason='Get-StorageReliabilityCounter is unavailable.'
            Counter=$null
        }
    }

    try {
        $counter=Get-StorageReliabilityCounter `
            -PhysicalDisk $PhysicalDisk `
            -ErrorAction Stop
        if($null -eq $counter) {
            throw 'The storage provider returned no reliability counter record.'
        }

        [pscustomobject]@{
            Success=$true
            Status='Measured'
            Reason=$null
            Counter=$counter
        }
    } catch {
        [pscustomobject]@{
            Success=$false
            Status='CounterUnavailable'
            Reason=$_.Exception.Message
            Counter=$null
        }
    }
}

function Get-WinCareOptionalStorageCounterValue {
    [CmdletBinding()]
    param(
        [AllowNull()][object]$Counter,
        [Parameter(Mandatory)][string]$PropertyName,
        [Parameter(Mandatory)][type]$TargetType
    )

    if($null -eq $Counter) {
        return $null
    }
    if($null -eq $Counter.PSObject.Properties[$PropertyName]) {
        return $null
    }

    $value=$Counter.$PropertyName
    if($null -eq $value) {
        return $null
    }
    [Convert]::ChangeType($value,$TargetType)
}

function Get-WinCareStorageHealthTriage {
    [CmdletBinding()]
    param([string]$DriveLetter='C')

    $cleanDrive=($DriveLetter.Trim() -replace '[:\\/]+$','')
    if($cleanDrive -notmatch '^[A-Za-z]$') {
        throw (
            'DriveLetter must identify one drive letter, for example ' +
            'C, C:, or C:\.'
        )
    }

    $cleanDrive=$cleanDrive.ToUpperInvariant()
    $issues=[Collections.Generic.List[string]]::new()
    $resolution=Resolve-WinCarePhysicalDiskForDrive `
        -DriveLetter $cleanDrive

    if(-not $resolution.Success) {
        if(
            -not [string]::IsNullOrWhiteSpace(
                [string]$resolution.Reason
            )
        ) {
            $issues.Add([string]$resolution.Reason)
        }

        return [pscustomobject]@{
            Supported=$false
            DriveLetter=$cleanDrive
            DiskNumber=$resolution.DiskNumber
            PhysicalDiskResolved=$false
            ReliabilityDataResolved=$false
            MediaType=$null
            HealthStatus='Unknown'
            WearPercentage=$null
            TemperatureCelsius=$null
            UncorrectedReadErrors=$null
            TrimRecommended=$null
            EvidenceCompleteness='Unavailable'
            EvidenceIssues=@($issues)
            Status=[string]$resolution.Status
            AuditTime=[datetime]::UtcNow.ToString('o')
            EvidenceType='StorageHealthTriageReport'
        }
    }

    $physicalDisk=$resolution.PhysicalDisk
    $counterEvidence=Get-WinCareStorageReliabilityEvidence `
        -PhysicalDisk $physicalDisk
    if(
        -not $counterEvidence.Success -and
        -not [string]::IsNullOrWhiteSpace(
            [string]$counterEvidence.Reason
        )
    ) {
        $issues.Add([string]$counterEvidence.Reason)
    }

    $counter=$counterEvidence.Counter
    $wear=Get-WinCareOptionalStorageCounterValue `
        -Counter $counter `
        -PropertyName 'Wear' `
        -TargetType ([int])
    $temp=Get-WinCareOptionalStorageCounterValue `
        -Counter $counter `
        -PropertyName 'Temperature' `
        -TargetType ([int])
    $uncorrected=Get-WinCareOptionalStorageCounterValue `
        -Counter $counter `
        -PropertyName 'ReadErrorsTotal' `
        -TargetType ([long])

    if($null -eq $wear) {
        $issues.Add('WearPercentageUnavailable')
    }
    if($null -eq $temp) {
        $issues.Add('TemperatureUnavailable')
    }
    if($null -eq $uncorrected) {
        $issues.Add('ReadErrorsTotalUnavailable')
    }

    $mediaType=[string]$physicalDisk.MediaType
    if(
        [string]::IsNullOrWhiteSpace($mediaType) -or
        $mediaType -eq 'Unspecified'
    ) {
        $mediaType=$null
        $issues.Add('MediaTypeUnavailable')
    }

    $elevated=(
        ($null -ne $wear -and $wear -gt 80) -or
        ($null -ne $temp -and $temp -gt 70) -or
        ($null -ne $uncorrected -and $uncorrected -gt 0)
    )
    $complete=(
        $null -ne $wear -and
        $null -ne $temp -and
        $null -ne $uncorrected
    )

    $healthStatus=if($elevated) {
        'ElevatedRisk'
    } elseif($complete) {
        'Healthy'
    } else {
        'Unknown'
    }

    $trimRecommended=if($null -eq $mediaType) {
        $null
    } elseif($mediaType -ne 'SSD') {
        $false
    } elseif($healthStatus -eq 'Healthy') {
        $true
    } elseif($healthStatus -eq 'ElevatedRisk') {
        $false
    } else {
        $null
    }

    $completeness=if($complete -and $null -ne $mediaType) {
        'Complete'
    } else {
        'Partial'
    }
    $physicalDiskUniqueId=if(
        $physicalDisk.PSObject.Properties['UniqueId']
    ) {
        [string]$physicalDisk.UniqueId
    } else {
        $null
    }
    $physicalDiskFriendlyName=if(
        $physicalDisk.PSObject.Properties['FriendlyName']
    ) {
        [string]$physicalDisk.FriendlyName
    } else {
        $null
    }

    [pscustomobject]@{
        Supported=$true
        DriveLetter=$cleanDrive
        DiskNumber=[int]$resolution.DiskNumber
        PhysicalDiskResolved=$true
        ReliabilityDataResolved=[bool]$counterEvidence.Success
        PhysicalDiskUniqueId=$physicalDiskUniqueId
        PhysicalDiskFriendlyName=$physicalDiskFriendlyName
        MediaType=$mediaType
        HealthStatus=$healthStatus
        WearPercentage=$wear
        TemperatureCelsius=$temp
        UncorrectedReadErrors=$uncorrected
        TrimRecommended=$trimRecommended
        EvidenceCompleteness=$completeness
        EvidenceIssues=@($issues)
        Status=if($complete){'Measured'}else{'Partial'}
        AuditTime=[datetime]::UtcNow.ToString('o')
        EvidenceType='StorageHealthTriageReport'
    }
}
