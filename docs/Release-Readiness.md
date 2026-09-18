# Release readiness

This document defines the maintained release-readiness contract for WinCare. It replaces historical progress ledgers and one-off implementation trackers.

## Current candidate

- Product version: `VersionPrefix` and `VersionSuffix` in `Directory.Build.props` are authoritative.
- Command catalog: **269 commands** (`src/WinCare.CommandCatalog/Data/commands.json`).
- Supported packaged architectures: **x64** and **ARM64**.
- Desktop runtime: **WinUI 3 on .NET 8**, with the Rust native core built for the matching Windows MSVC target.

Do not copy test counts or transient workflow run numbers into this file. The checked-in source and the current GitHub Actions result for a commit are the authoritative evidence.

## Repository gates

A release candidate is repository-ready only when the Native WinUI workflow succeeds for the exact commit being promoted. The workflow is responsible for the following automated evidence:

1. Repository Python regression/invariant tests and documentation screenshot verification.
2. Rust formatting, Clippy with warnings denied, unit tests, and target-specific native builds. Dependency-resolving Cargo gates use `--locked` against the committed `native/Cargo.lock`.
3. Locked NuGet restore, vulnerability auditing, managed test suites, and WinUI release builds.
4. Unsigned MSIX construction followed by development signing, signer/chain validation, and tamper rejection.
5. Self-contained single-file portable packages for x64 and ARM64.
6. Portable runtime smoke tests on x64 Windows and an ARM64 Windows runner.
7. Release-source finalization and release-asset staging when a tag or explicit release dispatch requests publication. Each downloaded architecture artifact is checked against its own build-time digest manifest before assets are renamed or merged.

A red, cancelled, or stale workflow is not release evidence. Rerun or fix the exact failing commit rather than documenting around it.

## Manual runtime evidence

Automated CI is necessary but does not replace human UI/accessibility validation. Before a production promotion, use [Windows-Validation.md](Windows-Validation.md) to verify the candidate on Windows and record the commit SHA with the evidence. At minimum, inspect light, dark, and high-contrast themes; Windows text scaling; keyboard-only navigation; Narrator output; package installation; and the primary user flows.

Manual evidence must describe what was actually exercised. A successful cross-build, screenshot render, or smoke-mode process launch must not be represented as evidence for an interaction or hardware path that was not executed.

## Production promotion

`tools/finalize_native_release.py` is the repository's final source/archive gate. Release publication additionally requires the GitHub Actions `release-gate` job, version/tag agreement, and the package/runtime checks above. Production-mode finalization fails closed when the command catalog does not satisfy its behavior-verification policy.

The release procedure is deliberately stricter than normal development. Do not weaken locked dependency restore, vulnerability thresholds, signature checks, approval boundaries, or behavior-verification requirements merely to publish a build.

## Source archive and provenance boundary

The native-source archive includes `tests/__init__.py`, `PRODUCT.md`, `UX-CONTRACT.md`, and `FINAL-VALIDATION.md` so its Python discovery layout and product-contract inputs travel with the source. After extraction, run `python -m unittest discover -s tests -t . -v` from the extracted root; archive creation alone is not evidence that its checks pass.

The legacy-oracle archive contains available frozen reference fixtures and tooling, not a complete historical runtime snapshot. Removed PowerShell runtimes and duplicate release/recovery publishers remain retired; the existing Native WinUI release gate owns publication.

Digest manifests detect byte mismatches but are not authenticated workflow provenance. Exact-final-asset attestations are not currently restored. The historical action commit's public `action.yml` and README were readable, but upstream tag verification returned HTTP 403 without credentials. Do not reuse an unverified version label or claim hosted attestation coverage; verify the upstream pin, input contract, and job permissions before enabling this in the consolidated gate. No authentication keys are required for local source and staging fixtures.
