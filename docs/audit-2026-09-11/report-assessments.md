## J. Systemic/root-cause findings

| Root cause | Concrete consequences | Structural response |
|---|---|---|
| Artifact acceptance stops at shell/backend smoke | Seven collection-route crashes and missing Doctor resource escape validation | Make final-artifact user journeys the release gate |
| Product promises are separate from executable contracts | Safe cleanup exception, mismatched preview roots, Connected without connectivity | One plan/result model drives policy, UI and tests |
| State is copied or inferred rather than owned | Stale Home evidence, divergent Checkup rows, plugin state/catalog disagreement | Explicit session and lifecycle state machines |
| Generic infrastructure substitutes for completed features | Raw JSON, category-to-empty search, schedules without scheduler | Commit to a small set of complete supported workflows |
| Durability boundaries are inconsistent | Open-stream exports, store race, silent plugin save failure, journal exit window | Common transaction/failure/shutdown contracts |
| Build/release configuration is duplicated | Local/CI trim mismatch, strict trim failure, tag/asset mismatch | One canonical reproducible artifact pipeline |

The architecture has useful layers: domain contracts, application dispatcher, infrastructure adapters, native ABI and declarative UI. The main defect is not that these layers exist. They do not yet enforce common truth about readiness, evidence, mutation certainty and lifecycle. Splitting a large partial executor into more files alone would not fix that.

## K. Visual/UI assessment

| Supplied-image region | Observation | Assessment and correction |
|---|---|---|
| Title bar/search | Large universal-looking search promise | Actual scope is tools; correct scope and discoverability, F-025 |
| Left navigation | Nine primary destinations plus three footer destinations, all equally credible | Most installed destinations crash; reduce supported surface and distinguish unavailable/experimental areas |
| Home heading/subtitle | Generic evidence copy above a passive hero | Give the page a clear next step and a visible primary Check action |
| Check-evidence hero | Large0/4, circular decoration, no action | Decorative weight exceeds useful information; explain what the four checks cover |
| Evidence-by-area grid | Six tiles beside a four-check score, all unchecked | Explain coverage and freshness; Activity is history, not another system health measurement |
| Quick-action headers | Technical IDs, repeated Safe/1-click badges, shared-cell title/badge layout | Remove internal IDs from default view, correct risk/prerequisites, reserve actual layout space |
| Quick-action bodies | Large empty regions, repeated Status/Ready | Replace empty furniture with compact explanation and outcome details |
| Main buttons | Startup text differs in contrast from adjacent controls | Reproduce control states; token tests alone are inadequate |
| Inspector icons | Same accessible names and same generic inspector | Use specific accessible labels or one clearly labeled diagnostics control |
| Recent activity | Honest empty-state text but too far from main workflow | Keep relevant outcomes beside the action and make rejection/history persistence reliable |
| Safety panel | Implementation jargon plus an inaccurate universal promise | Explain actual scope/review behavior in ordinary language |
| Bottom actions | Run Checkup is below the fold in Image3 | Move the central action to the hero; leave secondary links subordinate |
| Scrolled images1/2 | Header naturally above viewport | Not evidence of a clipping bug; they are duplicate views |

Typography, consistent icons and light surfaces provide some continuity. The issue is hierarchy, semantic accuracy and state visibility more than absence of styling. Thick borders and oversized cards amplify the sense of bulk. The design should be rebuilt around a working journey rather than restyled with another set of generic dashboard cards.

Runtime visual-state coverage remains limited. Light Home screenshots and native accessibility trees are available; source contains Light/Dark/High Contrast tokens and responsive rules. Dark/high-contrast rendering, hover/pressed/disabled/focus, text scaling, narrow widths, multiple monitors and localization were not all rendered. F-029 is explicitly a source-layout risk. No screenshot-based claim of full accessibility compliance is made.

## L. UX/product assessment

The first useful journey should be: open → run read-only check → understand findings and uncertainty → choose one change → inspect affected targets and prerequisites → approve → see completed/partial/failed outcome. The installed build breaks at navigation. The fresh build still burdens the user with generic tools, raw JSON, static category pages and incomplete evidence. This explains why a technically executing command can feel like a nonfunctional button.

Errors vary in quality. All-tools result areas and parameter validation can supply details; the Home elevation failure gives a restart instruction but no integrated recovery. Fatal navigation has no recoverable page outcome. “Failed safely,” “Connected,” “Healthy” and “Clean Complete” should be reserved for evidence that supports those statements. Secondary diagnostic details should not replace the task result.

## M. Functionality matrix

The [259-command matrix](D:/GitHub/WinCare/docs/audit-2026-09-11/command-matrix.csv) provides per-ID metadata, implementation location and runtime status. This product-level matrix accounts for the main modules without pretending a successful helper test proves every command.

| Feature / module | Classification | Established behavior |
|---|---|---|
| Home shell/navigation | Partially working | Opens; most destination pages fail in installed artifact |
| Startup inspection from Home | Partially working | Query succeeds, visible count, no useful entry/impact view |
| Network inspection from Home | Partially working | Query succeeds, unsupported Connected interpretation |
| Quick Clean | Broken product contract; host mutation unverified | Privilege gate/review/targets/partial results inconsistent |
| Checkup | Broken installed; partial fresh-source page | Navigation failure plus lifecycle/result-model defects |
| System care/Security/Repair | Broken installed; static directory implementation | Navigation crashes, fixed rows, wrong shortcut searches |
| All tools | Broken installed; partial fresh-source page | Search/selection works locally, typed editor absent, generic raw JSON |
| Activity/reports | Broken installed; backend journal partial | Crash plus rejection/retention/shutdown gaps |
| AI Doctor | Broken | Missing converter; independently fabricated/stale evidence paths |
| Settings | Navigation working; detailed persistence combinations unverified | Theme/window preference model exists |
| Help/About | Navigation working; content partly inaccurate | Safety/readiness claims require correction |
| System/storage/startup/network/security/health/application queries | Working in isolated representative read-only probes | Actual Windows data returned; semantic accuracy varies |
| Catalog/presets queries | Working in isolated probes | Embedded data returned; not proof all presets apply safely |
| Memory queries | Working in isolated probes | Data returned; Doctor uses a different problematic collector |
| JSON exports | Broken shared implementation | Real helper fails on Windows open-file rename |
| Administrative remediation/recovery | Source-reviewed; runtime unverified | Receipt/elevation mechanisms exist; partial-effect reporting flawed |
| Desktop/window/monitor controls | Source-reviewed; runtime unverified | Actual API adapters exist; hardware/failure matrix absent |
| Downloads/media/productivity/customization | Representative source review; runtime unverified | Includes real operations, presence probes and saved state; not one uniform capability |
| Notes/telemetry/maintenance/workspace saved records | Partial infrastructure, workflows unverified | Shared state exists, multi-instance race and export defect affect reliability |
| Cleaner scheduling | Disconnected | Saved records without a due-run consumer |
| Remote-consent records | Local metadata, not a demonstrated remote support service | No remote session was established or claimed |
| Local plugin management | Partially working; full external lifecycle unverified | Admission/registration machinery exists; persistence/widget-state defects |
| Remote Plugin store | Intentionally unavailable for installation without configured trust root | Browsing/offline illustrative metadata must not be mistaken for installability |
| Optional Guard | Experimental/disconnected from app notification delivery | Pipe/monitor code exists; no app queue consumer |
| Native hash/size/system ABI | Tested foundations; limits remain | Rust suite passes; header/queue/validity issues recorded |

## N. Interactive-control audit

| Control group | Actual wiring/result | Audit outcome |
|---|---|---|
| Sidebar destinations | Shell page navigation | All destinations accounted for;8 crashes installed |
| Home evidence tiles | Category/page navigation | Real handlers, but routes enter crashing/static pages |
| Clean Now | Direct execute through VM/dispatcher | Misleading review/elevation contract; not safely claimed dead |
| Analyze Startup / Check Network | Read-only dispatcher calls | Runtime complete; incomplete/misleading visible results |
| Three inspector icons | Shared toggle command | Wired; duplicate names/context, telemetry validity gaps |
| Run Checkup / View Activity / Browse All Tools | Page navigation | Wired but offscreen primary action and failing installed destinations |
| Global search | Submit → All tools query | Wrong advertised scopes; empty query retention |
| Category action buttons | Prewritten query → catalog | Five queries resolve to no commands |
| All-tools search/select | Catalog filter and selected-tool details | Native local runtime works |
| Typed parameter controls | Constructor visual-tree replacement | Not mounted in observed local runtime |
| Raw JSON/execute/review | Execution VM/dispatcher | Source path exists; exact mutation semantics require correction; host changes not exercised |
| Favorite/recent/filter/tab controls | VM/preferences/catalog | Source traced and statically inventoried; complete keyboard/restart runtime matrix unverified |
| Doctor analyze/plan buttons | Compiled bindings + translator/dispatcher | Page crashes before use; result tooltips/raw detail are insufficient workflow presentation |
| Plugin install/enable/disable | Store VM → trust/installer/registry | Trust limitations intentional; persistence/lifecycle failure cases remain |
| Settings controls | Preferences model | Navigation verified; every setting/restart combination not tested |

The static inventory contains89 declarative interactive controls. Some runtime controls are generated by code or WinUI templates, so this count is not a claim of89 total app controls. No genuinely unbound button is inferred merely from appearance. Dead-feeling controls are traced to page crashes, empty search results, unavailable prerequisites or insufficient outcomes.

## O. Backend reliability assessment

The backend is in-process C# plus Rust. Eleven representative commands succeeded without a crash: system, storage, startup, network, security, health, catalog, presets, applications, internals-memory and memory-anomalies. The measurements are warm fixture latencies, not performance guarantees. Root startup constructs the journal, native services, executor, dispatcher and plugin services eagerly; ABI mismatch is fail-fast.

Positive mechanisms include explicit command definitions, parameter validation, bounded subprocess output/time, cancellation tokens, native status codes and plugin admission gates. Reliability weaknesses are partial-mutation certainty, unbounded/non-cooperative waits at some boundaries, missing shutdown settlement and file-operation lifetime errors. The app has no separate crash-isolated backend supervisor: a fatal WinUI or native-process failure terminates the same application.

## P. Frontend/runtime-state assessment

XAML construction is an essential runtime boundary and is under-tested. The missing Doctor resource is a direct example. Cached pages retain view models, which is reasonable for navigation but makes stale query/evidence ownership significant. Checkup copies results before final assessment; Home reconstructs check state from unrelated journal entries; plugin enumeration can change state without catalog reconciliation.

Adopt explicit states such as NotChecked, Running, Partial, Failed, Unavailable and Completed for a named run. Include measurement freshness and separate execution success from diagnostic meaning. Raw parameter JSON should be an advanced view of the same typed model, not an independent authority. View-model tests need real-page integration companions.

## Q. Architecture assessment

The large WindowsCommandExecutor spans many independent concerns. Its shared helpers materially amplify defects: one export lifetime error breaks multiple modules, one risk heuristic changes many commands, and one failure mapper loses partial-effect certainty. These are stronger reasons to improve boundaries than file size or an “AI-generated” appearance.

Recommended boundaries are diagnostic evidence/assessment, reviewed mutation planning/execution, durable app state, plugin lifecycle, and optional specialist tooling. Keep a common command envelope, but use typed result/plan contracts for the supported user journeys. Give each boundary an owner and regression suite. Avoid a wholesale rewrite justified only by frustration: existing probe/admission/native/test foundations are reusable.

## R. Security assessment

Threat model: a local maintenance app can run elevated, consume user-selected files/paths, load admitted plugins and scripts, read remote catalog/package metadata, and alter Windows state. Its important assets are user files/settings, truthful review scope, plugin trust state and audit history. There is no general login/session service in the desktop runtime to audit as web authentication.

Confirmed concerns are the mismatch between mutation review promises and actual admission (F-004/F-005), loss of partial-effect certainty (F-010), inconsistent plugin lifecycle/persistence (F-012/F-034) and catalog body bounds (F-033). These findings are defensive source/fixture analysis; no third-party target was attacked and no exploit payload is included.

Positive controls inspected: parameter validation; process argument lists instead of shell concatenation in the bounded runner; package size/entry/uncompressed limits; path containment checks; pinned/admitted signature metadata; install freshness failing closed; installer cross-process operation locking and staged rollback; local Guard ACL excluding Everyone/Anonymous and rejection of remote clients; bounded one-line IPC protocol. Those controls do not make a loaded assembly plugin a sandbox: admitted plugin code runs with host trust and privilege.

No hard-coded production secret was established in the inspected runtime/release paths. This is not a historical secret scan or certification of user configuration. Release signing uses development certificates in the examined workflow; production signing identity, key custody and revocation operations still require dedicated validation. No credential values were printed or modified.

## S. Concurrency/lifecycle assessment

Confirmed race: two store instances do not share a transaction lock (F-011). Confirmed ownership gaps: WUA can outlive a Checkup run (F-014), journal/services lack a common close boundary (F-013), widget failures bypass normal lifecycle reconciliation (F-034). Plugin initialization/shutdown callbacks can hold the registry gate and require cooperative completion; a stuck trusted plugin is an unresolved availability risk.

The process runner has bounded output, cancellation and process-tree termination. Guard’s pipe server uses bounded reads and stop polling rather than an unlimited request buffer. These positives rule out blanket claims of unlimited loops everywhere. FFI hash/size calls check cancellation around synchronous native work; that does not interrupt work already inside the native call. Guard’s30-second loop is optional and is not the cause of ordinary app idle activity.

## T. Persistence/data-integrity assessment

Temporary-file replacement is used in several stores and the plugin installer has staged rollback logic. However, atomic rename is not equivalent to an atomic cross-process transaction or durable power-loss recovery. The shared export helper violates the required stream lifetime (F-008); state transactions are per-instance (F-011); plugin save errors disappear (F-012); journal close has an unflushed window (F-013).

Multi-step Windows remediation cannot be treated as an all-or-nothing JSON update. Recovery must record what actually happened before failure, cancellation or restart. A blanket failed safely message is not a rollback guarantee. No forced power-loss, disk-full, administrative rollback or state-migration matrix was run; those remain explicit release work.

## U. API/compatibility assessment

The catalog and route inventory agree on available command IDs, but a route is not behavioral parity. A concrete contract discrepancy remains in `SystemOverviewAsync`: the native branch returns snake_case fields such as logical_cpus/os_build, while the fallback returns processorCount/osBuild and a different field set. Callers must not assume a single stable shape merely because the command ID is the same. The production composition injects native services, so fallback-client breakage was not reproduced; normalize this in the contract backlog.

Command parameters need typed schemas shared with UI controls; result JSON needs stable field/version/availability semantics. The public native header is incomplete (F-039). ABI version checks are useful but do not replace structure size/offset and error-contract tests. Existing Rust release settings use panic=abort while exports contain catch_unwind; a release panic would terminate rather than be converted to an error. No reachable production panic was reproduced, so this is a native failure-containment risk, not the asserted cause of current crashes.

## V. Performance/resource-usage assessment

| Dimension | Measured / inspected result | Conclusion |
|---|---|---|
| Startup process CPU |1.0625 CPU seconds by first sample at5.02 wall seconds | Startup work exists; not a cold-start benchmark |
| Idle CPU |1.09375 cumulative CPU seconds at60.23s; only0.03125s added after first sample | No sustained idle CPU load reproduced in this run |
| Working set |About144.9–145.5MiB during completed sample | Modest stable short sample; no universal budget judgment |
| Private memory |About65MiB | No short-run growth pattern established |
| Threads |29 initially →23 at end | No accumulating thread pattern observed |
| Handles |996 initially →991 at end | No short-run handle growth observed |
| Processes |One app PID being sampled; no normal-app Guard startup | No separate backend daemon implicated |
| TCP |Earlier interrupted sample recorded0 owned TCP connections | Only one snapshot; no packet capture or whole-run network claim |
| I/O |Interrupted raw process snapshot read165,919 / write40 bytes | Not cold extraction or whole-system disk attribution |
| GPU/rendering |Not measured | No GPU root cause asserted |
| Active/background work |WUA overlap source path; eager initialization; noninterruptible FFI; remote body buffering; native pending queue | Real profiling targets, distinguished from idle evidence |
| Leaks |No multi-hour profiling or forced plugin lifecycle run | Long-run leaks remain unverified |

The failed first extended probe is disclosed in E. The final completed sample used simple per-process counters to reduce observer overhead. Hidden-window launches, warm caches, antivirus activity and existing machine load limit interpretation. The user’s “system works hard” report is plausible but not reproduced as sustained WinCare idle CPU. Cold extraction, Windows Defender scanning, GPU composition, active Checkup and whole-system ETW need separate attribution. Binary size alone does not prove bloat; the source build and installed build are also different sizes/configurations.

## W. Platform-specific assessment

The actual runtime targets Windows10 build19041+ with x64/ARM64 project configurations. This audit ran on one Windows x64 host. The current workflow includes an ARM64 runner smoke job; claiming ARM runtime is completely absent from CI would be false. That smoke still lacks the failing page navigation.

MSIX installation, uninstallation, upgrade, certificate trust, clean-user profile, enterprise policy, non-C Windows volume, alternative antivirus, high DPI, multi-monitor placement, accessibility tools and ARM64 hardware behavior were not all exercised. Browser/Linux/macOS UI testing is not applicable to this Windows product. Cross-platform Rust tooling tests do not establish Windows UI behavior.

## X. CI/CD and release assessment

The pipeline has meaningful controls: pinned action SHAs, lockfiles, native builds/tests, managed tests, package/signature checks, artifact staging, and x64/ARM64 portable smoke. Yet it misses basic page construction and allows readiness/configuration drift (F-003/F-035–038). The production gate recognizes that Implemented is weaker than BehaviorVerified, but the ordinary stable publication path does not enforce that distinction.

A clean, reproducible release requires: one immutable source SHA; freshly built native DLL per architecture; one canonical effective publish profile; strict trim analysis; artifact hashes and signing identity; full page navigation plus representative workflows; immutable tag/assets; installation/upgrade checks. The current local untrimmed build passing is insufficient to certify any existing release artifact. The strict trimmed build failure is retained as evidence rather than suppressed to make the audit appear green.

## Y. Test-quality assessment

| Validation | Result | Scope / limitation |
|---|---|---|
| Managed suites, same source, initial audit |73 Application +89 Infrastructure +18 Catalog =180 passed | Not re-counted as new runtime UI coverage; VM/test doubles omit compiled page resource loading |
| Native foundation verifier |Pass | Structural/source expectations |
| Python native/tooling tests |94 ran,1 skipped | Includes structural checks; not94 actual product journeys |
| Plugin CLI tests |9 ran,1 skipped | CLI packaging/validation fixtures |
| Rust tests |53 passed across groups | Native/Guard unit and integration coverage; release panic behavior not injected |
| Rust formatting and strict clippy |Pass | Style/static diagnostics |
| Theme token verification |33 entries:11 tokens ×3 themes pass | Token presence/values, not rendered-state usability |
| Contrast script |8 pairs pass | Does not cover screenshot’s actual Startup button state |
| NuGet advisory query |No findings in returned project results | Current feed query, not a full supply-chain guarantee |
| Fresh untrimmed publish |Pass | Existing staged native DLL; not full clean-machine certification |
| Strict restored trimmed publish |Fail,3 IL2026 diagnostics | Material negative evidence |
| New harmless audit fixtures |Export/state/plugin failures reproduced | Diagnostic harnesses, not production fixes |

Strong tests cover parts of admission, compatibility, isolation and native limits. False confidence arises when test names, source-string assertions, catalog counts or a process-start smoke are treated as evidence of end-to-end feature completion. No coverage percentage was fabricated. No browser axe/lighthouse score is claimed for a native WinUI application.

## Z. Dependency/supply-chain assessment

Central NuGet versions and architecture-specific lockfiles reduce accidental drift. Rust has pinned toolchain/dependency state; the plugin CLI uses standard Node modules rather than a large third-party dependency tree. WindowsAppSDK/runtime self-containment has a real distribution footprint but cannot be declared unnecessary just by size.

The app project manually prunes several heavyweight Windows projections from the portable bundle. That strategy needs artifact-level compatibility tests as SDK packages evolve; no pruning-induced current crash was proven. Existing staged native binaries must be tied to source/toolchain hashes. The advisory query found no reported vulnerable NuGet dependencies, but Rust advisory scanning, full historical secret scanning, provenance attestation and external plugin ecosystem trust were not comprehensively audited.

## AA. Dead-code/duplication/incomplete-feature assessment

Confirmed disconnected/incomplete paths: cleaner schedule records without a scheduler; Guard XML without an app consumer; typed editor code not reached at the correct lifecycle stage; remote store installation intentionally disabled without a production catalog trust root. Remote consent and maintenance records are local state machinery, not demonstrated remote transport or automatic orchestration.

Concrete duplication: diagnostic thresholds/evidence collectors; native versus managed system JSON shapes; separate native cleaner and managed cleanup semantics; independent persistence/error conventions; duplicated publishing flags/profile. The native cleaner has no established Home call path and should not be confused with the managed Home cleanup. Migration oracles, tests, documentation and local development skills are not automatically dead production weight. Removing them solely to reduce repository size would be unjustified.

## AB. Documentation drift

Confirmed mismatches: universal preview/approval copy versus risk admission; “current evidence” versus stale journal synthesis; safety labels versus prerequisites; Guard queue-delivery comment versus missing consumer; public header versus exports; portable profile versus CI publish flags; documented production behavior gate versus stable publication path. Catalog Implemented labels and generic Ready UI must not be interpreted as behavior verification.

The initial review’s trimming hypothesis remains a hypothesis for the installed collection crash. Its narrower Checkup-only scope is expanded here to all installed routes. The previously documented experimental Guard and fail-closed remote-store limitations remain intentional limits, not invented vulnerabilities. Current ARM64 smoke support is acknowledged.

## AC. Prioritized remediation roadmap

| Priority | Work | Depends on / acceptance |
|---|---|---|
| P0 — release blockers |Fix installed route crashes and missing Doctor resource; stop stable promotion without artifact journeys |Exact reproducible build/provenance; every route opens in shipped portable/MSIX |
| P0 — unsafe/misleading operation boundary |Unify mutation review/elevation/targets and partial-effect certainty; remove fabricated evidence |One authoritative plan/result/evidence contract; disposable-machine verification |
| P1 — core functionality |Mount typed inputs correctly, repair export helper, category routing, useful Startup/Network results |Core page stability; regression tests F-007/F-008/F-009/F-016 |
| P1 — reliability |Cross-process state ownership, persistence warnings, journal flush, cancellation/run ownership |Fault-injected persistence and noncooperative provider tests |
| P1 — release integrity |Canonical publish profile, strict trim fixes, mandatory production gate, immutable tag/assets |Clean restore/build plus x64/ARM64 artifact manifest and journeys |
| P2 — product/state/UX |One check-session model; truthful health/freshness; primary check in hero; task-based categories |Working core flows; agree on supported product scope |
| P2 — performance/accessibility |Cold/warm/active profiling; bounded native/catalog work; rendered contrast/UIA/screen-reader matrix |Stable reproducible artifact; explicit budgets |
| P3 — controlled scope/debt |Isolate optional tooling; complete or remove schedule/Guard promises; align ABI/docs |Supported features have owners and tests; keep useful oracles |

These priorities are repair sequencing, not a redefinition of finding severity. No catastrophic compromise was established. Cosmetic restyling before fixing navigation, execution and evidence would leave the principal failure intact.

## AD. Recommended regression-test backlog

1. Final-artifact page construction/navigation on x64/ARM64, portable/MSIX; fail on crash logs or unexpected exit.
2. Home→Checkup→findings→review→apply→result→Activity on disposable Windows fixtures, including unavailable/partial/failed states.
3. Every declarative resource and converter used by compiled bindings resolves; Doctor analyzing state toggles correctly.
4. Typed parameter rendering and raw JSON round trips; invalid conversion stays visible; displayed values equal submitted values.
5. Every category shortcut resolves to the intended commands; explicit empty search resets cached state.
6. Every mutation tier/elevation/receipt combination; preview/execution target identity; failed second step preserves partial effects.
7. Windows exports: new/replaced target, cancellation, denied path, no temp leaks; all shared callers exercised.
8. Check-session coherence/freshness and identical diagnostic thresholds across all entry points; no fabricated measurements.
9. WUA slow/noncooperative completion, repeated run, navigation away and shutdown with no accumulating work.
10. Two-process state updates, failed persistence warning, immediate-close/restart history, corrupt/disk-full fixtures.
11. Plugin widget/initialize/shutdown failures with consistent registry/catalog/dispatcher state and bounded shutdown.
12. Bounded catalog/signature input and native enumeration queue; no stress against external targets.
13. UIA row names, distinct inspector names, keyboard focus, Narrator, high contrast, text scaling and breakpoint screenshots.
14. Immutable release rerun, stable/RC readiness selection, canonical effective publish properties, strict trim and serialization in artifact.
15. Cold/warm startup,60-second idle, active check, repeated workflows and multi-hour resource baseline with explicit budgets.

Every retained finding also specifies a focused regression test. These are proposed tests; they were not all implemented in this review.

## AE. Unresolved/unverified risks

Exact installed collection-crash cause and installed-source provenance; all259 handlers on real supported environments; actual cleanup/registry/service/recovery mutations; partial rollback under host failure; clean MSIX install/upgrade/uninstall and production signing; full ARM64 runtime; high DPI/dark/high-contrast/text scaling/screen readers; cold startup/whole-system GPU/disk/antivirus attribution; multi-hour leaks; stuck admitted plugin callbacks; FFI in-flight cancellation/panic containment; historical secrets and full external dependency/plugin trust; disk-full/power-loss recovery and migration.

The audit did not silently classify these as healthy. Source-layout findings and prospective failure paths are labeled accordingly. Evidence files preserve failed/incomplete experiments as well as successful ones. The first two publish variants were not independent, and an interrupted idle sample was not represented as a completed measurement.

## AF. Final verdict

**Not release-ready.** Navigation crashes alone block ordinary use. The fresh-source Doctor failure, broken export helper, missing typed-input UI, false diagnostic evidence and inconsistent mutation review make this a functional/reliability problem requiring coordinated repair, not a cosmetic cleanup. At the same time, successful read-only commands, useful infrastructure controls and existing tests show that wholesale reconstruction of every module is not justified by the evidence. Release confidence requires working, truthful, durable user journeys in the exact artifacts that will be distributed.

The [coverage ledger](D:/GitHub/WinCare/docs/audit-2026-09-11/coverage-ledger.md), [findings data](D:/GitHub/WinCare/docs/audit-2026-09-11/findings.json), command/control inventories and retained evidence are the handoff for that work.
