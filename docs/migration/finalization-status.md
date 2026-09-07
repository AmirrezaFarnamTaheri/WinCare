# Project Finalization Status

**Version:** 2.5.0-rc5  
**Classification:** Native Source Release Candidate  
**Production Promotable:** No (gated by command-by-command Windows behavioral verification)

---

## Current readiness ledger

| Metric | Status | Details |
|---|:---:|---|
| **Stable Command IDs** | **259 / 259** | 100% parity with frozen oracle (`migration/oracle/legacy-command-ids.json`) |
| **Native Executor Routes** | **259 / 259** | Explicit routes in `WindowsCommandExecutor.cs` |
| **Catalog Entries at `Implemented`** | **259 / 259** | All 259 commands have typed request/result contracts |
| **Mutating Commands Preflighted** | **104 / 104** | Parameter validation executes during preview before approval |
| **`BehaviorVerified` command implementations** | **0 / 259** | Command-by-command live Windows parity evidence is still pending |
| **Native Implementation Blockers** | **0** | Native routing and fail-closed handling are implemented |
| **Production Verification Blockers** | **259** | Stable production promotion remains blocked until all 259 reach `BehaviorVerified` |
| **PowerShell Files in Native Roots** | **0** | Native application/runtime roots are C# and Rust |

Hosted CI now executes the packaged portable startup/core-flow smoke on both x64 and ARM64 Windows runners. That evidence is intentionally narrower than `BehaviorVerified`: it proves packaged startup, native ABI loading, runtime/plugin initialization, and a read-only dispatcher path, not every command's Windows behavior.

---

## Key milestones in this release candidate

1. **WinUI 3 desktop shell and visual system**: Fluent Mica surfaces, responsive layout breakpoints, keyboard/automation metadata, High Contrast support, and checked contrast contracts.
2. **Rust native engine and health guard**: bounded C-ABI primitives in `wincare-core` plus the experimental `wincare-guard` monitoring/IPC boundary.
3. **Modular plugin store and community SDK**: dynamic command dispatch, package/publisher admission metadata, revocation handling, and the Node.js developer CLI in `tools/wincare-plugin-cli`.
4. **AI System Doctor**: rule-based on-device intent classification, symptom parsing, two-phase diagnostic evidence collection, and fail-closed action plans.
5. **Fail-closed system boundaries**: reparse-point-aware cleanup, bounded process/filesystem operations, and privacy-conscious activity records.

---

## Deliberate behavior boundary

`security-control-reduce` and `security-control-restore` do not substitute Windows Firewall for unrelated legacy security controls. Temporary reduction remains unavailable until WinCare has a separately launchable recovery host that can authenticate snapshots and restore protection even if the WinUI process exits. No host mutation occurs on that blocked path.

---

## Artifact contract

Running `tools/finalize_native_release.py --mode rc` generates:

- `WinCare-<version>-native-source.zip`: native release source and audit evidence, excluding executable legacy PowerShell.
- `WinCare-<version>-legacy-oracle.zip`: isolated historical legacy oracle archive.
- `WinCare-<version>-finalization-report.md`: readiness and artifact-separation report.
- `WinCare-<version>-finalization-manifest.json`: machine-readable hashes, counts, provenance, and readiness metrics.

The GitHub Actions pipeline no longer has a second manual finalization workflow or a free-form `rc`/`production` mode input. `.github/workflows/native-winui.yml` derives finalization mode from the checked-in product version when an actual release is requested: prerelease versions such as `2.5.0-rc5` use `rc`; a stable version uses `production`.

> [!CAUTION]
> `--mode production` still exits non-zero until all 259 commands are `BehaviorVerified`. The workflow consolidation removes an inconsistent manual switch; it does not weaken the production readiness contract.
