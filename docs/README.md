# WinCare Technical Documentation

This directory contains the authoritative architecture, design, security, migration, and development specifications for WinCare.

## 📑 Core Documentation Index

| Document | Description |
|---|---|
| [Project Overview](../README.md) | High-level system overview, feature pillars, quickstart, and readiness metrics |
| [User Guide](User-Guide.md) | Comprehensive end-to-end user manual, page walkthroughs, and FAQ |
| [Architecture Specification](Architecture.md) | Component responsibilities, dependency topology, plugin trust model, and FFI IPC |
| [Design System & Visual Spec](../DESIGN.md) | Cyber-Teal design tokens, typography, WCAG 2.1 AA contrast matrix, and MVVM standards |
| [Validation & Promotion Gates](../VALIDATION.md) | Evidence classifications, source test suites, and production release criteria |
| [Command Parity Ledger](migration/command-parity-ledger.md) | 259-command migration accounting, safety invariants, and oracle provenance |
| [Finalization Status](migration/finalization-status.md) | Release candidate status, current blockers, and deterministic artifact contracts |
| [Windows Validation Guide](migration/windows-validation.md) | Windows build, WinApp CLI, UI Automation, and package verification procedures |
| [Contributing Guidelines](../CONTRIBUTING.md) | Environment setup, ownership rules, code standards, and PR requirements |
| [Security Policy & Model](../SECURITY.md) | Security invariants, trust boundaries, and vulnerability disclosure policies |
| [Changelog](../CHANGELOG.md) | Release history, feature additions, and architectural changelog |
| [Third-Party Notices](../THIRD-PARTY-NOTICES.md) | Platform dependencies, licenses, and external attributions |

---

## 🏛️ Architectural Upgrade Plans (`docs/plans/`)

Detailed design and requirements documents for major architectural enhancements:

- **AI System Doctor**: [Requirements](plans/2026-08-14-ai-system-doctor-requirements.md) | [Implementation Plan](plans/2026-08-14-ai-system-doctor-plan.md)
- **Plugin Store & Modular Architecture**: [Requirements](plans/2026-08-14-plugin-store-and-modular-architecture-requirements.md) | [Implementation Plan](plans/2026-08-14-plugin-store-and-modular-architecture-plan.md)
- **Community Plugin SDK & CLI**: [Requirements](plans/2026-08-14-community-plugin-sdk-requirements.md) | [Implementation Plan](plans/2026-08-14-community-plugin-sdk-plan.md)
- **Rust Background Health Guard Service**: [Requirements](plans/2026-08-14-rust-background-guard-service-requirements.md) | [Implementation Plan](plans/2026-08-14-rust-background-guard-service-plan.md)
- **Plugin Store Component Upgrade**: [Plan](plans/2026-08-14-plugin-store-components-upgrade-plan.md)

---

> [!NOTE]
> Historical PowerShell operator documentation is not part of the native source artifact. It remains preserved for reference in the separately hashed legacy oracle archive.
