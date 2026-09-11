# Evidence index

All artifacts are local to this audit. Read the main report for experiment limits and artifact identity.

Finalized documentation does not expand these experiments. See [package status](D:/GitHub/WinCare/docs/audit-2026-09-11/README.md) and [forensic qualifications](D:/GitHub/WinCare/docs/audit-2026-09-11/forensic-follow-up.md). Historical failed and incomplete experiments remain explicitly identified below.

| Evidence | Interpretation |
|---|---|
| [Inventory summary](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/inventory-summary.json) |342 files /259 commands /89 declarative controls |
| [Installed Checkup crash](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/Checkup-crash.log) |Fatal WinRT collection projection; initial alive UIA sample precedes crash |
| [Installed All tools crash](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/NavAllTools-crash.log) |Representative of seven installed collection-route failures |
| [Installed Doctor crash](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/NavAiDoctor-crash.log) |Missing InverseBooleanConverter |
| [Fresh-build Doctor crash](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/untrimmed-NavAiDoctor-crash.log) |Same missing resource in fresh local source build |
| [Fresh-build All tools UIA](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/untrimmed-NavAllTools-uia.json) |Page opens; repeated type-name row accessibility labels |
| [Selected tool editor UIA](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/tool-editor-uia.json) |Raw parameter expander remains; typed replacement absent |
| [Backend probe/state fixtures](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/backend-probes.json) |11 successful read-only queries, independent-store update failure, swallowed plugin persistence error |
| [Export fixture](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/export-fixture.json) |Shared helper IOException; no target file |
| [Completed idle samples](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/idle-60s.json) |12 samples over60seconds |
| [Resource/control probe outcome](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/resource-probe-outcome.json) |Owned process closed normally |
| [Startup action UIA](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/HomeStartupBoostButton-after.json) |Count and Audit Complete |
| [Network action UIA](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/HomeNetworkRefreshButton-after.json) |Interface count and Connected |
| [Untrimmed publish log](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/publish-untrimmed.txt) |Successful local publish |
| [Strict restored trimmed publish](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/publish-trimmed-restored.txt) |3 IL2026 errors; not a passing build |
| [Rust tests](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/rust-tests.txt) |53 passing tests |
| [Rust clippy](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/rust-clippy.txt) |Strict static checks |
| [Python native tests](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/python-native.txt) |94 run,1 skipped |
| [Plugin CLI tests](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/plugin-cli.txt) |9 run,1 skipped |
| [NuGet advisory query](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/nuget-vulnerabilities.json) |No findings returned; scope is that query |
| [Theme tokens](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/visual-tokens.txt) / [contrast](D:/GitHub/WinCare/docs/audit-2026-09-11/evidence/contrast.txt) |Static token checks, not full rendered-state proof |

Other `Nav*-navigation.json`, `Nav*-crash.log` and `Nav*-uia.json` files retain each installed navigation outcome. `untrimmed-*` records describe the fresh local build. `trimmed-*` from the initial no-restore attempt are byte-identical to untrimmed and must not be used as an independent trim comparison. `idle-interrupted.json` is an incomplete earlier run, not a completed60-second sample. Managed180-test results belong to the earlier audit in this same review and are summarized in `docs/review-2026-09-11.md`; no new log was invented for them.

The harness writes only under unique audit sandbox directories and executes an explicit read-only command whitelist. Its separate export-only mode calls the real JSON helper using harmless data. The native UI probes navigate pages and invoke only Startup/Network read-only actions; none applies Windows cleanup or administrative changes.
