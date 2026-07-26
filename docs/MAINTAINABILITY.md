# Maintainability Architecture and Ratchets

WinCare's runtime source is divided into three closed ownership groups:

- **Core** — models, configuration, policy, safety, transaction orchestration, action contracts, and compatibility bridge primitives.
- **Providers** — bounded operating-system and dependency adapters. Mutators return typed evidence and participate in the transaction engine.
- **UI** — console, headless, GUI, navigation, and screen composition. UI code may request plans but does not implement privileged mutation.

Each group has a checked-in source manifest under `src/WinCare/SourceManifests`. Module import fails closed when a source file is missing, duplicated, reordered, assigned to multiple groups, or added without manifest ownership. Executable host scripts under `src/WinCare/Host` remain outside the module import scope.

The former 2,297-line `Core/00-MasterBridge.ps1` has been decomposed into twelve ordered subsystems covering primitives, observability, telemetry, networking, page-file management, instrumentation, servicing, policy/monitoring, game state, toolkit compatibility, mutation handlers, and host compatibility. No module source file may exceed 600 lines.

`tools/validate_maintainability.py` enforces a checked-in complexity and formatting ratchet:

- existing files may reduce but may not increase maximum line length, lines over 200 or 500 characters, or maximum function length;
- the global density totals may not regress;
- new files must contain at most 600 lines, 200 characters per line, and 200 lines per function, with no lines above the formatting limit;
- source ownership is derived from the closed group manifests.

The baseline intentionally records remaining legacy density so it can be reduced incrementally without pretending it has already disappeared. Updating the baseline to permit a regression is a reviewable architecture decision, not an automatic formatter side effect.
