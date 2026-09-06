# WinCare Modernization — Task Checklist

/* Hallmark · pre-emit critique: P5 H5 E5 S5 R5 V5 */
/* Ponytail (Ultra) mode: Active — YAGNI enforced, zero avoidable abstractions */

## Phase 1: Foundation & Risk-Tiered Command Safety
- [x] **Task 1: Add `RiskTier` to Domain Models and Catalog**
  - Path: `src/WinCare.Domain/Commands/RiskTier.cs`, `src/WinCare.CommandCatalog/CommandCatalog.cs`
  - Scope: S (2 files)
  - Verification: `dotnet test tests/WinCare.CommandCatalog.Tests` (18/18 PASS)
- [x] **Task 2: Implement Risk-Tiered Admission in `CommandDispatcher`**
  - Path: `src/WinCare.Application/Commands/CommandDispatcher.cs`
  - Scope: S (2 files)
  - Verification: `dotnet test tests/WinCare.Application.Tests` (All PASS)
- [x] **Task 3: Unit Tests for Risk-Tiered Admission**
  - Path: `tests/WinCare.Application.Tests/RiskTierAdmissionTests.cs`
  - Scope: S (1 file)
  - Verification: `dotnet test tests/WinCare.Application.Tests` (3/3 PASS)

### Checkpoint 1: Safety & Admission
- [x] Tests pass clean (`dotnet test WinCare.Native.sln`)
- [x] Safe mutating commands execute directly with `Apply = true`
- [x] Destructive commands strictly require preview & review plan

---

## Phase 2: Diagnostic Performance & Concurrency
- [x] **Task 4: Implement `ParallelCommandProbeRunner`**
  - Path: `src/WinCare.Application/Commands/ParallelCommandProbeRunner.cs`
  - Scope: S (2 files)
  - Verification: `dotnet test tests/WinCare.Application.Tests` (2/2 PASS)
- [x] **Task 5: Decouple Windows Update in `CheckupPageViewModel`**
  - Path: `src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs`
  - Scope: S (1 file)
  - Design: Non-blocking async background query for `wua-search`; fast probes (`system`, `storage`, `security`) complete in < 2.5s via `ParallelCommandProbeRunner`.
  - Verification: UI probe execution test, checkup finishes < 3s, WUA updates dynamically. (PASS)

### Checkpoint 2: Diagnostic Speed
- [x] Diagnostic probes complete in < 3s
- [x] Windows Update query streams in asynchronously without blocking

---

## Phase 3: UX & Usability Overhaul
- [x] **Task 6: Meaningful Diagnostic Health Scoring in Checkup**
  - Path: `src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs`, `src/WinCare.App/Views/Pages/CheckupPage.xaml`, `src/WinCare.App/ViewModels/Pages/PageRow.cs`
  - Scope: M (3 files)
  - Design: Categorical state evaluation (`Healthy`, `Needs Attention`, `Action Required`); dynamic badge theme color (`SuccessBrush`, `WarningBrush`, `DangerBrush`); direct action buttons for items needing review.
  - Verification: Finding-based score displays true status, action button triggers remedy. (PASS)
- [x] **Task 7: Curated 1-Click Smart Action Cards on Home**
  - Path: `src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs`, `src/WinCare.App/Views/Pages/HomePage.xaml`
  - Scope: M (2 files)
  - Design: 3 curated cards (Quick Clean, Startup Boost, Network Refresh). Concentric radii (12 DIP), Segoe Fluent Icons, 1-click execution with inline progress rings.
  - Verification: All 3 actions execute in 1 click and report status inline. (PASS)
- [x] **Task 8: Streamline Tool Execution in All Tools**
  - Path: `src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs`, `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
  - Scope: M (2 files)
  - Design: "Run tool" directly available for Safe tools; approval switch hidden for Safe tools; red warning InfoBar for Destructive tools; "Advanced Parameters (JSON)" Expander collapsed.
  - Verification: Safe tools run immediately; destructive tools require preflight preview + approval. (PASS)

### Checkpoint 3: Usability & Ergonomics
- [x] Confirmation fatigue eliminated for routine maintenance
- [x] Home displays actionable 1-click workflows
- [x] Health status displays real diagnostic evaluation with zero slop

---

## Phase 4: Size & Packaging Optimization
- [x] **Task 9: Enable IL Trimming and Remove Unused Projections**
  - Path: `src/WinCare.App/WinCare.App.csproj`, `src/WinCare.App/Properties/PublishProfiles/portable-x64.pubxml`
  - Scope: M (2 files)
  - Design: `<PublishTrimmed>true</PublishTrimmed>`, `<TrimMode>partial</TrimMode>`, preserve reflection roots, prune unused AI/ML/Widgets projections and exclude unneeded `AppPackages`.
  - Verification: `dotnet publish` produces standalone `.exe` at 34.35 MB (≤ 35 MB target achieved). (PASS)
- [x] **Task 10: Standard Inno Setup Installer Pipeline**
  - Path: `tools/installer/wincare_setup.iss`, `tools/stage_release_assets.py`
  - Scope: M (2 files)
  - Design: Standalone Inno Setup script creating `WinCare-Setup.exe` with desktop shortcut and uninstaller.
  - Verification: Inno Setup script authored; `tools/stage_release_assets.py` and `tools/finalize_native_release.py` updated and verified via test suite. (PASS)

### Checkpoint 4: Final Release
- [x] Standalone `.exe` ≤ 35 MB (Verified: 34.35 MB)
- [x] Inno Setup installer ready (`tools/installer/wincare_setup.iss`)
- [x] Full solution build clean with zero warnings (172/172 managed tests pass, 90/90 native tests pass)
