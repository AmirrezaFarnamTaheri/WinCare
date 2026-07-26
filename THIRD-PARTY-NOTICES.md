# Third-Party Notices

WinCare is licensed under the Apache License 2.0. The source distribution does not bundle third-party repositories, research archives, package caches, models, eBPF programs, WASI modules, cloud agents, or external command-line runtimes.

## Platform dependencies

WinCare integrates with operating-system and framework components that are installed separately and remain governed by their own licenses:

- Microsoft Windows APIs and administration modules;
- PowerShell 7.2 or later;
- .NET 8 when native WinCare components are built from source;
- Pester and PSScriptAnalyzer in validation environments.

## Optional operator-supplied dependencies

Some advanced capabilities can use separately installed software. WinCare does not redistribute these components and requires dependency discovery, bounded execution, and digest verification where a custom path is accepted:

- OpenSSL for ML-KEM operations;
- Wasmtime or Wasmer for WASI execution;
- eBPF for Windows tooling;
- WireGuard command-line tooling;
- ONNX Runtime through the source-built WinCare host and an operator-approved model.

The applicable license must be reviewed before installing or distributing any optional dependency. Generated SBOM and build-receipt files describe the exact content of each WinCare release artifact.
