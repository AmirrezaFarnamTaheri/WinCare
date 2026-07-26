# WinCare Design

WinCare is a local Windows operations workstation optimized for explicit authority, recoverability, and observable evidence.

## Principles

1. One authoritative write path.
2. Query surfaces never acquire mutation authority.
3. UI state is not policy or durable truth.
4. Missing evidence never becomes success.
5. Resource bounds are part of every external boundary.
6. Recovery is designed before mutation.
7. Optional dependencies are explicit, verified, and replaceable.
8. Language boundaries must earn their operational cost.
9. Current executable documentation replaces implementation-history narratives.

## Language allocation

PowerShell remains the natural owner of Windows orchestration and operator workflows. C# is limited to compiled primitives that benefit from stable interop, strict byte-oriented parsing, or reduced runtime compilation. Python is limited to reproducible build and independent verification tools. No language is introduced solely to imitate an external architecture.

## Product surfaces

WPF, terminal, and headless entry points call the same module. Advanced capabilities are exposed through capability discovery and domain functions, with dependency state and non-claims included in their results.
