# WinCare Security Policy and Security Model

WinCare performs privileged and potentially mutating Windows operations. Security claims are treated as runtime contracts: if a boundary cannot be proven, WinCare must block, degrade explicitly, or describe the capability as unavailable/experimental.

## Vulnerability reporting

Do not disclose suspected vulnerabilities through public Issues or Discussions. Use [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories/new) or contact the maintainers directly.

Include the affected version/commit, Windows build and architecture, execution privilege, reproduction steps with non-sensitive data, observed impact, and redacted diagnostics.

## Supported versions

| Version line | Supported | Notes |
|---|:---:|---|
| `2.5.x` | Yes | Current development / release-candidate line |
| `2.4.x` | Yes | Supported release-candidate baseline |
| `< 2.4.0` | No | Historical legacy releases |

## Core security invariants

### 1. Dispatcher-issued two-phase mutation authority

Observation and planning never grant mutation authority.

For a mutating command:

1. the dispatcher admits a read-only preview;
2. the preview must succeed;
3. the dispatcher issues a short-lived `ApprovedMutationPlan` bound to the exact command ID, canonical parameter digest, correlation ID, and issuance time;
4. the UI asks for explicit approval;
5. Apply succeeds only if the dispatcher can atomically consume that exact issued receipt.

Caller-created receipts, expired receipts, parameter changes, correlation changes, and replayed receipts fail closed. `request.Apply=true` plus a UI flag alone is not sufficient authority.

### 2. Fail-closed outcomes and uncertainty

Unknown/unavailable commands, invalid parameters, unsupported architectures, missing dependencies, denied elevation, unverifiable cryptographic state, traversal attempts, cancellations, timeouts, and postcondition failures do not fabricate success.

If a mutating handler faults after execution has begun and the final machine state cannot be proven, the result explicitly reports **final system state unknown** and directs the user to verify the affected resource before retrying.

### 3. Typed and bounded command input

The executor uses bounded typed `CommandParameters` readers. All Tools mirrors those contracts through `CommandParameterCatalog` and native WinUI editors; raw JSON is an Advanced escape hatch, not a bypass. The executor remains the authoritative validator.

Filesystem traversal canonicalizes roots, rejects reparse-point operation roots, and skips link/junction descendants. External processes use executable resolution, argument arrays, bounded output, cancellation, and timeouts rather than shell-concatenated command strings.

### 4. Privacy-safe activity and crash handling

User-visible command failure records do not copy raw exception messages that may contain paths, usernames, or secrets. Detailed exceptions go to protected diagnostics/debug output.

Unexpected fatal UI exceptions are logged and allowed to terminate rather than being universally marked handled and continued in an unknown process state.

### 5. Plugin trust and cryptographic admission

Plugins execute full-trust **in process** with the WinCare user's privileges. Declared capabilities are informed-consent metadata, not a technical sandbox.

Remote installation requires an independently anchored catalog boundary:

- exact catalog bytes must verify against a WinCare-pinned catalog public key and detached signature;
- the security-sensitive install step forces a fresh catalog fetch and never falls back to an arbitrarily stale cache;
- the reviewed plugin is re-resolved from that fresh catalog, so delisting or security-relevant metadata changes invalidate stale UI state;
- package ID, SHA-256, trusted publisher identity, publisher manifest signature, declared capabilities, and revocation state are rechecked at admission;
- the admitted manifest digest/signature/publisher metadata are stored in an external trust record outside the plugin directory, and discovery re-verifies the installed manifest against that admission record;
- post-install modification of both a manifest and an old colocated digest therefore does not establish trust;
- revoked package IDs and trusted PublisherIds are enforced independently of author display strings;
- reserved namespaces such as `wincare.core.*` and `system.*` cannot be replaced by plugins;
- assembly-plugin initialization is transactional: commands introduced by a failed initialization are removed by ownership, followed by shutdown/disposal/load-context cleanup;
- uninstall is confirmation-gated and restores the previous enabled state if removal fails.

**Current release boundary:** this repository does not ship an approved production catalog signing key or a live official signed catalog. `AppRuntime` therefore creates `RemoteCatalogService` without a pinned key, keeping remote installation intentionally browse-only/disabled. This is safer than inventing a trust root. A later release may enable remote installation only after an explicitly reviewed public key and signed catalog endpoint are shipped.

### 6. Encrypted profile storage

New encrypted profile envelopes use AES-256-GCM and a versioned key-derivation envelope with PBKDF2-HMAC-SHA256 at 600,000 iterations. Legacy 100,000-iteration envelopes remain decryptable for compatibility and migrate naturally when rewritten. Envelope metadata is bounded before accepting a work factor, and AES-GCM authentication detects tampering or a wrong passphrase.

### 7. Rust FFI safety

Rust C-ABI exports use caller-owned buffers/pointer-length pairs, validate boundaries, and contain panics with `std::panic::catch_unwind`; Rust panics must not unwind into .NET.

### 8. Guard IPC boundary

`wincare-guard` is currently an **experimental local daemon**, not a production SCM-installed service. Its Windows named pipe uses an explicit DACL instead of the permissive default named-pipe ACL. Production service lifecycle, service identity/authorization policy, and complete app/native notification consumption remain promotion requirements and are not claimed as finished.

## Trust boundaries

| Boundary | Security expectation |
|---|---|
| User input | Bounded typed validation; advanced JSON remains untrusted |
| WinUI presentation | Cannot mint mutation authority or bypass dispatcher policy |
| Command dispatcher | Authoritative admission and one-time preview-receipt issuer |
| Native executor | Final typed validation and bounded Windows interaction |
| Plugin catalog | Browse-only unless exact bytes verify against a configured pinned root |
| Plugin runtime | Full trust; capability declarations inform consent but do not sandbox |
| Filesystem | Canonical containment, reparse rejection, bounded enumeration |
| External process | System executable resolution, discrete args, timeout/cancel/output bounds |
| Rust C ABI | Versioned interface, validated caller buffers, unwind containment |
| Profile encryption | Versioned AES-GCM envelope and bounded PBKDF2-HMAC-SHA256 work factor |
| Guard IPC | Local explicit DACL; production service authorization still pending |

## Deliberately excluded behavior

WinCare does not intentionally ship covert analytics, unrestricted remote script execution, kernel hooks/hidden drivers, signing bypasses, silent security-setting mutation, generic rollback claims for operations without a real compensator, or publisher-verification claims without an anchored catalog trust root.
