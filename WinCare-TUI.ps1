#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$Command,
    [ValidateLength(0,32768)][string]$ArgumentsJson = '{}',
    [switch]$Apply,
    [switch]$Json
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# ponytail: TUI launcher -> Terminal.Gui / Spectre.Console fuzzy palette
$entryPoint = Join-Path $PSScriptRoot 'WinCare.ps1'
if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
    throw "WinCare entry point not found: $entryPoint"
}
$parameters = @{}
if (-not [string]::IsNullOrWhiteSpace($Command)) {
    $parameters.Command = $Command
    $parameters.ArgumentsJson = $ArgumentsJson
    if ($Apply) { $parameters.Apply = $true }
    if ($Json) { $parameters.Json = $true }
}
& $entryPoint @parameters
exit $LASTEXITCODE
