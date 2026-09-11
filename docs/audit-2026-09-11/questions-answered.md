# Answers to the47 requested audit questions

Read alongside report sections A–AF and the coverage ledger. “Unverified” is a limit of evidence, not a passing result.

This is the finalized question index for an incomplete runtime audit. The [forensic follow-up](D:/GitHub/WinCare/docs/audit-2026-09-11/forensic-follow-up.md) records accepted coverage criticisms and factual corrections; the [package index](D:/GitHub/WinCare/docs/audit-2026-09-11/README.md) defines document authority.

| # | Question | Evidence-backed answer |
|---|---|---|
|1|Launch reliably?|Home repeatedly opens on this host, but eight navigation destinations crash: seven collection-projection failures plus one missing-converter failure in AI Doctor. Clean-machine launch reliability is unverified. E/O.|
|2|Why crashes?|Seven installed routes fail in WinRT collection projection; exact artifact cause unresolved. Doctor references a nonexistent converter, confirmed in installed and fresh builds. F-001/002.|
|3|Backend stays alive?|It shares the app process. Eleven read-only queries succeeded; no separate backend daemon crash was established. O.|
|4|Readiness truthful?|No: Ready/Implemented and successful shell smoke do not prove a usable page or workflow. F-003/024/036.|
|5|What works end to end?|Installed Home launch, Settings/Help/About navigation, Home startup/network query completion; fresh local tool search/selection; isolated11 backend previews. E/M. Useful action-result presentation remains partial.|
|6|Partial/dead/fake/disconnected?|Doctor fabricated network evidence; schedules without scheduler; Guard without app consumer; raw JSON instead of typed editor; static categories; remote install deliberately unavailable. M/AA.|
|7|Controls doing nothing?|No blanket unbound-button claim. Failures traced to crashing pages, empty category searches, missing typed controls and blocked prerequisites. N.|
|8|Incorrect success?|Connected from adapter enumeration, Clean Complete despite skips, failed safely after possible partial mutation, measured Doctor text without measurement. F-006/010/016/021.|
|9|UI/backend synchronized?|Inconsistently: stale mixed Home records, copied Checkup rows, plugin lifecycle divergence, discarded action data. P.|
|10|Worst visible defects?|Primary check below fold; large passive cards; misleading badges/IDs; inconsistent Startup label contrast; raw JSON task inputs. K.|
|11|Visual causes?|Task hierarchy lost beneath decorative layout; shared-cell header competition; control-state contrast cause unresolved. F-028–031.|
|12|Why hard to use?|Crashes interrupt journeys; categories redirect to generic tools/empty results; prerequisites and useful results are absent; technical terms replace decisions. L.|
|13|Navigation coherent?|Destination list exists but failures and wrong query routes break the path. F-001/009/024.|
|14|Information architecture understandable?|Too much equally prominent breadth and insufficient distinction between checks, changes, saved records and experimental tools. C/L/M.|
|15|Errors actionable?|Some parameter/elevation errors help, but fatal navigation, silent persistence and inaccurate failure certainty do not. F-010/012/022.|
|16|Accessibility acceptable?|Cannot certify. UIA exposes repeated type-name rows; inspector names duplicate; rendered contrast/state matrix and Narrator remain gaps. F-030–032.|
|17|Why heavy resources?|Sustained idle load not reproduced. Cold extraction, antivirus/GPU/system activity and active WUA overlap require attribution. V.|
|18|Idle use?|Completed60s: about145MiB working set/65MiB private;1.09375 cumulative CPU seconds; threads29→23; handles996→991. V.|
|19|Runaway loops/workers?|No idle runaway established. Repeated Checkup can overlap noncooperative WUA; optional Guard polls30s and can accumulate XML. F-014/027.|
|20|Damaging complexity?|Shared helpers amplify failures across disparate modules; duplicated evidence/policy/state models produce concrete contradictions. Q.|
|21|Measurable bloat?|Large surfaces require unsupported generic workflows; no dependency declared unnecessary solely by binary size. Stable short-run resource sample does not prove memory bloat. Q/V/Z.|
|22|Dead/duplicate/speculative?|Typed editor lifecycle, schedule consumer, Guard consumer; duplicated diagnostics/cleanup/serialization/publishing. Historical migration material is not active runtime. AA.|
|23|State corruption/loss?|Two-store conflicting updates, silent plugin save failure, unflushed journal; shared export fails without creating target. No malformed JSON corruption was induced. T.|
|24|Resource leaks?|No measured sustained leak. Missing coordinated disposal, stuck trusted plugin callbacks, in-flight COM/FFI and orphan Guard files are risks. S/V.|
|25|Concurrency failure?|Store fixture expected2 but got1 with one access failure; overlapping WUA and lifecycle mutation outside normal gate. F-011/014/034.|
|26|Retries safe?|Not generally established: partial mutations may be retried after misleading failure, release reruns can mix artifacts. No universal idempotency contract. F-010/035.|
|27|Startup deterministic?|Composition is eager and shared plugin initialization is cached, but state/files/DLL/config are external inputs; clean variants not fully exercised. E/O/X.|
|28|Shutdown deterministic?|No common cancellation/flush/disposal boundary. Audit navigation processes closed normally, but late durable work is not guaranteed. F-013.|
|29|Resources cleaned reliably?|Some paths use finally/disposal and bounded process termination; journal/services/native in-flight operations lack whole-app settlement proof. S/T.|
|30|APIs consistent?|Not fully: native/fallback system JSON differs, public header omits exports, risk/review contracts differ. U/F-004/039.|
|31|Clients/servers agree?|No web server. Command UI/dispatcher semantics differ; Guard client/server protocol has bounds but no active app integration. B/U.|
|32|One state authority?|No: journal-derived check evidence, copied result rows and plugin registry/dispatcher diverge. J/P.|
|33|Input resource amplification?|Catalog bodies buffer without explicit cap; native directory expansion exceeds pending-work bound; saved-record growth/FFI cancellation need further bounds. F-033/040, AE.|
|34|Auth consistently enforced?|No login system; Windows privilege, mutation consent and plugin trust are relevant. Privilege checks exist, universal advertised review is not enforced. R/F-004.|
|35|Secrets safe?|No production hard-coded secret established in inspected paths; no history-wide/user-config certification. R/Z.|
|36|Crash-safe persistence?|Not certified: atomic file replacement helps, but transaction/flush/failure boundaries are defective; power loss not tested. T.|
|37|Platforms tested?|This audit Windows x64; CI contains x64/ARM64 smoke, not comprehensive platform UI coverage. W.|
|38|CI tests shipped behavior?|Only partially: packaged startup/system preview, no failing navigation. F-003/X.|
|39|Reproducible clean release?|Not established. Strict restored trim fails, native staging provenance and publish profiles differ, tags/assets mutable. F-035–038.|
|40|False-confidence tests?|Source-string checks, catalog migration counts, VM-only tests and Home/system smoke if interpreted as actual user journeys. Y.|
|41|Missing critical journeys?|Full check/findings/review/apply/result/history, all page construction, typed editor, exports, restart durability, accessibility and platform install/upgrade. AD.|
|42|Future defect risks?|Parallel truth models, broad generic executor/helpers, implicit ownership, policy inferred from names, duplicated artifact configuration. J/Q.|
|43|Advertised but not working?|Universal review, Doctor measured network evidence, schedules, Guard notification delivery, useful Boost/Refresh effects, typed inputs, broad search. F-004/006/007/016/025–027.|
|44|Prototype presented as ready?|Implemented catalog entries and Ready labels exceed verification; remote store/Guard are limited, category pages static. M/AA.|
|45|Docs aligned?|Several confirmed mismatches; explicit experimental/fail-closed limits recognized rather than called hidden bypasses. AB.|
|46|What before release?|Fix blockers, truthful plan/evidence/result models, core flows, durability/cancellation, immutable reproducible pipeline and artifact regression matrix. AC/AD.|
|47|Remaining risks?|Exact collection crash cause;248 unexecuted commands; real privileged changes; ARM/MSIX/upgrade; accessibility; long-run/cold resource attribution; hostile/fault/power-loss boundaries. AE.|
