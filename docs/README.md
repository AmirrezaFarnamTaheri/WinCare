# WinCare documentation

This directory contains the maintained technical and operator documentation for WinCare. The root [README](../README.md) is the project landing page; this index routes readers to the document that owns each subject.

## Start here

| Audience | Recommended path |
| --- | --- |
| New operator | [Project overview](../README.md) → [Command reference](COMMANDS.md) → [Compatibility](Compatibility.md) |
| Administrator | [Security model](../SECURITY.md) → [Capability matrix](CAPABILITY-MATRIX.md) → [Advanced capabilities](Advanced-Capabilities.md) |
| Contributor | [Contributing](../CONTRIBUTING.md) → [Architecture](ARCHITECTURE.md) → [Testing](Testing.md) |
| Release engineer | [Validation](../VALIDATION.md) → [Release engineering](RELEASE.md) |
| UI contributor | [Design system](DESIGN.md) → [Architecture](ARCHITECTURE.md) |

## Documentation map

### Operating WinCare

- [Command reference](COMMANDS.md) — entry-point parameters, operating modes, JSON automation, preview/apply behavior, and examples.
- [Compatibility and degradation](Compatibility.md) — supported Windows environments, optional dependencies, and fail-closed behavior.
- [Capability matrix](CAPABILITY-MATRIX.md) — authoritative inventory of implemented capability areas and assurance behavior.
- [Advanced capabilities](Advanced-Capabilities.md) — dependency-gated adapters, explicit boundaries, and non-claims.

### Understanding the system

- [Architecture](ARCHITECTURE.md) — component responsibilities, execution lifecycle, trust boundaries, state, recovery, and packaging.
- [Security policy and model](../SECURITY.md) — vulnerability reporting, security invariants, and threat boundaries.
- [Design system](DESIGN.md) — interaction principles and cross-surface visual semantics.

### Developing and releasing

- [Contributing](../CONTRIBUTING.md) — development workflow, change requirements, documentation ownership, and pull-request expectations.
- [Testing](Testing.md) — contributor checks, Windows runtime validation, and test-selection guidance.
- [Validation](../VALIDATION.md) — evidence classes, mandatory gates, and promotion rules.
- [Release engineering](RELEASE.md) — deterministic build, verification, installation lifecycle, and promotion procedure.
- [Release notes](../CHANGELOG.md) — current supported release summary.

## Ownership rules

Documentation is part of the product contract. A change is incomplete when it alters behavior, prerequisites, command syntax, safety boundaries, or release evidence without updating the owning document.

| Change | Required documentation review |
| --- | --- |
| Entrypoint parameter or headless route | `docs/COMMANDS.md` and root `README.md` examples |
| Capability implementation or prerequisite | `docs/CAPABILITY-MATRIX.md` and, when advanced, `docs/Advanced-Capabilities.md` |
| Platform support or degradation behavior | `docs/Compatibility.md` |
| Transaction, policy, state, recovery, or component boundary | `docs/ARCHITECTURE.md` and `SECURITY.md` |
| Test, validator, workflow, artifact, or promotion requirement | `docs/Testing.md`, `VALIDATION.md`, and `docs/RELEASE.md` |
| UI semantics or interaction behavior | `docs/DESIGN.md` |

## Documentation conventions

- Describe observable behavior, not aspiration.
- Distinguish implemented, dependency-gated, unsupported, and explicitly excluded behavior.
- Use repository-relative links.
- Keep command examples directly executable from the repository root unless stated otherwise.
- State when a command previews, mutates, requires elevation, or depends on an external runtime.
- Do not describe compilation, source inspection, or a smoke test as runtime verification.
- Avoid duplicating full inventories across documents; link to the authoritative owner.

## Evidence vocabulary

Use these terms consistently when verification status matters:

- **Verified** — executed successfully in the stated environment.
- **Statically validated** — checked without executing the platform behavior.
- **Reviewed** — manually inspected against the relevant contract.
- **Pending** — required evidence was unavailable in the current environment.
- **Blocked** — execution could not proceed because a declared prerequisite or policy condition was not satisfied.

## Keeping links healthy

When moving or renaming a document:

1. update this index;
2. update the root README;
3. search all Markdown files for the old path;
4. verify relative links from both repository-root and `docs/` documents;
5. include the documentation change in the same pull request as the behavior change.
