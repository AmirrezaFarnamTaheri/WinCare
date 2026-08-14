# Native command parity ledger

The frozen oracle preserves all stable command IDs from the historical implementation. The native
catalog has exact 259-ID parity. Catalog and implementation coverage do not imply Windows behavioral
parity.

## Summary

| State | Count |
|---|---:|
| Total stable IDs | 259 |
| Unique native catalog IDs | 259 |
| Cataloged | 259 |
| Contract verified or later | 259 |
| Implemented or later | 259 |
| Behavior verified | 0 |
| Native implementation blockers | 0 |
| Production behavior-verification blockers | 259 |

All 259 commands are registered through the same catalog-driven command runtime and native executor.
There is no generated handler directory. Read-only commands gather host evidence or durable WinCare
state; mutating commands must pass explicit parameter validation during preview before approval can be
granted. Unsupported dependencies and safety preconditions return blocked/failed outcomes and never
fall back to success.

### Important safety exception

The legacy temporary security-maintenance feature required authenticated snapshots plus automatic
timed restoration after protection was reduced. The native application does not yet have a separately
launchable recovery host that can provide that guarantee if the UI process exits. Therefore
`security-control-reduce` and its restore path are deliberately fail-closed and perform no mutation.
The former incorrect firewall substitution has been removed. This is an admitted safety boundary, not
behavior-verification evidence.

## State definitions

| State | Required evidence |
|---|---|
| `Cataloged` | Stable ID, title, summary, area, section, risk, privilege, restart, and keywords |
| `ContractVerified` | Typed request/result contract and frozen parity fixture reviewed |
| `Implemented` | Native route exists, validates its admitted inputs, and fails closed outside its supported/safe scope |
| `BehaviorVerified` | Native and historical behavior compared on supported Windows environments with matching safety and result semantics |

## Promotion rule

Production finalization requires exactly 259 `BehaviorVerified` records. An implemented route without
Windows behavior evidence remains a production blocker.

## Oracle provenance

Frozen fixtures live under `migration/oracle/`:

- `legacy-command-ids.json`
- `remediation-rules.json`
- `presets.json`
- `provenance.json`

The provenance file records the historical source commit and SHA-256 hash of authoritative inputs.
The native source archive uses non-executable fixtures. Executable historical PowerShell source is
kept only in the separate legacy-oracle archive and is not a native runtime dependency.
