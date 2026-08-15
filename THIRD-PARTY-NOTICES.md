# Third-Party Software Notices & Attributions

WinCare is licensed under the [Apache License, Version 2.0](LICENSE).

---

## 📦 Platform & Framework Dependencies

The native WinCare application and toolchains rely upon separately licensed platform components and open-source libraries:

| Component / Library | Publisher / Project | License |
|---|---|---|
| **Microsoft Windows APIs & WinAppSDK** | Microsoft Corporation | MIT / Proprietary Windows SDK |
| **WinUI 3 (Windows App SDK)** | Microsoft Corporation | MIT License |
| **.NET 8 Runtime & SDK** | .NET Foundation / Microsoft | MIT License |
| **CommunityToolkit.Mvvm** | .NET Community Toolkit | MIT License |
| **Microsoft.ML.OnnxRuntime.DirectML** | Microsoft Corporation | MIT License |
| **Rust Toolchain & Standard Library** | Rust Project Developers | Apache 2.0 / MIT License |
| **Windows-rs (`windows` crate)** | Microsoft Corporation | MIT / Apache 2.0 License |
| **Python 3 Standard Library** | Python Software Foundation | PSF License |

---

## 🛡️ Distribution Isolation & Provenance

The distributable native source archive does not bundle third-party binary caches, development certificates, or signing keys. All NuGet and Cargo dependencies are resolved deterministically from declared project manifests (`WinCare.Native.sln`, `Directory.Packages.props`, and `native/Cargo.toml`).

The legacy oracle archive contains historical WinCare migration reference source only and is excluded from the native execution runtime.
