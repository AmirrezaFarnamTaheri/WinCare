#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\convergence\convergence-validation.json')
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$python=Get-Command python3,python -ErrorAction SilentlyContinue|Select-Object -First 1
if(-not $python){throw 'Python 3 is required for the convergence evidence audit.'}
$process=Start-Process -FilePath $python.Source -ArgumentList @((Join-Path $PSScriptRoot 'validate_convergence.py'),$Root,'--output',$OutputPath) -Wait -PassThru -NoNewWindow
if($process.ExitCode -ne 0){throw 'Peer-convergence evidence validation failed.'}
Write-Host "Convergence evidence validated: $OutputPath" -ForegroundColor Green
