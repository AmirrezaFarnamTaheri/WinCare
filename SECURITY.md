# Security Policy

## Supported release

The current release line receives security fixes. Production use requires a package that passed the complete Windows and native validation workflow.

## Security architecture

- one typed and policy-governed mutation path;
- operation-specific approval and elevation;
- authenticated IPC and elevated result envelopes;
- closed source manifests and explicit exports;
- bounded process, network, filesystem, archive, JSON, and state-store operations;
- digest-pinned custom runtimes, models, programs, and executables;
- structural redaction before durable logging or telemetry storage;
- deterministic release manifests, SBOM, build receipts, checksums, and provenance;
- verified installation identity and conservative uninstallation.

## Reporting vulnerabilities

Report suspected vulnerabilities privately to the repository owner. Include the affected version, exact entry point, reproduction steps, impact, and any proof-of-concept data needed to validate the issue. Do not include real secrets or personal data.

## High-impact features

Critical actions are default-deny and require the applicable policy gate, exact acknowledgement, preview, and elevation. Some security, servicing, application-removal, and legacy compatibility changes can disrupt recovery or management. WinCare discloses non-recoverable steps and does not infer safety merely because an operating-system API accepted a request.

## Advanced adapters

ONNX, OpenSSL/ML-KEM, WASI, eBPF, WireGuard, cloud, and fleet functionality is dependency-aware and bounded. WinCare does not bundle optional runtimes, models, programs, credentials, or remote infrastructure. Missing, unverified, or incompatible dependencies fail closed.
