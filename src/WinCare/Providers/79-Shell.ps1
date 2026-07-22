function Get-WinCareShellExtension {
    [CmdletBinding()]param()
    $roots=@(
        'Registry::HKEY_CURRENT_USER\Software\Classes\*\shellex\ContextMenuHandlers',
        'Registry::HKEY_LOCAL_MACHINE\Software\Classes\*\shellex\ContextMenuHandlers',
        'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\shellex\ContextMenuHandlers',
        'Registry::HKEY_LOCAL_MACHINE\Software\Classes\Directory\shellex\ContextMenuHandlers',
        'Registry::HKEY_CURRENT_USER\Software\Classes\Directory\Background\shell',
        'Registry::HKEY_LOCAL_MACHINE\Software\Classes\Directory\Background\shell'
    )
    $items=[Collections.Generic.List[object]]::new()
    foreach($root in $roots){if(-not(Test-Path -LiteralPath $root)){continue};foreach($key in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue){$value=(Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue).'(default)';$disabled=$key.PSChildName -like '*.WinCareDisabled';$items.Add([pscustomobject]@{Name=$key.PSChildName;Path=$key.PSPath;RegistryPath=$key.Name;Value=$value;Scope=if($key.Name -like 'HKEY_CURRENT_USER*'){'User'}else{'Machine'};Enabled=(-not $disabled);RequiresAdmin=$key.Name -like 'HKEY_LOCAL_MACHINE*'})}}
    @($items)
}

function New-WinCareShellExtensionPlan {
    [CmdletBinding()]param([Parameter(Mandatory)][object]$Extension,[Parameter(Mandatory)][bool]$Enabled)
    $action=New-WinCareAction -Type SetShellExtensionState -Label "$(if($Enabled){'Enable'}else{'Disable'}) shell entry $($Extension.Name)" -Risk Moderate -Parameters @{RegistryPath=$Extension.RegistryPath;Enabled=$Enabled} -RequiresAdmin ([bool]$Extension.RequiresAdmin) -Reversible $true -Compensator @{Type='SetShellExtensionState';RequiresAdmin=[bool]$Extension.RequiresAdmin;Parameters=@{RegistryPath=$Extension.RegistryPath;Enabled=(-not $Enabled)}} -Tags @('Shell','Idempotent') -SourceRecords @('D11-SHELL-UX','D06-CONTEXT-MENU')
    New-WinCarePlan -Title 'Change shell extension state' -Actions @($action) -SourceRecords @('D11-SHELL-UX','D06-CONTEXT-MENU')
}

function Invoke-WinCareShellExtensionAction {
    param([object]$Action)
    $path=[string]$Action.Parameters.RegistryPath;$enabled=[bool]$Action.Parameters.Enabled
    if($path -notmatch '^HKEY_(CURRENT_USER|LOCAL_MACHINE)\\Software\\Classes\\'){return New-WinCareResult -Success $false -Message 'Shell entry is outside the allowed registry roots.' -ExitCode 5}
    $psPath='Registry::'+$path;$leaf=Split-Path $psPath -Leaf;$parent=Split-Path $psPath -Parent
    if($enabled){if($leaf -notlike '*.WinCareDisabled'){return New-WinCareResult -Success $true -Code 'AlreadyEnabled' -Message 'Shell entry is already enabled.'};$newName=$leaf.Substring(0,$leaf.Length-16)}else{if($leaf -like '*.WinCareDisabled'){return New-WinCareResult -Success $true -Code 'AlreadyDisabled' -Message 'Shell entry is already disabled.'};$newName=$leaf+'.WinCareDisabled'}
    $destination=Join-Path $parent $newName;if(Test-Path -LiteralPath $destination){return New-WinCareResult -Success $false -Message 'Destination registry key already exists.' -ExitCode 17}
    Rename-Item -LiteralPath $psPath -NewName $newName -ErrorAction Stop
    New-WinCareResult -Success (Test-Path -LiteralPath $destination) -Message 'Shell entry state updated.' -Data @{OldPath=$path;NewName=$newName;Enabled=$enabled}
}

function Get-WinCareDesktopShortcut {
    [CmdletBinding()]param()
    $roots=@([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('CommonDesktopDirectory'))|Where-Object{$_ -and (Test-Path -LiteralPath $_)}|Sort-Object -Unique
    @($roots|ForEach-Object{Get-ChildItem -LiteralPath $_ -File -ErrorAction SilentlyContinue|Where-Object Extension -in @('.lnk','.url')|ForEach-Object{[pscustomobject]@{Name=$_.BaseName;Path=$_.FullName;Scope=if(Test-WinCarePathWithin -Child $_.FullName -Parent ([Environment]::GetFolderPath('CommonDesktopDirectory'))){'Public'}else{'User'};Extension=$_.Extension;Bytes=$_.Length;LastWriteTime=$_.LastWriteTimeUtc}}})
}


