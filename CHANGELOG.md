# Changelog & Release Notes

All notable changes to WinCare are documented in this file in accordance with [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) principles.

---

## [2.5.0-rc1] - 2026-08-15

### 🔒 Security, Signing & Packaging Hardening
- **Immutable Tag-Gated Releases**: CI release pipelines trigger strictly upon tag pushes (`refs/tags/v*`), validating tag versions against checked-in metadata and preventing release mutations.
- **Ephemeral & Secret-Backed MSIX Code Signing**: Replaced committed developer certificates with secure repository secret handling (`WINCARE_SIGNING_CERT_BASE64`) and TripleDES-compatible ephemeral runner-local signing keys with strict post-build signature verification gates.
- **Fail-Closed MSIX Installer**: `tools/install_msix.py` performs host architecture auto-detection (`x64` / `ARM64`), validates package signatures, and imports certificates using environment variable bindings to eliminate shell script interpolation.
- **Secret Key Prevention**: Added hard assertions in release source packaging and structural gates to strictly reject private keys (`.pfx`, `.p12`, `.key`, `.pem`, `.snk`, `.secret`).

### ⚡ Performance & Resource Optimization
- **Single-File PE Compression**: Enabled `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` and stripped debug symbols, reducing standalone executable footprints by ~65% (from ~370 MB down to ~120–135 MB).
- **Zero-Allocation Rust Streaming**: Direct buffer slice serialization in `wincare-core` without intermediate heap allocations.
- **Catalog Search Memoization & Array Pooling**: Thread-safe catalog search result caching in `ToolCatalogService` with automatic invalidation upon `RegistryChanged`, and `ArrayPool<char>` pooling in process execution streams.

---

## [2.4.0-rc1] - 2026-08-14

### 🌟 Major Upgrades & Platform Expansion

#### 1. Modular Plugin Store & Community SDK Architecture
- **Dynamic Command Dispatcher**: Extended `ICommandDispatcher` with thread-safe dynamic registration via `ConcurrentDictionary`, allowing third-party and built-in plugins to dynamically inject tool definitions and execution handlers into *All Tools* without application restarts.
- **Native Script Execution Engine**: Added `PluginScriptCommandHandler` backed by `BoundedProcessRunner` with process boundary limits, path traversal isolation, and structured `CommandHandlerOutcome` returns.
- **Package Admission & Integrity Verification**: Hardened `PluginInstallerService` to enforce strict package ID equality (`manifest.Id == targetPluginId`) and byte-level SHA-256 digest validation across stream extractions and remote downloads.
- **Cryptographic Publisher Signatures**: Added RSA SHA-256 and ECDSA public key verification (`publisher.pem` / `wincare-plugin.sig`) supporting 3 trust tiers: *Verified Organization*, *Digitally Signed*, and *Community / Unsigned*.
- **Namespace Reservation & Collision Safety**: Protected core command IDs and reserved namespaces (`wincare.core.*`, `system.*`) against collision or overwriting in `ToolCatalogService` and `PluginRegistryService`.
- **Atomic Backup Staging**: Isolated update rollback backups into `.staging/backups/` outside active plugin discovery paths, preventing stale or duplicate plugin registrations.
- **Dynamic Plugin Lifecycle Management**: Implemented `event EventHandler RegistryChanged` on `IPluginRegistry` and wired dynamic tool list updating, along with full Enable, Disable, and Uninstall actions in `PluginStorePage` and `PluginStorePageViewModel`.
- **In-Process Security Model & Capabilities**: Upgraded `PluginDetailDialog.xaml` with full-trust execution disclosures and permission reviews ("Declared Capabilities"), gating installations behind explicit user consent.
- **Developer CLI SDK (`tools/wincare-plugin-cli`)**: Created Node.js developer CLI (`wincare-plugin.js`) with `create`, `validate`, `lint`, and `pack` commands for JSON packs and C# binary plugins.

#### 2. AI System Doctor (DirectML & Local ONNX)
- **Local ONNX Inference Engine**: Integrated quantized ONNX model inference via `OnnxInferenceEngine` and `ModelManager` with DirectML GPU acceleration and sub-1.5s cold-start latency.
- **Natural-Language Intent Translation**: Implemented `IntentTranslator` mapping user symptom prompts into structured, validated `DoctorActionPlan` execution sequences.
- **Evidence-Grounded Two-Phase Diagnostics**: `AiDoctorPage.xaml` presents real-time diagnostic chat feeds and two-phase action cards where read-only evidence collection precedes mutative repairs with step-by-step confirmation.

#### 3. Rust 2024 Background Health Guard Service (`wincare-guard`)
- **High-Performance Guard Daemon**: Developed background monitoring crate (`native/wincare-guard`) with sub-millisecond RAM pressure, disk quota, and thermal monitors.
- **Autonomous Monitoring & Scaffolding**: Local threshold evaluations and Windows Toast XML notifications with C# IPC client scaffolding (`GuardPipeClient.cs`).
- **Windows Toast Notifications**: Implemented XML template-based Toast notification builder for immediate threshold breach alerts.

#### 4. Cloud Profile Sync & Source Generation
- **Encrypted Sync Provider**: Implemented AES-256-GCM profile payload encryption (`CryptoService.cs`) with PBKDF2/SHA-256 key derivation and `GitHubGistSyncProvider.cs` for multi-machine synchronization.
- **Roslyn Source Generation**: Added `CommandDispatcherGenerator.cs` for zero-allocation compile-time command handler registration.

#### 5. Native WinUI 3 Desktop Shell & Cyber-Teal Visual System
- **WinUI 3 Modernization**: Replaced legacy interfaces with an approved WinUI 3 shell using Fluent Mica surfaces, task-based navigation, responsive tabular lists, and UI Automation metadata.
- **Cyber-Teal Design Tokens**: Unified `#00D2B4` (Dark) and `#007A99` (Light) accent tokens with Cascadia Code monospaced telemetry pill badges (`[ READ-ONLY ]`, `[ MUTATING ]`, `[ ELEVATED ]`, `[ NOT READY ]`).
- **WCAG 2.1 AA Contrast Standard**: All status pill color pairs pass WCAG 2.1 AA (≥ 4.5:1), achieving up to 14.68:1 contrast.
- **Keyboard Ergonomics**: 3px high-contrast focus rings, `Ctrl+F` tool search focusing, and responsive layout collapsing at 920 DIP breakpoint.

#### 6. Fail-Closed Command Dispatcher & Native Migration
- **259/259 Command Parity**: Preserved all 259 stable legacy command IDs in a typed catalog with fail-closed native executor routing.
- **Parameter Validation & Preflight**: All 104 mutating commands preflight parameters during preview before approval can be granted.
- **PII-Safe Logging**: Exception logs record exception type names only, preventing accidental path or credential leakage.
- **Reparse-Point Safety**: Filesystem operations canonicalize paths, reject reparse-point operation roots, and skip symlink descendants.
- **Automated CI Release Pipeline**: Hardened `.github/workflows/` to automatically build MSIX, standalone single-file executables, and portable ZIP packages for `x64` and `ARM64`.

---

## [2.2.0] - Historical

- Final PowerShell/WPF release line, retained in Git history and the legacy oracle archive for controlled parity reference.

## [1.1.0] - Historical

- Historical module compatibility baseline retained for deterministic legacy packaging and validator contracts.
