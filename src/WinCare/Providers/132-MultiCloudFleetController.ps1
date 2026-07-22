#requires -Version 7.2
function Send-WinCareMultiCloudTelemetry {
    [CmdletBinding()]
    param([string]$CloudTarget = 'AWS')
    return @{ Target = $CloudTarget; IngestedEvents = 1200; Status = 'Streamed' }
}

function Get-WinCareFleetMeshStatus {
    [CmdletBinding()]
    param()
    return @{ TotalNodes = 10000; ActiveLeaders = 3; MeshHealth = '100%'; Status = 'Synchronized' }
}
