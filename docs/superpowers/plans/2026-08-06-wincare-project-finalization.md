# WinCare Project Finalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce an auditable zero-PowerShell native source release candidate, preserve the legacy implementation as a separate immutable parity oracle, and make production promotion fail closed until all 259 commands are behavior-verified.

**Architecture:** The migration workspace remains the evidence source. A Python finalizer stages a clean native repository from an explicit allowlist, copies frozen JSON parity fixtures rather than executable legacy scripts, creates deterministic archives, writes a manifest and readiness report, and refuses production mode unless every command is `BehaviorVerified`. Existing C#/Rust/WinUI projects remain unchanged except for verification inputs and release metadata.

**Tech Stack:** Python 3.13, deterministic ZIP packaging, JSON parity fixtures, GitHub Actions, C#/.NET 8 source, WinUI 3, Rust.

## Global Constraints

- Preserve all 259 existing command IDs.
- The native source archive must contain zero `.ps1`, `.psm1`, and `.psd1` files.
- The legacy implementation must not be invoked, embedded, or required by native runtime projects.
- The legacy implementation remains available only as a separate immutable oracle archive.
- Production promotion must fail unless all 259 commands are `BehaviorVerified`.
- Release-candidate mode may package incomplete migration source only when the report states exact migration counts and blockers.
- Archives must be deterministic, sorted, symlink-free, and use fixed metadata.
- Do not claim C#, Rust, WinUI, UI Automation, MSIX, or Windows behavior verification from Linux-only checks.
- Do not add PowerShell-based build, release, or test tooling.

---

### Task 1: Freeze non-executable parity fixtures

**Files:**
- Create: `migration/oracle/legacy-command-ids.json`
- Create: `migration/oracle/remediation-rules.json`
- Create: `migration/oracle/presets.json`
- Create: `migration/oracle/provenance.json`
- Modify: `tools/verify_native_foundation.py`
- Modify: `tests/native/test_native_foundation.py`
- Modify: `tests/native/test_command_runtime.py`

**Interfaces:**
- Consumes: authoritative legacy command arrays and built-in catalog JSON from the migration workspace.
- Produces: immutable JSON fixtures usable by the zero-PowerShell native repository.

- [x] **Step 1: Write failing tests requiring the frozen fixture files and requiring verification to avoid reading `.ps1` files.**
- [x] **Step 2: Run the native tests and confirm failure because the fixtures do not exist.**
- [x] **Step 3: Generate the four fixtures with 259 unique IDs, 69 rules, 7 presets, source commit `83567c4dbf3cf85e44217855d39604b0193623e3`, and SHA-256 hashes of the original source files.**
- [x] **Step 4: Update the verifier and structural tests to use only the frozen JSON fixtures.**
- [x] **Step 5: Run the tests and verifier and confirm they pass.**

### Task 2: Add fail-closed release finalization

**Files:**
- Create: `tools/finalize_native_release.py`
- Create: `tests/native/test_finalization.py`
- Modify: `tools/package_portable.py`

**Interfaces:**
- Consumes: repository root, command catalog, explicit native-source allowlist, and migration oracle paths.
- Produces: `FinalizationResult`, deterministic native and oracle archives, `finalization-report.md`, and `finalization-manifest.json`.

- [x] **Step 1: Write failing tests for zero-PowerShell native staging, deterministic ZIP metadata, oracle separation, exact 259-command counts, and production refusal while parity is incomplete.**
- [x] **Step 2: Run the finalization tests and confirm failure because the finalizer does not exist.**
- [x] **Step 3: Implement explicit source staging, exclusion of caches/build artifacts, symlink rejection, SHA-256 manifest generation, and deterministic archive creation.**
- [x] **Step 4: Implement `--mode rc` and `--mode production`; production requires 259 `BehaviorVerified` commands and zero release blockers.**
- [x] **Step 5: Falsify the gate with an incomplete catalog, then run it against a synthetic fully verified catalog to prove the production check judges both bad and good inputs.**
- [x] **Step 6: Run the finalization tests and confirm they pass.**

### Task 3: Make CI enforce native finalization

**Files:**
- Modify: `.github/workflows/native-winui.yml`
- Create: `.github/workflows/native-release-candidate.yml`
- Modify: `tests/native/test_native_foundation.py`
- Modify: `tests/native/test_finalization.py`

**Interfaces:**
- Consumes: finalizer CLI and current command migration state.
- Produces: Linux source/RC artifacts and Windows build/package gates without PowerShell.

- [x] **Step 1: Write failing structural assertions requiring `master` and `main` branch coverage, finalization tests, RC artifact generation, and production fail-closed mode.**
- [x] **Step 2: Run tests and confirm the workflow assertions fail.**
- [x] **Step 3: Update native CI to run all native tests and the RC finalizer.**
- [x] **Step 4: Add a manual production job that runs `--mode production` before any package promotion.**
- [x] **Step 5: Run tests and confirm workflow coverage passes.**

### Task 4: Align release documentation and metadata

**Files:**
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `docs/Architecture.md`
- Modify: `docs/migration/command-parity-ledger.md`
- Create: `docs/migration/finalization-status.md`
- Modify: `VALIDATION.md`

**Interfaces:**
- Consumes: finalizer output and exact migration counts.
- Produces: precise user and maintainer guidance that distinguishes a native source RC from a production release.

- [x] **Step 1: Document the separate native and oracle archives and the production promotion rule.**
- [x] **Step 2: Remove instructions that require PowerShell from the native release path while keeping historical legacy instructions clearly labeled.**
- [x] **Step 3: Record exact counts: 259 cataloged, 2 implemented, 0 behavior-verified, 257 native implementation blockers and 259 production behavior-verification blockers.**
- [x] **Step 4: Add Windows-only validation requirements without claiming they ran here.**

### Task 5: Verify and package the finalization candidate

**Files:**
- Modify: `docs/superpowers/plans/2026-08-06-wincare-project-finalization.md`
- Generate: `/mnt/data/WinCare-2.4.0-rc1-native-source.zip`
- Generate: `/mnt/data/WinCare-2.4.0-rc1-legacy-oracle.zip`
- Generate: `/mnt/data/WinCare-2.4.0-rc1-finalization-report.md`
- Generate: `/mnt/data/WinCare-2.4.0-rc1-finalization-manifest.json`

**Interfaces:**
- Consumes: completed source, tests, and finalizer.
- Produces: downloadable deterministic artifacts and fresh verification evidence.

- [x] **Step 1: Run `python3 -m unittest discover -s tests/native -v`.**
- [x] **Step 2: Run `python3 tools/verify_native_foundation.py`.**
- [x] **Step 3: Run `python3 -m py_compile` for every Python file under `tools` and `tests/native`.**
- [x] **Step 4: Run the RC finalizer and verify both archive hashes and contents independently.**
- [x] **Step 5: Confirm the native archive contains zero PowerShell files and exactly 259 command IDs.**
- [x] **Step 6: Confirm production mode exits non-zero with 259 behavior-verification blockers and 257 implementation blockers.**
- [x] **Step 7: Mark completed steps and record the environment limitations.**


## Execution record

- Native structural suite: 22 tests passed.
- Native source verifier: passed with 259 frozen-oracle command IDs and two fail-closed read-only handlers.
- Python compilation: 51 files under `tools` and `tests/native` compiled successfully.
- Determinism: two independent RC finalization runs produced byte-identical archives, report, and manifest.
- Native archive audit: 155 files, zero PowerShell files, zero cache files, 259 unique command IDs, and all local Markdown links resolved.
- Legacy oracle audit: 300 files, including 246 PowerShell source or module files, isolated from the native artifact.
- Production falsification: `--mode production` exited 1 with 259 behavior-verification blockers.
- Environment limitation: `dotnet`, `cargo`, `rustc`, `pwsh`, `winapp`, and a Windows desktop are unavailable here. C#, Rust, WinUI runtime, UI Automation, MSIX, and command behavior remain pending their Windows CI gates.

## Self-review

- Spec coverage: catalog preservation, PowerShell separation, deterministic packaging, CI gates, documentation, and honest production blocking are assigned to explicit tasks.
- Placeholder scan: no TBD, TODO, or unspecified implementation steps remain.
- Type consistency: the finalizer exposes one `FinalizationResult`; CLI modes are exactly `rc` and `production`; generated filenames are fixed in Task 5.
- Known limitation: this plan finalizes the migration repository and release controls. It does not fabricate behavioral parity for the 257 commands that still lack native implementations.
