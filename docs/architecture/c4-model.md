# WinCare C4 Architecture Documentation

**System:** WinCare Windows Maintenance & Recovery Workspace  
**Specification Version:** 2.1 (Task-first product architecture)  
**Standard:** C4 Model (Context, Container, Component)

---

## 1. Level 1: System Context

WinCare is a local-first Windows desktop workspace for evidence collection, maintenance, security, repair, recovery, and advanced operational tooling.

```mermaid
C4Context
    title System Context Diagram for WinCare

    Person(user, "Windows User", "Checks current system evidence and performs supported maintenance or recovery work.")
    Person(admin, "System Administrator", "Reviews advanced tools, security state, reports, and recovery workflows.")

    System(wincare, "WinCare Workspace", "Native Windows maintenance, diagnostic, security, and recovery application.")

    System_Ext(win_api, "Windows Core APIs", "Win32, Shell, Registry, Windows Security, and native system APIs.")
    System_Ext(wua_service, "Windows Update Agent", "Local COM service used for update search and supported update operations.")
    System_Ext(fs_reg, "File System & Registry", "Volumes, user/system files, and registry hives.")
    System_Ext(eventing, "Windows Eventing", "Event Log and related local diagnostic evidence.")

    Rel(user, wincare, "Runs Checkup, care workflows, Power tools, Extensions, Troubleshoot, and Activity", "WinUI 3")
    Rel(admin, wincare, "Reviews advanced operations, recovery, security, and reports", "WinUI 3")

    Rel(wincare, win_api, "Reads evidence and executes admitted Windows operations", "P/Invoke / COM / Rust C ABI")
    Rel(wincare, wua_service, "Queries or performs admitted Windows Update operations", "COM interop")
    Rel(wincare, fs_reg, "Inspects and changes admitted resources", "Bounded OS I/O")
    Rel(wincare, eventing, "Reads local diagnostic evidence", "Windows APIs")
```

### Context boundary and trust principles

- **Local-first:** core product workflows do not require a cloud service. Network-dependent features fail visibly when unavailable.
- **Least privilege:** read-only and standard-user work does not require unnecessary elevation; commands declare administrator requirements explicitly.
- **Risk-tiered admission:** Safe work can execute directly; Moderate work requires reviewed confirmation; Destructive work requires preview plus explicit approval of a parameter-bound plan.
- **Single execution authority:** Home, care pages, Checkup, and Troubleshoot do not invent their own mutation semantics. Advanced execution converges on the dispatcher and Power tools review surface.

---

## 2. Level 2: Containers

```mermaid
C4Container
    title Container Diagram for WinCare

    Person(user, "Windows User", "Interacts with the native task-first UI.")

    Container_Boundary(c1, "WinCare Desktop Application") {
        Container(app, "WinCare.App", "WinUI 3 / XAML, .NET 8", "Task-first shell, Home, Checkup, care pages, Power tools, Activity, Extensions, Troubleshoot, Settings, Help.")
        Container(application, "WinCare.Application", "C# / .NET 8", "Command dispatcher, admission, catalog projection, diagnostics, extensions, activity journal.")
        Container(domain, "WinCare.Domain", "C# / .NET 8", "Typed requests/results, RiskTier policy, evidence and activity models.")
        Container(catalog, "WinCare.CommandCatalog", "C# / embedded JSON", "269 command definitions, 259 frozen legacy IDs, presets, remediation metadata, typed parameter schemas.")
        Container(infrastructure, "WinCare.Infrastructure", "C# / .NET 8, P/Invoke", "Windows adapters, bounded processes, state persistence, extension verification, native bridge.")
        Container(rust_core, "wincare_core", "Rust 2024 / C ABI", "Bounded native primitives and system probes.")
        Container(guard, "wincare_guard", "Rust 2024", "Experimental local health/IPC boundary.")
    }

    ContainerDb(journal_store, "Activity Journal", "Local durable store", "Operation lifecycle and outcome evidence.")
    System_Ext(os, "Windows Subsystems", "Win32, COM, NTFS, Registry, Windows Security")

    Rel(user, app, "Interacts with", "Native UI")
    Rel(app, application, "Requests evidence, catalog views, or admitted operations", "In-process services")
    Rel(application, domain, "Uses domain contracts and policy", "Assembly reference")
    Rel(application, catalog, "Indexes commands and schemas", "Assembly reference")
    Rel(application, infrastructure, "Delegates admitted Windows operations", "ICommandOperationExecutor")
    Rel(infrastructure, rust_core, "Invokes bounded native routines", "C ABI")
    Rel(infrastructure, os, "Reads or changes admitted Windows resources", "Win32 / COM / PInvoke")
    Rel(application, journal_store, "Records operation lifecycle/outcomes", "Local persistence")
```

---

## 3. Level 3: Application components

```mermaid
C4Component
    title WinCare.Application and presentation ownership

    Container_Boundary(presentation, "WinCare.App") {
        Component(home, "Home", "Presentation projection", "Summarizes shared evidence/activity and routes to dedicated workflows.")
        Component(checkup, "Checkup", "Read-only workflow", "Runs bounded read-only evidence probes and routes findings to care sections.")
        Component(care, "Care pages", "Catalog projections", "Exact Area/Section views of supported tools.")
        Component(tools, "Power tools", "Canonical command inspector", "Typed parameters, safety-tier review, execution results, advanced details.")
        Component(troubleshoot, "Troubleshoot", "Rule-based diagnostic UI", "Turns symptom text into evidence-backed suggested commands, then hands them to Power tools.")
    }

    Container_Boundary(app_core, "WinCare.Application") {
        Component(dispatcher, "CommandDispatcher", "C#", "Validates requests, enforces admission/risk semantics, owns command lifecycle.")
        Component(parallel_runner, "ParallelCommandProbeRunner", "C#", "Runs independent read-only probes with bounded concurrency.")
        Component(catalog_service, "ToolCatalogService", "C#", "Indexes/searches all 269 command definitions and exact Area/Section metadata.")
        Component(intent, "IntentTranslator", "C#", "Rule-based symptom interpretation and evidence-backed action planning.")
        Component(journal, "ActivityJournalService", "C#", "Records running, terminal, and needs-attention outcomes.")
    }

    Container(infra_exec, "WindowsCommandExecutor", "WinCare.Infrastructure", "Executes admitted Windows operations.")

    Rel(home, journal, "Reads shared evidence/activity")
    Rel(checkup, parallel_runner, "Runs system/storage/security previews")
    Rel(checkup, dispatcher, "Runs Windows Update readiness preview")
    Rel(care, catalog_service, "Projects exact care taxonomy")
    Rel(tools, catalog_service, "Searches/filters command catalog")
    Rel(tools, dispatcher, "Previews/applies admitted commands")
    Rel(troubleshoot, intent, "Requests local diagnosis")
    Rel(troubleshoot, tools, "Opens suggested command with parameters")
    Rel(parallel_runner, dispatcher, "Dispatches read-only probes")
    Rel(dispatcher, infra_exec, "Executes admitted command")
    Rel(dispatcher, journal, "Records lifecycle/outcome")
```

---

## 4. Product navigation and handoffs

```mermaid
flowchart LR
    Home --> Checkup
    Home --> Care[System care / Security / Repair & recovery]
    Home --> Tools[Power tools]
    Home --> Activity
    Home --> Extensions
    Home --> Troubleshoot

    Checkup -->|named finding handoff| Care
    Care -->|open exact command + parameters| Tools
    Troubleshoot -->|suggested command + parameters| Tools
    Tools -->|outcome| Activity
```

Navigation uses stable route IDs. New cross-page care navigation uses named section requests rather than positional tab indices. Integer section parameters remain only for compatibility with older deep links/callers.

Hidden routes such as About can be opened from Help/search without leaving an unrelated visible navigation item selected.

---

## 5. Command admission sequences

### 5.1 Safe command

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Power tools / admitted caller
    participant Disp as CommandDispatcher
    participant Exec as WindowsCommandExecutor
    participant Jrnl as ActivityJournalService

    User->>UI: Run Safe command
    UI->>Disp: Execute validated request
    Disp->>Disp: Confirm RiskTier == Safe
    Disp->>Jrnl: Begin operation
    Disp->>Exec: Execute admitted command
    Exec-->>Disp: Outcome
    Disp->>Jrnl: Complete / Fail / Needs attention
    Disp-->>UI: CommandResult
```

Safe includes read-only commands and bounded low-risk actions. Read-only is a separate property, not a fourth product safety tier.

### 5.2 Moderate command

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Power tools
    participant Disp as CommandDispatcher
    participant Exec as WindowsCommandExecutor
    participant Jrnl as ActivityJournalService

    User->>UI: Review Moderate command
    UI->>Disp: Resolve reviewed operation
    UI-->>User: Present target/impact and request confirmation
    User->>UI: Confirm
    UI->>Disp: Execute confirmed request
    Disp->>Exec: Execute admitted command
    Disp->>Jrnl: Record lifecycle/outcome
    Disp-->>UI: CommandResult
```

### 5.3 Destructive command

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Power tools
    participant Disp as CommandDispatcher
    participant Exec as WindowsCommandExecutor
    participant Jrnl as ActivityJournalService

    User->>UI: Review Destructive command
    UI->>Disp: Execute preview request
    Disp->>Exec: Resolve read-only affected-resource preview
    Exec-->>Disp: Preview evidence
    Disp->>Disp: Issue single-use parameter-bound review plan
    Disp-->>UI: Preview + review plan
    UI-->>User: Show affected resources / explicit approval
    User->>UI: Approve reviewed plan
    UI->>Disp: Apply with matching plan + approval
    Disp->>Disp: Validate and consume plan
    Disp->>Jrnl: Begin operation
    Disp->>Exec: Execute mutation
    Exec-->>Disp: Outcome
    Disp->>Jrnl: Complete / Fail / Needs attention
    Disp-->>UI: CommandResult
```

---

## 6. Checkup sequence

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant Checkup
    participant Runner as ParallelCommandProbeRunner
    participant Disp as CommandDispatcher
    participant WUA as Windows Update search

    User->>Checkup: Run read-only Checkup
    par bounded fast probes
        Checkup->>Runner: system preview
        Checkup->>Runner: storage preview
        Checkup->>Runner: security preview
    and independent update readiness
        Checkup->>Disp: wua-search preview
        Disp->>WUA: Query readiness
    end
    Runner-->>Checkup: Fast evidence results
    Checkup-->>User: First evidence summary
    WUA-->>Disp: Update readiness
    Disp-->>Checkup: Background update result
    Checkup-->>User: Update evidence + optional named care handoff
```

Checkup itself never applies maintenance.

---

## 7. Troubleshoot sequence

```mermaid
sequenceDiagram
    autonumber
    actor User
    participant T as Troubleshoot
    participant I as IntentTranslator
    participant Tools as Power tools
    participant Disp as CommandDispatcher

    User->>T: Describe Windows symptom
    T->>I: Translate symptom
    I->>Disp: Run supported read-only evidence commands
    Disp-->>I: Evidence
    I-->>T: Findings + proposed command
    T-->>User: Explain evidence and next step
    User->>T: Open suggested step
    T->>Tools: Open exact command + parameters
    Tools->>Disp: Use normal Safe / Moderate / Destructive flow
```

Troubleshoot cannot mint an approval receipt and does not own a private apply path.

---

## 8. Extension trust boundary

Remote extension installation is fail-closed unless a configured WinCare-pinned catalog key verifies the exact freshly fetched catalog used for installation. Package ID/hash, publisher identity/signature, revocation state, declared capabilities, and external admission metadata are revalidated at the security boundary.

The current repository ships without an approved production catalog public key, so remote installation remains browse-only/disabled. The Extensions UI surfaces this trust/availability state explicitly.

Extensions execute full-trust in process with the current user's privileges; declared capabilities are consent metadata rather than a sandbox.

---

## 9. Activity, persistence, and recovery

Activity is the shared operation ledger:

- **Running** — currently executing work.
- **Needs attention** — an operation ended in a state requiring review or follow-up.
- **Completed** — terminal completed/failed/cancelled outcomes.
- **Reports** — aggregated daily summaries.

`UndoAvailable` is exposed only when a concrete executable compensator and sufficient captured state exist. WinCare does not advertise generic undo for irreversible operations.

---

## 10. Adaptivity, accessibility, and validation

The app-level compact breakpoint is `LayoutVisibility.CompactBreakpointDip = 920`; components may use local fit-specific breakpoints without redefining the global compact state. Power tools and Extensions have explicit responsive layouts rather than visual-tree/order heuristics.

High Contrast uses system colors, important controls have UI Automation names/IDs, and core task layouts wrap/stack rather than assuming desktop width.

Successful source tests and Windows compilation do **not** prove Narrator order, keyboard focus order, High Contrast rendering, text/display scaling, dialog focus, or final visual appeal. Those require the installed-candidate Windows validation procedure and fresh screenshots for the exact build.
