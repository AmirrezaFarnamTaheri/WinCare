# Security policy

WinCare performs consequential local Windows management. Reports should name the affected version and typed action, explain the trust boundary, provide minimal reproduction steps, and include only redacted evidence. Never submit credentials, local integrity keys, personal file contents, unredacted reports, complete inventories, or private policy backups.

## Required security invariants

- one typed, policy-gated mutation dispatcher for TUI, headless, automation, playbooks, extensions, recovery, and elevation;
- canonical exact-plan preview receipts before mutation and explicit `-Apply` for headless changes;
- action-contract risk floors, bounded inputs/runtime/output, and fail-closed schemas;
- exact native argument boundaries and rejection of shell/script-host uninstall paths;
- canonical path confinement, approved roots, reparse rejection, expected hashes, and collision-safe behavior;
- bounded Registry-tree capture/restore and exact prior-state compensators;
- staged, flush-verified, hash-verified file replacement with internal rollback on failed commit verification;
- authenticated, expiring, replay-protected UAC requests/results bound to action, operation, policy, helper, full module tree, state root, and result path;
- strict data-only extension/playbook/catalog admission; no executable donor content or arbitrary embedded commands;
- hash-chained journals, authenticated terminal receipts, validated compensators, and integrity-gated undo;
- CiTool-only WDAC deployment; Microsoft-signature/hash validation for LGPO and Sysmon tools;
- pre-mutation LGPO capture and provider-internal restoration on failed import/refresh;
- healthy read-write mounted-image requirement and explicit non-recoverable servicing disclosures;
- local-only reports/metrics and permanently disabled telemetry;
- deterministic release manifest, SBOM, receipt, provenance, and independent archive verification.

## Authority-reduction boundary

WinCare must not add permanent, generic, hidden, or optimization-branded Defender, SmartScreen, UAC, Firewall, Secure Boot, Windows Update, or WDAC disabling. Defender real-time protection, shell SmartScreen, UAC, and the Windows Update service stack may be reduced only through the default-denied expiring maintenance protocol: exact authenticated pre-state, explicit reason/environment, strict duration policy, no Tamper Protection bypass, scheduled restoration, manual recovery, compare-before-restore, and restart disclosure where applicable. Firewall, Secure Boot, WDAC, and Tamper Protection have no corresponding reduction path. It must not add wildcard AppX removal, raw EFI/bootloader mutation, global code injection, Explorer binary patching, blind Registry imports, fake RAM cleaners, unmeasured pagefile or TCP folklore, timer hacks, arbitrary process injection, arbitrary elevated commands, plaintext secret transport, unauthenticated IPC, executable extensions, or arbitrary playbook scripts.

## Reporting vulnerabilities

Include the exact action/plan schema, affected target, expected versus observed authority, and whether the issue crosses user, administrator, filesystem, Registry, process, extension, package, or recovery boundaries. Redact usernames, machine identifiers, and private paths where possible.

## Productivity and remote-support boundaries

- power requests are owned by a dedicated integrity-checked host and revisioned record; indefinite and away-mode sessions are independently default-denied by policy;
- window mutation is bound to exact HWND, process, process-start, title hash, current bounds/topmost state, and current monitor geometry;
- pixel capture is one-shot and stores no screenshot; palette stores are strict, protected, bounded, revisioned, and fail closed for mutation when corrupt;
- local notes separate Markdown bodies from metadata, bind recovery snapshots by hash, redact private bodies from ordinary views/reports, and never sync remotely;
- remote-support discovery collects configuration key names but not values, credentials, tokens, device IDs, or relay secrets;
- remote-support consent is local, expiring, closed-state, and cannot initiate a session; emergency termination applies only to exact inventoried allowlisted processes and is default-denied;
- browser inventory does not enable debugging, install extensions, or claim exact tab counts; existing extension manifests are bounded and risk-classified;
- command aliases remain JSON-only declarations of existing approved routes; fuzzy rank and workspace visibility never grant authority.

## WinCare 4.5 operations threat boundaries

- **Network input:** URLs and headers are data, never commands. Sensitive durable headers, non-HTTP schemes, newline injection, oversized jobs, unapproved HTTP, and destinations outside approved roots are rejected.
- **Transfer-to-filesystem commit:** BITS partial output is untrusted until size/hash verification. Finalization uses same-directory staging and a destination tombstone; path reparse points and directories are rejected.
- **Telemetry privacy:** raw cumulative counters and process objects are transient. Durable/report samples omit private helper fields and never collect process command lines or network endpoints.
- **Launcher authority:** search score is presentation only. Apply re-resolves the target and verifies exact identity before invoking only supported target kinds.
- **Game archives:** ZIP paths, duplicates, hidden/unlisted members, sizes, hashes, roots, Steam process state, and final snapshots are checked. Donor cheats, trainers, injectors, bundled DLLs, and elevation helpers are not admitted.
- **Mounted images:** only a healthy mounted read-write image and exact package/feature identities may be serviced. Irreversible actions are disclosed and never represented as rollback-capable.
- **Window matching:** regex execution is time-bounded and window mutation requires exact process/start/title/monitor/geometry evidence.

## Workspace and Device Studio boundary

- Package archives are metadata-only inputs. Traversal, absolute paths, duplicate/case collisions, excessive members, and excessive expanded size fail closed; no archive is extracted or executed.
- Verified peer tools require an exact SHA-256, explicit tool identity, strict argument validation, no shell, bounded execution, and expected exit codes. Discovery never grants execution authority.
- Kanata configuration is validated as data and cannot start a global input hook. Syncthing inspection reads only local state and redacts API keys. Monitoring exports contain no listener, remote sink, or credentials.
- Folder appearance mutation snapshots exact desktop.ini bytes and attributes; recovery rejects conflicts instead of overwriting newer state.
- Xbox full-screen enablement is a Critical Legacy Unsafe mutation. It additionally requires `AllowXboxFullscreenExperience`, exact ViVeTool admission, exact acknowledgement, and a disclosed reboot. No donor driver, service, test-signing helper, or opaque script is shipped.

## Legacy Unsafe mutation boundary

WinCare 5.2 intentionally includes high-impact donor-derived one-click mutations. They are not reachable through ordinary safe profiles. The effective policy defaults `AllowLegacyUnsafeProfiles`, `AllowPermanentSecurityReduction`, and `AllowDisplayOverrideReset` to `false`, while existing narrower gates continue to govern Critical actions, UAC, Windows Update, firewall, hosts, AppX, services/tasks, and related boundaries.

Interactive execution requires the exact acknowledgement `I ACCEPT LEGACY UNSAFE MUTATIONS` and the normal Critical confirmation. Headless execution requires the exact acknowledgement and explicit apply. Plans are preview-hash bound, cross UAC only as typed allowlisted actions, and are journaled and post-state checked. Exact snapshots and conflict-aware compensators are used where possible. Broad AppX removal, deleted hibernation-file contents, and some permanent security reductions can lack complete recovery; the preview and receipt disclose that boundary.

Raw donor scripts, DLLs, EXEs, browser extensions, hooks, and plugin loaders are evidence only and are not shipped or executed.

## WinCare 4.7 cleanup and AppX boundaries

- Cleanup mutation is confined to named canonical targets. Reparse points, drive-root wildcard sweeps, Event Log clearing, Prefetch purge, Defender-history deletion, and generic log-extension recursion are not admitted.
- Windows Update download-cache cleanup captures the pre-state of BITS and `wuauserv`, coordinates stop/delete/restart, and reports irreversibility; it does not claim deleted payload recovery.
- Component-store cleanup uses supported servicing without `ResetBase`; update uninstallability is not intentionally destroyed.
- Imported AppX lists are regular non-reparse `.txt`/`.list` files no larger than 64 KiB, containing 1–200 unique validated wildcard patterns. Patterns select inventory only; exact package identities reach mutation actions.
- All-user and provisioned-package removal are administrator mutations. Offline removal requires an already-mounted healthy read-write image and does not implicitly mount, commit, unmount, or guess a WIM path.
- `windows-cleaner-utility-all`, `remove-ms-store-apps-all`, and `windows-repo-nuclear` are Critical Legacy Unsafe profiles. They require default-denied policy opt-ins, exact preview binding, explicit apply, UAC where necessary, and `I ACCEPT LEGACY UNSAFE MUTATIONS`.
- Broad AppX/feature/capability removal and cleanup can be partially or wholly irreversible. Recovery may require Store/Windows servicing, installation media, a restore point, image backup, or OS repair/reinstallation; WinCare does not represent a compensator where none exists.

## WinCare 4.7 toolkit boundaries

Hardening profiles are explicit typed registry and service actions with captured prior state. Hail Mary is classified Critical because remote management, legacy authentication, SMB, and credential workflows may break. Batch-download definitions are regular JSON files capped at 256 KiB and 100 jobs; every job passes the existing URL, destination, hash, retry, schedule, and protected-store validators. Torrent support is parser-only: a regular local file, at most 16 MiB, nesting depth 16, bounded dictionary/file counts, and no network execution. Donor elevation binaries, cached broad elevation, raw `.reg` bulk import, downloader engines, and torrent networking are not admitted.

## WinCare 5.2 Experience Studio boundaries

- Visual-asset inspection is metadata-only. Palette values are bounded to 256 entries, sprite dimensions and frame counts are bounded, and source images are never executed or rewritten by inspection.
- Location privacy changes use explicit machine policy and user-consent values. Service state is reported separately and is not silently disabled as a side effect of permission denial.
- Remote-access plans admit only exact RDP/OpenSSH capabilities, service identities, capability names, Registry values, and firewall rules. They do not create users, passwords, KMS jobs, arbitrary firewall openings, or generic remote-command authority.
- Power recommendations are advisory until converted into existing typed power-scheme and brightness actions. No donor hook, driver, named pipe, resident tray process, or arbitrary executable is admitted.
- Download-strategy inspection documents the existing verified BITS path. Surge's daemon, remote listener, and parallel chunk engine are not imported.
- Privacy profiles resolve only known WinCare catalog-rule IDs. Raw donor command bodies and generated shell scripts are never evaluated.
- `rytunex-maximum` and `privacy-sexy-maximum` are Critical Legacy Unsafe profiles. They require applicable policy opt-ins, exact preview binding, explicit apply, UAC where necessary, and `I ACCEPT LEGACY UNSAFE MUTATIONS`; some resulting security, update, service, task, AppX, privacy, and cleanup changes may be incompletely recoverable.
- Five repeat submissions are provenance-only. Duplicate archives cannot grant new authority or create a second implementation path.

## 5.2 cleaner and preview security boundary

Winapp2 input is data, not code: only `FileKeyN=path|pattern` is accepted, paths must resolve beneath approved roots, patterns cannot contain separators or drive syntax, and the exact catalog plus each selected file is hash-bound. All files are verified before mutation and again immediately before deletion. Relocation is restricted to built-in profiles, refuses running applications, verifies the complete source/copy/destination hash and exact junction target, and blocks recovery if state changed. Preview accepts local non-reparse paths only, bounds bytes/lines/members, prohibits XML DTDs, marks active formats, does not load COM handlers or plug-ins, and excludes text unless explicitly requested.
