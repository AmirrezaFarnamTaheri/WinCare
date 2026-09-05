# Contributing to WinCare

Thank you for your interest in contributing to **WinCare**! We welcome contributions that preserve WinCare's safety, evidence, performance, accessibility, and platform contracts.

---

## 🛠️ Required Development Environment

The complete development environment requires:

- **Operating System**: Supported 64-bit Windows 10 (version 2004 / build 19041+) or Windows 11
- **.NET SDK**: Exactly .NET SDK **8.0.416**, as pinned by `global.json` (feature-band roll-forward and prerelease SDKs are disabled)
- **Windows App SDK**: Build tools and WinApp CLI
- **Rust Toolchain**: Rust stable (1.97.1) with `x86_64-pc-windows-msvc` and/or `aarch64-pc-windows-msvc` targets
- **Python**: Python 3.11+ (Python 3.13 recommended) for structural validation, contrast checks, and deterministic packaging
- **Node.js**: Node.js 18+ (for developer plugin CLI under `tools/wincare-plugin-cli`)
- **Windows Developer Mode**: Enabled in Windows Settings for local WinUI 3 loose package execution

> [!NOTE]
> PowerShell is not a dependency of the native source project. Historical PowerShell source exists solely in the separate legacy oracle archive used for controlled parity work.

---

## 📐 Project Ownership & Language Boundaries

To maintain clean separation of concerns and avoid duplicated logic:

| Language | Responsibility & Domain |
|---|---|
| **C# (.NET 8)** | WinUI 3 presentation, MVVM view models, command catalog admission, dynamic plugin host, AI Doctor diagnostic engine, activity journaling, recovery workflows, and packaging |
| **Rust 2024** | Bounded native system primitives (`sys_info`, `dir_size`, `sha256`) via versioned C ABI (`wincare-core`) and low-overhead background health monitoring service (`wincare-guard`) |
| **Python 3.13** | Source validation gates, visual token and WCAG AA contrast verification, parity-fixture comparison, and deterministic release packaging |
| **Node.js / JS** | Developer Plugin CLI toolkit (`tools/wincare-plugin-cli`) for scaffolding, linting, validating, and packaging community extensions |

- **No Business Logic Duplication**: Do not implement duplicate business rules across C# and Rust.
- **No PowerShell Runtimes**: Do not introduce PowerShell runtime components or script wrappers into the native source tree.

---

## 🔄 Contribution Workflow

1. **Create a Branch**: Create a focused topic branch from `master` (e.g. `feature/my-enhancement` or `fix/issue-description`).
2. **Review Specifications**: Read [README.md](README.md), [DESIGN.md](DESIGN.md), [docs/Architecture.md](docs/Architecture.md), and the relevant migration ledger in [docs/migration/](docs/migration/).
3. **Test-First Discipline**: Write a unit test covering the invariant or behavior before implementing changes.
4. **Implement Minimally**: Make the smallest, most focused change that satisfies the requirement.
5. **Keep Dependencies Reproducible**: NuGet lockfiles are committed. Normal CI restore runs in locked mode and audits all transitive packages for moderate-or-higher known vulnerabilities. When intentionally changing a NuGet dependency, update the affected `packages.lock.json` files in the same pull request and review the resolved transitive graph.
6. **Run Local Verification**:
   ```bash
   # 1. Native structural gate
   python tools/verify_native_foundation.py

   # 2. Python test suite
   python -m unittest discover -s tests/native -v

   # 3. Rust format, lints, and tests
   cargo fmt --manifest-path native/Cargo.toml --all -- --check
   cargo clippy --manifest-path native/Cargo.toml --all-targets --all-features -- -D warnings
   cargo test --manifest-path native/Cargo.toml

   # 4. Plugin CLI tests
   python tests/tools/test_plugin_cli.py -v

   # 5. Visual token & contrast checks
   python tools/verify_visual_tokens.py
   python tools/verify_pill_contrast.py

   # 6. Verify the committed NuGet graph before building/testing
   dotnet restore WinCare.Native.sln -p:Platform=x64 --locked-mode

   # 7. .NET C# tests
   dotnet test WinCare.Native.sln -c Release -p:Platform=x64 --no-restore
   ```
7. **Windows Runtime Validation**: If touching UI or Windows integration, execute the Windows build and validation steps in [docs/migration/windows-validation.md](docs/migration/windows-validation.md).
8. **Document Changes**: Update relevant documentation and [CHANGELOG.md](CHANGELOG.md) within the same pull request.

---

## 📋 Command Parity & Migration Rules

A command advances through four immutable states:
1. `Cataloged`: Stable command ID, summary, risk rating, and metadata defined.
2. `ContractVerified`: Typed request/result contracts and frozen oracle parity fixture verified.
3. `Implemented`: Native fail-closed route exists and validates parameters before approval.
4. `BehaviorVerified`: Behavior executed and validated against live Windows behavior.

> [!CAUTION]
> Never mark a command `BehaviorVerified` from code inspection alone. Live execution evidence is strictly required.

---

## 📝 Pull Request Guidelines

Every pull request must include:
- **Summary**: Concise description of what changed and why.
- **Scope Impact**: Affected subsystems (WinUI, Dispatcher, Plugins, Rust Core, CLI, Packaging).
- **Verification Evidence**: Exact commands/runs used and their results; do not claim unexecuted validation.
- **Safety Impact**: State whether command mutation, approval, plugin trust, IPC, persistence, signing, or recovery boundaries changed.
- **UX/Accessibility Impact**: For UI changes, describe keyboard, automation metadata, contrast, text scaling, and responsive-layout implications.
- **Dependency Impact**: Include lockfile changes for intentional NuGet dependency updates and explain any audit warnings.
- **Residual Risk**: Any assumptions, limitations, or pending Windows-only validation.

Do not weaken or bypass tests, trust checks, locked restore, vulnerability auditing, or validation gates merely to make CI green. Fix the underlying contract or document a narrowly justified exception for review.
