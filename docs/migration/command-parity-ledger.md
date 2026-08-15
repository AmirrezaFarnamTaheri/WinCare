# Native Command Parity Ledger

The frozen oracle preserves all 259 stable command IDs from the historical implementation. The native catalog enforces exact 259-ID parity. Catalog and implementation coverage do not imply Windows behavioral parity.

---

## 📊 Summary Ledger

| State | Count | Definition |
|---|:---:|---|
| **Total Stable Legacy IDs** | **259** | Complete command catalog preserved from frozen legacy oracle |
| **Unique Native Catalog IDs** | **259** | Typed command definitions registered in `WinCare.CommandCatalog` |
| **Cataloged** | **259** | Metadata, summaries, risks, and privilege boundaries defined |
| **Contract Verified** | **259** | Typed request/result contracts and frozen parity fixtures reviewed |
| **Implemented** | **259** | Native routes implemented in `WindowsCommandExecutor.cs` with fail-closed handling |
| **Behavior Verified** | **0** | Live comparative execution on physical Windows test hosts |
| **Native Implementation Blockers** | **0** | Zero blocking code or routing omissions |
| **Production Verification Blockers** | **259** | Production promotion blocked until all 259 reach `BehaviorVerified` |

---

## 🔒 Execution & Safety Invariants

All 259 commands route through the same catalog-driven command runtime and native executor (`WindowsCommandExecutor.cs`):
- **Read-Only Commands (155)**: Gather live Windows telemetry or durable WinCare state without modifying system settings.
- **Mutating Commands (104)**: Require explicit parameter validation and preflight checks during preview before execution approval can be granted.
- **Fail-Closed Guarantees**: Unsupported dependencies, elevation denials, and unmet safety preconditions return explicit blocked/failed outcomes and never fall back to fabricated success.

---

## 🛑 Specific Safety Boundaries

The legacy temporary security-maintenance feature required authenticated snapshots plus automatic timed restoration after protection was reduced. The native application does not yet have a separately launchable recovery host that can provide that guarantee if the UI process exits.

Therefore, `security-control-reduce` and its restore path are deliberately fail-closed and perform no mutation. The former incorrect firewall substitution has been removed. This is an admitted safety boundary, not behavior-verification evidence.

---

## 🏛️ Oracle Provenance & Fixtures

Authoritative non-executable fixtures are stored under `migration/oracle/`:
- `legacy-command-ids.json`: Master list of all 259 stable command IDs.
- `remediation-rules.json`: Frozen remediation rules and issue mappings.
- `presets.json`: Standard, aggressive, and custom diagnostic presets.
- `provenance.json`: Source commit hash (`83567c4dbf3cf85e44217855d39604b0193623e3`) and SHA-256 digests of upstream artifacts.
