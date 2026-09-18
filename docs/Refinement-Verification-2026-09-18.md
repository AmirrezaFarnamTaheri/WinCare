# Refinement verification — 2026-09-18

## Scope and verdict

This is a bounded implementation and verification pass in the approved `worktrees/task-first-refinement` worktree, based on HEAD `983efff2c1266f0dec0807d2d21ef260c1f7c094`, with substantial pre-existing uncommitted changes preserved. It is not an exhaustive audit, release approval, or proof of every Windows command's behavior. No commits or publishing were performed.

## Application fixes implemented in this pass

- Plugin mutations have a host-owned admission floor of Moderate. Read-only semantics, stricter tiers, and invalid explicit tiers are preserved. Native core commands are not globally reclassified. The policy is applied to manifest conversion, host registration, and dispatcher dynamic registration.
  - `src/WinCare.Application/Plugins/PluginCommandPolicy.cs` (new)
  - `src/WinCare.Application/Plugins/PluginManifest.cs`
  - `src/WinCare.Application/Plugins/DefaultPluginHost.cs`
  - `src/WinCare.Application/Commands/CommandDispatcher.cs`
  - `tests/WinCare.Application.Tests/PluginAdmissionRefinementTests.cs` (new)
- Journal, registry, and catalog notifications isolate each subscriber, outside their state locks, and log listener faults.
  - `src/WinCare.Application/Activity/ActivityJournalService.cs`
  - `src/WinCare.Application/Plugins/PluginRegistryService.cs`
  - `src/WinCare.Application/Tools/ToolCatalogService.cs`
- Journal snapshots are enqueued while holding the record lock, preserving mutation order; disk I/O remains on the asynchronous continuation. The concurrent flush/reload test passes, but did not deterministically reproduce the old race.
- Plugin lifecycle invocation is deferred into the exception boundary. Synchronous shutdown failure no longer skips disposal, and synchronous disposal failure no longer escapes cleanup. In-process synchronous hangs cannot be forcibly timed out by this helper.
  - `tests/WinCare.Application.Tests/ApplicationReliabilityRefinementTests.cs` (new; event, persistence, lifecycle regressions)

The admission tests initially failed twice for the expected Safe/Moderate mismatch, then passed all five cases. Three event/lifecycle regressions failed before their fixes. The lifecycle regression was separately rerun by its exact name and passed after the full Application suite.

## Integrated parallel work

The infrastructure worker reported the borrowed-HttpClient ownership fix in `src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs`, with two passing inert tests in `tests/WinCare.Infrastructure.Tests/HttpClientOwnershipTests.cs`. The parent has not independently rerun that test project. The worker disclosed running its focused managed test despite the parent-build ownership instruction.

The release worker changed `tools/finalize_native_release.py`, `tools/stage_release_assets.py`, `.github/workflows/native-winui.yml`, `tests/native/test_finalization.py`, `tests/native/test_toolchain_determinism.py`, and `docs/Release-Readiness.md`. It reported archive-input completeness, repeated-manifest CLI parsing, and artifact-specific digest binding fixes; its source-archive extraction passed the then-current 145-test Python suite. Cargo locking was already present and was preserved. Exact-asset attestations remain disabled because upstream verification returned HTTP 403; no duplicate publishing workflow was restored.

Presentation changes are present in the shared tree. The parent inspected unchanged-row reuse versus changed-definition invalidation and high-contrast color-cache clearing. `tests/WinCare.Application.Tests/PresentationRefinementTests.cs` is covered by the passing Application suite; `tests/native/test_presentation_refinement.py` is covered by the passing Python suite. These tests support the string-list round-trip behavior and refute the reported corruption for the tested array/string cases. The presentation agent could not be reattached after session continuation, so no final worker completion report was obtained. This is not a visual or accessibility runtime sign-off.

## Parent-observed verification

Run from the approved worktree:

| Command | Observed result |
|---|---|
| `dotnet test tests/WinCare.Application.Tests/WinCare.Application.Tests.csproj -c Release -p:Platform=x64 --no-restore --verbosity quiet` | 208 passed, 0 failed, 0 skipped |
| Same test project, filter `FullyQualifiedName~Synchronous_shutdown_failure_still_attempts_disposal`, `--no-build --no-restore` | Exact regression passed, 1/1 |
| `python -m unittest discover -s tests -t . -q` | 151 tests, OK, 2 skipped; exit 0 |
| `python tools/verify_native_foundation.py` | Passed structural gate |
| `python tools/verify_visual_tokens.py` | Passed token checks |
| `python tools/verify_pill_contrast.py` | All eight tested static color pairs passed 4.5:1 |
| `git diff --check` | Passed |
| `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64 --no-restore --verbosity quiet` | Succeeded, 0 errors, 3 warnings |

Build warnings: two WMC1506 notification-support warnings at `src/WinCare.App/Controls/WidgetContainer.xaml:21–22`, plus missing `mspdbcmf.exe`, which prevents symbols-package generation. These were not suppressed or repaired in this pass.

## Open findings and limits

- Download scheduler pause/resume stale-attempt race remains open.
- Disk cache/state streaming bounds remain open; the infrastructure report's proposed pre-read length checks alone would not guarantee a streaming bound against concurrent file growth.
- Rust release `panic = "abort"` remains inconsistent with recoverable in-process panic handling. Cargo was not on the current shell PATH; Rust gates were not run.
- Some native tests invoke real host operations, including file-cache trimming. The complete solution test command was deliberately not executed blindly. No Windows maintenance commands were used for verification.
- Other infrastructure review claims (masking fidelity/performance, script argument semantics, cancellation ownership, clipboard ordering, IPC peer identity, archive expansion bounds) need independent confirmation before implementation; report severity is not adopted as established fact.
- No fresh locked restore, package install, desktop launch, Narrator run, theme-switch interaction, scaling inspection, ARM64 runtime check, or production release validation was performed by the parent in this pass.
- Historical cloud-sync/crypto/model-manager/source-generator and obsolete publisher/runtime removals remain retired; no evidence justified reinstating them.
- The infrastructure and application reviews explicitly left substantial source unread. The original broad overhaul request therefore remains only partially complete.

## Infrastructure suite regression fix (parent pass)

The Infrastructure suite had not been run by the refinement passes. Running it surfaced
11 failures that the per-project Application runs could not see. Both were fixture gaps
introduced by the reliability pass's new security gates, not defects in the gates:

- Nine tests dropped a raw `wincare-plugin.json` into a temp user-writable plugins
  directory. Discovery now fails closed without an external admission record
  (`JsonPluginLoader.LoadFromDirectory(..., requireAdmissionRecord: !isBuiltIn)`), so
  those packages were correctly refused and never registered. The affected tests assert
  discovery behavior, not the missing-trust rejection, so they now install a package the
  way the plugin store does.
- Two tests exercise mutating commands declared `AdministratorAccess.Required` through
  `Apply`, which the new admission-time elevation gate blocks in a non-elevated process.
  They now pass the `isProcessElevated` test hook that the reliability pass added to the
  `CommandDispatcher` constructor for exactly this purpose.

Edits are test-only, in `tests/WinCare.Infrastructure.Tests/`:
`PluginRegistryServiceTests.cs` (shared `WritePluginWithAdmission` helper + 8 call sites +
elevation override), `CommandSafetyTests.cs` and `PluginSecurityRegressionTests.cs`
(elevation override and an admission record binding the compiled assembly digest). No
production source was changed. No assertion was weakened: every gate still fires in
production, the fixtures simply establish trust the way the installer does.

| Command | Observed result |
|---|---|
| `dotnet test tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj -c Debug --nologo` | Before: 11 failed / 295 passed. After: 306 passed, 0 failed |
| `dotnet test tests/WinCare.Application.Tests/... -c Debug --no-build --nologo` | 208 passed, 0 failed |
| `dotnet test tests/WinCare.CommandCatalog.Tests/... -c Debug --nologo` | 24 passed, 0 failed |
| `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64 --nologo -v m` | Succeeded, 0 errors, 3 warnings (same pre-existing set) |
| `python -m unittest discover -s tests -t . -q` | 151 tests, OK, 2 skipped |

Managed total: 538 tests passing across three projects. The same three build warnings
(WMC1506 x2, missing `mspdbcmf.exe`) persist and were not repaired.
