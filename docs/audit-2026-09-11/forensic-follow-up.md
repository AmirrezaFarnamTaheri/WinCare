# Forensic follow-up to the audit critique

Source rechecked: `b30516a5bb6fdd13e844a6022906dd03090b576e`. This follow-up validates the critique and repairs documentation. It does **not** claim that the omitted runtime experiments have now been completed. Production source remains unchanged.

## Overall assessment

The original review found substantial defects, but its runtime coverage fell well short of the user's exhaustive request. A coverage disclaimer is necessary; it does not substitute for doing the omitted work. The strongest criticisms are the small read-only execution sample, absent real mutation/rollback matrix, unresolved installed collection crash, invalid initial build comparison, and missing long-duration/platform/accessibility experiments. The initial final response should have described the audit as broad but incomplete more plainly.

The critique also contains incorrect source facts and converts several untested hypotheses into established failures. Those should not enter the finding register without correction. There are **eight crashing destinations in two groups: seven collection-projection failures and one missing-converter failure**. There is no numerical contradiction, but the headline now makes the decomposition explicit.

## Claim-by-claim disposition

| Critique | Disposition | Source/evidence and consequence |
|---|---|---|
| Native cleaner completely uninspected | Overstated; insufficient documented depth is fair | Coverage ledger already included the cleaner. Its source is now summarized explicitly below. No native-cleaner benchmark or full permission/failure matrix was performed. |
| Panic/abort effects overlooked | Already identified, not experimentally validated | Report U explicitly states release `panic=abort` defeats catch_unwind recovery. `native/Cargo.toml:22` confirms it. OOM/stack-overflow host termination was not injected; no such cause of existing crashes is established. |
| Guard pipe security ignored / LPE established | Incorrect as a source-review claim; runtime identity testing remains absent | Report R and ledger already describe ACL and remote rejection. Actual pipe is `\\.\pipe\WinCareGuardIPC`, not `\\.\pipe\wincare-guard`. Source grants SYSTEM/administrators/owner and rejects remote clients. No LPE or impersonation exploit was established. |
| Guard restart/hang/sleep recovery untested | Accepted | Client has per-exchange reset, 2-second connect and 5-second exchange deadline, but this is source behavior, not a sleep/wake/restart test. |
| ALC permanent retention untested | Accepted | Collectible context plus Unload is not proof of collection. Weak-reference collection and plugin lifetime tests were not run. Permanent leaks are not yet proven either. |
| Plugin runtime privilege isolation ignored | Partly accepted: scripts needed more explicit treatment | Report R already says admitted assembly code has host trust. Scripts also use ordinary child processes: capability declarations are advisory metadata, not an OS permission boundary. No AppContainer/restricted-token launch is implemented in the inspected runner path. |
| Cached pages prove accumulating leaks in PageService | Unsupported causal claim | PageService maps page types and calls Frame.Navigate; cache declarations live in XAML. Home unsubscribes from journal/registry on navigation away; Settings and Activity also detach handlers. Bounded intended caching is different from accumulating unreachable objects. Long-run retention remains untested. |
| Pruning target ignored | Incorrect wording; experimental gap accepted | Report Z explicitly identifies manual pruning. No controlled prune-on/off artifact experiment or complete dependency closure was performed. Listing filename patterns does not establish they remove the collection projection dependencies. |
| PageRow is internal | False | `src/WinCare.App/ViewModels/Pages/PageRow.cs:5` declares **public sealed class PageRow**. |
| Internal PageRow proves exact trimming root cause | Not established | The premise is false, and the app project already roots five assemblies when trimming is enabled. That does not prove all projection requirements are satisfied; nor does it establish a fix. Installed collection-crash causality remains unresolved. |
| 144 read-only commands unexecuted | Accepted | 155 read-only minus 11 executed = 144. They should be individually triaged for arguments, environment, load and data exposure, then exercised where appropriate. |
| All 144 completely harmless / zero risk | Unsupported | A read-only label is not a cost, privacy, timeout or prerequisite guarantee. Examples include file-preview, torrent-metadata and binary-intelligence, which require user-selected input; hardware/COM/network reads can block or be expensive. This does not excuse omitting the whole group. |
| 104 mutations uncategorized | Partly false | The command matrix already supplies per-command risk and administrator fields. Missing aggregate: 1 Critical, 17 High, 73 Moderate, 13 Low. Actual rollback/undo/elevation testing remains absent. These are catalog risks, not necessarily the effective dispatcher tiers. |
| Nine exports falsely stamped as individually failing | Incorrect description of the recorded status; clarity improvement accepted | Status explicitly says shared-helper failure reproduced and individual command not executed. Nine caller paths are traced below. New explicit evidence-class and individual-execution columns remove ambiguity. |
| Invalid no-restore trimming comparison | Accepted | Byte-identical outputs invalidate that comparison. Disclosure was correct; treating disclosure as closure would not be. |
| Strict IL2026 failure resolves collection crash | It does not | It proves a strict configuration build failure, not the installed crash's cause. A failing strict build need not end the investigation; separate diagnostic builds can retain warnings and vary one build factor without changing production policy. Those experiments remain outstanding. |
| Short warm hidden-window sampling insufficient | Accepted | The 60-second run supports only a short per-process idle observation. It does not answer cold extraction, antivirus attribution, active rendering/GPU or multi-hour retention. |
| ARM64/edition/domain/DPI/assistive-tech coverage absent | Accepted as local runtime gaps | Current CI scripts include ARM64 smoke, but no physical ARM64 test was performed by this audit. No universal Windows edition or DPI claim should be inferred. |
| Machine known to be Pro/Enterprise at 100% DPI | Not established by the retained evidence | Do not add environment facts inferred from appearance. Edition and effective DPI must be captured in a future run manifest. |
| Power loss, disk full, actual multi-instance mutation skipped | Accepted | The two-store fixture is meaningful but narrower than two running applications and fault-injected durability testing. No SQLite store was found in the inspected production source; persistence concerns should name the actual JSON stores. |
| Two competing current reports | Valid discoverability concern | The later report already said it superseded the first; the old report lacked a prominent reciprocal banner. Added one, preserving the old evidence rather than silently rewriting history. Both audit documents are untracked working-tree additions, not contents of commit b30516a. |
| build_report.py disconnected from outputs | Overstated | It generates report.md/findings.json and annotates command-matrix.csv. It does not regenerate all handwritten companions; this split should be explicit and checked. It now also generates command-coverage-summary.json. |

## Native cleaner: explicit source assessment

[cleaner.rs](D:/GitHub/WinCare/native/wincare-core/src/cleaner.rs) uses TEMP (or the platform temporary directory fallback), checks child entries for Windows reparse attributes and visits files at the root and one child-directory level. It is **not** the same implementation as Home's managed cleanup. There is a Windows child-junction sentinel test in the source; this is not a general proof of root policy, race resistance or permissions across all execution identities.

Important limitations: no age filter; no explicit work/deadline budget; read/enumeration/removal failures are skipped; `error_code` remains zero; dry-run counts eligible bytes without proving actual removability. The root directory policy and approved-target identity need independent review. These deserve dedicated correctness tests and explicit partial-result semantics. The audit did not benchmark the native cleaner or execute deletion against real user TEMP, and this follow-up does not do so.

Rust `panic=abort` is already a confirmed build setting. `catch_unwind` cannot convert an abort into a returned FFI status. This does not establish that directory enumeration currently causes a panic or that every native error kills the host. Resource limits and out-of-process containment decisions require their own evidence.

## Plugin and page lifetime: what is known

[PluginLoadContext](D:/GitHub/WinCare/src/WinCare.Application/Plugins/PluginLoadContext.cs) is collectible and shares host/WinUI dependencies with the default context. [Registry shutdown](D:/GitHub/WinCare/src/WinCare.Application/Plugins/PluginRegistryService.cs:271) removes its dictionary entry, awaits ShutdownAsync and DisposeAsync, then calls Unload. External roots or non-completing callbacks can defeat clean lifetime completion. No WeakReference/GC proof exists in the audit; no permanent leak should be claimed yet.

[PluginScriptCommandHandler](D:/GitHub/WinCare/src/WinCare.Infrastructure/Plugins/PluginScriptCommandHandler.cs) validates paths, applies preview/approval rules for declared mutations and uses a bounded runner. It does not implement operating-system capability confinement. Full trust must be stated clearly for both assemblies and scripts, particularly when the app is elevated. Signature/admission trust and runtime confinement are different controls.

[Home navigation](D:/GitHub/WinCare/src/WinCare.App/Views/Pages/HomePage.xaml.cs:25) subscribes on entry and unsubscribes on exit. A page referencing its own view model is not by itself a memory leak. The correct missing experiment is repeated navigation/plugin refresh followed by heap-root analysis, distinguishing intentionally cached pages from growing retained instances and unfinished work.

## Export inference: explicit caller trace

The original fixture calls **WriteJsonExportAsync**, not ExportStateAsync. It writes harmless JSON inside the audit sandbox and reproduces the open-stream rename failure. For the nine matrix rows, source control flow is:

| Command | Caller chain |
|---|---|
| widget-export | Executor route → ExportStateAsync → WriteJsonExportAsync |
| maintenance-export | Executor route → ExportStateAsync → WriteJsonExportAsync |
| telemetry-export | Executor route → ExportStateAsync → WriteJsonExportAsync |
| studio-monitoring-export | Executor route → ExportStateAsync → WriteJsonExportAsync |
| system-shortcuts-export | SystemShortcutsExportAsync → WriteJsonExportAsync |
| experience-visual-manifest | ExperienceVisualManifestAsync → WriteJsonExportAsync |
| hardware-report | HardwareReportAsync → WriteJsonExportAsync |
| terminal-export | TerminalExportAsync → WriteJsonExportAsync |
| file-preview-export | FilePreviewExportAsync → WriteJsonExportAsync |

Sources: [routes](D:/GitHub/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs), [state helper](D:/GitHub/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.State.cs:122), [experience callers](D:/GitHub/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs), [shortcut caller](D:/GitHub/WinCare/src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs:914).

On these reviewed paths, callers do not control the private helper's stream lifetime and do not provide an alternate successful writer. Earlier validation/admission/data collection may prevent a command reaching the helper. The warranted claim is therefore **source-proven exposure to a runtime-proven helper failure when reached**, not nine individually observed command failures. All 104 mutations remain individually unexecuted; subtracting nine inferred callers must not make them appear tested.

## Required next verification, still outstanding

1. Reproduce the installed artifact from captured source/toolchain/native-DLL/publish properties; build clean independent outputs and compare manifests before running them. Separate trimming from custom pruning and record warnings rather than confusing warning suppression with a successful causal experiment.
2. Triage each of the 144 remaining read-only commands into runnable with defaults, requires controlled input, expensive/slow, external dependency, or unavailable platform capability. Use explicit time/output budgets and preserve per-command outcomes. Do not execute the catalog blindly based on its read-only label.
3. Exercise 104 mutations by family in disposable Windows environments, including ordinary app-state operations, exports, privileged changes and destructive recovery. Verify observable effects, partial failures, undo and restart behavior. The existing risk fields are a starting point, not a rollback certificate.
4. Add benign native-cleaner fixtures for fresh/old/locked/inaccessible files, bounded work, nested entries and truthful result counters; inspect target containment defensively without producing an exploitation recipe.
5. Test Guard reconnect/EOF/deadline/restart with a controlled peer and actual account permissions. Inspect service identity and pipe access without assuming an LPE exists.
6. Run plugin collection/lifecycle and repeated-page navigation retention tests; measure active rendering and cold extraction separately from idle process counters.
7. Use actual available Windows/ARM64/DPI/assistive-tech environments and document unavailable combinations. Do not substitute CI configuration inspection for hardware execution.

## Documentation changes made

- Old review prominently marked superseded; historical findings preserved.
- Main report explicitly labeled an incomplete runtime audit; 7 + 1 crash decomposition stated at the top.
- Per-command individual execution and evidence-class columns added, preserving the original truthful shared-helper qualification.
- Read-only/mutation totals and catalog risk distribution generated into command-coverage-summary.json.
- This claim-by-claim follow-up linked from the main report; unresolved experiments remain unresolved rather than being relabeled as passed.

These changes improve the accuracy and usability of the audit. They do not expand its empirical runtime coverage beyond the experiments already recorded.
