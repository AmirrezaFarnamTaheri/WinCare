# WinCare 5.2 converged architecture


## Wave eleven: D164-D166 Cleaner & Preview Studio

The eleventh convergence wave adds WindowsCleaner as D164, records the byte-identical Windows Community Toolkit resubmission as D165 linked to D143, and adds QuickLook as D166. The cumulative evidence graph contains **166 submitted donor IDs**, **153 unique archive baselines**, **52,629 classified surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**.

Target-native additions are disk-pressure assessment and scheduling over canonical cleanup targets, bounded Winapp2 FileKey admission with exact deletion manifests, verified application-data relocation with exact junction recovery, local non-executing file preview, and preview-handler assessment. All three new mutation paths remain inside the existing typed plan, policy, receipt, journal, and compensation architecture. Donor source, binaries, plug-ins, global hooks, active renderers, and updater runtimes are not bundled.

## Wave seven: D106-D109 cleanup and AppX convergence

Wave seven adds one bounded cleanup/AppX provider and extends the existing Legacy Unsafe composer. It introduces reversible Explorer preferences, bounded cleanup profiles, exact AppX selection and mounted-image plans, coordinated Windows Update cache cleanup, and supported component cleanup. Three new one-click profiles preserve requested destructive behavior through the existing Critical policy, preview, UAC, journal, and recovery path. No donor script runner or alternate authority path is introduced.

## Wave six: D87-D105 Workspace and Device Studio

Wave six adds one bounded Studio provider and screen over existing authority. New durable state is confined to the WorkspaceStudio root. New mutations use three typed contracts (`SetFolderAppearance`, `RestoreFolderAppearance`, and `RunVerifiedPeerTool`); all other Studio capabilities are read-only or generate existing target-native plans. Exact-hash tool admission is the only external-executable boundary. The Xbox profile is composed from that boundary plus Critical Legacy Unsafe policy, exact acknowledgement, journaling, and recovery metadata.


## Wave five: D70-D86 peer utilities and Legacy Unsafe

The fifth wave adds 17 donors and 1,299 fully classified file surfaces. Material value is decomposed into display override inventory/reset/recovery, WLAN survey, bounded log inspection and regex filtering, non-executing PE metadata, strict container log configuration, local task-board state, and donor-derived one-click mutation presets. Existing WinCare window, browser, workspace-layout, telemetry, and TUI mechanisms supersede donor-specific resident applications where they already provide the outcome.

Unsafe donor behavior is retained as executable target-native behavior rather than silently rejected: permanent security reductions, firewall shutdown, broad service/task suppression, hosts blocking, aggressive AppX removal, hibernation disablement, privacy/performance rules, and display override/cache reset. The composition preserves one authoritative action path, default-deny policy gates, exact acknowledgement, preview receipts, UAC brokering, journaling, post-state verification, and conflict-aware recovery where recovery exists. Raw donor scripts and binaries remain non-authoritative evidence.
## Baseline and scope

WinCare 5.2 is an additive convergence release over the frozen WinCare 5.1 source commit `75a96f8768605ea2bbcecc2fa5d0c06a2dbfae9d`. It preserves the prior authority, policy, transaction, recovery, GUI, shell/hardware, extension, security, servicing, productivity, and release planes and extends the cumulative convergence graph through the eleventh donor wave, D164-D166.

The cumulative system accounts for **166 submitted donor IDs**, **153 unique archive baselines**, **52,629 donor surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**. Donor packaging is not authoritative. Useful mechanisms are decomposed and recomposed into target-native providers that share one versioned action engine.

## Architectural invariants

1. **One authoritative mutation path.** TUI, headless, automation, playbooks, recovery, and elevation resolve to the same 110 typed actions and dispatcher.
2. **Plan-before-authority.** A canonical plan is risk-classified, policy-checked, previewed, and bound to a short-lived receipt before mutation.
3. **Narrow privilege.** Administrative actions cross an authenticated, expiring, replay-protected UAC envelope bound to action identity, module tree, helper, policy, state root, and result path.
4. **Executable recovery only.** Reversibility is advertised only when a static or provider-issued compensator validates against the same action schema.
5. **Fail-closed persistence.** Durable records are schema/version bounded, canonicalized, integrity protected, compare-and-swap updated, atomically replaced, and rejected when malformed.
6. **No shell authority.** External tools receive separated executable/argument tokens. Donor command strings, scripts, macros, trainers, or plug-in code never become authority merely by import.
7. **Local evidence.** Telemetry, histories, reports, download records, Steam manifests, and layout records remain local and bounded.

## Responsibility planes

### 1. Verified download orchestration

The donor queue, mirror, scheduler, checksum, progress, and retry mechanisms from Motrix, AB Download Manager, KGet, QDM, Risuko, and Trauma are recomposed behind Windows BITS and WinCare state.

Public contract:

- exact HTTP/HTTPS URL admission, HTTPS required unless policy allows otherwise;
- one to eight mirrors, bounded non-sensitive headers, bounded retry/schedule/priority fields;
- protected queue records with explicit lifecycle and current BITS identity;
- configured/policy-intersected byte and concurrency limits;
- SHA-256 or SHA-512 verification;
- pause, resume, reconcile, cancel, due-job discovery, and scheduled-start plans;
- same-directory staging, flush, tombstone replacement, post-hash verification, and rollback-safe finalization.

The BITS job is not the source of truth by itself. WinCare owns the durable intent, expected output, integrity contract, and journal. Browser-originated links are merely candidate URLs and never authorize transfer.

### 2. Operations telemetry

The dense dashboard and monitoring patterns from eDEX-UI, LiteMonitor, and Tabame become bounded local observations:

- CPU, memory, physical-disk, network, process, thread, and handle summaries;
- top-process evidence with explicit limits;
- configurable CPU and memory alerts;
- bounded local history and local-only export;
- cancellation and per-provider degradation;
- public projection that removes private helper fields.

Telemetry has no network listener, upload, or remote analytics path. “Memory cleaning” surfaces are converted to telemetry and anti-folklore guidance rather than working-set purge authority.

### 3. Unified launcher intelligence

Pinpoint, Launcher, WinKey palette, Tabame, and eDEX fuzzy-search ideas converge into a local exact-target launcher:

- bounded Start-menu traversal with reparse rejection;
- App Paths, Control Panel, and allowlisted Settings URI discovery;
- normalized weighted search and usage evidence;
- exact target/path/hash checks before execution;
- a non-evaluating arithmetic parser;
- no arbitrary command line, shell expansion, remote plug-in, database-backed secret, or global Win-key replacement.

### 4. Steam and game-state protection

SteamCloudFileManager and the game-related donors contribute state discovery, exact manifests, backup/restore UX, conflict awareness, and defensive findings:

- Steam library/user/AppID/local-cloud root discovery;
- bounded file inventory and canonical snapshot hash;
- exact ZIP manifests, duplicate/unlisted/traversal rejection, and per-file hashes;
- live Steam-process exclusion before restore;
- pre-restore rollback archive and provider-internal restoration on failure;
- defensive trainer/injector filename/process/signature findings.

Cheat execution, memory writing, remote threads, trainer loading, anti-cheat bypass, and credential/session theft are categorically outside the action vocabulary.

### 5. Offline image reduction

nano11 evidence is transformed into three strict profiles using already governed offline servicing:

- `nano-safe`;
- `developer-lean`;
- `component-cleanup`.

Profiles contain exact provisioned-package/feature identities, supported architecture/build constraints, risk and reversibility metadata, and explicit servicing classifications. Wildcards, raw donor scripts, unsupported registry tricks, ResetBase, bootloader mutation, and hidden irreversible work are rejected.

### 6. Multi-window layouts

SnapZones, Pinpoint, Tabame, and existing window-workspace mechanisms converge into revisioned reusable layouts:

- normalized monitor-relative slots;
- ratio and collision validation;
- bounded layout count;
- regex compilation with timeout;
- monitor work-area binding;
- exact HWND/process/start-time/title matching;
- per-window geometry compensation and restore verification.

Global input daemons and shell replacement are not imported. Layout application remains an explicit local operation.

## Cross-plane contracts

All new providers inherit:

- schema/version fields;
- operation and action IDs;
- canonical hashes and idempotency keys;
- policy risk floors;
- cancellation and timeouts;
- structured results and warnings;
- journal references and compensators;
- bounded lists, files, bytes, history, recursion, and concurrency;
- redacted report projections;
- explicit capability degradation on non-Windows or missing optional components.

## Persistence ownership

- Downloads: protected queue and BITS identity under the WinCare state root.
- Telemetry: bounded local history and explicit exports.
- Launcher: bounded local usage history; installed-system surfaces remain external read-only sources.
- Steam: exact backup/rollback archives plus protected operation journals.
- Offline reduction: immutable built-in profile definitions; DISM is the external servicing executor.
- Layouts: revisioned protected local layout store; HWND/monitor state is revalidated immediately before mutation.

## Language ownership

PowerShell remains the authoritative implementation language because WinCare already owns Windows APIs, policy, transactions, UAC, TUI, packaging, and recovery there. Donor JavaScript, TypeScript, Rust, C#, C++, Java/Kotlin, and shell implementations are evidence and mechanism references. No new language runtime is introduced solely to imitate donor packaging.

## Wave-seven architecture extension

The cleanup/AppX plane adds `108-CleanupMods.ps1` without changing authority ownership. Read-only inventory and selection produce typed plans; current/all-user, provisioned, and mounted-image mutation reuse existing policy, transaction, UAC, journal, and offline-servicing owners. `106-PeerUtilities.ps1` performs many-to-one composition for the new one-click profiles. No donor interpreter, script runner, service, hook, or background process is introduced.

## Wave eight system-toolkit plane

`105-SystemToolkit.ps1` is an additive adapter plane. It does not own privileged execution, durable maintenance state, the download queue, window state, update state, or registry mutation. It emits existing typed actions and delegates persistence to the established maintenance, download, catalog, and transaction providers. `106-PeerUtilities.ps1` composes the new hardening plans only for explicitly selected one-click profiles.
