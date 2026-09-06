---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
date: 2026-09-06
title: WinCare Modernization & Ergonomics Overhaul
status: Completed
---

# WinCare Modernization & Ergonomics Overhaul — Product Contract

## 1. Executive Summary

WinCare is a native Windows system maintenance and diagnostic desktop application built with .NET 8, WinUI 3, and Rust. While its technical foundations offer strong auditability and safety guarantees, the current user experience suffers from five major pain points:
1. **Excessive Binary Size:** Standalone single-file executables reach 120–135 MB (decompressing to ~370 MB) because trimming is disabled and full runtimes/projections are bundled.
2. **High Latency & Sluggish Probes:** Checkups run strictly serialized diagnostic probes one after another, stalling on Windows Update queries and creating 30–60+ second waits.
3. **Overly Defensive Safety Ceremony:** Every mutating command requires a two-phase preview and a cryptographic review token (`ApprovedMutationPlan`), forcing users into a repetitive 3-step ritual for even trivial, low-risk operations like clearing a cache.
4. **Usability Friction & Misleading Metrics:** The app displays an uncurated 259-command catalog, raw JSON parameter expanders, and a "Health Score" that merely calculates probe completion percentages rather than actual machine health.
5. **High-Friction Release Distribution:** MSIX releases require running elevated Python scripts to install development certificates, making the app inaccessible to non-technical users.

This document establishes the product requirements for a **comprehensive modernization overhaul** to transform WinCare into a fast, lightweight, user-friendly, yet reliably safe system utility.

---

## 2. Problem Statement & User Needs

| Area | Current State | User Pain Point | Desired Outcome |
|---|---|---|---|
| **Safety Model** | Mandatory 2-phase preview + SHA-256 parameter digest review token on all mutating commands. | Confirmation fatigue; routine operations require 3+ clicks, toggle switches, and preview waits. | **Risk-Tiered Admission:** 1-click execution for safe/routine actions; preview + explicit confirmation only for destructive operations. |
| **UX & Workflows** | Massive flat catalog of 259 commands; raw JSON parameters; completion-rate "Health Score". | Users don't know what to run; the health score gives false reassurance (100% even if malware or full disks exist). | **Curated Smart Cards:** 1-click "Quick Clean", "Optimize Startup", "Network Fix", with a diagnostic assessment based on actual findings. |
| **Performance** | `SequentialCommandProbeRunner` runs checks one by one; Windows Update COM queries block the UI. | System checkup takes 30–60+ seconds; app cold boot is slow due to PE decompression. | **Parallel Probes & Decoupled WUA:** System, Storage, and Security probes run concurrently in <3s; WUA runs in the background. |
| **Binary Size** | `<PublishTrimmed>false</PublishTrimmed>` + self-contained WASDK runtime = 135 MB standalone executable. | Downloads are slow, disk footprint exceeds 350 MB, and cold start is bogged down. | **Aggressive Trimming & Cleanup:** Strip unused AI/projection DLLs, enable IL trimming, reducing standalone executable to ~25–35 MB. |
| **Distribution** | Unsigned/dev-signed MSIX requiring Python `install_msix.py` and cert imports to `TrustedPeople`. | Average Windows users cannot install the packaged release. | **Standard Setup Installer:** Standard Inno Setup / WiX `.exe` installer alongside portable ZIPs. |

---

## 3. Key Decisions & Strategic Choices

1. **Risk-Tiered Command Admission:**
   - **Tier 1 (Safe / Non-Destructive):** Cache flushes, DNS reset, volume trim, temporary file purge, event log reading. Admitted and executed immediately on primary action click.
   - **Tier 2 (Moderate Impact):** Service restart/disabling, firewall rule changes, network adapter reset. Standard native confirmation dialog summarizing resources affected.
   - **Tier 3 (High Risk / Destructive):** Registry key deletion, disk formatting, component store cleanup (`DISM /ResetBase`), driver alterations. Requires preflight preview, impact summary, and explicit review approval.

2. **Curated Everyday Workflows on Home:**
   - The Home page introduces primary action cards:
     - **Quick Clean:** Recovers safe disk space from caches and temp locations.
     - **Optimize Startup:** Identifies and disables high-impact startup items.
     - **Network Refresh:** Flushes DNS, renews IP leases, and tests latency.
   - The diagnostic score reflects **detected system status** (e.g. "Healthy", "Attention Needed", "Action Recommended") with concrete itemized findings rather than a percentage of completed probes.

3. **Parallel Probe Architecture:**
   - Decouple measurement-sensitive probes: execute System, Storage, and Security diagnostics concurrently using `Task.WhenAll`.
   - Asynchronous Windows Update Agent: Checkup surfaces immediate results for device and storage health while Windows Update search streams in asynchronously.

4. **Trimming & Distribution Packaging:**
   - Enable `<PublishTrimmed>true</PublishTrimmed>` in .NET publish profiles, annotating dynamic reflection entry points.
   - Remove unused WindowsAppSDK dependencies (AI Text/Vision/Audio, MachineLearning, OnnxRuntime, Widgets).
   - Introduce an Inno Setup script to produce a single-file, zero-dependency `WinCare-Setup.exe` installer that automatically handles runtime dependencies.

---

## 4. Product Scope & Functional Requirements

### 4.1 Safety & Command Admission (`WinCare.Application.Commands`)
- **REQ-SAF-01:** The `CommandDefinition` model shall include a `RiskTier` classification (`Safe`, `Moderate`, `Destructive`).
- **REQ-SAF-02:** When executing a `Safe` mutating command with `Apply = true`, `CommandDispatcher` shall admit and execute the request directly without requiring an `ApprovedMutationPlan`.
- **REQ-SAF-03:** When executing a `Destructive` command with `Apply = true`, `CommandDispatcher` shall enforce preflight preview verification and review plan consumption.
- **REQ-SAF-04:** The Activity Journal shall log all executed commands regardless of risk tier, preserving auditability.

### 4.2 Home & Checkup UX (`WinCare.App.Views.Pages`)
- **REQ-UX-01:** The Home page shall feature top-level smart action cards for routine maintenance tasks.
- **REQ-UX-02:** The Checkup view shall replace the "evidence-collection completion score" with an actionable health assessment based on real inspection thresholds:
  - *Storage:* Warning if free space is below 15% or 10 GB.
  - *Security:* Warning if Windows Defender or Firewall is disabled.
  - *Updates:* Status indicating whether reboot is pending or updates are available.
- **REQ-UX-03:** Each warning in Checkup shall provide a direct 1-click remediation button pointing to the corresponding tool.
- **REQ-UX-04:** Raw JSON parameter editors shall be hidden by default in favor of clean, native UI inputs.

### 4.3 Probe Execution & Performance (`WinCare.Application.Commands`)
- **REQ-PERF-01:** Read-only diagnostic probes for system, storage, and security shall run concurrently using `Task.WhenAll`.
- **REQ-PERF-02:** The primary Quick Check shall complete and render results in under 3.0 seconds on standard modern hardware.
- **REQ-PERF-03:** Windows Update readiness scans shall be decoupled from the primary Quick Check payload and report status asynchronously.

### 4.4 Build, Footprint & Packaging
- **REQ-PKG-01:** Standalone executable publish profiles shall enable `<PublishTrimmed>true</PublishTrimmed>` with trimmed BCL and WinUI dependencies.
- **REQ-PKG-02:** Total standalone `.exe` release size shall not exceed 35 MB (compressed).
- **REQ-PKG-03:** An Inno Setup / WiX installer (`.exe`) shall be generated in CI to allow seamless installation without requiring manual certificate imports or Python scripts.

---

## 5. Non-Functional Requirements & Boundaries

- **Elevation Boundaries:** Commands requiring Administrator access shall still prompt or fail gracefully when un-elevated.
- **Backwards Compatibility:** Advanced users and technicians must still be able to access the full 259-command catalog via the "All Tools" tab.
- **Activity & Auditing:** No action shall bypass the Activity Journal. Every mutation and probe must leave a clear receipt.

---

## 6. Verification & Acceptance Criteria

1. **Size Verification:** Standalone release build artifact is ≤ 35 MB.
2. **Speed Verification:** Quick check on Home/Checkup completes in ≤ 3 seconds.
3. **Safety Ergonomics Verification:** Running "Flush DNS" or "Clear Temp Files" completes in exactly 1 user click without a preview toggle.
4. **Installation Verification:** Windows users can install WinCare via `WinCare-Setup.exe` with standard UAC approval, zero Python dependencies, and zero certificate trust errors.
