# WinCare Rust Background Health Guard Service — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Implementation-Ready (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/plans/2026-08-14-rust-background-guard-service-requirements.md`

---

## 1. Overview & Architecture Summary

This plan details the technical implementation of `wincare-guard.exe`, a lightweight Windows Background Service written in Rust 2024 for 24/7 proactive health monitoring and Named Pipe IPC notification streaming to WinCare.App.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: Rust Windows Service Crate & SCM Lifecycle
**Goal:** Create `native/wincare-guard` crate integrated with `windows-service` crate for Windows Service Control Manager startup/stop events.

- **Files touched / created:**
  - `native/wincare-guard/Cargo.toml`
  - `native/wincare-guard/src/main.rs`
  - `native/wincare-guard/src/service.rs`
  - `native/Cargo.toml`

- **Acceptance Criteria:**
  - [ ] `wincare-guard.exe` compiles cleanly in Rust 2024 release mode.
  - [ ] Implements ServiceMain and handler loop responding to `SERVICE_CONTROL_STOP`.
  - [ ] Idle memory footprint verified <= 4.0 MB RAM.

- **Verification Commands:**
  - `cargo check --manifest-path native/wincare-guard/Cargo.toml`

---

### Unit 2: System Telemetry Sampling & Threshold Monitors
**Goal:** Implement disk space, thermal throttling, and RAM pressure samplers in Rust.

- **Files touched / created:**
  - `native/wincare-guard/src/monitors/disk.rs`
  - `native/wincare-guard/src/monitors/thermal.rs`
  - `native/wincare-guard/src/monitors/ram.rs`

- **Acceptance Criteria:**
  - [ ] Periodically polls disk free space and CPU thermal state every 30s.
  - [ ] Raises alert events when free disk space falls below 5 GB.

- **Verification Commands:**
  - `cargo test --manifest-path native/wincare-guard/Cargo.toml`

---

### Unit 3: Named Pipe IPC & Windows Native Toast Sender
**Goal:** Stream health alerts via Named Pipe (`\\.\pipe\WinCareGuardIPC`) and display interactive Windows 11 Toast Notifications.

- **Files touched / created:**
  - `native/wincare-guard/src/ipc/pipe_server.rs`
  - `native/wincare-guard/src/notifications/toast.rs`
  - `src/WinCare.Infrastructure/IPC/GuardPipeClient.cs`

- **Acceptance Criteria:**
  - [ ] Named Pipe server accepts connection from `WinCare.App`.
  - [ ] Displays Windows 11 Native Toast with interactive button launching `WinCare.App`.

- **Verification Commands:**
  - `cargo test --manifest-path native/wincare-guard/Cargo.toml`
