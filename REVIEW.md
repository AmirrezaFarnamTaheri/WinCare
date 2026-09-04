# WinCare — Deep Code & Architecture Review

**Date:** 2026-09-04 · **Branch:** `arena/01a06cc8-wincare` · **Base:** `711558f` (`master`)

> This is an independent, from-the-code review of the whole repository: C# backend
> (Domain / CommandCatalog / Application / Infrastructure / SourceGenerators / App),
> the Rust native layer (`wincare-core`, `wincare-guard`), the WinUI 3 frontend, the
> Python validation/CI tooling, the Node plugin CLI, the test suites, and the docs.

---

## 1. Verdict at a glance

WinCare is a **genuinely well-engineered, unusually disciplined codebase**. The layered
architecture is clean, the fail-closed command plane is real (not marketing), the
path-traversal / reparse-point / FFI-safety / bounded-process work is consistently
applied, and the static verification tooling is more serious than most production apps.
The dominant theme of the findings below is therefore *not* "this is broken" — it is
"there is a gap between what the docs/marketing claim and what the code actually does,
concentrated in the plugin-script and AI-Doctor paths, plus a pile of small hygiene
drift."

The **most important issue** is a genuine safety-model violation: declarative plugin
scripts are executed on *preview* (`Apply=false`), breaking the app's own
observe → preview → approve → execute invariant for third-party code.

---

## 2. What is genuinely good (so the rest is read in context)

- **Layering is enforced, not aspirational.** `Domain` → `Application` →
  `Infrastructure` → `App` with a strict dependency direction; `Infrastructure`
  implements `Application` contracts (`ICommandOperationExecutor`,
  `INativeCoreService`, `IPluginInstallerService`). The `WindowsCommandExecutor` is
  `internal` and exposed through an interface.
- **Fail-closed dispatcher.** `CommandDispatcher.ExecuteAsync` blocks unknown,
  unmigrated, unregistered, read-only-but-applied, unapproved, and expired commands
  with typed result codes, before any handler runs. Cancellation is a linked token
  source with a per-command deadline budget.
- **Hardened process execution.** `BoundedProcessRunner` uses `ArgumentList` (no shell
  interpolation), redirects both streams, bounds output to a character cap, enforces a
  hard timeout, kills the whole process tree, and observes reader tasks on cancellation.
- **Path-containment discipline.** `CommandStateStore.PathFor`, `PluginInstallerService`
  ZIP extraction, `JsonPluginLoader`, `PluginScriptCommandHandler`, and
  `PluginRegistryService.InstantiateAssemblyPlugin` all canonicalize and verify
  containment; ZIP extraction counts entries and uncompressed bytes; the Rust
  `dir_size` uses an iterative (non-recursive) walk and skips symlinks/reparse points.
- **FFI hygiene.** Rust exports are `#[unsafe(no_mangle)]`, wrapped in
  `catch_unwind`, validate pointers/lengths, and every `unsafe` block has a `// SAFETY:`
  comment. The C# wrapper bounds the probe/fill retry loop for `sys_info`.
- **Data parity gates.** `commands.json` (259) / `remediation-rules.json` (69) /
  `presets.json` (7) exactly match a frozen oracle and are validated at load time with
  strict schemas, duplicate-ID checks, and enum/pattern validation.
- **Test & CI discipline.** 65 Python tests, Rust unit + FFI + guard tests, an
  end-to-end C# plugin SDK round-trip that *compiles a plugin with Roslyn at test time*,
  SHA-pinned GitHub Actions, tamper-rejection smoke test for the signed MSIX, and
  WCAG contrast checks. All currently-passing checks ran green in this environment.

---

## 3. Findings

Severity key: 🔴 High (correctness/security) · 🟠 Medium (design gap / doc-vs-code) ·
🟡 Low (hygiene).

### 🔴 H1 — Plugin scripts execute on *preview*; the two-phase gate does not apply to them

`PluginScriptCommandHandler.ExecuteAsync` never reads `request.Apply`. It always
launches the script (`.ps1`/`.cmd`/`.bat`/binary). The dispatcher's safety path for
core commands routes `Apply=false` to `MutationPreview`, but dynamic plugin commands
are handled by this class, so for a non-read-only script plugin:

1. User clicks **"Review changes"** → `CommandRequest.Preview(...)` (`Apply=false`) →
   **the script actually runs and mutates the system.**
2. User then approves → the script runs a second time.

This directly breaks the README/SECURITY.md invariant *"read the preview, understand
the impact, then approve deliberately"* for third-party declarative plugins.

- **Location:** `src/WinCare.Infrastructure/Plugins/PluginScriptCommandHandler.cs`
- **Suggested fix:** return a non-mutating preview (script path, interpreter,
  parameters, declared capabilities) when `request.Apply == false`, and only launch the
  process when `request.Apply == true`. Add a regression test asserting the script is
  **not** executed on preview.

### 🔴 H2 — Plugin `risk` / `readOnly` / capabilities are self-asserted and unenforced

`PluginToolDefinition.ToCommandDefinition` converts author-declared `risk`,
`readOnly`, `administratorAccess` straight into a `CommandDefinition` that the
dispatcher and UI treat as authoritative. `declaredCapabilities` is parsed but never
checked against what a script actually does. Script plugins run **in-process with the
user's full privileges** (correctly documented), but the only "enforcement" is:
read-only plugins may not be *applied*; non-read-only plugins need a preview+approval —
which H1 shows the preview doesn't protect. Net effect: a malicious or buggy script
plugin is one click away from arbitrary user-privilege mutation.

- **Location:** `src/WinCare.Application/Plugins/PluginManifest.cs`
  (`ToCommandDefinition`), `src/WinCare.Infrastructure/Plugins/PluginScriptCommandHandler.cs`
- **Suggested fix:** treat plugin-declared risk as *advisory display metadata only*;
  require explicit, per-capability consent at install; prefer an out-of-process,
  capability-restricted host for scripts (or at minimum surface H1's preview fix and
  keep scripts behind the existing "disabled by default" stance).

### 🔴 H3 — `BuiltInDelegatingHandler` forwards a mismatched approval plan (built-in mutating aliases always blocked)

For built-in plugin tools that alias core commands, `BuiltInDelegatingHandler.ExecuteAsync`
re-dispatches to `_targetCoreCommandId` but reuses the caller's `request.Approval`, which
was minted against the *plugin* command id. The inner dispatcher validates
`ApprovedMutationPlan.IsValid(targetCoreId, …)`, the `CommandId` mismatches, and the
call is blocked with `command.approval_plan_invalid`. Mutating built-in aliases
(e.g. `cleaner.system_temp` → `cleaner-disk-pressure`) can therefore never apply.

- **Location:** `src/WinCare.Application/Plugins/PluginRegistryService.cs`
  (`BuiltInDelegatingHandler`, near the end of the file)
- **Suggested fix:** mint a fresh `ApprovedMutationPlan` (or drop `Approval` and rely on
  `ReviewApproved` with a re-preflight) for the mapped core request, or re-validate the
  alias-level approval before translation.

### 🟠 M1 — "AI Doctor" is a keyword matcher, not ONNX/DirectML (docs claim otherwise)

`OnnxInferenceEngine.PredictIntentAsync` performs `string.Contains(...)` heuristics
(`"dns"`, `"ram"`, `"update"`…). There is **no ONNX runtime or DirectML package**
anywhere in `Directory.Packages.props` or the code; `InitializeAsync` is a 25 ms
`Task.Delay` with the comment *"Simulating fast DirectML session initialization"*;
`ModelManager.IsModelAvailable` checks for a `doctor.onnx` that is never downloaded or
loaded. Yet `docs/migration/finalization-status.md` (milestone 4) and
`docs/Architecture.md` (§5) claim "DirectML-accelerated local ONNX inference".

- **Location:** `src/WinCare.Application/Diagnostics/OnnxInferenceEngine.cs`,
  `ModelManager.cs`; claims in `docs/Architecture.md`, `docs/migration/finalization-status.md`
- **Suggested fix:** either implement real ONNX inference or relabel the feature as
  "rule-based intent classifier" in all docs. Do not claim a capability the binary
  doesn't contain.

### 🟠 M2 — AI Doctor can only execute parameter-less steps

`AiDoctorPageViewModel.ExecuteStepAsync` always builds parameters `{}`, and
`IntentTranslator` never populates `ProposedActionStep.Parameters`. Every
parameterized command (`security-control-reduce`, `pagefile-set`, `wua-install`, …)
will be blocked by `ValidateCommandParameters` with `command.parameters_invalid`.

- **Location:** `src/WinCare.App/ViewModels/Pages/AiDoctorPageViewModel.cs`,
  `src/WinCare.Application/Diagnostics/IntentTranslator.cs`
- **Suggested fix:** have the translator emit concrete `Parameters` per step, and
  surface parameter-missing as a reviewable prompt rather than a silent blocked run.

### 🟠 M3 — Integrity is verified at *install*, never at *load/discovery*

`PluginInstallerService` verifies SHA-256/signature during package admission, but
`JsonPluginLoader` / `PluginRegistryService` re-read the manifest at discovery with
no re-hash or signature check. A package (or a local plugin directory) modified after
install is loaded and enabled with no integrity re-verification.

- **Location:** `src/WinCare.Application/Plugins/JsonPluginLoader.cs`,
  `src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`
- **Suggested fix:** persist the admission digest in the plugin directory and re-check
  it (at least for the manifest) on every discovery.

### 🟠 M4 — Signature verification is fragile around inline `signature`

The manifest model has no `signature` property; the installer reads it manually from
raw JSON. When the signature lives *inside* the manifest, the "signed bytes" include the
signature field itself — a self-referential construct that is brittle (editing the
signature invalidates the signature) and invites confusion versus the external
`wincare-plugin.sig` form. The `.sig` path is sound; the inline path should be removed
or made unambiguous.

- **Location:** `src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`
  (`manifestSignature` extraction and `VerifyManifestSignature`)

### 🟠 M5 — `wincare-guard` daemon/IPC/toast are scaffolds, presented as milestones

- `ipc/pipe_server.rs`: `start()`/`stop()` only toggle an `AtomicBool`; no pipe is ever
  created. `GuardIpcMessage`/`PIPE_NAME` are unused.
- `monitors/thermal.rs`: `is_throttling` is hard-coded `false`; only AC-line state is read.
- `notifications/toast.rs`: XML is generated but **nothing calls it**; `main.rs` only
  `println!`s. The daemon is a console loop, not a Windows service (no SCM integration
  despite the `service.rs` doc comment).
- `docs/migration/finalization-status.md` (milestone 2) and `docs/Architecture.md` (§6)
  describe "Windows Toast XML notifications" and a health daemon as delivered.

- **Location:** `native/wincare-guard/src/**`
- **Suggested fix:** wire toasts to the alert path, implement the pipe server, or
  downgrade these claims to "scaffold / not yet functional".

### 🟠 M6 — Version label drift across metadata and docs

Single source of truth (`Directory.Build.props`) says **2.5.0-rc5**, but:
`docs/migration/finalization-status.md` header = 2.5.0-**rc1**; `tools/release_checklist.py`
fallback = **rc1**; `VALIDATION.md` example = **rc1**; `README.md` / `docs/Screenshots.md`
caption the screenshots as v2.5.0-**rc2**; `docs/README.md` = **rc1**;
`Package.appxmanifest` hard-codes `Version="2.5.0.6"`.

- **Suggested fix:** derive the version in all docs/tools from
  `Directory.Build.props` (as the workflows already do), and make the `.appxmanifest`
  version a build-time substitution (`-p:Version`) rather than a literal.

### 🟠 M7 — Compile-time command routing is generated but unused

`WinCare.SourceGenerators` emits `GeneratedCommandDispatcher.TryRouteStatic`, but no
runtime code calls it (verified by grep). The dispatcher uses hand-built dictionaries.
The "zero-overhead compile-time routing" is dead weight — either wire it into the
dispatcher or drop the generator and its tests.

- **Location:** `src/WinCare.SourceGenerators/CommandDispatcherGenerator.cs`,
  `src/WinCare.Application/Commands/CommandDispatcher.cs`

### 🟠 M8 — Favorites/Recent/activity are in-memory only

- `AllToolsPageViewModel` keeps `_favoriteIds`/`_recentIds` in RAM; they are lost on
  restart despite `CloudProfilePayload.FavoriteCommandIds` and `AppPreferences`
  existing for exactly this purpose.
- `ActivityJournalService` is an in-memory list; the "audit trail" resets every launch
  and the Activity page's **Reports** section is permanently empty.

- **Location:** `src/WinCare.App/ViewModels/Pages/AllToolsPageViewModel.cs`,
  `src/WinCare.Application/Activity/ActivityJournalService.cs`

### 🟠 M9 — Remote plugin installation is fully disabled in the UI

`PluginStorePageViewModel.InstallPluginAsync` unconditionally returns false with
"Remote plugin installation is disabled for this release." That is a defensible RC
gate, but it means the entire `PluginInstallerService` cryptographic pipeline is
currently unreachable from the product, and the plugin store "Install" affordance is
dead UI. The architecture doc's revocation gap (§4.2 — "enforcement inside the
installer and registry remains an explicit security gap") compounds this: when the
gate opens, revocation won't be enforced.

- **Location:** `src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs`

### 🟠 M10 — Doc/code mismatch on package hashing

`SECURITY.md` states *"Every remote and local package is validated with full-stream
SHA-256 integrity verification."* In code, SHA-256 is **mandatory only for HTTPS**
downloads; `file://` and direct-stream installs accept an optional digest.

- **Location:** `SECURITY.md` vs `PluginInstallerService.InstallPluginFromPackageAsync`

### 🟠 M11 — Startup crash risk in Debug without a staged native DLL

`AppRuntime` always constructs `NativeCoreService` and passes it to
`CommandDispatcher`, whose constructor immediately P/Invokes
`wincare_core_abi_version()`. The Release build enforces the DLL's presence via an
MSBuild target, but a Debug/F5 run without `Native/<Platform>/wincare_core.dll` staged
will `DllNotFoundException` at startup with no user-facing message
(`App.OnUnhandledException` only `Debug.WriteLine`s).

- **Location:** `src/WinCare.App/Services/AppRuntime.cs`,
  `src/WinCare.Application/Commands/CommandDispatcher.cs`,
  `src/WinCare.App/App.xaml.cs`

### 🟡 L1 — Committed AI/tooling scaffolding

`.context/` (a "compound-engineering" spec about the *legacy 172-file PowerShell module*),
`.impeccable/` (UI critique logs), and `idea-stage/` (an IDEA report) are tracked in Git
and are not covered by `.gitignore`. They are tooling artifacts, not project content,
and the `.context` spec documents a pre-migration world that no longer exists.

### 🟡 L2 — `packages.lock.json` is gitignored despite `RestorePackagesWithLockFile=true`

`Directory.Build.props` enables lock files, but `.gitignore` lists `packages.lock.json`,
so the lock file is never committed and cannot provide reproducible restores.

### 🟡 L3 — Native crate config inconsistency

`wincare-core` has `[lints.rust]`/`[lints.clippy]`; `wincare-guard` has none.
`rust-version = "1.85"` (MSRV) vs `rust-toolchain.toml` pinned to `1.97.1`.

### 🟡 L4 — Registry handle leak in a LINQ expression

`WindowsCommandExecutor.Productivity.cs:81` — `item?.OpenSubKey("command")?.GetValue(null)`
opens a `RegistryKey` that is never disposed.

### 🟡 L5 — Dead branches in `ToolRowViewModel`

`RiskPillLabel` and `StatusPillBackgroundResourceKey` switch on `"Elevated"`,
`"Mutating"`, `"High Risk"` — strings the `Risk` property can never produce (it only
emits `Read-only/Low/Moderate/High/Critical`). Leftover from an earlier enum model.

### 🟡 L6 — `PluginManifest` alias-property surface is a footgun

`assemblyEntry` shares backing state with `assemblyFileName` but `AssemblyEntry`'s
getter returns `_assemblyFileName`; `entryClass`/`pluginClassName`, `title`/`name`,
`summary`/`description`, `risk`/`riskLevel`, `executorType`/`executionType`,
`scriptPath`/`script` all overlap. There is no validation that the two spellings agree,
which invites silent mis-serialization.

### 🟡 L7 — Remediation `maxBuild` is validated then silently dropped

`RemediationCatalogValidator.ValidateCompatibility` accepts and validates `maxBuild`,
but `RemediationCompatibility` has no `MaxBuild` property, so the value is discarded on
deserialization.

### 🟡 L8 — `GuardPipeClient` recreates a buffered `StreamReader` per request

Each `SendCommandAsync` builds a new `StreamReader(…, leaveOpen: true)` over the same
pipe; a reader that buffers beyond the first line will discard the remainder when
disposed. Also no maximum read length. Works for a single-line protocol, fragile
otherwise.

### 🟡 L9 — Plugin manifest reads are unbounded

`JsonPluginLoader.LoadFromDirectory` uses `File.ReadAllText` with no size limit and a
reflection-based `JsonSerializerOptions` (contrast with the bounded, source-generated
`EmbeddedJsonResource` used for the catalogs).

### 🟡 L10 — `RemoteCatalogService` offline default ships a placeholder digest

The offline fallback catalog lists `sha256 = e3b0c442…` (the SHA-256 of an empty
string) labeled as "verified sample metadata."

### 🟡 L11 — `AcquirePluginOperationLockAsync` busy-waits unbounded

The cross-process lock loop retries every 50 ms with no maximum wait and no delay
observe on cancellation beyond the token check.

### 🟡 L12 — `dependabot.yml` comment drift

The comment says "All four workflow files" — only two exist.

### 🟡 L13 — `InitializePluginsAsync` caches a cancelled task

`_pluginInitializationTask ??= InitializePluginsCoreAsync(ct)` returns the same task to
all callers; a cancelled first token leaves a cancelled cached task (only exceptions
reset it). `App.xaml.cs` additionally swallows init failures with only a debug write.

### 🟡 L14 — Health score is never computed

`CheckupPageViewModel` always sets `HealthScoreText = "—"` after a check, and the
"Results" section re-lists the *Quick check* rows instead of distinct results. The
README's "score remains unavailable" note is accurate but the UI implies evidence
collection will produce one.

### 🟡 L15 — Minor time/kind hazards

`TelemetryEvidence.IsStale` compares `DateTime.UtcNow` against a `TimestampUtc` that
may be an unspecified-kind `DateTime` from `CapturedAtUtc`; `MainWindow.xaml`
hard-codes `RequestedTheme="Dark"` then overrides it from preferences (initial flash).

---

## 4. Backend notes (Domain / Application / Infrastructure)

- `ApprovedMutationPlan` canonicalizes JSON (recursive key sort) before hashing and
  uses `stackalloc` + `ArrayPool` — good. But note `CommandCatalog.Find` is
  ordinal-sensitive while `IsValid` compares command IDs ordinal-insensitively; make
  the comparers consistent.
- `CommandDispatcher.RegisterDynamicCommand` does not verify that
  `handler.CommandId == definition.Id` (the static path does). Add that check.
- `WindowsCommandExecutor` routes all 259 IDs with a `_ => Block(...)` fallback for
  read-only and a `throw` for mutations — good fail-closed posture. The
  `MutationPreview` affected-resources map is hand-maintained and covers only a few
  commands; the other ~90 mutating commands preview as a generic
  `{resourceType:"SystemState", target:"HostMutation"}` payload, which under-delivers
  on the "affected paths" preview promise in `DESIGN.md`.
- `CommandStateStore` is correct (temp-file + move, containment, single gate), but the
  single `SemaphoreSlim` serializes *all* keys; consider per-key or reader/writer
  locking if download/telemetry/workspace state grows. `FlushAsync` does not
  `Flush(true)` (no fsync) — acceptable, but the "no partial writes" claim rests on
  the rename, not on durability.
- `CryptoService` (PBKDF2-SHA256 100k + AES-256-GCM) is reasonable; the envelope has no
  version byte, so future format changes need out-of-band migration.
- `GitHubGistSyncProvider` is unreachable from the UI (no settings wiring) — the cloud
  sync feature exists only as infrastructure + tests.

## 5. Frontend notes (WinUI 3)

- MVVM is largely clean (`CommunityToolkit.Mvvm`, `{x:Bind}`, no XAML types in
  view models — enforced by a Python test). Code-behind is minimal and focused.
- `AllToolsPageViewModel` re-materializes the entire row list on every debounced
  keystroke (clear + re-add + re-sort of ≤259 items); fine at this scale but the
  merged-catalog cache is invalidated on any `RegistryChanged` even when the plugin
  set is unchanged.
- Accessibility metadata and the `ThemeResourceBrushConverter` boundary are thoughtful;
  the contrast/token verification scripts back them up.
- `App.OnUnhandledException` (L11/M11) and `MainWindow.HandleProtocolActivation`
  (silently ignores malformed URIs) leave no user-visible error path.

## 6. Performance & resource notes

- Startup path is genuinely probe-free (verified by the structural tests).
- Reflection-based JSON options are used in several hot paths (`ToolExecutionViewModel.ResultJsonOptions`,
  executor `JsonOptions`, `CommandStateStore.JsonOptions`) where the source-generated
  contexts exist only for domain/catalog types; consider extending source-gen to the
  remaining DTOs.
- `ToolCatalogService.Search` does a full scan + sort per keystroke; a precomputed
  lowercase search index would be trivial and future-proof.
- Rust `dir_size` saturating-adds sizes and caps entries at 500k — good; be aware
  `wincare_core_dir_size` reports `Truncated` (still writes a partial total) which the
  C# layer surfaces as an exception, so a large scan never yields a misleading number.

## 7. Testing & CI

- Impressive breadth: parity gates, safety regressions, a Roslyn-compiled plugin
  round-trip, FFI contract tests, deterministic packaging, MSIX tamper-rejection, and
  contrast checks. All Python gates pass here (65/65).
- Managed tests run only on x64 in CI (`if: matrix.platform == 'x64'`); ARM64 is
  build+package only — reasonable, but say so explicitly (it already does).
- Add regression tests for: H1 (plugin script not run on preview), H3 (built-in alias
  approval), and M3 (load-time integrity re-check).

## 8. Documentation & consistency

- Docs are extensive and mostly accurate, but the ONNX/DirectML (M1), guard-daemon
  toast/IPC (M5), "every local package SHA-256" (M10), and version labels (M6) claims
  need correction. `docs/migration/command-parity-ledger.md` is admirably honest about
  `BehaviorVerified = 0/259`.

---

## 9. Prioritized remediation roadmap

1. **Fix H1** (plugin preview must not execute) and add a regression test — this is the
   one issue that invalidates a core safety promise.
2. **Fix H3** (built-in alias approval) or document aliases as preview-only.
3. **Resolve the AI-Doctor claims (M1/M2)** — either ship real inference or relabel.
4. **Re-verify plugin integrity at load (M3)** and harden the signature model (M4).
5. **Reconcile docs/tools/version labels (M6)** and make the MSIX version build-derived.
6. **Decide the fate of dead artifacts (M7, L1, L2)** — wire or remove the source
   generator, drop/gitignore the AI scaffolding, commit the lock file.
7. **Close the trust gaps before enabling remote installs (H2, M9)** — capability
   consent + installer-side revocation enforcement.
8. **Persist favorites/recent/activity (M8)** to make the audit-trail promise real.

---

*Prepared by an automated deep review of commit `711558f` on branch
`arena/01a06cc8-wincare`. File paths are relative to the repository root.*
