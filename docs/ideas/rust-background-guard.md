# Rust Background Health Guard Service (`wincare-guard.exe`)

## Problem Statement
How might we provide 24/7 proactive system health, thermal, and disk space monitoring for Windows PCs without background memory bloat or CPU drain?

## Recommended Direction
Build a lightweight Windows Background Service in Rust 2024 (`wincare-guard.exe`) compiled with `#![windows_subsystem = "windows"]`. The service runs quietly in the background using Win32 APIs (PDH, WMI, NtQuerySystemInformation) to monitor free disk space, CPU thermal throttling, RAM cache pressure, and runaway background processes every 30 seconds.

When health issues are detected, `wincare-guard.exe` triggers Windows 11 Native Toast Notifications featuring interactive action buttons. Clicking an action button uses Named Pipe IPC (`\\.\pipe\WinCareGuardIPC`) to launch `WinCare.App` directly into the targeted maintenance execution view.

## Key Assumptions to Validate
- [ ] **Idle Memory Footprint:** Confirm RAM usage remains strictly <= 4.0 MB while background monitoring.
- [ ] **CPU Overhead:** Confirm CPU usage stays <0.1% during 30s polling cycles.
- [ ] **IPC Reliability:** Named Pipe IPC connection establishes cleanly on Windows 10/11 without firewall prompts.

## MVP Scope
### What's In
- Windows Service wrapper (`windows-service` Rust crate).
- Disk space (<5GB trigger) and RAM pressure samplers.
- Windows 11 Native Toast Notification sender.
- Named Pipe IPC connection to `WinCare.App`.

### What's Out
- Kernel-mode driver components (User-mode Win32 service only).
- Cloud reporting / external telemetry upload.

## Not Doing (and Why)
- **C# / .NET Background Service:** Replaced with Rust to eliminate .NET Garbage Collection overhead and keep memory footprint under 4MB.
- **Continuous High-Frequency Polling (100ms):** Avoided 30s intervals to preserve laptop battery life.

## Open Questions
- What is the best installer strategy for registering the Windows Service during MSIX / Portable app setup?
