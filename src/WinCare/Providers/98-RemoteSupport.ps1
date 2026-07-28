function Get-WinCareBoundedRemoteSupportConfigFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Root,
        [ValidateRange(1,5000)][int]$MaximumFiles=500,
        [ValidateRange(1,32)][int]$MaximumDepth=8,
        [ValidateRange(1,10000)][int]$MaximumDirectories=1000
    )
    if(-not(Test-Path -LiteralPath $Root -PathType Container)){return @()}
    $safeRoot=Assert-WinCareSafePath -LiteralPath $Root -AllowedRoots @($Root)
    $rootItem=Get-Item -LiteralPath $safeRoot -Force -ErrorAction Stop
    if(($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Remote-support configuration root is a reparse point: $safeRoot"}
    $queue=[Collections.Generic.Queue[object]]::new()
    $queue.Enqueue([pscustomobject]@{Path=$safeRoot;Depth=0})
    $files=[Collections.Generic.List[object]]::new();$visitedDirectories=0
    while($queue.Count -gt 0 -and $files.Count -lt $MaximumFiles){
        $entry=$queue.Dequeue();$visitedDirectories++
        if($visitedDirectories -gt $MaximumDirectories){break}
        try{$children=@(Get-ChildItem -LiteralPath $entry.Path -Force -ErrorAction Stop)}catch{continue}
        foreach($child in $children|Sort-Object FullName){
            if(($child.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){continue}
            if($child.PSIsContainer){
                if([int]$entry.Depth -lt $MaximumDepth){
                    try{$directoryPath=Assert-WinCareSafePath -LiteralPath $child.FullName -AllowedRoots @($safeRoot);$queue.Enqueue([pscustomobject]@{Path=$directoryPath;Depth=[int]$entry.Depth+1})}catch{}
                }
                continue
            }
            try{$filePath=Assert-WinCareSafePath -LiteralPath $child.FullName -AllowedRoots @($safeRoot);$files.Add([pscustomobject]@{Path=$filePath;Length=[long]$child.Length;Extension=[string]$child.Extension})}catch{}
            if($files.Count -ge $MaximumFiles){break}
        }
    }
    @($files)
}

function Get-WinCareRemoteSupportCatalog {
    @(
        [pscustomobject]@{Id='rustdesk';Name='RustDesk';ProcessNames=@('rustdesk');ServicePatterns=@('RustDesk*');AppPattern='(?i)rustdesk';ConfigRoots=@("$env:APPDATA\RustDesk","$env:ProgramData\RustDesk")},
        [pscustomobject]@{Id='mousekeyproxy';Name='MouseKeyProxy';ProcessNames=@('MouseKeyProxy.Agent','MouseKeyProxy.Service','mkp');ServicePatterns=@('MouseKeyProxy*');AppPattern='(?i)mousekeyproxy';ConfigRoots=@("$env:LOCALAPPDATA\MouseKeyProxy","$env:ProgramData\MouseKeyProxy")},
        [pscustomobject]@{Id='anydesk';Name='AnyDesk';ProcessNames=@('AnyDesk','AnyDeskMSI');ServicePatterns=@('AnyDesk*');AppPattern='(?i)anydesk';ConfigRoots=@("$env:APPDATA\AnyDesk","$env:ProgramData\AnyDesk")},
        [pscustomobject]@{Id='teamviewer';Name='TeamViewer';ProcessNames=@('TeamViewer','TeamViewer_Service','TeamViewer_Desktop');ServicePatterns=@('TeamViewer*');AppPattern='(?i)teamviewer';ConfigRoots=@("$env:APPDATA\TeamViewer","$env:ProgramData\TeamViewer")},
        [pscustomobject]@{Id='quickassist';Name='Quick Assist';ProcessNames=@('QuickAssist');ServicePatterns=@();AppPattern='(?i)quick assist';ConfigRoots=@()}
    )
}
