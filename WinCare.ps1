#requires -Version 7.2
[CmdletBinding()]
param(
    [switch]$NoLogo,
    [switch]$Ascii,
    [switch]$ReadOnly,
    [ValidateSet('Normal','HighContrast','Monochrome')][string]$Theme,
    [string]$Command,
    [string]$ArgumentsJson='{}',
    [switch]$Apply,
    [switch]$Json,
    [switch]$Gui
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$modulePath=Join-Path $PSScriptRoot 'src/WinCare/WinCare.psd1';Import-Module $modulePath -Force
if($Gui){
    if(-not $IsWindows){throw 'WinCare GUI requires Windows 10 or Windows 11.'}
    if([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA){
        $pwsh=[Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
        if([string]::IsNullOrWhiteSpace($pwsh)){throw 'The current PowerShell executable path is unavailable.'}
        $pwshItem=Get-Item -LiteralPath $pwsh -Force -ErrorAction Stop
        if($pwshItem.PSIsContainer -or ($pwshItem.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'The current PowerShell executable is not a regular non-reparse file.'}
        $pwsh=$pwshItem.FullName
        $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$pwsh;$start.UseShellExecute=$false;$start.WorkingDirectory=$PSScriptRoot
        foreach($arg in @('-NoLogo','-NoProfile','-STA','-File',$PSCommandPath,'-Gui')){[void]$start.ArgumentList.Add($arg)}
        if($ReadOnly){[void]$start.ArgumentList.Add('-ReadOnly')};if($Theme){[void]$start.ArgumentList.Add('-Theme');[void]$start.ArgumentList.Add($Theme)}
        $process=[Diagnostics.Process]::Start($start);if($null -eq $process){throw 'The STA GUI process did not start.'}
        try{$process.WaitForExit();exit $process.ExitCode}finally{$process.Dispose()}
    }
    $guiParameters=@{ReadOnly=$ReadOnly};if($Theme){$guiParameters.Theme=$Theme};Start-WinCareGui @guiParameters
    exit 0
}
if($Command){
    Initialize-WinCareState -ReadOnly:(!$Apply) -SkipConfigSave
    $arguments=if([string]::IsNullOrWhiteSpace($ArgumentsJson)){@{}}else{$ArgumentsJson|ConvertFrom-Json -AsHashtable -Depth 40}
    $result=Invoke-WinCareHeadlessCommand -Command $Command -Arguments $arguments -Apply:$Apply -JsonObject
    if($Json){$result|ConvertTo-Json -Depth 50}else{$result|Format-List *}
    if($result -and $result.PSObject.Properties['Success'] -and -not $result.Success){exit 1}
    exit 0
}
$startParameters=@{NoLogo=$NoLogo};if($PSBoundParameters.ContainsKey('Ascii')){$startParameters.Ascii=$Ascii};if($PSBoundParameters.ContainsKey('ReadOnly')){$startParameters.ReadOnly=$ReadOnly};if($PSBoundParameters.ContainsKey('Theme')){$startParameters.Theme=$Theme};Start-WinCare @startParameters
