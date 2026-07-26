# Validation

WinCare uses separate evidence boundaries.

## Source assurance

Structural validators confirm closed module membership, export/definition parity, action/dispatcher parity, public parameter compatibility, bounded I/O, process/network authority, GUI catalog integrity, test fixtures, anti-stub rules, and maintainability limits.

## Windows runtime assurance

The mandatory Windows job imports the module normally, runs all Pester suites, executes PSScriptAnalyzer, builds every .NET 8 native project from source, validates the GUI catalog and XAML resource references, and exercises installation, package verification, and uninstallation.

## Release assurance

The release builder accepts an empty output directory, uses one audited source state, includes source-built native artifacts for production, produces deterministic bytes, and emits an SPDX SBOM, build receipt, manifest, checksum, release receipt, and in-toto provenance. The independent verifier streams archive members and rejects unsafe paths, duplicates, case collisions, links, suspicious expansion, unmanifested files, hash mismatches, profile contamination, dirty production receipts, and native provenance mismatch.

## Promotion rule

A development artifact is not a production release. Promotion requires all mandatory Windows and native evidence to pass for the exact immutable package bytes being published.
