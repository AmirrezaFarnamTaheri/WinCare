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
> WinCare 3.0.0 is the current re-release line. The current catalog contains 269 commands. Operations that can change the system are admitted according to their declared risk and privilege requirements; review high-impact changes before applying them.

## Overview

<p align="center">
  <img src="docs/images/runtime-dashboard.png" alt="Historical WinCare v2.5.0-rc5 Home runtime capture" width="900" />
</p>

<p align="center"><em>Historical v2.5.0-rc5 runtime capture. The current source has a newer task-first Home; see <a href="docs/Screenshots.md">Screenshots</a> for capture status.</em></p>

WinCare brings Windows maintenance, diagnostics, recovery, and operational tooling into one native WinUI 3 application. Read-only checks remain separate from mutations, system-changing actions use explicit admission rules, and operation history is stored locally so users can see what ran and why.

| Goal | WinCare |
|---|---|
| Check system health | Inspect system, storage, security, hardware, network, update, and runtime evidence. |
| Clean up and maintain | Review bounded cleanup targets, startup state, app/package state, and maintenance workflows. |
| Repair and recover | Use guarded repair, remediation, restore, and recovery operations with explicit outcomes. |
| Work with advanced tools | Use Power tools for the complete typed catalog, including diagnostics, automation, window/workspace tools, developer utilities, native helpers, and download tooling. |
| Extend the app | Add and manage locally admitted capabilities through Extensions. |
| Review history | Inspect activity, receipts, change records, and local operation evidence. |

Explore the [interactive showcase](docs/showcase.html) or see [interface screenshots](docs/Screenshots.md).

### Where capabilities live

WinCare keeps common work out of the full command catalog:

- **Home** surfaces the next useful action, common care tasks, recent activity, and links to advanced areas.
- **Checkup** is read-only and hands findings off to the relevant care page instead of changing Windows itself.
- **System care**, **Security**, and **Repair & recovery** use the catalog's exact Area/Section taxonomy so tools do not leak into unrelated tabs.
- **Power tools** exposes all 269 native commands with task search, Area + Section filters, real category browsing, Favorites, Recent, typed parameters, and Care plans.
- **Extensions** contains optional built-in and locally admitted capabilities; **Troubleshoot** is the local rule-based diagnostic assistant.
- **Ctrl+K** searches pages, tools, extensions, and help topics from anywhere in the app.

## Install

Download the current build from [Releases](https://github.com/AmirrezaFarnamTaheri/WinCare/releases). Release candidates provide x64 and ARM64 artifacts:

| Asset | Purpose |
|---|---|
| `WinCare-v<version>-x64.msix` / `...-ARM64.msix` | Packaged Windows application. |
| `WinCare-v<version>-x64.exe` / `...-ARM64.exe` | Self-contained single-file executable. |
| `WinCare-v<version>-x64-portable.zip` / `...-ARM64-portable.zip` | Portable/offline package. |

Development-signed MSIX artifacts include a matching certificate and install helper. The helper validates the package signer and imports only the supplied certificate into `LocalMachine\TrustedPeople`; run it from an elevated terminal. See the [user guide](docs/User-Guide.md) for installation and safety details.

## Safety model

WinCare derives admission from each command's declared risk and behavior contract:

```text
Safe: direct execution  |  Moderate: confirmation  |  Destructive/Critical: preview and explicit approval
```

- Read-only commands do not mutate host state.
- Mutating commands validate parameters and privilege requirements before execution.
- Higher-impact operations require stronger review and approval.
- Undo is exposed only when the implementation has a real recovery path and sufficient captured state.
- Results and failures are recorded as operation evidence rather than converted into generic success messages.

### Plugins

Plugins run with the permissions available to the WinCare process. Local packages can be inspected and managed directly. For safety, remote plugin installation intentionally remains fail-closed unless the configured catalog trust root verifies the exact catalog and package evidence required by the plugin admission flow.

## Architecture

```text
WinCare.App             WinUI 3 shell, typed tool UI, accessibility and presentation
WinCare.Application     Dispatcher, admission, operation lifecycle, plugins, activity
WinCare.Domain          Requests, results, policies, risk and evidence models
WinCare.Infrastructure  Windows integration, persistence, bounded process execution, native interop
WinCare.CommandCatalog  269 command definitions, schemas, presets and remediation data
native/wincare-core     Rust native primitives and C ABI
native/wincare-guard    Experimental local health/IPC component
tools/                  Validation, packaging, release and plugin tooling
```

`wincare-guard` is **experimental** in this release candidate; production service lifecycle and complete app-notification delivery are not claimed as finished.

For architecture and trust boundaries, see [docs/Architecture.md](docs/Architecture.md).

## Build and verify

Prerequisites:

- Windows 10 version 2004 (build 19041) or later, or Windows 11
- .NET SDK pinned by [`global.json`](global.json)
- Rust toolchain pinned by [`rust-toolchain.toml`](rust-toolchain.toml)
- Python 3.11+ for repository validation

Core local checks:

```powershell
python tools/verify_native_foundation.py
python -m unittest discover -s tests -t . -v
cargo fmt --manifest-path native/Cargo.toml --all -- --check
cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
cargo test --manifest-path native/Cargo.toml
dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode
dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
```

The `Native WinUI` GitHub Actions workflow is the automated release gate for the exact commit under test. It also builds x64/ARM64 native and managed artifacts, verifies package signing/tamper rejection, produces portable packages, and runs portable smoke execution on matching Windows architectures.

Automated checks do not replace human UI/accessibility inspection. Follow [Windows validation](docs/Windows-Validation.md) for theme, scaling, keyboard, Narrator, installation, and primary-flow verification.

## Documentation

| Document | Purpose |
|---|---|
| [User guide](docs/User-Guide.md) | Installation, navigation, safe operation, and troubleshooting. |
| [Screenshots](docs/Screenshots.md) | Runtime captures and interface references. |
| [Architecture](docs/Architecture.md) | System boundaries, trust model, lifecycle, native integration, and packaging. |
| [Release readiness](docs/Release-Readiness.md) | Current automated and manual release evidence requirements. |
| [Windows validation](docs/Windows-Validation.md) | Repeatable Windows runtime and accessibility validation. |
| [Validation](docs/Validation.md) | Evidence model and verification categories. |
| [Security](SECURITY.md) | Security invariants and vulnerability reporting. |
| [Contributing](CONTRIBUTING.md) | Development and review workflow. |
| [Third-party notices](THIRD-PARTY-NOTICES.md) | Licensing information for external dependencies. |

## Support

- Report bugs and feature requests through [GitHub Issues](https://github.com/AmirrezaFarnamTaheri/WinCare/issues).
- Report security vulnerabilities privately through [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories).
- Maintainer: Amirreza “Farnam” Taheri — [taherifarnam@gmail.com](mailto:taherifarnam@gmail.com)

WinCare is licensed under the [Apache License 2.0](LICENSE). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for external dependency notices.
