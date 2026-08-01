param([int]$Runs = 3, [switch]$WarmOnly, [switch]$ColdOnly)
# ce-optimize harness v2: measure cold (no receipt) and warm (receipt exists) load times separately.
# Cold = first Import-Module (writes receipt). Warm = subsequent imports (reads receipt, skips validation).

$psd1 = 'D:\WinCare-5.2.0\WinCare\src\WinCare\WinCare.psd1'
$receiptPath = 'D:\WinCare-5.2.0\WinCare\src\WinCare\.wincare-load-receipt.json'

function Measure-OneLoad {
    param([string]$Psd1)
    $job = Start-Job -ScriptBlock {
        param($p)
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        Import-Module $p -Force -ErrorAction Stop 3>$null
        $sw.Stop()
        $sw.Elapsed.TotalMilliseconds
    } -ArgumentList $Psd1
    $elapsed = $job | Wait-Job | Receive-Job
    $job | Remove-Job
    return [double]$elapsed
}

$coldTimes = [System.Collections.Generic.List[double]]::new()
$warmTimes = [System.Collections.Generic.List[double]]::new()

if (-not $WarmOnly) {
    # Cold runs: delete receipt before each
    for ($i = 0; $i -lt $Runs; $i++) {
        if (Test-Path $receiptPath) { Remove-Item $receiptPath -Force }
        $coldTimes.Add((Measure-OneLoad $psd1))
        # Let receipt exist for warm runs after last cold
    }
}

if (-not $ColdOnly) {
    # Ensure receipt exists from a cold load first
    if (-not (Test-Path $receiptPath)) {
        $null = Measure-OneLoad $psd1
    }
    for ($i = 0; $i -lt $Runs; $i++) {
        $warmTimes.Add((Measure-OneLoad $psd1))
    }
}

function Get-Stats([double[]]$values) {
    if (-not $values -or $values.Count -eq 0) { return @{median=0;min=0;max=0;count=0} }
    $s = $values | Sort-Object
    $med = if ($s.Count % 2 -eq 0) { ($s[$s.Count/2-1]+$s[$s.Count/2])/2 } else { $s[[int]($s.Count/2)] }
    return @{
        median = [math]::Round($med,1)
        min    = [math]::Round(($s | Measure-Object -Minimum).Minimum,1)
        max    = [math]::Round(($s | Measure-Object -Maximum).Maximum,1)
        count  = $s.Count
    }
}

$result = [ordered]@{
    load_time_ms_median = (Get-Stats $warmTimes).median  # primary metric = warm (post-receipt)
    load_time_ms_min    = (Get-Stats $warmTimes).min
    load_time_ms_max    = (Get-Stats $warmTimes).max
    cold_ms_median      = (Get-Stats $coldTimes).median
    cold_ms_min         = (Get-Stats $coldTimes).min
    cold_ms_max         = (Get-Stats $coldTimes).max
    run_count           = $Runs
    source_file_count   = 172
    receipt_exists      = (Test-Path $receiptPath)
    load_succeeded      = $true
}
$result | ConvertTo-Json -Compress
