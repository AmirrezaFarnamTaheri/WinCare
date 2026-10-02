# WinCare

Native Windows 10/11 maintenance and diagnostics application using WinUI 3, .NET 8 and a Rust core. People and technicians inspect Windows, maintain it, review changes and recover from problems.

The 296-command catalog and existing dispatcher/integrations define functionality; product surfaces organize those capabilities without inventing new ones. The release candidate's promotion gates remain in docs/Validation.md.

Evidence collection is distinct from a health verdict. Commands retain risk/admission rules. Activity reports actual outcomes. Undo requires an executable compensator. Remote installation stays browse-only without an approved catalog trust root.

The current product model is task-first: Home and Checkup guide common work, Care pages use exact catalog taxonomy, Power tools exposes the complete catalog, and Extensions/Troubleshoot remain secondary capabilities. DESIGN.md records the visual system and UX-CONTRACT.md its behavior. Runtime contracts outweigh generated concept copy.

## Competitive Alternatives & Differentiating Invariants

WinCare is built for operators, technicians, and power users who need reliable Windows maintenance without black-box risk:

| Dimension | Black-Box "Cleaners" & Registry Sweepers | WinCare Invariant |
|---|---|---|
| **Execution Model** | Blind background mutation with fabricated "issues found" metrics | Two-phase preview-first workflow: dry-run inspection produces structured JSON evidence before any mutation |
| **Admission & Gating** | Silent registry deletions and unverified sweeps | Strict fail-closed risk tiering (Safe, Moderate, Destructive) requiring explicit approval for system alterations |
| **Integrity & Bounds** | Recursive traversal that can cross NTFS reparse points / junctions | Native Rust engine (`wincare_core`) with NT object-attribute `OBJ_DONT_REPARSE` traversal guards and bounded depth |
| **Auditability** | Ephemeral or non-existent logs | Local, persistent activity journal recording start time, duration, parameters, exit status, and structured diffs |
| **Reversibility** | Empty promises of "undo" without captured baseline | Explicit compensator contracts: undo is offered only when a verified rollback handler and baseline exist |

Product evidence: README.md, docs/Architecture.md, SECURITY.md, docs/Validation.md, command catalog and application services. docs/Screenshots.md distinguishes historical captures from concepts.
