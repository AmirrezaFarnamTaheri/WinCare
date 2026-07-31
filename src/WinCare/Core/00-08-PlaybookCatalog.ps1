#requires -Version 7.2

# Canonical playbook catalog, schema, and compatibility evaluation.

function Get-WinCarePlaybook {
    [CmdletBinding()]param([string]$Id='')
    $path=Join-Path $script:WinCareModuleRoot 'Data\Catalog\playbooks.json'
    $document=Read-WinCareJsonHashtable -LiteralPath $path
    $null=Test-WinCareStrictObjectKeys $document @('schemaVersion','playbooks') 'playbook catalog'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported playbook catalog schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $sourceSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    $playbooks=@($document.playbooks|ForEach-Object{
        $playbook=[pscustomobject]$_
        $null=Test-WinCarePlaybookDefinition $playbook
        $playbook|Add-Member -NotePropertyName SourcePath -NotePropertyValue $path -Force
        $playbook|Add-Member -NotePropertyName SourceSha256 -NotePropertyValue $sourceSha256 -Force
        $playbook
    })
    foreach($playbook in $playbooks){if(-not $seen.Add([string]$playbook.Id)){throw 'Duplicate playbook ID.'}}
    if($Id){return @($playbooks|Where-Object Id -eq $Id)}
    @($playbooks)
}

function Test-WinCarePlaybookDefinition {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Playbook)
    $p=ConvertTo-WinCareParameterDictionary $Playbook
    $null=Test-WinCareStrictObjectKeys -InputObject $p -AllowedKeys @('id',
        'title',
        'description',
        'minimumBuild',
        'maximumBuild',
        'architectures',
        'requiresAdmin',
        'sourceRecords',
        'steps',
        'SourcePath',
        'SourceSha256') -Context 'playbook'
    if([string]$p.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$' -or [string]::IsNullOrWhiteSpace([string]$p.title)){throw 'Playbook identity is invalid.'}
    if([int]$p.minimumBuild -lt 0 -or [int]$p.maximumBuild -lt [int]$p.minimumBuild){throw 'Playbook build range is invalid.'}
    if($p.requiresAdmin -isnot [bool]){throw 'Playbook requiresAdmin must be Boolean.'}
    $hasSourcePath=$p.ContainsKey('SourcePath');$hasSourceSha256=$p.ContainsKey('SourceSha256')
    if($hasSourcePath -xor $hasSourceSha256){throw 'Playbook source metadata is incomplete.'}
    if($hasSourcePath){
        if([string]::IsNullOrWhiteSpace([string]$p.SourcePath) -or ([string]$p.SourcePath).Length -gt 32760){throw 'Playbook source path is invalid.'}
        if([string]$p.SourceSha256 -notmatch '^[a-f0-9]{64}$'){throw 'Playbook source digest is invalid.'}
    }
    $architectures=@($p.architectures);if($architectures.Count -lt 1 -or @($architectures|Where-Object{[string]$_ -notin @('AMD64','ARM64')}).Count){throw 'Playbook architecture list is invalid.'}
    $steps=@($p.steps);if($steps.Count -lt 1 -or $steps.Count -gt 128){throw 'Playbook must contain 1..128 data-only steps.'}
    foreach($stepValue in $steps){
        $step=ConvertTo-WinCareParameterDictionary $stepValue;$kind=[string](Get-WinCarePropertyValue $step 'kind' '')
        if($kind -notin @('preset','catalog','context-menu','app-removal')){throw "Unsupported playbook step kind: $kind"}
        switch($kind){
            'preset'{$null=Test-WinCareStrictObjectKeys $step @('kind','id') 'playbook preset step';
                if([string]$step.id -notmatch '^[a-z0-9][a-z0-9._-]{2,80}$'){throw 'Playbook preset ID is invalid.'}}
            'catalog'{$null=Test-WinCareStrictObjectKeys $step @('kind','ids') 'playbook catalog step';if(@($step.ids).Count -lt 1){throw 'Playbook catalog step is empty.'}}
            'context-menu'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','enable') 'playbook context-menu step';
                if($step.enable -isnot [bool]){throw 'Playbook context-menu enable must be Boolean.'}}
            'app-removal'{$null=Test-WinCareStrictObjectKeys $step @('kind','id','includeProvisioned') 'playbook app-removal step';
                if($step.includeProvisioned -isnot [bool]){throw 'Playbook app-removal flag must be Boolean.'}}
        }
    }
    $true
}

function Test-WinCarePlaybookCompatibility {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Playbook)

    $null = Test-WinCarePlaybookDefinition -Playbook $Playbook
    $map = ConvertTo-WinCareParameterDictionary $Playbook
    if (-not $IsWindows) { return $false }

    $build = [Environment]::OSVersion.Version.Build
    $minimumBuild = [int](Get-WinCarePropertyValue $map 'minimumBuild' 0)
    $maximumBuild = [int](Get-WinCarePropertyValue $map 'maximumBuild' 0)
    if ($minimumBuild -gt 0 -and $build -lt $minimumBuild) { return $false }
    if ($maximumBuild -gt 0 -and $build -gt $maximumBuild) { return $false }

    $architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToUpperInvariant()
    if ($architecture -eq 'X64') { $architecture = 'AMD64' }
    return $architecture -in @($map.architectures)
}
