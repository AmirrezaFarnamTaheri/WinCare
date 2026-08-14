# Release Notes

## 2.4.0-rc1

Released: 2026-08-09

### Native WinUI 3 shell

- Replaced the WPF product surface with an approved WinUI 3 shell using task-based navigation,
  page-level tabs, responsive tabular lists, system theming, keyboard support, and UI Automation metadata.
- Preserved all 259 stable command IDs in a typed embedded catalog.

### Cyber-Teal visual identity

- Unified **Cyber-Teal** color system across Light and Dark themes:
  - Light: `AccentTealBrush` `#007A99` (5.1:1 contrast), `AccentTealSubtleBrush` `#1A007A99`
  - Dark: `AccentTealBrush` `#00D2B4`, `AccentTealSubtleBrush` `#1A00D2B4`
- Dynamic status pill engine: mutating commands show crimson `[ MUTATING ]`, elevated show amber
  `[ ELEVATED ]`, read-only show emerald `[ READ-ONLY ]`, unreleased show slate `[ NOT READY ]`.
- All pill color pairs pass **WCAG 2.1 AA** (≥ 4.5:1), verified by `tools/verify_pill_contrast.py`.
- Monospaced Cascadia Code pill badges with 4px corner radius, crisp pixel edges.

### Accessibility and keyboard ergonomics

- 3px `FocusVisualPrimaryBrush` focus rings on all `ListViewItem` elements (teal in dark, deep teal in light).
- `Ctrl+F` keyboard accelerator focuses the tool search box.
- `ToolTipService.ToolTip` on all table column headers and action buttons.
- All column headers include WCAG-compliant `AutomationProperties` metadata.
- `FavoriteToggleButton` bound with `IsChecked="{x:Bind ViewModel.IsSelectedToolFavorite, Mode=OneWay}"`.

### Layout and responsiveness

- Fixed `ActivityPage.xaml` grid-row collision: `AttentionInfoBar` now has its own dedicated
  `RowDefinition Height="Auto"` (row 1), eliminating Z-index overlap with `SectionSelector` (row 2).
- `TableHeader` visibility is bound to `InvertBoolToVisibility(ViewModel.IsCompactLayout)` via
  `{x:Bind}` compiled binding — column headers hide cleanly at the 920 DIP compact breakpoint.
- Removed the non-observable `IsCompact` proxy property from `AllToolsPage.xaml.cs`; XAML binds
  directly to the INPC-compliant `ViewModel.IsCompactLayout`.

### Command dispatcher architecture

- Fail-closed C# command dispatcher with a typed embedded catalog of all 259 command IDs.
- `MigrationStatus` is checked before handler lookup, ensuring catalog-defined status codes are
  returned before runtime handler resolution.
- `request.Apply` gate on review-approval: dry-run and preview flows bypass the review guard.
- Exception logs emit only the exception type name — `ex.Message` is never written to the journal
  to prevent file-path or PII leakage.
- Removed static mutable `CommandRuntime.LastJournal` global: eliminates race conditions in
  concurrent and test contexts.
- Replaced command-specific cleanup stubs with shared native path admission: canonical roots,
  reparse-point rejection, bounded traversal, and junction-safe deletion/copy behavior.
- Added one application-scoped activity journal through `AppRuntime`; cached Activity navigation
  refreshes from the same injected journal instead of mutable global fallback state.
- Added typed JSON command parameters to All Tools; malformed or non-object JSON fails locally before
  dispatch, and parameter changes invalidate prior mutation approval.
- Bounded parameter JSON/string/array inputs, rejected duplicate durable state IDs, and replaced the
  former universal 30-second UI deadline with command-class execution budgets for servicing, updates,
  downloads, backups, ordinary mutations, and diagnostics.
- Hardened native child-process lookup to prefer Windows System32 tools, and made ETW/Xbox rollback
  outcomes explicit instead of suppressing cleanup failures.

### Rust native core

- Bounded Rust C ABI foundation (`native/wincare-core`): `dir_size` and `sys_info` primitives.
- All 5 FFI exports wrapped in `std::panic::catch_unwind` — panics crossing the FFI boundary are
  safely converted to error status codes (UB eliminated).
- `// SAFETY:` invariant comments on every `unsafe` block.
- Comprehensive `ffi_contract.rs` test suite: null-pointer safety, buffer-too-small paths,
  all exports verified with valid inputs, hash correctness assertions.

### Test tooling

- `validate_gui.py`: now validates `{ThemeResource}` references in addition to `{StaticResource}`
  and `{DynamicResource}`; targets WinUI 3 XAML styles, not the legacy PowerShell surface.
- `verify_visual_tokens.py`: upgraded from naive string-count to XML tree parsing with per-dictionary
  (Light / Dark / HighContrast) token verification — false positives eliminated.
- `verify_pill_contrast.py`: validates all 8 deployed pill color pairs across the supported themes,
  including the Light ReadOnly, Light NotReady, and PillAltTextBrush pairings.

### Design system accuracy

- `DESIGN.md` updated to match actual `ThemeResources.xaml` values (previously documented
  `status-crimson Dark` as `#EF4444`; actual is `#C41A1A`; and `bg-shell Dark` as `#0B0F17`; actual
  is `#202020`).
- `PillNotReadyBgBrush` in High Contrast mode fixed to use `SystemColorHighlightColor` +
  `SystemColorHighlightTextColor` pairing — eliminates same-color-on-same-color accessibility failure.

### Async reliability

- `AllToolsPageViewModel.DebounceSearch` uses real `async/await Task.Delay(250, token)` debouncing
  with `CancellationTokenSource` pattern — search no longer triggers full list rebuild on every
  keystroke; outer `catch (Exception)` prevents unobserved exceptions from crashing the WinUI 3 process.

### Native command migration

- Removed the bulk-generated handler family that returned canned machine state or fabricated mutation
  success; all 259 stable IDs now route through one fail-closed native executor.
- Added explicit routing for all 155 read-only and 104 mutating commands, with parameter preflight for
  every mutating command before approval can be granted.
- Added real Windows collectors and bounded native/process integrations for system, storage, network,
  security, Windows Update, AppX, browser/Steam, desktop, telemetry, downloads, offline servicing,
  maintenance, Studio, and tooling surfaces.
- Reworked downloads to retain partial files on suspend, resume with HTTP Range, verify optional SHA-256,
  and report partial batch failures truthfully.
- Corrected Studio integrations: fixed-command ADB inventory and Xbox FSE operations require reviewed
  tool hashes; WezTerm writes the local status module instead of accepting arbitrary CLI execution.
- Temporary security-control reduction now fails closed until an authenticated native recovery host can
  guarantee automatic restoration; the incorrect firewall substitution was removed.
- Removed false generic `UndoAvailable` claims where no recovery action is wired.
- Moved historical executable PowerShell into the separate legacy-oracle artifact; finalized native
  source contains no `.ps1`, `.psm1`, or `.psd1` runtime dependency.

### Release candidate status

This release candidate is not production-promotable. Current migration status:
- 259 commands cataloged
- 259 native command routes implemented
- 0 behavior-verified
- 0 native implementation blockers
- 259 production behavior-verification blockers

`Implemented` means a typed native route exists and fails closed when its platform, dependency, or safety
preconditions cannot be satisfied. It is not a claim of Windows parity. Production finalization continues
to refuse promotion until all 259 commands reach `BehaviorVerified`.

---

## 2.2.0

The final PowerShell/WPF release remains available in Git history and the legacy oracle archive
for parity work.

## 1.1.0

Historical PowerShell module compatibility baseline retained for deterministic legacy packaging
and validator contracts.
