#requires -Version 7.2
[CmdletBinding()]
param(
    [switch]$ReadOnly,
    [ValidateSet('Normal','HighContrast','Monochrome')][string]$Theme='Normal'
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'WinCare GUI requires Windows 10 or Windows 11.'}
if([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA){
    $pwsh=(Get-Command pwsh.exe -ErrorAction Stop).Source
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$pwsh;$start.UseShellExecute=$false;$start.WorkingDirectory=$PSScriptRoot
    foreach($arg in @('-NoLogo','-NoProfile','-STA','-File',$PSCommandPath)){[void]$start.ArgumentList.Add($arg)}
    if($ReadOnly){[void]$start.ArgumentList.Add('-ReadOnly')};if($Theme){[void]$start.ArgumentList.Add('-Theme');[void]$start.ArgumentList.Add($Theme)}
    $process=[Diagnostics.Process]::Start($start);if($null -eq $process){throw 'The STA GUI process did not start.'}
    exit 0
}
$modulePath=Join-Path $PSScriptRoot 'src/WinCare/WinCare.psd1'
Import-Module $modulePath -Force
Start-WinCareGui -ReadOnly:$ReadOnly -Theme $Theme
