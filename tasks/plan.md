# WinCare — Ultra Cleanup Implementation Plan

**Date:** 2026-08-01
**Source:** Ultra Review Swarm (5-agent parallel audit) → `ultra_review_report.md`
**Scope:** Zero-risk deletes → Structural simplification → Security hardening → Design token adoption → Coding standards → CI/Workflow consolidation

---

## Overview

The swarm identified **~2,700 source + tooling lines removable**, 3 security gaps, a critical
design-token mismatch (AI-purple vs brand-blue), and WCAG AA keyboard/focus failures.

Work is divided into six phases ordered by risk (lowest first).
Every phase ends with a green test suite (`88 Pester tests`) before the next begins.

Architecture constraints (from `DESIGN.md`):
- PowerShell owns Windows orchestration; no new language introductions
- Python is limited to reproducible build and independent verification tools
- UI state is never policy; mutations flow through one authoritative write path
- Missing evidence never becomes success

---

## Architecture Decisions

1. **Phase ordering by blast radius** — Phase 1 (deletes) cannot regress behavior; Phases 2-4 touch live logic; Phase 5 touches CI YAML.
2. **Test-first for all behavior-touching phases** — Run `Invoke-Pester` after every structural change before moving to the next task.
3. **No cross-phase file bundling** — Each task touches at most 3-5 files.
4. **Design token migration is additive** — Colors are tokenized without rewriting XAML structure.
5. **`Read-BoundedUtf8Text` extracted to shared helper** — Appears verbatim in `release.yml`, `recover-release.yml`, and `windows-release-validation.yml`; extract to `tools/Invoke-BoundedFileRead.ps1`.

---

## Phase 1: Zero-Risk Deletes

*All items here are confirmed dead or duplicate. No behavior change. Tests must stay green.*

### Task 1.1: ~~Delete `tools/verify_release_v3.py`~~ — CANCELLED

**Finding:** `verify_release_v3.py` is **NOT dead**. It is:
- Imported by `tools/finalize_release.py` (lines 17, 19)
- Called directly by `tools/Build-Release.ps1` (line 136) as the final `verify-v3` gate
- Tested exhaustively by `tools/test_standalone_release.py` (13 reference sites)
- Referenced in `docs/RELEASE.md`

It serves a distinct role: v3 verifies the **final standalone archive** with `.exe` PE header checks,
while `verify_release.py` handles the v2 development/production archive. Both are live and necessary.

**Replacement task:** See Phase 6.2 — extract shared utilities into `release_utils.py` to
eliminate the ~175 lines duplicated *between* v2 and v3 verifiers.

---

### Task 1.2: Delete `src/WinCare/UI/MainWindow.xaml`

**Description:** Dead stub XAML. The actual UI source is `src/WinCare/Data/Gui/WinCare.MainWindow.xaml`. No code-behind, no references from any `.ps1` or `.csproj`.

**Acceptance criteria:**
- [ ] File deleted
- [ ] `Select-String -Recurse -Path . -Include '*.ps1','*.csproj' -Pattern 'UI.MainWindow.xaml'` returns zero matches

**Verification:**
- [ ] `Invoke-Pester` all 88 tests green
- [ ] `python tools/validate_source.py .` passes

**Dependencies:** None
**Files:** `src/WinCare/UI/MainWindow.xaml`
**Estimated scope:** XS

---

### Task 1.3: Remove dead `ConvertTo-WinCarePrometheusMaintenanceMetrics`

**Description:** Exported in `WinCare.psm1` but zero callers, no test. Dead export.

**Acceptance criteria:**
- [ ] Function body and export entry removed
- [ ] `Select-String -Recurse -Path . -Pattern 'ConvertTo-WinCarePrometheusMaintenanceMetrics'` returns zero matches

**Verification:**
- [ ] `Invoke-Pester` all 88 tests green
- [ ] `python tools/validate_module_manifest.py .` passes

**Dependencies:** None
**Files:** `src/WinCare/Providers/86-Maintenance.ps1`, `src/WinCare/WinCare.psm1`
**Estimated scope:** S

---

### Checkpoint: After Phase 1

- [ ] All 88 Pester tests pass
- [ ] `python tools/validate_source.py .` passes
- [ ] `python tools/validate_module_manifest.py .` passes
- [ ] Git diff shows only deletions

---

## Phase 2: Export-List Simplification

### Task 2.1: Replace ~330-line manual export list with `Export-ModuleMember -Function *`

**Description:** `WinCare.psm1` contains a ~330-line `Export-ModuleMember -Function` list. Replace with wildcard export; `validate_module_manifest` gate enforces the public surface contract.

**Acceptance criteria:**
- [ ] Manual list replaced with `Export-ModuleMember -Function *` (or prefix-based pattern)
- [ ] `python tools/validate_module_manifest.py .` passes with identical function count
- [ ] `Invoke-Pester` all 88 tests green

**Verification:**
- [ ] `Get-Command -Module WinCare | Measure-Object` returns same count before/after

**Dependencies:** Task 1.3
**Files:** `src/WinCare/WinCare.psm1`
**Estimated scope:** S
**ponytail:** `shrink` 330 lines -> ~5 lines. Ceiling: private naming convention needed if helpers use `Wc` prefix. Upgrade path: none while validate_module_manifest enforces the contract.

---

### Checkpoint: After Phase 2

- [ ] All 88 Pester tests pass
- [ ] `python tools/validate_module_manifest.py .` passes
- [ ] Exported function count unchanged

---

## Phase 3: Security Gap Remediation

### Task 3.1: Harden `settings.json` silent corruption fallback

**Description:** `src/WinCare/Core/01-Config.ps1` silently substitutes defaults on corrupt JSON — masking fallback slop. Fix: explicit `Write-WcLog -Level Error` + re-throw.

**Acceptance criteria:**
- [ ] Corrupt `settings.json` raises terminating error with diagnostic message
- [ ] No silent default substitution path remains
- [ ] Existing config tests pass

**Verification:**
- [ ] New Pester test: corrupt JSON -> catches specific error message
- [ ] `Invoke-Pester` all 88+ tests green

**Dependencies:** None
**Files:** `src/WinCare/Core/01-Config.ps1`, tests
**Estimated scope:** S

---

### Task 3.2: Fix swallowed empty `catch{}` blocks

**Description:** Empty `catch{}` in 3+ locations silently discards failure evidence. Replace each with `catch { Write-WcLog -Level Warning "..." ; throw }`.

**Acceptance criteria:**
- [ ] `Select-String -Path src -Include *.ps1 -Pattern 'catch\s*\{\s*\}' -Recurse` returns zero matches
- [ ] No behavior regression

**Verification:**
- [ ] `Invoke-Pester` all 88 tests green
- [ ] `python tools/test_security_invariants.py` passes

**Dependencies:** None
**Files:** `src/WinCare/Core/` (3-5 files from security report)
**Estimated scope:** M

---

### Task 3.3: Close `secret.bin` TOCTOU window in `ElevatedActionHost.ps1`

**Description:** Check-then-read file pattern creates symlink substitution window. Replace with atomic `[IO.FileStream]` open-and-read (single operation, no existence check).

**Acceptance criteria:**
- [ ] File-existence + file-read replaced with single atomic operation
- [ ] `python tools/test_security_invariants.py` covers TOCTOU path and passes
- [ ] Static grep finds no remaining check-then-read pattern

**Verification:**
- [ ] `Invoke-Pester` all 88 tests green
- [ ] `python tools/test_security_invariants.py` passes

**Dependencies:** None
**Files:** `src/WinCare/Host/ElevatedActionHost.ps1`, `tools/test_security_invariants.py`
**Estimated scope:** M

---

### Checkpoint: After Phase 3

- [ ] All 88+ Pester tests pass (new security tests included)
- [ ] `python tools/test_security_invariants.py` passes
- [ ] Zero empty `catch{}` blocks remain

---

## Phase 4: Design Token Adoption

### Task 4.1: Wire `WinCare-Tokens.json` into `10-GuiRuntime.ps1`

**Description:** `design/WinCare-Tokens.json` is never loaded. GUI has 16+ hardcoded hex colors including `#7C5CFC` (AI purple, not brand). Add `Get-WcDesignToken` helper; replace all hardcoded hex with token lookups. Brand accent must be `#2F80ED` (blue).

**Acceptance criteria:**
- [ ] `Select-String -Path src/WinCare/UI -Pattern '#[0-9A-Fa-f]{6}' -Recurse` returns zero matches
- [ ] Token file loaded at GUI startup
- [ ] `#7C5CFC` eliminated; accent is brand blue

**Verification:**
- [ ] Manual: launch GUI -> accent is blue, not purple
- [ ] `Invoke-Pester` all 88 tests green

**Dependencies:** None
**Files:** `src/WinCare/UI/Gui/10-GuiRuntime.ps1`, `design/WinCare-Tokens.json`
**Estimated scope:** M

---

### Task 4.2: Replace `UniformGrid Columns="4"` with responsive layout

**Description:** Every card container uses rigid 4-column grid. Replace with `WrapPanel` or variable-definition `Grid` for content-appropriate emphasis.

**Acceptance criteria:**
- [ ] `UniformGrid Columns="4"` replaced with responsive layout
- [ ] GUI renders without horizontal scroll at 1280x720

**Verification:**
- [ ] Manual inspection of rendered GUI
- [ ] `Invoke-Pester` all tests green

**Dependencies:** Task 4.1
**Files:** `src/WinCare/Data/Gui/WinCare.MainWindow.xaml`
**Estimated scope:** M

---

### Task 4.3: Add WCAG AA keyboard focus indicators

**Description:** No visible focus ring on any interactive control. Add `FocusVisualStyle` XAML resource using brand blue token.

**Acceptance criteria:**
- [ ] Tab navigation shows visible focus ring on all controls
- [ ] Focus ring uses brand blue token (not hardcoded hex)

**Verification:**
- [ ] Manual: Tab through GUI -> focus ring visible
- [ ] `Invoke-Pester` all tests green

**Dependencies:** Task 4.1
**Files:** `src/WinCare/Data/Gui/WinCare.MainWindow.xaml`, XAML resource dictionaries
**Estimated scope:** S

---

### Checkpoint: After Phase 4

- [ ] Zero hardcoded hex colors in `src/WinCare/UI/`
- [ ] GUI accent is brand blue
- [ ] Keyboard focus visible on all interactive controls
- [ ] All 88 tests pass

---

## Phase 5: Coding Standards Pass

### Task 5.1: Add PSScriptAnalyzer semicolon-ban rule

**Description:** Entire codebase is semicolon-minified. Add `PSScriptAnalyzer.psd1` banning semicolon-chaining; fix Core modules first.

**Acceptance criteria:**
- [ ] `tools/PSScriptAnalyzer.psd1` bans semicolons
- [ ] `src/WinCare/Core/` passes new rule
- [ ] `tools/Invoke-StaticChecks.ps1` runs the new settings

**Verification:**
- [ ] `Invoke-ScriptAnalyzer -Path src/WinCare/Core -Settings tools/PSScriptAnalyzer.psd1` returns zero violations

**Dependencies:** None
**Files:** `tools/Invoke-StaticChecks.ps1`, `tools/PSScriptAnalyzer.psd1` (new)
**Estimated scope:** M

---

### Task 5.2: Extract magic numeric literals to named constants

**Description:** Raw literals `1048576`, `32768`, `4096`, `65536`, `300`, `3600` scattered through Core and Host files. Extract to named constants with WHY-comments.

**Acceptance criteria:**
- [ ] `Select-String -Pattern '\b(1048576|32768|65536|4096)\b' -Path src -Recurse -Include *.ps1` returns zero matches
- [ ] Each constant has a WHY-comment naming the ceiling

**Verification:**
- [ ] `Invoke-Pester` all 88 tests green

**Dependencies:** None
**Files:** `src/WinCare/Core/01-Config.ps1`, `src/WinCare/Core/00-10-SystemToolkit.ps1`, `src/WinCare/Host/ElevatedActionHost.ps1`
**Estimated scope:** S

---

### Checkpoint: After Phase 5

- [ ] PSScriptAnalyzer semicolon rule passes on Core
- [ ] Zero magic size literals in `src/WinCare/Core/`
- [ ] All 88 tests pass

---

## Phase 6: CI/Workflow Consolidation

### Task 6.1: Extract `Read-BoundedUtf8Text` to `tools/Invoke-BoundedFileRead.ps1`

**Description:** `Read-BoundedUtf8Text` is copy-pasted verbatim into `release.yml` (3 times), `recover-release.yml` (2 times), and `windows-release-validation.yml` (1 time). Single canonical helper; workflows dot-source it.

**Acceptance criteria:**
- [ ] `Select-String -Path .github -Pattern 'function Read-BoundedUtf8Text' -Recurse` returns zero matches
- [ ] `tools/Invoke-BoundedFileRead.ps1` exports the canonical implementation
- [ ] All three workflow files dot-source it

**Verification:**
- [ ] `python -m unittest tools.test_release_tools -v` passes
- [ ] Local dot-source test passes

**Dependencies:** Phases 1-5 stable
**Files:** `.github/workflows/release.yml`, `.github/workflows/recover-release.yml`, `.github/workflows/windows-release-validation.yml`, `tools/Invoke-BoundedFileRead.ps1` (new)
**Estimated scope:** M

---

### Task 6.2: Evaluate/remove Ubuntu CI gate 16 duplication

**Description:** Ubuntu `evidence` job gate 16 (`development_package`) builds a `.zip` that is not tested on a real Windows runtime. Windows job already runs `build_release.py`. Evaluate whether gate 16 provides unique safety signal; document and remove if not.

**Acceptance criteria:**
- [ ] Decision documented with rationale
- [ ] If removed: Windows job is sole builder; Ubuntu job validates contracts only
- [ ] YAML lint passes

**Verification:**
- [ ] `python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"` clean
- [ ] CI runs green on test branch

**Dependencies:** Task 6.1
**Files:** `.github/workflows/ci.yml`
**Estimated scope:** S

---

### Task 6.3: Add `.github/dependabot.yml` for action pinning maintenance

**Description:** All four workflows pin actions to SHA digests (correct security practice). Add Dependabot config to propose updates automatically.

**Acceptance criteria:**
- [ ] `.github/dependabot.yml` exists with `package-ecosystem: github-actions`
- [ ] Existing pinned digests unchanged

**Verification:**
- [ ] `cat .github/dependabot.yml` shows valid config

**Dependencies:** None
**Files:** `.github/dependabot.yml` (new)
**Estimated scope:** XS

---

### Final Checkpoint

- [ ] All 88+ Pester tests pass
- [ ] `python -m unittest tools.test_release_tools tools.test_standalone_release tools.test_security_invariants -v` green
- [ ] `python tools/validate_source.py .` passes
- [ ] `python tools/validate_module_manifest.py .` passes
- [ ] Zero hardcoded hex in `src/WinCare/UI/`
- [ ] `Read-BoundedUtf8Text` has one canonical definition
- [ ] `.github/dependabot.yml` present

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `Export-ModuleMember *` breaks private helpers | High | `validate_module_manifest` enforces surface before/after |
| TOCTOU fix breaks ElevatedActionHost timing | Medium | New Pester test covers atomic read path |
| Token adoption breaks GUI startup | Medium | Guard `Get-WcDesignToken` with fallback to current defaults |
| Ubuntu CI gate 16 removal misses unique coverage | Low | Review gate 16 output vs Windows gate; document delta |
| YAML edit introduces syntax error | Low | YAML lint step in verification |

---

## Open Questions

1. Does gate 16 in the `evidence` job catch any failure the Windows job does not?
2. Should the semicolon ban apply to XAML-embedded snippets or only `.ps1` files?
3. Does `$env:WINCARE_UNEXPORTED` exist or does the wildcard export need a different mechanism?

