# Windows Validation Guide for Native WinCare

This document outlines the step-by-step procedure for building, launching, and validating the native WinCare application on a physical Windows host.

---

## 🛠️ Prerequisites

- **Operating System**: Supported Windows 10 (version 2004 / build 19041+) or Windows 11
- **.NET SDK**: Exactly **8.0.416**, as pinned by `global.json`; feature-band roll-forward and prerelease SDKs are disabled
- **Rust Toolchain**: 1.97.1 stable with MSVC target (`x86_64-pc-windows-msvc` or `aarch64-pc-windows-msvc`)
- **Windows App SDK**: Build tools and WinApp CLI
- **Developer Mode**: Enabled in Windows Settings (`Settings > System > For developers`)

NuGet dependency resolution is reproducible by contract. Each project commits its normal `packages.lock.json`, CI restores with locked mode enabled, and all transitive NuGet dependencies are audited for moderate-or-higher known vulnerabilities. Portable single-file publish has a different RID-specific dependency graph, so each source project also commits `packages.portable.win-x64.lock.json` and `packages.portable.win-arm64.lock.json`. `Directory.Build.props` stages the correct portable variants immediately before the implicit publish restore.

---

## 🧪 Step 1: Run Source & Unit Gates

```bash
# Verify native foundation & 259-command parity
python tools/verify_native_foundation.py

# Run Python native unit tests
python -m unittest discover -s tests/native -v

# Run Rust formatting, clippy, and unit tests
cargo fmt --manifest-path native/Cargo.toml --all -- --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml

# Run Community Plugin CLI tests
python tests/tools/test_plugin_cli.py -v

# Run Visual Tokens & Pill Contrast checks
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py

# Verify the committed NuGet graph and run the C# solution test suite
dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

A dependency change that causes locked restore or the NuGet vulnerability audit to fail must be resolved explicitly. Do not disable locked mode, suppress `NU190x` advisories, or loosen the audit threshold merely to make validation pass.

---

## 🏗️ Step 2: Build & Launch Desktop Application

```bash
# 1. Compile Rust native core
cargo build --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc --release

# 2. Stage native library for WinUI application
mkdir -p src/WinCare.App/Native/x64
cp native/target/x86_64-pc-windows-msvc/release/wincare_core.dll src/WinCare.App/Native/x64/wincare_core.dll

# 3. Build WinCare WinUI 3 application
dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64

# 4. Launch with WinApp CLI from the repository root
winapp run src/WinCare.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64
```

---

## 🤖 Step 3: Perform UI & Accessibility Inspection

No UI Automation runner is checked into this repository. Record the launched process ID and perform the following manual checks in **Light**, **Dark**, and **High Contrast** themes:

- Confirm the shell opens without an unhandled error and every navigation destination loads.
- Confirm text labels are unclipped, tables adapt at narrow widths, and keyboard focus indicators remain visible.
- Test Windows text scaling at **100%, 150%, 200%, and 225%** and confirm actionable controls remain reachable without clipped critical text.
- Verify `Ctrl+K`, `Ctrl+F`, `Tab`, `Shift+Tab`, `Enter`, `Space`, and `Esc` in the contexts documented by the user guide.
- Exercise the primary flows with Narrator and confirm announced names, roles, states, errors, and focus order are meaningful.
- Capture screenshots and the commit SHA with the validation record. Manual inspection is runtime evidence, but it is not automated UI-test evidence.

---

## 📦 Step 4: Validate Package Distributions

### 1. MSIX Package Build
```bash
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

### 2. Standalone Single-File Binary & Portable ZIP
```bash
# Publish the self-contained executable. The MSBuild restore hook automatically stages
# the committed win-x64 portable lock variants before the locked implicit restore.
dotnet publish src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -r win-x64 \
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/portable/win-x64

# Package into deterministic ZIP archive
python tools/package_portable.py artifacts/portable/win-x64 artifacts/WinCare-v2.5.0-rc5-x64-portable.zip
```

Repeat the publish validation with `-p:Platform=ARM64 -r win-arm64` when validating the ARM64 artifact. A successful cross-build is not evidence that the ARM64 binary was executed on ARM64 hardware.

---

## 🏁 Step 5: Finalization & Audit Evidence

After completing all verification matrices, generate the deterministic release candidate archives:

```bash
python tools/finalize_native_release.py --output artifacts/finalization --version 2.5.0-rc5 --mode rc
```

The version must match `VersionPrefix` plus the optional `VersionSuffix` in `Directory.Build.props`. The finalized native-source archive includes the reproducibility configuration, dependency lock graphs, infrastructure security tests, repository review guardrails, and native validation suite needed to audit the candidate. Production mode fails closed while any catalog command lacks `BehaviorVerified` status.
