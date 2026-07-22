# Safety model

## Risk levels

- **ReadOnly:** inventory and diagnostics with no intended state mutation.
- **Low:** narrow reversible user settings or rebuildable local state.
- **Moderate:** application operations, startup state, file moves, cache removal, scans and user-level controls.
- **High:** application reset, provisioning, broad system settings, update installation and WDAC deployment.
- **Critical:** update removal, WDAC removal, UAC reduction, injection-surface quarantine, page-file disabling, and similarly authority-bearing operations with material recovery risk.

Risk cannot be lowered below the action contract or policy floor. High/Critical plans require stronger typed confirmation. Read-only mode blocks every mutation and cannot be disabled from within a command-line locked session.

## Filesystem boundaries

- canonical path normalization and explicit allowed roots;
- no destination overwrite;
- reparse-point/junction/symlink rejection on mutation paths;
- expected source identity/hash checks immediately before action;
- exact per-file backup/quarantine manifests and post-copy hashes;
- bounded file count/size/depth and rollback on partial restore;
- personal folders are never selected by generic cleanup rules.

## Application and package boundaries

- exact MSI product-code removal and exact WinGet identities;
- Windows command-line tokenization for registered uninstallers;
- `cmd`, PowerShell, WSH, MSHTA and other generic script/shell hosts are rejected;
- AppX profiles use exact package names, not wildcard removal;
- repair is separate from reset/uninstall;
- reset and uninstall disclose non-recoverable or partial-recovery behavior.

## System-control boundaries

- WDAC mutation requires CiTool; direct policy-directory writes are forbidden;
- WDAC guidance is audit-first and policy hashes are verified;
- WUA actions use exact update identities, bounded item counts and cancellation points;
- BCD is inspected/exported only; WinCare does not replace boot loaders or write raw EFI/NTFS structures;
- documented Windows controls replace timer, RAM, kernel and broad service “optimization” hacks;
- page-file changes require crash-dump compatibility, fixed-volume and free-space validation, exact preview hashes, restart disclosure, and executable restoration;
- Defender, UAC, SmartScreen, and Windows Update reductions exist only as default-denied, reasoned, expiring maintenance records with exact restoration; Firewall, Secure Boot, WDAC, and Tamper Protection are never bypassed by that workflow;
- TCP settings are changed one at a time only after a bounded baseline, and rejected/cancelled experiments restore the exact prior token;
- process observation uses supported ETW/WPR, module inventory, and event evidence; arbitrary process injection, remote memory writes, remote threads, and global hooks are denied.

## Privilege and protocol boundaries

- one typed action per UAC request;
- HMAC authentication, fixed-time verification, expiry, nonce/replay protection and full code/policy binding;
- canonical result/state path confinement and authenticated result envelopes;
- local broker is current-user, read-only, allowlisted, authenticated, ordered and bounded;
- extensions are non-executable and admitted only through strict manifests.

## Recovery boundaries

A recovery option is shown only when the exact compensator and required source state exist. Schema-v4 journals use canonical, order-independent hashing for actions, plans, events, final records and schema-v2 protected receipts. Terminal journals without a valid schema-v2 receipt are never eligible for automatic undo. WinCare 1.x/2.x journals and earlier receipt schemas remain visible as historical evidence but are intentionally not trusted for automated recovery. Interrupted active journals are reconciled through a new hash-chained event and authenticated terminal receipt. Undo never claims to reverse vendor uninstaller behavior, Windows servicing, malware remediation or other operations without a deterministic reverse path.

See `docs/convergence/threat-model.md` for the full trust-boundary analysis.
- Security-control and process-interception recovery is bound to authenticated provider records; caller-supplied snapshots cannot authorize privileged restoration.
- Expiring security maintenance uses a durable Prepared → Armed → Active → Restored lifecycle; interrupted armed records are reconciled by the authenticated recovery task.

## Productivity-state boundaries

- Power sessions require a typed plan, bounded refresh cadence, policy-approved duration/mode, durable revisioned record, exact host identity, heartbeat, expiry reconciliation, and a runtime-issued stop compensator.
- Window plans use normalized monitor work-area ratios but execute only after exact live-window compare-and-swap; handle reuse, process replacement, title/bounds/topmost drift, or monitor changes block mutation.
- Local input recovery releases only known modifier keys and mouse buttons; no global keyboard hook, capture stream, or replay facility is installed.
- Color capture is one-shot and local. Image metadata rejects unsupported types, files larger than 256 MiB, excessive dimensions/pixel counts, and unbounded frame-delay output.
- Corrupt palette or remote-consent stores may degrade to empty read-only views but cannot be overwritten by a mutation plan.
- Private note bodies are visible only through explicit local access/search and are omitted from normal reports.
- Remote-support consent does not confer network or process authority; it is an auditable local governance record. Process termination requires separate policy plus exact identity.
- Browser workspace observations are read-only and bounded; no extension installation, debugging endpoint, cookie/history content collection, or automatic tab enumeration is performed.

## Operations-plane safeguards

- Download headers reject authorization, proxy authorization, cookies, CR/LF injection, excessive names/values, and unbounded maps. HTTP is policy-gated; HTTPS is the default. Destination, partial, staging, and tombstone paths are root-confined and reparse-free.
- The effective download byte ceiling is the lower of user configuration and organizational policy. Concurrent starts are bounded before BITS admission. Existing destination data is never deleted before a verified tombstone is available.
- Telemetry does not persist raw private counters, command lines, environment variables, file contents, or network destinations. Collection and history are bounded and cancellable.
- Launch targets are reconstructed from the current local index and must match the previewed kind, exact target, and hash. Arbitrary command text is not accepted.
- Steam restore rejects unlisted ZIP entries and refuses to run while Steam is active. A verified rollback archive is created before mutation.
- Offline profile entries cannot contain wildcards. `ResetBase` is denied. Provisioned-package and component cleanup actions are labeled non-reversible.
- Layout title patterns compile with a 100 ms timeout; action authority remains exact current HWND/process/start-time/title/monitor/geometry state.
- Trainer, injector, cheat, anti-cheat-bypass, and bundled third-party executable surfaces are never invoked.

## Legacy Unsafe profiles

The Legacy Unsafe workspace is an explicit exception to WinCare's normal conservative optimization posture. It includes real permanent or destructive mutations because the operator requested them. Profiles remain target-native typed plans and require default-deny policy opt-ins, exact acknowledgement, Critical confirmation, preview receipts, elevation where required, journaling, and post-state verification.

Potential effects include reduced malware and reputation protection, suppressed updates, disabled firewall profiles, disabled services and scheduled tasks, telemetry hosts entries, removal of Microsoft Store and other AppX recovery paths, removal of `hiberfil.sys`, and deletion of display override/cache registry state. Recovery is best-effort and conflict-aware; it is not promised for irreversible package removal or lost hibernation contents. Use disposable VMs for first execution.

## Experience Studio safety

Visual inspection, capability cards, download strategy, and power assessment are read-only. Location, remote access, power, and privacy changes are typed plans over existing policy and recovery owners. No donor graphics editor, shell-mod host, downloader daemon, hook engine, IPC service, or generated script is executed. `rytunex-maximum` and `privacy-sexy-maximum` remain Critical and default-deny.

## Cleaner and preview guardrails

- No drive-root wildcard traversal, Prefetch purge, restore-point deletion, Defender evidence deletion, Installer-patch deletion, or automatic process termination.
- No Winapp2 registry directives, scripts, exclusions, arbitrary recursion, or unapproved roots.
- No arbitrary relocation paths; only built-in profiles, unchanged content hashes, closed processes, exact junction targets, and conflict-aware recovery.
- No remote paths, active web rendering, global keyboard hooks, Explorer injection, plug-in loading, COM handler activation, or preview-triggered execution.
