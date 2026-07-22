#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root=(Split-Path $PSScriptRoot -Parent),
    [switch]$RequirePSScriptAnalyzer,
    [string]$OutputPath=(Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\powershell-static-validation.json')
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$rootPath=(Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$failures=[Collections.Generic.List[string]]::new()
$warnings=[Collections.Generic.List[string]]::new()
$files=@(Get-ChildItem -LiteralPath $rootPath -Recurse -File -Include *.ps1,*.psm1,*.psd1|Where-Object{$_.FullName -notmatch '[\\/](\.git|artifacts)[\\/]'}|Sort-Object FullName)
$parseCount=0
$tokenMap=@{}
foreach($file in $files){
    $tokens=$null;$parseErrors=$null
    [Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$parseErrors)|Out-Null
    $tokenMap[$file.FullName]=@($tokens)
    $parseCount++
    foreach($error in @($parseErrors)){$failures.Add("Parse: $($file.FullName):$($error.Extent.StartLineNumber):$($error.Extent.StartColumnNumber): $($error.Message)")}
}
$modulePath=Join-Path $rootPath 'src\WinCare\WinCare.psd1'
try{
    $manifest=Test-ModuleManifest -Path $modulePath -ErrorAction Stop
    Import-Module $modulePath -Force -ErrorAction Stop
    $expected=@($manifest.ExportedFunctions.Keys|Sort-Object)
    $actual=@((Get-Command -Module WinCare -CommandType Function).Name|Sort-Object)
    foreach($difference in Compare-Object $expected $actual){$failures.Add("Module export mismatch: $($difference.InputObject) $($difference.SideIndicator)")}
}catch{$failures.Add("Module: $($_.Exception.Message)")}

$contractPath=Join-Path $rootPath 'src\WinCare\Core\11-ActionContracts.ps1'
$transactionPath=Join-Path $rootPath 'src\WinCare\Core\09-Transactions.ps1'
$contracts=@([regex]::Matches((Get-Content -LiteralPath $contractPath -Raw),"Add-Contract\s+'([^']+)'")|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)
$dispatch=@([regex]::Matches((Get-Content -LiteralPath $transactionPath -Raw),"(?m)^\s*'([^']+)'\s*\{Invoke-WinCare")|ForEach-Object{$_.Groups[1].Value}|Sort-Object -Unique)
foreach($difference in Compare-Object $contracts $dispatch){$failures.Add("Action contract/dispatcher mismatch: $($difference.InputObject) $($difference.SideIndicator)")}

foreach($json in Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter *.json|Where-Object{$_.FullName -notmatch '[\\/](\.git|artifacts)[\\/]'}){
    try{Get-Content -LiteralPath $json.FullName -Raw|ConvertFrom-Json -Depth 100|Out-Null}catch{$failures.Add("JSON: $($json.FullName): $($_.Exception.Message)")}
}

$securityFiles=@($files|Where-Object{$_.Name -notin @('Invoke-StaticChecks.ps1')})
$codePatterns=@(
    @{Pattern='\bInvoke-Expression\b|(?<![\w-])iex(?![\w-])';Name='dynamic evaluation'},
    @{Pattern='\bcmd(?:\.exe)?\s+/c\b';Name='shell-string execution'},
    @{Pattern='-EncodedCommand\b';Name='encoded-command execution'},
    @{Pattern='FromBase64String\s*\([^)]*\)\s*.*\bInvoke';Name='decoded execution'}
)
$ignoredTokenKinds=@('StringLiteral','StringExpandable','HereStringLiteral','HereStringExpandable','Comment')
foreach($file in $securityFiles){
    $codeText=@($tokenMap[$file.FullName]|Where-Object{$_.Kind.ToString() -notin $ignoredTokenKinds}|ForEach-Object{$_.Text}) -join ' '
    foreach($rule in $codePatterns){if($codeText -match $rule.Pattern){$failures.Add("$($rule.Name): $($file.FullName)")}}
}
$placeholderFiles=@($securityFiles|Where-Object{$_.FullName -notmatch '[\\/]tests[\\/]Wave2\.Convergence\.Tests\.ps1$'})
foreach($match in Select-String -Path $placeholderFiles.FullName -Pattern '\bTODO\b|\bFIXME\b|\bTBD\b|NotImplemented' -CaseSensitive:$false){$failures.Add("placeholder marker: $($match.Path):$($match.LineNumber)")}

$nativeSources=@(
    @{Path=(Join-Path $rootPath 'src\WinCare\Native\WinCare.Audio.cs');Type='WinCare.StaticValidation.AudioEndpoint'},
    @{Path=(Join-Path $rootPath 'src\WinCare\Native\WinCare.Productivity.cs');Type='WinCare.StaticValidation.Productivity'}
)
foreach($native in $nativeSources){
    if(Test-Path -LiteralPath $native.Path){
        try{
            if(-not ($native.Type -as [type])){
                $nativeText=(Get-Content -LiteralPath $native.Path -Raw).Replace('namespace WinCare.Native','namespace WinCare.StaticValidation')
                Add-Type -TypeDefinition $nativeText -Language CSharp -ErrorAction Stop
            }
        }catch{$failures.Add("CSharp interop ($($native.Path)): $($_.Exception.Message)")}
    }
}

$analyzer=Get-Module -ListAvailable PSScriptAnalyzer|Sort-Object Version -Descending|Select-Object -First 1
if($analyzer){
    try{
        Import-Module $analyzer -Force
        $settings=Join-Path $rootPath 'PSScriptAnalyzerSettings.psd1'
        foreach($finding in Invoke-ScriptAnalyzer -Path $rootPath -Recurse -Settings $settings){$failures.Add("PSScriptAnalyzer $($finding.RuleName): $($finding.ScriptPath):$($finding.Line): $($finding.Message)")}
    }catch{$failures.Add("PSScriptAnalyzer execution: $($_.Exception.Message)")}
}elseif($RequirePSScriptAnalyzer){$failures.Add('PSScriptAnalyzer is required but unavailable.')}else{$warnings.Add('PSScriptAnalyzer was unavailable and not required for this invocation.')}

$python=Get-Command python,python3,'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue|Where-Object{$_.Source -notmatch 'WindowsApps'}|Select-Object -First 1
if($python){
    foreach($validator in @('validate_source.py','validate_convergence.py')){
        $path=Join-Path $PSScriptRoot $validator
        $process=Start-Process -FilePath $python.Source -ArgumentList @($path,$rootPath) -Wait -PassThru -NoNewWindow
        if($process.ExitCode -ne 0){$failures.Add("Python validator failed: $validator")}
    }
    $toolTests=Start-Process -FilePath $python.Source -ArgumentList @('-m','unittest','tools.test_release_tools','-v') -WorkingDirectory $rootPath -Wait -PassThru -NoNewWindow
    if($toolTests.ExitCode -ne 0){$failures.Add('Python release-tool tests failed.')}
}else{$failures.Add('Python 3 is required for cross-file and convergence validation.')}

$report=[ordered]@{
    schema='wincare.powershell.static-validation/v1'
    generatedAt=[datetime]::UtcNow.ToString('o')
    root=$rootPath
    powershellFiles=$files.Count
    parsedFiles=$parseCount
    actionContracts=$contracts.Count
    psscriptAnalyzer=if($analyzer){[string]$analyzer.Version}else{'unavailable'}
    errors=@($failures)
    warnings=@($warnings)
    status=if($failures.Count){'failed'}else{'passed'}
}
$directory=Split-Path $OutputPath -Parent;if($directory){$null=New-Item -ItemType Directory -Path $directory -Force}
$report|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $OutputPath -Encoding utf8
if($failures.Count){$failures|ForEach-Object{Write-Error $_};throw "$($failures.Count) static check(s) failed."}
Write-Host "Static checks passed: $($files.Count) PowerShell files; $($contracts.Count) typed action contracts." -ForegroundColor Green
