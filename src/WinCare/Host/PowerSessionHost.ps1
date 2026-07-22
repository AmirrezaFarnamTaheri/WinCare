#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ModulePath,
    [Parameter(Mandatory)][string]$SessionId,
    [Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{64}$')][string]$ExpectedModuleTreeHash
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if($SessionId -notmatch '^[a-f0-9]{32}$'){throw 'Invalid session ID.'}
function Assert-PowerSessionNoReparse {
    param([Parameter(Mandatory)][string]$LiteralPath)
    $item=Get-Item -LiteralPath $LiteralPath -Force -ErrorAction Stop
    while($item){
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Power-session host refused a reparse path: $($item.FullName)"}
        $parent=Split-Path -Parent $item.FullName
        if(-not $parent -or $parent -eq $item.FullName){break}
        $item=Get-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue
    }
}
function Get-PowerSessionModuleTreeHash {
    param([Parameter(Mandatory)][string]$Root)
    $files=Get-ChildItem -LiteralPath $Root -File -Recurse -ErrorAction Stop|Sort-Object FullName
    $builder=[Text.StringBuilder]::new()
    $sha=[Security.Cryptography.SHA256]::Create()
    try{
        foreach($file in $files){
            if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){throw "Power-session host refused a reparse member: $($file.FullName)"}
            $relative=[IO.Path]::GetRelativePath($Root,$file.FullName).Replace('\','/')
            $stream=[IO.File]::OpenRead($file.FullName)
            try{$hash=$sha.ComputeHash($stream)}finally{$stream.Dispose()}
            $null=$builder.Append($relative).Append(':').Append([Convert]::ToHexString($hash).ToLowerInvariant()).Append("`n")
        }
    }finally{$sha.Dispose()}
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($builder.ToString()))).ToLowerInvariant()
}

$manifest=(Resolve-Path -LiteralPath $ModulePath -ErrorAction Stop).Path
Assert-PowerSessionNoReparse -LiteralPath $manifest
$moduleRoot=Split-Path -Parent $manifest
Assert-PowerSessionNoReparse -LiteralPath $moduleRoot
$actual=Get-PowerSessionModuleTreeHash -Root $moduleRoot
if($actual -ne $ExpectedModuleTreeHash){throw 'Power-session host refused a module tree that changed after authorization.'}
Import-Module $manifest -Force -ErrorAction Stop
Initialize-WinCareState -SkipConfigSave
if((Get-PowerSessionModuleTreeHash -Root $moduleRoot) -ne $ExpectedModuleTreeHash){throw 'Power-session host module tree changed during import.'}
Invoke-WinCarePowerSessionHost -SessionId $SessionId
