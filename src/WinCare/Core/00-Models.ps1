function New-WinCareResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][bool]$Success,
        [Parameter(Mandatory)][string]$Message,
        [object]$Data,
        [int]$ExitCode = 0,
        [string[]]$Warnings = @(),
        [ValidateSet('Succeeded','SucceededWithWarnings','Failed','Cancelled','Preview','Blocked')]
        [string]$Status = $(if ($Success) { 'Succeeded' } else { 'Failed' }),
        [string]$Code = '',
        [string]$OperationId = '',
        [string]$ActionId = '',
        [bool]$RestartRequired = $false
    )

    [pscustomobject]@{
        PSTypeName  = 'WinCare.Result'
        SchemaVersion = 3
        Success     = $Success
        Status      = $Status
        Code        = $Code
        Message     = $Message
        Data        = $Data
        ExitCode    = $ExitCode
        Warnings    = @($Warnings)
        OperationId = $OperationId
        ActionId    = $ActionId
        RestartRequired = $RestartRequired
        Timestamp   = [datetime]::UtcNow
    }
}

function New-WinCareCondition {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Kind,
        [string]$Operator = 'Equals',
        [object]$Expected = $true,
        [hashtable]$Parameters = @{},
        [object[]]$Children = @(),
        [string]$Message = ''
    )
    [pscustomobject]@{
        PSTypeName='WinCare.Condition'; SchemaVersion=1; Kind=$Kind; Operator=$Operator
        Expected=$Expected; Parameters=$Parameters; Children=@($Children); Message=$Message
    }
}

function New-WinCareAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Type,
        [Parameter(Mandatory)][string]$Label,
        [ValidateSet('ReadOnly','Low','Moderate','High','Critical')][string]$Risk = 'Low',
        [hashtable]$Parameters = @{},
        [bool]$RequiresAdmin = $false,
        [bool]$Reversible = $false,
        [long]$EstimatedBytes = 0,
        [string]$Verification = '',
        [object[]]$Preconditions = @(),
        [object[]]$Postconditions = @(),
        [hashtable]$Compensator = @{},
        [int]$TimeoutSeconds = 3600,
        [string]$IdempotencyKey = '',
        [string[]]$Tags = @(),
        [hashtable]$Compatibility = @{},
        [string[]]$SourceRecords = @(),
        [string]$RecoveryDescription = '',
        [bool]$RestartPossible = $false
    )

    if ([string]::IsNullOrWhiteSpace($IdempotencyKey)) {
        $stable = ConvertTo-WinCareCanonicalJson -InputObject ([ordered]@{ Type=$Type; Parameters=$Parameters; Label=$Label }) -Depth 24
        $bytes = [Text.Encoding]::UTF8.GetBytes($stable)
        $hash = [Security.Cryptography.SHA256]::HashData($bytes)
        $IdempotencyKey = [Convert]::ToHexString($hash).ToLowerInvariant()
    }

    [pscustomobject]@{
        PSTypeName          = 'WinCare.Action'
        SchemaVersion       = 3
        Id                  = [guid]::NewGuid().ToString('N')
        Type                = $Type
        Label               = $Label
        Risk                = $Risk
        Parameters          = $Parameters
        RequiresAdmin       = $RequiresAdmin
        Reversible          = $Reversible
        EstimatedBytes      = $EstimatedBytes
        Verification        = $Verification
        Preconditions       = @($Preconditions)
        Postconditions      = @($Postconditions)
        Compensator         = $Compensator
        TimeoutSeconds      = $TimeoutSeconds
        IdempotencyKey      = $IdempotencyKey
        Tags                = @($Tags)
        Compatibility       = $Compatibility
        SourceRecords       = @($SourceRecords)
        RecoveryDescription = $RecoveryDescription
        RestartPossible     = $RestartPossible
    }
}

function New-WinCarePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Title,
        [string]$Description = '',
        [object[]]$Actions = @(),
        [bool]$StopOnFailure = $true,
        [bool]$RollbackOnFailure = $true,
        [int]$MaxActions = 1000,
        [hashtable]$Metadata = @{},
        [string[]]$SourceRecords = @()
    )

    $plan = [pscustomobject]@{
        PSTypeName        = 'WinCare.Plan'
        SchemaVersion     = 3
        Id                = [guid]::NewGuid().ToString('N')
        Title             = $Title
        Description       = $Description
        Actions           = [System.Collections.Generic.List[object]]::new()
        StopOnFailure     = $StopOnFailure
        RollbackOnFailure = $RollbackOnFailure
        MaxActions        = $MaxActions
        Metadata          = $Metadata
        SourceRecords     = @($SourceRecords)
        CreatedAt         = [datetime]::UtcNow
    }
    foreach ($action in @($Actions)) { $plan.Actions.Add($action) }
    return $plan
}

function Add-WinCarePlanAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory, ValueFromPipeline)][object]$Plan,
        [Parameter(Mandatory)][object]$Action
    )
    process {
        if ($Plan.Actions.Count -ge $Plan.MaxActions) { throw "Plan action limit of $($Plan.MaxActions) was exceeded." }
        $Plan.Actions.Add($Action)
        $Plan
    }
}
