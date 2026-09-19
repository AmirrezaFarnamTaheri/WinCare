# Native Validation and Promotion Policy

WinCare separates source validation, hosted Windows CI, packaged-runtime evidence, interactive validation, and command-by-command behavior verification. Passing one evidence class does not silently substitute for another.

This document consolidates the former root `VALIDATION.md` and `FINAL-VALIDATION.md`; release history lives in git, not in validation docs.

---

## Evidence classifications

| Evidence term | Definition |
|---|---|
| **Verified** | Behavior executed and confirmed in the targeted environment. |
| **CI Verified** | Build, test, packaging, signing, artifact, or runtime behavior completed successfully in the declared hosted CI environment. |
| **Statically Validated** | Source, metadata, or artifact structure passed deterministic automated analysis. |
| **Reviewed** | Implementation or contract was inspected against its specification. |
| **Pending** | Required evidence has not yet been collected in the environment that can prove it. |
| **Blocked** | A declared prerequisite or platform capability prevented execution. |
| **Failed** | A check ran and did not satisfy its acceptance contract. |

---

## Current verification matrix

### 1. Source and structural verification

- [x] **Native foundation contract** (`tools/verify_native_foundation.py`): exact 259/259 command ID parity with the frozen oracle, native-source boundaries, WinUI navigation contracts (including three-way catalog ⇄ PageService ⇄ ShellPage route agreement), and one fail-closed command executor boundary.
- [x] **Native Python regression suite** (`tests/native/`): passes in PR CI. Coverage includes command admission, parameter/approval provenance, bounded process behavior, reparse-point safety, plugin admission rollback, dependency-lock determinism, portable publish contracts, finalized-source completeness, responsive UI contracts, chrome-label resource parity, and release behavior.
- [x] **Community plugin CLI suite** (`tests/tools/`): passes in PR CI. Coverage includes scaffolding, manifest linting, SemVer validation, archive bounds, symlink/path traversal rejection, deterministic packaging, and Unicode archive paths.
- [x] **Visual and accessibility source contracts**: theme-token consistency and status-pill WCAG 2.1 AA contrast remain covered by the repository tests and validators; see the workflow result for the exact commit for current token and pair counts.
- [x] **Documentation image integrity**: checked-in PNG evidence is validated without regenerating screenshots during ordinary CI.

The unified workflow runs the Python repository tests with one discovery command instead of repeating the native foundation and plugin test paths as separate workflow steps.

### 2. Rust and managed Windows CI

The Windows build matrix owns both the native Rust core and the managed/package build for each architecture; there is no intermediate DLL artifact upload/download hop.

- [x] **Rust x64 and ARM64**: formatting is checked once, Clippy runs for both Windows targets with `-D warnings`, x64 unit tests execute, and release builds compile for x64 and ARM64.
- [x] **Managed x64 tests**: all managed test suites pass in PR CI; see the workflow result for the exact commit for current counts.
- [x] **Locked NuGet restore and audit contract**: committed dependency graphs remain the build input.
- [x] **x64 and ARM64 MSIX builds**: both architectures compile with their directly staged Rust core.
- [x] **Runner-local development signing**: each MSIX is signed with an ephemeral certificate, signer/publisher identity is checked, a modified package is rejected, and the private certificate is removed in the same packaging step.
- [x] **x64 and ARM64 portable publication**: self-contained, trimmed single-file executables and deterministic portable ZIPs are produced for both RIDs and checked against the canonical executable-size ceiling.

These are CI-verified build/package results. Runner-local development signing does not claim production-certificate deployment trust.

### 3. Packaged runtime smoke

The workflow downloads the actual versioned portable artifact and executes `--smoke-test` on the matching architecture runner:

- [x] **x64 portable runtime** on `windows-latest`.
- [x] **ARM64 portable runtime** on `windows-11-vs2026-arm`.

The smoke crosses WinUI startup/window activation, native Rust ABI loading, plugin/runtime initialization, and a read-only `system` dispatcher path before exiting successfully. This is meaningful packaged-runtime evidence, but it does not prove every WUA/COM path, arbitrary third-party plugin behavior, accessibility behavior, or all 269 command implementations.

### 4. Interactive / deployment / command evidence still required

- [ ] **Narrator, keyboard-only navigation, High Contrast rendering, and 100–225% text scaling** on the release candidate.
- [ ] **Production-certificate MSIX install, repair, upgrade, and uninstall cycles**.
- [ ] **Broader ARM64 Windows integration behavior** beyond the hosted portable startup/core-flow smoke.
- [ ] **Command-by-command Windows behavior comparison against the historical oracle**. Production promotion remains blocked until all 269 commands reach `BehaviorVerified`.

---

## Repository gates on every head update

The authoritative merge evidence is the GitHub Actions **Native WinUI** workflow for the exact PR head. The source is expected to pass these repository-level gates on every update:

- native/Python repository regression suite;
- `tools/verify_native_foundation.py`;
- visual-token verification;
- status-pill WCAG 2.1 AA contrast verification;
- XML/XAML/RESW/project/manifest and JSON parsing checks;
- command-catalog uniqueness and migration/finalization checks;
- XAML/code-behind event-wiring checks;
- x64 and ARM64 native/managed build and packaging jobs.

Hard-coded historical test counts are intentionally not used here because regression tests are added as the product contract evolves. The workflow result for the exact commit is the source of truth.

## Product/data invariants

- Command catalog: **269 commands / 269 unique IDs** (frozen IDs; 259 in the legacy-parity oracle).
- Care pages continue to derive from exact command-catalog Area/Section taxonomy.
- Checkup remains read-only and routes findings to care surfaces.
- Home remains presentation-only and exposes one primary Checkup CTA; its evidence rows match the actual Checkup evidence sources.
- Troubleshoot hands suggested commands to the canonical Power tools execution/review surface; Power tools exposes Safe / Moderate / Destructive product tiers instead of raw backend risk values.
- Extension catalog trust/availability is visible in the frontend.
- Named care-section navigation keeps shell/PageService/navigation-catalog route parity.
- Raw catalog risk and command IDs remain available as advanced technical detail without defining the normal product taxonomy.
- The dispatcher remains authoritative for admission, preview/approval semantics, execution, results, and Activity evidence.

## Core safety evidence

- **Approval provenance**: mutating execution uses dispatcher-issued, parameter-bound, expiring, single-use approval plans.
- **Exception sanitization**: `CommandDispatcher` logs exception type names rather than copying arbitrary exception messages into the activity journal.
- **Plugin trust boundary**: remote installs re-resolve catalog data, enforce package/publisher revocation, bind installed manifests to admission records, and re-verify publisher signatures during discovery.
- **Plugin rollback**: failed upgrades preserve prior plugin state and the last known-good admission record.
- **Namespace reservation**: dynamic plugins cannot overwrite reserved core command namespaces.
- **Junction and reparse safety**: cleanup canonicalizes roots, rejects reparse-point roots, and skips reparse descendants during traversal.
- **Process isolation**: native process invocation passes bounded argument arrays rather than interpolating commands through a shell.
- **FFI unwind safety**: Rust exports guard panic boundaries so Rust unwinds do not cross the C ABI into .NET.

---

## Running the local verification gate

Use the repository-pinned toolchains and committed lockfiles. `global.json` defines the exact .NET SDK used by CI.

```bash
# Repository Python tests (native + tooling)
python -m unittest discover -s tests -t . -v

# Rust checks on a supported Windows x64 development host
cargo fmt --manifest-path native/Cargo.toml --all -- --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml

# Screenshot integrity only; ordinary CI does not regenerate documentation images
python tools/capture_screenshots.py --verify-only

# Managed restore/tests
dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

`tools/release_checklist.py` remains available when a contributor wants the broader local source checklist or a direct portable-artifact size check.

---

## Deterministic source finalization

Source and oracle archives can be generated locally:

```bash
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version <version> \
  --mode <rc|production>
```

The finalizer produces:

- `WinCare-<version>-native-source.zip`: native C#, Rust, Python tooling, plugin developer tooling, documentation/assets, repository guardrails, and committed dependency metadata; executable legacy PowerShell is excluded.
- `WinCare-<version>-legacy-oracle.zip`: isolated historical legacy oracle for parity tracking.
- `WinCare-<version>-finalization-report.md`: readiness and artifact-separation report.
- `WinCare-<version>-finalization-manifest.json`: hashes, file counts, oracle provenance, and readiness metrics.

The structural regression suite exercises finalization. Ordinary PR/branch CI therefore does **not** create and upload another source-finalization bundle on every run.

Production mode still exits non-zero until all 269 commands are `BehaviorVerified`. That contract is intentionally retained; CI no longer exposes an independent manual mode switch that can incorrectly ask an RC version to finalize as production.

---

## Unified GitHub Actions pipeline

`.github/workflows/native-winui.yml` is the single CI/build/release workflow for pull requests, `master`/`main` pushes, release tags, and manual dispatches:

1. **Verify** — one Python repository test invocation, checked-in screenshot integrity, and one product-version extraction.
2. **Build matrix** — Rust format/lint/test/build plus managed restore/test, MSIX build/sign/verify, trimmed portable publish, size validation, and package artifact staging for x64 and ARM64.
3. **Portable runtime smoke** — runs the versioned portable executable on matching x64 and ARM64 hosted runners.
4. **Release gate** — only for release tags or an explicit manual `publish_release=true`; downloads package artifacts, finalizes source/oracle evidence, stages release assets, and publishes or completes the matching GitHub release.

Manual dispatch defaults to validation/build only. A supplied `release_tag` must exactly match `Directory.Build.props`. Finalization mode is derived from the checked-in product version: prerelease versions such as `3.0.0-rc1` use `rc`, while a stable version uses `production` and therefore still requires full `BehaviorVerified` command parity.

Release builds do not require repository signing secrets. Each packaging job creates a temporary runner-local development certificate, validates the resulting MSIX against its exported public certificate without adding a trusted root, and removes the private identity before the job completes.

## Live Windows visual-validation limitation

Agent environments without a Windows desktop session cannot truthfully certify the final rendered UI, Narrator output, keyboard focus order, High Contrast appearance, text/display scaling, or new runtime screenshots. GitHub Actions can validate Windows compilation/build/package behavior, but a fresh installed-candidate visual/accessibility pass is still required. Follow `docs/Windows-Validation.md` and recapture the runtime screenshots when the next release candidate is installed.
