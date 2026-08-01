# Contributing to WinCare

WinCare accepts changes that preserve its safety, evidence, and compatibility contracts. A feature is not complete merely because it runs on one machine: its authority, failure behavior, verification evidence, documentation, and release impact must also be clear.

## Before starting

Read the documents that define the affected contract:

- [Architecture](docs/ARCHITECTURE.md)
- [Security policy and model](SECURITY.md)
- [Capability matrix](docs/CAPABILITY-MATRIX.md)
- [Compatibility](docs/Compatibility.md)
- [Testing](docs/Testing.md)
- [Validation](VALIDATION.md)

For user-visible or operator-facing work, also read [Commands](docs/COMMANDS.md) and [Design](docs/DESIGN.md).

## Development requirements

The complete Windows development environment uses:

- Windows 10 22H2 or a supported Windows 11 edition;
- PowerShell 7.2 or later;
- Python 3 for structural validators and release tooling;
- .NET 8 SDK for native and standalone builds;
- Pester and PSScriptAnalyzer as exercised by the Windows validation script.

Some source validators run on non-Windows systems, but they do not replace Windows runtime evidence.

## Change workflow

1. Start from the current default branch.
2. Create a focused branch.
3. Inspect nearby contracts, providers, tests, and documentation before editing.
4. Make the smallest coherent change that satisfies the intended outcome.
5. Add or update tests for positive, negative, blocked, and unsupported paths as applicable.
6. Run the relevant local checks.
7. Update every document whose contract changed.
8. Review the complete diff for unrelated edits, generated residue, secrets, and unsafe authority expansion.
9. Open a pull request with explicit validation evidence and remaining limitations.

## Architectural responsibilities

Use the existing ownership model:

- **PowerShell** owns orchestration, Windows administration, typed plans, policies, transactions, provider routing, evidence, and recovery.
- **.NET 8** owns narrow compiled or Windows-interoperability primitives where PowerShell is not the appropriate implementation layer.
- **Python** owns deterministic packaging, structural validation, archive verification, and release-tool tests.

Do not move behavior between languages without a concrete technical reason and corresponding release/test updates.

## Safety requirements

State-changing behavior must:

- have an explicit action or plan contract;
- validate target identity, scope, policy, and prerequisites;
- preview before execution unless the contract is inherently read-only;
- require explicit apply/approval authority;
- respect `ShouldProcess` or the equivalent governed transaction path;
- bound filesystem, process, network, archive, and output behavior;
- verify observable postconditions or return explicit inconclusive/blocked evidence;
- journal the outcome;
- define compensation or partial-failure behavior where reversal is possible.

Never convert missing evidence, unavailable providers, nonzero process exits, or unsupported platforms into success.

## Dependency policy

WinCare does not silently install optional runtimes or broaden machine authority to satisfy a feature.

When adding a dependency:

1. justify why existing Windows/.NET/PowerShell facilities are insufficient;
2. define exact discovery and compatibility behavior;
3. pin or authenticate external artifacts where applicable;
4. document the missing-dependency result;
5. keep credentials and keys outside the repository;
6. add supply-chain and failure-path tests;
7. update the capability and compatibility documents.

## Tests to add

Select tests according to the change:

| Change type | Expected coverage |
| --- | --- |
| New exported function | definition/export parity, parameter compatibility, success and failure behavior |
| New action or headless route | action/dispatcher parity, preview/apply behavior, structured result shape |
| Mutating provider | policy denial, blocked prerequisite, postcondition evidence, rollback/partial failure |
| Filesystem behavior | canonical containment, reparse-point rejection, entry/byte ceilings, race-sensitive identity checks |
| Process execution | exact executable identity, bounded arguments/output/time, exit handling |
| Network behavior | admitted endpoint, redirect/credential restrictions, bounded response, no implicit egress |
| UI change | catalog binding, resource validity, accessibility state, read-only authority preservation |
| Packaging change | deterministic output, manifest/receipt/SBOM/provenance compatibility, adversarial verification |
| Installer change | clean install, repair, shortcut identity, corrupt/unmanaged handling, clean uninstall |

See [Testing](docs/Testing.md) for runnable commands.

## Documentation requirements

Update documentation in the same pull request when changing:

- command parameters, examples, or result behavior;
- capability availability or prerequisites;
- platform support or degradation behavior;
- security or trust boundaries;
- transaction, recovery, or state semantics;
- build, validation, installation, verification, or promotion requirements;
- UI interaction or accessibility semantics.

The [documentation index](docs/README.md) identifies the owning document for each contract.

## Pull-request description

A pull request should explain:

- **What changed** — concrete behavior and affected components.
- **Why** — the problem or requirement being addressed.
- **Authority impact** — whether the change can observe, plan, mutate, elevate, execute, download, or transmit.
- **Compatibility impact** — supported, degraded, blocked, and unsupported environments.
- **Evidence** — exact checks run and their outcomes.
- **Residual risk** — known limitations, pending Windows evidence, or intentionally excluded scope.
- **Documentation** — files updated to keep the contract accurate.

Do not claim a check ran when it was only inspected or inferred.

## Commit and repository hygiene

- Keep commits reviewable and purpose-specific.
- Do not commit build output, caches, temporary diagnostics, credentials, keys, machine-specific state, or extracted payloads.
- Do not rewrite unrelated files solely for formatting.
- Preserve the existing package manager, lockfiles, workflow pinning, and deterministic build inputs.
- Treat generated artifacts through their source of truth.

## Definition of done

A change is ready when:

- the requested behavior is implemented within the intended scope;
- contracts and exports remain coherent;
- relevant validators and tests pass, or unavailable evidence is clearly marked pending;
- mutation and failure paths remain fail-closed;
- affected documentation is current and cross-linked;
- no known task-created secrets, temporary processes, debug residue, or unintended artifacts remain;
- no known unrelated user changes were overwritten or deliberately discarded.
