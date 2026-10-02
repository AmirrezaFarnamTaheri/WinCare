# WinCare 4.0: Phase 2 — UX Acceleration & Active Mutations

**Date:** 2026-10-02

## 1. Radical Reduction of Defensive UX
Historically, WinCare used a rigid 2-step process (Preview -> Approve -> Apply) for almost all mutating commands to enforce extreme safety boundaries. To modernize the interface and reduce friction for operators:
- Removed the strict `RequiresApprovalSwitch` blocking.
- Mutating tools (Safe/Moderate) now execute directly when requested without demanding redundant secondary confirmation dialogs.
- `AdministratorAccess.Required` rigid application blocks have been lifted, relying on the native Windows UAC handling and `TokenPrivilegeScope` rather than imposing arbitrary UI dead-ends.

## 2. Promoting Passive Scans to Active Solutions
Several read-only telemetry and discovery pipelines have been promoted to first-class active problem solvers:
- Added `installer-cache-purge` (Moderate) replacing purely passive cache analysis.
- Added `app-residual-purge` (Moderate) replacing discovery-only mode.
- Added `startup-optimize` (Safe) allowing single-click startup delay eradication rather than just reading boot times.

## 3. Top-Level Active Troubleshooter (Omni-Resolve)
- Promoted the `AiDoctor` feature out of the obscure footer menu and pinned it front-and-center under the `Care` core navigation block.
- Renamed the feature to **Active Troubleshooter**.
- The prompt has been made immediately responsive, promising direct and proactive resolution paths instead of defensive observations.
