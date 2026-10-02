# WinCare Redesign Spec: Fluent Native, Clean Re-Release (2026-09)

Status: APPROVED: in execution (2026-09-19). Open decisions applied per the standing mandate
("re-release completely... Can change everything"): D1 version target = 3.0.0 (MSIX monotonicity
forbids a 1.x drop over shipped 2.5.0 builds); D2 purge scope as listed in §4; D3 chrome accent =
user's Windows system accent, teal brand-only; D4 the pending `.gitignore` line committed as its
own chore.
Decided in the prior review round: visual and UX primary, information architecture and
code cleanliness secondary; direction = native Fluent played straight; craft bar =
Windows 11 Settings + Task Manager, hybrid at all levels; density split deliberately by surface
(calm defaults, with detailed evidence available); brand teal stays in logo/wordmark and
documentation, not in chrome. Product safety model (risk admission, approval gates, plugin trust,
dispatcher authority) is preserved.

## 1. Verified baseline (2026-09-19)

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | success |
| `dotnet test` (Release x64) | CommandCatalog 24/24, Application 218/218, Infrastructure 311/311 pass |
| `cargo fmt --check` / `clippy -D warnings` / `test` | exit 0; guard 17 tests pass, core tests pass |
| `verify_native_foundation.py` | pass (259 frozen command IDs retained) |
| `tests/native` (132 py tests) | pass |
| plugin CLI py tests / visual tokens / pill contrast | pass (8 pairs ≥ 4.5:1 AA) |

Toolchain verified present: .NET 8.0.416 (matches global.json), Rust 1.97.1 (matches
rust-toolchain.toml), Python 3.11.9, Node v26.1.0.

Audit note: All audit findings below are verified against repository source code and tests. Marked **uncertain** where a claim requires live runtime verification.

## 2. Audit findings (file:line verified)

1. [high] High-contrast collapse: `ThemeResources.xaml:73-75` Success/Warning/Danger all bind to
   `SystemColorWindowTextColor`; `:84-88` four risk-pill backgrounds all bind to
   `SystemColorHighlightColor`. Risk state becomes visually indistinguishable in HC, violating
   DESIGN.md's status contract. Fix in §3.
2. [high] Hand-rolled palette: `ThemeResources.xaml` is 28 keys × 3 themes of literal hex,
   including a Tailwind fingerprint in the pill set (`#047857/#DC2626/#D97706/#E2E8F0/#1F2937`).
   No spacing scale, no elevation, no component tokens: `ControlStyles.xaml` carries 3 font
   families, 4 radii, ~20 one-line styles.
3. [medium] 202 inline layout literals (Margin/Padding/Width/Height/FontSize/CornerRadius/Spacing)
   across `Views/` + `Controls/` XAML, with no single owner of the 4-DIP rhythm documented in
   DESIGN.md:Typography.
4. [medium] Navigation declared in three places: `WinCare.Application/Navigation/NavigationCatalog.cs:8-19`
   (labels, uids, search concepts), `WinCare.App/Services/PageService.cs:29-37` (id→Type map,
   debug-only one-direction assert), and 11 hand-written `Tag=` items in `Views/ShellPage.xaml`.
5. [medium] Global search is inline logic in `MainWindow.xaml.cs` (293 lines, 30 search-related
   references), rather than an isolated component testable at the presentation boundary.
6. [low] `CheckupPageViewModel.cs:35-117` `HealthScoreText/Detail/BrushKey` produce qualitative
   states ("Not checked", "Checking", "Stopped", "Incomplete"). Behavior matches DESIGN.md ("checked-area
   status, not a health score"); the identifiers require neutral renaming.
7. [low] Localization: `NavigationCatalog` carries English labels and search concepts; localization
   status of search-result rendering is audited in wave 4.
8. [info] Only 2 shared controls exist (`CareToolList`, `WidgetContainer`); card shells, section
   headers, and pills are re-composed inline per page.
9. [info] `src/WinCare.App/AppPackages/WinCare.App_2.5.0.0_x64_Test/` on disk is gitignored
   build output; `.agents/`, `.ai-codex/`, `.statamcp/` are gitignored tooling scaffolding. None are commits.

## 3. Target visual architecture (Native Windows 11 Fluent)

Strategy: **Restrained**: neutrals owned by Windows, one accent owned by the user's system
accent. Light/dark/HC handled by the platform, reducing custom palette code.

### 3.1 Theme delegation (resolves findings 1 and 2)

Rewrite `Styles/ThemeResources.xaml` so the same 28 semantic keys remain the public contract
(`ThemeResourceBrushConverter.cs:52-80`, VM brush-key strings, and `verify_visual_tokens.py`
all depend on the names), but values delegate to WinUI theme resources:

- Text: `TextFillColorPrimary/Secondary/Disabled` · Surfaces: `SolidBackgroundFillColorBase/Secondary`,
  `LayerFillColorDefault`, `CardBackgroundFillColorDefault` · Borders: `ControlStrokeColorDefault`,
  `CardStrokeColorDefault` · Accent: `AccentFillColorDefault` (resolves to system accent) and
  `TextOnAccentFillColorPrimary` · Status: `SystemFillColorSuccess/Caution/Critical/Neutral`.
  These four carry real per-theme and HC values from Microsoft, resolving the HC collapse.
- Standalone `SolidColorBrush Color="{ThemeResource X}"` resolution under theme changes is verified
  via `ThemeResourceBrushConverterRefreshTests` covering the `RefreshBrushes()` path.
- Compile-time safety: invalid resource keys fail XAML compilation.
- `verify_visual_tokens.py` and `verify_pill_contrast.py` keep their ≥ AA thresholds unchanged;
  expected-value tables match the delegation. Pill risk meaning never relies on color alone;
  each pill carries text and borders.

### 3.2 Tokens and typography (resolves finding 3)

- `ControlStyles.xaml` gains `Spacing` (4/8/12/16/24/32) and keeps the 12/8/4 radius scale,
  matching DESIGN.md's stated rhythm.
- Custom text styles become `BasedOn` native `TitleTextBlockStyle/SubtitleTextBlockStyle/BodyTextBlockStyle/CaptionTextBlockStyle` aliases, preserving current sizes (34/20/15/14/13)
  where DESIGN.md fixed them and inheriting Windows type behavior otherwise.
- Views migrate inline literals to tokens/styles per page family
  (Home → Checkup → Care×3 → Power tools → Activity → Extensions/Troubleshoot → Settings/Help/About).
  Net effect: literal count trends toward 0 without layout redesign; composition stays task-first per DESIGN.md:Composition.

### 3.3 Component layer (resolves findings 5 and 8)

- New `Controls/GlobalSearchBox.xaml(.cs)` extracted from `MainWindow.xaml.cs`: same behavior,
  same automation ids, query-submit routing to Power tools fallback preserved. View concerns stay
  in code-behind; search provider calls use the existing service seam.
- Shared styles promoted: `SectionHeaderTextStyle` exists; add `PageHeaderControl` (title+description+actions)
  and use existing `SurfaceBorderStyle` where card shells re-derive `Border`. `WidgetContainer` and `CareToolList` remain.

### 3.4 Shell and navigation (resolves findings 4 and 7)

- Keep XAML-declared `NavigationViewItem`s (preserving `x:Uid` localization capabilities).
  Enforce the contract via `tools/verify_native_foundation.py` to require three-way agreement:
  every catalog id maps to a PageService key and a ShellPage Tag (in both directions, with the `{about}` hidden exception).
  The runtime `Debug.Assert` also validates catalog to PageService mappings.
- Route labels through `ResourceLoader`/resw so localization covers navigation text.

### 3.5 Code cleanliness (resolves finding 6)

- `HealthScore*` → `CheckupStatus*` rename across ViewModel, XAML bindings, and consumers.
  Characterization test added first, rename itself behavior-neutral, bindings remain internal to the application.
- Large ViewModels (`ToolExecutionViewModel`, `PluginStore/Checkup/AllToolsPage`): No split
  scheduled without concrete performance or maintenance evidence, adhering to minimal implementation principles.

## 4. Clean re-release

- **Version 3.0.0**, not a 1.x restart: MSIX in-place update requires the manifest version to be
  monotonic, and signed 2.5.0-rc builds exist in releases. 3.0.0 is the clean-slate major.
  Bump set: `Directory.Build.props`, `Package.appxmanifest`,
  `native/*/Cargo.toml` + `Cargo.lock` + `lib.rs` + `ffi_contract.rs`, `CHANGELOG.md`, README/docs
  references, and the 28 `CommandCatalog/Plugins/*.json` manifest versions.
- **CHANGELOG.md rewritten** as a fresh 3.0.0 history; prior history remains in git.
- **History purge:**
  Remove `design/redesign-2026-09/` concept studies (concept art, not runtime evidence);
  remove superseded process docs `docs/PRODUCT-UX-FINALIZATION.md`, `docs/Refinement-Verification-2026-09-18.md`,
  `docs/Release-Readiness.md`; consolidate `VALIDATION.md` + `FINAL-VALIDATION.md` into
  `docs/Validation.md` and update `finalize_native_release.py` file lists + `test_finalization.py` + pointers together.
- **Retained assets:** `migration/oracle/` (referenced by test suites and `CommandDefinition.cs`),
  `docs/images/runtime-*.png` (execution evidence), brand assets in `design/`, architecture and user docs.

## 5. Execution waves

0. Spec approved; record decisions in PRODUCT.md and DESIGN.md.
1. Gates-first: nav three-way contract in `verify_native_foundation.py`.
2. Theme delegation + HC fix + token scale + validators updated. Gate: build, py validators, Application.Tests.
3. Literal migration per page family + shared styles. Gate: build, py validators.
4. GlobalSearchBox extraction + search-label i18n audit/fix. Gate: build, Application/Infra tests.
5. HealthScore rename (characterization test first). Gate: full `dotnet test`.
6. Re-release: version set, changelog rewrite, history purge + reference updates. Gate: full baseline matrix.
7. Finish: full gate matrix and DESIGN.md synchronization. Runtime verification for Narrator, High Contrast, and display scaling is executed per `docs/Windows-Validation.md`.

## 6. Explicit non-changes

Safety model, dispatcher, catalog IDs/frozen fixtures, IPC/ABI, plugin trust/signing, CLI,
Rust code, CI release pipeline, quality-gate thresholds.

## 7. Deep-review appendix (engineering-protocol pass, 2026-09-19)

Targeted scans beyond the UI layer, all resolved against source:

- Rust non-test `unwrap/expect`: `cleaner.rs:650+` are inside its `mod tests` (no `cfg(test)`
  anchor needed: `use super::*` proves it); `lib.rs:2383/2385` are fixed-length slice
  conversions (`[0..8]`/`[8..16]` of a 16-byte array → infallible); `lib.rs:2434+` all after the
  `cfg(test)` at 2412; `thermal.rs:134` after its 116 anchor; `toast.rs:60` is a documented
  infallible `write!` to `String`. No panics in non-test source.
- FFI boundary: `catch_unwind` present at extern-"C" entry points (≥5 sites, 62 boundary functions
  total): panics cannot unwind into C#.
- Rust unsafe blocks: All `unsafe {` blocks in `wincare-core/src/lib.rs` are audited and documented with
  formal safety invariants.
- `verify_pill_contrast.py` reads literal hex only, so pill background/text brushes stay
  literal hex (measured by the 4.5:1 WCAG AA gate); non-pill brushes delegate. In High Contrast,
  pills carry text/glyph state with system text/border colors.
- Status pill templates have 2 consumers (`WidgetContainer.xaml:24`, `PluginStorePage.xaml:69`);
  `PAIRS_SPEC` covers all pairs.
- Wave 1: `verify_native_foundation.py` enforces three-way nav agreement
  (catalog ⇄ PageService ⇄ ShellPage Tags, with the `{about}` hidden exception).
- Wave 3: real XAML spacing values sit on the documented scale; card shells use `DashboardCardStyle`/`SurfaceBorderStyle`.
- Wave 4: global-search ranking extracted to `Application/Navigation/GlobalSearchService`.
  Native gate `test_chrome_labels_agree_across_catalog_xaml_and_resources` pins XAML⇄resw equality and
  catalog ⇄ resw label sets.
- Wave 5: `HealthScore{Text,Detail,BrushKey}` → `CheckupStatus*` in `CheckupPageViewModel` + `CheckupPage.xaml`.
  The values represent qualitative states ("Action needed", "Looks good"), not synthetic scores.
