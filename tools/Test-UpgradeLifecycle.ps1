#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CurrentArchivePath,
    [Parameter(Mandatory)][string]$PreviousArchivePath,
    [Parameter(Mandatory)][string]$WorkRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Upgrade lifecycle validation must run on Windows.' }
. (Join-Path $PSScriptRoot 'WinCare.Tooling.ps1')

function Read-WinCareUpgradeJson {
    param([Parameter(Mandatory)][string]$Path)
    Read-WinCareToolingJson -LiteralPath $Path -MaximumBytes 1048576 -MaximumDepth 32
}

function Expand-WinCareUpgradeArchive {
    param(
        [Parameter(Mandatory)][string]$Archive,
        [Parameter(Mandatory)][string]$Destination
    )
    $python = Get-Command python,python3,'C:\Program Files\Python311\python.exe' `
        -ErrorAction SilentlyContinue |
        Where-Object Source -NotMatch 'WindowsApps' |
        Select-Object -ExpandProperty Source -First 1
    if (-not $python) { throw 'Python 3 is required by the upgrade lifecycle validator.' }
    & $python (Join-Path $PSScriptRoot 'verify_release.py') $Archive --extract-to $Destination
    if ($LASTEXITCODE -ne 0) { throw "Validated archive extraction failed: $Archive" }
    $roots = @(Get-ChildItem -LiteralPath $Destination -Directory -Force -ErrorAction Stop)
    if ($roots.Count -ne 1) { throw 'Upgrade archive must contain exactly one root directory.' }
    $roots[0].FullName
}

$currentArchive = (Resolve-Path -LiteralPath $CurrentArchivePath -ErrorAction Stop).Path
$previousArchive = (Resolve-Path -LiteralPath $PreviousArchivePath -ErrorAction Stop).Path
$work = [IO.Path]::GetFullPath($WorkRoot)
if (Test-Path -LiteralPath $work) {
    if (@(Get-ChildItem -LiteralPath $work -Force -ErrorAction Stop).Count) {
        throw "Upgrade work root must be absent or empty: $work"
    }
} else {
    [void][IO.Directory]::CreateDirectory($work)
}

try {
    $phaseName = 'version-upgrade'
    Write-Host "::group::lifecycle/$phaseName"
    $currentSource = Expand-WinCareUpgradeArchive `
        -Archive $currentArchive `
        -Destination (Join-Path $work 'current')
    $previousSource = Expand-WinCareUpgradeArchive `
        -Archive $previousArchive `
        -Destination (Join-Path $work 'previous')

    $currentVersion = [version][string](
        Import-PowerShellDataFile (Join-Path $currentSource 'src\WinCare\WinCare.psd1')
    ).ModuleVersion
    $previousVersion = [version][string](
        Import-PowerShellDataFile (Join-Path $previousSource 'src\WinCare\WinCare.psd1')
    ).ModuleVersion
    if ($previousVersion -ge $currentVersion) {
        throw "Previous fixture version $previousVersion is not lower than $currentVersion."
    }

    $destination = Join-Path $work 'installed\WinCare'
    $shortcutRoot = Join-Path $work 'shortcuts'
    [void][IO.Directory]::CreateDirectory($shortcutRoot)
    $previousInstaller = Join-Path $previousSource 'Install-WinCare.ps1'
    $currentInstaller = Join-Path $currentSource 'Install-WinCare.ps1'
    $currentUninstaller = Join-Path $currentSource 'Uninstall-WinCare.ps1'

    & $previousInstaller `
        -Destination $destination `
        -ShortcutRoot $shortcutRoot `
        -NoStartMenuShortcut `
        -Confirm:$false
    $before = Read-WinCareUpgradeJson (Join-Path $destination '.wincare-install.json')
    if ([version][string]$before.Version -ne $previousVersion) {
        throw 'The previous fixture installation recorded the wrong version.'
    }
    $installationId = [string]$before.InstallationId

    $userData = Join-Path $work 'user-data\settings.json'
    [void][IO.Directory]::CreateDirectory((Split-Path $userData -Parent))
    [IO.File]::WriteAllText($userData, '{"preserve":true}', [Text.UTF8Encoding]::new($false))

    $tamperedSource = Join-Path $work 'tampered-current'
    Copy-Item -LiteralPath $currentSource -Destination $tamperedSource -Recurse -Force
    $tamperTarget = Join-Path $tamperedSource 'README.md'
    if (-not (Test-Path -LiteralPath $tamperTarget -PathType Leaf)) {
        $tamperTarget = Join-Path $tamperedSource 'WinCare.ps1'
    }
    [IO.File]::AppendAllText($tamperTarget, "`n# upgrade tamper`n", [Text.UTF8Encoding]::new($false))
    $tamperedRejected = $false
    try {
        & (Join-Path $tamperedSource 'Install-WinCare.ps1') `
            -Destination $destination `
            -ShortcutRoot $shortcutRoot `
            -NoStartMenuShortcut `
            -Force `
            -Confirm:$false
    } catch {
        $tamperedRejected = $true
    }
    if (-not $tamperedRejected) { throw 'A tampered upgrade source was accepted.' }
    $afterRejected = Read-WinCareUpgradeJson (Join-Path $destination '.wincare-install.json')
    if (
        [string]$afterRejected.InstallationId -ne $installationId -or
        [version][string]$afterRejected.Version -ne $previousVersion
    ) {
        throw 'Rejected upgrade changed the previous installation identity or version.'
    }
    foreach ($name in @('WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe')) {
        $process = Start-Process `
            -FilePath (Join-Path $destination $name) `
            -ArgumentList '--wincare-self-test' `
            -Wait `
            -PassThru `
            -NoNewWindow
        try {
            if ($process.ExitCode -ne 0) {
                throw "Previous installation failed after rejected upgrade: $name"
            }
        } finally {
            $process.Dispose()
        }
    }
    $UpgradeRollbackVerified = $true

    $result = & $currentInstaller `
        -Destination $destination `
        -ShortcutRoot $shortcutRoot `
        -NoStartMenuShortcut `
        -Force `
        -Confirm:$false
    if ([string]$result.Operation -ne 'replace') {
        throw "Expected managed replacement during upgrade, got: $($result.Operation)"
    }
    $after = Read-WinCareUpgradeJson (Join-Path $destination '.wincare-install.json')
    if ([string]$after.InstallationId -ne $installationId) {
        throw 'Upgrade did not preserve the installation identity.'
    }
    if ([version][string]$after.Version -ne $currentVersion) {
        throw 'Upgrade did not record the current release version.'
    }
    if ([string]$after.LastOperation -ne 'replace') {
        throw 'Upgrade marker did not record a replace operation.'
    }
    if (-not (Test-Path -LiteralPath $userData -PathType Leaf)) {
        throw 'Upgrade removed user data outside the installation tree.'
    }
    foreach ($name in @('WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe')) {
        $process = Start-Process `
            -FilePath (Join-Path $destination $name) `
            -ArgumentList '--wincare-self-test' `
            -Wait `
            -PassThru `
            -NoNewWindow
        try {
            if ($process.ExitCode -ne 0) { throw "Upgraded self-test failed: $name" }
        } finally {
            $process.Dispose()
        }
    }
    $UpgradeVerified = $true

    & $currentUninstaller `
        -Destination $destination `
        -ShortcutRoot $shortcutRoot `
        -Confirm:$false
    if (Test-Path -LiteralPath $destination) {
        throw 'Upgrade lifecycle uninstall left the destination behind.'
    }

    [pscustomobject]@{
        Schema = 'wincare.installation.upgrade/v1'
        Status = 'passed'
        PreviousVersion = [string]$previousVersion
        CurrentVersion = [string]$currentVersion
        InstallationIdPreserved = $true
        UserDataPreserved = $true
        UpgradeVerified = $UpgradeVerified
        UpgradeRollbackVerified = $UpgradeRollbackVerified
        UninstallVerified = $true
    }
} finally {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host '::endgroup::' }
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
