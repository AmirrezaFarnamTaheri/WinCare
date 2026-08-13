# Project finalization status

**Version:** 2.4.0-rc1  
**Classification:** native source release candidate  
**Production promotable:** no

## Completed

- Approved WinUI 3 navigation, page tabs, responsive tables, themes, and accessibility metadata
- C# application, domain, infrastructure, and command-catalog boundaries
- Rust workspace and versioned C ABI foundation
- Exact preservation of all 259 stable command IDs
- Typed, fail-closed command dispatch
- Native read-only `catalog`, `presets`, `system`, `storage`, `network`, `experience-privacy-profiles`, `cleaner-disk-pressure`, `cleanup-targets`, `startup`, `security`, `applications`, `health`, `pagefile`, `tcp-global`, `pagefile-recommendation`, `security-controls`, `network-measure`, `process-modules`, `security-maintenance`, `network-experiments`, `etw-sessions`, `wua-history`, `injection-surfaces`, `remote-thread-events`, `wua-search`, `preset`, `wdac-policies`, `wdac-events`, `internals-processes`, `wua-hide`, `internals-memory`, `internals-cpu`, `credential-providers`, `appcontainer`, `pagefile-set`, `security-control-reduce`, `security-control-restore`, `network-experiment`, `etw-capture`, `injection-surface-quarantine`, `wua-unhide`, `wua-download`, `wua-install`, `wua-uninstall`, `wdac-deploy`, `desktop-controls`, `wifi-profiles`, `shell-extensions`, `desktop-shortcuts`, `boot`, `bcd-export`, `unattend-analyze`, `provisioning-plan`, `knowledge`, `reports`, `automation-profiles`, `run-automation`, `cancel-operation`, `widgets`, `widget-catalog`, `widget-export`, `bluetooth`, `bluetooth-events`, `maintenance`, `maintenance-create`, `maintenance-transition`, `maintenance-metrics`, `maintenance-export`, `context-menu`, `context-menu-set`, `customization-hosts`, `rainmeter-skins`, `windhawk-mods`, `explorerpatcher`, `modern-context-validate`, `playbooks`, `playbook`, `group-policy`, `group-policy-import`, `sysmon`, `sysmon-configure`, `sysmon-uninstall`, `offline-images`, `offline-drivers`, `offline-packages`, `offline-features`, `offline-driver-add`, `offline-driver-remove`, `offline-package-add`, `offline-feature-set`, `power-sessions`, `power-start`, `power-stop`, and `windows` handlers
- Frozen non-executable parity fixtures with provenance
- Deterministic native-source and legacy-oracle archives
- Zero PowerShell files in the native source archive
- Linux-capable structural and finalization tests
- Windows CI definitions for Rust, managed tests, MSIX, and portable builds
- Manual release workflow with separate RC and production modes

## Not completed

- Native handlers for 165 commands
- Windows behavioral verification for all 259 commands
- Windows UI Automation evidence for the final source tree
- Signed and timestamped x64 and ARM64 release packages
- Representative-device startup measurements
- Production promotion

## Release accounting

| Gate | Result |
|---|---|
| Command ID parity | 259 of 259 |
| Contract verification | 94 of 259 |
| Implementation readiness | 94 of 259 |
| Native source PowerShell files | 0 |
| Production blockers | 259 behavior-verification gaps |

## Artifact contract

`tools/finalize_native_release.py --mode rc` creates:

- `WinCare-<version>-native-source.zip`
- `WinCare-<version>-legacy-oracle.zip`
- `WinCare-<version>-finalization-report.md`
- `WinCare-<version>-finalization-manifest.json`

`--mode production` exits non-zero unless every command is `BehaviorVerified`.

## Honest completion boundary

This candidate finalizes the architecture, native user interface, repository separation, deterministic source artifacts, and release controls. It does not claim that 257 missing command implementations or 259 missing Windows behavior comparisons are complete.
