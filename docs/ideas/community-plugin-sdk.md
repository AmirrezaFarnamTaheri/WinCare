# Community Plugin SDK & Developer CLI (`wincare-plugin-cli`)

## Problem Statement
How might we empower third-party developers, system administrators, and open-source contributors to easily scaffold, test, validate, and publish custom WinCare plugins without needing complex build setups or deep WinCare core internal knowledge?

## Recommended Direction
Develop `wincare-plugin-cli`, a Node.js-based cross-platform developer tool executable via `npx wincare-plugin`. The CLI provides four core commands: `create`, `validate`, `test`, and `pack`.

The CLI scaffolds two template formats: Declarative JSON script packs (PowerShell / CMD) and compiled WinUI 3 C# assembly `.dll` plugins. It enforces built-in security linting (blocking path traversal `../`, checking SemVer, verifying reverse domain IDs) before generating distribution-ready `.wincare-plugin` ZIP archives.

## Key Assumptions to Validate
- [ ] **Scaffolding Speed:** Developer can scaffold and run a custom PowerShell maintenance tool in under 3 minutes.
- [ ] **Linter Accuracy:** 100% of invalid manifests, broken relative paths, or illegal script commands are caught during `wincare-plugin validate`.
- [ ] **Packaging Compression:** `.wincare-plugin` ZIP archives remain under 5 MB for script packs.

## MVP Scope
### What's In
- `npx wincare-plugin create` wizard with `json-pack` and `csharp-plugin` templates.
- `npx wincare-plugin validate` manifest and security linter.
- `npx wincare-plugin pack` ZIP builder.

### What's Out
- Web-based graphical plugin builder (CLI-first approach).
- Automated cloud signing server.

## Not Doing (and Why)
- **Proprietary Scripting Language:** Avoided in favor of native PowerShell, CMD, and C# to leverage existing developer skills.
- **Mandatory Paid Developer License:** Kept 100% free and open-source to encourage maximum community contribution.

## Open Questions
- Should the CLI support automatic pull-request generation against the official `WinCare/plugin-catalog` repository?
