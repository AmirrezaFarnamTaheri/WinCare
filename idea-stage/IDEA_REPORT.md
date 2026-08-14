# WinCare Architecture & Platform Research Idea Report

**Direction**: High-Performance Desktop System Care Platform & Modular Architecture Evolution
**Generated**: 2026-08-14
**Ideas evaluated**: 8 generated → 6 survived filtering → 4 recommended for roadmap

---

## 1. Landscape & Architectural State Summary

WinCare is a high-performance Windows 10/11 system care application built with a dual C# WinUI 3 frontend (`WinCare.App`) and a Rust 2024 native FFI safety kernel (`wincare-core`).

The platform has achieved 100% parity across frozen oracle data (259 system commands) and established a fail-closed `CommandDispatcher` safety admission engine. The current milestone introduces a **4-tier Plugin Store Architecture**, decoupling core tool categories into modular, user-customizable extension packages.

---

## 2. Recommended Research & Engineering Ideas (Ranked)

### Idea 1: Local ONNX DirectML Natural Language Diagnostics ("AI System Doctor")
- **Method (what we actually build)**: Integrate `Microsoft.ML.OnnxRuntime.DirectML` in C# `WinCare.Application`, loading 4-bit quantized local SLM weights (`doctor.onnx`) from `%ProgramData%/WinCare/Models/`. The engine parses natural language queries, classifies maintenance intent, and translates them into structured `CommandDefinition` action plans executed via `CommandDispatcher`.
- **Hypothesis**: On-device DirectML inference provides sub-200ms intent classification for system troubleshooting without sending user telemetry or system metrics to external cloud APIs.
- **Minimum Experiment**: Benchmark cold-start model load time (<1.5s) and query parsing latency (<200ms) on integrated Intel Iris Xe / AMD Radeon graphics and discrete NVIDIA GPUs across 100 test maintenance prompts.
- **Expected Outcome**: 100% offline, zero-latency system doctor experience with >95% intent classification accuracy.
- **Novelty**: 9/10 — Local DirectML SLM integration inside a desktop WinUI 3 maintenance host.
- **Feasibility**: High (C# `Microsoft.ML.OnnxRuntime.DirectML` package available).
- **Risk**: LOW (Local execution fallback).
- **Contribution Type**: New Method / System Architecture.

---

### Idea 2: Zero-Copy FFI Telemetry Buffer Sharing (Rust -> WinUI 3)
- **Method (what we actually build)**: Allocate shared memory buffers in Rust `wincare-core` and expose pointers via C ABI. `WinCare.Application` maps shared memory regions directly to C# `Memory<T>` structs, streaming real-time CPU, GPU, RAM, and Disk I/O metrics directly to WinUI 3 telemetry charts without managed heap allocations.
- **Hypothesis**: Zero-copy FFI buffer sharing eliminates C# GC pauses during high-rate (60 FPS) dashboard telemetry rendering.
- **Minimum Experiment**: Measure GC allocation rate (bytes/sec) and frame rendering latency during 60 FPS telemetry streaming over 10-minute continuous stress runs.
- **Expected Outcome**: Zero managed allocations during telemetry streaming, 60 FPS smooth animation curve.
- **Novelty**: 8/10 — High-frequency zero-copy FFI IPC between Rust 2024 and C# WinUI 3.
- **Feasibility**: High (Rust `std::slice` and C# `Memory<T>` / `UnmanagedMemoryStream`).
- **Risk**: LOW (Unsafe pointer safety validated via Rust `catch_unwind`).
- **Contribution Type**: Performance / System Architecture.

---

### Idea 3: Community Plugin SDK & Developer CLI (`wincare-plugin-cli`)
- **Method (what we actually build)**: Develop a Node.js CLI tool (`tools/wincare-plugin-cli`) providing `create`, `validate`, `test`, and `pack` subcommands. Enforce automated manifest linting, path traversal checks (`../`), and script safety verification before packaging distribution `.wincare-plugin` ZIP archives.
- **Hypothesis**: Providing an automated CLI tool reduces developer onboarding time for custom WinCare plugins to under 3 minutes.
- **Minimum Experiment**: Test third-party developer creation flow across 10 sample PowerShell script packs and C# assembly plugins.
- **Expected Outcome**: 100% of malformed manifests or unsafe relative paths caught during `wincare-plugin validate`.
- **Novelty**: 7/10 — CLI-first extension toolchain for desktop WinUI 3 apps.
- **Feasibility**: High.
- **Risk**: LOW.
- **Contribution Type**: Developer Experience / Tooling.

---

### Idea 4: Ultra-Low Overhead Rust Background Health Service (`wincare-guard.exe`)
- **Method (what we actually build)**: Compile a native Rust 2024 Windows Service (`native/wincare-guard`) using `windows-service` crate. Poll disk space, CPU thermal state, and memory pressure every 30s. Raise Windows 11 Native Toast Notifications connected via Named Pipe IPC (`\\.\pipe\WinCareGuardIPC`) to launch WinCare.App.
- **Hypothesis**: A native Rust background service achieves continuous 24/7 system health monitoring with idle memory usage <4.0 MB RAM and CPU usage <0.1%.
- **Minimum Experiment**: Measure RAM footprint and CPU usage over 24-hour continuous background monitoring on Windows 10/11 VMs.
- **Expected Outcome**: Idle RAM <4.0 MB, zero battery impact on laptops, instant IPC notification response.
- **Novelty**: 8/10 — Rust-native Windows Service IPC bridge for desktop maintenance apps.
- **Feasibility**: High.
- **Risk**: LOW.
- **Contribution Type**: System Performance / Architecture.

---

## 3. Eliminated & Deprioritized Ideas

| Idea | Reason Eliminated |
|---|---|
| Cloud-hosted LLM Diagnostic Server | Eliminated due to user privacy risks and ongoing server API cost. Replaced with local ONNX DirectML. |
| Runtime Reflection Command Dispatcher | Deprioritized in favor of C# Source Generators (`[GenerateCommandHandler]`) for sub-millisecond cold start times. |

---

## 4. Suggested Execution Order

1. **Milestone 1:** Plugin Store Architecture & Extension Engine (Unit 1 - Unit 6 in `docs/plans/2026-08-14-plugin-store-and-modular-architecture-plan.md`).
2. **Milestone 2:** AI System Doctor & Local ONNX Diagnostics.
3. **Milestone 3:** Community Plugin SDK & Developer CLI.
4. **Milestone 4:** Rust Background Guard Service.
