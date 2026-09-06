<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="460" alt="WinCare" />
</p>

<p align="center">
  <strong>A safer, native Windows workspace for checkups, maintenance, recovery, and evidence-led repair.</strong>
</p>

<p align="center">
  <a href="https://github.com/AmirrezaFarnamTaheri/WinCare/actions/workflows/native-winui.yml"><img src="https://img.shields.io/github/actions/workflow/status/AmirrezaFarnamTaheri/WinCare/native-winui.yml?label=build%20%26%20release&logo=github" alt="Build and release status" /></a>
  <a href="https://github.com/AmirrezaFarnamTaheri/WinCare/releases"><img src="https://img.shields.io/github/v/release/AmirrezaFarnamTaheri/WinCare?display_name=tag&include_prereleases&sort=semver" alt="Latest release" /></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%20Windows%2011-0078D4?logo=windows" alt="Windows 10 and 11" />
  <img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="Apache 2.0 license" />
</p>

> [!IMPORTANT]
> WinCare is a release candidate. The 259-command native catalog is implemented, but production promotion remains gated on live Windows behavioral, accessibility, installation/upgrade, and administrator-command verification. Treat mutating operations as administrator work: read the preview, understand the impact, then approve deliberately.

## See it at a glance

<p align="center">
  <img src="docs/images/runtime-dashboard.png" alt="Historical WinCare Home capture from the installed v2.5.0-rc5 release candidate package" width="900" />
</p>

<p align="center"><em>Historical x64 runtime capture from the v2.5.0-rc5 package. Current source has since changed; see <code>docs/Screenshots.md</code> for capture provenance and recapture status.</em></p>

<p align="center">
  <a href="docs/showcase.html"><strong>Explore the Live Interactive Web Showcase</strong></a> &bull;
  <a href="docs/Screenshots.md"><strong>Full Interface Screenshots & Previews</strong></a>
</p>

WinCare brings everyday Windows care into one focused desktop app. It starts with evidence, keeps read-only work separate from changes, and records what happened so technicians and power users can make informed decisions.

| When you need to… | WinCare helps you… |
|---|---|
| Understand a machine | Run a system checkup and review read-only evidence before acting. |
| Make a change safely | Inspect a preflighted plan, elevation requirement, and affected resources before approval. |
| Recover or troubleshoot | Use typed native tools with fail-closed outcomes and explicit uncertainty when final state cannot be proven. |
| Extend the toolset | Review installed plugins and browse catalog metadata; remote installation stays disabled until a production catalog trust root is shipped. |
| Keep an audit trail | Review privacy-conscious activity receipts, items needing attention, and aggregated daily reports. |

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

## A deliberate safety model

WinCare does not treat a listed tool as permission to run it. Mutating commands follow a bounded two-phase path:

```text
Observe → validate typed inputs → preview → inspect evidence → dispatcher issues one-time receipt → explicit approval → execute → record outcome
```

- Unknown, unavailable, unauthorized, and unsafe operations fail closed.
- A caller cannot manufacture a valid mutation approval. Review receipts are dispatcher-issued, parameter-bound, short-lived, and single-use.
- Editing parameters or replaying a receipt requires a new preview.
- If mutation has started and a handler faults before the final state is known, WinCare reports that uncertainty rather than claiming nothing changed.
- Read-only diagnostic work is separated from mutating repair work.
- All Tools renders typed native inputs where the executor declares parameters; raw JSON is an explicit Advanced escape hatch.
- Activity advertises Undo only when a concrete executable compensator exists.
- The native file-system and process boundaries reject unsafe traversal and unbounded execution paths.

### Plugin trust

The plugin subsystem fails closed at the catalog boundary. `RemoteCatalogService` supports detached-signature verification of the exact catalog bytes against a WinCare-pinned public key, and remote installation additionally verifies package identity, SHA-256, publisher-signed manifest metadata, consent, revocation state, and installed-manifest admission records.

**This repository does not currently ship an approved production catalog signing key or a live official signed catalog.** The app composition root therefore keeps remote plugin installation intentionally **browse-only/disabled** rather than inventing a trust root. Local/installed plugins remain manageable. A future release may enable remote installation only by shipping an explicitly reviewed pinned catalog key and signed catalog endpoint.

Plugin capability declarations are informed-consent metadata. Full-trust in-process plugins are not sandboxed by those declarations.

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

## Security and license

Please report vulnerabilities privately through [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories), not public issues. WinCare is licensed under [Apache-2.0](LICENSE).
