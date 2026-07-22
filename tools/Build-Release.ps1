#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [switch]$SkipTests,
    [switch]$AllowDirty
)
Set-StrictMode -Version Latest
$hasPssa = [bool](Get-Module -ListAvailable PSScriptAnalyzer -ErrorAction SilentlyContinue)
$pssaArgs=@{}
if($hasPssa){$pssaArgs['RequirePSScriptAnalyzer']=$true}
& (Join-Path $PSScriptRoot 'Invoke-StaticChecks.ps1') -Root $Root @pssaArgs -OutputPath (Join-Path $OutputDirectory 'powershell-static-validation.json')
& (Join-Path $PSScriptRoot 'Invoke-ConvergenceAudit.ps1') -Root $Root
if(-not $SkipTests){
    Import-Module Pester -ErrorAction Stop
    $result=Invoke-Pester -Path (Join-Path $Root 'tests') -PassThru
    $failedCount = if($null -ne $result.FailedCount){$result.FailedCount}else{($result.TestResult|Where-Object Passed -eq $false).Count}
    if($failedCount -gt 0){throw "Pester gate failed: failed=$failedCount."}
}
$python=Get-Command python,python3,'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue|Where-Object{$_.Source -notmatch 'WindowsApps'}|Select-Object -First 1
if(-not $python){throw 'Python 3 is required by the deterministic release builder.'}
$scriptPath=Join-Path $PSScriptRoot 'build_release.py'
$arguments=@($scriptPath, $Root, '--output-directory', $OutputDirectory)
if($AllowDirty){$arguments+='--allow-dirty'}
& $python.Source @arguments
if($LASTEXITCODE -ne 0){throw 'Deterministic release build failed.'}
$version=(Import-PowerShellDataFile (Join-Path $Root 'src\WinCare\WinCare.psd1')).ModuleVersion
$archive=Join-Path $OutputDirectory "WinCare-$version.zip"
& (Join-Path $PSScriptRoot 'Test-ReleaseArchive.ps1') -ArchivePath $archive -OutputPath (Join-Path $OutputDirectory "WinCare-$version-archive-validation.json")
Write-Host "Release built and independently verified: $archive" -ForegroundColor Green
