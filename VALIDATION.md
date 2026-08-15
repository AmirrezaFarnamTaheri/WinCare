# Native Validation and Promotion Policy

WinCare establishes strict boundaries between static source validation, unit test execution, Windows runtime evidence, package verification, and command parity. Passing one verification class never substitutes for another.

---

## 🏷️ Evidence Classifications

| Evidence Term | Definition & Contract |
|---|---|
| **Verified** | Behavior executed and confirmed passing in the targeted Windows execution environment |
| **Statically Validated** | Source code, AST, or artifact structure confirmed via automated analysis tools |
| **Reviewed** | Implementation, design tokens, or contract inspected manually or against specifications |
| **Pending** | Required evidence cannot be collected in the current environment (e.g. non-Windows host) |
| **Blocked** | A declared prerequisite, platform capability, or safety gate prevented execution |
| **Failed** | A check ran and did not satisfy its acceptance contract |

---

## 🧪 Current Verification Matrix

### 1. Source, Rust, and Structural Verification Gates

- [x] **Native Foundation Gate (`tools/verify_native_foundation.py`)**:
  - Exact 259/259 unique command ID parity with frozen oracle (`migration/oracle/legacy-command-ids.json`).
  - Zero PowerShell or WPF dependencies in native roots.
  - Required WinUI 3 pages, navigation items, and telemetry instrumentation verified.
- [x] **Native Structural & Safety Unit Tests (`tests/native/`)**:
  - 56/56 unit tests passed covering fail-closed execution, PII exception sanitization, reparse-point safety, and bounded process runners.
- [x] **Rust 2024 Workspace Suites (`native/Cargo.toml`)**:
  - 29/29 tests passed across `wincare-core` (FFI contract, unwind safety, hashing) and `wincare-guard` (RAM/thermal monitors, Named Pipe IPC, Windows Toast XML notifications).
  - Clean `cargo clippy` with zero warnings under `-D warnings`.
- [x] **Community Plugin SDK & CLI Tests (`tests/tools/test_plugin_cli.py`)**:
  - Scaffolding, manifest linting, SemVer validation, and ZipSlip path traversal rejection verified.
- [x] **Visual Token Consistency (`tools/verify_visual_tokens.py`)**:
  - 33/33 visual tokens verified across Light, Dark, and High Contrast theme dictionaries.
- [x] **WCAG 2.1 AA Contrast Compliance (`tools/verify_pill_contrast.py`)**:
  - All 8 status pill pairs pass WCAG 2.1 AA (≥ 4.5:1), achieving up to 14.68:1 contrast.

### 2. Live Windows Runtime Evidence (Pending Physical Validation)

- [ ] **WinUI 3 Shell Launch & WinApp CLI execution** — *Pending live Windows runner*
- [ ] **UI Automation, keyboard accelerators, and contrast theme rendering** — *Pending live Windows runner*
- [ ] **Signed MSIX install, repair, upgrade, and uninstall cycles** — *Pending live Windows runner*
- [ ] **Command-by-command Windows behavior comparison against historical oracle** — *Pending live Windows runner*

---

## 🛡️ Core Safety Evidence

- **Exception Sanitization**: `CommandDispatcher` logs only exception type names; sensitive file paths or credentials in `ex.Message` are never copied to the activity journal.
- **Namespace Reservation**: Dynamic plugin registration strictly prohibits overwriting core command namespaces (`wincare.core.*`, `system.*`).
- **Shared Activity Journal**: A single application-scoped `ActivityJournalService` is shared across execution and UI layers, automatically refreshing upon page navigation.
- **Junction & Reparse Safety**: File cleanup canonicalizes roots, rejects reparse-point roots, and skips symlink descendants during traversal.
- **Process Isolation**: Process invocation passes discrete argument arrays, avoiding shell interpolation and injection vectors.
- **FFI Unwind Safety**: Every Rust FFI export is wrapped in `std::panic::catch_unwind`, preventing panics from crossing the C-ABI boundary into .NET.

---

## 🚀 Running the Local Verification Gate

To run the complete suite of deterministic source, design, and plugin checks:

```bash
# 1. Verify foundation and command parity
python tools/verify_native_foundation.py

# 2. Run Python regression suite
python -m unittest discover -s tests/native -v

# 3. Run Rust unit, FFI, and Guard tests
cargo test --manifest-path native/Cargo.toml

# 4. Run Plugin CLI developer suite
python tests/tools/test_plugin_cli.py -v

# 5. Verify design tokens and WCAG contrast
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py

# 6. Run Release Checklist
python tools/release_checklist.py
```

---

## 📦 Deterministic Release Finalization

To generate auditable release archives and separation reports:

```bash
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0-rc1 \
  --mode rc
```

The finalization tool produces:
- `WinCare-<version>-native-source.zip`: Pure C# and Rust source tree (zero PowerShell files).
- `WinCare-<version>-legacy-oracle.zip`: Isolated historical legacy oracle for parity tracking.
- `WinCare-<version>-finalization-report.md`: Markdown audit report detailing file counts, SHA-256 digests, and readiness metrics.
- `WinCare-<version>-finalization-manifest.json`: Machine-readable JSON manifest of all artifacts.

> [!IMPORTANT]
> Running with `--mode production` will exit with a non-zero error code until all 259 commands reach `BehaviorVerified`.

---

## 🤖 Automated CI Release Pipeline

The GitHub Actions workflow (`.github/workflows/native-winui.yml`) executes the following on `master` branch pushes and release tags:

1. **Source Verification**: Runs Python foundation and unit tests on Ubuntu runner.
2. **Rust Core Build & Test**: Compiles and tests `wincare-core` and `wincare-guard` across `x86_64-pc-windows-msvc` and `aarch64-pc-windows-msvc`.
3. **Managed Build & Packaging**: Compiles WinCare WinUI 3 desktop application for `x64` and `ARM64`, generating:
   - Signed MSIX packages (`.msix`)
   - Single-file standalone executables (`.exe`)
   - Portable self-contained ZIP distributions (`-portable.zip`)
4. **Release Gate**: Dynamically determines version tag, stages all assets via `tools/stage_release_assets.py`, and publishes or updates the release on GitHub Releases.
