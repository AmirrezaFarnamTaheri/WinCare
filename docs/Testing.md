# Testing and release validation

## Source and evidence gates

1. Parse every PowerShell file with the PowerShell AST on Windows.
2. Validate module manifest/import/exports and semantic version consistency.
3. Reconcile every typed action contract with exactly one dispatcher.
4. Reconcile every visible menu action and declared headless command with a route.
5. Reject duplicate functions, unresolved internal calls, unknown JSON, placeholder markers, dynamic evaluation and shell-string execution.
6. Validate UAC authentication markers, WDAC no-direct-write invariant and telemetry prohibition.
7. Validate all 615 convergence records and all 52,629 donor surface links/hashes.
8. Run PSScriptAnalyzer and Pester.
9. Build deterministically and independently verify ZIP structure, internal manifest, SBOM, checksum and receipt.

## Test suites

The current repository contains **159** `It` cases across the suites below. They are inventoried here but remain pending Windows execution.

- `Core.Contracts.Tests.ps1` — action/plan/config/policy contracts and restart state.
- `Safety.Recovery.Tests.ps1` — path confinement, hashing, protected records, moves, conditions and recovery.
- `Providers.Admission.Tests.ps1` — catalog, presets, exact AppX identities, provisioning and automation admission.
- `SupplyChain.Protocol.Tests.ps1` — extension admission, broker authentication and uninstall safety.
- `UI.Release.Tests.ps1` — routes, headless completeness, accessibility and metadata.
- `Convergence.Evidence.Tests.ps1` — cumulative evidence counts, bidirectional links, target/test nodes and 52-donor coverage.
- `Wave2.Convergence.Tests.ps1` — widgets, Bluetooth, maintenance, playbooks, context-menu, offline servicing, LGPO/Sysmon, and broker boundaries.
- `Expert.Convergence.Tests.ps1` — page-file, expiring security maintenance, measured TCP, ETW/process evidence, defensive quarantine, policy, cancellation, and forbidden-injection contracts.
- `Wave6.WorkspaceStudio.Tests.ps1` — package admission, verified-tool boundaries, folder recovery, Studio routes, policy gates, Xbox exact values/acknowledgement, and D87-D105 evidence.
- `Wave7.CleanupMods.Tests.ps1` — default-deny destructive profiles, cleanup/AppX routes, typed contracts, bounded list grammar, exact acknowledgement, excluded evidence-destroying sweeps, service coordination, and `ResetBase` denial.
- `Wave3.Productivity.Tests.ps1` — power-host integrity, window/input contracts, palette/note/consent corruption and rollback, image bounds, browser permission risk, remote-support privacy, routes, broker admission, and data-only aliases.

## Windows matrix

- Windows 10 22H2 and supported Windows 11 builds;
- PowerShell 7.2 and current stable PowerShell 7;
- x64 and ARM64 where available;
- standard-user and administrator sessions;
- local and Microsoft accounts; domain-joined/restricted policy where available;
- WinGet/Store/Defender present, absent, passive or policy restricted;
- Windows Terminal and Console Host; narrow/standard/wide terminals;
- normal/high-contrast/monochrome/ASCII/screen-reader/reduced-motion modes;
- online and offline operation.

## Destructive and negative-path exercises

Use only Windows Sandbox or disposable VMs. Seed disposable Registry values, startup entries, files, AppX packages, applications, WUA test conditions and WDAC audit policies. Exercise:

- preview/apply/read-only policy;
- UAC approval, cancellation, stale/replayed/tampered requests and result mismatch;
- exact native arguments, timeouts, child processes and non-zero exits;
- path substitution, reparse points, destination conflicts and hash mismatch;
- cancellation between WUA items and restart-required results;
- journal mutation, interruption, duplicate action, idempotency and compensation failure;
- extension traversal, duplicate/case-collision/link, file-list/hash/signer tampering;
- partial backup/restore and disk-full conditions;
- Windows restart and recovery reconciliation;
- installer upgrade rollback and uninstall identity checks;
- report redaction and support-bundle contents;
- page-file crash-dump conflicts, custom-size rollback, and restart-required recovery;
- security-maintenance expiry/logon restoration, Tamper Protection refusal, module upgrade during an active record, and service-state restoration;
- TCP baseline failure, DNS/connect timeout, cancellation, accepted/rejected thresholds, history authentication, and recovery conflict;
- ETW cancellation/output verification and interception-snapshot tampering or out-of-scope Registry targets;
- power host crash, stale heartbeat, expiry/stop races, module-tree tampering, and indefinite/away-mode policy refusal;
- reused HWND/process identities, monitor changes, stale bounds/topmost state, failed native mutation rollback, and modifier release;
- corrupt palette/consent records, oversized images, note snapshot tampering/private-report redaction, browser inventory limits, broad manifest permissions, and exact remote-process termination identity.

## Commands

```powershell
.\tools\Invoke-StaticChecks.ps1 -RequirePSScriptAnalyzer
Invoke-Pester .\tests -CI -Output Detailed
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts
.\tools\Test-ReleaseArchive.ps1 -ArchivePath .\artifacts\WinCare-4.8.0.zip
```

Executed and pending evidence are reported separately in `VALIDATION.md`.

## WinCare 4.7 focused native exercises

On a disposable Windows VM, additionally exercise:

- BITS create/start/suspend/resume/error/mirror/reconcile/cancel, future scheduling, due-job batches, concurrency saturation, configured-versus-policy byte ceilings, HTTP denial, sensitive-header denial, checksum mismatch, destination collision, disk-full staging, tombstone restoration, and host restart persistence;
- telemetry first/subsequent samples, formatted disk counters, adapter counter reset, process access failures, cancellation, history truncation, corrupt history refusal, and report/export redaction;
- Start-menu reparse trees, stale target hashes, removed App Paths, malformed search, unsafe calculator input, and Settings URI launch;
- Steam archive traversal/duplicates/unlisted entries/missing files/hash and size mismatch, Steam-running refusal, partial restore failure, rollback failure, extra current-file deletion, and final snapshot mismatch;
- mounted-image read-only/unhealthy state, missing exact packages/features, feature compensation, provisioned-package failure, component cleanup, and ResetBase denial;
- malformed/catastrophic title regexes, stale layouts, missing required windows, changed HWND/process identity, monitor work-area changes, partial multi-window failure, and reverse-order bounds restoration.

## Legacy Unsafe native exercises

Use Windows Sandbox or a disposable VM. Confirm default policy denial, exact acknowledgement, Critical confirmation, preview-hash binding, UAC envelope validation, permanent security-control state hashes, firewall/service/task/hosts/AppX/hibernation/display side effects, journal integrity, post-state verification, conflict-aware compensators, and explicit irreversible receipts.


## Workspace and Device Studio native exercises

In Windows Sandbox or a disposable VM, exercise bounded package inspection with traversal/collision/oversize fixtures; file-workspace revision conflicts; brightness schedule application and unavailable-monitor behavior; folder icon/color application, attribute restoration, and conflict refusal; Kanata include confinement; Syncthing API-key redaction; generated WezTerm syntax; exact-hash ADB inventory with unauthorized arguments; and Xbox full-screen default denial, wrong-hash denial, wrong-acknowledgement denial, exact feature/registry mutation, reboot, recovery, and state conflict.

## WinCare 5.2 focused native exercises

On a disposable Windows host, run `tests/Wave9.ExperienceStudio.Tests.ps1`, then exercise visual manifest creation, location allow/deny restoration, RDP/OpenSSH enable-disable restoration, battery/AC profile recommendation, privacy profile preview/apply/undo, and both new Critical profiles. Confirm exact acknowledgement, policy denial, stale preview rejection, UAC binding, journal integrity, and disclosed non-recoverable steps.

## Current inventory

WinCare 5.2 contains **217** Pester `It` cases across **18** suites and **28** Python release/convergence/V5/Wave 11 tests. Static validation covers **151 PowerShell files**, **960 functions**, **116 typed action contracts**, **236 headless commands**, and **47 routed TUI actions**.
