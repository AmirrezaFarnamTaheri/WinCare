# Final validation — product / UX finalization

This release candidate contains the task-first WinCare product restructuring plus a second deeper pass across visual hierarchy, frontend behavior, backend/frontend parity, design-system debt, navigation architecture, documentation truth, and regression coverage. See `docs/PRODUCT-UX-FINALIZATION.md` for the implementation summary.

## Automated validation contract

The authoritative merge evidence is the GitHub Actions **Native WinUI** workflow for the exact PR head. It gates the source with repository tests and then builds the Windows/native deliverables for the supported architectures.

The final source is expected to pass these repository-level gates on every head update:

- native/Python repository regression suite;
- `tools/verify_native_foundation.py`;
- visual-token verification;
- status-pill WCAG 2.1 AA contrast verification;
- XML/XAML/RESW/project/manifest and JSON parsing checks;
- command-catalog uniqueness and migration/finalization checks;
- XAML/code-behind event-wiring checks;
- x64 and ARM64 native/managed build and packaging jobs.

Hard-coded historical test counts are intentionally not used here because the second pass adds regression tests as the product contract evolves. The workflow result for the exact commit is the source of truth.

## Second-pass regression coverage

The deeper pass adds explicit checks for:

- Home remaining presentation-only and exposing one primary Checkup CTA;
- Home evidence rows matching the four actual Checkup evidence sources rather than duplicating system evidence as “Performance”;
- Troubleshoot handing suggested commands to the canonical Power tools execution/review surface;
- Power tools using named controls instead of visual-tree/order probing;
- Power tools exposing Safe / Moderate / Destructive product tiers instead of raw backend risk values;
- extension catalog trust/availability being visible in the frontend;
- Checkup preserving follow-up actions in both Quick check and Results projections;
- named care-section navigation and shell/PageService/navigation-catalog route parity;
- removal of abandoned instrument-panel styles/resources;
- documentation identifying stale runtime screenshots as historical rather than current UI evidence.

## Product/data invariants retained

- Command catalog: **269 commands / 269 unique IDs**.
- Care pages continue to derive from exact command-catalog Area/Section taxonomy.
- Checkup remains read-only and routes findings to care surfaces.
- Raw catalog risk and command IDs remain available as advanced technical detail without defining the normal product taxonomy.
- The dispatcher remains authoritative for admission, preview/approval semantics, execution, results, and Activity evidence.

## Live Windows visual-validation limitation

The interactive agent environment used for this review does not provide a Windows desktop session, so it cannot truthfully certify the final rendered UI, Narrator output, keyboard focus order, High Contrast appearance, text/display scaling, or new runtime screenshots. GitHub Actions can validate Windows compilation/build/package behavior, but a fresh installed-candidate visual/accessibility pass is still required. Follow `docs/Windows-Validation.md` and recapture the runtime screenshots when the next release candidate is installed.
