#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$WorkRoot = (Join-Path $env:TEMP ('WinCare-install-lifecycle-' + [guid]::NewGuid().ToString('N')))
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-WinCareLifecyclePhase {
    param([Parameter(Mandatory)][string]$Name,[Parameter(Mandatory)][ValidateSet('started','passed','failed','info')][string]$Status,[string]$Message='')
    Write-Host "[lifecycle][$Status][$Name] $Message"
    if($env:GITHUB_ACTIONS -eq 'true' -and $Status -eq 'started'){Write-Host "::group::lifecycle/$Name"}
    if($env:GITHUB_ACTIONS -eq 'true' -and $Status -in @('passed','failed')){Write-Host '::endgroup::'}
}

$archive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path
$work = [IO.Path]::GetFullPath($WorkRoot)
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop }
New-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null
$extract = Join-Path $work 'extract'
$destination = Join-Path $work 'installed[1]\WinCare'
$shortcutRoot = Join-Path $work 'shortcuts[1]'
New-Item -ItemType Directory -Path $extract,$shortcutRoot -ErrorAction Stop | Out-Null

try {
    Write-WinCareLifecyclePhase 'validated-extraction' started 'verifying and extracting the final archive'
    $python = Get-Command python,python3,'C:\Program Files\Python311\python.exe' -ErrorAction SilentlyContinue |
        Where-Object Source -NotMatch 'WindowsApps' | Select-Object -ExpandProperty Source -First 1
    if (-not $python) { throw 'Python 3 is required by the installation lifecycle validator.' }
    & $python (Join-Path $PSScriptRoot 'verify_release.py') $archive --extract-to $extract
    if ($LASTEXITCODE -ne 0) { throw 'Validated lifecycle archive extraction failed.' }
    $roots = @(Get-ChildItem -LiteralPath $extract -Directory -Force -ErrorAction Stop)
    if ($roots.Count -ne 1) { throw 'Installation lifecycle archive must contain exactly one root directory.' }
    Write-WinCareLifecyclePhase 'validated-extraction' passed 'archive extracted with generic verifier'
    $source = $roots[0].FullName
    $installer = Join-Path $source 'Install-WinCare.ps1'
    $uninstaller = Join-Path $source 'Uninstall-WinCare.ps1'
    if (-not (Test-Path -LiteralPath $installer -PathType Leaf) -or -not (Test-Path -LiteralPath $uninstaller -PathType Leaf)) { throw 'Release archive does not contain installer lifecycle entrypoints.' }

    Write-WinCareLifecyclePhase 'whatif-forwarding' started 'proving wrapper common parameters preserve ShouldProcess behavior'
    & $installer -Destination $destination -ShortcutRoot $shortcutRoot -WhatIf -Confirm:$false
    if (Test-Path -LiteralPath $destination) { throw 'Install -WhatIf created the destination.' }
    if (Test-Path -LiteralPath (Join-Path $shortcutRoot 'WinCare')) { throw 'Install -WhatIf created shortcuts.' }
    Write-WinCareLifecyclePhase 'whatif-forwarding' passed 'install wrapper preserved WhatIf semantics'

    Write-WinCareLifecyclePhase 'force-unmanaged-guard' started 'proving -Force cannot overwrite an unrelated directory'
    $unmanaged = Join-Path $work 'unmanaged[1]\WinCare'
    [void][IO.Directory]::CreateDirectory($unmanaged)
    Set-Content -LiteralPath (Join-Path $unmanaged 'foreign.txt') -Value 'preserve' -Encoding utf8NoBOM
    $unmanagedRejected = $false
    try { & $installer -Destination $unmanaged -NoStartMenuShortcut -Force -Confirm:$false } catch { $unmanagedRejected = $true }
    if (-not $unmanagedRejected) { throw 'Installer -Force overwrote an unmanaged destination.' }
    if (-not (Test-Path -LiteralPath (Join-Path $unmanaged 'foreign.txt') -PathType Leaf)) { throw 'Unmanaged destination guard did not preserve foreign content.' }
    Write-WinCareLifecyclePhase 'force-unmanaged-guard' passed 'unmanaged destination remained untouched'

    Write-WinCareLifecyclePhase 'shortcut-conflict-preservation' started 'proving unmanaged shortcut namespaces are rejected and preserved'
    $foreign = Join-Path $shortcutRoot 'WinCare'
    New-Item -ItemType Directory -Path $foreign -ErrorAction Stop | Out-Null
    Set-Content -LiteralPath (Join-Path $foreign 'foreign.txt') -Value 'preserve' -Encoding utf8NoBOM
    $conflictRejected = $false
    try { & $installer -Destination $destination -ShortcutRoot $shortcutRoot -Confirm:$false } catch { $conflictRejected = $true }
    if (-not $conflictRejected) { throw 'Installer did not reject an unmanaged shortcut namespace conflict.' }
    if (-not (Test-Path -LiteralPath (Join-Path $foreign 'foreign.txt') -PathType Leaf)) { throw 'Installer modified an unmanaged shortcut namespace.' }
    Remove-Item -LiteralPath $foreign -Recurse -Force
    Write-WinCareLifecyclePhase 'shortcut-conflict-preservation' passed 'unmanaged shortcut content remained intact'

    Write-WinCareLifecyclePhase 'clean-install-and-self-tests' started 'installing release and running all standalone self-tests'
    & $installer -Destination $destination -ShortcutRoot $shortcutRoot -Confirm:$false
    foreach ($name in @('WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe','.wincare-install.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $destination $name) -PathType Leaf)) { throw "Installed lifecycle artifact is missing: $name" }
    }
    foreach ($name in @('WinCare.exe','WinCare-GUI.exe','WinCare-TUI.exe')) {
        $process = Start-Process -FilePath (Join-Path $destination $name) -ArgumentList '--wincare-self-test' -Wait -PassThru -NoNewWindow
        try { if ($process.ExitCode -ne 0) { throw "Installed standalone self-test failed: $name exit=$($process.ExitCode)" } }
        finally { $process.Dispose() }
    }
    $ownerPath = Join-Path $shortcutRoot 'WinCare\.wincare-shortcuts.json'
    if (-not (Test-Path -LiteralPath $ownerPath -PathType Leaf)) { throw 'Managed shortcut ownership evidence is missing.' }
    $owner = [IO.File]::ReadAllText($ownerPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json -Depth 20
    foreach ($record in @($owner.Shortcuts)) {
        if ($null -eq $record.Arguments -or [string]$record.Arguments -ne '') { throw "Shortcut owner record is missing the empty Arguments contract: $($record.Name)" }
        if ([string]$record.WorkingDirectory -ne $destination) { throw "Shortcut owner record has the wrong working directory: $($record.Name)" }
    }
    & $uninstaller -Destination $destination -ShortcutRoot $shortcutRoot -WhatIf -Confirm:$false
    if (-not (Test-Path -LiteralPath $destination -PathType Container)) { throw 'Uninstall -WhatIf removed the destination.' }
    if (-not (Test-Path -LiteralPath (Join-Path $shortcutRoot 'WinCare') -PathType Container)) { throw 'Uninstall -WhatIf removed shortcuts.' }
    Write-WinCareLifecyclePhase 'clean-install-and-self-tests' passed 'installation, literal paths, shortcut ownership, WhatIf, and three executable self-tests passed'

    Write-WinCareLifecyclePhase 'repair' started 'removing one manifested file and validating exact repair'
    $repairTarget = Join-Path $destination 'README.md'
    if (-not (Test-Path -LiteralPath $repairTarget -PathType Leaf)) { $repairTarget = Join-Path $destination 'WinCare.ps1' }
    Remove-Item -LiteralPath $repairTarget -Force
    & $installer -Destination $destination -ShortcutRoot $shortcutRoot -Repair -Confirm:$false
    if (-not (Test-Path -LiteralPath $repairTarget -PathType Leaf)) { throw 'Repair did not restore a missing manifested file.' }
    Write-WinCareLifecyclePhase 'repair' passed 'repair restored the manifested file'

    Write-WinCareLifecyclePhase 'force-recovery' started 'recovering a recognizable WinCare tree with a missing marker'
    Remove-Item -LiteralPath (Join-Path $destination '.wincare-install.json') -Force
    $recovery = & $installer -Destination $destination -ShortcutRoot $shortcutRoot -Force -Confirm:$false
    if ($recovery.Operation -ne 'recover') { throw "Expected force recovery operation, got: $($recovery.Operation)" }
    if (-not (Test-Path -LiteralPath (Join-Path $destination '.wincare-install.json') -PathType Leaf)) { throw 'Force recovery did not restore the installation marker.' }
    Write-WinCareLifecyclePhase 'force-recovery' passed 'managed recovery completed without weakening the unmanaged-directory guard'

    Write-WinCareLifecyclePhase 'clean-uninstall' started 'validating strict clean uninstall, shortcut cleanup, and non-destructive data purge'
    $savedLocalAppData = $env:LOCALAPPDATA
    $testLocalAppData = Join-Path $work 'localappdata[1]'
    $unmanagedData = Join-Path $testLocalAppData 'WinCare'
    [void][IO.Directory]::CreateDirectory($unmanagedData)
    Set-Content -LiteralPath (Join-Path $unmanagedData 'foreign-data.txt') -Value 'preserve' -Encoding utf8NoBOM
    try {
        $env:LOCALAPPDATA = $testLocalAppData
        $uninstallResult = & $uninstaller -Destination $destination -ShortcutRoot $shortcutRoot -PurgeData -Confirm:$false
    }
    finally {
        $env:LOCALAPPDATA = $savedLocalAppData
    }
    if (Test-Path -LiteralPath $destination) { throw 'Clean uninstall left the installation directory behind.' }
    if (Test-Path -LiteralPath (Join-Path $shortcutRoot 'WinCare')) { throw 'Clean uninstall left managed shortcuts behind.' }
    if ($uninstallResult.PurgedData) { throw 'Uninstaller claimed it purged unrecognized data.' }
    if (-not (Test-Path -LiteralPath (Join-Path $unmanagedData 'foreign-data.txt') -PathType Leaf)) { throw 'Uninstaller removed unrecognized data.' }
    Write-WinCareLifecyclePhase 'clean-uninstall' passed 'installation and managed shortcuts were removed while unrecognized data was preserved'

    Write-WinCareLifecyclePhase 'corrupt-installation-removal' started 'proving strict uninstall rejects corruption and explicit recovery removes it'
    & $installer -Destination $destination -ShortcutRoot $shortcutRoot -Confirm:$false
    Add-Content -LiteralPath (Join-Path $destination 'WinCare.ps1') -Value '# lifecycle corruption'
    $untrustedModule = Join-Path $destination 'src\WinCare\Install\WinCare.Installation.psm1'
    Set-Content -LiteralPath $untrustedModule -Value '$env:WINCARE_UNTRUSTED_MODULE_EXECUTED = ''1''' -Encoding utf8NoBOM
    Remove-Item Env:WINCARE_UNTRUSTED_MODULE_EXECUTED -ErrorAction SilentlyContinue
    $strictRejected = $false
    try { & $uninstaller -Destination $destination -ShortcutRoot $shortcutRoot -Confirm:$false } catch { $strictRejected = $true }
    if (-not $strictRejected) { throw 'Strict uninstall accepted a corrupted installation.' }
    if ($env:WINCARE_UNTRUSTED_MODULE_EXECUTED) { throw 'Uninstaller executed a module from the untrusted destination.' }
    & $uninstaller -Destination $destination -ShortcutRoot $shortcutRoot -RemoveCorruptInstallation -Confirm:$false
    if ($env:WINCARE_UNTRUSTED_MODULE_EXECUTED) { throw 'Corrupt-removal flow executed a module from the untrusted destination.' }
    if (Test-Path -LiteralPath $destination) { throw 'Corrupt-installation removal left the installation directory behind.' }
    Write-WinCareLifecyclePhase 'corrupt-installation-removal' passed 'strict rejection, trusted module loading, and explicit corrupt removal all passed'

    [pscustomobject]@{
        Schema = 'wincare.installation.lifecycle/v1'
        Status = 'passed'
        Archive = $archive
        ShortcutConflictPreserved = $true
        RepairVerified = $true
        CleanUninstallVerified = $true
        WhatIfVerified = $true
        LiteralPathVerified = $true
        ForceRecoveryVerified = $true
        UnmanagedForceGuardVerified = $true
        TrustedUninstallerModuleVerified = $true
        UnrecognizedDataPreserved = $true
        CorruptRemovalVerified = $true
    } | ConvertTo-Json -Depth 8
} catch {
    Write-WinCareLifecyclePhase 'lifecycle' failed $_.Exception.Message
    throw
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
