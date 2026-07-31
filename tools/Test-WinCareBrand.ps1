#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$Root = (Split-Path $PSScriptRoot -Parent),
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

$rootPath = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
$brandDirectory = Join-Path $rootPath 'src\WinCare\Data\Gui'
$manifestPath = Join-Path $brandDirectory 'WinCare.Brand.json'
$manifest = Read-WinCareToolingJson `
    -LiteralPath $manifestPath `
    -MaximumBytes 1048576 `
    -MaximumDepth 24
if ([string]$manifest.schema -ne 'wincare.brand/v1') {
    throw 'WinCare brand manifest schema is invalid.'
}
if ([string]$manifest.name -ne 'WinCare') {
    throw 'WinCare brand manifest name is invalid.'
}

$records = [Collections.Generic.List[object]]::new()
foreach ($property in @('markSvg','logoSvg','logoPng','appPng','appIcon')) {
    $entry = $manifest.assets.$property
    if ($null -eq $entry) {
        throw "WinCare brand manifest is missing asset: $property"
    }
    $name = [string]$entry.path
    if ([IO.Path]::GetFileName($name) -ne $name) {
        throw "WinCare brand asset path is not a leaf name: $name"
    }
    $path = Join-Path $brandDirectory $name
    $evidence = Get-WinCareToolingFileSha256 `
        -LiteralPath $path `
        -MaximumBytes 16777216 `
        -Purpose "WinCare brand asset $name"
    if ($evidence.Sha256 -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "WinCare brand asset hash mismatch: $name"
    }
    $records.Add([ordered]@{
        Name = $name
        Sha256 = $evidence.Sha256
        Bytes = [long]$evidence.Bytes
    })
}

$iconPath = Join-Path $brandDirectory ([string]$manifest.assets.appIcon.path)
$iconBytes = Read-WinCareToolingBoundedBytes `
    -LiteralPath $iconPath `
    -MaximumBytes 1048576 `
    -Purpose 'WinCare application icon'
try {
    if ($iconBytes.Length -lt 6) { throw 'WinCare ICO is truncated.' }
    $reserved = [BitConverter]::ToUInt16($iconBytes,0)
    $type = [BitConverter]::ToUInt16($iconBytes,2)
    $count = [BitConverter]::ToUInt16($iconBytes,4)
    if ($reserved -ne 0 -or $type -ne 1 -or $count -lt 1 -or $count -gt 64) {
        throw 'WinCare ICO header is invalid.'
    }
    if ($iconBytes.Length -lt 6 + (16 * $count)) {
        throw 'WinCare ICO directory is truncated.'
    }
    $frames = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $count; $index++) {
        $offset = 6 + (16 * $index)
        $width = if ($iconBytes[$offset] -eq 0) {
            256
        } else {
            [int]$iconBytes[$offset]
        }
        $height = if ($iconBytes[$offset + 1] -eq 0) {
            256
        } else {
            [int]$iconBytes[$offset + 1]
        }
        if ($width -ne $height) {
            throw "WinCare ICO frame is not square: ${width}x${height}"
        }
        $imageBytes = [BitConverter]::ToUInt32($iconBytes,$offset + 8)
        $imageOffset = [BitConverter]::ToUInt32($iconBytes,$offset + 12)
        if (
            $imageBytes -lt 1 -or
            [long]$imageOffset + [long]$imageBytes -gt $iconBytes.Length
        ) {
            throw "WinCare ICO frame has an invalid byte range: $width"
        }
        $frames.Add($width)
    }
    $observed = @($frames | Sort-Object -Unique)
    $expected = @(
        $manifest.assets.appIcon.frames |
            ForEach-Object { [int]$_ }
    )
    if (($observed -join ',') -ne ($expected -join ',')) {
        throw (
            'WinCare ICO frames do not match the brand manifest. ' +
            "Expected=$($expected -join ',') Actual=$($observed -join ',')"
        )
    }
} finally {
    if ($iconBytes.Length) {
        [Array]::Clear($iconBytes,0,$iconBytes.Length)
    }
}

$result = [ordered]@{
    schema = 'wincare.brand.validation/v1'
    status = 'passed'
    manifestSha256 = (
        Get-WinCareToolingFileSha256 `
            -LiteralPath $manifestPath `
            -MaximumBytes 1048576 `
            -Purpose 'WinCare brand manifest'
    ).Sha256
    iconFrames = @($observed)
    assets = @($records)
}
if ($OutputPath) {
    $parent = Split-Path -Parent $OutputPath
    if ($parent) { [void][IO.Directory]::CreateDirectory($parent) }
    Write-WinCareToolingAtomicJson `
        -LiteralPath $OutputPath `
        -Value $result `
        -MaximumBytes 1048576 `
        -Depth 12
}
[pscustomobject]$result
