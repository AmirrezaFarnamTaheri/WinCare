# Windows validation for the native WinCare app

## Prerequisites

- Windows 10 22H2 or a supported Windows 11 release
- .NET SDK 8.0.416
- Rust stable with the MSVC target for the machine architecture
- Windows App SDK build tools
- WinApp CLI for launch and UI Automation checks
- Developer Mode enabled for local package deployment

## Source and unit gates

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
cargo fmt --manifest-path native/Cargo.toml --all -- --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

## Build and launch

Build the Rust DLL, copy it to `src/WinCare.App/Native/x64`, then build the app:

```text
cargo build --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc --release
dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64
winapp run src/WinCare.App/bin/x64/Debug/net8.0-windows10.0.19041.0/win-x64
```

Run the UI Automation smoke test against the PID printed by `winapp run`:

```text
python tests/ui/test_winui_shell.py --pid <PID> --output artifacts/ui
```

Review the screenshot in `artifacts/ui/all-tools.png` in light, dark, and a Windows Contrast theme. UIA pass results do not prove that text is unclipped or that columns remain usable.

## Package outputs

MSIX:

```text
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

Portable self-contained folder and ZIP:

```text
dotnet publish src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -r win-x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -p:PublishSingleFile=false -o artifacts/portable/win-x64
python tools/package_portable.py artifacts/portable/win-x64 artifacts/WinCare-win-x64-portable.zip
```

Production packages must be signed and timestamped. Development certificates are not release credentials.

## Finalization evidence

After the Windows matrix passes, run RC finalization and archive the generated manifest with the job evidence:

```text
python tools/finalize_native_release.py --output artifacts/finalization --version 2.4.0-rc1 --mode rc
```

Production mode remains blocked until the command catalog contains 259 `BehaviorVerified` entries.
