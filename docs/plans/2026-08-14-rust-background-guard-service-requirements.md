# WinCare Rust Background Health Guard Service — Product & Technical Requirements Specification

- **Date:** 2026-08-14
- **Status:** Implementation-Ready Specification (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Implementation Plan:** `docs/plans/2026-08-14-rust-background-guard-service-plan.md`
- **Source:** `/ce-ideate` -> `/ce-brainstorm` -> `/ce-plan`

---

## 1. Executive Summary & Intent

WinCare will implement a lightweight **Windows Background Service in Rust (`wincare-guard.exe`)** to monitor system health, thermal states, disk capacity, and background resource hogs 24/7 without consuming noticeable system memory or CPU.

### Key Objectives
1. **Ultra-Low Resource Footprint:** Memory consumption under 4.0 MB RAM idle, CPU usage <0.1%.
2. **Proactive Health Telemetry:** Periodically sample system metrics (every 30s) and raise Windows Native Toast Notifications when maintenance is recommended.
3. **Seamless WinCare Integration:** IPC channel (Named Pipes) connecting `wincare-guard.exe` to `WinCare.App` for 1-click issue resolution.

---

## 2. Technical Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Windows System Background                       │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                wincare-guard.exe (Rust 2024 Service)           │  │
│  │  - Low-level Win32 API metrics sampling (PDH / WMI / NtQuery)    │  │
│  │  - Thermal, RAM, Disk Space, & Process Anomaly Monitors           │  │
│  └──────────────────────────────────┬───────────────────────────────┘  │
└─────────────────────────────────────┼──────────────────────────────────┘
                                      │ Named Pipe IPC
                                      │ (\\.\pipe\WinCareGuardIPC)
┌─────────────────────────────────────▼──────────────────────────────────┐
│                          WinCare.App (WinUI 3)                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │     Interactive System Care Dashboard & Notification Receiver    │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Detailed Requirements

### Requirement 1: Rust Service Lifecycle & Win32 Integration
- Build `wincare-guard` as a native Rust binary (`native/wincare-guard/`) compiled with `#![windows_subsystem = "windows"]`.
- Integrate with Windows Service Control Manager (SCM) for auto-start on Windows boot (`SERVICE_AUTO_START`).

### Requirement 2: System Health Monitors
- **Disk Guard:** Triggers alert when system drive space drops below 10% or 5 GB.
- **Thermal Guard:** Detects CPU thermal throttling events via ACPI WMI queries.
- **RAM Compression Guard:** Detects excessive memory pressure and cache fragmentation.
- **Rogue Process Guard:** Identifies runaway background processes consuming >90% CPU for over 2 minutes.

### Requirement 3: IPC & Toast Notifications
- Send Windows 11 Native Toast Notifications with interactive action buttons:
  - *"Storage Low: C: drive has 3.2 GB remaining. [ Run Quick Clean ]"*
  - Clicking *"Run Quick Clean"* launches `WinCare.App` directly into the maintenance execution workflow.

---

## 4. Success Criteria

- **Efficiency:** Idle RAM usage verified <= 4.0 MB; zero battery drain on laptops.
- **Reliability:** Service automatically recovers from crashes without leaking handles or OS locks.
