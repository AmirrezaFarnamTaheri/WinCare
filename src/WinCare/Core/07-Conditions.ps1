function Compare-WinCareValue {
    param([object]$Actual,[object]$Expected,[string]$Operator='Equals')
    switch ($Operator) {
        'Equals' { return $Actual -eq $Expected }
        'NotEquals' { return $Actual -ne $Expected }
        'GreaterThan' { return $Actual -gt $Expected }
        'GreaterOrEqual' { return $Actual -ge $Expected }
        'LessThan' { return $Actual -lt $Expected }
        'LessOrEqual' { return $Actual -le $Expected }
        'Contains' { return @($Actual) -contains $Expected }
        'NotContains' { return @($Actual) -notcontains $Expected }
        'Matches' { return [string]$Actual -match [string]$Expected }
        'Like' { return [string]$Actual -like [string]$Expected }
        'Exists' { return $null -ne $Actual }
        default { throw "Unsupported condition operator: $Operator" }
    }
}

function Get-WinCareConditionValue {
    param([Parameter(Mandatory)][object]$Condition,[hashtable]$Context=@{})
    $p=$Condition.Parameters
    switch ([string]$Condition.Kind) {
        'Capability' { return Test-WinCareCapability -Name ([string]$p.Name) }
        'Admin' { return [bool]$script:WinCareState.IsAdmin }
        'Windows' { return [bool]$IsWindows }
        'Online' { return [bool](Test-WinCareInternetConnection) }
        'Config' { return Get-WinCareConfig -Name ([string]$p.Name) }
        'Policy' { return Get-WinCarePolicy -Name ([string]$p.Name) }
        'TerminalWidth' { return (Get-WinCareConsoleSize).Width }
        'TerminalHeight' { return (Get-WinCareConsoleSize).Height }
        'Build' {
            try { return [int](Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).BuildNumber } catch { return 0 }
        }
        'PendingRestart' { return [bool](Get-WinCarePendingRestart) }
        'ResultCount' { return [int](Get-WinCarePropertyValue $Context ([string]$p.Name) 0) }
        'Context' { return Get-WinCarePropertyValue $Context ([string]$p.Name) $null }
        'PathExists' { return Test-Path -LiteralPath ([string]$p.Path) }
        'CommandAvailable' { return [bool](Get-Command ([string]$p.Name) -ErrorAction SilentlyContinue) }
        'Environment' { return [Environment]::GetEnvironmentVariable([string]$p.Name) }
        default { throw "Unsupported condition kind: $($Condition.Kind)" }
    }
}

function Test-WinCareCondition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Condition,[hashtable]$Context=@{})
    $kind=[string]$Condition.Kind
    if ($kind -in @('All','Any','OnlyOne','Not')) {
        $values=@($Condition.Children | ForEach-Object { Test-WinCareCondition -Condition $_ -Context $Context })
        switch ($kind) {
            'All' { return -not ($values -contains $false) }
            'Any' { return $values -contains $true }
            'OnlyOne' { return @($values | Where-Object {$_}).Count -eq 1 }
            'Not' { return -not [bool]($values | Select-Object -First 1) }
        }
    }
    $actual=Get-WinCareConditionValue -Condition $Condition -Context $Context
    return Compare-WinCareValue -Actual $actual -Expected $Condition.Expected -Operator ([string]$Condition.Operator)
}

function Resolve-WinCareAdaptiveItem {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Item,[hashtable]$Context=@{})
    $visible=$true; $enabled=$true; $reasons=[Collections.Generic.List[string]]::new()
    foreach ($condition in @((Get-WinCarePropertyValue $Item 'VisibleWhen' @()))) {
        if (-not (Test-WinCareCondition -Condition $condition -Context $Context)) {$visible=$false; if ($condition.Message) {$reasons.Add([string]$condition.Message)}}
    }
    foreach ($condition in @((Get-WinCarePropertyValue $Item 'EnabledWhen' @()))) {
        if (-not (Test-WinCareCondition -Condition $condition -Context $Context)) {$enabled=$false; if ($condition.Message) {$reasons.Add([string]$condition.Message)}}
    }
    [pscustomobject]@{
        Label=$Item.Label; Description=$Item.Description; Action=$Item.Action; Badge=(Get-WinCarePropertyValue $Item 'Badge' '')
        Visible=$visible; Disabled=(-not $enabled); Reason=($reasons -join '; '); Original=$Item
    }
}

function Get-WinCareAdaptiveItems {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Items,[hashtable]$Context=@{})
    @($Items | ForEach-Object { Resolve-WinCareAdaptiveItem -Item $_ -Context $Context } | Where-Object Visible)
}
