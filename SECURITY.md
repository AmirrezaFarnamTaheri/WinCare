# WinCare Security Policy and Security Model

WinCare performs privileged, low-level, and potentially mutating Windows operating system tasks. Security, auditability, and deterministic failure are core architectural contracts.

---

## 🛡️ Vulnerability Reporting

If you discover a security vulnerability in WinCare, please do **not** disclose it publicly via GitHub Issues or discussions.

- **Reporting Channel**: Use [GitHub Private Vulnerability Reporting](https://github.com/AmirrezaFarnamTaheri/WinCare/security/advisories/new) or contact the project maintainers directly.
- **Report Contents**:
  - Affected version / commit SHA
  - Windows version, build number, and architecture (x64 / ARM64)
  - Execution context and privilege level (Standard User vs. Administrator)
  - Detailed reproduction steps with non-sensitive data
  - Observed impact and potential attack vector
  - Redacted logs or diagnostic evidence

We will acknowledge receipt within 48 hours and provide status updates as we investigate and remediate the issue.

---

## 🎯 Supported Versions

| Version Line | Supported | Notes |
|---|:---:|---|
| `2.5.x` | ✅ Yes | Current development and release candidate line |
| `2.4.x` | ✅ Yes | Supported release candidate baseline |
| `< 2.4.0` | ❌ No | Historical legacy releases (PowerShell/WPF) |

---

## 🔒 Core Security Invariants

### 1. Explicit Authority & Two-Phase Approval
- Observation does not grant mutation authority.
- Planning does not grant execution authority.
- Every mutating command requires an explicit two-phase approval sequence (`request.Apply = true` and `options.ReviewApproved = true`).
- UI previews perform parameter validation and preflight checks before the user can grant mutation authority.

### 2. Fail-Closed Operation
- Missing dependencies, unsupported architectures, denied elevation, unverifiable certificates, path traversal attempts, postcondition violations, native FFI errors, cancelled operations, and timeouts result in explicit blocked or failed states.
- Under no circumstances does a failed or unavailable operation fabricate a success result.

### 3. Bounded Resources & Process Limits
- Filesystem enumeration uses bounded iterative traversal (`BoundedProcessRunner` / native core).
- Recursive file traversals canonicalize roots, reject reparse-point operation roots, and skip symlink/junction descendants.
- Process execution uses strict argument lists rather than raw shell concatenation, preventing command injection.

### 4. PII-Safe Activity Journaling
- `CommandDispatcher` exception handlers log only exception type names (e.g. `UnauthorizedAccessException`, `FileNotFoundException`).
- `ex.Message` is never written to user-visible activity journals or telemetry, eliminating accidental path or PII exposure.

### 5. Plugin Trust & Cryptographic Package Admission
- Plugins execute in-process with user privileges; script-backed plugins run only after the
  dispatcher's two-phase preview → approve gate, and installed plugins are disabled until the
  user enables them.
- Manifest IDs must strictly equal the target package ID (`manifest.Id == targetPluginId`).
- **HTTPS** package downloads require a full-stream SHA-256 digest and a publisher key +
  signature supplied by the independently trusted catalog boundary. `file://` and
  direct-stream installs verify the SHA-256 digest when one is supplied (an omitted digest is
  accepted only for these non-remote paths).
- Package authenticity is established from an external sidecar signature (`wincare-plugin.sig`)
  verified against an RSA SHA-256 or ECDSA public key provided by the trusted catalog boundary.
  Inline manifest `signature` fields are not accepted (the signed bytes would otherwise include
  the signature itself).
- Every capability the manifest declares must be covered by the user's explicit per-capability
  consent at install; an empty consent set rejects any capability-bearing package.
- Revoked package IDs and publishers are enforced at install time, independent of catalog UI
  filtering.
- The installer records an admission digest (`.wincare-manifest.sha256`) that is re-verified on
  every load/discovery, so post-install manifest modification is detected.
- Core namespaces (`wincare.core.*`, `system.*`) are strictly reserved and cannot be overwritten.
- Plugin updates create isolated backups in `.staging/backups/` outside active discovery paths.

### 6. Rust FFI Boundary Safety
- All Rust FFI exports in `native/wincare-core` wrap execution in `std::panic::catch_unwind`.
- Unhandled panics in native Rust code cannot unwind across the C-ABI boundary into the .NET runtime, preventing undefined behavior and memory corruption.

---

## 🧱 Trust Boundaries

| Boundary | Invariant & Security Expectation |
|---|---|
| **User Input / Search** | Treated as untrusted text and validated against typed schema definitions |
| **WinUI 3 Presentation** | Presentation-only; cannot bypass dispatcher admission or safety checks |
| **Command Dispatcher** | Authoritative gate; rejects unknown, disabled, or unmigrated commands |
| **Plugin Subsystem** | Full-trust warnings, capability consent review, signature verification, and blocklist enforcement |
| **Filesystem Operations** | Canonical path containment, reparse-point rejection, and bounded enumeration |
| **External Processes** | Executable path resolution prefers Windows `System32`, bounded arguments, timeout enforcement |
| **Rust C ABI** | Versioned interface, caller-owned buffers, pointer validation, and unwind safety |
| **Cloud Sync** | AES-256-GCM authenticated encryption with PBKDF2/SHA-256 key derivation for all synced profiles |

---

## 🚫 Deliberately Excluded Behaviors

WinCare does not implement or ship:
- Covert network telemetry or background analytics without consent
- Unrestricted arbitrary remote script execution
- Kernel hooks, hidden drivers, or unverified driver signing bypasses
- Silent modification of Windows security settings without explicit review
