# Third-Party Notices

WinCare itself is licensed under the [Apache License 2.0](LICENSE). This file summarizes major external platform and library dependencies used to build or run the project. Each dependency remains governed by its own license and distribution terms.

| Component | Project / publisher | License or terms |
|---|---|---|
| Windows APIs and Windows SDK | Microsoft | Microsoft Windows SDK terms |
| Windows App SDK / WinUI 3 | Microsoft | MIT |
| .NET 8 runtime and SDK | .NET Foundation / Microsoft | MIT |
| CommunityToolkit.Mvvm | .NET Community Toolkit | MIT |
| coverlet.collector 10.0.1 | Coverlet contributors | MIT |
| Microsoft.CodeAnalysis.CSharp 4.8.0 | Microsoft | MIT |
| Microsoft.NET.Test.Sdk 17.11.1 | Microsoft | MIT |
| Microsoft.Win32.Registry 5.0.0 | Microsoft | MIT |
| System.Diagnostics.EventLog 8.0.1 | Microsoft | MIT |
| xunit 2.9.3 | xunit project | Apache-2.0 |
| xunit.runner.visualstudio 2.8.2 | xunit project | Apache-2.0 |
| Rust toolchain and standard library | Rust Project | Apache-2.0 / MIT |
| serde / serde_json | serde project | Apache-2.0 / MIT |
| sha2 | RustCrypto | Apache-2.0 / MIT |
| tempfile | Rust project ecosystem | Apache-2.0 / MIT |
| Python 3 | Python Software Foundation | PSF License |
| Node.js 18+ | OpenJS Foundation / Node.js contributors | MIT |

The repository does not intentionally distribute private signing keys, package caches, or development secrets. NuGet and Cargo dependencies are resolved from the checked-in manifests and lock data used by the build and release workflows.

For an exact release, the dependency manifests, lockfiles, packaged artifacts, and applicable third-party license files are the authoritative source for the dependency set shipped with that release.
