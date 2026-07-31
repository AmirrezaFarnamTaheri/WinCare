#requires -Version 7.2

# Canonical compatibility profiles and governed legacy-control plans.

function New-WinCareLegacyProfileRecord {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Description,
        [string[]]$Controls = @(),
        [string]$PlanKind = 'LegacyControls',
        [string]$TargetId = '',
        [string[]]$SourceRecords = @(),
        [int]$MaximumDurationMinutes = 0
    )
    [pscustomobject]@{
        Id = $Id
        Title = $Title
        Description = $Description
        Controls = @($Controls)
        MaximumDurationMinutes = $MaximumDurationMinutes
        Risk = 'Critical'
        PlanKind = $PlanKind
        TargetId = $TargetId
        SourceRecords = @($SourceRecords)
    }
}

function Get-WinCareLegacyUnsafeProfileDefinition {
    $compat = @('AdvertisingId', 'TailoredExperiences', 'ConsumerFeatures')
    @(
        New-WinCareLegacyProfileRecord -Id 'diagnostic-window' `
            -Title 'Time-boxed diagnostic security relaxation' `
            -Description 'Time-boxed diagnostic control reduction with recorded recovery.' `
            -Controls @('RealTimeProtection', 'FirewallPublic') `
            -SourceRecords @('SRC:1c7d9e2add') -MaximumDurationMinutes 30
        New-WinCareLegacyProfileRecord -Id 'winhance-maximum' `
            -Title 'Winhance maximum compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:1461504853')
        New-WinCareLegacyProfileRecord -Id 'windows-cleaner-utility-all' `
            -Title 'Windows Cleaner Utility bounded aggressive profile' `
            -Description 'Routes to bounded cleanup and excludes evidence deletion.' `
            -PlanKind DeepCleanup -TargetId 'windows-cleaner-utility-all' `
            -SourceRecords @('SRC:6e8ae24ac4')
        New-WinCareLegacyProfileRecord -Id 'remove-ms-store-apps-all' `
            -Title 'Remove removable Microsoft Store applications' `
            -Description 'Routes to the explicit removable-AppX selection provider.' `
            -PlanKind AppxSelection -TargetId 'remove-every-removable-appx' `
            -SourceRecords @('SRC:6e8ae24ac4')
        New-WinCareLegacyProfileRecord -Id 'windows-repo-nuclear' `
            -Title 'Windows repository nuclear compatibility profile' `
            -Description 'Constrained translation; destructive donor behaviour is excluded.' `
            -Controls $compat -SourceRecords @('SRC:6e8ae24ac4')
        New-WinCareLegacyProfileRecord -Id 'hardening-hailmary' `
            -Title 'Emergency containment hardening' `
            -Description 'Routes to the reviewed emergency hardening profile.' `
            -PlanKind Hardening -TargetId hailmary -SourceRecords @('SRC:1c7d9e2add')
        New-WinCareLegacyProfileRecord -Id 'rytunex-maximum' `
            -Title 'Rytunex maximum privacy translation' `
            -Description 'Routes to typed reversible maximum privacy rules.' `
            -PlanKind Privacy -TargetId maximum -SourceRecords @('SRC:5287f02b14')
        New-WinCareLegacyProfileRecord -Id 'privacy-sexy-maximum' `
            -Title 'Privacy Sexy maximum translation' `
            -Description 'Routes to typed reversible maximum privacy rules.' `
            -PlanKind Privacy -TargetId maximum -SourceRecords @('SRC:5287f02b14')
        New-WinCareLegacyProfileRecord -Id 'legacy-unsafe-all' `
            -Title 'Legacy Unsafe compatibility aggregate' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:legacy-unsafe-all')
        New-WinCareLegacyProfileRecord -Id 'reins-nuclear' `
            -Title 'REINS nuclear compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:reins-nuclear')
        New-WinCareLegacyProfileRecord -Id 'toolbox-aggressive' `
            -Title 'Toolbox aggressive compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:toolbox-aggressive')
        New-WinCareLegacyProfileRecord -Id 'et-gaming-extreme' `
            -Title 'ET gaming extreme compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:et-gaming-extreme')
        New-WinCareLegacyProfileRecord -Id 'pleasant-legacy' `
            -Title 'Pleasant legacy compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:pleasant-legacy')
        New-WinCareLegacyProfileRecord -Id 'xd-antispy-max' `
            -Title 'XD-AntiSpy maximum compatibility profile' `
            -Description 'Constrained translation to reviewed privacy controls.' `
            -Controls $compat -SourceRecords @('SRC:xd-antispy-max')
        New-WinCareLegacyProfileRecord -Id 'optimize-debloat-ultimate' `
        -Title 'Ultimate cleanup compatibility profile' `
        -Description 'Routes aggressive donor cleanup intent to the bounded reviewed deep-clean workflow.' `
        -PlanKind DeepCleanup -TargetId 'windows-cleaner-utility-all' `
        -SourceRecords @('SRC:6e8ae24ac4')
    New-WinCareLegacyProfileRecord -Id 'personalization-legacy' `
        -Title 'Legacy personalization compatibility profile' `
        -Description 'Routes legacy personalization intent to reviewed reversible appearance controls.' `
        -Controls @('AdvertisingId','TailoredExperiences','ConsumerFeatures') `
        -SourceRecords @('SRC:1c7d9e2add')
    )
}

function Get-WinCareLegacyUnsafeProfile {
    [CmdletBinding()]
    param([string]$Id = '')
    $profiles = @(Get-WinCareLegacyUnsafeProfileDefinition)
    if ($Id) { return @($profiles | Where-Object Id -eq $Id) }
    return $profiles
}

function New-WinCareLegacyControlProfilePlan {
    param(
        [Parameter(Mandatory)]$Profile,
        [int]$DurationMinutes,
        [string]$Reason
    )
    if ($Profile.MaximumDurationMinutes -gt 0 -and
        ($DurationMinutes -lt 1 -or $DurationMinutes -gt $Profile.MaximumDurationMinutes)) {
        throw "Duration must be between 1 and $($Profile.MaximumDurationMinutes) minutes."
    }
    $actions = [Collections.Generic.List[object]]::new()
    foreach ($control in $Profile.Controls) {
        $before = Get-WinCareLegacySecurityControlSnapshot -Control $control
        $parameters = @{
            Control = $control
            Enabled = $false
            BeforeHash = $before.Hash
            ProfileId = $Profile.Id
            DurationMinutes = $DurationMinutes
            Reason = $Reason
        }
        $compensator = @{
            Type = 'RestoreLegacySecurityControlState'
            Parameters = @{Control = $control; Snapshot = $before; ExpectedCurrentHash = ''}
            RequiresAdmin = $true
        }
        $actions.Add((New-WinCareBridgeAction -Type SetLegacySecurityControlState `
            -Label "Set legacy control $control" -Risk Critical -RequiresAdmin $true `
            -Reversible $true -Parameters $parameters -Compensator $compensator `
            -Verification 'The exact control state must be observed and recoverable.'))
    }
    New-WinCareBridgePlan -Title $Profile.Title -Description $Profile.Description `
        -Actions @($actions) -Metadata @{
            ProfileId = $Profile.Id
            DurationMinutes = $DurationMinutes
            Reason = $Reason
            ProvisionedAppxIncluded = $false
            Acknowledgement = 'I ACCEPT LEGACY UNSAFE MUTATIONS'
        }
}

function New-WinCareLegacyUnsafeProfilePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ProfileId,
        [int]$DurationMinutes = 15,
        [string]$Reason = 'Explicitly approved diagnostic operation',
        [switch]$IncludeProvisionedAppx
    )
    if ($IncludeProvisionedAppx) {
        throw 'Provisioned AppX removal requires the explicit AppX workflow.'
    }
    $profile = Get-WinCareLegacyUnsafeProfile -Id $ProfileId | Select-Object -First 1
    if (-not $profile) { throw "Unknown legacy profile: $ProfileId" }
    switch ($profile.PlanKind) {
        'DeepCleanup' { return New-WinCareDeepCleanupPlan -ProfileId $profile.TargetId }
        'AppxSelection' { return New-WinCareAppxSelectionPlan -ProfileId $profile.TargetId }
        'Hardening' { return New-WinCareHardeningProfilePlan -ProfileId $profile.TargetId }
        'Privacy' { return New-WinCarePrivacyProfilePlan -ProfileId $profile.TargetId }
        default {
            return New-WinCareLegacyControlProfilePlan -Profile $profile `
                -DurationMinutes $DurationMinutes -Reason $Reason
        }
    }
}
