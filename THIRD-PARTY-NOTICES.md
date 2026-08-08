# Third-Party Notices

WinCare is licensed under the Apache License 2.0.

The native source project depends on separately licensed platform and development components:

- Microsoft Windows and Windows APIs
- Windows App SDK and WinUI 3
- .NET 8
- Rust toolchain and standard library
- Python for deterministic source validation and packaging
- NuGet and Cargo packages declared by the source manifests

The native source archive does not bundle external repositories, package caches, development certificates, signing keys, or optional command-line runtimes. The legacy oracle archive contains historical WinCare source only and is not a native runtime dependency.

Production package SBOM and provenance generation must describe the exact binaries and dependencies included in the signed release candidate.
