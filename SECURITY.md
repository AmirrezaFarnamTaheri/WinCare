# WinCare security policy and model

WinCare performs privileged and potentially destructive Windows operations. Security is therefore a product contract, not an optional hardening layer.

## Reporting a vulnerability

Do not publish an undisclosed vulnerability, exploit, credential, machine identifier, or sensitive log in a public issue or pull request.

Use GitHub's private vulnerability-reporting or Security Advisory workflow when it is available for this repository. If no private reporting control is visible, contact the repository maintainer through a private channel and request a secure disclosure path without including exploit details in the initial public message.

A useful report includes:

- affected version, branch, or commit;
- affected Windows edition/build and architecture;
- required privilege and user interaction;
- reproducible steps using non-sensitive test data;
- expected versus observed authority or outcome;
- impact and realistic attack preconditions;
- logs, traces, or proof-of-concept material with secrets removed;
- suggested mitigation when known.

Please allow maintainers time to reproduce, remediate, validate, and prepare coordinated disclosure before publishing details.

## Supported security scope

Security fixes target:

- the latest supported release;
- the current default branch;
- release and installation tooling used to produce or validate supported artifacts.

Older commits, development artifacts, dirty-source packages, locally modified installations, and unsupported Windows environments may be useful for diagnosis but are not equivalent to a supported production release.

## Core security invariants

### Explicit authority

Observation does not imply mutation authority. Planning does not imply execution authority. Local execution does not imply elevation, network transmission, remote control, persistence, or deployment authority.

Mutating operations must use the governed action/transaction path and require explicit approval or `-Apply` semantics as applicable.

### Fail-closed results

Missing dependencies, unsupported platforms, denied policies, unverifiable identities, unsafe paths, failed postconditions, and nonzero process results must remain explicit. WinCare does not report synthetic success.

### Bounded resources

Filesystem traversal, archive processing, process output, network responses, event collection, and generated artifacts must have explicit limits. Reparse points, unsafe archive paths, case collisions, duplicates, suspicious expansion, and unmanifested content are rejected where those conditions affect authority or integrity.

### Exact identity

Security-sensitive operations bind to canonical paths, hashes, manifests, receipts, product identity, admitted executables, or provider-specific identifiers. Display names and mutable ambient state are not sufficient authority.

### No implicit egress

WinCare does not infer remote destinations, silently upload telemetry, attach hidden interceptors, or broaden network authority as a fallback. Network operations require admitted endpoints and bounded request/response behavior.

### Protected state and secrets

Credentials, keys, tokens, and sensitive state must not be committed to the repository or emitted into ordinary logs. Protected records must preserve their envelope, identity, access-control, size, and lifecycle contracts.

### Evidence before assurance

Configuration state must not be overstated as a guaranteed security property. Observed VBS, HVCI, Credential Guard, DMA, Secure Boot, TPM, BitLocker, or policy settings are evidence about configuration and provider state; they are not proof that every possible compromise is absent.

## Trust boundaries

| Boundary | Security expectation |
| --- | --- |
| User input | Parsed as data, validated against explicit command/action contracts, and never treated as arbitrary code |
| Filesystem | Canonical containment, no-follow behavior, reparse rejection, bounded enumeration, and identity re-checks where required |
| External process | Exact executable discovery/identity, bounded arguments, timeout, captured output ceiling, and exit-code evidence |
| Network | Explicit target admission, credential/redirect restrictions, response limits, and no silent fallback |
| Archive/package | Safe member paths, duplicate/collision rejection, size ceilings, manifest closure, hashes, receipts, and provenance |
| Optional runtime | Dependency detection and digest/identity evidence; no marketplace or auto-install authority |
| UI/TUI/headless surfaces | Shared backend contracts; presentation layers do not gain independent mutation authority |
| Standalone executable | Cryptographically bound embedded payload, verified extraction/cache, and restricted argument forwarding |
| Installer/uninstaller | Product/path/manifest/receipt binding, managed-tree validation, transactional shortcuts, and guarded corrupt-install recovery |
| Fleet/cloud adapter | Explicit peers/endpoints and credentials; no general-purpose remote administration service |

## Deliberately excluded behavior

WinCare does not claim or ship:

- covert interception or redirection;
- packet desynchronization or censorship-bypass behavior;
- kernel patching;
- automatic unsigned-driver installation;
- arbitrary remote command execution;
- hidden background telemetry;
- autonomous production patch deployment;
- an unrestricted plugin marketplace;
- a guarantee that configuration evidence proves compromise absence;
- a security boundary based only on launching a process.

See [Advanced Capabilities](docs/Advanced-Capabilities.md) for capability-specific boundaries.

## Security review requirements for changes

Changes require explicit security review when they add or expand:

- mutation or elevation authority;
- filesystem deletion, relocation, staging, or recursive traversal;
- process creation, IPC, service, task, or driver behavior;
- network egress, remote peers, credentials, or cloud adapters;
- archive extraction, package verification, installation, or update behavior;
- protected state, cryptography, key material, signatures, or policy enforcement;
- plugin/runtime loading, native interop, binary inspection, or code execution;
- recovery, compensation, or corrupt-install removal.

The review must include negative-path and adversarial tests, not only a successful example.

## Release integrity

A development package is not a production release. Production promotion requires the exact artifact bytes to pass the mandatory Windows, native-build, deterministic-package, independent-verification, installation, repair, and uninstall evidence gates defined in [Validation](VALIDATION.md) and [Release Engineering](docs/RELEASE.md).

## Security-related logs and diagnostics

Before sharing diagnostics:

1. remove credentials, tokens, keys, user names, host names, internal URLs, and personal paths when they are not essential;
2. preserve timestamps, exit codes, hashes, schema versions, and bounded error context needed to reproduce the issue;
3. do not upload complete protected-state stores, memory captures, or private forensic collections to public issues;
4. use a private disclosure channel for exploit material or sensitive evidence.
