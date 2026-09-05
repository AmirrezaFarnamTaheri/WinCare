# Changelog & Release Notes

All notable changes to WinCare are documented in this file in accordance with [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) principles.

---

## [Unreleased]

### Security & Safety

- Made mutating approval a dispatcher-issued, parameter-bound, expiring, single-use capability; callers such as the System Doctor can no longer synthesize their own approval without a successful preview.
- Hardened plugin catalog trust, fresh-install re-resolution, publisher revocation, signed admission records, assembly-plugin rollback, uninstall recovery, and discovery-time signature verification.
- Made plugin upgrade rollback preserve the last known-good external admission record when trust-record restoration itself encounters an I/O or access failure, surfacing both failures instead of deleting recovery evidence.
- Hardened Guard named-pipe access control and retained fail-closed semantics for future mutating IPC.
- Versioned encrypted profile envelopes, retained legacy decryption compatibility, and raised PBKDF2-HMAC-SHA256 work factor to 600,000 iterations.
- Changed mutation-handler fault reporting to state when final machine state is unknown rather than implying that no change occurred.

### UX, Accessibility & Reliability

- Reworked Home, Activity/Reports, Help, Plugin Store, Checkup, Doctor, settings, and window-continuity behavior around truthful state, visible degraded/error modes, safer destructive actions, and evidence-driven recovery.
- Added typed All Tools parameter editors generated from command contracts while keeping raw JSON as an explicit Advanced escape hatch.
- Standardized compact behavior around the shared breakpoint, added real Checkup compact states, and made measurement-sensitive Checkup probes explicitly sequential without globally serializing the dispatcher.
- Tightened accessible theme tokens, automation metadata, and validation guidance for High Contrast, Narrator, keyboard use, and 100–225% Windows text scaling.
- Replaced polling/synchronous persistence patterns with event-driven refresh and queued/coalesced persistence where appropriate, while surfacing durability failures.

### Build, Supply Chain & Repository Quality

- Pinned .NET SDK 8.0.416 exactly with feature-band roll-forward and prerelease SDKs disabled; Rust and GitHub Actions remain pinned as well.
- Committed normal NuGet dependency lockfiles for all eight solution projects and RID-specific portable publish lock variants for all five source projects on `win-x64` and `win-arm64`.
- Enabled CI locked restore plus NuGet auditing of all transitive packages at moderate-or-higher severity; audit warnings are not suppressed.
- Expanded Dependabot coverage to GitHub Actions, NuGet, Cargo, and npm.
- Added CODEOWNERS and a pull-request template covering safety/trust, accessibility, supply chain, exact verification evidence, documentation truth, and residual risk.
- Expanded finalized native-source evidence to include NuGet configuration, infrastructure security tests, repository review guardrails, all committed dependency lock graphs, complete linked documentation/assets, and the plugin developer CLI with its tests.
- Added structural regressions for deterministic toolchains, dependency graph drift, plugin admission rollback, portable publish locking, finalized-source completeness, responsive behavior, typed inputs, and approval provenance.

## [2.5.0-rc5] - 2026-08-31

### Fixed

- **Functional Settings**: Replaced the placeholder settings table with a persistent System/Light/Dark theme control, immediate theme application, and a working local-data-folder action.
- **Actionable capability pages**: System Care, Security, and Repair & Recovery now open the real filtered command catalog for the selected section instead of ending at descriptive rows.
- **Shared in-shell routing**: Consolidated page-to-shell navigation so Home and capability actions keep the sidebar and content frame synchronized.

## [2.5.0-rc4] - 2026-08-31

### Fixed

- **Live check results**: Corrected one-time bindings that prevented completed check states and evidence details from updating onscreen.
- **Reliable Checkup lifecycle**: Results now populate independently of the selected tab, the running state always clears after failures, and unimplemented Full/Custom modes no longer masquerade as usable features.
- **AI Doctor startup**: Constructed its view model before compiled bindings initialize, eliminating a startup-time null binding path.
- **Fresh activity and clean page lifetimes**: Cached Activity refreshes from the journal on every visit, while disposable tool and plugin-store models release subscriptions and cancellation sources when leaving their pages.
- **Working Home actions**: Replaced disconnected Home tabs with direct, sidebar-synchronized navigation to Checkup, Activity, and All Tools.
- **Interaction regression coverage**: Added a structural gate that rejects button-like controls without a command, click handler, or navigation target.

## [2.5.0-rc3] - 2026-08-30

### Fixed

- **Functional Checkup**: Restored the Quick check action as a real read-only execution pipeline. It now runs native system, storage, security, and Windows Update checks and displays each actual outcome instead of presenting an inert dashboard.

## [2.5.0-rc2] - 2026-08-30

### Changed

- **Native Dashboard Refresh**: Reworked the Home and System Checkup views around a dark, card-based desktop dashboard with transparent status states before a system check has run.
- **Runtime Documentation**: Added real native-runtime captures alongside the design-concept references, so the project documentation shows the application rather than mockups alone.

## [2.5.0-rc1] - 2026-08-30

### 🔒 Security, Signing & Packaging Hardening
- **Immutable Tag-Gated Releases**: CI release pipelines trigger strictly upon tag pushes (`refs/tags/v*`), validating tag versions against checked-in metadata and preventing release mutations.
- **Runner-Local MSIX Code Signing**: Removed release-blocking signing-secret dependencies. Each architecture is signed with a temporary runner-local development certificate, verified (including a tamper-rejection check), and published with its matching public `.cer` file.
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

#### 2. AI System Doctor (Rule-Based On-Device Classification)
- **Rule-Based Intent Classification Engine**: `RuleBasedIntentInferenceEngine` classifies symptom prompts with deterministic keyword rules on-device; initialization is a near-zero-cost no-op (no model weights, no ONNX/DirectML runtime).
- **Natural-Language Intent Translation**: Implemented `IntentTranslator` mapping user symptom prompts into structured, validated `DoctorActionPlan` execution sequences drawn strictly from the native catalog.
- **Evidence-Grounded Two-Phase Diagnostics**: `AiDoctorPage.xaml` presents real-time diagnostic chat feeds and two-phase action cards where read-only evidence collection precedes mutative repairs with step-by-step confirmation.

#### 3. Rust 2024 Background Health Guard Service (`wincare-guard`)
- **High-Performance Guard Daemon**: Developed background monitoring crate (`native/wincare-guard`) with RAM pressure, disk quota, and thermal (cooling-mode) monitors.
- **Named-Pipe Health Endpoint**: Real named-pipe IPC server on `\\.\pipe\WinCareGuardIPC` answering `ping`/`health` for the C# `GuardPipeClient`.
- **Windows Toast Notifications**: Implemented an XML template-based Toast notification builder, raised on critical threshold breaches via the daemon alert path.

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
