# Product / UX finalization

This pass converts the product-level audit into implementation changes while preserving WinCare's existing dispatcher, command contracts, and platform integrations.

## Product architecture

- Primary navigation is organized around user jobs: Home, Checkup, System care, Security, Repair & recovery, Power tools, and Activity.
- Extensions, Troubleshoot, Settings, and Help are secondary routes. About remains reachable from Help and global search without occupying permanent navigation.
- Home is recommendation-led rather than telemetry-led. It offers Checkup, common care entry points, recent activity, system-area status, and explicit paths to advanced capabilities.
- “AI Doctor” is presented as **Troubleshoot**, accurately reflecting the shipped local rule-based inference engine.
- “All Tools” is presented as **Power tools**, an advanced surface rather than the app's de facto information architecture.

## Capability discoverability

- Ctrl+K searches routes, all 269 native tools, discovered extensions, and help topics.
- Power tools has exact Area and Section filters.
- Categories is a real Area/Section browser rather than a different sort of the same table.
- Favorites, Recent, and Care plans are distinct modes.
- Normal tool rows show user-relevant task, category, impact, administrator requirement, and restart expectation. Command IDs and migration metadata are confined to Advanced details.
- Extensions can be reached from the shell, Home, Help, and global search.

## Correct care taxonomy

System care, Security, and Repair & recovery no longer use fuzzy text searches to define product tabs. Each tab projects the command catalog through exact `Area` + `Section` values. This prevents commands from unrelated areas from leaking into a tab simply because their title or summary shares a word.

System care combines the catalog's `Routines` and `Maintenance` sections intentionally under **Routines & maintenance**. Repair & recovery removes the misleading empty **Change records** tab and keeps **Portable playbooks** as its own workflow.

## Checkup boundary

Checkup is now a read-only diagnostic surface end to end. Storage, update, and security findings hand the user to the relevant care section; Checkup no longer performs cleanup behind a preview internally. Its UI reports the status of checked areas rather than a synthetic machine-health score.

## Safety presentation

The dispatcher and risk/admission protections are unchanged. The interface no longer repeats the safety architecture as the product's primary message. Normal pages explain only the information needed for the current action; detailed review, approval, evidence, JSON parameters, and technical identifiers remain available where they are relevant.

## Visual hierarchy

- Home and Help use flatter, task-oriented sections with fewer nested enclosures.
- Checkup uses a simple read-only hero, tabs before content, and direct findings.
- Power tools removes migration/status decoration from everyday rows and reserves technical detail for the inspector.
- Typography and theme resources remain native WinUI/Fluent; semantic state colors keep their existing meaning.

## Validation performed in this artifact

- The native repository suite passes **98 / 98 tests**, and `python3 tools/verify_native_foundation.py` passes after the product contract was updated for the new labels/tabs/columns.
- Visual token verification passes, and all 8 status-pill foreground/background pairs meet WCAG 2.1 AA contrast.
- All XML/XAML/RESW/project/manifest files are parsed during final static validation.
- The command catalog remains 269 unique native commands and retains the frozen 259-command legacy baseline required by migration verification.
- Care-area mappings are validated directly against `commands.json`.
- Modified C# files receive delimiter/symbol sanity checks.

A .NET SDK is not installed in the artifact environment, so the final ZIP cannot claim a local Windows/WinUI compile or runtime visual pass. `docs/Windows-Validation.md` remains the authoritative Windows runtime validation procedure.
