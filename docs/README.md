# WinCare documentation

This directory is the product-facing and engineering reference for WinCare. Start with the guide that matches the task at hand; obsolete implementation trackers and source-ingest notes are intentionally not part of the maintained documentation set.

## Use the app

| Document | Purpose |
|---|---|
| [User guide](User-Guide.md) | Install safely, navigate the app, understand approvals, and troubleshoot. |
| [Interface screenshots](Screenshots.md) | Runtime captures and interactive showcase previews for the current release candidate alongside design references. |
| [Release page](https://github.com/AmirrezaFarnamTaheri/WinCare/releases) | Current x64 and ARM64 packages, portable archives, certificates, and release notes. |

## Build, validate, or review it

| Document | Purpose |
|---|---|
| [Architecture](Architecture.md) | Ownership boundaries, command lifecycle, plugin admission, native integration, and packaging. |
| [Windows validation](Windows-Validation.md) | Repeatable Windows build, package, runtime, accessibility, and release-validation procedure. |
| [Validation](Validation.md) | Evidence categories, repeatable checks, and the production-promotion gate. |
| [Security](../SECURITY.md) | Threat boundaries, reporting route, and safety invariants. |
| [Contributing](../CONTRIBUTING.md) | Setup, test expectations, and review workflow. |

## Reference material

- [Interactive Web Showcase](showcase.html) — interactive diagnostic simulation and telemetry visualization.
- [Architecture Blueprint](architecture.html) — C4 model interactive governance diagram matching canonical architecture.
- [Terminal REPL Preview](terminal-preview.html) — historical terminal exploration concept; WinCare ships as a WinUI 3 desktop application.
- [Design system](../DESIGN.md) — visual tokens, accessibility, and UI conventions.
- [Plugin admission diagram](diagrams/plugin-admission.html) — interactive trust-flow companion to the architecture guide.

Compatibility fixtures under `migration/oracle/` are test/reference data only. They are not runtime source, user documentation, or a roadmap, and they must not be treated as implementation authority.
