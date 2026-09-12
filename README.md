<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="460" alt="WinCare" />
</p>

<p align="center">
  <strong>A fast, native Windows workspace for system checkups, maintenance, and repair.</strong>
</p>

<p align="center">
  <a href="https://github.com/AmirrezaFarnamTaheri/WinCare/actions/workflows/native-winui.yml"><img src="https://img.shields.io/github/actions/workflow/status/AmirrezaFarnamTaheri/WinCare/native-winui.yml?label=build%20%26%20release&logo=github" alt="Build and release status" /></a>
  <a href="https://github.com/AmirrezaFarnamTaheri/WinCare/releases"><img src="https://img.shields.io/github/v/release/AmirrezaFarnamTaheri/WinCare?display_name=tag&include_prereleases&sort=semver" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011-0078D4?logo=windows" alt="Windows 10 and 11" />
  <img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="Apache 2.0 license" />
</p>

> [!IMPORTANT]
> WinCare is currently a release candidate. The 259-command native catalog is fully implemented. Mutating operations that require administrator privileges prompt for elevation; review high-impact changes before applying them.

## See it at a glance

<p align="center">
  <img src="docs/images/runtime-dashboard.png" alt="Historical WinCare Home capture from the installed v2.5.0-rc5 release candidate package" width="900" />
</p>

<p align="center"><em>Runtime capture from the v2.5.0-rc5 package.</em></p>

<p align="center">
  <a href="docs/showcase.html"><strong>Explore the Live Interactive Web Showcase</strong></a> &bull;
  <a href="docs/Screenshots.md"><strong>Full Interface Screenshots & Previews</strong></a>
</p>

WinCare brings everyday Windows maintenance, diagnostics, and recovery into a modern desktop app. Read-only checks stay separate from system changes, and all operations are logged locally so you always know what ran.

| What you want to do | How WinCare helps |
|---|---|
| Check system health | Run diagnostic scans and review system status across hardware, disk, and network. |
| Clean up & maintain | Run quick disk cleanups, manage startup items, and tune system responsiveness. |
| Repair & troubleshoot | Run native troubleshooting and repair tools with clear status outcomes. |
| Extend with plugins | Add custom tools via the plugin system; local packages can be inspected and run directly. |
| View history | Review activity history, pending items, and daily operation logs. |

## Get WinCare

Download the latest build from [Releases](https://github.com/AmirrezaFarnamTaheri/WinCare/releases). Each prerelease includes x64 and ARM64 options:

| Asset | Use it when… |
|---|---|
| `WinCare-v<version>-x64.msix` or `...-ARM64.msix` | You want the packaged Windows app. Use the certificate with the same architecture when the release uses a development certificate. |
| `WinCare-v<version>-x64.exe` or `...-ARM64.exe` | You want a self-contained, single-file executable. |
| `WinCare-v<version>-x64-portable.zip` or `...-ARM64-portable.zip` | You want a portable toolkit for an offline or technician workflow. |

For an MSIX release built with the runner-local development identity, download the matching `WinCare-v<version>-<arch>.cer` and use the included helper:

```powershell
python install_msix.py `
  --package .\WinCare-v<version>-x64.msix `
  --certificate .\WinCare-v<version>-x64.cer
```

The helper verifies the package signature, requires the certificate to match its signer, and imports it only to `LocalMachine\TrustedPeople`. Run it from an elevated terminal. See the [installation guide](docs/User-Guide.md#2-installation--getting-started) for detail.

## Safety model

WinCare organizes system modifications into three clear risk tiers:

```text
Safe: 1-click execution  │  Moderate: confirmation dialog  │  Destructive: preview & explicit approval
```

- **Safe:** Routine, low-impact maintenance (such as temporary file cleanup or DNS flushing) runs directly in 1 click.
- **Moderate:** Non-destructive configuration and system settings changes require user confirmation before applying.
- **Destructive:** High-impact operations (such as disk wiping or service deletion) require a read-only preview and explicit confirmation.
- **Audit trail:** Every command execution is logged to the local Activity journal with its exact parameters and timestamp.
- **Honest rollback:** Undo is offered only for commands that have a verified rollback mechanism, never as a generic placeholder promise.

### Plugins

WinCare supports plugins to add custom tools and diagnostics. Plugins run with the permissions of your Windows user account. In this build, remote plugin installation intentionally remains disabled without a configured production catalog root; local plugins can be inspected, tested, and managed directly.

## What’s inside

```text
WinCare.App             WinUI 3 desktop shell, typed tool UI, accessibility metadata
WinCare.Application     Dispatcher, review receipts, plugin host, activity journal
WinCare.Domain          Typed requests, results, policies, and evidence models
WinCare.Infrastructure  Windows integration, bounded processes, persistence, native interop
WinCare.CommandCatalog  259 command definitions plus typed parameter schemas
native/wincare-core     Rust bounded native primitives
native/wincare-guard    Experimental local health daemon / IPC boundary
tools/                  Packaging, validation, and plugin developer tooling
```

`wincare-guard` is **experimental** in the current candidate. Its local IPC boundary is access-controlled, but production SCM service lifecycle and complete native/app notification delivery are not claimed as finished.

The app is built with WinUI 3, .NET 8, and Rust. Read the [architecture guide](docs/Architecture.md) for boundaries and lifecycle details.

## For contributors

### Prerequisites

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- .NET SDK version specified in [global.json](global.json)
- Rust toolchain and Windows target specified in [rust-toolchain.toml](rust-toolchain.toml)
- Python 3.11+ for repository validation

### Verify before you change

```powershell
python tools/verify_native_foundation.py
python -m unittest discover -s tests/native -v
cargo test --manifest-path native/Cargo.toml
python tests/tools/test_plugin_cli.py -v
```

Follow [CONTRIBUTING.md](CONTRIBUTING.md) for environment setup, review expectations, and the release workflow.

## Documentation

| Start here | What you’ll find |
|---|---|
| [User guide](docs/User-Guide.md) | Installation, navigation, safe-operation guidance, and troubleshooting. |
| [Interface screenshots](docs/Screenshots.md) | Build-specific runtime captures, provenance, recapture status, and concepts. |
| [Architecture](docs/Architecture.md) | Layer boundaries, trust model, lifecycle, native integration, and packaging. |
| [Validation](VALIDATION.md) | Evidence model, checks, and promotion gates. |
| [Security](SECURITY.md) | Security invariants and private vulnerability reporting. |
| [Release finalization](docs/migration/finalization-status.md) | Current RC limitations and production promotion conditions. |

## Support WinCare

Support WinCare’s maintenance and development:

- **Star on GitHub**: Help more people find [WinCare](https://github.com/AmirrezaFarnamTaheri/WinCare)
- **Report an issue**: Bugs, ideas, and feedback on [GitHub Issues](https://github.com/AmirrezaFarnamTaheri/WinCare/issues)
- **Contact maintainer**: Amirreza “Farnam” Taheri &middot; [taherifarnam@gmail.com](mailto:taherifarnam@gmail.com)

### Donate

Choose a network, then copy the wallet address:

- **Bitcoin**: `bc1q68g4m4denjw4smhvwmnz5fychuj3ge2vupx07w`
- **Ethereum**: `0xbd5af5d1517317111db9523d6bb42fceae887abb`
- **TRON**: `TRjFLA1Dd32Bw1i3FxjZW5dmVub5UfXFSS`

## Security and license

Please report vulnerabilities privately through [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories), not public issues. WinCare is licensed under [Apache-2.0](LICENSE).
