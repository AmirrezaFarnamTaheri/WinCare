# WinCare Community Plugin SDK & Developer CLI — Product & Technical Requirements Specification

- **Date:** 2026-08-14
- **Status:** Implementation-Ready Specification (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Implementation Plan:** `docs/plans/2026-08-14-community-plugin-sdk-plan.md`
- **Source:** `/ce-ideate` -> `/ce-brainstorm` -> `/ce-plan`

---

## 1. Executive Summary & Intent

To empower third-party developers, system administrators, and open-source contributors, WinCare will release the **Community Plugin SDK & Developer CLI (`wincare-plugin-cli`)**.

### Key Objectives
1. **Developer CLI Tooling:** Provide a cross-platform CLI tool (`npx wincare-plugin`) for scaffolding, testing, validating, and packaging `.wincare-plugin` archives.
2. **Template Generators:** Support templates for both Declarative JSON script packs (PowerShell / CMD) and compiled C# WinUI 3 assembly plugins.
3. **Automated Manifest & Security Linter:** Verify `wincare-plugin.json` schema accuracy, path traversal safety, permission requests, and icon asset resolution before packaging.

---

## 2. CLI Command Structure

```bash
# 1. Create new plugin project interactively
npx wincare-plugin create my-custom-cleaner

# 2. Validate plugin manifest and script paths
npx wincare-plugin validate ./my-custom-cleaner

# 3. Test plugin execution against local WinCare host
npx wincare-plugin test ./my-custom-cleaner

# 4. Package into .wincare-plugin ZIP archive ready for submission
npx wincare-plugin pack ./my-custom-cleaner --output ./my-custom-cleaner.wincare-plugin
```

---

## 3. Detailed Requirements

### Requirement 1: Scaffolding Templates
- **Template A (Declarative Script Pack):** Generates `wincare-plugin.json`, README, `scripts/sample.ps1`, and icon assets.
- **Template B (WinUI 3 C# Assembly Plugin):** Generates `.csproj`, `PluginEntryPoint.cs` implementing `IWinCarePlugin`, and a sample `IPluginWidget` WinUI 3 control.

### Requirement 2: Linter & Security Checker
- Ensures `id` follows reverse domain notation (`com.author.plugin_name`).
- Validates version string adheres to SemVer (`1.0.0`).
- Scans PowerShell/CMD scripts for prohibited system actions (e.g. formatting system drives, modifying boot configuration data) unless explicit permission flag is set.
- Enforces strict relative file paths within the plugin archive.

### Requirement 3: Packaging & Signing
- `wincare-plugin pack` creates a compressed ZIP container containing manifest, scripts, DLLs, and assets.
- Support optional developer RSA digital signatures for verified community plugins.

---

## 4. Success Criteria

- **Ease of Use:** Developer can scaffold and run a custom PowerShell maintenance plugin in under 3 minutes.
- **Validation Strictness:** 100% of invalid manifests, malformed JSON files, or unsafe relative paths are caught prior to packaging.
