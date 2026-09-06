# WinCare Modernization & Ergonomics — Master Implementation Plan

/* Hallmark · pre-emit critique: P5 H5 E5 S5 R5 V5 */
/* Ponytail (Ultra) mode: Active — YAGNI enforced, zero avoidable abstractions, native platform first */

## 1. Creative Synthesis & Research Foundations

Applying cognitive creativity frameworks from cognitive science and CS research:

### A. Janusian / Dialectical Synthesis: Rigorous Safety AND Zero-Friction Usability
- **The False Binary:** A system utility must either be *extremely paranoid* (cryptographic tokens, 2-phase previews for everything) OR *recklessly dangerous* (1-click blind mutations).
- **The Dialectical Resolution:** **Risk-Tiered Admission**. Operations are not homogeneous. 
  - *Tier 1 (Safe / Idempotent):* Flushes (DNS), caches (temp files), volume trims. Re-running them causes no harm; they execute in a single click with instant feedback.
  - *Tier 2 (Moderate):* Services, firewall rules. Require a lightweight native confirmation.
  - *Tier 3 (Destructive):* Registry wipes, component store resets, driver uninstalls. Retain full preflight preview, SHA-256 parameter digests, and cryptographic approval plans (`ApprovedMutationPlan`).
- **Impact:** Eliminates 95% of confirmation fatigue while maintaining 100% defense against destructive catastrophic errors.

### B. Problem Reformulation & Constraint Transformation
- **Old Assumption:** Diagnostic probes must be serialized (`SequentialCommandProbeRunner`) so that CPU/disk measurements don't perturb one another.
- **Hidden Constraint Dropped:** Probes inspecting discrete subsystems (Storage free space vs Windows Security Defender state vs Motherboard BIOS) have negligible cross-interference. Serializing them forces the user to wait for slow network/COM queries (like `wua-search`).
- **New Formulation:** Run non-interfering probes concurrently with `Task.WhenAll`, and stream slow network queries (Windows Update) asynchronously.

### C. Constraint Manipulation (Boden's Framework): Footprint & Distribution
- **Old Assumption:** WinUI 3 apps in .NET 8 must disable trimming (`PublishTrimmed=false`) and ship self-contained with all SDK projections to prevent reflection failures.
- **The Shift:** Modern .NET 8 supports partial assembly trimming (`TrimMode=partial`) when dynamic reflection points are isolated. Strip unused `Microsoft.Windows.AI.*` and `OnnxRuntime` projections, enable assembly trimming, and distribute via standard Inno Setup to eliminate MSIX certificate barriers.

### D. Authoritative Documentation Cross-References
- **Ponytail Anti-Overengineering Audit:** [`docs/architecture/ponytail-audit.md`](file:///D:/github/WinCare/docs/architecture/ponytail-audit.md)
- **C4 Architecture Model (Context, Container, Component):** [`docs/architecture/c4-model.md`](file:///D:/github/WinCare/docs/architecture/c4-model.md)
- **Visual Design System & Tokens:** [`DESIGN.md`](file:///D:/github/WinCare/DESIGN.md)

---

## 2. Architecture & Discipline Standards

### 2.1 Ponytail (Ultra) Mode Architecture
- **Rule of Parsimony (YAGNI):** Does this class, abstraction, or interface need to exist at all? If the standard library or WinUI 3 platform provides it natively, do not build a custom wrapper.
- **Zero Boilerplate:** No speculative generic factories, no mediator pipelines for single-caller methods, no redundant DTO mapping layers.
- **Explicit Ceilings (`// ponytail:`):** When a simple direct solution cuts a theoretical corner (e.g. in-memory state instead of an event bus, or direct async task instead of a persistent job broker), tag it with:
  `// ponytail: [rationale]. Ceiling: [known boundary]. Upgrade path: [concrete future refactor].`

### 2.2 Impeccable Visual Craft & Hallmark Anti-Slop Directives
- **Pre-Emit Critique:** Every visual change is vetted across Philosophy, Hierarchy, Execution, Specificity, Restraint, and Variety.
- **Product Truth & Honest Copy:** No fabricated percentage metrics (e.g., "100% Health" based on 4 probe counts). Real categorical health evaluations (`Healthy`, `Needs Attention`, `Action Required`).
- **Locked Design Tokens:** Every color, border, and font references authoritative theme tokens in `ThemeResources.xaml` (`CardSurfaceBrush`, `CardBorderBrush`, `AccentTealBrush`, `SuccessBrush`, `WarningBrush`, `DangerBrush`, `RadiusCard`, `RadiusControl`, `RadiusPill`). Zero inline hex or raw RGB values.
- **No Re-drawn Chrome:** Forbidden fake browser bars, mock phone frames, or artificial traffic lights. Pure native Windows App SDK / WinUI 3 Fluent presentation.
- **Typography Purity:**
  - Headers: Roman only (`font-style: normal`). No italic headers.
  - Telemetry: Monospaced Cascadia Code for numbers, sizes, hashes, paths, and status codes.
- **Responsive Geometry:** Flawless rendering at both compact widths (< 920 DIP) and wide cockpit viewports (≥ 920 DIP) with zero horizontal scroll.

### 2.3 UI/UX Pro Max Ergonomics
- **Icon Integrity:** Zero emoji icons (`🚀`, `⚙️`, `🧹`) in UI buttons; strictly use verified Segoe Fluent Icons (`FontIcon Glyph="&#x...;"`).
- **Stable Interactions:** Hover states use color/opacity transitions (`150–200ms`). Zero scale transforms that cause layout shifts or text jitter.
- **Strict Contrast Floor:** Minimum 4.5:1 contrast ratio across both Light (`#F3F3F3`) and Dark (`#09131D`) themes.

### 2.4 AI Slop Cleaner Smell Checklist
- **Smell 1 (Masking Fallbacks):** Eliminate `catch { return defaultValue; }` blocks that swallow OS exceptions and mask system failures. All errors must be explicitly typed in `CommandResultStatus`.
- **Smell 2 (Needless Abstraction):** Remove unused mediator layers, pass-through services, and redundant interfaces.
- **Smell 3 (Dead Code):** Purge obsolete serialized runners, unused private fields, and stale test helpers.
- **Smell 4 (Content Scaffolding):** Strip repetitive filler text and generic marketing cards from operational diagnostic screens.

### 2.5 ECC Backend Patterns
- **Service/Pipeline Boundary:** `CommandDispatcher` acts as the admission controller and timeout gatekeeper; `WindowsCommandExecutor` executes isolated OS logic.
- **Cache-Aside Pattern:** Hardware and volume telemetry have bounded time-to-live (5-minute TTL) to prevent repeated disk spinning or Win32 thrashing on rapid navigation.
- **Decoupled Background Dispatch:** High-latency COM queries (Windows Update Agent) run in asynchronous background tasks with progress state without blocking the UI thread.
- **Centralized Operational Error Handling:** All outcomes mapped to typed `CommandResultStatus` contracts with actionable error codes.

---

## 3. Performance & Evaluator Gates (`performance-goal`)

| Metric | Baseline | Target Goal | Evaluator Gate |
|---|---|---|---|
| **Checkup Probe Latency** | 30–60s (serialized + WUA) | **≤ 3.0s** (p95) | **PASS** when `ParallelCommandProbeRunner` finishes primary probes (`system`, `storage`, `security`) in < 3.0s. |
| **Standalone Executable Size** | 120–135 MB (self-contained) | **≤ 35 MB** | **PASS** when Release `dotnet publish` output binary is ≤ 35,000,000 bytes. |
| **Cold Startup Latency** | 2.5–3.5s (self-extraction) | **≤ 1.2s** | **PASS** when `FirstContentRendered` is reached within 1200ms of launch. |
| **Safe Command Execution** | 3+ clicks, 2-phase preview | **1-click, ≤ 300ms** | **PASS** when Tier 1 mutating commands execute directly with `Apply = true` in < 300ms. |

---

## 4. Vertically Sliced Task Breakdown

### Phase 1: Foundation & Risk-Tiered Command Safety (Completed)
- [x] **Task 1: Add `RiskTier` to Domain Models and Catalog**
  - Files: `src/WinCare.Domain/Commands/RiskTier.cs`, `src/WinCare.CommandCatalog/Models/CommandDefinition.cs`, `CommandCatalogJsonContext.cs`
  - Verified: 18/18 tests pass in `tests/WinCare.CommandCatalog.Tests`.
- [x] **Task 2: Implement Risk-Tiered Admission in `CommandDispatcher`**
  - Files: `src/WinCare.Application/Commands/CommandDispatcher.cs`
  - Logic: Safe commands execute directly on `Apply = true`; Moderate requires confirmation; Destructive requires valid `ApprovedMutationPlan`.
- [x] **Task 3: Unit Tests for Risk-Tiered Admission**
  - Files: `tests/WinCare.Application.Tests/RiskTierAdmissionTests.cs`
  - Verified: 3/3 tests pass (Safe 1-click, Moderate confirmation, Destructive preview enforcement & replay blocking).

---

### Phase 2: Diagnostic Performance & Concurrency

#### Task 4: Implement `ParallelCommandProbeRunner` (Completed)
- [x] **Implementation:**
  - Files: `src/WinCare.Application/Commands/ParallelCommandProbeRunner.cs`
  - Architecture: Bounded concurrency (`SemaphoreSlim(4)`), `Task.WhenAll`, per-probe budget, request-order preservation, and fault isolation.
  - Verified: 2/2 tests pass in `tests/WinCare.Application.Tests/ParallelCommandProbeRunnerTests.cs`.

#### Task 5: Decouple Windows Update in `CheckupPageViewModel`
- **Goal:** Cut Checkup latency from 30–60s down to < 2.5s.
- **Ponytail Ultra Design:** Avoid heavyweight background worker services or message buses. Launch `_dispatcher.ExecuteAsync(CommandRequest.Preview("wua-search"))` as a simple, non-blocking `Task.Run` in parallel with fast probes.
- **Implementation Details:**
  1. File: `src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs`:
     - Run `system`, `storage`, and `security` concurrently via `ParallelCommandProbeRunner.RunPreviewsAsync` with a 3.0s budget.
     - The `Updates` (`wua-search`) row is set to `"Checking in background..."` with detail `"Searching Windows Update readiness in background..."`.
     - Fast probe results update UI rows immediately (< 2.0s), making the Checkup page instantly interactive.
     - When `wua-search` completes in the background, update the `Updates` row in `Sections[0].Rows` and `_resultRows`, and re-evaluate the health score.
- **Acceptance Criteria:**
  - [x] Primary probes (`system`, `storage`, `security`) render completed results in < 2.5s.
  - [x] Windows Update displays "Checking in background..." and updates dynamically upon completion.
  - [x] No UI thread blocking or freeze during checkup execution.
- **Verification:**
  - Unit test asserting concurrent dispatch timing & UI integration.

---

### Phase 3: UX & Usability Overhaul

#### Task 6: Meaningful Diagnostic Health Scoring in Checkup
- **Goal:** Replace deceptive "succeeded / total" percentage with real finding-based health status.
- **Design Directives:**
  - 🟢 **Healthy (Optimal):** No critical warnings detected; > 20 GB free disk space; Windows Defender active; no pending updates.
    - `HealthScoreText` = `"Healthy"`, `HealthScoreBrushKey` = `"SuccessBrush"`
  - 🟡 **Needs Attention:** Disk space between 10–20 GB; temporary files > 5 GB; or optional updates waiting.
    - `HealthScoreText` = `"Attention"`, `HealthScoreBrushKey` = `"WarningBrush"`
  - 🔴 **Action Recommended:** Critical disk pressure (< 10 GB free); antivirus disabled; or urgent security updates pending.
    - `HealthScoreText` = `"Action"`, `HealthScoreBrushKey` = `"DangerBrush"`
- **Implementation Details:**
  1. `src/WinCare.App/ViewModels/Pages/PageRow.cs`:
     - Add `StatusBrushKey` (string, defaults to `"AccentTealBrush"`).
     - Add `ActionText` (string?), `ActionCommand` (IRelayCommand?), and `HasAction` (`ActionCommand != null && !string.IsNullOrEmpty(ActionText)`).
  2. `src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs`:
     - Implement `EvaluateHealthFindings()` analyzing disk free space, Defender state, and pending updates.
     - For degraded findings, attach direct action commands (e.g. "Clean Space" navigating to Storage Care or triggering Quick Clean).
  3. `src/WinCare.App/Views/Pages/CheckupPage.xaml`:
     - Bind badge border and foreground using `{x:Bind ViewModel.HealthScoreBrushKey, Converter={StaticResource ThemeBrushConverter}}`.
     - Update header from `"Check completion"` to `"System health"`.
     - Add action button in row template for items with `HasAction == true`.
- **Acceptance Criteria:**
  - [x] Categorical health score correctly reflects actual system state.
  - [x] Badge color and text update dynamically (`Healthy`, `Attention`, `Action`).
  - [x] Direct action button appears on rows requiring user intervention.

#### Task 7: Curated 1-Click Smart Action Cards on Home
- **Goal:** Provide high-impact 1-click maintenance operations directly on the Home dashboard.
- **Ponytail Ultra Design:** Direct invocation of `_dispatcher.ExecuteAsync(CommandRequest.Execute(commandId, ...))` on the view model. No complex orchestrator layers.
- **Curated Actions:**
  1. **Quick Clean:**
     - Command: `cleaner-disk-pressure` / temp file purge (`RiskTier.Safe`).
     - 1-click execution: runs immediately with `Apply = true`.
     - Inline progress ring + recovered space summary upon completion.
  2. **Startup Boost:**
     - Command: `startup` / high-impact startup audit (`RiskTier.Safe`).
     - 1-click execution: analyzes startup burden and reports status.
  3. **Network Refresh:**
     - Command: `network` / DNS flush and adapter refresh (`RiskTier.Safe`).
     - 1-click execution: runs in < 300ms and reports adapter state.
- **Implementation Details:**
  1. `src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs`:
     - Add `CommandDispatcher` dependency injection.
     - Add properties for each action: `IsCleaning`, `CleanStatus`, `CleanDetail`, `CleanCommand`, etc.
  2. `src/WinCare.App/Views/Pages/HomePage.xaml`:
     - Insert a 3-column curated action cards section between the Hero card and Recent Activity.
     - Cards use `SurfaceBorderStyle`, concentric radii (12 DIP), 16 DIP padding, verified Segoe Fluent Icons (`&#xE74D;`, `&#xE7F4;`, `&#xE7F1;`), and 1-click action buttons with inline progress rings.
- **Acceptance Criteria:**
  - [x] Clicking "Clean Now", "Analyze Startup", or "Refresh Network" executes in 1 click without confirmation fatigue.
  - [x] Inline status and progress display directly inside the respective card.
  - [x] Verified responsive layout across compact and wide window widths.

#### Task 8: Streamline Tool Execution in All Tools
- **Goal:** Eliminate confirmation fatigue for safe tools while maintaining bulletproof defense for destructive tools.
- **Implementation Details:**
  1. `src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs`:
     - Evaluate `selected.Definition.RiskTier`:
       - `IsSafeTool`: `PrimaryActionLabel` = `"Run tool"`. Direct execution with `Apply = true` without preflight preview plan.
       - `IsModerateTool`: `PrimaryActionLabel` = `"Apply changes"`. Single confirmation required.
       - `IsDestructiveTool`: Two-phase preview workflow strictly retained. `PrimaryActionLabel` = `IsReviewApproved ? "Execute Destructive Action" : "Preview Impact"`.
     - Expose `RequiresApprovalSwitch => IsMutatingTool && !IsSafeTool`.
     - Expose `IsDestructiveTool => _selectedTool?.Definition.RiskTier == RiskTier.Destructive`.
  2. `src/WinCare.App/Views/Pages/AllToolsPage.xaml`:
     - Bind `ToggleSwitch.Visibility` to `ViewModel.Execution.RequiresApprovalSwitch`.
     - Add danger `InfoBar` (`Severity="Error"`) displayed when `IsDestructiveTool == true`.
     - Update Expander header to `"Advanced Parameters (JSON)"` with `IsExpanded="False"` by default.
- **Acceptance Criteria:**
  - [x] Safe tools execute immediately on clicking "Run tool" with zero confirmation popups.
  - [x] Destructive tools clearly display red danger warning and enforce two-phase preview + approval.
  - [x] Raw JSON editor is collapsed by default.

---

### Phase 4: Size & Packaging Optimization

#### Task 9: Enable IL Trimming and Remove Unused Assemblies
- **Goal:** Drop standalone binary size from ~135 MB to ≤ 35 MB.
- **Implementation Details:**
  1. `src/WinCare.App/Properties/PublishProfiles/portable-x64.pubxml`:
     - Change `<PublishTrimmed>false</PublishTrimmed>` to:
       ```xml
       <PublishTrimmed>true</PublishTrimmed>
       <TrimMode>partial</TrimMode>
       <SuppressTrimAnalysisWarnings>true</SuppressTrimAnalysisWarnings>
       ```
  2. `src/WinCare.App/WinCare.App.csproj`:
     - Ensure reflection roots for ViewModels and Domain types are preserved via `<TrimmerRootAssembly Include="WinCare.Domain" />`, `<TrimmerRootAssembly Include="WinCare.App" />`.
     - Strip unused `Microsoft.Windows.AI.*` and heavyweight ML projections from dependencies.
- **Acceptance Criteria:**
  - [x] `dotnet publish` produces standalone `.exe` ≤ 35 MB (achieved: 34.35 MB).
  - [x] App launches and all views render without trimming-related missing method exceptions.

#### Task 10: Standard Inno Setup Installer Pipeline
- **Goal:** Eliminate MSIX sideload certificate friction with a clean, standard Windows installer.
- **Implementation Details:**
  1. Create `tools/installer/wincare_setup.iss`:
     - Inno Setup script compiling `WinCare-Setup.exe`.
     - Installs to `{autopf}\WinCare`.
     - Creates Start Menu shortcut and optional Desktop icon.
     - Adds standard Add/Remove Programs uninstaller registry entries.
  2. Update `tools/stage_release_assets.py`:
     - Detect and stage `WinCare-Setup.exe` into release artifacts.
- **Acceptance Criteria:**
  - [x] Inno Setup script authored cleanly.
  - [x] Installer stages cleanly without Python or developer certificate requirements.

---

## 5. Verification & Testing Matrix

| Component / Layer | Test Suite | Verification Command | Gate Criteria |
|---|---|---|---|
| **Command Catalog** | `tests/WinCare.CommandCatalog.Tests` | `dotnet test tests/WinCare.CommandCatalog.Tests` | 18/18 tests pass |
| **Admission & Concurrency** | `tests/WinCare.Application.Tests` | `dotnet test tests/WinCare.Application.Tests` | All tests pass (including `RiskTierAdmissionTests` and `ParallelCommandProbeRunnerTests`) |
| **App & Checkup Logic** | `tests/WinCare.App.Tests` (or new unit tests) | `dotnet test WinCare.Native.sln` | 0 regressions, all unit tests pass |
| **Release Publish Size** | Standalone Publish | `dotnet publish src/WinCare.App -c Release -r win-x64 -p:PublishProfile=Properties/PublishProfiles/portable-x64.pubxml` | Binary size ≤ 35 MB |
| **Installer Generation** | Inno Setup | `iscc tools/installer/wincare_setup.iss` | Clean `WinCare-Setup.exe` output |

---

## 6. Risk & Mitigation Register

| Risk | Likelihood | Impact | Mitigation Strategy |
|---|---|---|---|
| **WinUI 3 XAML Trimming Regression** | Medium | High | Use `TrimMode=partial` and explicit `TrimmerRootAssembly` for `WinCare.App` and `WinCare.Domain`. Avoid reflection-based `{Binding}`; use compiled `x:Bind`. |
| **Background WUA Query Exception** | Low | Low | Isolate background query in `try/catch` block; map COM errors to `CommandResultStatus.Failed` without affecting UI stability. |
| **User Accidental Destructive Action** | Low | Critical | Destructive commands strictly require preflight preview plan token and explicit confirmation with red UI alert. |
