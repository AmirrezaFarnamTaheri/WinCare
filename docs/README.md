# WinCare documentation

This directory is the product-facing and engineering reference for WinCare. Start with the guide that matches your task instead of working through historical plans.

## Use the app

| Document | Purpose |
|---|---|
| [User guide](User-Guide.md) | Install safely, navigate the app, understand approvals, and troubleshoot. |
| [Interface screenshots](Screenshots.md) | Runtime captures from v2.5.0-rc5 alongside the original design concepts. |
| [Release page](https://github.com/AmirrezaFarnamTaheri/WinCare/releases) | Current x64 and ARM64 packages, portable archives, certificates, and release notes. |

## Build or review it

| Document | Purpose |
|---|---|
| [Architecture](Architecture.md) | Ownership boundaries, command lifecycle, plugin admission, native integration, and packaging. |
| [Validation](../VALIDATION.md) | Evidence categories, repeatable checks, and the production-promotion gate. |
| [Security](../SECURITY.md) | Threat boundaries, reporting route, and safety invariants. |
| [Contributing](../CONTRIBUTING.md) | Setup, test expectations, and review workflow. |
| [Finalization status](migration/finalization-status.md) | Current release-candidate state and the remaining production gate. |

## Reference material

- [Command parity ledger](migration/command-parity-ledger.md) — native catalog accounting and evidence limits.
- [Windows validation](migration/windows-validation.md) — Windows packaging and runtime-validation procedures.
- [Design system](../DESIGN.md) — visual tokens, accessibility, and UI conventions.
- [Plugin admission diagram](diagrams/plugin-admission.html) — interactive trust-flow companion to the architecture guide.
- [`plans/`](plans/) — dated design records retained for historical context; they are not current implementation instructions.

> [!NOTE]
> Historical PowerShell material remains isolated in the separately hashed legacy-oracle release archive. It is not included in the native runtime source tree.
