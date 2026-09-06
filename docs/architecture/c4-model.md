# WinCare C4 Architecture Documentation

**System:** WinCare Windows Maintenance & Optimization Workspace  
**Specification Version:** 2.0 (Modernized & Streamlined)  
**Standard:** C4 Model (Context, Container, Component, Code)

---

## 1. Level 1: System Context Diagram

The System Context shows how the WinCare desktop workspace interacts with users and external Windows OS subsystems.

```mermaid
C4Context
    title System Context Diagram for WinCare

    Person(user, "Windows User", "Casual user or power user seeking system health, speed, and disk space.")
    Person(admin, "System Administrator", "Automating fleet maintenance, security audits, or recovery.")

    System(wincare, "WinCare Workspace", "High-performance native Windows maintenance, diagnostic, and optimization application.")

    System_Ext(win_api, "Windows Core APIs", "Win32, Shell32, Registry, and Native NT APIs.")
    System_Ext(wua_service, "Windows Update Agent (WUA)", "Local COM service querying Microsoft Update catalogs.")
    System_Ext(fs_reg, "File System & Registry", "NTFS / ReFS volumes, user caches, system registry hives.")
    System_Ext(etw_event, "Event Log & ETW", "Windows Event Log and Event Tracing for Windows.")

    Rel(user, wincare, "Runs 1-click cleanups, scans diagnostics, applies optimizations", "WinUI 3 GUI")
    Rel(admin, wincare, "Executes policy profiles, exports reports, audits security", "WinUI 3 / CLI")

    Rel(wincare, win_api, "Gathers hardware, memory, process, and disk state", "P/Invoke & Rust C-ABI")
    Rel(wincare, wua_service, "Queries pending security and quality updates", "COM Interop (Async)")
    Rel(wincare, fs_reg, "Clears caches, inspects disk pressure, manages startup", "Direct OS I/O")
    Rel(wincare, etw_event, "Audits system reliability and security events", "Win32 ETW API")
```

### Context Boundary & Trust Principles
- **No Cloud Dependency:** WinCare is 100% local-first and operational without an Internet connection (except when checking Windows Updates).
- **Least Privilege vs Elevation:** Runs without elevation for standard queries; prompts for UAC elevation only when executing administrator-level mutations.
- **Fail-Closed Admission:** Commands targeting sensitive Windows settings (registry, component store, drivers) cannot execute without cryptographic plan verification.

---

## 2. Level 2: Container Diagram

The Container Diagram illustrates the high-level technology choices, deployable containers, and communication boundaries within WinCare.

```mermaid
C4Container
    title Container Diagram for WinCare

    Person(user, "Windows User", "Interacts with modern native UI.")

    Container_Boundary(c1, "WinCare Standalone Desktop Application") {
        Container(app, "WinCare.App (Presentation)", "WinUI 3 / XAML, .NET 8 (Trimmed)", "Provides Tactile Telemetry UI, curated action cards, and diagnostics view.")
        Container(application, "WinCare.Application (Service Layer)", "C# / .NET 8", "Implements Command Dispatcher, Risk-Tiered Admission, and Parallel Diagnostic Runner.")
        Container(domain, "WinCare.Domain (Domain Core)", "C# / .NET 8", "Defines typed command contracts, RiskTier enum, and Activity records.")
        Container(catalog, "WinCare.CommandCatalog", "C# / .NET 8 (Embedded JSON)", "Provides frozen 259-command definitions and typed parameter schemas.")
        Container(infrastructure, "WinCare.Infrastructure (OS Adapters)", "C# / .NET 8, P/Invoke", "Executes Windows maintenance actions, manages journal persistence, and bridges Rust ABI.")
        Container(rust_core, "wincare_core (Native Core)", "Rust 2024 (C-ABI dll)", "Low-latency memory scanning, cryptographic digests, and high-performance system probing.")
    }

    ContainerDb(journal_store, "Activity Journal", "Local JSONL", "Immutable audit log of all checks, previews, and executed mutations.")
    System_Ext(os, "Windows Subsystems", "Win32, COM, NTFS, Registry")

    Rel(user, app, "Interacts with", "XAML / compiled x:Bind")
    Rel(app, application, "Dispatches commands and queries", "In-Process DI")
    Rel(application, domain, "Uses domain types and policies", "Assembly Reference")
    Rel(application, catalog, "Loads command schemas", "Assembly Reference")
    Rel(application, infrastructure, "Delegates OS operations", "ICommandOperationExecutor")
    Rel(infrastructure, rust_core, "Invokes low-level native routines", "P/Invoke (C-ABI v1)")
    Rel(infrastructure, journal_store, "Appends execution records", "Direct File I/O")
    Rel(infrastructure, os, "Executes system operations", "Win32 / COM / PInvoke")
```

---

## 3. Level 3: Component Diagram (`WinCare.Application`)

The Component Diagram details the core architectural pipeline inside `WinCare.Application`.

```mermaid
C4Component
    title Component Diagram for WinCare.Application

    Container(app_vm, "ViewModels (Checkup, Home, Tools)", "WinUI 3 MVVM", "Drives UI state and user intent.")

    Container_Boundary(app_core, "WinCare.Application") {
        Component(dispatcher, "CommandDispatcher", "C# Class", "Middleware pipeline: enforces timeouts, admission policies, and risk gating.")
        Component(parallel_runner, "ParallelCommandProbeRunner", "C# Class", "Executes independent diagnostic probes concurrently via Task.WhenAll.")
        Component(admission_policy, "RiskTierAdmissionPolicy", "C# Policy Logic", "Admits Safe commands directly; gates Destructive commands behind ApprovedMutationPlan.")
        Component(journal_service, "ActivityJournalService", "C# Service", "Logs begin, complete, cancel, and failure lifecycle events.")
        Component(catalog_service, "CommandCatalogService", "C# Service", "Indexes and queries the 259 cataloged commands.")
    }

    Container(infra_exec, "WindowsCommandExecutor", "WinCare.Infrastructure", "Executes native Windows actions.")
    ContainerDb(activity_file, "activity.jsonl", "Disk File", "Audit log.")

    Rel(app_vm, dispatcher, "Executes commands", "DispatchAsync")
    Rel(app_vm, parallel_runner, "Runs multi-probe diagnostics", "RunParallelAsync")
    Rel(parallel_runner, dispatcher, "Dispatches individual probes", "DispatchAsync(preview)")
    Rel(dispatcher, admission_policy, "Evaluates risk tier", "ValidateAdmission")
    Rel(dispatcher, journal_service, "Records audit trail", "Begin / Complete / Fail")
    Rel(dispatcher, infra_exec, "Executes admitted command", "ExecuteAsync")
    Rel(journal_service, activity_file, "Appends journal entry", "File Stream")
```

---

## 4. Execution & Admission Sequence (Janusian Dialectic)

### 4.1 Tier 1: Safe Mutation (1-Click, Zero Friction)
```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Home / Tools View
    participant Disp as CommandDispatcher
    participant Exec as WindowsCommandExecutor
    participant Jrnl as ActivityJournalService

    User->>UI: Clicks "Quick Clean" (Apply = true)
    UI->>Disp: DispatchAsync(CommandRequest, Apply=true)
    Disp->>Disp: Check RiskTier == Safe
    Note over Disp: Safe: Bypass ApprovedMutationPlan requirement!
    Disp->>Jrnl: Begin("cleaner-disk-pressure")
    Disp->>Exec: ExecuteAsync(request)
    Exec-->>Disp: Outcome(Succeeded, RecoveredBytes=1.4GB)
    Disp->>Jrnl: Complete(activityId)
    Disp-->>UI: CommandResult(Succeeded)
    UI-->>User: Instant toast / inline success badge
```

### 4.2 Tier 3: Destructive Mutation (Full Defense)
```mermaid
sequenceDiagram
    autonumber
    actor User
    participant UI as Tools View
    participant Disp as CommandDispatcher
    participant Exec as WindowsCommandExecutor
    participant Jrnl as ActivityJournalService

    User->>UI: Requests "Safe deep clean"
    UI->>Disp: DispatchAsync(request, Apply=false) [PREVIEW PASS]
    Disp->>Exec: ExecuteAsync(request) [Read-only simulation]
    Exec-->>Disp: Outcome(PreviewData)
    Disp->>Disp: IssueReviewPlan(SHA-256 Digest Token)
    Disp-->>UI: CommandResult(Preview, ApprovedMutationPlan)
    UI-->>User: Displays Warning Dialog + Affected Resources
    User->>UI: Confirms & Approves
    UI->>Disp: DispatchAsync(request, Apply=true, Approval=PlanToken, ReviewApproved=true)
    Disp->>Disp: Validate & Consume PlanToken (Single-use, unexpired)
    Disp->>Jrnl: Begin("deep-clean")
    Disp->>Exec: ExecuteAsync(request) [MUTATION PASS]
    Exec-->>Disp: Outcome(Succeeded)
    Disp->>Jrnl: Complete(activityId)
    Disp-->>UI: CommandResult(Succeeded)
```
