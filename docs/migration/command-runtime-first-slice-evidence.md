# Command runtime first-slice evidence

**Recorded:** 2026-08-06

## Delivered

- A typed `CommandDispatcher` that owns command admission, timestamps, deadlines, cancellation, handler lookup, and structured results.
- Fail-closed outcomes for unknown, unmigrated, incorrectly applied read-only, unreviewed mutating, expired, cancelled, and misregistered commands.
- Strict, bounded native loading for the 69 built-in remediation rules and 7 built-in presets.
- Native read-only handlers for `catalog` and `presets`.
- Two command definitions advanced from `Cataloged` to `Implemented`.
- All tools execution through an `AsyncRelayCommand`, with busy state, accessible status feedback, a complete structured result envelope, and recent-tool tracking.
- A malformed duplicate XAML attribute in the inherited All tools table template was removed.

## Verified in this environment

| Gate | Result |
|---|---|
| Python native and packaging tests | 14 passed, 0 failed |
| Command runtime structural tests | 6 passed, 0 failed |
| Native verifier | Passed |
| Preserved command IDs | 259 unique IDs with exact legacy parity |
| Native handler IDs | `catalog`, `presets` |
| Embedded remediation rules | 69, with parsed-value equality to the built-in legacy document |
| Embedded presets | 7, with parsed-value equality to the built-in legacy document |
| Native PowerShell/WPF references | 0 |
| All tools XAML parse | Passed |
| Python source compilation | Passed |
| CI source gate | Runs the foundation, portable packaging, and command-runtime structural suites |
| New gate falsification | Known-bad CI coverage and result-envelope specimens were rejected before the final pass |

## Migration state

| State | Commands |
|---|---:|
| Cataloged | 259 |
| Contract verified | 2 |
| Implemented | 2 |
| Behavior verified | 0 |

`catalog` currently returns the built-in rule document. The legacy implementation can also merge extension rules, so full behavior parity is intentionally not claimed. `presets` returns the built-in presets, but parity still requires execution against the PowerShell oracle on Windows.

## Runtime gates still pending

This environment has no `dotnet`, `cargo`, `rustc`, `pwsh`, `winapp`, or Windows desktop session. The following were not run:

- C# compilation and xUnit tests
- PowerShell versus native fixture comparison
- Rust build and tests
- WinUI launch and UI Automation
- light, dark, and High Contrast screenshot review
- MSIX and portable launch validation on x64 and ARM64

These are unverified gates, not assumed passes.

## Legacy retention

The PowerShell implementation remains in the migration workspace as the behavior oracle. No PowerShell file is part of the native application projects or native startup path. Deletion remains blocked until all 259 commands reach `BehaviorVerified` and the final packaging pipeline proves zero PowerShell payload files.
