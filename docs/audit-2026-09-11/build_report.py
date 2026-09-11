"""Materialize the authored audit and annotate evidence inventories; no production changes."""
from pathlib import Path
import csv, json, re
ROOT=Path(__file__).resolve().parents[2]
OUT=Path(__file__).resolve().parent
F=[]
def finding(title,severity,confidence,source,problem,trigger,impact,cause,proof,fix,test,related):
    F.append(dict(id=f'F-{len(F)+1:03}',title=title,severity=severity,confidence=confidence,source=source,problem=problem,trigger=trigger,impact=impact,cause=cause,proof=proof,fix=fix,test=test,related=related))

finding('Seven installed navigation destinations terminate the app','High','Runtime confirmed; exact artifact cause unresolved',
'src/WinCare.App/Views/Pages/CheckupPage.xaml:122',
'Checkup, System care, Security, Repair and recovery, All tools, Activity and Plugin store crash the installed rc5 artifact. This is separate from AI Doctor below.',
'Start the supplied desktop executable and select the destination; no command execution is needed.',
'Most core workflows cannot be entered. An apparently working Home masks a largely unusable release.',
'Recorded stacks fail in WinRT collection projection / XAML ItemsSource binding. Current local untrimmed Checkup, All tools and Plugin store survive, so source/artifact build differences matter.',
'evidence/Nav*-crash.log and *-navigation.json; Checkup also has the earlier fresh crash evidence. No independent backend daemon failure is established.',
'Reproduce from the exact release build configuration; repair projection/binding preservation and validate every route in the final artifact. Do not merely swallow the fatal exception.',
'Portable and MSIX navigation matrix across every destination, x64/ARM64, with crash-log and process-exit assertions.', 'F-002, F-003, F-037, F-038')
finding('AI Doctor references a nonexistent converter and crashes','High','Runtime and source confirmed',
'src/WinCare.App/Views/Pages/AiDoctorPage.xaml:77',
'Two IsEnabled bindings request InverseBooleanConverter, but there is no definition anywhere under src.',
'Navigate to AI Doctor in the fresh local build.',
'Doctor cannot open, even in the build where collection-backed pages work.',
'A missing StaticResource dependency in compiled XAML bindings.',
'evidence/untrimmed-NavAiDoctor-crash.log explicitly reports Cannot find a resource with the given key: InverseBooleanConverter. Line 85 repeats the reference.',
'Define and test the resource or use an explicit inverse view-model property; verify the generated page bindings.',
'Load the real page and toggle idle/analyzing/error states, asserting controls enable correctly.', 'F-003, F-006')
finding('Artifact smoke tests omit the routes that fail','High','Source confirmed',
'src/WinCare.App/App.xaml.cs:53',
'Smoke testing opens the shell, verifies ABI, initializes plugins, runs system preview and exits. It never opens the failing pages.',
'CI passes --smoke-test while meaningful navigation remains broken.',
'Passing CI provides little evidence that a person can use the shipped app.',
'Release acceptance is built around startup and backend availability instead of user journeys.',
'RunPortableSmokeTestAsync and .github/workflows/native-winui.yml; page VM tests do not instantiate compiled XAML.',
'Require final-artifact navigation and representative read-only workflows before promotion.',
'Detect both the missing converter and collection binding failures in the packaging gate.', 'F-001, F-002, F-032, F-037')
finding('Mutation approval promises differ from dispatcher policy','High','Source confirmed',
'src/WinCare.CommandCatalog/Models/CommandDefinition.cs:117',
'Home promises review receipts and explicit approval for every change. Cleaner identifiers are downgraded to Safe, which executes directly; Moderate actions accept confirmation without a prior receipt. Quick Clean also requires elevation but offers no elevation handoff.',
'Normal Home cleanup is blocked unelevated; elevated Home cleanup can apply directly.',
'Users cannot rely on the advertised review boundary and may encounter immediate deletion or an unexplained prerequisite.',
'Identifier-derived risk exceptions, dispatcher admission and user copy encode different policies.',
'CommandDispatcher.cs admission near line 196, HomePageViewModel.cs:131, WindowsCommandExecutor.cs:96, ToolExecutionViewModel.cs:159. Host cleanup was not executed.',
'Use one explicit mutation admission contract and privilege preflight. Review resolved targets before applying; align the UI with actual requirements.',
'Table-driven tests for every risk tier/preview/receipt/elevation combination, plus Home cleanup on disposable fixtures.', 'F-005, F-007, F-010, F-021')
finding('Cleanup preview names different roots from execution','High','Source confirmed',
'src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs:696',
'Preview metadata names TEMP and WINDIR/Temp; CleanerDiskPressure executes the current temp and LocalApplicationData/Temp roots.',
'Review and apply the cleanup command.',
'The proposed scope is not a reliable description of what will be changed.',
'Preview targets are separately hard-coded rather than derived from the execution plan.',
'WindowsCommandExecutor.Experience.cs:305 target list versus preview metadata. No real files deleted in this audit.',
'Resolve a single immutable target plan for preview and execution, deduplicate equivalent roots, and include exclusions.',
'Fixture with distinct temp/Windows/local roots; verify preview set equals visited execution set.', 'F-004, F-021')
finding('Doctor labels hard-coded network text as measured evidence','High','Source confirmed',
'src/WinCare.Application/Diagnostics/DiagnosticEvidenceCollector.cs:32',
'The network evidence branch creates a fixed DNS/socket observation and sets HasMeasuredEvidence=true without a network probe.',
'A network-related diagnostic intent reaches the collector, after the page crash is repaired or through application-layer callers.',
'Diagnostic recommendations rest on invented evidence.',
'A placeholder was promoted into the same evidence model as real measurements.',
'Collector branch contains no actual network query; default translator uses the collector.',
'Collect a bounded real observation or mark it unavailable; carry source, timestamp and confidence into recommendations.',
'Disconnected, DNS-failure and healthy fixtures must produce distinct measured results; no fixed healthy observation.', 'F-002, F-019, F-020')
finding('Typed command inputs never mount in the observed UI','High','Runtime and source confirmed; secondary synchronization risk source-only',
'src/WinCare.App/Views/Pages/AllToolsPage.xaml.cs:22',
'The constructor searches the visual tree before it is ready. ReplaceRawParameterEditor returns when no expander is found and is not retried on Loaded. The working local page retains Command parameters JSON. If the generated editor is mounted later, its controls also copy initial values without model-to-control updates after raw JSON import.',
'Open All tools, search window-search, select the tool.',
'Ordinary users receive raw JSON instead of the intended typed controls. A future mount-only fix would expose stale displayed values.',
'UI initialization depends on a template visual tree at constructor time; generated fields lack two-way binding.',
'evidence/tool-editor-uia.json retains Command parameters JSON and Advanced Parameters (JSON), with no AdvancedParameterEditing control; constructor lines 28-29 and editor methods near 171/251.',
'Use named declarative controls or Loaded/template lifecycle, then real two-way field bindings. Surface invalid JSON conversion errors instead of swallowing them.',
'Real-page tests covering every field type, raw-to-typed round trip, invalid JSON, tool switching and execution payload/display equality.', 'F-003, F-004, F-032')
finding('Shared JSON exports fail because their source stream is still open','High','Isolated runtime and source confirmed',
'src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.State.cs:132',
'WriteJsonExportAsync holds an await-using FileStream with FileShare.None through File.Move. The stream is disposed only after the move attempt.',
'Export harmless JSON to a new writable path on Windows.',
'Shared exports fail, including widget, maintenance, telemetry, studio monitoring, hardware report, terminal, file preview, system shortcuts and visual manifest paths.',
'Incorrect stream lifetime around an otherwise appropriate temporary-file replacement design.',
'evidence/export-fixture.json: IOException (file in use), targetExists=false; harness invokes the actual helper with {audit:true} in its own sandbox.',
'Close the temporary stream before replacement; retain cleanup and preserve an existing destination on failure.',
'New destination, replacement destination, cancellation and denied-write tests on Windows; assert valid content and no leaked temp files.', 'F-010, F-011, F-013')
finding('Five category shortcuts search for phrases that match no tools','High','Source and catalog query confirmed',
'src/WinCare.Application/Tools/ToolCatalogService.cs',
'Search treats the whole query as one substring. Built-in shortcuts send storage cleanup, network update, defender firewall, export backup and recovery reset; all return zero matches.',
'Follow the corresponding category-page shortcut after its navigation crash is fixed.',
'The product leads users to empty results for advertised workflows.',
'Navigation intent was encoded as prose instead of structured area/category/command selection.',
'Embedded 259-command catalog evaluated with Matches semantics; SystemCarePageViewModel, SecurityPageViewModel and RepairRecoveryPageViewModel supply these strings.',
'Use stable command IDs or explicit category filters for product navigation; define user search token semantics separately.',
'Every built-in shortcut must resolve to the intended nonempty command set.', 'F-024, F-025')
finding('Partial mutations can be described as having failed safely','High','Source confirmed; host mutation not exercised',
'src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs:121',
'Common IO/Win32 errors are converted into failed safely; access failures become Blocked. A multi-step command may already have applied earlier changes. Cancellation also lacks a universal partial-effect outcome.',
'A later step fails after an earlier mutation succeeds, such as brightness succeeding before contrast fails.',
'Users may retry or assume no change even though system state is partially modified.',
'The executor loses mutation certainty before the dispatcher can classify unknown/partial execution; progress records are not a universal transactional log.',
'ExecuteAsync catch ladder and WindowsCommandExecutor.Desktop.cs DisplayCalibrate; remediation records accumulate across incremental steps. This is a failure-path source proof, not an observed hardware change.',
'Track started/applied steps and return explicit partial/unknown outcomes with reconciliation guidance. Do not claim rollback unless verified.',
'Injected dependency fails on step two; assert first-step effects and partial status survive reporting, cancellation and restart.', 'F-004, F-008, F-013, F-021')
finding('Independent state-store instances conflict on updates','Medium','Isolated runtime and source confirmed',
'src/WinCare.Infrastructure/Commands/CommandStateStore.cs:88',
'UpdateAsync is atomic only within one instance. App startup does not enforce single-instance ownership, and independent stores use independent semaphores.',
'Two instances update the same state key concurrently.',
'An update can fail or be lost. The atomicity claim does not cover real multi-instance ownership.',
'Read-modify-write locks are process/object local while the file is shared.',
'evidence/backend-probes.json: expected counter2, actual1; one update UnauthorizedAccessException and the other Succeeded, using two harmless stores.',
'Choose single-instance application ownership or a cross-process transaction/lock protocol with version checks and bounded wait.',
'Two processes update distinct records repeatedly; both survive without lost updates, sharing violations or corrupt JSON.', 'F-008, F-012, F-013')
finding('Plugin state persistence suppresses failures','Medium','Isolated runtime and source confirmed',
'src/WinCare.Infrastructure/Plugins/PluginStateRepository.cs:68',
'SaveEnabledPluginIds catches all failures and returns normally. Load errors become an empty set with no diagnostic distinction.',
'The persistence destination is unwritable or invalid.',
'Enable/disable state can appear saved and then revert after restart; storage problems are hidden.',
'A void persistence contract uses silent catch-all recovery.',
'evidence/backend-probes.json pluginPersistence: writeThrew=false and fileExists=false for a directory used as the target.',
'Return or publish persistence failure, retain the last known good state, and distinguish missing from damaged data.',
'Denied path/corrupt file/restart tests must expose a warning and avoid false persistence success.', 'F-011, F-034')
finding('Shutdown does not settle the journal or runtime services','Medium','Source confirmed; loss window not force-tested',
'src/WinCare.App/MainWindow.xaml.cs:66',
'Close flushes preferences but not the queued activity journal. AppRuntime owns disposable executor/catalog/installer and plugin lifecycle without an application shutdown coordinator.',
'Close immediately after an outcome or while plugin/background work is active.',
'Late history can be lost and plugin shutdown work has no deterministic completion boundary. OS process exit reclaims handles, so this alone is not proof of an idle leak.',
'Process-lifetime ownership is implicit and separate persistence queues have different shutdown treatment.',
'MainWindow close handler, AppRuntime composition, ActivityJournalService queued persistence and registry shutdown methods.',
'Add a bounded shutdown sequence: stop new work, cancel owned tasks, settle mutation certainty, flush durable state, then dispose services.',
'Close immediately after success/failure and during background work; restart and verify recorded outcomes and no owned tasks remain.', 'F-010, F-011, F-014')
finding('Checkup can overlap update searches beyond its apparent deadline','Medium','Source confirmed',
'src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs:146',
'Fast probes clear IsRunning while WUA continues. Another check can start; version guards suppress old UI updates but do not stop old work. The 25-second cancellation deadline is cooperative, while synchronous searcher.Search cannot observe it during the call.',
'Start another check before the previous update search finishes or leave the page.',
'Redundant system work and misleading completion/cancellation state; a plausible active-work resource problem, not a proven idle loop.',
'Run ownership stops at the UI version counter; underlying COM work is not bounded by that counter.',
'CheckupPageViewModel update task, ParallelCommandProbeRunner and WindowsCommandExecutor.Security.cs WUA Search path.',
'Keep one owned run, prevent overlap, cancel superseded tasks and isolate operations that cannot honor deadlines.',
'Slow/non-cooperative fake update provider: repeat, navigate away, cancel and close without accumulating work.', 'F-013, F-015')
finding('Checkup summary and Results use divergent copies','Medium','Source confirmed',
'src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs:303',
'Results rows are created before disk/security evaluation updates Quick check rows.',
'A collected measurement produces a warning or critical finding.',
'A problem can appear in one section while Results merely says Collected.',
'Derived display rows are copied before the final state transition.',
'Run/evaluation sequence and separate PageRow updates.',
'Represent findings once and derive every visible summary from that model.',
'Low disk and protection-warning fixtures must agree across tabs and Home.', 'F-017, F-020')
finding('Home read-only actions hide useful data and overstate the result','Medium','Runtime and source confirmed',
'src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs:162',
'Startup displays a count and Audit Complete; Network displays an interface count and Connected. Returned entry/adapter details are discarded. The names Startup Boost and Network Refresh imply effects these queries do not perform.',
'Invoke either Home read-only button.',
'The button runs but does not complete a useful user decision, and adapter enumeration is confused with connectivity.',
'Technical success is mapped directly to a product outcome without interpreting results.',
'evidence/HomeStartupBoostButton-after.json and HomeNetworkRefreshButton-after.json:14 entries and8 interfaces; source maps successful query to Connected.',
'Show actionable inspected entries and actual connectivity evidence; name the actions for inspection.',
'Empty/offline/disabled interfaces and startup entries with no impact data must produce truthful visible outcomes.', 'F-006, F-020, F-028')
finding('Home calls unrelated old records current evidence','Medium','Source confirmed',
'src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs:284',
'Each command contributes its latest successful record without age limit or common run ID; newest timestamp labels the combined state.',
'Records exist from different check sessions or dates.',
'A stale mixed set can appear as a fresh 4/4 check.',
'Activity retention is being used as a measurement-session store.',
'RefreshActivity selects command records independently.',
'Persist coherent check sessions and show per-area freshness/unavailable states.',
'Mixed old/new, failed-latest and expired records must never become a fresh complete session.', 'F-015, F-020, F-023')
finding('Telemetry loses metric validity and volume identity','Medium','Source confirmed',
'native/wincare-core/src/telemetry.rs:106',
'CPU baseline and GetSystemTimes failure yield zero while aggregate snapshot success marks CPU available. Disk telemetry always probes C: and is shown beside generic cleanup targets.',
'First inspector sample, failed CPU API or Windows/cleanup volume other than C:.',
'Unknown becomes measured zero and the displayed disk may not be the task target.',
'The aggregate FFI result lacks per-metric validity and explicit target identity.',
'Native snapshot implementation and HomePageViewModel.TryRefreshNativeTelemetry availability mapping.',
'Return per-metric status, sample interval and actual volume; derive task targets explicitly.',
'First sample/API failure and non-C system-volume fixtures; do not display unknown as zero.', 'F-019, F-020, F-039')
finding('Doctor memory evidence is stale or absent but counted as live','Medium','Source confirmed',
'src/WinCare.Application/Diagnostics/DiagnosticEvidenceCollector.cs:122',
'GC memory information is used as current machine pressure; absent/zero values are treated as measured. Evidence counts include records with HasMeasuredEvidence=false.',
'A memory diagnosis before useful GC information or after machine pressure changes.',
'Doctor can give reassuring advice without a fresh measurement.',
'Runtime GC bookkeeping and system telemetry share an undifferentiated evidence label.',
'ProbeMemoryUsage and AI Doctor evidence-count projection.',
'Use a current system probe and count only valid measured evidence, with timestamps.',
'Unavailable/stale/changed system-memory inputs must preserve uncertainty.', 'F-006, F-018, F-020')
finding('Health classifications disagree and exceed their evidence','Medium','Source confirmed',
'src/WinCare.App/ViewModels/Pages/CheckupPageViewModel.cs:303',
'Checkup uses10/20GB disk thresholds, Doctor15GB or10%, and health10%. Checkup Healthy does not reflect all collected fields; stopped Defender alone is not a complete assessment of installed protection.',
'The same machine is assessed by different product paths.',
'Contradictory warnings and overly broad healthy/critical labels.',
'Diagnostic policies are duplicated across UI/application/executor layers.',
'Checkup evaluation, DiagnosticEvidenceCollector storage and System executor health/security functions.',
'Centralize explicit, versioned assessment rules and state the limits of each check.',
'Shared boundary fixtures across every entry point, including alternate protection and disabled UAC.', 'F-006, F-015, F-017, F-018, F-019')
finding('Cleanup reports completion despite skips and scan caps','Medium','Source confirmed',
'src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Experience.cs:305',
'Cleanup counts skipped files, caps enumeration at200,000 and returns success; Home reduces this to Clean Complete.',
'Inaccessible files, a large target or partial work.',
'The user cannot tell what was omitted or whether the intended cleanup completed.',
'Bounded/partial execution metadata is discarded by the success presentation.',
'CleanerDiskPressure and Home cleanup status mapping; no host cleanup run.',
'Expose reclaimed bytes, omissions and truncation with a partial outcome.',
'Fixtures with skips and an artificial low scan cap must show partial completion.', 'F-004, F-005, F-010')
finding('Admission rejections are absent from Activity','Medium','Source confirmed',
'src/WinCare.Application/Commands/CommandDispatcher.cs:196',
'Admission rejection returns before journal.Begin.',
'An action is rejected for approval/receipt/deadline policy.',
'The failed attempt leaves no activity trail, making apparently dead controls harder to understand.',
'Only admitted execution enters the event model.',
'Admission sequence precedes journal creation; distinguish this from executor-level blocked outcomes, which can be journaled.',
'Record rejected attempts as non-mutating outcomes with actionable reasons.',
'Every admission rejection appears once with no false success or mutation record.', 'F-004, F-013, F-023')
finding('Reports hide the journal retention limit','Medium','Source confirmed',
'src/WinCare.Application/Activity/ActivityJournalService.cs:14',
'Only200 records are retained, while daily report aggregates do not explain eviction.',
'More than200 operations or a report spanning evicted data.',
'A report can look complete while omitting older events.',
'Recent-feed retention and durable reporting use the same limited store.',
'Retention constant and Activity report projection.',
'Label coverage/retention or maintain a separate durable report store.',
'201+ events across dates: reports disclose or preserve excluded history.', 'F-013, F-017, F-022')
finding('Category workspaces show fixed content instead of current state','Medium','Source confirmed',
'src/WinCare.App/ViewModels/Pages/SystemCarePageViewModel.cs',
'System care, Security and Repair construct static rows and mostly forward to All tools. Security can still show Not checked after a successful query elsewhere; Undo/Routines sections lack completed flows.',
'Perform a check elsewhere and revisit these categories.',
'Navigation adds work without providing a coherent task workspace.',
'A directory of tools is presented using status-oriented workspace language.',
'The three category view models and their event handlers.',
'Build live task summaries and direct actions, or clearly present these pages as directories.',
'Complete a check/action, navigate away/back and verify results and next steps.', 'F-009, F-015, F-025')
finding('Global search promises unavailable scopes and retains stale queries','Medium','Source confirmed',
'src/WinCare.App/MainWindow.xaml:39',
'Placeholder promises tasks, settings and activity, but Shell only opens tool search. Empty navigation queries are ignored, so a cached tool page keeps its old search.',
'Search for a setting/activity, or clear a previous global query.',
'Search appears broken or silently searches the wrong scope.',
'Search copy, navigation parameters and catalog filtering have different contracts.',
'ShellPage.OpenGlobalSearch and AllToolsPage.OnNavigatedTo:36.',
'Implement explicit supported scopes or narrow the promise; treat an explicit empty query as reset.',
'Settings/activity queries and nonempty→empty navigation tests.', 'F-009, F-024')
finding('Cleaner schedules have no execution consumer','Medium','Source confirmed',
'src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs:423',
'cleaner-disk-pressure-schedule writes cleaner-schedules state; no scheduler reader or execution loop is connected.',
'Save a cleanup schedule and wait for its intended time.',
'A saved record is mistaken for an automatic maintenance capability.',
'Persistence was implemented without the feature lifecycle.',
'Route invokes UpsertStateItemAsync; repository-wide src/native search finds no consumer of the key.',
'Implement an owned, observable scheduler or explicitly label saved configuration as non-executing.',
'Controlled clock plus restart/missed-run/cancel tests; exactly one admitted action at due time.', 'F-024, F-027')
finding('Experimental Guard notifications are not delivered by the app','Medium','Source confirmed; documented limitation',
'native/wincare-guard/src/main.rs:50',
'Guard writes XML alerts into LOCALAPPDATA; the app has no notification-queue reader or GuardPipeClient use. Repeated critical ticks create more files without a consumer/retention loop.',
'Run the optional Guard with a sustained critical threshold.',
'No promised in-app notification delivery; accumulating orphan alert files in an experimental path.',
'Producer, app consumer and lifecycle were never joined.',
'Guard main/notification path and absence of app call sites. Guard is not started by normal app launch.',
'Keep explicitly experimental or implement namespaced bounded queue, acknowledgement, retention and notification activation.',
'Controlled repeated alerts yield deduplicated delivered notifications and bounded storage.', 'F-026, F-013')
finding('Home hides the primary check action below decorative content','Medium','Screenshot and source confirmed',
'src/WinCare.App/Views/Pages/HomePage.xaml:461',
'The hero has no primary check action. Run Checkup appears after quick-action cards and lower panels; the supplied first-screen image does not reach it.',
'First launch at the supplied window size.',
'The user must scroll past secondary actions to start the central workflow; large empty cards and repeated status furniture weaken hierarchy.',
'Layout follows decorative dashboard sections rather than the check→findings→review task order.',
'All supplied screenshots; Home XAML order and UIA offscreen flag for HomeRunCheckup.',
'Place the main read-only check in the hero; use compact summaries and contextual actions after evidence exists.',
'At representative window/text scales, a first-time user can discover and activate Checkup without scrolling.', 'F-016, F-029, F-031')
finding('Action-card headers have no allocated space for title versus badge','Medium','Source-layout risk; intermediate-width overlap not runtime reproduced',
'src/WinCare.App/Views/Pages/HomePage.xaml',
'The title/icon group and right badge share one grid cell while three columns persist to a narrow breakpoint.',
'Intermediate width near920DIP or larger text.',
'Headers may overlap or become cramped before responsive reflow occurs.',
'Breakpoint and content minimum widths are unrelated; siblings compete for the same space.',
'Three-column width/padding calculation and shared-cell XAML; supplied wide images alone do not prove the narrow failure.',
'Reserve separate layout columns and reflow based on minimum usable card width.',
'Boundary widths around breakpoint plus200% text and keyboard focus screenshots.', 'F-028, F-030')
finding('Startup action label has inconsistent visible contrast','Medium','Screenshot confirmed; state/cause unresolved',
'src/WinCare.App/Styles/ControlStyles.xaml',
'Analyze Startup is black on teal in supplied images, while adjacent primary labels are white.',
'The state captured in the supplied screenshots.',
'Visual inconsistency and reduced readability of a primary action.',
'Exact resource/state cause is unconfirmed: current source uses the same style, so no unique hard-coded black label is alleged.',
'Images1–3; token contrast tests pass but do not exercise actual control visual states.',
'Inspect normal/hover/pressed/disabled/focus/theme resources in the release artifact before choosing the correction.',
'Rendered contrast and screenshots of all primary-button states in Light/Dark/High Contrast.', 'F-028, F-029')
finding('Internal terminology and duplicate inspector names confuse users','Low','Screenshot and source confirmed',
'src/WinCare.App/Views/Pages/HomePage.xaml:225',
'Home exposes cleaner-disk-pressure and dispatcher-issued preview receipt; four checks sit next to six area tiles without explanation. Three inspector buttons share Toggle Telemetry Inspector and the same command.',
'Read or navigate the Home dashboard, including with accessible names.',
'Users must infer technical concepts and cannot identify an inspector by its name.',
'Developer diagnostic language leaked into the main product flow.',
'Supplied images, Home UIA tree and inspector command bindings.',
'Use task/result language, explain check coverage, and name contextual actions distinctly.',
'Content review plus UIA name assertions and first-use task walkthrough.', 'F-028, F-032')
finding('All-tools selectable rows expose type names to accessibility APIs','Medium','Runtime confirmed',
'src/WinCare.App/Views/Pages/AllToolsPage.xaml',
'ListItem accessible names are WinCare.App.ViewModels.Pages.ToolRowViewModel rather than the tool title.',
'Navigate the selectable tools list through UI Automation or assistive technology.',
'Rows are difficult to distinguish without relying on descendant text; full screen-reader behavior still needs testing.',
'The item container has no meaningful bound accessible name.',
'evidence/untrimmed-NavAllTools-uia.json contains repeated type-name ListItem names.',
'Bind item-container AutomationProperties.Name to a concise title/risk summary and verify selection announcements.',
'UIA names must identify each row; Narrator/keyboard selection must announce the actual command.', 'F-003, F-007, F-031')
finding('Remote catalog reads have no explicit body-size bound','Medium','Source confirmed; no hostile input tested',
'src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs:99',
'Catalog and signature bodies are buffered in full before parsing/verification. Unlike package downloads, these paths have no explicit byte cap or shared body-read deadline.',
'A catalog response is unexpectedly large or slow.',
'Memory/time consumption can exceed the limits applied elsewhere in plugin admission.',
'Transport headers, body consumption and admission bounds are treated separately.',
'ResponseHeadersRead followed by ReadAsByteArrayAsync/ReadAsStringAsync; PluginInstallerService has explicit package limits.',
'Apply bounded streaming reads and a full-operation deadline to catalog/signature/cache inputs; fail closed for installation freshness.',
'Controlled oversized/slow local fixtures must fail within byte/time budgets without allocating the whole body.', 'F-034')
finding('Widget failure changes plugin state without reconciling registered commands','Medium','Source confirmed',
'src/WinCare.Application/Plugins/PluginRegistryService.cs:124',
'GetActivePluginWidgets marks an entry Error on exception but does not unregister its commands or publish the same catalog transition as lifecycle operations.',
'An enabled admitted plugin throws while supplying widgets.',
'Registry state, searchable tools and dispatcher availability can disagree.',
'A read-style widget enumeration mutates lifecycle state outside the normal transition path.',
'GetActivePluginWidgets catch block and host registration ownership.',
'Route failures through one serialized lifecycle transition, or keep widget failure distinct from whole-plugin failure.',
'Throwing-widget fixture: registry, catalog and dispatcher must agree after failure and recovery.', 'F-012, F-013, F-033')
finding('Release reruns can mix a new tag with old assets','High','Source confirmed; no remote action performed',
'.github/workflows/native-winui.yml:426',
'Manual publication force-retags to the current SHA; existing same-name assets are skipped rather than checksum-compared or replaced. Tag push failure is tolerated.',
'Rerun publication for an existing version from changed source.',
'Published source and binaries may disagree; partial reruns can retain a mixed artifact set.',
'Version identity is mutable while asset identity is based only on filenames.',
'Release shell block lines426–438.',
'Make releases immutable; reject SHA/checksum mismatch and upload an attested complete set from one validated run.',
'Local mocked release API with old tag/assets must fail safely instead of mixing versions.', 'F-003, F-036, F-038')
finding('Stable publication uses the release-candidate readiness gate','High','Source confirmed',
'.github/workflows/native-winui.yml:52',
'The main gate always invokes --mode rc. Later publication marks non-prerelease versions --latest without invoking production readiness. Production mode requires BehaviorVerified while the259 catalog entries are Implemented.',
'Publish a stable version through the normal workflow.',
'The workflow can bypass the repository’s own behavior-verification promotion requirement.',
'Release classification and readiness enforcement are disconnected jobs/policies.',
'native-winui.yml:52 and407; finalize_native_release.py:183–206. A separate manual production workflow does not make this gate mandatory.',
'Select/enforce production readiness for stable publication and make it a required dependency.',
'A stable version with any non-BehaviorVerified command must fail; RC behavior remains explicit.', 'F-003, F-035, F-037')
finding('A restored strict trimmed build fails serialization analysis','High','Build confirmed',
'src/WinCare.Domain/Commands/CommandRequest.cs:30',
'With trimming enabled and warnings visible, restored publication fails IL2026 in CommandRequest and ApprovedMutationPlan serialization.',
'Publish the current source with PublishTrimmed=true and SuppressTrimAnalysisWarnings=false after restore.',
'The trimmed configuration is not cleanly validated, and suppression conceals unresolved compatibility warnings.',
'Reflection-dependent JSON serialization crosses the trim analysis boundary without an explicit contract.',
'evidence/publish-trimmed-restored.txt has three IL2026 errors; ApprovedMutationPlan.cs:107/113. Earlier no-restore variants were byte-identical and are NOT a valid trim comparison.',
'Use explicit serialization metadata or verified preservation, then perform a clean profile-specific restore/publish and route tests.',
'Strict clean trimmed publish must pass without blanket suppression and all serialized contracts must round-trip in the artifact.', 'F-001, F-003, F-038')
finding('CI portable publication does not use the portable trimming profile','Medium','Source/property inspection confirmed',
'.github/workflows/native-winui.yml:297',
'The portable profile enables trimming, but the CI command supplies neither that profile nor PublishTrimmed. The app only defaults PublishProfile for packaged builds.',
'Compare local portable-profile publication with the CI single-binary command.',
'Different publication paths validate different artifacts, frustrating crash reproduction and size/runtime expectations.',
'Duplicated publish configuration rather than one canonical profile.',
'WinCare.App.csproj PublishProfile condition and Properties/PublishProfiles/portable-x64.pubxml; initial local no-restore binary hashes equal despite variant labels, disclosed separately.',
'Use one checked-in configuration for local and CI builds; record effective properties, hashes and native DLL provenance.',
'Assert intended effective publish properties and compare signed build manifests across paths.', 'F-001, F-035, F-037')
finding('The documented C header omits exported telemetry/cleaner ABI','Low','Source confirmed',
'native/wincare-core/include/wincare_core.h',
'The public header documents version/hash/directory/system JSON functions but omits the newer aggregate snapshot and cleaner exports and their structures.',
'A consumer implements bindings from the supplied header.',
'The header is not a complete contract for the library actually exported.',
'Rust exports and C# P/Invoke evolved independently of the published header.',
'native/wincare-core/src/lib.rs:546/560 versus include/wincare_core.h and managed interop declarations.',
'Generate or maintain one ABI definition with layout/version compatibility tests.',
'Compile a C consumer for every supported export on x64/ARM64 and compare struct sizes/offsets.', 'F-018')
finding('Native directory-size entry limit does not bound pending allocations','Medium','Source confirmed; stress not executed',
'native/wincare-core/src/lib.rs:283',
'The500,000 entry cap is checked when popping work, but read_dir pushes every child path into pending before that check runs again.',
'A very wide directory is sized.',
'Memory and enumeration work can exceed the advertised processing cap before the truncated status is reached.',
'The bound is applied after work expansion instead of at queue admission.',
'accumulate_dir_size loop: read_dir pushes child paths without checking the remaining budget.',
'Bound queued plus processed entries and make cancellation observable during enumeration.',
'Use a synthetic enumerator with a small limit and assert maximum pending work and explicit truncation.', 'F-018, F-033')

def source_link(value):
    p,sep,line=value.rpartition(':')
    if sep and line.isdigit():
        return f'[{value}]({(ROOT/p).as_posix()}:{line})'
    return f'[{value}]({(ROOT/value).as_posix()})'

def render_findings(severity):
    result=[]
    for x in F:
        if x['severity']!=severity: continue
        result.append(f"### [{x['id']}] {x['title']}\n\n**Severity:** {x['severity']}. **Confidence:** {x['confidence']}.\n\n**Affected location:** {source_link(x['source'])}\n\n**Problem:** {x['problem']}\n\n**Trigger / user-visible manifestation:** {x['trigger']}\n\n**Impact:** {x['impact']}\n\n**Root cause:** {x['cause']}\n\n**Proof and limits:** {x['proof']}\n\n**Remediation:** {x['fix']}\n\n**Regression test:** {x['test']}\n\n**Related findings:** {x['related']}\n")
    return '\n'.join(result)

intro=(OUT/'report-introduction.md').read_text(encoding='utf-8')
outro=(OUT/'report-assessments.md').read_text(encoding='utf-8')
counts={s:sum(x['severity']==s for x in F) for s in ['Critical','High','Medium','Low']}
report=intro.replace('{{COUNTS}}',', '.join(f'{n} {s.lower()}' for s,n in counts.items()))
report+='\n## F. Critical findings\n\nNone established at the requested catastrophic/security-compromise severity. This does not diminish the release-blocking usability failures.\n\n'
for letter,severity in [('G','High'),('H','Medium'),('I','Low')]:
    report+=f'## {letter}. {severity} findings\n\n'+render_findings(severity)+'\n'
report+=outro
# Keep numeric prose readable without modifying identifiers or file contents.
report=re.sub(r'\b(all|the|The|only|Only|about|About|of|over|with|than|contains|includes|returned|expected|observed|at|by|run|ran|passed|across|within|for|is|are|commands|requires|collected)(\d)',r'\1 \2',report)
report=re.sub(r'(\d)(MiB|bytes|seconds|DIP|GB)\b',r'\1 \2',report)
(OUT/'report.md').write_text(report,encoding='utf-8')
(OUT/'findings.json').write_text(json.dumps(F,indent=2),encoding='utf-8')

path=OUT/'command-matrix.csv'
with path.open(encoding='utf-8-sig',newline='') as f: rows=list(csv.DictReader(f))
probes=json.loads((OUT/'evidence/backend-probes.json').read_text(encoding='utf-8-sig'))
tested={x['id']:x for x in probes['commands']}
for row in rows:
    row['individual_command_executed_by_audit']='yes' if row['id'] in tested else 'no'
    row['evidence_class']='individual read-only backend probe' if row['id'] in tested else 'source inventory only; not runtime verified'
    if row['id'] in tested: row['runtime_status']='isolated backend read-only probe succeeded; returned shape/timing in evidence/backend-probes.json; not full UI feature certification'
    if 'ExportStateAsync' in row['route'] or row['id'] in ['experience-visual-manifest','hardware-report','terminal-export','file-preview-export','system-shortcuts-export']:
        row['runtime_status']='shared export helper failure reproduced in harmless Windows fixture; individual command not executed (F-008)'
        row['evidence_class']='shared-helper runtime proof plus caller source trace; NOT individual-command runtime failure'
with path.open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.DictWriter(f,fieldnames=rows[0]);writer.writeheader();writer.writerows(rows)
from collections import Counter
coverage={
    'total_commands':len(rows),
    'read_only_total':sum(r['read_only']=='True' for r in rows),
    'read_only_executed':len(tested),
    'read_only_not_executed':sum(r['read_only']=='True' and r['id'] not in tested for r in rows),
    'mutating_total':sum(r['read_only']=='False' for r in rows),
    'mutating_catalog_risk_counts':dict(Counter(r['risk'] for r in rows if r['read_only']=='False')),
    'shared_helper_source_impacted_not_executed':[r['id'] for r in rows if 'F-008' in r['runtime_status']],
    'note':'Catalog risk is not necessarily effective dispatcher risk. Read-only metadata does not establish no prerequisites, negligible load, or no data exposure.'}
(OUT/'command-coverage-summary.json').write_text(json.dumps(coverage,indent=2),encoding='utf-8')
print(f'Wrote {len(F)} findings: {counts}')
