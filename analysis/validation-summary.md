# WinCare 5.2 convergence validation summary


## Wave eleven: D164-D166 validation boundary

The eleventh convergence wave adds D164 WindowsCleaner, records D165 as an exact duplicate of D143, and adds D166 QuickLook. The cumulative evidence graph contains **166 submitted donor IDs**, **153 unique archive baselines**, **52,629 classified surfaces**, **615 semantic decisions**, **436 composition groups**, **355 target nodes**, and **172 convergence test nodes**.

Linux-side validation covers the complete source/evidence graph, bounded preview and cleanup contracts, operator routes, GUI catalog integration, evidence hashes, and release tooling. It does not claim Windows-native execution of file deletion, junction relocation, WPF rendering, shell integration, Registry operations, or PowerShell/Pester behavior.

## Wave-six additions

The source/evidence gates cover D87-D105, exact package/tool admission, Studio routes and exports, folder recovery, new policy gates, and the Critical Xbox acknowledgement/feature/registry constants. Windows-native execution remains pending and is not inferred from static validation.

## Verified evidence and source gates

- **166 submitted donor IDs**, **153 unique archive baselines**, and **52,629** per-surface classifications and hashes.
- **615** semantic decisions across **436** composition groups.
- **355** target nodes and **172** convergence test nodes.
- Complete D01-D166 submission coverage with no orphan surface or decision; exact duplicate submissions retain provenance without creating duplicate implementation claims.
- Source cross-file contracts: **151 PowerShell files**, **960 functions**, **116 action contracts**, **236 headless commands**, and **47 menu actions**.
- Default-deny Legacy Unsafe and verified-tool gates, exact acknowledgement markers, typed dispatcher routes, conflict-aware recovery markers, and D01-D166 evidence identities.
- Deterministic release/convergence tool unit tests: 12 passed.

## Claim boundary

PowerShell AST parsing, PSScriptAnalyzer, Pester, module import, Windows APIs, UAC, Registry and security mutations, AppX, services/tasks, hibernation, display reset, WLAN/Event Log parsing, restart/recovery, installer, and interactive TUI behavior remain pending Windows-native execution against the exact archive bytes.

## Wave-seven additions

The source/evidence gates cover D106-D109, 87 new regular-file surfaces, bounded cleanup/AppX admission, three new typed contracts, seven headless routes, six catalog rules, three destructive profiles, exact acknowledgement, service coordination, and explicit excluded-operation guardrails. Windows-native execution remains pending and is not inferred from static validation.

## Current claim boundaries

Static source validation covers 151 PowerShell files, 960 functions, 116 action contracts, 236 headless commands, and 47 routed TUI actions. Cumulative evidence validation covers 166 submitted donor IDs, 153 unique archive baselines, 615 semantic records, 52,629 surfaces, 436 composition groups, 355 target nodes, and 172 test nodes. Twenty-eight Python release/convergence/V5/Wave 11 tests pass. Windows-native PowerShell parsing, Pester execution, registry/service mutation, WUA, MSI COM, BITS, and TUI interaction remain pending on a disposable Windows host.

## WinCare 5.2 measured gate

The current source contains 151 PowerShell files, 960 functions, 116 typed contracts, 236 headless commands, and 47 TUI routes. Evidence contains 166 donors, 153 unique baselines, 615 decisions, 436 composition groups, 355 target nodes, 172 test nodes, and 52,629 surfaces. The Python CI-equivalent suite contains 28 passing tests. The repository contains 217 Pester cases across 18 suites; Windows execution remains pending.
