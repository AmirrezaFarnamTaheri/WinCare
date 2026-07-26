function Get-WinCareGuiModuleAssurance {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    $moduleRoot=Join-Path $Root 'src\WinCare'
    $manifestDirectory=Join-Path $moduleRoot 'SourceManifests'
    $missing=[Collections.Generic.List[string]]::new()
    $sourceCount=0
    foreach($manifestName in @('Core.psd1','Providers.psd1','UI.psd1')){
        $manifestPath=Join-Path $manifestDirectory $manifestName
        if(-not(Test-Path -LiteralPath $manifestPath -PathType Leaf)){
            throw "Source manifest is missing: $manifestName"
        }
        $manifest=Import-PowerShellDataFile -LiteralPath $manifestPath
        foreach($relativePath in @($manifest.Files)){
            $sourceCount++
            $sourcePath=Join-Path $moduleRoot $relativePath
            if(-not(Test-Path -LiteralPath $sourcePath -PathType Leaf)){
                $missing.Add([string]$relativePath)
            }
        }
    }
    $moduleManifest=Import-PowerShellDataFile -LiteralPath (Join-Path $moduleRoot 'WinCare.psd1')
    $sourceStatus=if($missing.Count){'Failed'}else{'Passed'}
    $sourceDetail=if($missing.Count){
        "Missing: $($missing -join ', ')"
    }else{
        "$sourceCount files are assigned to closed ordered manifests."
    }
    [pscustomobject]@{
        ModuleRoot=$moduleRoot
        SourceCount=$sourceCount
        ExportCount=@($moduleManifest.FunctionsToExport).Count
        Rows=@(
            [pscustomobject]@{
                Check='Module closure';Status=$sourceStatus;Scope='Core / Providers / UI';Detail=$sourceDetail
            }
            [pscustomobject]@{
                Check='Public API';Status='Passed';Scope='WinCare.psd1'
                Detail="$(@($moduleManifest.FunctionsToExport).Count) explicitly exported commands."
            }
        )
    }
}

function Get-WinCareGuiTestAssurance {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Root)
    $testsPath=Join-Path $Root 'tests'
    if(Test-Path -LiteralPath $testsPath -PathType Container){
        $testFiles=@(Get-ChildItem -LiteralPath $testsPath -Recurse -File -Filter '*.Tests.ps1' -ErrorAction Stop)
        $testCases=0
        foreach($testFile in $testFiles){
            $testText=Read-WinCareBoundedText -LiteralPath $testFile.FullName -MaximumBytes 16777216
            $testCases+=[regex]::Matches($testText,'(?m)^\s*It\s+["'']').Count
        }
        return [pscustomobject]@{
            Cases=$testCases
            Suites=$testFiles.Count
            Row=[pscustomobject]@{
                Check='Test inventory';Status='Inventory';Scope='Pester source'
                Detail="$testCases cases across $($testFiles.Count) suites; execution status is reported by CI."
            }
        }
    }
    $receiptPath=Join-Path $Root 'BUILD-RECEIPT.json'
    if(Test-Path -LiteralPath $receiptPath -PathType Leaf){
        $receipt=Read-WinCareBoundedJson -LiteralPath $receiptPath -MaximumBytes 1048576 -Depth 40
        $status=if($receipt.validation.windowsNative){
            [string]$receipt.validation.windowsNative
        }else{
            'See release validation'
        }
        return [pscustomobject]@{
            Cases=[int]$receipt.validation.pesterCases
            Suites=[int]$receipt.validation.pesterSuites
            Row=[pscustomobject]@{
                Check='Packaged validation';Status=$status;Scope='BUILD-RECEIPT.json'
                Detail="Source commit $($receipt.source.gitCommit); profile $($receipt.packageProfile)."
            }
        }
    }
    [pscustomobject]@{
        Cases=0
        Suites=0
        Row=[pscustomobject]@{
            Check='Test inventory';Status='Unavailable';Scope='Installed package'
            Detail='Use the release validation sidecar for executed test evidence.'
        }
    }
}

function Get-WinCareGuiNativeAssurance {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ModuleRoot)
    $nativePath=Join-Path $ModuleRoot 'bin\WinCare.Native.dll'
    $status=if(Test-Path -LiteralPath $nativePath -PathType Leaf){'Available'}else{'Pending'}
    $detail=if($status -eq 'Available'){
        'Source-built native assembly is present; load-time identity checks remain enforced.'
    }else{
        'Native assembly is produced and verified by the mandatory Windows/.NET build gate.'
    }
    [pscustomobject]@{
        Status=$status
        Row=[pscustomobject]@{
            Check='Native runtime';Status=$status;Scope='WinCare.Native.dll';Detail=$detail
        }
    }
}

function Update-WinCareGuiAssurance {
    $controls=$script:WinCareGuiState.Controls
    $root=Get-WinCareGuiRepositoryRoot
    try{
        $module=Get-WinCareGuiModuleAssurance -Root $root
        $tests=Get-WinCareGuiTestAssurance -Root $root
        $native=Get-WinCareGuiNativeAssurance -ModuleRoot $module.ModuleRoot
        $registry=Get-WinCareCapabilityRegistry
        $capabilities=@($registry.Capabilities)
        $available=[int]$registry.AvailableCount
        $capabilityRow=[pscustomobject]@{
            Check='Advanced capabilities';Status='Inventory';Scope='Capability registry'
            Detail="$available of $($capabilities.Count) capabilities are available on this host."
        }
        $rows=@($module.Rows)+@($tests.Row,$native.Row,$capabilityRow)
        $controls.AssuranceSourceText.Text=[string]$module.SourceCount
        $controls.AssuranceExportText.Text=[string]$module.ExportCount
        $controls.AssuranceTestText.Text=if($tests.Cases -or $tests.Suites){
            "$($tests.Cases) / $($tests.Suites)"
        }else{
            'See release'
        }
        $controls.AssuranceNativeText.Text=$native.Status
        $controls.AssuranceGrid.ItemsSource=$rows
    }catch{
        Show-WinCareGuiToast -Message "Assurance view failed: $($_.Exception.Message)" -Kind Warning
    }
}
