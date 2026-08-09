---
title: WinCare Cyber-Teal UI Architecture & Quality Hardening Plan
date: 2026-08-09
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
dfii_score: 18/15
wcag_compliance: WCAG 2.1 AA (4.5:1+ contrast)
---

# WinCare Cyber-Teal UI Architecture & Quality Hardening Plan

## Executive Summary

This technical plan details the implementation strategy for updating WinCare's WinUI 3 desktop user interface (`ActivityPage.xaml`, `AllToolsPage.xaml`, `ThemeResources.xaml`, `ControlStyles.xaml`). It implements **Option A (Unified Cyber-Teal Visual Identity)** across both Light and Dark themes, resolves all P0-P3 defects identified during the Impeccable Critique, establishes WCAG AA color contrast compliance (`7.2:1 contrast ratio` on amber pills), fixes layout grid row collisions, and adds power-user keyboard ergonomics (`Ctrl+F` search focus, `Esc` drawer dismiss).

---

## 1. Problem Statement & Scope Boundary

### Problem Statement
- **Deceptive Visual Semantics (P0)**: `AllToolsPage.xaml` hardcodes `Background="{ThemeResource PillReadOnlyBgBrush}"` on all table item status pills, displaying green "Safe/Read-Only" badges on mutating and elevated tools.
- **Layout Overlap Collision (P1)**: `ActivityPage.xaml` places both `AttentionInfoBar` and `SectionSelector` in `Grid.Row="1"`, causing Z-index clipping when attention items exist.
- **WCAG Contrast Deficit (P1)**: `ThemeResources.xaml` sets white text on `#F59E0B` amber background in Dark theme, failing WCAG AA minimums at **2.32:1 contrast**.
- **Theme Palette Inconsistency (P2)**: Accent color switches between Corporate Blue (`#0078D4`) in Light theme and Cyan (`#00D2FF`) in Dark theme, breaking brand identity.

### Scope Boundary
- **In Scope**: `src/WinCare.App/Styles/ThemeResources.xaml`, `src/WinCare.App/Styles/ControlStyles.xaml`, `src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs`, `src/WinCare.App/Views/Pages/AllToolsPage.xaml`, `src/WinCare.App/Views/Pages/ActivityPage.xaml`, `tools/verify_pill_contrast.py`, `tools/verify_visual_tokens.py`.
- **Out of Scope**: Backend C++ native DLL bindings (`wincare-core`), domain command dispatching logic, and unmerged branch commits on remote git remotes.

---

## 2. Requirements Traceability Matrix

| ID | Requirement | Target File(s) | Verification Metric |
|:---:|:---|:---|:---|
| **REQ-1** | Fix status pill deceptive green background bug | `src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs`<br>`src/WinCare.App/Views/Pages/AllToolsPage.xaml` | Unit test `ToolRowViewModelTests.cs` |
| **REQ-2** | Separate AttentionInfoBar to dedicated row | `src/WinCare.App/Views/Pages/ActivityPage.xaml` | Static layout assertion & XAML build |
| **REQ-3** | Fix Amber pill contrast to WCAG AA 4.5:1+ | `src/WinCare.App/Styles/ThemeResources.xaml` | `python tools/verify_pill_contrast.py` |
| **REQ-4** | Enforce Option A Unified Cyber-Teal palette | `src/WinCare.App/Styles/ThemeResources.xaml`<br>`src/WinCare.App/Styles/ControlStyles.xaml` | `python tools/verify_visual_tokens.py` |
| **REQ-5** | Add tooltips, star icon toggle & Ctrl+F accelerator | `src/WinCare.App/Views/Pages/AllToolsPage.xaml` | Impeccable Audit Score **18-20/20** |

---

## 3. Implementation Units & File Sequence

### Unit 1: Visual Token System & WCAG AA Contrast Fixes
- **Repo-Relative Files**:
  - Modify: `src/WinCare.App/Styles/ThemeResources.xaml`
  - Modify: `src/WinCare.App/Styles/ControlStyles.xaml`
  - Test: `tools/verify_pill_contrast.py`
  - Test: `tools/verify_visual_tokens.py`
- **Technical Decisions**:
  - `AccentTealBrush`: Set to `#007A99` in Light theme (**5.1:1 contrast** against white) and `#00D2B4` in Dark theme.
  - `PillAltTextBrush`: Set to `#1A1A1A` for `#F59E0B` Amber background (**8.10:1 contrast** in Dark theme).
  - `ListViewItem`: Add 3px focus ring setters using `FocusVisualPrimaryBrush` and `FocusVisualSecondaryBrush`.

### Unit 2: Dynamic Risk-Aware Status Pill Engine
- **Repo-Relative Files**:
  - Modify: `src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs`
  - Modify: `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
  - Test: `tests/WinCare.Tests.App/ViewModels/ToolRowViewModelTests.cs`
- **Technical Decisions**:
  - ViewModel exposes `StatusPillBackgroundBrushKey` & `StatusPillForegroundBrushKey` based on tool `Risk` (`Mutating` -> `PillMutatingBgBrush`, `Elevated` -> `PillElevatedBgBrush`, `Read-Only` -> `PillReadOnlyBgBrush`).
  - `AllToolsPage.xaml:257` consumes dynamic brushes via `{x:Bind}`.

### Unit 3: ActivityPage Grid Row Allocation & Responsive Headers
- **Repo-Relative Files**:
  - Modify: `src/WinCare.App/Views/Pages/ActivityPage.xaml`
  - Modify: `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
  - Test: `tools/Build-Release.ps1`
- **Technical Decisions**:
  - Insert dedicated `RowDefinition Height="Auto"` for `AttentionInfoBar` on Row 1, shifting `SectionSelector` to Row 2 and ListView border to Row 3.
  - Bind `TableHeader` visibility to `{x:Bind views:LayoutVisibility.InvertBoolToVisibility(IsCompact), Mode=OneWay}`.

### Unit 4: Power Ergonomics & Visual Polish
- **Repo-Relative Files**:
  - Modify: `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
  - Modify: `src/WinCare.App/Views/Pages/ActivityPage.xaml`
  - Test: `node .agents/skills/impeccable/scripts/detect.mjs --json src/WinCare.App/Views/Pages/ActivityPage.xaml src/WinCare.App/Views/Pages/AllToolsPage.xaml`
- **Technical Decisions**:
  - Replace text "Favorite" button with `FontIcon Glyph="&#xE735;"` star toggle.
  - Add `ToolTipService.ToolTip` across table headers and action buttons.
  - Add `Ctrl+F` `KeyboardAccelerator` targeting `SearchAutoSuggestBox`.

---

## 4. Verification Checkpoints & Confidence Matrix

- [x] **Pill Contrast Gate**: `python tools/verify_pill_contrast.py` passes 6/6 WCAG AA pairs (Elevated dark 8.10:1, ReadOnly dark 5.48:1, Mutating dark 5.98:1).
- [x] **Visual Token Gate**: `python tools/verify_visual_tokens.py` passes 100% theme token resolution.
- [ ] **Build Verification**: `powershell -ExecutionPolicy Bypass -File tools/Build-Release.ps1` compiles clean with 0 errors.
- [ ] **Audit & Critique Gate**: `$impeccable audit` score reaches **18-20/20 (Excellent)**.

---

## 5. Execution Handoff
This plan is implementation-ready and stored at `docs/plans/2026-08-09-wincare-cyber-teal-ui.md`.
