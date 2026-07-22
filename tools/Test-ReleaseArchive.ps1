#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$OutputPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$resolved=(Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
$python=Get-Command 'C:\Program Files\Python311\python.exe',python,python3 -ErrorAction SilentlyContinue|Where-Object{$_.Source -notmatch 'WindowsApps'}|Select-Object -First 1
if(-not $python){throw 'Python 3 is required for independent archive verification.'}
$arguments=@((Join-Path $PSScriptRoot 'verify_release.py'),$resolved)
if($OutputPath){$arguments+=@('--output',$OutputPath)}
$process=Start-Process -FilePath $python.Source -ArgumentList $arguments -Wait -PassThru -NoNewWindow
if($process.ExitCode -ne 0){throw 'Release archive verification failed.'}
