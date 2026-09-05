## Summary

<!-- What changed, why, and what user/developer problem does it solve? -->

## Scope impact

- [ ] WinUI / UX
- [ ] Command dispatcher / execution
- [ ] Plugins / catalog trust
- [ ] Guard / IPC / Rust native core
- [ ] Persistence / recovery
- [ ] Packaging / signing / release
- [ ] Dependencies / build tooling
- [ ] Documentation only

## Safety and trust review

- [ ] Mutating operations still require a successful preview and dispatcher-issued single-use approval.
- [ ] Inputs are validated before mutation and failure does not overstate final machine state.
- [ ] Plugin/catalog changes preserve fail-closed trust, revocation, admission, and rollback behavior.
- [ ] IPC/security changes preserve explicit authentication/ACL boundaries and bounded I/O.
- [ ] Destructive or irreversible behavior has an explicit user confirmation/recovery story.
- [ ] Not applicable; this change does not touch a safety or trust boundary.

## UX and accessibility review

- [ ] Keyboard and focus behavior checked.
- [ ] Automation names/IDs remain meaningful.
- [ ] Contrast, High Contrast, text scaling, and narrow-window behavior considered.
- [ ] Loading, empty, offline, failure, and degraded states remain distinguishable.
- [ ] Not applicable; no user-facing UI changed.

## Dependency and supply-chain review

- [ ] `global.json` / Rust / Actions pins remain intentional and reproducible.
- [ ] NuGet lockfiles are updated only when dependency resolution intentionally changes.
- [ ] Known-vulnerability audit warnings were not suppressed to make CI pass.
- [ ] Not applicable; dependency graph unchanged.

## Verification evidence

<!-- List exact commands and/or GitHub Actions runs. Do not claim tests that were not executed. -->

- 

## Documentation / product truth

- [ ] User-facing copy and docs match the implemented guarantees.
- [ ] Validation docs/screenshots were updated if their evidence changed.
- [ ] CHANGELOG updated when appropriate.

## Residual risk / unverified scenarios

<!-- Windows-only behavior, production certificate install/upgrade, physical ARM64, Narrator/text scaling, etc. -->

- 
