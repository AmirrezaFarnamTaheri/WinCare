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
    $pwsh=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if([string]::IsNullOrWhiteSpace($pwsh)){throw 'The current PowerShell executable path is unavailable.'}
        $pwshItem=Get-Item -LiteralPath $pwsh -Force -ErrorAction Stop
        if($pwshItem.PSIsContainer -or ($pwshItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'The current PowerShell executable is not a regular non-reparse file.'}
        $pwsh=$pwshItem.FullName
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$pwsh;$start.UseShellExecute=$false;$start.WorkingDirectory=$PSScriptRoot
    foreach($arg in @('-NoLogo','-NoProfile','-STA','-File',$PSCommandPath)){[void]$start.ArgumentList.Add($arg)}
    if($ReadOnly){[void]$start.ArgumentList.Add('-ReadOnly')};if($Theme){[void]$start.ArgumentList.Add('-Theme');[void]$start.ArgumentList.Add($Theme)}
    $process=[Diagnostics.Process]::Start($start);if($null -eq $process){throw 'The STA GUI process did not start.'}
    try{$process.WaitForExit();exit $process.ExitCode}finally{$process.Dispose()}
}
$modulePath=Join-Path $PSScriptRoot 'src/WinCare/WinCare.psd1'
Import-Module $modulePath -Force
Start-WinCareGui -ReadOnly:$ReadOnly -Theme $Theme
