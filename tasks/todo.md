# WinCare — Ultra Cleanup Task List

## Phase 1: Zero-Risk Deletes
- [ ] **Task 1.1** — ~~Delete `tools/verify_release_v3.py`~~ CANCELLED — file is live (imported by `finalize_release.py`, tested by `test_standalone_release.py`, called by `Build-Release.ps1`)
- [ ] **Task 1.2** — Delete `src/WinCare/UI/MainWindow.xaml` (dead stub XAML, zero references)
- [ ] **Task 1.3** — Remove `ConvertTo-WinCarePrometheusMaintenanceMetrics` from `86-Maintenance.ps1` + `WinCare.psm1` (zero callers, no test)
- [ ] **Task 1.4** — Investigate and delete `tools/validate_gui.py` if confirmed dead (no CI reference found; 250 lines)
- [ ] **Task 1.5** — Delete `tools/scratch_parity.py` and `tools/benchmark_performance.py` if confirmed as orphaned stubs

**Checkpoint 1:** `Invoke-Pester` (88 green) + `python tools/validate_source.py .` + `python tools/validate_module_manifest.py .`

---

## Phase 2: Export-List Simplification
- [ ] **Task 2.1** — Replace ~330-line manual `Export-ModuleMember -Function` list in `WinCare.psm1` with `Export-ModuleMember -Function *`; verify count unchanged via `validate_module_manifest`

**Checkpoint 2:** `Invoke-Pester` (88 green) + `python tools/validate_module_manifest.py .`

---

## Phase 3: Security Gap Remediation
- [ ] **Task 3.1** — Fix silent `settings.json` corruption fallback in `Core/01-Config.ps1` → explicit `Write-WcLog -Level Error` + re-throw
- [ ] **Task 3.2** — Fix all empty `catch{}` blocks in `src/WinCare/Core/` → `Write-WcLog -Level Warning + throw`
- [ ] **Task 3.3** — Close TOCTOU window in `ElevatedActionHost.ps1` → atomic `[IO.FileStream]` read

**Checkpoint 3:** `Invoke-Pester` (88+ green) + `python tools/test_security_invariants.py`

---

## Phase 4: Design Token Adoption
- [ ] **Task 4.1** — Wire `design/WinCare-Tokens.json` into `10-GuiRuntime.ps1`; add `Get-WcDesignToken` helper; eliminate all hardcoded hex including `#7C5CFC` (replace with brand blue `#2F80ED`)
- [ ] **Task 4.2** — Replace `UniformGrid Columns="4"` with responsive WrapPanel/Grid in `WinCare.MainWindow.xaml`
- [ ] **Task 4.3** — Add WCAG AA `FocusVisualStyle` XAML resources using brand blue token

**Checkpoint 4:** Zero hex in `src/WinCare/UI/` + visual inspection GUI + `Invoke-Pester` (88 green)

---

## Phase 5: Coding Standards
- [ ] **Task 5.1** — Add `tools/PSScriptAnalyzer.psd1` with semicolon-ban rule; update `Invoke-StaticChecks.ps1`; fix Core module violations
- [ ] **Task 5.2** — Extract magic numeric literals (`1048576`, `32768`, `4096`, `65536`, `300`, `3600`) to named constants with WHY-comments in `Core/01-Config.ps1`, `Core/00-10-SystemToolkit.ps1`, `Host/ElevatedActionHost.ps1`

**Checkpoint 5:** PSScriptAnalyzer passes on Core + zero magic literals + `Invoke-Pester` (88 green)

---

## Phase 6: Tooling Consolidation
- [ ] **Task 6.1** — Extract `Read-BoundedUtf8Text` (defined 5× across `release.yml` + `recover-release.yml`) to `tools/Invoke-BoundedFileRead.ps1`; workflows dot-source it. Note: `Read-WinCareToolingBoundedUtf8Text` in `WinCare.Tooling.ps1` is a 6th copy — unify naming.
- [ ] **Task 6.2** — Create `tools/release_utils.py` consolidating: `canonical_json`, `pretty_json`, `tree_hash`, `make_sbom`, `write_zip`, `sha256_file`, `normalize_member`, `parse_manifest`, `WINDOWS_RESERVED` (currently duplicated across `build_release.py`, `finalize_release.py`, `verify_release.py`, `verify_release_v3.py`, `prepare_standalone_payload.py`, 2 publish scripts). Estimated savings: ~340 lines.
- [ ] **Task 6.3** — Consolidate `validate_bounded_io.py` + `validate_external_processes.py` + `validate_network_egress.py` into single `validate_safety_invariants.py`; update CI gate names in `ci.yml` + `Invoke-WindowsValidation.ps1`. Saves ~100 lines.
- [ ] **Task 6.4** — Evaluate Ubuntu CI `evidence` gate 16 (`development_package`): document whether it provides unique coverage vs the Windows job. Remove if redundant.
- [ ] **Task 6.5** — Add `.github/dependabot.yml` for `github-actions` ecosystem to auto-propose digest updates.

**Final Checkpoint:** All 88+ tests green + YAML lint + `Read-BoundedUtf8Text` has one canonical definition + `release_utils.py` created + `.github/dependabot.yml` present
