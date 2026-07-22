#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [switch]$SkipTests,
    [switch]$AllowDirty
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'Invoke-StaticChecks.ps1') -Root $Root -RequirePSScriptAnalyzer -OutputPath (Join-Path $OutputDirectory 'powershell-static-validation.json')
& (Join-Path $PSScriptRoot 'Invoke-ConvergenceAudit.ps1') -Root $Root
if(-not $SkipTests){
    $pester=Get-Module -ListAvailable Pester|Where-Object Version -ge '5.5.0'|Sort-Object Version -Descending|Select-Object -First 1
    if(-not $pester){throw 'Pester 5.5 or later is required.'}
    Import-Module $pester -Force
    $result=Invoke-Pester -Path (Join-Path $Root 'tests') -PassThru -Output Detailed
    if($result.FailedCount -gt 0 -or $result.SkippedCount -gt 0 -or $result.NotRunCount -gt 0){throw "Pester gate failed: failed=$($result.FailedCount), skipped=$($result.SkippedCount), notRun=$($result.NotRunCount)."}
}
$python=Get-Command python3,python -ErrorAction SilentlyContinue|Select-Object -First 1
if(-not $python){throw 'Python 3 is required by the deterministic release builder.'}
$arguments=@((Join-Path $PSScriptRoot 'build_release.py'),$Root,'--output-directory',$OutputDirectory)
if($AllowDirty){$arguments+='--allow-dirty'}
$process=Start-Process -FilePath $python.Source -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if($process.ExitCode -ne 0){throw 'Deterministic release build failed.'}
$version=(Import-PowerShellDataFile (Join-Path $Root 'src\WinCare\WinCare.psd1')).ModuleVersion
$archive=Join-Path $OutputDirectory "WinCare-$version.zip"
& (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $archive -OutputPath (Join-Path $OutputDirectory "WinCare-$version-archive-validation.json")
Write-Host "Release built and independently verified: $archive" -ForegroundColor Green
