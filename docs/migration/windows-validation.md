# Windows Validation Guide for Native WinCare

This document outlines the step-by-step procedure for building, launching, and validating the native WinCare application on a physical Windows host.

---

## 🛠️ Prerequisites

- **Operating System**: Supported Windows 10 (version 2004 / build 19041+) or Windows 11
- **.NET SDK**: 8.0.416 or newer
- **Rust Toolchain**: 1.97.1 stable with MSVC target (`x86_64-pc-windows-msvc` or `aarch64-pc-windows-msvc`)
- **Windows App SDK**: Build tools and WinApp CLI
- **Developer Mode**: Enabled in Windows Settings (`Settings > System > For developers`)

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

# Run C# .NET solution test suite
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

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
- Verify `Ctrl+K`, `Ctrl+F`, `Tab`, `Shift+Tab`, `Enter`, `Space`, and `Esc` in the contexts documented by the user guide.
- Capture screenshots and the commit SHA with the validation record. Manual inspection is runtime evidence, but it is not automated UI-test evidence.

---

## 📦 Step 4: Validate Package Distributions

### 1. MSIX Package Build
```bash
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

### 2. Standalone Single-File Binary & Portable ZIP
```bash
# Publish self-contained executable
dotnet publish src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -r win-x64 \
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/portable/win-x64

# Package into deterministic ZIP archive
python tools/package_portable.py artifacts/portable/win-x64 artifacts/WinCare-v2.5.0-rc1-x64-portable.zip
```

---

## 🏁 Step 5: Finalization & Audit Evidence

After completing all verification matrices, generate the deterministic release candidate archives:

```bash
python tools/finalize_native_release.py --output artifacts/finalization --version 2.5.0-rc1 --mode rc
```

The version must match `VersionPrefix` plus the optional `VersionSuffix` in `Directory.Build.props`. Production mode fails closed while any catalog command lacks `BehaviorVerified` status.
