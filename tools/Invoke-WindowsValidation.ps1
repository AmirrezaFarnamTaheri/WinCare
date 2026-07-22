#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\windows-validation'),
    [ValidateRange(60,7200)][int]$GateTimeoutSeconds=1800,
    [switch]$SkipReleaseBuild
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $IsWindows){throw 'Windows validation must run on Windows.'}
$rootPath=(Resolve-Path -LiteralPath $Root).Path
$null=New-Item -ItemType Directory -Path $OutputDirectory -Force
$tempRoot=Join-Path $env:TEMP ("WinCare-validation-"+[guid]::NewGuid().ToString('N'))
$null=New-Item -ItemType Directory -Path $tempRoot -Force
$pwsh=(Get-Command pwsh.exe -ErrorAction Stop).Source
$gates=[Collections.Generic.List[object]]::new()

function Invoke-ValidationGate {
    param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][string]$Script,[int]$TimeoutSeconds=$GateTimeoutSeconds)
    $scriptPath=Join-Path $tempRoot ($Name+'.ps1')
    $stdout=Join-Path $tempRoot ($Name+'.out.txt');$stderr=Join-Path $tempRoot ($Name+'.err.txt')
    Set-Content -LiteralPath $scriptPath -Value $Script -Encoding utf8
    $psi=[Diagnostics.ProcessStartInfo]::new();$psi.FileName=$pwsh;$psi.UseShellExecute=$false;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true;$psi.CreateNoWindow=$true
    foreach($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$scriptPath)){$null=$psi.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::new();$process.StartInfo=$psi;$started=[datetime]::UtcNow;$null=$process.Start()
    $outTask=$process.StandardOutput.ReadToEndAsync();$errTask=$process.StandardError.ReadToEndAsync()
    $timedOut=-not $process.WaitForExit($TimeoutSeconds*1000)
    if($timedOut){try{$process.Kill($true)}catch { Write-Verbose 'A best-effort operation was unavailable.' };$process.WaitForExit()}
    $outText=$outTask.GetAwaiter().GetResult();$errText=$errTask.GetAwaiter().GetResult();Set-Content -LiteralPath $stdout -Value $outText -Encoding utf8;Set-Content -LiteralPath $stderr -Value $errText -Encoding utf8
    $record=[pscustomobject]@{Name=$Name;Status=if(-not $timedOut -and $process.ExitCode -eq 0){'passed'}elseif($timedOut){'timed-out'}else{'failed'};ExitCode=if($timedOut){-1}else{$process.ExitCode};StartedAt=$started.ToString('o');DurationMilliseconds=[math]::Round(([datetime]::UtcNow-$started).TotalMilliseconds);StdOut=$stdout;StdErr=$stderr}
    $gates.Add($record);$process.Dispose();return $record
}

try{
    $quotedRoot=$rootPath.Replace("'","''");$quotedOutput=$OutputDirectory.Replace("'","''")
    $environment=[ordered]@{
        schema='wincare.windows.environment/v1';capturedAt=[datetime]::UtcNow.ToString('o');computerName=$env:COMPUTERNAME;user=[Security.Principal.WindowsIdentity]::GetCurrent().Name
        isAdministrator=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        os=(Get-CimInstance Win32_OperatingSystem|Select-Object Caption,Version,BuildNumber,OSArchitecture);powershell=$PSVersionTable.PSVersion.ToString();edition=$PSVersionTable.PSEdition
        culture=[Globalization.CultureInfo]::CurrentCulture.Name;uiCulture=[Globalization.CultureInfo]::CurrentUICulture.Name;terminal=$env:WT_SESSION
        capabilities=[ordered]@{winget=[bool](Get-Command winget.exe -ErrorAction SilentlyContinue);defender=[bool](Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue);citool=[bool](Get-Command CiTool.exe -ErrorAction SilentlyContinue);appx=[bool](Get-Command Get-AppxPackage -ErrorAction SilentlyContinue);wuaCom=try{$null=New-Object -ComObject Microsoft.Update.Session;$true}catch{$false}}
    }
    $environment|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $OutputDirectory 'environment.json') -Encoding utf8

    $static=Invoke-ValidationGate -Name '01-static' -Script "& '$quotedRoot\tools\Invoke-StaticChecks.ps1' -Root '$quotedRoot' -RequirePSScriptAnalyzer -OutputPath '$quotedOutput\powershell-static-validation.json'"
    $pester=Invoke-ValidationGate -Name '02-pester' -Script @"
`$ErrorActionPreference='Stop';Import-Module Pester -MinimumVersion 5.5.0 -ErrorAction Stop
`$result=Invoke-Pester -Path '$quotedRoot\tests' -PassThru -Output Detailed
[ordered]@{passed=`$result.PassedCount;failed=`$result.FailedCount;skipped=`$result.SkippedCount;notRun=`$result.NotRunCount;duration=[string]`$result.Duration}|ConvertTo-Json|Set-Content '$quotedOutput\pester-summary.json'
if(`$result.FailedCount -or `$result.SkippedCount -or `$result.NotRunCount){throw 'Pester did not complete with all tests passed.'}
"@
    $module=Invoke-ValidationGate -Name '03-module-smoke' -Script @"
`$ErrorActionPreference='Stop';Import-Module '$quotedRoot\src\WinCare\WinCare.psd1' -Force
`$commands=@(Get-Command -Module WinCare);if(`$commands.Count -lt 30){throw 'Expected exported command surface was not loaded.'}
Initialize-WinCareState -ReadOnly
`$gui=Test-WinCareGuiCatalog;if(-not `$gui.Success -or `$gui.Commands -ne 224){throw 'GUI catalog validation failed on Windows.'}
foreach(`$command in @('system','catalog','presets','knowledge')){`$null=Invoke-WinCareHeadlessCommand -Command `$command -JsonObject}
"@
    if(-not $SkipReleaseBuild){
        $release=Invoke-ValidationGate -Name '04-release' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds,3600)) -Script "& '$quotedRoot\tools\Build-Release.ps1' -Root '$quotedRoot' -OutputDirectory '$quotedOutput\release' -SkipTests"
        $cleanInstall=Invoke-ValidationGate -Name '05-clean-install' -TimeoutSeconds ([math]::Max($GateTimeoutSeconds,1800)) -Script @"
`$ErrorActionPreference='Stop'
`$manifest=Import-PowerShellDataFile '$quotedRoot\src\WinCare\WinCare.psd1'
`$archive=Join-Path '$quotedOutput\release' ("WinCare-{0}.zip" -f `$manifest.ModuleVersion)
if(-not(Test-Path -LiteralPath `$archive -PathType Leaf)){throw "Release archive is missing: `$archive"}
`$sandbox=Join-Path `$env:TEMP ('WinCare-clean-install-'+[guid]::NewGuid().ToString('N'))
`$extract=Join-Path `$sandbox 'extract';`$destination=Join-Path `$sandbox 'installed'
try {
  `$null=New-Item -ItemType Directory -Path `$extract -Force
  Expand-Archive -LiteralPath `$archive -DestinationPath `$extract -Force
  `$releaseRoot=Join-Path `$extract ("WinCare-{0}" -f `$manifest.ModuleVersion)
  & (Join-Path `$releaseRoot 'Install-WinCare.ps1') -Destination `$destination -NoStartMenuShortcut
  `$installedManifest=Join-Path `$destination 'src\WinCare\WinCare.psd1'
  Import-Module `$installedManifest -Force -ErrorAction Stop
  Initialize-WinCareState -ReadOnly
  foreach(`$command in @('system','catalog','presets','knowledge')){`$result=Invoke-WinCareHeadlessCommand -Command `$command -JsonObject;if(`$null -eq `$result){throw "Installed headless command returned no result: `$command"}}
  Remove-Module WinCare -Force
  & (Join-Path `$destination 'Uninstall-WinCare.ps1') -Destination `$destination -Confirm:`$false
  if(Test-Path -LiteralPath `$destination){throw 'Clean-install destination remains after uninstall.'}
} finally {
  Remove-Module WinCare -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath `$sandbox -Recurse -Force -ErrorAction SilentlyContinue
}
"@
    }

    $failed=@($gates|Where-Object Status -ne 'passed')
    $report=[ordered]@{schema='wincare.windows.validation/v1';generatedAt=[datetime]::UtcNow.ToString('o');sourceRoot=$rootPath;environment=$environment;gates=@($gates);status=if($failed.Count){'failed'}else{'passed'}}
    $reportPath=Join-Path $OutputDirectory 'windows-validation-report.json';$report|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $reportPath -Encoding utf8
    if($failed.Count){throw "Windows validation failed in gate(s): $($failed.Name -join ', ')"}
    Write-Host "Windows validation passed: $reportPath" -ForegroundColor Green
}finally{Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue}
