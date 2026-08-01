param([int]$Runs = 3, [switch]$WarmOnly, [switch]$ColdOnly)
# ce-optimize harness v2: measure cold (no receipt) and warm (receipt exists) load times separately.

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$psd1 = Join-Path $root 'src/WinCare/WinCare.psd1'
$receiptPath = Join-Path $root 'src/WinCare/.wincare-load-receipt.json'
$script:LoadFailed = $false

function Measure-OneLoad {
    param([string]$Psd1)
    $job = Start-Job -ScriptBlock {
        param($p)
        Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Import-Module $p -Force -ErrorAction Stop 3>$null
        $sw.Stop()
        $sw.Elapsed.TotalMilliseconds
    } -ArgumentList $Psd1

    $elapsed = $job | Wait-Job | Receive-Job -ErrorAction SilentlyContinue
    $failed = $job.State -ne 'Completed' -or $null -eq $elapsed
    $job | Remove-Job
    if ($failed) {
        $script:LoadFailed = $true
        return [double]::NaN
    }
    return [double]$elapsed
}

$coldTimes = [System.Collections.Generic.List[double]]::new()
$warmTimes = [System.Collections.Generic.List[double]]::new()

if (-not $WarmOnly) {
    for ($i = 0; $i -lt $Runs; $i++) {
        if (Test-Path $receiptPath) { Remove-Item $receiptPath -Force }
        $t = Measure-OneLoad $psd1
        if (-not [double]::IsNaN($t)) { $coldTimes.Add($t) }
    }
}

if (-not $ColdOnly) {
    if (-not (Test-Path $receiptPath)) {
        $null = Measure-OneLoad $psd1
    }
    for ($i = 0; $i -lt $Runs; $i++) {
        $t = Measure-OneLoad $psd1
        if (-not [double]::IsNaN($t)) { $warmTimes.Add($t) }
    }
}

function Get-Stats([double[]]$values) {
    $valid = @($values | Where-Object { -not [double]::IsNaN($_) })
    if (-not $valid -or $valid.Count -eq 0) { return @{median=0;min=0;max=0;count=0} }
    $s = $valid | Sort-Object
    $med = if ($s.Count % 2 -eq 0) { ($s[$s.Count/2-1]+$s[$s.Count/2])/2 } else { $s[[int]($s.Count/2)] }
    return @{
        median = [math]::Round($med,1)
        min    = [math]::Round(($s | Measure-Object -Minimum).Minimum,1)
        max    = [math]::Round(($s | Measure-Object -Maximum).Maximum,1)
        count  = $s.Count
    }
}

$sourceFilesCount = @(Get-ChildItem (Join-Path $root 'src/WinCare') -Filter '*.ps1' -Recurse -File | Where-Object { $_.Name -notlike '.wincare-*' }).Count

$result = [ordered]@{
    load_time_ms_median = (Get-Stats $warmTimes).median
    load_time_ms_min    = (Get-Stats $warmTimes).min
    load_time_ms_max    = (Get-Stats $warmTimes).max
    cold_ms_median      = (Get-Stats $coldTimes).median
    cold_ms_min         = (Get-Stats $coldTimes).min
    cold_ms_max         = (Get-Stats $coldTimes).max
    run_count           = $Runs
    source_file_count   = $sourceFilesCount
    receipt_exists      = (Test-Path $receiptPath)
    load_succeeded      = (-not $script:LoadFailed)
}
$result | ConvertTo-Json -Compress
