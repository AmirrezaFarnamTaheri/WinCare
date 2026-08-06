# WinCare security policy and model

WinCare performs privileged and potentially destructive Windows operations. Security is a product contract, not an optional hardening layer.

## Reporting a vulnerability

Do not publish an undisclosed vulnerability, exploit, credential, machine identifier, or sensitive log in a public issue or pull request. Use GitHub private vulnerability reporting or a private maintainer channel. Include the affected version, Windows build and architecture, privilege level, reproduction steps with non-sensitive data, observed impact, and redacted evidence.

## Supported security scope

Security fixes target the current default branch, the latest supported production release, and release tooling used to produce supported artifacts. WinCare 2.4.0-rc1 is a source release candidate and is not a supported production release.

## Core invariants

### Explicit authority

Observation does not imply mutation authority. Planning does not imply execution authority. Search, navigation, UI state, Rust results, and catalog presence cannot grant elevation or mutation authority.

### Fail closed

Missing dependencies, unsupported platforms, denied policy, unverifiable identity, unsafe paths, failed postconditions, native errors, cancelled work, timeouts, and incomplete migration remain explicit failures or blocked results. They never become success.

### Bounded resources

Filesystem traversal, archive processing, process output, network responses, event collection, and native buffers require explicit limits. Unsafe archive paths, links, collisions, duplicates, suspicious expansion, and unmanifested content are rejected.

### Exact identity

Security-sensitive operations bind to canonical paths, hashes, manifests, receipts, product identity, admitted executables, or provider-specific identifiers. Display names and mutable ambient state are not authority.

### No implicit egress

WinCare does not infer remote destinations, upload hidden telemetry, attach interceptors, or broaden network authority as a fallback. Network operations require admitted endpoints and bounded request and response behavior.

### Evidence before assurance

Source inspection, successful compilation, configuration state, and a smoke test are not command-equivalence evidence. Production promotion requires command-by-command Windows behavior verification.

## Trust boundaries

| Boundary | Security expectation |
|---|---|
| User input | parsed as data and validated against typed command contracts |
| WinUI | presentation only; cannot bypass dispatcher admission or safety policy |
| Command dispatcher | rejects unknown, disabled, unavailable, and unmigrated commands |
| Filesystem | canonical containment, link rejection, bounded enumeration, and identity re-checks |
| External process | exact executable identity, bounded arguments and output, timeout, and exit evidence |
| Network | explicit target admission, redirect and credential restrictions, and response ceilings |
| Rust ABI | versioned interface, explicit lengths, bounded buffers, status codes, and no business policy |
| Archive and package | safe paths, deterministic entries, manifest closure, hashes, signatures, and provenance |
| Elevation | narrow operation-specific broker after review, never shell access |
| Recovery | durable journal, explicit partial-failure state, and compensation when supported |

## Deliberately excluded behavior

WinCare does not claim or ship covert interception, kernel patching, automatic unsigned-driver installation, arbitrary remote command execution, hidden telemetry, unrestricted plugins, or autonomous production changes.

## Release integrity

A development artifact is not a production release. Promotion requires the exact artifact bytes to pass the source, Windows runtime, command parity, deterministic package, signature, installation, repair, upgrade, and uninstall gates in [Validation](VALIDATION.md) and [finalization status](docs/migration/finalization-status.md).

## Diagnostics

Before sharing diagnostics, remove credentials, keys, user and host names, internal URLs, and personal paths unless essential. Preserve timestamps, status codes, hashes, schema versions, and bounded error context needed for reproduction. Use a private channel for exploit material or sensitive evidence.
