# Language and dependency ownership

| Surface | Language/runtime | Ownership rule |
|---|---|---|
| TUI, providers, policy, transactions, automation, elevation | PowerShell 7.2+ | Authoritative Windows runtime and one mutation path |
| Audio endpoint COM interop | C# source loaded by PowerShell | Narrow, deterministic interop only; no separate service or package |
| Static cross-file/convergence validation | Python 3 standard library | Build-time only; no WinCare runtime dependency |
| Deterministic ZIP/SBOM/provenance build and verification | Python 3 standard library plus PowerShell wrapper | Release-time only; PowerShell build remains the Windows entry point |
| Data catalogs, policies, evidence | JSON/CSV/Markdown | Non-executable, schema-validated inputs and evidence |

No donor language runtime was introduced. UWP, C++, Rust, JavaScript, Dart, and Node donor packaging were not carried into the target because their useful semantics fit existing target boundaries without paying a new runtime, signing, packaging, debugging, or incident-response cost.
