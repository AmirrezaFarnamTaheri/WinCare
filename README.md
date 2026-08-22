<p align="center">
  <img src="design/WinCare-Wordmark.svg" width="540" alt="WinCare" />
</p>

<p align="center">
  <strong>Native Windows Care, Diagnostics, Security, Repair, and Recovery Engine</strong><br />
  Built with WinUI 3, .NET 8, and Rust 2024.
</p>

<p align="center">
  <a href="https://github.com/AmirrezaFarnamTaheri/WinCare/actions/workflows/native-winui.yml"><img src="https://img.shields.io/github/actions/workflow/status/AmirrezaFarnamTaheri/WinCare/native-winui.yml?branch=master&label=Native%20WinUI%20CI&logo=github" alt="CI Status" /></a>
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%20Windows%2011-0078D4?logo=windows" alt="Platform: Windows" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Rust-2024%20(1.97.1)-DEA584?logo=rust" alt="Rust 2024" />
  <img src="https://img.shields.io/badge/UI-WinUI%203%20%7C%20Windows%20App%20SDK-007A99" alt="WinUI 3" />
  <img src="https://img.shields.io/badge/Accessibility-WCAG%202.1%20AA%20(14.68%3A1)-047857" alt="WCAG AA" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-Apache%202.0-blue.svg" alt="License: Apache 2.0" /></a>
</p>

---

## 📖 Overview

**WinCare** is a native, modern Windows diagnostic, optimization, security, and recovery system engineered from the ground up to replace legacy scripting wrappers with a memory-safe, fail-closed desktop application.

Combining a **WinUI 3 Fluent Mica** interface, a **.NET 8** domain engine, and high-performance **Rust 2024** native extensions, WinCare delivers deep system insights, an AI-assisted diagnostic copilot, and a modular community plugin platform—all without sacrificing security, determinism, or system stability.

> [!IMPORTANT]
> **Release Candidate Status**: This repository represents the **native release candidate** line. All 259 legacy command contracts have been mapped to native, fail-closed executor routes. Production promotion is strictly gated until all 259 commands pass live Windows behavioral validation.

---

## ✨ Core Pillars & Capabilities

```
+----------------------------------------------------------------------------------------------------+
|                                      WinCare Architecture                                         |
+------------------------------------+----------------------------------+----------------------------+
|        WinUI 3 Desktop Shell       |      .NET 8 Application Core     |    Rust Native Engine      |
|  - Mica / Acrylic backdrop         |  - Fail-Closed Dispatcher        |  - Bounded C-ABI primitives|
|  - Cascadia Code telemetry pills   |  - AI Doctor Intent Translator   |  - Memory-safe FFI wrapper |
|  - High-contrast Cyber-Teal theme  |  - Dynamic Plugin Host           |  - Background Guard Monitor|
|  - Compact responsive layout (<920)|  - Encrypted Cloud Sync (AES-GCM)|  - Toast XML Notification  |
+------------------------------------+----------------------------------+----------------------------+
```

### 1. 🖥️ Native WinUI 3 Desktop Experience
- **Fluent Mica Surfaces**: Crisp Obsidian Slate (`#202020` Dark) and Mica Light (`#F3F3F3` Light) depth layers with zero artificial blur or decorative clutter.
- **Tactile Telemetry Badges**: High-contrast, monospaced status pills (`[ READ-ONLY ]`, `[ MUTATING ]`, `[ ELEVATED ]`, `[ NOT READY ]`) verified up to 14.68:1 contrast against WCAG 2.1 AA standards.
- **Ergonomic Keyboard & Responsive Layout**: Full `Ctrl+K` and `Ctrl+F` search palettes, visible focus rings, and an adaptive 920 DIP breakpoint for ultrabook displays.

### 2. 🦀 Rust 2024 Native Foundation & Background Guard
- **Memory-Safe FFI Core (`wincare-core`)**: Bounded native file enumeration (`dir_size`), system topology (`sys_info`), and hashing (`sha256`) exported via a versioned C ABI. Every export wraps internal logic in `std::panic::catch_unwind` to prevent unwinding across FFI boundaries.
- **Background Health Daemon (`wincare-guard`)**: Standalone background service performing sub-millisecond RAM pressure, disk quota, and thermal checks, triggering native Windows Toast XML notifications upon critical resource thresholds (with structured Named Pipe IPC client scaffolding in development).

### 3. 🧩 Modular Plugin Store & Community SDK
- **Dynamic Command Dispatcher**: Thread-safe dynamic extension plane registering tools directly into *All Tools* without application restarts.
- **Cryptographic Package Admission**: Enforces package ID equality, full-stream SHA-256 validation, and RSA/ECDSA digital signature verification (`publisher.pem` / `wincare-plugin.sig`).
- **Capability Disclosure**: Strict full-trust in-process execution warning modals and capability consent reviews before installation.
- **Developer CLI SDK (`tools/wincare-plugin-cli`)**: Node.js developer CLI with `create`, `validate`, `lint`, and `pack` commands for JSON packs and C# binary plugins.

### 4. 🧠 AI System Doctor (Local ONNX & DirectML)
- **Zero-Cloud Privacy**: Offline diagnostic inference powered by DirectML GPU acceleration with sub-1.5s cold-start latency.
- **Intent Translation**: Natural-language symptom parsing into validated, two-phase `DoctorActionPlan` sequences.
- **Two-Phase Confirmation**: Read-only evidence collection is performed first; mutative repairs require explicit step-by-step user approval.

### 5. 🛡️ Fail-Closed Security & Governance
- **Zero Fabricated State**: Commands report unavailable, blocked, or failed when dependencies or preconditions fail—never pretending success.
- **Reparse-Point & Path Traversal Immunity**: Traversal canonicalizes roots, rejects reparse-point roots, and skips symlink descendants.
- **PII-Clean Activity Journal**: Exception logs record exception type names only, preventing path or credential leakage.

---

## 📊 Parity & Migration Readiness

WinCare enforces an auditable migration accounting ledger:

| Metric | Status | Details |
|---|:---:|---|
| **Preserved Legacy Command IDs** | **259 / 259** | 100% frozen oracle parity (`migration/oracle/legacy-command-ids.json`) |
| **Typed Command Contracts** | **259 / 259** | Fully typed request/result contracts in `WinCare.Domain` |
| **Native Executor Routes** | **259 / 259** | Fail-closed routing in `WindowsCommandExecutor.cs` |
| **Mutating Commands Preflighted** | **104 / 104** | Parameter validation executed during preview before approval |
| **Legacy PowerShell in Native Roots** | **0 Files** | 100% clean C#/Rust source tree |
| **Live Windows Behavioral Evidence** | **0 / 259** | Pending live execution verification on physical Windows hosts |
| **Production Promotion Blockers** | **259** | Production promotion blocked until all 259 commands reach `BehaviorVerified` |

---

## 🏗️ Repository Architecture

```text
WinCare/
├── .github/workflows/          # Pinned CI/CD workflows for WinUI, Rust, and Releases
├── design/                     # Brand assets, logos, and visual token manifests
├── docs/                       # Architectural specifications and migration ledgers
│   ├── Architecture.md         # Component boundaries, trust model, and IPC
│   ├── migration/              # Command parity ledgers and validation guides
│   └── plans/                  # Approved architectural upgrade specifications
├── migration/oracle/           # Non-executable frozen legacy oracle parity fixtures
├── native/                     # Rust 2024 native crates
│   ├── wincare-core/           # C-ABI DLL export library
│   └── wincare-guard/          # Background health monitoring daemon
├── src/                        # .NET 8 C# solution
│   ├── WinCare.App/            # WinUI 3 desktop presentation layer
│   ├── WinCare.Application/    # Use cases, dispatching, plugins, AI doctor
│   ├── WinCare.CommandCatalog/ # Embedded typed catalog of 259 commands
│   ├── WinCare.Domain/         # Domain models, contracts, and telemetry
│   └── WinCare.Infrastructure/ # Windows APIs, Rust interop, process runners
├── tests/                      # Python structural, Rust, and C# unit test suites
└── tools/                      # Deterministic validation, packaging, and plugin CLI
```

---

## 🚀 Getting Started

### Prerequisites
- **Operating System**: Windows 10 (version 2004 / build 19041+) or Windows 11 (x64 or ARM64)
- **.NET SDK**: 8.0.416 or newer
- **Rust Toolchain**: 1.97.1 stable with `x86_64-pc-windows-msvc` or `aarch64-pc-windows-msvc` target
- **Python**: 3.11+ (Python 3.13 recommended) for validation and deterministic packaging
- **Node.js**: 18+ (for `tools/wincare-plugin-cli`)
- **Windows Developer Mode**: Enabled for local WinUI 3 package deployment

### 1. Clone & Setup
```bash
git clone https://github.com/AmirrezaFarnamTaheri/WinCare.git
cd WinCare
```

### 2. Run Local Verification Gates
```bash
# Verify native foundation & 259-command parity
python tools/verify_native_foundation.py

# Run Python regression and structural tests
python -m unittest discover -s tests/native -v

# Run Rust unit, FFI, and Guard daemon tests
cargo test --manifest-path native/Cargo.toml

# Run Community Plugin CLI tests
python tests/tools/test_plugin_cli.py -v

# Verify visual design tokens and WCAG AA contrast compliance
python tools/verify_visual_tokens.py
python tools/verify_pill_contrast.py
```

### 3. Build & Run Desktop Application
```bash
# 1. Build Rust native core DLL
cargo build --manifest-path native/Cargo.toml --target x86_64-pc-windows-msvc --release

# 2. Stage native library for WinUI
mkdir -p src/WinCare.App/Native/x64
cp native/target/x86_64-pc-windows-msvc/release/wincare_core.dll src/WinCare.App/Native/x64/wincare_core.dll

# 3. Restore and build WinCare solution
dotnet restore WinCare.Native.sln -p:Platform=x64
dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64
```

### 4. Create Standalone Packages
```bash
# Publish portable single-file binary
dotnet publish src/WinCare.App/WinCare.App.csproj -c Release -p:Platform=x64 -r win-x64 \
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfContained=true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o artifacts/portable/win-x64

# Package into deterministic ZIP distribution
python tools/package_portable.py artifacts/portable/win-x64 artifacts/WinCare-v2.5.0-rc1-x64-portable.zip
```

---

## 📦 Releases & Distributions

Official signed releases are published immutably via GitHub Actions upon tagged commits (`refs/tags/v*`), while master and PR builds produce verified CI workflow artifacts:

- **MSIX Package (`.msix`)**: Standard modern Windows application installer. This repository does not define an App Installer feed or automatic-update configuration; install newer releases explicitly.
- **Standalone Binary (`.exe`)**: Single-file self-contained executable requiring zero external runtime installation.
- **Portable Distribution (`.zip`)**: Standalone archive suitable for USB repair toolkits and offline maintenance environments.
- **Native Source Archive (`.zip`)**: Deterministic release archive containing verified pure C# and Rust source code.

Explore existing releases on the [GitHub Releases Page](https://github.com/AmirrezaFarnamTaheri/WinCare/releases).

---

## 📚 Documentation Index

| Document | Description |
|---|---|
| [User Guide](docs/User-Guide.md) | Comprehensive end-to-end user manual, page walkthroughs, and FAQ |
| [Architecture Specification](docs/Architecture.md) | Component architecture, trust boundaries, IPC, and lifecycle |
| [Design System & Visual Spec](DESIGN.md) | Cyber-Teal design tokens, typography, and WCAG contrast matrix |
| [Validation & Promotion Policy](VALIDATION.md) | Evidence classifications, test suites, and promotion gates |
| [Command Parity Ledger](docs/migration/command-parity-ledger.md) | Detailed 259-command migration status and accounting |
| [Finalization Status](docs/migration/finalization-status.md) | Current candidate release status, blockers, and artifact manifests |
| [Windows Validation Guide](docs/migration/windows-validation.md) | Windows build, WinApp CLI, and UI Automation test instructions |
| [Contributing Guidelines](CONTRIBUTING.md) | Code standards, ownership rules, and pull request workflow |
| [Security Policy](SECURITY.md) | Security invariants, threat boundaries, and vulnerability reporting |
| [Changelog](CHANGELOG.md) | Full version history and detailed release notes |
| [Third-Party Notices](THIRD-PARTY-NOTICES.md) | Third-party software dependencies and license attributions |

---

## 🔒 Security & Vulnerability Reporting

WinCare executes privileged and system-level operations. Security is treated as a fundamental product contract.

- Please do not submit vulnerability reports through public GitHub issues.
- Report security issues privately through [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories) or contact the project maintainers directly.
- Full details regarding our security model and threat invariants are detailed in [SECURITY.md](SECURITY.md).

---

## 📄 License

WinCare is licensed under the [Apache License, Version 2.0](LICENSE).
Copyright © 2026 Amirreza Farnam Taheri and WinCare Contributors.
