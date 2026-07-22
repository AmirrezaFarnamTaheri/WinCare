# WinCare 5.2 threat model


## Wave eleven: D164-D166 threats and controls

The eleventh convergence wave adds D164 WindowsCleaner, D165 as a duplicate of D143, and D166 QuickLook. The cumulative evidence graph contains **166 submitted donor IDs**, **153 unique archive baselines**, **52,629 classified surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**.

Primary added threats are malicious Winapp2 paths and patterns, deletion time-of-check/time-of-use races, reparse escape, content changes after preview, relocation into or beneath the source, process restart during relocation, stale or substituted junction targets, partial copy/commit failure, secret-bearing previews, archive bombs, XML entities, active HTML/Markdown/Office rendering, remote content, COM handler loading, and global keyboard hooks. Controls are canonical approved roots, strict grammar and limits, immediate content-hash revalidation, exact target verification, staged copy and rollback, local-only non-reparse paths, bounded bytes/lines/members, redaction, DTD prohibition, metadata-only active-format handling, and assessment-only handler inventory.

## Wave-six threats and controls

Package bombs/traversal, exact-tool substitution, shell injection, broad ADB mutation, Kanata hook activation, Syncthing secret disclosure, folder-state clobbering, and unconsented Xbox feature mutation are the principal added threats. Controls are bounded metadata-only archive inspection, exact SHA-256 admission, strict no-shell arguments, separate mutation gates, data-only configuration validation, secret redaction, conflict-aware byte/attribute recovery, Critical policy, exact acknowledgement, UAC brokering, and reboot/irreversibility disclosure.

## Security objective

WinCare may inspect broad Windows state but may mutate only through narrowly typed, preview-bound, policy-authorized operations whose targets, bounds, verification, and recovery are explicit. Donor artifacts are untrusted evidence; their scripts, binaries, browser extensions, RPC servers, trainers, and plug-ins never gain authority by inclusion.

## Assets

- Windows configuration, applications, services, packages, mounted images, and user data;
- download destinations and expected content hashes;
- Steam local cloud-cache files and backups;
- window positions/topmost state and monitor topology;
- WinCare policy, configuration, journals, receipts, replay records, quarantine, reports, and evidence;
- private paths, process names, browser/profile metadata, and telemetry history.

## Actors and boundaries

1. Local standard user and administrator.
2. TUI/headless/automation/playbook callers.
3. Authenticated elevated helper.
4. Windows BITS, DISM, WUA, Registry, CIM, AppX, and process/window APIs.
5. Remote download origins and mirrors.
6. Imported extension/profile/playbook/download/Steam archives.
7. Donor source, binaries, scripts, browser extensions, and generated data.
8. Read-only broker clients.
9. Installer, updater, package, and release artifacts.

## Fourth-wave threats and controls

### Download orchestration

Threats: URL downgrade, credential/header leakage, malicious mirrors, oversized content, queue flooding, job-ID substitution, stale destination, partial replacement, checksum confusion, unbounded retry, and browser-extension authority.

Controls: HTTPS default, policy-gated HTTP, sensitive-header denial, strict URL/mirror counts, byte/concurrency/retry limits, protected queue records, BITS job identity, exact checksum algorithm/value, cancellation, same-directory staging, tombstone rollback, post-hash verification, and explicit link admission.

### Telemetry

Threats: permanent surveillance, high-frequency resource exhaustion, private helper leakage, remote exfiltration, unbounded history, and working-set “cleaner” mutation disguised as monitoring.

Controls: on-demand bounded capture, local history limit, provider degradation, cancellation, public projection, local-only export, no listener/upload, and no memory purge action.

### Launcher

Threats: shell/argument injection, arbitrary URI execution, stale shortcut target, path substitution, remote provider authority, secret-bearing plug-ins, and global-key hijack.

Controls: exact target kinds, canonical paths, hash recheck, allowlisted Settings URIs, bounded local index, non-evaluating arithmetic grammar, data-only aliases, no shell evaluation, no remote plug-in execution, and no OS key replacement.

### Steam/game state

Threats: archive traversal, hidden entries, duplicate paths, oversized files, forged hashes, restore while Steam is active, stale target state, partial overwrite, malicious trainers/binaries, and anti-cheat bypass.

Controls: safe roots, exact membership, duplicate/traversal rejection, file/byte bounds, SHA-256 per file, live-process exclusion, expected snapshot hash, pre-operation rollback ZIP, provider-internal restore, signature/hash findings, and categorical denial of injection/cheat actions.

### Offline reduction

Threats: wildcards, wrong image, read-only/unhealthy mount, removing protected/required components, unsupported build/profile, hidden irreversibility, and partial servicing.

Controls: strict immutable profiles, exact package/feature names, build/architecture checks, healthy read-write mount, policy gate, servicing verification, protected component constraints, no ResetBase, explicit non-reversible classification, and existing offline provider rollback where technically possible.

### Window layouts

Threats: catastrophic regex, matching the wrong process/window, stale HWND reuse, changed monitor topology/work area, off-screen geometry, collision, partial multi-window apply, and global input hooks.

Controls: regex timeout, bounded rules, exact process/start-time/title/handle evidence, monitor/work-area hash, normalized ratios, collision validation, per-window compensators, reverse-order rollback, and explicit invocation without hook daemons.

## Cross-cutting threats

- Replay: nonce/idempotency/preview receipts and journal state.
- Tampering: canonical hashes, HMAC-protected records, exact manifests, signatures where configured.
- Path escape: canonicalization, approved roots, reparse rejection, archive admission.
- Privilege escalation: allowlisted typed actions and authenticated UAC broker.
- Partial failure: provider-internal compensation plus transaction rollback.
- Secret leakage: sensitive header rejection, report redaction, data-only extension admission.
- Resource exhaustion: explicit bounds for bytes, entries, files, depth, retries, queues, history, regex time, and operation timeouts.
- Supply chain: deterministic package, exact internal manifest, SBOM, checksums, release receipt, and provenance.

## Residual risks

- BITS and DISM have native behaviors that require Windows integration testing.
- Network origins can serve malicious but correctly hashed content if the user trusts the wrong hash; WinCare verifies identity, not intent.
- Steam local-cache semantics vary by game; backups protect local state but cannot arbitrate remote Steam Cloud conflicts.
- Window handles and applications may change immediately after verification; compare-and-swap narrows but cannot eliminate all GUI races.
- Performance counters may be unavailable or localized; providers must degrade rather than fabricate values.

## Legacy Unsafe authority and recovery boundary

The operator may intentionally authorize permanent security reductions and destructive one-click profiles. The threat is accidental, stale, imported, or lower-authority activation. Controls are default-deny organization policy, exact acknowledgement, Critical confirmation, canonical preview hash, narrow typed UAC envelopes, before-state hashes, durable journals, post-state verification, and conflict-aware compensators.

The system does not claim complete recovery for broad AppX/provisioned-package removal, deleted hibernation-file contents, or all permanent security changes. The negative invariant is that lack of recovery is disclosed before execution and never represented as reversible. Raw donor scripts, binaries, hooks, extensions, and plugin loaders cannot authorize execution.

## Wave-seven threat boundaries

The dominant new risks are evidence destruction, broad package removal, servicing irreversibility, stale inventory, and selection-language injection. Mitigations are bounded canonical cleanup targets, fixed service coordination, exact package/feature/capability resolution, strict list grammar and byte/count limits, framework/resource/non-removable exclusions, healthy read-write mounted-image checks, Critical policy/acknowledgement gates, preview-bound plans, and explicit non-recovery disclosures.

## Wave eight trust boundaries

- MSI inspection opens a local regular `.msi` or `.msp` database read-only and does not install or repair it.
- Download batches are local regular JSON files capped at 256 KiB and 100 jobs; each entry is revalidated by the existing download record constructor.
- Torrent files are local regular files capped at 16 MiB, nesting depth 16, 4,096 dictionary entries, and 10,000 reported files; networking is absent.
- Hail Mary hardening and the three new unsafe profiles remain behind legacy-unsafe policy, Critical action policy, preview, exact acknowledgement, journaling, and recovery receipts.
- System-shortcut discovery is data-only; discovery does not imply execution authority.

## Wave 11 threats and controls

Primary threats are malicious Winapp2 paths/patterns, manifest races, symlink/reparse escape, deletion TOCTOU, relocation into or beneath the source, process restart after preview, stale junction targets, partial copy/commit failure, secret-bearing text preview, archive bombs, XML entities, active HTML/Markdown/Office rendering, COM handler loading, remote content, and global keyboard hooks. Controls are canonical approved roots, strict grammar and limits, complete and immediate revalidation, content-hash CAS, process checks, exact target verification, staged copy and internal rollback, local-only non-reparse paths, byte/line/member bounds, redaction, DTD prohibition, metadata-only active-format handling, and assessment-only handler inventory.
