# Finalized WinCare Cyber-Teal & Impeccable Quality Checklist

**Status:** ALL PHASES COMPLETE & VERIFIED

## Phase 1: Unified Cyber-Teal Design System & Theme Foundations
- [x] **Task 1.1**: Update `src/WinCare.App/Styles/ThemeResources.xaml`
  - [x] Set Light theme `AccentTealBrush` to `#007A99` and `AccentTealSubtleBrush` to `#1A007A99`
  - [x] Set Dark theme `AccentTealBrush` to `#00D2B4` and `AccentTealSubtleBrush` to `#1A00D2B4`
  - [x] Fix Dark theme amber pill contrast: set `PillAltTextBrush` to `#1A1A1A` for `#F59E0B` amber background (8.10:1 contrast)
- [x] **Task 1.2**: Update `src/WinCare.App/Styles/ControlStyles.xaml`
  - [x] Add `FocusVisualPrimaryBrush` (`#00D2B4` / `#007A99`) and `FocusVisualSecondaryBrush` to `ListViewItem` style
  - [x] Standardize corner radius and typography setters

## Phase 2: Dynamic Status Pill Engine & Data Binding
- [x] **Task 2.1**: Update `src/WinCare.App/ViewModels/Pages/ToolRowViewModel.cs`
  - [x] Add `StatusPillBackgroundBrush` and `StatusPillForegroundBrush` properties driven dynamically by tool risk level
- [x] **Task 2.2**: Update `src/WinCare.App/Views/Pages/AllToolsPage.xaml`
  - [x] Line 257: Replace hardcoded `PillReadOnlyBgBrush` with `{x:Bind StatusPillBackgroundBrush, Mode=OneWay}` and `{x:Bind StatusPillForegroundBrush, Mode=OneWay}`

## Phase 3: Layout Collision Resolution & Responsive Header Synchronization
- [x] **Task 3.1**: Update `src/WinCare.App/Views/Pages/ActivityPage.xaml`
  - [x] Insert dedicated `RowDefinition Height="Auto"` for `AttentionInfoBar`
  - [x] Update `SectionSelector` to `Grid.Row="2"` and main content border to `Grid.Row="3"`
- [x] **Task 3.2**: Update Header Visibility in `ActivityPage.xaml` and `AllToolsPage.xaml`
  - [x] Bind `TableHeader` visibility to `{x:Bind views:LayoutVisibility.InvertBoolToVisibility(IsCompact), Mode=OneWay}`

## Phase 4: UI/UX Pro Max Ergonomics, Accessibility & Visual Polish
- [x] **Task 4.1**: Upgrade Favorite Button in `AllToolsPage.xaml`
  - [x] Replace text button with star icon toggle (`FontIcon Glyph="\xE735"`)
- [x] **Task 4.2**: Add ToolTips & Keyboard Accelerators
  - [x] Add `ToolTipService.ToolTip` to tool table headers and action buttons
  - [x] Add keyboard accelerators (`Ctrl+F` search focus)
- [x] **Task 4.3**: Refine Advanced Details Expander
  - [x] Set `LineHeight="18"` and `Padding="12"` inside `Expander` text box

## Phase 5: Review Swarm Remediation & Architectural Hardening
- [x] **Fix Now 1**: Update `ToolRowViewModel.cs` to resolve `Microsoft.UI.Xaml.Media.Brush` instances via `Application.Current.Resources`.
- [x] **Fix Now 2**: Handle `"Low"` risk explicitly in `StatusPillBackgroundBrush` switch expression (`"Elevated" or "Moderate" or "Low"`).
- [x] **Fix Now 3**: Align `ActivityPage.xaml.cs` breakpoint threshold to `920.0` DIP (`LayoutVisibility.CompactBreakpointDip`).
- [x] **Fix Soon 1**: Clean up redundant manual code-behind visibility toggles in `Page_SizeChanged` across `ActivityPage.xaml.cs` and `AllToolsPage.xaml.cs`.
- [x] **Fix Soon 2**: Add `IsChecked="{x:Bind ViewModel.IsSelectedToolFavorite, Mode=OneWay}"` to `FavoriteToggleButton` in `AllToolsPage.xaml`.
- [x] **Optional Follow-Up 1**: Add debouncing to `SearchText` and preserve `SelectedTool` across filters in `AllToolsPageViewModel.cs`.

## UI/UX Pro Max Verification Checkpoints
- [x] Audit Note: Phase 4 phantom checkmarks (ToolTips, Ctrl+F, Expander LineHeight/Padding) were verified as correctly implemented by parallel agent.
- [x] Re-run python verification scripts (`tools/verify_visual_tokens.py`, `tools/verify_pill_contrast.py`) -> PASSED
- [x] Re-run GUI test suites (`tools/validate_gui.py`, `tools/test_gui.py`) -> PASSED (0 errors, 9/9 tests OK)
- [x] Re-run Rust unit/FFI test suite (`cargo test`) -> PASSED (18/18 tests OK)
- [x] Re-run `$impeccable audit` and `$impeccable critique` to confirm health score -> VERIFIED & PERSISTED

## Post-Shipping Cleanup (2026-08-09)
- [x] Removed `tasks/plan_part1.md` — superseded split artifact from plan construction phase
- [x] Removed `tasks/plan_temp.py` — empty ephemeral temp file
- [x] Removed `.review-fix/trigger-2.txt` — stale CI trigger; purpose fulfilled
- [x] Updated `.gitignore` to cover `build_out.txt` and `.review-fix/` permanently

## CodeRabbitAI Remediation (2026-08-09) — commit 7f98847
All 16 actionable CodeRabbitAI PR review comments addressed:
- [x] **Rust / accumulate_dir_size**: `symlink_metadata` guard prevents stack overflow on cyclic junction trees
- [x] **Rust / read_registry_os_build**: Aligned `[u16;64]` buffer eliminates u8→u16 pointer UB; validates REG_SZ type
- [x] **Rust / GlobalMemoryStatusEx**: Failure surfaces as explicit error JSON instead of zeroed memory
- [x] **SystemInfoCommandHandler**: `JsonException` → explicit `Failed` outcome
- [x] **StorageHealthCommandHandler**: Per-drive IOException/UnauthorizedAccessException → skip gracefully
- [x] **NetworkStatusCommandHandler**: `n.Speed` guarded; virtual/tunnel NIC exception → -1 sentinel
- [x] **ActivityPageViewModel.RefreshFromJournal**: Maps `ActivityRecord → PageRow`; populates `CurrentRows` per-section
- [x] **ActivityPageViewModel.SelectSection**: Override serves rows from backing lists on tab switch
- [x] **CommandRuntime.LastJournal**: Added; parameterless ctor compilation error resolved
- [x] **TabbedPageViewModel.SelectSection**: Made `virtual`
- [x] **ActivityPage.xaml SelectorBarItems**: AutomationId added to all 4 items
- [x] **verify_pill_contrast.py**: Parses live hex from ThemeResources.xaml; all 8 pairs WCAG AA ✓
- [x] **verify_visual_tokens.py**: StatusPillTemplate presence check added
- [x] **release_checklist.py**: OSError catch; missing executables → FAIL not crash
- [x] **AllToolsPage.xaml glyph `&#xE735;`**: Already correct — CR stale comment
- [x] **Ctrl+F accelerator**: Already wired in code-behind — CR stale comment
