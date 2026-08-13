# Testing WinCare

WinCare testing is organized by evidence boundary. Contributors should run focused checks while developing, then the complete applicable validation chain before claiming completion.

## Test layers

| Layer | Primary purpose | Typical environment |
| --- | --- | --- |
| Structural validators | Cross-file contracts, authority boundaries, manifests, maintainability, release-tool invariants | Python-capable system |
| Python unit/adversarial tests | Deterministic tooling, archive attacks, source parsing, security invariants | Python-capable system |
| Pester suites | PowerShell core, provider, action, safety, compatibility, UI, and lifecycle contracts | PowerShell; many require Windows |
| PSScriptAnalyzer | PowerShell static quality and correctness | PowerShell with module installed |
| Native builds/tests | .NET compilation and compiled primitive behavior | Windows with .NET 8 SDK |
| Windows integration gate | Real provider, GUI, package, standalone, install, repair, and uninstall behavior | Supported Windows |
| Release verification | Determinism, archive closure, receipts, hashes, SBOM/provenance, exact-byte lifecycle | Supported release environment |

No layer is a universal substitute for the others.

## Fast contributor preflight

Run the narrow checks that match the files changed. At minimum, for ordinary PowerShell/source work:

```bash
python tools/validate_source.py .
python tools/validate_module_manifest.py .
python tools/validate_action_bindings.py .
python tools/validate_powershell_calls.py .
python tools/validate_test_source_assertions.py .
```

Then run the relevant Pester file or directory on Windows:

```powershell
Invoke-Pester -Path .\tests\Core.Contracts.Tests.ps1
```

Before submitting broad changes, run the complete suites described below.

## Complete structural suite

From the repository root:

```bash
python tools/validate_source.py .
python tools/validate_module_manifest.py .
python tools/validate_action_bindings.py .
python tools/validate_powershell_calls.py .
python tools/validate_test_source_assertions.py .
python tools/validate_source_references.py .
python tools/audit_stub_inventory.py .
python tools/validate_maintainability.py .
python tools/validate_network_egress.py .
python tools/validate_external_processes.py .
python tools/validate_bounded_io.py .
python tools/validate_read_only_state.py .
python tools/validate_context_menu_catalog.py .
python tools/validate_test_fixtures.py .
python tools/validate_gui.py .
```

These validators check source topology and known invariant classes. When supported, pass each tool's output option to retain machine-readable evidence in `artifacts/` rather than relying only on console text.

## Python tooling tests

```bash
python -m unittest \
  tools.test_release_tools \
  tools.test_standalone_release \
  tools.test_maintainability \
  tools.test_security_invariants \
  tools.test_cleaner_preview \
  tools.test_gui \
  tools.test_source_references \
  tools.test_wincare_ps_source \
  -v
```

These tests exercise release builders/verifiers, malformed archives and payloads, source parsing, security rules, GUI/source references, and maintainability behavior.

## Pester

Run all PowerShell suites on Windows:

```powershell
Import-Module Pester -RequiredVersion 5.5.0
Invoke-Pester -Path .\tests -Output Detailed
```

For a focused edit, run the nearest contract suite first, then the full suite. Representative areas include:

- core/action/transaction contracts;
- safety and recovery;
- provider admission and platform behavior;
- applications, cleanup, storage, networking, security, diagnostics, and maintenance;
- GUI/TUI catalogs and release UI behavior;
- advanced capabilities and supply-chain protocols;
- scheduled tasks and multi-phase provider suites;
- standalone and installation lifecycle integration.

A test that only checks for source text should not be used as runtime evidence unless the behavior is intentionally a source-level invariant.

## PSScriptAnalyzer

```powershell
Invoke-ScriptAnalyzer `
  -Path .\src, .\WinCare.ps1, .\Install-WinCare.ps1, .\Uninstall-WinCare.ps1 `
  -Recurse
```

The release/static-check wrapper is authoritative for the exact repository profile. Do not suppress a rule merely to make the gate green; explain and narrowly configure intentional exceptions.

## Windows validation gate

Run the integrated Windows gate:

```powershell
.\tools\Invoke-WindowsValidation.ps1 `
  -OutputDirectory .\artifacts\windows-validation
```

The gate is expected to cover:

- source/static validation;
- module import/export smoke tests;
- all Pester suites;
- PSScriptAnalyzer;
- .NET 8 native builds;
- GUI catalog and XAML resources;
- standalone payload/executables and self-tests;
- deterministic packaging and independent verification;
- installation, repair, and clean uninstall.

Use the generated evidence and logs in pull-request validation notes.

## Release preflight

For changes affecting packaging, native binaries, standalone hosts, installation, or release workflows:

```powershell
.\tools\Build-Release.ps1 `
  -OutputDirectory .\artifacts\release
```

The output directory must be absent or empty. Do not use `-SkipTests` or `-AllowDirty` when assessing a production candidate.

See [Release Engineering](RELEASE.md).

## Selecting tests by change

| Changed area | Focused tests/checks |
| --- | --- |
| `WinCare.ps1` or wrappers | public-call validator, entry-point tests, headless JSON/exit behavior, GUI/TUI launch path |
| `src/WinCare/Core/` | core/action/transaction/safety suites, module/source validators |
| `src/WinCare/Providers/` | provider-specific contracts, admission, blocked/unsupported paths, action bindings |
| `src/WinCare/UI/` or XAML | GUI validator, catalog/resource tests, Windows launch/accessibility checks |
| `src/WinCare/Native/` | .NET build, native contract tests, source/provenance closure |
| `src/WinCare/Standalone/` | `tools.test_standalone_release`, publish/self-test, PE and payload verification |
| `src/WinCare/Install/` or public installer scripts | installation lifecycle, path/reparse/identity, repair, shortcuts, uninstall and purge |
| `tools/` release code | tooling unit tests, deterministic comparison, malformed input and size-bound tests |
| workflows | local script reproduction plus workflow syntax/pinning/evidence aggregation review |
| docs only | link, command, path, terminology, and ownership review; relevant doc-aware validators |

## Required negative paths

Tests should cover failure behavior appropriate to the authority involved:

- prerequisite absent;
- platform unsupported;
- policy denied;
- approval/apply absent;
- elevation unavailable;
- target identity mismatch;
- reparse, traversal, duplicate, collision, or oversize input;
- external process timeout/nonzero exit/excess output;
- network redirect, invalid endpoint, credential risk, or excess response;
- postcondition missing or inconclusive;
- partial transaction and compensation failure;
- malformed receipt, manifest, payload, or archive;
- corrupt versus unrecognized installation.

A successful happy path alone is insufficient for security-sensitive behavior.

## Test isolation and cleanup

Tests must:

- use dedicated temporary roots;
- avoid real user/application/system targets when a fixture can express the contract;
- never rely on unrelated machine state without explicit skip/block behavior;
- clean up task-created files, processes, services, tasks, rules, mounts, or state;
- preserve diagnostics needed to understand a failure;
- avoid committing generated artifacts or machine-specific evidence.

When testing destructive or elevated behavior, use the smallest admitted fixture and verify cleanup/postconditions explicitly.

## Interpreting failures

Separate:

- regressions caused by the current change;
- pre-existing repository failures;
- unsupported platform/provider results;
- missing dependencies;
- environmental or flaky failures.

Do not remediate unrelated failures merely to make the current change appear green. Continue only when the requested behavior can still be validated independently and safely; otherwise report a blocker.

## Reporting evidence in a pull request

Record:

- exact command;
- environment/runner;
- result and exit status;
- relevant artifact/log path;
- whether evidence is verified, statically validated, reviewed, pending, or blocked;
- any skipped or unrelated failures;
- limitations not exercised locally.

Example:

```text
Verified on Windows 11 x64 / PowerShell 7.4:
- Invoke-Pester -Path tests/Core.Contracts.Tests.ps1 — passed
- python tools/validate_action_bindings.py . — passed

Pending:
- Full production release workflow; requires CI Windows matrix.
```

## Related documents

- [Validation](../VALIDATION.md)
- [Contributing](../CONTRIBUTING.md)
- [Architecture](Architecture.md)
- [Release Engineering](RELEASE.md)
