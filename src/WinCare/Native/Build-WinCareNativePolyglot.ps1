[CmdletBinding()]
param(
    [string]$OutputDirectory = "D:\WinCare-5.2.0\WinCare-5.2.0\bin"
)

$ErrorActionPreference = 'Stop'
Write-Host "Building WinCare v1.0.0 Self-Inclusive Polyglot Modules..." -ForegroundColor Cyan

if (-not (Test-Path $OutputDirectory)) {
    New-Item -Path $OutputDirectory -ItemType Directory -Force | Out-Null
}

$csFiles = Get-ChildItem -Path $PSScriptRoot -Filter "*.cs" | Select-Object -ExpandProperty FullName
$targetDll = Join-Path $OutputDirectory "WinCare.Native.dll"
if (Test-Path $targetDll) { Remove-Item $targetDll -Force }

Write-Host "Compiling WinCare.Native.dll from $($csFiles.Count) source files..." -ForegroundColor Yellow
Add-Type -Path $csFiles -OutputAssembly $targetDll -OutputType Library -ReferencedAssemblies "System.dll", "System.Collections.dll", "System.Net.Sockets.dll", "Microsoft.Win32.Registry.dll", "Microsoft.Win32.Primitives.dll", "System.Drawing.dll", "System.Windows.Forms.dll"
Write-Host "SUCCESS: Built $targetDll" -ForegroundColor Green
