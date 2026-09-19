# Windows validation guide

This guide defines the repeatable procedure for building, launching, packaging, and validating WinCare on Windows.

## Prerequisites

- **Operating system:** supported Windows 10 (version 2004 / build 19041+) or Windows 11.
- **.NET SDK:** exactly **8.0.416**, as pinned by `global.json`; feature-band roll-forward and prerelease SDKs are disabled.
- **Rust toolchain:** 1.97.1 stable with the matching MSVC target (`x86_64-pc-windows-msvc` or `aarch64-pc-windows-msvc`).
- **Windows App SDK:** build tools and WinApp CLI.
- **Developer Mode:** enabled in Windows Settings (`Settings > System > For developers`) when required for local package/app testing.

NuGet dependency resolution is reproducible by contract. Every project commits its normal `packages.lock.json`, CI restores in locked mode, and moderate-or-higher known vulnerabilities fail the dependency gate. Portable single-file publishing has a RID-specific dependency graph, so each source project also commits `packages.portable.win-x64.lock.json` and `packages.portable.win-arm64.lock.json`. `Directory.Build.props` selects the matching portable lock without overwriting the canonical lock file.

## 1. Run source and unit gates

```bash
# Verify native foundation and 269-command catalog coverage
python tools/verify_native_foundation.py

# Run repository Python tests
python -m unittest discover -s tests -t . -v

# Run Rust formatting, Clippy, and unit tests
cargo fmt --manifest-path native/Cargo.toml --all -- --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml

# Run Community Plugin CLI tests
python tests/tools/test_plugin_cli.py -v

# Verify visual tokens and pill contrast
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py

# Verify the committed NuGet graph and managed solution
dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

A dependency change that causes locked restore or the NuGet vulnerability audit to fail must be resolved explicitly. Do not disable locked mode, suppress `NU190x` advisories, or loosen the audit threshold simply to make validation pass.

## 2. Build and launch the desktop application

```bash
# Compile the Rust native core
cargo build --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc --release

# Stage the native library for the WinUI application
mkdir -p src/WinCare.App/Native/x64
cp native/target/x86_64-pc-windows-msvc/release/wincare_core.dll src/WinCare.App/Native/x64/wincare_core.dll

# Build WinCare
dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64

# Launch from the repository root
winapp run src/WinCare.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64
```

## 3. Perform UI and accessibility inspection

Automated smoke execution does not replace interaction-level inspection. Record the commit SHA and perform the following checks in **Light**, **Dark**, and **High Contrast** themes:

- Confirm the shell opens without an unhandled error and every navigation destination loads.
- Confirm text labels are unclipped, tables adapt at narrow widths, and keyboard focus indicators remain visible.
- Test Windows text scaling at **100%, 150%, 200%, and 225%** and confirm actionable controls remain reachable without clipped critical text.
- Verify `Ctrl+K`, `Ctrl+F`, `Tab`, `Shift+Tab`, `Enter`, `Space`, and `Esc` in the contexts documented by the user guide.
- Exercise the primary flows with Narrator and confirm announced names, roles, states, errors, and focus order are meaningful.
- Capture screenshots and the commit SHA with the validation record.

## 4. Validate package distributions

### MSIX package

```bash
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

### Standalone executable and portable ZIP

```bash
dotnet publish src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -r win-x64 \
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/portable/win-x64

python tools/package_portable.py artifacts/portable/win-x64 artifacts/WinCare-v3.0.0-x64-portable.zip
```

Repeat the package/runtime validation for ARM64 with `-p:Platform=ARM64 -r win-arm64`. The CI workflow also runs the portable smoke path on an ARM64 Windows runner; a local cross-build alone is not ARM64 runtime evidence.

## 5. Finalize release evidence

After the required validation is complete, generate the deterministic release-candidate source archive:

```bash
python tools/finalize_native_release.py --output artifacts/finalization --version 3.0.0 --mode production
```

The version must match `VersionPrefix` plus the optional `VersionSuffix` in `Directory.Build.props`. For production publication, follow [Release-Readiness.md](Release-Readiness.md) and use the guarded GitHub Actions release path so package, runtime, signature, version, and source-finalization checks remain attached to the exact commit being released.
