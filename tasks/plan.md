# WinCare Modernization & Ergonomics — Master Implementation Plan

/* Hallmark · pre-emit critique: P5 H5 E5 S5 R5 V5 */
/* Ponytail (Ultra) mode: Active — YAGNI enforced, zero avoidable abstractions, native platform first */

## 1. Creative Synthesis & Research Foundations

Applying cognitive creativity frameworks from cognitive science and CS research:

### A. Janusian / Dialectical Synthesis: Rigorous Safety AND Zero-Friction Usability
- **The False Binary:** A system utility must either be *extremely paranoid* (cryptographic tokens, 2-phase previews for everything) OR *recklessly dangerous* (1-click blind mutations).
- **The Dialectical Resolution:** **Risk-Tiered Admission**. Operations are not homogeneous.
  - *Tier 1 (Safe / Idempotent):* Flushes (DNS), caches (temp files), volume trims. They execute in a single click with instant feedback.
  - *Tier 2 (Moderate):* Services, firewall rules. Require a lightweight native confirmation, not a mandatory preview plan.
  - *Tier 3 (Destructive):* Registry wipes, component store resets, driver uninstalls. Retain full preflight preview, parameter-digest review plan, and explicit approval.
- **Impact:** Removes routine confirmation fatigue while preserving the existing strict two-phase path for commands classified Destructive.

### B. Problem Reformulation & Constraint Transformation
- **Old Assumption:** Diagnostic probes must be serialized (`SequentialCommandProbeRunner`) so CPU/disk measurements do not perturb one another.
- **Hidden Constraint Dropped:** Probes inspecting discrete subsystems (storage free space vs Windows Security state vs hardware facts) can run concurrently while the slower Windows Update query is reported separately.
- **New Formulation:** Run the primary local probes through `ParallelCommandProbeRunner.RunPreviewsAsync`, and report `wua-search` asynchronously.

### C. Constraint Manipulation (Boden's Framework): Footprint & Distribution
- **Old Assumption:** WinUI 3 apps in .NET 8 must disable trimming and ship every SDK projection to avoid reflection failures.
- **The Shift:** Use partial trimming for the portable profiles, but treat trim compatibility as a runtime property. The actual packaged x64 and ARM64 executables must pass native-architecture startup/core-flow smoke tests; warning suppression or compiled bindings alone are not proof.

### D. Authoritative Documentation Cross-References
- **Ponytail Anti-Overengineering Audit:** [`docs/architecture/ponytail-audit.md`](../docs/architecture/ponytail-audit.md)
- **C4 Architecture Model (Context, Container, Component):** [`docs/architecture/c4-model.md`](../docs/architecture/c4-model.md)
- **Visual Design System & Tokens:** [`DESIGN.md`](../DESIGN.md)

---

## 2. Architecture & Discipline Standards

### 2.1 Ponytail (Ultra) Mode Architecture
- **Rule of Parsimony (YAGNI):** Prefer existing platform/library capability over a custom abstraction when it meets the requirement.
- **Zero Boilerplate:** No speculative generic factories, mediator pipelines for single-caller methods, or redundant DTO mapping layers.
- **Explicit Ceilings (`// ponytail:`):** When a direct solution has a known boundary, document the rationale, ceiling, and concrete upgrade trigger instead of pre-building infrastructure.

### 2.2 Impeccable Visual Craft & Hallmark Anti-Slop Directives
- **Product Truth & Honest Copy:** No fabricated percentage metrics or unverified success language. Health presentation is categorical and grounded in actual findings.
- **Locked Design Tokens:** Colors, borders, and fonts reference authoritative resources in `ThemeResources.xaml`.
- **No Re-drawn Chrome:** Use native Windows App SDK / WinUI 3 Fluent presentation.
- **Typography:** Headers remain Roman; telemetry uses Cascadia Code where tabular technical values benefit from it.
- **Responsive Geometry:** Compact mode is governed by the shared 920-DIP breakpoint.

### 2.3 UI/UX Ergonomics
- **Icon Integrity:** Use verified Segoe Fluent icon glyphs rather than emoji icons in operational controls.
- **Stable Interactions:** Avoid interaction effects that cause layout shifts or text jitter.
- **Accessibility Floor:** Follow `DESIGN.md`: logical keyboard navigation, visible focus, meaningful automation names, minimum `44 x 44 DIP` targets, and Windows High Contrast usability.

### 2.4 Anti-Overengineering Checklist
- Do not swallow OS failures into fabricated healthy/default values.
- Do not add mediator/broker layers where direct dispatcher/view-model composition is sufficient.
- Keep legacy/sequential helpers only where a real serial caller still needs them; active Checkup uses the parallel runner.
- Do not add blanket confirmation ceremony to Safe or Moderate commands.

### 2.5 Backend Boundaries
- `CommandDispatcher.ExecuteAsync` is the admission/dispatch boundary.
- `WindowsCommandExecutor` owns OS operation execution.
- High-latency WUA work is decoupled from primary Checkup completion.
- Outcomes remain typed through `CommandResultStatus` and stable error codes.

---

## 3. Performance & Evaluator Gates

| Metric | Baseline | Target Goal | Executable Gate |
|---|---|---|---|
| **Primary Checkup Latency** | 30–60s when serialized with WUA | **≤ 3.0s on representative Windows hardware** | Application tests prove real overlap/order/fault isolation; a p95 claim requires a separate hardware benchmark. |
| **Portable Executable Size** | 120–135 MB untrimmed baseline | **≤ 70,000,000 bytes** | `python tools/release_checklist.py --portable-artifact artifacts/portable/<rid>/WinCare.App.exe` for both RIDs; adoption measurements were 67,362,718 bytes x64 and 66,071,465 bytes ARM64. |
| **Trimmed Runtime Compatibility** | Build-only evidence | **x64 + ARM64 packaged smoke pass** | Native-architecture runners launch the actual portable executable with `--smoke-test`. |
| **Safe Command UX** | Blanket preview/token ceremony | **1-click** | `RiskTierAdmissionTests` and view-model tests verify direct Safe execution. |

---

## 4. Vertically Sliced Task Breakdown

### Phase 1: Foundation & Risk-Tiered Command Admission

- [x] **Task 1: Add `RiskTier` to Domain Models and Catalog**
  - Files: `src/WinCare.Domain/Commands/RiskTier.cs`, `src/WinCare.CommandCatalog/Models/CommandDefinition.cs`.
  - Explicit risk metadata cannot downgrade a higher derived tier.
- [x] **Task 2: Implement Risk-Tiered Admission in `CommandDispatcher`**
  - Safe executes directly; Moderate requires lightweight confirmation; Destructive requires a valid current review plan.
  - Unsupported enum values are blocked before handler execution.
- [x] **Task 3: Regression Coverage**
  - `tests/WinCare.Application.Tests/RiskTierAdmissionTests.cs` covers Safe, Moderate, Destructive, downgrade prevention, invalid tiers, and destructive replay blocking.

---

### Phase 2: Diagnostic Performance & Concurrency

#### Task 4: Implement `ParallelCommandProbeRunner`
- [x] Bounded concurrency with `SemaphoreSlim`, `Task.WhenAll`, stable request-order result placement, per-probe budgets, and fault isolation.
- [x] Tests use a deterministic peak-concurrency tracker rather than a fragile sub-second stopwatch assertion.

#### Task 5: Decouple Windows Update in `CheckupPageViewModel`
- [x] `system`, `storage`, and `security` run through `ParallelCommandProbeRunner.RunPreviewsAsync`.
- [x] `wua-search` reports independently in the background.
- [x] A run-version guard prevents an older WUA continuation overwriting newer Quick Check state.
- [x] Incomplete probes cannot produce a `Healthy` result.
- **Performance boundary:** the product target is ≤3.0s for the primary local batch on representative hardware; unit-test overlap is not described as a p95 benchmark.

---

### Phase 3: UX & Usability Overhaul

#### Task 6: Meaningful Checkup Findings
- [x] Replace completion percentage with categorical finding state.
- [x] Storage/security/update findings feed `Healthy`, `Attention`, `Action`, `Checking`, or incomplete/review states.
- [x] Finding action buttons bind `ActionText` and `ActionCommand` in `Mode=OneWay` so asynchronous mutations remain live.
- [x] Disk free-space messages use a real one-decimal format (`0.0`).

#### Task 7: Curated Home Actions
- [x] Quick Clean runs the Safe cleaner directly.
- [x] Startup and Network cards expose direct read-only analyses.
- [x] Telemetry inspector reports CPU as `N/A` when the native probe is unavailable instead of synthesizing `0.0%`.

#### Task 8: Streamline All Tools
- [x] Safe tools execute directly.
- [x] Moderate tools can apply after lightweight confirmation without a mandatory preview.
- [x] Destructive tools retain the two-phase preview + approval-plan workflow.
- [x] Raw JSON remains an advanced/collapsed surface.

---

### Phase 4: Native Cleaner Correctness

#### Task 9: Bound Temp Cleanup Traversal
- [x] `native/wincare-core/src/cleaner.rs` obtains non-following metadata before directory traversal.
- [x] Windows reparse-point directories/junctions are skipped.
- [x] A Windows junction regression points outside the cleanup root and verifies the external sentinel survives.

---

### Phase 5: Portable Size, Trimming, and Distribution

#### Task 10: Canonical Portable Contract
- [x] x64 and ARM64 portable profiles publish trimmed, compressed single-file executables.
- [x] One measured regression ceiling applies to the executable itself: `WinCare.App.exe <= 70,000,000 bytes`.
- [x] The Windows workflow invokes `tools/release_checklist.py --portable-artifact` for each RID.
- [x] Portable-specific lock graphs are selected with `NuGetLockFilePath` rather than overwriting canonical lock files before restore.

#### Task 11: Runtime Validation of the Trimmed Artifact
- [x] `WinCare.App.exe --smoke-test` constructs the real WinUI window, initializes runtime/plugin discovery, validates the bundled Rust ABI, and executes the read-only `system` command through `CommandDispatcher`.
- [x] x64 smoke runs on a native x64 Windows runner.
- [x] ARM64 smoke runs on `windows-11-vs2026-arm`.
- [x] Release promotion depends on both portable runtime smoke jobs.
- **Boundary:** dynamic COM/WUA or plugin scenarios outside this smoke path still need focused Windows runtime coverage and any required trimming roots/annotations.

#### Task 12: Inno Setup Definition
- [x] `tools/installer/wincare_setup.iss` exists and consumes `artifacts/portable/win-x64/*`.
- [x] The architecture and planning docs describe it as a wrapper around the same x64 portable payload, not a separate framework-dependent app.
- [ ] The current `native-winui.yml` does not compile/publish `WinCare-Setup.exe`; do not claim a setup artifact is produced until a workflow step actually invokes Inno Setup.

---

## 5. Verification Matrix

| Component / Layer | Verification | Gate |
|---|---|---|
| **Command admission** | `RiskTierAdmissionTests` | Safe direct, Moderate confirmation-only, Destructive plan enforcement, invalid-tier rejection. |
| **Probe concurrency** | `ParallelCommandProbeRunnerTests` | Real overlap, stable order, failure isolation. |
| **Managed solution** | `dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore` | Exact-head Windows CI must pass. |
| **Rust** | format, clippy, x64 unit tests, x64/ARM64 release builds | Exact-head CI must pass. |
| **Structural contract** | `python -m unittest discover -s tests/native -v` | Source/packaging/runtime-smoke contracts remain wired. |
| **Portable size** | `tools/release_checklist.py --portable-artifact .../WinCare.App.exe` | Each RID ≤70,000,000 bytes. |
| **Portable runtime** | packaged `--smoke-test` | x64 and ARM64 native-runner exit code 0. |
| **MSIX** | build + runner-local development signing + signature/publisher verification | Exact-head CI must pass. |

---

## 6. Risk & Mitigation Register

| Risk | Impact | Mitigation |
|---|---|---|
| **Trimmed WinUI/XAML or dynamic-code regression** | High | Native-architecture packaged smoke plus focused runtime tests for dynamic paths; do not treat linker warning suppression as proof. |
| **Background WUA completion races a newer check** | Medium | Run-version guard before applying asynchronous WUA results. |
| **Cleaner traverses a junction outside temp scope** | High | `symlink_metadata` + reparse-point skip + junction sentinel regression. |
| **Risk metadata understates a destructive command** | High | Explicit tier cannot lower the derived floor; invalid tiers are rejected before handler execution. |
| **Installer documentation drifts from build pipeline** | Medium | Treat Inno as an x64 portable wrapper and explicitly leave workflow publication unchecked until implemented. |
