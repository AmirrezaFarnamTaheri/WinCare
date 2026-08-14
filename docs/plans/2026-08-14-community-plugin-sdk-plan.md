# WinCare Community Plugin SDK & Developer CLI — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Completed (`artifact_readiness: completed`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/plans/2026-08-14-community-plugin-sdk-requirements.md`

---

## 1. Overview & Architecture Summary

This plan details the technical implementation of `wincare-plugin-cli`, the official developer CLI tool for scaffolding, linting, testing, and packaging WinCare plugin archives (`.wincare-plugin`).

---

## 2. Implementation Units & Task Breakdown

### Unit 1: CLI Core & Interactive Scaffolding Templates
**Goal:** Build Node.js CLI tool (`tools/wincare-plugin-cli`) with interactive `create` wizard for JSON script packs and C# assembly plugins.

- **Files touched / created:**
  - `tools/wincare-plugin-cli/bin/wincare-plugin.js`
  - `tools/wincare-plugin-cli/src/commands/create.js`
  - `tools/wincare-plugin-cli/package.json`

- **Acceptance Criteria:**
  - [x] `node wincare-plugin.js create <name>` prompts for plugin metadata and scaffolds template directory.
  - [x] Generates valid `wincare-plugin.json` adhering to WinCare schema.

- **Verification Commands:**
  - `python tests/tools/test_plugin_cli.py -v`

---

### Unit 2: Manifest Linter & Security Checker
**Goal:** Implement `wincare-plugin validate` command verifying manifest fields and script path safety.

- **Files touched / created:**
  - `tools/wincare-plugin-cli/src/commands/validate.js`
  - `tools/wincare-plugin-cli/src/linter/manifestLinter.js`

- **Acceptance Criteria:**
  - [x] Checks SemVer format, reverse domain ID, and required catalog fields.
  - [x] Rejects absolute script paths and relative path traversal (`../`).

- **Verification Commands:**
  - `python tests/tools/test_plugin_cli.py -v`

---

### Unit 3: Plugin Packager & Archive Generator
**Goal:** Implement `wincare-plugin pack` producing `.wincare-plugin` ZIP archives.

- **Files touched / created:**
  - `tools/wincare-plugin-cli/src/commands/pack.js`
  - `tools/wincare-plugin-cli/src/packager/zipBuilder.js`
  - `tests/tools/test_plugin_cli.py`

- **Acceptance Criteria:**
  - [x] Runs linter automatically before packaging.
  - [x] Outputs compressed `.wincare-plugin` archive ready for WinCare Plugin Store installation.

- **Verification Commands:**
  - `python tests/tools/test_plugin_cli.py -v`
