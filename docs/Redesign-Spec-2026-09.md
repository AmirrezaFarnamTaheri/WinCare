# WinCare Redesign Spec — Fluent Native, Clean Re-Release (2026-09)

Status: APPROVED — in execution (2026-09-19). Open decisions applied per the standing mandate
("re-release completely… Can change everything"): D1 version target = 3.0.0 (MSIX monotonicity
forbids a 1.x drop over shipped 2.5.0 builds); D2 purge scope as listed in §4; D3 chrome accent =
user's Windows system accent, teal brand-only; D4 the pending `.gitignore` line committed as its
own chore.
Decided in the prior interview round (recorded there, not re-litigated): visual+UX primary, IA and
code-cleanliness secondary; direction = native Fluent played straight (canon path); craft bar =
Windows 11 Settings + Task Manager, hybrid at all levels; density split deliberately by surface
(people get calm defaults, technicians drill to evidence); brand teal stays in logo/wordmark and
marketing, not in chrome. Product safety model (risk admission, approval gates, plugin trust,
dispatcher authority) is out of scope for change and preserved verbatim.

## 1. Verified baseline (2026-09-19, this session)

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | success |
| `dotnet test` (Release x64) | CommandCatalog 24/24, Application 218/218, Infrastructure 311/311 pass |
| `cargo fmt --check` / `clippy -D warnings` / `test` | exit 0; guard 17 tests pass, core tests pass |
| `verify_native_foundation.py` | pass (259 frozen command IDs retained) |
| `tests/native` (132 py tests) | pass after restoring `FINAL-VALIDATION.md`, which had been deleted from the working tree pre-session (not by this session; restored via `git checkout`) |
| plugin CLI py tests / visual tokens / pill contrast | pass (8 pairs ≥ 4.5:1 AA) |

Toolchain verified present: .NET 8.0.416 (matches global.json), Rust 1.97.1 (matches
rust-toolchain.toml), Python 3.11.9, Node v26.1.0.

Subagent review note: the Agent tool is broken in this environment (every agent type fails with
`model sonnet/haiku/inherit not found`). All audit findings below are therefore self-verified
in-thread, not independent-review output. Marked **uncertain** where a claim needs runtime proof.

## 2. Audit findings (file:line verified)

1. [high] High-contrast collapse: `ThemeResources.xaml:73-75` Success/Warning/Danger all bind to
   `SystemColorWindowTextColor`; `:84-88` four risk-pill backgrounds all bind to
   `SystemColorHighlightColor`. Risk state becomes visually indistinguishable in HC — violates
   DESIGN.md's own status contract. Fix in §3.
2. [high] Hand-rolled palette: `ThemeResources.xaml` is 28 keys × 3 themes of literal hex,
   including a Tailwind fingerprint in the pill set (`#047857/#DC2626/#D97706/#E2E8F0/#1F2937`).
   No spacing scale, no elevation, no component tokens: `ControlStyles.xaml` carries 3 font
   families, 4 radii, ~20 one-line styles.
3. [medium] 202 inline layout literals (Margin/Padding/Width/Height/FontSize/CornerRadius/Spacing)
   across `Views/` + `Controls/` XAML — no single owner of the 4-DIP rhythm documented in
   DESIGN.md:Typography.
4. [medium] Navigation declared in three places: `WinCare.Application/Navigation/NavigationCatalog.cs:8-19`
   (labels, uids, search concepts), `WinCare.App/Services/PageService.cs:29-37` (id→Type map,
   debug-only one-direction assert — a catalog entry missing from Pages or a wrong XAML Tag ships
   silently in Release), and 11 hand-written `Tag=` items in `Views/ShellPage.xaml`.
5. [medium] Global search is inline logic in `MainWindow.xaml.cs` (293 lines, 30 search-related
   references) — not a component, untestable at the presentation boundary.
6. [low] `CheckupPageViewModel.cs:35-117` `HealthScoreText/Detail/BrushKey` produce qualitative
   states ("Not checked", "Checking", "Stopped", "Incomplete"). Behavior matches DESIGN.md ("checked-area
   status, not a health score"); the identifiers lie. Rename, behavior-neutral.
7. [low] i18n risk: `NavigationCatalog` carries English labels and search concepts; localization
   status of search-result rendering is unverified — audit in wave 4, fix if broken.
8. [info] Only 2 shared controls exist (`CareToolList`, `WidgetContainer`); card shells, section
   headers, and pills are re-composed inline per page.
9. [info] `src/WinCare.App/AppPackages/WinCare.App_2.5.0.0_x64_Test/` on disk is gitignored
   build output; `.agents/`, `.ai-codex/`, `.statamcp/` are gitignored tooling scaffolding
   (`.agents` holds a self-duplicated 2 962-file impeccable copy). None are commits; disk purge is
   cleanup, not history.

## 3. Target visual world (Operate mode, canon: Windows 11 first-party)

Strategy: **Restrained** — neutrals owned by Windows, one accent owned by the user's system
accent. Light/dark/HC handled by the platform, not by us. This deletes rather than adds
palette code.

### 3.1 Theme delegation (fixes findings 1–2)

Rewrite `Styles/ThemeResources.xaml` so the same 28 semantic keys remain the public contract
(`ThemeResourceBrushConverter.cs:52-80`, VM brush-key strings, and `verify_visual_tokens.py`
all depend on the names), but values delegate to WinUI theme resources:

- Text: `TextFillColorPrimary/Secondary/Disabled` · Surfaces: `SolidBackgroundFillColorBase/Secondary`,
  `LayerFillColorDefault`, `CardBackgroundFillColorDefault` · Borders: `ControlStrokeColorDefault`,
  `CardStrokeColorDefault` · Accent: `AccentFillColorDefault` (resolves to system accent) and
  `TextOnAccentFillColorPrimary` · Status: `SystemFillColorSuccess/Caution/Critical/Neutral` —
  these four carry real per-theme and HC values from Microsoft, which is what fixes the HC collapse.
- Mechanical risk (honest): whether a standalone `SolidColorBrush Color="{ThemeResource X}"`
  re-resolves on live theme change under the converter's cached-brush design. Mitigation: new
  `ThemeResourceBrushConverterRefreshTests` pin color-after-switch via the existing
  `RefreshBrushes()` path; fallback design is per-theme dictionaries whose entries delegate to
  the same system color resources. Either shape passes the same validator.
- Compile-time truth: any wrong resource key fails XAML bind on build — the build is the check.
- `verify_visual_tokens.py` and `verify_pill_contrast.py` keep their ≥ AA thresholds unchanged;
  expected-value tables move with the delegation (measured, not lowered). Pill risk meaning
  must never rely on color alone: audit each pill for glyph+text before wave sign-off.

### 3.2 Tokens and type (finding 3)

- `ControlStyles.xaml` gains `Spacing` (4/8/12/16/24/32) and keeps the 12/8/4 radius scale,
  matching DESIGN.md's stated rhythm — tokens make the stated system enforceable.
- Custom text styles become `BasedOn` native `TitleTextBlockStyle/SubtitleTextBlockStyle/
  BodyTextBlockStyle/CaptionTextBlockStyle` aliases, preserving current sizes (34/20/15/14/13)
  where DESIGN.md fixed them and inheriting Windows type behavior otherwise.
- Views migrate inline literals to tokens/styles, one commit per page family
  (Home → Checkup → Care×3 → Power tools → Activity → Extensions/Troubleshoot → Settings/Help/About).
  Net effect: literals count trends toward 0 without layout redesign; spacing/scale follow
  §3.1-3.2, composition stays task-first per DESIGN.md:Composition.

### 3.3 Component layer (findings 5, 8)

- New `Controls/GlobalSearchBox.xaml(.cs)` extracted from `MainWindow.xaml.cs` — same behavior,
  same automation ids, query-submit routing to Power tools fallback preserved; code-behind only
  for view concerns, search provider calls moved behind the existing service seam it already uses.
- Shared styles promoted (not new controls): `SectionHeaderTextStyle` exists; add
  `PageHeaderControl` (title+description+actions) and use existing `SurfaceBorderStyle`
  everywhere per-page card shells re-derive `Border`. `WidgetContainer`/`CareToolList` stay.

### 3.4 Shell and navigation (findings 4, 7)

- Keep XAML-declared `NavigationViewItem`s (x:Uid localization would break under code generation —
  a real constraint, verified by `PageService.cs:29-37`'s author comment acknowledging the sync
  burden). Instead of generating, **gate** the contract: extend `tools/verify_native_foundation.py`
  (the repo's established pattern for structural source contracts) to enforce three-way agreement:
  every catalog id ⇔ a PageService key ⇔ a ShellPage Tag (both directions, plus `{about}` hidden
  exception). Convert the runtime `Debug.Assert` to also check catalog→PageService.
- Audit whether search UI surfaces `NavigationCatalog` English labels; if yes, route labels
  through `ResourceLoader`/resw so localization covers nav text.

### 3.5 Code cleanliness (finding 6, bounded)

- `HealthScore*` → `CheckupStatus*` rename across VM, XAML bindings, and consumers —
  characterization test added first (behavior pinned), rename itself behavior-neutral, public
  XAML bindings are internal to the app (no external consumer; verified 3 files touch it).
- Big VMs (`ToolExecutionViewModel` 32KB, `PluginStore/Checkup/AllToolsPage` ~20KB): **no split
  scheduled**. Independent review could not run (see §1); splitting without evidence violates
  the repo's own "implement minimally" rule. Wave 4 re-dispatches this review if the environment
  recovers, else it becomes a named residual risk.

## 4. Clean re-release

- **Version 3.0.0**, not a 1.x restart: MSIX in-place update requires the manifest version to be
  monotonic, and signed 2.5.0-rc builds exist in releases. 3.0.0 is the honest clean-slate major.
  Bump set (all tracked): `Directory.Build.props` (5 fields), `Package.appxmanifest`,
  `native/*/Cargo.toml` + `Cargo.lock` + `lib.rs` + `ffi_contract.rs`, `CHANGELOG.md`, README/
  docs references, and the 28 `CommandCatalog/Plugins/*.json` manifest versions (no host-version
  coupling found — metadata-safe). CI derives tags from `VersionPrefix` (per commit 0b3c1cf
  "publish validated releases from master" — verify exact derivation in `.github/workflows/native-winui.yml`
  before tag).
- **CHANGELOG.md rewritten** as a fresh 3.0.0 history; prior history remains in git, not in the file.
- **History purge (tracked, with reference updates in the same commits):**
  delete `design/redesign-2026-09/` concept studies (DESIGN.md itself labels them
  "concept art, not runtime evidence"); delete superseded process docs
  `docs/PRODUCT-UX-FINALIZATION.md`, `docs/Refinement-Verification-2026-09-18.md`,
  `docs/Release-Readiness.md`; consolidate `VALIDATION.md` + `FINAL-VALIDATION.md` into
  `docs/Validation.md` and update `finalize_native_release.py` file lists +
  `test_finalization.py` + PRODUCT.md/README/docs-README pointers together.
- **Keep:** `migration/oracle/` (referenced by 4 py suites + finalizer + `CommandDefinition.cs` —
  release-gate infrastructure, not history), `docs/images/runtime-*.png` (execution evidence),
  brand assets in `design/` (logo/wordmark/ico are the surviving brand surface), architecture and
  user docs.
- Untracked disk cruft (`AppPackages/`, nested `.agents` copy): report, delete on request, not commits.

## 5. Execution waves (each ends green on its gates)

0. Spec approved; record canon decision + craft bar in PRODUCT.md/DESIGN.md handoff note.
1. Gates-first: nav three-way contract in `verify_native_foundation.py` (expect: passes as-is or
   exposes drift — either is signal, not risk).
2. Theme delegation + HC fix + token scale + validators updated. Gate: build, both py validators,
   Application.Tests.
3. Literal migration per page family + shared styles. Gate: build, py validators; each page its
   own commit.
4. GlobalSearchBox extraction + search-label i18n audit/fix. Gate: build, Application/Infra tests.
5. HealthScore rename (characterization test first). Gate: full `dotnet test`.
6. Re-release: version set, changelog rewrite, history purge + reference updates. Gate: full
   baseline matrix of §1, plus finalizer staging dry-run through its existing tests.
7. Finish: full gate matrix + DESIGN.md rewritten from the built world (impeccable documenter
   role executed in-thread — subagents broken) + `OVERHAUL_REPORT.md`. Runtime look-and-feel,
   Narrator, HC, scaling: **you verify** per `docs/Windows-Validation.md`; I cannot launch and
   see the WinUI window from here and will not claim otherwise.

## 6. Explicit non-changes

Safety model, dispatcher, catalog IDs/frozen fixtures, IPC/ABI, plugin trust/signing, CLI,
Rust code, CI release pipeline (except version derivation sanity-check), quality-gate thresholds.

## 8. Deep-review appendix (engineering-protocol pass, 2026-09-19)

Targeted scans beyond the UI layer, all resolved against source:

- Rust non-test `unwrap/expect`: `cleaner.rs:650+` are inside its `mod tests` (no `cfg(test)`
  anchor needed — `use super::*` proves it); `lib.rs:2383/2385` are fixed-length slice
  conversions (`[0..8]`/`[8..16]` of a 16-byte array → infallible); `lib.rs:2434+` all after the
  `cfg(test)` at 2412; `thermal.rs:134` after its 116 anchor; `toast.rs:60` is a documented
  infallible `write!` to `String`. No production panic sites found. No panics in non-test src.
- FFI boundary: `catch_unwind` present at extern-"C" entry points (≥5 sites, 62 boundary fn
  total) — panics cannot unwind into C#.
- [residual, documented] `wincare-core/src/lib.rs`: 168 `unsafe {` blocks vs 61 `// SAFETY:`
  comments. Auditing 107 undocumented blocks is a separate, deliberate effort; bulk comment
  insertion without per-block understanding would be slop. Deferred with owner gate: any wave
  that edits an unsafe region must document its invariants.
- `verify_pill_contrast.py` reads literal hex only — it structurally cannot see
  `{ThemeResource}`-delegated values. Consequence for wave 2: pill background/text brushes stay
  literal hex (re-picked as an AA-safe family, measured by the same 4.5:1 gate, threshold
  unchanged); non-pill brushes delegate. HC dictionaries: pills become text/glyph-carried state
  with system text/border colors (color-only distinction is invalid in HC by design).
- Status pill templates have exactly 2 real consumers (`WidgetContainer.xaml:24`,
  `PluginStorePage.xaml:69`) — the "risk pill" family is smaller than the palette suggests;
  wave 2 must also enumerate each consumer's actual fg/bg pair and extend
  `PAIRS_SPEC` to cover them (gate strengthened, not weakened).
- Wave 1 landed: `verify_native_foundation.py` now enforces three-way nav agreement
  (catalog ⇄ PageService ⇄ ShellPage Tags, `about` hidden-exception) — verified live: passes on
  current source, fails with the correct finding on a seeded drift, restores clean. The runtime
  one-direction `Debug.Assert` in `PageService.cs` remains as defense-in-depth.
- Wave 3 triage (finding, not a skip): real XAML spacing values (6/10/14/18/20/28) do not sit on
  the documented 4/8/12/16/24 scale; forcing token substitution without live rendering would be
  a blind redesign. Card shells mostly already use `DashboardCardStyle`/`SurfaceBorderStyle`
  (recon overstated). PageHeader duplication (12 pages, one adjacency) is real — deferred to a
  post-render verified pass. The spacing token scale from wave 2 stays available.
- Wave 4 landed: global-search ranking extracted to `Application/Navigation/GlobalSearchService`
  (+11 characterization tests; view glue left in `MainWindow`; parity gate retargeted, contract
  unchanged). i18n audit finding: the app is English-only (single `Strings/en-US/Resources.resw`,
  22 keys) covering only nav/page-title chrome; all body copy, VM strings, and search results are
  unlocalized literals. Each chrome label exists in three copies (catalog / XAML / resw) with no
  prior guard — new native gate
  `test_chrome_labels_agree_across_catalog_xaml_and_resources` pins XAML⇄resw equality and
  catalog⇄resw label sets (falsification-tested). Expanding resw coverage is out of scope for the
  re-release; recorded as a post-3.0 decision point.
