# Native command parity ledger

The frozen oracle contains all stable command IDs from the historical implementation. The native catalog has exact 259-ID parity. Catalog preservation is not behavioral parity.

## Summary

| State | Count |
|---|---:|
| Total stable IDs | 259 |
| Unique native catalog IDs | 259 |
| Cataloged | 259 |
| Contract verified or later | 2 |
| Implemented or later | 2 |
| Behavior verified | 0 |
| Native implementation blockers | 257 |
| Production behavior-verification blockers | 259 |

Implemented read-only commands:

- `catalog`
- `presets`

Neither command is marked `BehaviorVerified` because Windows oracle comparison has not run in the current environment.

## State definitions

| State | Required evidence |
|---|---|
| `Cataloged` | stable ID, title, summary, area, section, risk, privilege, restart, and keywords |
| `ContractVerified` | typed request/result contract and frozen parity fixture reviewed |
| `Implemented` | native handler exists and fails closed outside its admitted scope |
| `BehaviorVerified` | native and historical behavior compared on supported Windows environments with matching safety and result semantics |

## Promotion rule

The production finalizer requires exactly 259 `BehaviorVerified` records. An implemented handler without Windows behavior evidence is still a production blocker.

## Oracle provenance

Frozen fixtures live under `migration/oracle/`:

- `legacy-command-ids.json`
- `remediation-rules.json`
- `presets.json`
- `provenance.json`

The provenance file records the historical source commit and SHA-256 hash of each authoritative input. The native source archive uses these non-executable fixtures. The executable historical source is kept only in the separate oracle archive.
