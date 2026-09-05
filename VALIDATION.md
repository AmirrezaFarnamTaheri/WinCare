# Native Validation and Promotion Policy

WinCare establishes strict boundaries between static source validation, automated CI execution, interactive Windows evidence, package verification, and command parity. Passing one verification class never substitutes for another.

---

## 🏷️ Evidence Classifications

| Evidence Term | Definition & Contract |
|---|---|
| **Verified** | Behavior executed and confirmed passing in the targeted execution environment |
| **CI Verified** | An automated build, test, packaging, signing, or artifact contract executed successfully in the declared hosted CI environment |
| **Statically Validated** | Source code, AST, metadata, or artifact structure confirmed via deterministic automated analysis |
| **Reviewed** | Implementation, design tokens, or contract inspected manually or against specifications |
| **Pending** | Required evidence has not yet been collected in the environment that can prove the behavior |
| **Blocked** | A declared prerequisite, platform capability, or safety gate prevented execution |
| **Failed** | A check ran and did not satisfy its acceptance contract |

---

## 🧪 Current Verification Matrix

### 1. Source, Rust, and Structural Verification Gates

- [x] **Native Foundation Gate (`tools/verify_native_foundation.py`)**:
  - Exact 259/259 unique command ID parity with the frozen oracle (`migration/oracle/legacy-command-ids.json`).
  - Zero PowerShell or WPF dependencies in native runtime roots.
  - Required WinUI 3 pages, navigation contracts, command execution bindings, and automation metadata verified.
  - All 259 catalog commands route through one fail-closed native executor boundary.
- [x] **Native Structural & Safety Unit Tests (`tests/native/`)**:
  - **88/88 passed** in PR CI.
  - Coverage includes fail-closed execution, parameter/approval provenance, bounded process behavior, reparse-point safety, plugin admission rollback, dependency-lock determinism, portable publish lock contracts, finalized-source completeness, responsive UI contracts, and release safety gates.
- [x] **Rust 2024 Workspace Gates (`native/Cargo.toml`)**:
  - `cargo fmt --check` and Clippy with `-D warnings` pass for both `x86_64-pc-windows-msvc` and `aarch64-pc-windows-msvc`.
  - Rust unit tests execute on the x64 Windows runner.
  - Release builds compile successfully for both x64 and ARM64 targets.
  - ARM64 Rust unit execution is intentionally skipped in hosted CI; cross-compilation is not runtime evidence on ARM hardware.
- [x] **Community Plugin SDK & CLI Tests (`tests/tools/test_plugin_cli.py`)**:
  - **9/9 passed**, covering scaffolding, manifest linting, SemVer validation, archive bounds, symlink/path traversal rejection, deterministic repeated packaging, and Unicode archive paths.
- [x] **Visual Token Consistency (`tools/verify_visual_tokens.py`)**:
  - 33/33 visual tokens verified across Light, Dark, and High Contrast theme dictionaries.
- [x] **WCAG 2.1 AA Contrast Compliance (`tools/verify_pill_contrast.py`)**:
  - All 8 status pill pairs pass WCAG 2.1 AA (≥ 4.5:1), achieving up to 14.68:1 contrast.

### 2. Automated Windows CI Evidence

The main PR workflow uses hosted Windows runners to exercise build/package contracts that cannot be proven by Linux-only source inspection:

- [x] **Managed x64 tests** — **164/164**: 9 Command Catalog, 58 Application, and 97 Infrastructure tests.
- [x] **Locked NuGet restore and vulnerability audit** — the committed dependency graph is restored in locked mode and audits all transitive packages at moderate-or-higher severity.
- [x] **x64 and ARM64 MSIX build** — both package architectures compile with the staged Rust core.
- [x] **Runner-local development signing** — each MSIX is signed with an ephemeral certificate, signer/publisher identity is verified, and a tampered package is rejected.
- [x] **x64 and ARM64 portable publication contract** — CI requires self-contained single-file publication plus deterministic portable ZIP staging for both RIDs using committed portable lock graphs.

These checks are **CI Verified** build/package evidence. They do not prove interactive UI behavior, production-certificate deployment, or native execution on physical ARM64 hardware.

### 3. Interactive / Deployment / Behavior Evidence Still Required

- [ ] **WinUI 3 shell launch and interactive command execution** — requires an interactive Windows desktop session on the candidate artifact.
- [ ] **Narrator, keyboard-only navigation, High Contrast rendering, and 100–225% text scaling** — requires live accessibility validation; static XAML/token checks are not a substitute.
- [ ] **Production-certificate MSIX install, repair, upgrade, and uninstall cycles** — runner-local development signing proves package integrity plumbing, not production deployment trust.
- [ ] **ARM64 runtime execution** — requires physical or emulated ARM64 Windows execution; cross-compilation alone is insufficient.
- [ ] **Command-by-command Windows behavior comparison against the historical oracle** — the catalog currently remains blocked from production promotion until all 259 commands reach `BehaviorVerified`.

---

## 🛡️ Core Safety Evidence

- **Approval Provenance**: Mutating execution requires a dispatcher-issued, parameter-bound, expiring, single-use plan produced by successful preview.
- **Exception Sanitization**: `CommandDispatcher` logs exception type names rather than copying potentially sensitive exception messages into the activity journal.
- **Plugin Trust Boundary**: Remote installs re-resolve against fresh catalog data, enforce package and publisher revocation, bind installed manifests to external admission records, and re-verify publisher signatures during discovery.
- **Plugin Rollback**: Failed upgrades preserve prior plugin state and retain the last known-good admission-record backup if trust-record restoration itself fails.
- **Namespace Reservation**: Dynamic plugin registration prohibits overwriting reserved core command namespaces.
- **Junction & Reparse Safety**: File cleanup canonicalizes roots, rejects reparse-point roots, and skips symlink descendants during traversal.
- **Process Isolation**: Process invocation passes discrete bounded argument arrays rather than interpolating commands through a shell.
- **FFI Unwind Safety**: Rust FFI exports guard panic boundaries so Rust unwinds do not cross the C ABI into .NET.

---

## 🚀 Running the Local Verification Gate

Use the repository-pinned toolchains and committed lockfiles. The exact .NET SDK is defined by `global.json`; CI restores NuGet packages in locked mode.

```bash
# 1. Verify foundation and command parity
python tools/verify_native_foundation.py

# 2. Run Python regression suite
python -m unittest discover -s tests/native -v

# 3. Run Rust unit, FFI, and Guard tests on a supported Windows x64 development host
cargo test --manifest-path native/Cargo.toml

# 4. Run Plugin CLI developer suite
python tests/tools/test_plugin_cli.py -v

# 5. Verify design tokens and WCAG contrast
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py

# 6. Run Release Checklist
python tools/release_checklist.py

# 7. Restore/test the managed solution with committed locks
# global.json requires .NET SDK 8.0.416 exactly.
dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

---

## 📦 Deterministic Release Finalization

To generate auditable release archives and separation reports:

```bash
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.5.0-rc5 \
  --mode rc
```

The finalization tool produces:
- `WinCare-<version>-native-source.zip`: Native release source and audit evidence, including C#, Rust, Python release/validation tooling, the JavaScript plugin developer CLI and tests, linked documentation/assets, repository guardrails, and committed dependency lock graphs; executable legacy PowerShell is excluded.
- `WinCare-<version>-legacy-oracle.zip`: Isolated historical legacy oracle for parity tracking.
- `WinCare-<version>-finalization-report.md`: Markdown audit report with readiness metrics and artifact-separation status.
- `WinCare-<version>-finalization-manifest.json`: Machine-readable artifact paths, SHA-256 digests, file counts, oracle provenance, and readiness metrics.

The structural regression suite stages the native source bundle and fails if required security/reproducibility evidence or plugin developer tooling is absent, or if staged Markdown contains a broken local target.

> [!IMPORTANT]
> Running with `--mode production` exits non-zero until all 259 commands reach `BehaviorVerified`.

---

## 🤖 Automated CI Release Pipeline

`.github/workflows/native-winui.yml` runs for pull requests, supported branch pushes, release tags, and manual dispatch. Its main gates are:

1. **Source Verification**: Foundation verification, the full Python structural suite, plugin CLI tests, and deterministic source finalization on Ubuntu.
2. **Rust Gates**: Pinned Rust formatting and Clippy for x64/ARM64, x64 unit tests, and release builds for both Windows targets.
3. **Managed Build & Packaging**: Exact .NET SDK provisioning, locked/audited NuGet restore, x64 managed tests, x64/ARM64 MSIX build and ephemeral signing verification, portable single-file publication, deterministic archive staging, and artifact upload.
4. **Tag Release Gate**: Requires the pushed tag to exactly match checked-in version metadata, stages verified assets through `tools/stage_release_assets.py`, and creates the GitHub release. Release-candidate tags remain prereleases; stable production tags are marked latest. Existing published releases are treated as immutable and fail closed on conflicting re-upload attempts.

Release builds do not require repository signing secrets. Each packaging job creates a temporary runner-local development certificate, signs its architecture-specific MSIX, verifies signer/publisher identity and tamper rejection, then removes the private identity. The matching public `.cer` is published with the artifact. Non-tag CI builds produce workflow artifacts rather than GitHub release assets.

The separate manual workflow, `.github/workflows/native-release-candidate.yml`, publishes source-finalization artifacts only. Dispatch it from `master` or `main` with a version matching `Directory.Build.props`; choose `production` only after all commands are `BehaviorVerified`.
