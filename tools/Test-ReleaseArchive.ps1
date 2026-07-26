#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')
$resolved=(Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
$python=Get-Command python,python3,'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue|Where-Object{$_.Source -notmatch 'WindowsApps'}|Select-Object -First 1
if(-not $python){throw 'Python 3 is required for independent archive verification.'}
$scriptPath=Join-Path $PSScriptRoot 'verify_release.py'
$arguments=@($scriptPath,$resolved)
if($OutputPath){$arguments+=@('--output',$OutputPath)}
$result=Invoke-WinCareToolingProcess -Executable $python.Source -Arguments $arguments -TimeoutSeconds 1800 -MaximumCapturedOutputBytes 33554432 -WriteCapturedOutput
if($result.ExitCode -ne 0){throw "Release archive verification failed. ExitCode=$($result.ExitCode)"}
