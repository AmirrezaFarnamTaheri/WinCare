# WinCare 5.2 validation status

## Verified in this build environment

- Safe member validation and bounded extraction for the four newly supplied archives: **103 archive members**, **87 regular files**, **zero symlinks**, **zero validation warnings/errors**, and **710,936 expanded bytes**. Prior D01-D105 archive evidence remains cumulative and unchanged.
- Exact cumulative evidence for **166 submitted donor IDs / 153 unique archive baselines**, **52,629 classified surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**.
- Bidirectional donor-surface links, per-surface hashes, target/test path existence, complete donor coverage through D109, canonical donor-ID validation, strict dispositions, and acyclic parent/dependency graphs.
- Lexical delimiter, duplicate-function, unresolved-call, module export/version, JSON, policy, route, contract, and dispatcher checks across **139 PowerShell files** and **830 discovered WinCare functions**.
- Exact **109-action** contract/dispatcher parity, **177-command** headless declaration/case parity, and **46-action** menu/route parity.
- Python compilation, convergence-validator D100+ regression tests, and deterministic release-tool tests, including adversarial ZIP traversal, duplicate/case collision, link, membership, expansion-bound, and reproducibility cases.

Machine-readable reports are written to `docs/convergence/` and copied into `evidence/` for release.

## Included but pending Windows-native verification

This build environment is Linux and does not provide a Windows PowerShell runtime. The following are included but **not claimed as executed**:

- PowerShell AST parsing, module import/export, PSScriptAnalyzer, and the **159 inventoried Pester `It` cases**, including `tests/Wave6.WorkspaceStudio.Tests.ps1` and `tests/Wave7.CleanupMods.Tests.ps1`.
- Registry, WMI brightness, desktop.ini attributes, Windows process/service/event providers, Syncthing local configuration, exact ADB/ViVeTool execution, AppX current/all-user/provisioned/offline-image mutation, Windows Update cache cleanup, component-store cleanup, UAC brokering, Windows restart, and interactive TUI behavior.
- The Xbox full-screen mutation: exact feature IDs, OEM `DeviceForm`, preview binding, policy gates, acknowledgement, compensators, reboot handling, and post-state verification.
- Prior Legacy Unsafe Registry, Defender, SmartScreen, UAC, Windows Update, firewall, services, tasks, hosts, AppX, hibernation, display override/cache reset, and conflict-aware recovery paths.
- Installer, upgrade, clean-install, and uninstall behavior on supported Windows 10/11 systems.

## Mandatory Windows release gate

Run against the exact release bytes in Windows Sandbox or a disposable VM, first as a standard user and then as administrator:

```powershell
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
.\tools\Test-ReleaseArchive.ps1 -ArchivePath .\artifacts\WinCare-4.8.0.zip
```

Exercise `studio-xbox-fse`, `windows-cleaner-utility-all`, `remove-ms-store-apps-all`, `windows-repo-nuclear`, and all other Legacy Unsafe profiles only in a disposable environment unless the operator accepts the disclosed blast radius. Verify default denial, exact executable hash, argument allowlist, preview receipt, exact acknowledgement, elevation, post-state, journal integrity, compensators, conflict rejection, reboot behavior, and irreversible outcomes.

## WinCare 4.7 focused validation

Wave-eight source assertions cover the 12 new headless routes, exported toolkit functions, three hardening levels, three Critical one-click compositions, JSON batch bounds, local-only torrent parsing, state-root shortcut export, reuse of the authoritative maintenance/download stores, personalization catalog rules, and the absence of donor elevation or networking engines. Windows-native AST/Pester and runtime exercises remain required on a disposable Windows host.

## WinCare 5.2 focused validation

Static validation covers the new Experience Studio provider, 15 new headless routes, module exports, TUI routing, duplicate-donor provenance, bounded sprite/layout inputs, location service/permission separation, exact RDP/OpenSSH identities, BITS-only download strategy, privacy profile rule resolution, and the `rytunex-maximum` / `privacy-sexy-maximum` Critical profiles. `tests/Wave9.ExperienceStudio.Tests.ps1` is included for Windows/Pester execution.

Linux-side source validation currently measures **151 PowerShell files**, **960 functions**, **116 typed action contracts**, **236 headless commands**, and **47 routed TUI actions**. Cumulative evidence validation measures **166 submitted donor IDs**, **153 unique archive baselines**, **615 semantic records**, **52,629 surfaces**, **436 composition groups**, **355 target nodes**, and **172 test nodes**. PowerShell AST/Pester and Windows-native Registry, services, DISM, RDP, OpenSSH, brightness, battery, UAC, and TUI execution remain pending on a disposable Windows host.

## WinCare 5.2 measured validation inventory

- **217 Pester `It` cases** across **18 suites**, including 14 Shell & Hardware Studio cases and 12 V5 GUI cases.
- **23 Python release/convergence/V5 unit tests**.
- Static source inventory: **151 PowerShell files**, **960 functions**, **116 typed action contracts**, **236 headless commands**, and **47 routed TUI actions**.
- Cumulative evidence: **166 submitted donor IDs**, **153 unique archive baselines**, **615 semantic records**, **52,629 classified surfaces**, **436 composition groups**, **355 target nodes**, and **172 test nodes**.
- Repository symlink accountability: **15**, all recorded and not materialized.
