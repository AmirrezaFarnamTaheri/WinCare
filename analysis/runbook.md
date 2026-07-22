# WinCare 5.2 release and operator runbook


## Wave eleven: D164-D166 operator sequence

The eleventh convergence wave brings cumulative coverage to **166 submitted donor IDs**, **153 unique archive baselines**, **52,629 classified surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**. D165 is an exact duplicate of D143 and remains provenance-only.

Operate the new plane in this order: inspect disk pressure and preview profiles first; validate any Winapp2 catalog without applying it; preview exact cleanup manifests; verify relocation process state, source/destination hashes, and recovery paths; and keep active formats metadata-only. Apply mutation plans only after policy and receipt validation. Exercise deletion-race, reparse escape, partial relocation, stale junction, archive-bomb, XML-entity, remote-path, and active-content negative cases in a disposable Windows environment before production use.

## Wave-six operator sequence

1. Run source and convergence validation and confirm D01-D109 coverage.
2. Exercise Studio read-only routes before any mutation.
3. Admit ADB/ViVeTool only by exact local path and SHA-256.
4. Exercise folder/brightness actions in a disposable profile with recovery conflict tests.
5. Exercise `studio-xbox-fse` only in Windows Sandbox or a disposable VM, with the exact acknowledgement and reboot validation.
6. Build once from a clean source commit, freeze the ZIP, and independently verify the same bytes.


## Wave five: D70-D86 peer utilities and Legacy Unsafe

The fifth wave adds 17 donors and 1,299 fully classified file surfaces. Material value is decomposed into display override inventory/reset/recovery, WLAN survey, bounded log inspection and regex filtering, non-executing PE metadata, strict container log configuration, local task-board state, and donor-derived one-click mutation presets. Existing WinCare window, browser, workspace-layout, telemetry, and TUI mechanisms supersede donor-specific resident applications where they already provide the outcome.

Unsafe donor behavior is retained as executable target-native behavior rather than silently rejected: permanent security reductions, firewall shutdown, broad service/task suppression, hosts blocking, aggressive AppX removal, hibernation disablement, privacy/performance rules, and display override/cache reset. The composition preserves one authoritative action path, default-deny policy gates, exact acknowledgement, preview receipts, UAC brokering, journaling, post-state verification, and conflict-aware recovery where recovery exists. Raw donor scripts and binaries remain non-authoritative evidence.
## Source gate

From the repository root:

```powershell
python .	oolsalidate_source.py . --json-output .\docs\convergence\source-validation.json
python .	oolsalidate_convergence.py . --evidence-dir .\evidence --output .\docs\convergence\convergence-validation.json
.	ools\Invoke-StaticChecks.ps1 -RequirePSScriptAnalyzer
Invoke-Pester .	ests -CI -Output Detailed
```

The gate must report exact action/dispatcher, headless declaration/case, menu/route, module export, version, policy, schema, and evidence parity; no unresolved internal calls; and no empty catches, placeholder implementations, unsafe archive paths, or transient build state.

## Focused Windows exercises

### Downloads

1. Create a checksum-bound HTTPS job targeting a disposable directory.
2. Verify preview receipt, queue record, BITS job identity, progress, pause, resume, reconciliation, and completion.
3. Exercise a mirror failover and a checksum mismatch.
4. Confirm destination replacement uses staging/tombstone recovery and preserves prior content after injected failure.
5. Validate scheduled/due jobs and policy/config concurrency and byte intersections.

### Telemetry

1. Capture two samples under CPU, disk, and network load.
2. Verify counter normalization and history bounds.
3. Confirm public report/export omits private helper fields.
4. Exercise cancellation and unavailable counter classes.

### Launcher

1. Index Start-menu, App Paths, Control Panel, and Settings targets.
2. Verify reparse rejection and bounded traversal.
3. Launch an exact target, then alter its path/hash and confirm stale-plan rejection.
4. Test arithmetic grammar with valid expressions and command/code payloads.

### Steam/game state

1. Create a disposable Steam-like local cloud tree and exact backup.
2. Add unlisted, duplicate, traversal, oversized, and hash-forged ZIP members; all must fail admission.
3. Confirm restore refuses while Steam/steamwebhelper is active.
4. Inject a mid-restore failure and verify provider-internal rollback.
5. Run defensive findings and confirm no trainer/injection action exists.

### Offline reduction

1. Mount disposable supported Windows images read-write.
2. Validate all built-in profiles against build/architecture and exact identities.
3. Exercise AppX and optional-feature plans and verify state.
4. Confirm component cleanup discloses non-reversibility and ResetBase is denied.
5. Reject unhealthy/read-only mounts and unexpected package/feature state.

### Workspace layouts

1. Create/update/delete layouts through revisioned state.
2. Test ratio overlap, out-of-range geometry, invalid and catastrophic regexes.
3. Apply across multi-monitor/mixed-DPI arrangements.
4. Reuse a stale handle/process identity and change monitor work area after preview; both must block.
5. Inject failure after one window and verify reverse-order compensation.

## Full Windows validation

```powershell
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
```

The orchestrator must capture OS/build/architecture/culture/elevation/capabilities, parser/import results, PSScriptAnalyzer, Pester, read-only headless smoke, cleanup preview, release build, package verification, isolated install/import/headless smoke/uninstall, and a machine-specific JSON evidence report.

## Deterministic release

Build twice from the same clean commit when reproducibility is being audited:

```powershell
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts\build-a
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts\build-b
```

Require byte-identical archives. Build the promotion candidate once from the frozen clean source, then run independent verification on those exact bytes:

```powershell
.\tools\Test-ReleaseArchive.ps1 -ArchivePath .\artifacts\WinCare-4.8.0.zip
python .\tools\verify_release.py .\artifacts\WinCare-4.8.0.zip --output .\artifacts\WinCare-4.7.0-archive-verification.json
python -m unittest tools.test_release_tools tools.test_convergence_tools
```

Any byte change invalidates the checksum, receipt, SBOM, provenance, archive verification, and clean-install evidence.

## Incident and rollback guidance

- Do not bypass a blocked plan by editing journals or records.
- Preserve rollback archives and journal IDs when Steam or download finalization fails.
- Cancel BITS through the typed action so queue and native job state reconcile.
- Treat mounted-image servicing failures as servicing incidents and retain DISM logs.
- Do not start Steam after an unverified restore until the rollback archive is reviewed.
- Export reports/support bundles with redaction before sharing.

## WinCare 4.7 exact release commands

```powershell
python .\tools\validate_source.py . --json-output .\docs\convergence\source-validation.json
python .\tools\validate_convergence.py . --output .\docs\convergence\convergence-validation.json
python .\tools\build_release.py . --output-directory .\artifacts
python .\tools\verify_release.py .\artifacts\WinCare-4.8.0.zip --output .\artifacts\WinCare-4.7.0-archive-verification.json
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
```

The final promotion candidate must be built from a clean commit and the same ZIP bytes must pass archive and Windows-native validation. Legacy Unsafe profiles should first be exercised in Windows Sandbox or a disposable VM.

## WinCare 4.7 operator additions

Use `explorer-quick`, `deep-clean-profiles`, `deep-clean`, `appx-selection-profiles`, `appx-selection-assess`, `appx-selection`, and `offline-appx-selection` for the new bounded workflows. Preview first. Apply destructive profiles only with `-Apply` and `Acknowledgement='I ACCEPT LEGACY UNSAFE MUTATIONS'`, preferably in Windows Sandbox or a disposable VM with a verified backup or restore point.

## WinCare 4.7 focused operator checks

1. Run `toolkit-diagnostics` and confirm no mutation occurs.
2. Inspect `hardening-assess` before applying any hardening plan; test Hail Mary only on a disposable or tightly managed host.
3. Validate a small download-batch JSON in preview mode, then confirm destinations, hashes, schedules, and collision policies before apply.
4. Parse a known `.torrent` and verify the output reports `Network execution: Not implemented`; no socket or tracker traffic should occur.
5. Create a maintenance template record and exercise Scheduled -> Notice -> Active -> Completed plus cancellation and stale-revision conflicts.
6. Verify exact rollback for all adopted personalization rules.

## Wave 11 Windows validation

1. Exercise Conservative/Balanced/Emergency pressure plans on disposable target data and confirm excluded system locations are untouched.
2. Test benign and malicious Winapp2 catalogs, stale catalog/file races, root escape, separators, over-limit inputs, and partial access failures.
3. Relocate each built-in profile in a disposable account; inject copy, hash, junction, and rollback failures and verify exact recovery.
4. Preview empty, large, malformed, active, reparse, UNC, archive-bomb, XML-DTD, PE, image, PDF, and directory samples.
5. Verify registered preview handlers are enumerated without module loads.
