# Contributing to WinCare

WinCare accepts changes that preserve its safety, evidence, accessibility, and compatibility contracts.

## Required environment

The complete development environment uses:

- supported Windows 10 or Windows 11
- .NET 8 SDK
- Windows App SDK build tools and WinApp CLI
- Rust stable with x64 and ARM64 MSVC targets as needed
- Python 3.13 for structural validation and deterministic packaging
- Developer Mode for local WinUI deployment

PowerShell is not a dependency of the native source project. Historical PowerShell source exists only in the separate legacy oracle used for controlled parity work.

## Ownership

- C# owns UI, workflows, command admission, policy, evidence, journaling, recovery, elevation, and packaging.
- Rust owns bounded native primitives justified by measurement or safety.
- Python owns source validation, parity-fixture validation, and deterministic source packaging.

Do not duplicate business rules across C# and Rust. Do not add a PowerShell runtime, script, module, or build dependency to the native source tree.

## Change workflow

1. Create a focused branch from the current default branch.
2. Read `README.md`, `DESIGN.md`, `docs/Architecture.md`, and the relevant migration ledger.
3. Write a failing test for the behavior or invariant being changed.
4. Implement the smallest coherent change.
5. Run focused tests, then the complete native source gate.
6. Run the applicable Windows gate.
7. Update the command migration state only when its required evidence exists.
8. Update affected documentation in the same pull request.
9. Record exact checks and limitations in the pull-request description.

## Command migration

A command advances through `Cataloged`, `ContractVerified`, `Implemented`, and `BehaviorVerified`. Do not skip a state or mark a command behavior-verified from source inspection alone.

Every command implementation must define:

- typed inputs and outputs
- current-state observation
- privilege and compatibility rules
- preview and approval behavior for mutations
- cancellation and deadline handling
- bounded output
- evidence and postconditions
- journal and recovery behavior where applicable
- positive, negative, blocked, and unsupported tests

## Required checks

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
```

On Windows, also run the Rust, .NET, WinUI, UI Automation, and packaging checks in `docs/migration/windows-validation.md`.

## Pull-request evidence

State what changed, authority impact, command migration state, exact tests run, Windows evidence, packaging impact, residual risk, and documentation updated. Never describe a check as run when it was only inspected or inferred.
