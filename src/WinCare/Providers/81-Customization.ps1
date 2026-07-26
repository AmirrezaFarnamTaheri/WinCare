function Get-WinCareContextMenuCatalogPath { Join-Path $script:WinCareModuleRoot 'Data\Catalog\context-menu-rules.json' }
function Get-WinCareCustomizationProfilePath { Join-Path $script:WinCareModuleRoot 'Data\Catalog\customization-profiles.json' }

function Test-WinCareContextMenuRule {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Collections.IDictionary]$Rule)
    $null=Test-WinCareStrictObjectKeys -InputObject $Rule -AllowedKeys @('id','title','description','risk','requiresAdmin','minimumBuild','maximumBuild','requiredExecutables','sourceRecords','trees') -Context 'context-menu rule'
    foreach($required in @('id','title','description','risk','requiresAdmin','minimumBuild','requiredExecutables','sourceRecords','trees')){if(-not $Rule.ContainsKey($required)){throw "Context-menu rule is missing $required."}}
    if([string]$Rule.id -notmatch '^[a-z0-9][a-z0-9.-]{2,80}$'){throw 'Context-menu rule ID is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$Rule.title) -or ([string]$Rule.title).Length -gt 160){throw "Context-menu rule title is invalid: $($Rule.id)"}
    if(([string]$Rule.description).Length -gt 2000){throw "Context-menu rule description is too long: $($Rule.id)"}
    if([string]$Rule.risk -notin @('Moderate','High','Critical')){throw "Context-menu rule risk is invalid: $($Rule.id)"}
    if($Rule.requiresAdmin -isnot [bool]){throw "Context-menu requiresAdmin must be Boolean: $($Rule.id)"}
    $minimum=[int]$Rule.minimumBuild;$maximum=if($Rule.ContainsKey('maximumBuild')){[int]$Rule.maximumBuild}else{0}
    if($minimum -lt 0 -or $minimum -gt 999999 -or $maximum -lt 0 -or $maximum -gt 999999 -or ($maximum -gt 0 -and $maximum -lt $minimum)){throw "Context-menu build range is invalid: $($Rule.id)"}
    if($Rule.requiredExecutables -is [string] -or $Rule.requiredExecutables -isnot [Collections.IEnumerable]){throw "Context-menu requiredExecutables must be an array: $($Rule.id)"}
    $executables=@($Rule.requiredExecutables);if($executables.Count -gt 32){throw "Context-menu rule declares too many executables: $($Rule.id)"}
    $seenExecutables=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($name in $executables){if([string]$name -notmatch '^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}\.exe$' -or -not $seenExecutables.Add([string]$name)){throw "Context-menu executable name is invalid or duplicated: $name"}}
    if($Rule.sourceRecords -is [string] -or $Rule.sourceRecords -isnot [Collections.IEnumerable]){throw "Context-menu sourceRecords must be an array: $($Rule.id)"}
    $records=@($Rule.sourceRecords);if($records.Count -lt 1 -or $records.Count -gt 64){throw "Context-menu rule must declare 1..64 source records: $($Rule.id)"}
    $seenRecords=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);foreach($record in $records){if([string]::IsNullOrWhiteSpace([string]$record) -or ([string]$record).Length -gt 160 -or -not $seenRecords.Add([string]$record)){throw "Context-menu source record is invalid or duplicated: $record"}}
    if($Rule.trees -is [string] -or $Rule.trees -isnot [Collections.IEnumerable]){throw "Context-menu trees must be an array: $($Rule.id)"}
    $trees=@($Rule.trees);if($trees.Count -lt 1 -or $trees.Count -gt 32){throw "Context-menu rule must contain 1..32 registry trees: $($Rule.id)"}
    $treePaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($tree in $trees){
        $null=Test-WinCareStrictObjectKeys -InputObject $tree -AllowedKeys @('path','values','deleteValues','deleteSubKeys') -Context "context-menu tree $($Rule.id)"
        foreach($required in @('path','values','deleteValues','deleteSubKeys')){if(-not $tree.ContainsKey($required)){throw "Context-menu tree is missing ${required}: $($Rule.id)"}}
        $path=[string]$tree.path
        if($path -notmatch '^(HKCU|HKLM):\\Software\\(Classes|Microsoft\\Windows\\CurrentVersion\\Explorer)\\'){throw "Context-menu tree path is outside approved roots: $path"}
        if(-not $treePaths.Add($path)){throw "Context-menu rule contains a duplicate tree path: $path"}
        if($path -match '^HKLM:' -and -not [bool]$Rule.requiresAdmin){throw "Machine context-menu rule must require elevation: $($Rule.id)"}
        foreach($field in @('values','deleteValues','deleteSubKeys')){if($tree[$field] -is [string] -or $tree[$field] -isnot [Collections.IEnumerable]){throw "Context-menu tree field $field must be an array: $($Rule.id)"}}
        if(@($tree.values).Count -gt 128 -or @($tree.deleteValues).Count -gt 128 -or @($tree.deleteSubKeys).Count -gt 64){throw "Context-menu tree is too large: $($Rule.id)"}
        $valueNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($value in @($tree.values)){
            $null=Test-WinCareStrictObjectKeys -InputObject $value -AllowedKeys @('name','value','valueType') -Context "context-menu value $($Rule.id)"
            foreach($required in @('name','value','valueType')){if(-not $value.ContainsKey($required)){throw "Context-menu value is missing ${required}: $($Rule.id)"}}
            $name=[string]$value.name;if($name.Length -gt 260 -or $name -match '[\\/]' -or $name -match "`0" -or -not $valueNames.Add($name)){throw "Context-menu value name is invalid or duplicated: $($Rule.id)"}
            $type=[string]$value.valueType;if($type -notin @('String','ExpandString','Binary','DWord','MultiString','QWord')){throw "Context-menu value type is invalid: $($Rule.id)"}
            switch($type){
                {$_ -in @('String','ExpandString')} {if(([string]$value.value).Length -gt 32768 -or [string]$value.value -match "`0"){throw "Context-menu string value is invalid: $($Rule.id)"}}
                'Binary' {if($value.value -is [string]){try{$bytes=[Convert]::FromBase64String([string]$value.value)}catch{throw "Context-menu binary value must be base64: $($Rule.id)"};if($bytes.Length -gt 65536){throw "Context-menu binary value is too large: $($Rule.id)"}}elseif($value.value -isnot [Collections.IEnumerable]){throw "Context-menu binary value is invalid: $($Rule.id)"}}
                'DWord' {try{$number=[uint32]$value.value}catch{throw "Context-menu DWord value is invalid: $($Rule.id)"}}
                'QWord' {try{$number=[uint64]$value.value}catch{throw "Context-menu QWord value is invalid: $($Rule.id)"}}
                'MultiString' {if($value.value -is [string] -or $value.value -isnot [Collections.IEnumerable] -or @($value.value).Count -gt 256 -or @($value.value|Where-Object{$_ -isnot [string] -or ([string]$_).Length -gt 32768 -or [string]$_ -match "`0"}).Count){throw "Context-menu MultiString value is invalid: $($Rule.id)"}}
            }
        }
        $deletedValues=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($name in @($tree.deleteValues)){if([string]$name -match '[\\/]' -or ([string]$name).Length -gt 260 -or -not $deletedValues.Add([string]$name) -or $valueNames.Contains([string]$name)){throw "Context-menu deleted value is invalid, duplicated, or also assigned: $($Rule.id)"}}
        $deletedSubkeys=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($name in @($tree.deleteSubKeys)){if([string]$name -notmatch '^[^\\/:*?"<>|]{1,260}$' -or -not $deletedSubkeys.Add([string]$name)){throw "Context-menu deleted subkey is invalid or duplicated: $($Rule.id)"}}
    }
    return $true
}

function Get-WinCareContextMenuRule {
    [CmdletBinding()]param([string]$Id)
    $document=Read-WinCareJsonHashtable -LiteralPath (Get-WinCareContextMenuCatalogPath)
    $null=Test-WinCareStrictObjectKeys -InputObject $document -AllowedKeys @('schemaVersion','rules') -Context 'context-menu catalog'
    if([int]$document.schemaVersion -ne 1){throw 'Unsupported context-menu catalog schema.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($rule in @($document.rules)){$null=Test-WinCareContextMenuRule -Rule $rule;if(-not $seen.Add([string]$rule.id)){throw "Duplicate context-menu rule: $($rule.id)"}}
    if($Id){return @($document.rules|Where-Object id -eq $Id|Select-Object -First 1)}
    return @($document.rules)
}

function Test-WinCareNamedPolicySelection {
    param([string]$Id,[string]$AllowedName,[string]$DeniedName)
    $allowed=@(Get-WinCarePolicy $AllowedName);$denied=@(Get-WinCarePolicy $DeniedName)
    if($Id -in $denied){return $false}
    return '*' -in $allowed -or $Id -in $allowed
}

function Resolve-WinCareRequiredExecutable {
    param([Parameter(Mandatory)][string]$Name)
    try { return [string](Resolve-WinCareProcessExecutablePath -FilePath $Name).Path } catch { }
    $candidates=switch($Name.ToLowerInvariant()){
        'mpcmdrun.exe' {@(Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe';Join-Path $env:ProgramData 'Microsoft\Windows Defender\Platform\*\MpCmdRun.exe')}
        'code.exe' {@(Join-Path $env:LOCALAPPDATA 'Programs\Microsoft VS Code\Code.exe';Join-Path $env:ProgramFiles 'Microsoft VS Code\Code.exe')}
        'git-bash.exe' {@(Join-Path $env:ProgramFiles 'Git\git-bash.exe';Join-Path ${env:ProgramFiles(x86)} 'Git\git-bash.exe')}
        'vlc.exe' {@(Join-Path $env:ProgramFiles 'VideoLAN\VLC\vlc.exe';Join-Path ${env:ProgramFiles(x86)} 'VideoLAN\VLC\vlc.exe')}
        default {@()}
    }
    foreach($candidate in @($candidates)){
        $paths=if($candidate.Contains('*')){@(Get-ChildItem -Path $candidate -File -ErrorAction SilentlyContinue|Sort-Object FullName -Descending|ForEach-Object FullName)}else{@($candidate)}
        foreach($path in $paths){
            if(-not $path){continue}
            try { return [string](Resolve-WinCareProcessExecutablePath -FilePath $path).Path } catch { }
        }
    }
    return $null
}

function ConvertTo-WinCareContextMenuRegistryValue {
    param(
        [Parameter(Mandatory)][object]$Value,
        [Parameter(Mandatory)][Collections.IDictionary]$Executables
    )
    $text=[string]$Value
    foreach($name in $Executables.Keys){
        $resolved=[string]$Executables[$name]
        if([string]::IsNullOrWhiteSpace($resolved)){continue}
        if([string]::Equals($text,[string]$name,[StringComparison]::OrdinalIgnoreCase)){return $resolved}
        $prefix=[string]$name+' '
        if($text.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){
            return '"'+$resolved+'"'+$text.Substring(([string]$name).Length)
        }
    }
    return $Value
}

function Test-WinCareContextMenuRuleCompatibility {
    param([Parameter(Mandatory)][object]$Rule)
    $build=try{[int](Get-CimInstance Win32_OperatingSystem -ErrorAction Stop).BuildNumber}catch{0}
    if($Rule.minimumBuild -and $build -lt [int]$Rule.minimumBuild){return [pscustomobject]@{Compatible=$false;Reason="Requires build $($Rule.minimumBuild) or newer.";Executables=@{}}}
    if($Rule.maximumBuild -and $build -gt [int]$Rule.maximumBuild){return [pscustomobject]@{Compatible=$false;Reason="Supports builds through $($Rule.maximumBuild).";Executables=@{}}}
    $executables=@{};$missing=[Collections.Generic.List[string]]::new()
    foreach($name in @($Rule.requiredExecutables)){$resolved=Resolve-WinCareRequiredExecutable -Name ([string]$name);$executables[[string]$name]=$resolved;if(-not $resolved){$missing.Add([string]$name)}}
    [pscustomobject]@{Compatible=$missing.Count -eq 0;Reason=if($missing.Count){"Missing executable(s): $($missing -join ', ')"}else{'Compatible'};Executables=$executables}
}

function Get-WinCareContextMenuRuleState {
    [CmdletBinding()]param([string]$Id)
    $rules=if($Id){Get-WinCareContextMenuRule -Id $Id}else{Get-WinCareContextMenuRule}
    foreach($rule in @($rules)){
        $compat=Test-WinCareContextMenuRuleCompatibility -Rule $rule
        $treeStates=[Collections.Generic.List[object]]::new();$present=0;$matching=0
        foreach($tree in @($rule.trees)){
            $snapshot=Get-WinCareRegistryTreeSnapshot -Path ([string]$tree.path)
            if($snapshot.Exists){$present++}
            $expected=@{}
            foreach($value in @($tree.values)){
                $materialized=ConvertTo-WinCareContextMenuRegistryValue `
                    -Value $value.value -Executables $compat.Executables
                $expected[[string]$value.name]=@{
                    name=[string]$value.name
                    value=$materialized
                    valueType=[string]$value.valueType
                }
            }
            $actual=@{};foreach($value in @($snapshot.Values)){$actual[[string]$value.Name]=$value}
            $treeMatch=[bool]$snapshot.Exists
            foreach($name in $expected.Keys){
                if(-not $actual.ContainsKey($name) -or [string]$actual[$name].Value -ne [string]$expected[$name].value -or [string]$actual[$name].ValueType -ne [string]$expected[$name].valueType){$treeMatch=$false;break}
            }
            if($treeMatch){$matching++}
            $treeStates.Add([pscustomobject]@{Path=$tree.path;Exists=[bool]$snapshot.Exists;Matches=$treeMatch;SnapshotHash=Get-WinCareRegistryTreeSnapshotHash $snapshot})
        }
        [pscustomobject]@{Id=$rule.id;Title=$rule.title;Description=$rule.description;Risk=$rule.risk;Enabled=$matching -eq @($rule.trees).Count;Drifted=$present -gt 0 -and $matching -ne @($rule.trees).Count;Compatible=$compat.Compatible;CompatibilityReason=$compat.Reason;Trees=@($treeStates);SourceRecords=@($rule.sourceRecords)}
    }
}

function New-WinCareContextMenuPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$RuleId,[Parameter(Mandatory)][bool]$Enable)
    $rule=Get-WinCareContextMenuRule -Id $RuleId|Select-Object -First 1
    if(-not $rule){throw "Context-menu rule was not found: $RuleId"}
    if(-not(Test-WinCareNamedPolicySelection -Id $RuleId -AllowedName AllowedContextMenuRuleIds -DeniedName DeniedContextMenuRuleIds)){throw "Context-menu rule is blocked by policy: $RuleId"}
    $compat=Test-WinCareContextMenuRuleCompatibility -Rule $rule
    if($Enable -and -not $compat.Compatible){throw $compat.Reason}
    $plan=New-WinCarePlan -Title "$(if($Enable){'Enable'}else{'Disable'}) context-menu rule: $($rule.title)" -Description $rule.description -SourceRecords @($rule.sourceRecords) -Metadata @{ContextMenuRuleId=$RuleId;Enable=$Enable}
    $trees=if($Enable){@($rule.trees)}else{@($rule.trees|Sort-Object {([string]$_.path).Length} -Descending)}
    foreach($tree in $trees){
        $path=[string]$tree.path;$before=Get-WinCareRegistryTreeSnapshot -Path $path;$admin=$path -match '^HKLM:'
        if($Enable){
            $values=@($tree.values|ForEach-Object{
                $materialized=ConvertTo-WinCareContextMenuRegistryValue `
                    -Value $_.value -Executables $compat.Executables
                @{Name=[string]$_.name;Value=$materialized;ValueType=[string]$_.valueType}
            })
            $action=New-WinCareAction -Type ApplyRegistryTree -Label "Apply $($rule.title): $path" -Risk ([string]$rule.risk) -Parameters @{Path=$path;Values=$values;DeleteValues=@($tree.deleteValues);DeleteSubKeys=@($tree.deleteSubKeys);ExpectedSnapshotHash=Get-WinCareRegistryTreeSnapshotHash $before} -RequiresAdmin:$admin -Reversible $true -Tags @('ContextMenu','RegistryTree','Idempotent','DynamicCompensator') -SourceRecords @($rule.sourceRecords) -RecoveryDescription 'Restore the exact previous registry tree.'
        }else{
            if(-not $before.Exists){continue}
            $action=New-WinCareAction -Type RemoveRegistryTree -Label "Remove $($rule.title): $path" -Risk High -Parameters @{Path=$path;ExpectedSnapshotHash=Get-WinCareRegistryTreeSnapshotHash $before} -RequiresAdmin:$admin -Reversible $true -Compensator @{Type='RestoreRegistryTree';RequiresAdmin=$admin;Parameters=@{Path=$path;Snapshot=$before;ExpectedCurrentHash=Get-WinCareRegistryTreeSnapshotHash ([ordered]@{SchemaVersion=1;Exists=$false;Values=@();SubKeys=@()})}} -Tags @('ContextMenu','RegistryTree') -SourceRecords @($rule.sourceRecords) -RecoveryDescription 'Restore the exact removed registry tree.'
        }
        $null=Add-WinCarePlanAction -Plan $plan -Action $action
    }
    return $plan
}

function Test-WinCareModernContextMenuDefinition {
    [CmdletBinding()]param([Parameter(Mandatory)][Collections.IDictionary]$Definition)
    $allowed=@('title','exe','param','icon','iconDark','acceptDirectory','acceptDirectoryFlag','acceptFile','acceptFileFlag','acceptExts','acceptFileRegex','acceptMultipleFilesFlag','pathDelimiter','paramForMultipleFiles','index','showWindowFlag','runAsFlag','workingDirectory')
    $null=Test-WinCareStrictObjectKeys -InputObject $Definition -AllowedKeys $allowed -Context 'modern context-menu definition'
    if([string]::IsNullOrWhiteSpace([string]$Definition.title) -or ([string]$Definition.title).Length -gt 120){throw 'Modern menu title is invalid.'}
    if([string]::IsNullOrWhiteSpace([string]$Definition.exe) -or ([string]$Definition.exe).Length -gt 2048){throw 'Modern menu executable is invalid.'}
    if($Definition.ContainsKey('acceptDirectoryFlag') -and ([int]$Definition.acceptDirectoryFlag -lt 0 -or [int]$Definition.acceptDirectoryFlag -gt 15)){throw 'acceptDirectoryFlag is outside 0..15.'}
    if($Definition.ContainsKey('acceptFileFlag') -and [int]$Definition.acceptFileFlag -notin @(0,1,2,3,4)){throw 'acceptFileFlag is invalid.'}
    if($Definition.ContainsKey('acceptMultipleFilesFlag') -and [int]$Definition.acceptMultipleFilesFlag -notin @(0,1,2)){throw 'acceptMultipleFilesFlag is invalid.'}
    if($Definition.ContainsKey('showWindowFlag') -and [int]$Definition.showWindowFlag -notin @(-1,0,1,2)){throw 'showWindowFlag is invalid.'}
    if($Definition.ContainsKey('runAsFlag') -and [int]$Definition.runAsFlag -notin @(0,1)){throw 'runAsFlag is invalid.'}
    if([int](Get-WinCarePropertyValue $Definition 'acceptFileFlag' 4) -eq 3 -and [string](Get-WinCarePropertyValue $Definition 'acceptExts' '') -notmatch '^\.[A-Za-z0-9]+(\|\.[A-Za-z0-9]+)*$'){throw 'Exact extension list is invalid.'}
    if([int](Get-WinCarePropertyValue $Definition 'acceptFileFlag' 4) -eq 2){try{$null=[regex]::new([string]$Definition.acceptFileRegex,[Text.RegularExpressions.RegexOptions]::CultureInvariant,[timespan]::FromMilliseconds(200))}catch{throw 'Filename regex is invalid or unsafe.'}}
    foreach($field in @('param','paramForMultipleFiles','workingDirectory')){
        $text=[string](Get-WinCarePropertyValue $Definition $field '')
        $tokens=[regex]::Matches($text,'\{[^}]+\}')|ForEach-Object Value|Select-Object -Unique
        $unknown=@($tokens|Where-Object{$_ -notin @('{path}','{parent}','{name}','{nameNoExt}','{extension}','{path0}','{name0}','{nameNoExt0}','{extension0}')})
        if($unknown.Count){throw "Unknown template token(s) in ${field}: $($unknown -join ', ')"}
    }
    return $true
}

function Import-WinCareModernContextMenuDefinition {
    [CmdletBinding()]param([Parameter(Mandatory)][string]$LiteralPath)
    $path=Assert-WinCareSafePath -LiteralPath $LiteralPath
    if([IO.Path]::GetExtension($path) -ne '.json'){throw 'Modern context-menu definition must be JSON.'}
    $document=Read-WinCareJsonHashtable -LiteralPath $path
    $definitions=if($document -is [Collections.IDictionary] -and $document.ContainsKey('menus')){@($document.menus)}elseif($document -is [Collections.IEnumerable] -and $document -isnot [string]){@($document)}else{@($document)}
    if($definitions.Count -lt 1 -or $definitions.Count -gt 256){throw 'Modern context-menu file must contain 1..256 definitions.'}
    foreach($definition in $definitions){$null=Test-WinCareModernContextMenuDefinition -Definition $definition}
    return @($definitions)
}

function Get-WinCareCustomizationHost {
    [CmdletBinding()]param()
    $known=@(
        @{Id='windhawk';Name='Windhawk';Processes=@('windhawk');Paths=@((Join-Path $env:LOCALAPPDATA 'Programs\Windhawk'),(Join-Path $env:ProgramFiles 'Windhawk'));SourceRecords=@('SRC:f8045859ed')},
        @{Id='explorerpatcher';Name='ExplorerPatcher';Processes=@('ep_setup','ExplorerPatcher');Paths=@((Join-Path $env:ProgramFiles 'ExplorerPatcher'),(Join-Path $env:LOCALAPPDATA 'ExplorerPatcher'));SourceRecords=@('SRC:f8045859ed')},
        @{Id='rainmeter';Name='Rainmeter';Processes=@('Rainmeter');Paths=@((Join-Path $env:ProgramFiles 'Rainmeter'),(Join-Path ${env:ProgramFiles(x86)} 'Rainmeter'));SourceRecords=@('SRC:f8045859ed')},
        @{Id='contextmenuforwindows11';Name='ContextMenuForWindows11';Processes=@('ContextMenuCustom');Paths=@();AppxNames=@('f830d6cf-c83d-48b1-a9f6-95927d5f5b1c','852637cb-5c5e-4695-93cd-d2b0ca79f6de','31896935-DD54-4362-ACDD-6DA496624671','7061touchwp.CustomContextMenu');SourceRecords=@('SRC:f8045859ed')},
        @{Id='airpodsdesktop';Name='AirPods Desktop';Processes=@('AirPodsDesktop');Paths=@((Join-Path $env:LOCALAPPDATA 'Programs\AirPodsDesktop'));SourceRecords=@('SRC:f8045859ed')}
    )
    foreach($hostDef in $known){
        $processes=@(Get-Process -ErrorAction SilentlyContinue|Where-Object ProcessName -in $hostDef.Processes|Select-Object ProcessName,Id,Path)
        $paths=@($hostDef.Paths|Where-Object{$_ -and (Test-Path -LiteralPath $_)})
        $packages=if($hostDef.AppxNames -and (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue)){@(Get-AppxPackage -ErrorAction SilentlyContinue|Where-Object Name -in @($hostDef.AppxNames)|Select-Object Name,PackageFullName,InstallLocation)}else{@()}
        [pscustomobject]@{Id=$hostDef.Id;Name=$hostDef.Name;Installed=$paths.Count -gt 0 -or $processes.Count -gt 0 -or $packages.Count -gt 0;Running=$processes.Count -gt 0;Paths=$paths;Packages=$packages;Processes=$processes;Mode=[string](Get-WinCareConfig ExternalCustomizerMode);Writable=[string](Get-WinCareConfig ExternalCustomizerMode) -eq 'Managed' -and [bool](Get-WinCarePolicy AllowExternalCustomizerWrites);SourceRecords=$hostDef.SourceRecords}
    }
}

function Get-WinCareRainmeterSkinInventory {
    [CmdletBinding()]param()
    $root=Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Rainmeter\Skins'
    if(-not(Test-Path -LiteralPath $root -PathType Container)){return @()}
    foreach($ini in @(Get-ChildItem -LiteralPath $root -Filter '*.ini' -File -Recurse -ErrorAction SilentlyContinue|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0 -and $_.Length -le 1048576}|Sort-Object FullName|Select-Object -First 4096)){
        $relative=[IO.Path]::GetRelativePath($root,$ini.FullName).Replace('\','/');$content=try{Read-WinCareBoundedText -LiteralPath $ini.FullName -MaximumBytes 1048576}catch{''}
        $sections=@([regex]::Matches([string]$content,'(?m)^\s*\[([^\]]+)\]\s*$')|ForEach-Object{$_.Groups[1].Value})
        $measures=@([regex]::Matches([string]$content,'(?im)^\s*Measure\s*=\s*([^\r\n;]+)')|ForEach-Object{$_.Groups[1].Value.Trim()}|Select-Object -Unique)
        $meters=@([regex]::Matches([string]$content,'(?im)^\s*Meter\s*=\s*([^\r\n;]+)')|ForEach-Object{$_.Groups[1].Value.Trim()}|Select-Object -Unique)
        [pscustomobject]@{Path=$ini.FullName;RelativePath=$relative;Sha256=Get-WinCareSha256 $ini.FullName;Size=$ini.Length;Sections=$sections;Measures=$measures;Meters=$meters;SourceRecord='SRC:f8045859ed'}
    }
}

function Get-WinCareWindhawkModInventory {
    [CmdletBinding()]param()
    $roots=@((Join-Path $env:LOCALAPPDATA 'Programs\Windhawk\Mods'),(Join-Path $env:ProgramData 'Windhawk\Mods'))|Where-Object{$_ -and (Test-Path -LiteralPath $_ -PathType Container)}
    foreach($root in $roots){
        foreach($file in @(Get-ChildItem -LiteralPath $root -Filter '*.wh.cpp' -File -ErrorAction SilentlyContinue|Where-Object{($_.Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0 -and $_.Length -le 2097152}|Sort-Object Name|Select-Object -First 4096)){
            $text=try{Read-WinCareBoundedText -LiteralPath $file.FullName -MaximumBytes 2097152}catch{''}
            $meta=@{};foreach($match in [regex]::Matches([string]$text,'(?m)^//\s*==WindhawkMod(?<body>.*?)^//\s*==/WindhawkMod==',[Text.RegularExpressions.RegexOptions]::Singleline)){foreach($line in $match.Groups['body'].Value -split "`r?`n"){if($line -match '^//\s*@(?<key>[A-Za-z0-9_-]+)\s+(?<value>.+)$'){$meta[$Matches.key]=$Matches.value.Trim()}}}
            [pscustomobject]@{Id=[IO.Path]::GetFileNameWithoutExtension($file.Name);Path=$file.FullName;Sha256=Get-WinCareSha256 $file.FullName;Name=[string](Get-WinCarePropertyValue $meta 'name' $file.BaseName);Version=[string](Get-WinCarePropertyValue $meta 'version' '');Author=[string](Get-WinCarePropertyValue $meta 'author' '');Description=[string](Get-WinCarePropertyValue $meta 'description' '');SourceRecord='SRC:f8045859ed'}
        }
    }
}

function Get-WinCareExplorerPatcherState {
    [CmdletBinding()]param()
    $path='HKCU:\Software\ExplorerPatcher';$snapshot=Get-WinCareRegistryTreeSnapshot -Path $path -MaximumDepth 5 -MaximumEntries 1024
    [pscustomobject]@{Installed=[bool]$snapshot.Exists;RegistryPath=$path;SnapshotHash=Get-WinCareRegistryTreeSnapshotHash $snapshot;Snapshot=$snapshot;SourceRecords=@('SRC:f8045859ed')}
}




