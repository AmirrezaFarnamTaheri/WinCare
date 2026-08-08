# Native validation and promotion

WinCare uses separate source, Windows runtime, package, and command-parity gates. One evidence class cannot substitute for another.

## Evidence terms

- **Verified:** behavior executed successfully in the named environment.
- **Statically validated:** source or artifact structure checked without executing Windows behavior.
- **Reviewed:** implementation or evidence inspected against its contract.
- **Pending:** required evidence was unavailable.
- **Blocked:** a declared prerequisite or gate prevented execution.
- **Failed:** the check ran and did not satisfy its contract.

## Linux-capable source gate

Run from the repository root:

```text
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
```

This gate verifies:

- exact frozen-oracle parity for 259 command IDs
- native project separation from PowerShell and WPF
- approved navigation, tabs, table structure, and accessibility metadata
- XML and JSON validity
- Rust ABI source contract
- deterministic ZIP metadata and sorted entries
- native-source and legacy-oracle separation
- release metadata
- fail-closed production readiness accounting

A passing source gate means **statically validated**, not Windows verified.

## Release-candidate finalization

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0-rc1 \
  --mode rc
```

The finalization manifest records artifact paths, file counts, SHA-256 hashes, command migration counts, and production readiness. The native source archive must contain zero `.ps1`, `.psm1`, and `.psd1` files. The legacy oracle archive must remain separate.

## Production gate

```text
python tools/finalize_native_release.py \
  --output artifacts/finalization \
  --version 2.4.0 \
  --mode production
```

Production mode requires:

- exactly 259 unique command IDs
- all 259 commands marked `BehaviorVerified`
- zero production behavior-verification blockers
- successful deterministic staging

The current project intentionally fails this gate with 259 behavior-verification blockers.

## Windows gate

The Windows matrix must run on the exact candidate source:

```text
cargo fmt --manifest-path native/Cargo.toml --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
dotnet build src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64
```

Required runtime evidence includes:

- WinUI launch through WinApp CLI
- keyboard navigation and command-palette behavior
- UI Automation flows for every primary page
- screenshots in light, dark, and Windows Contrast themes
- cancellation, timeout, and fail-closed result states
- x64 and ARM64 Rust DLL loading
- MSIX installation, launch, repair, upgrade, and uninstall
- portable launch and data-location behavior
- startup markers and representative-device timing
- command-by-command comparison with the frozen historical oracle

## Package promotion

A production release requires all source and Windows gates, signed and timestamped packages, coherent checksums and provenance, and exact-byte promotion of the artifacts that passed validation. Rebuilding or re-zipping after verification creates a new candidate and requires the full gate again.
