# Release readiness

This document defines the maintained release-readiness contract for WinCare. It replaces historical progress ledgers and one-off implementation trackers.

## Current candidate

- Product version: **2.5.0-rc5** (`Directory.Build.props`).
- Command catalog: **269 commands** (`src/WinCare.CommandCatalog/Data/commands.json`).
- Supported packaged architectures: **x64** and **ARM64**.
- Desktop runtime: **WinUI 3 on .NET 8**, with the Rust native core built for the matching Windows MSVC target.

Do not copy test counts or transient workflow run numbers into this file. The checked-in source and the current GitHub Actions result for a commit are the authoritative evidence.

## Repository gates

A release candidate is repository-ready only when the Native WinUI workflow succeeds for the exact commit being promoted. The workflow is responsible for the following automated evidence:

1. Repository Python regression/invariant tests and documentation screenshot verification.
2. Rust formatting, Clippy with warnings denied, unit tests, and target-specific native builds.
3. Locked NuGet restore, vulnerability auditing, managed test suites, and WinUI release builds.
4. Unsigned MSIX construction followed by development signing, signer/chain validation, and tamper rejection.
5. Self-contained single-file portable packages for x64 and ARM64.
6. Portable runtime smoke tests on x64 Windows and an ARM64 Windows runner.
7. Release-source finalization and release-asset staging when a tag or explicit release dispatch requests publication.

A red, cancelled, or stale workflow is not release evidence. Rerun or fix the exact failing commit rather than documenting around it.

## Manual runtime evidence

Automated CI is necessary but does not replace human UI/accessibility validation. Before a production promotion, use [Windows-Validation.md](Windows-Validation.md) to verify the candidate on Windows and record the commit SHA with the evidence. At minimum, inspect light, dark, and high-contrast themes; Windows text scaling; keyboard-only navigation; Narrator output; package installation; and the primary user flows.

Manual evidence must describe what was actually exercised. A successful cross-build, screenshot render, or smoke-mode process launch must not be represented as evidence for an interaction or hardware path that was not executed.

## Production promotion

`tools/finalize_native_release.py` is the repository's final source/archive gate. Release publication additionally requires the GitHub Actions `release-gate` job, version/tag agreement, and the package/runtime checks above. Production-mode finalization fails closed when the command catalog does not satisfy its behavior-verification policy.

The release procedure is deliberately stricter than normal development. Do not weaken locked dependency restore, vulnerability thresholds, signature checks, approval boundaries, or behavior-verification requirements merely to publish a build.
