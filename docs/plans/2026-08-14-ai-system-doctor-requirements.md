# WinCare AI System Doctor — Product & Technical Requirements Specification

- **Date:** 2026-08-14
- **Status:** Requirements Specification (`artifact_readiness: requirements-only`)
- **Contract Version:** `ce-unified-plan/v1`
- **Source:** `/ce-ideate` -> `/ce-brainstorm`

---

## 1. Executive Summary & Intent

WinCare will introduce the **AI System Doctor**, an on-device, privacy-first natural language diagnostic assistant powered by ONNX Runtime for Windows.

### Key Capabilities
1. **Offline Natural Language Querying:** Users can ask plain English questions (e.g., *"Why is my C: drive filling up fast?"*, *"Optimize my PC for high FPS gaming"*, *"Is my firewall configured safely?"*).
2. **Intent & Command Mapping:** AI Doctor analyzes system state and maps user intents directly into safe, executable WinCare maintenance pipelines.
3. **100% Privacy & Zero Telemetry Leak:** Model weights execute locally via DirectML / GPU acceleration without sending user telemetry or system metrics to external cloud servers.

---

## 2. Architecture & System Flow

```
┌────────────────────────────────────────────────────────────────────────┐
│                        WinCare.App (WinUI 3 UI)                        │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │               AI System Doctor Conversational View               │  │
│  └──────────────────────────────────┬───────────────────────────────┘  │
└─────────────────────────────────────┼──────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼──────────────────────────────────┐
│                      WinCare.Application.Diagnostics                   │
│  ┌────────────────────────┐  ┌───────────────────┐  ┌───────────────┐ │
│  │   OnnxInferenceEngine  │  │ IntentTranslator  │  │ ActionRunner  │ │
│  │   (DirectML / Local)   │  │ (Intent -> Cmds)  │  │ (Dispatcher)  │ │
│  └───────────┬────────────┘  └─────────┬─────────┘  └───────┬───────┘ │
└──────────────┼─────────────────────────┼────────────────────┼──────────┘
               │                         │                    │
┌──────────────▼─────────────────────────▼────────────────────▼──────────┐
│                   WinCare.Domain & CommandDispatcher                   │
│  ┌─────────────────────────┐         ┌──────────────────────────────┐ │
│  │  CommandDispatcher      │<───────>│  wincare-core (Rust 2024 FFI) │ │
│  │  (Fail-Closed Safety)   │         │  (System Telemetry & OS API) │ │
│  └─────────────────────────┘         └──────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Detailed Requirements & User Scenarios

### Requirement 1: Local ONNX Model Runner
- Integrate `Microsoft.ML.OnnxRuntime.DirectML` package into `WinCare.Application`.
- Load quantized 4-bit SLM (Small Language Model / Intent Classifier) stored locally in `%ProgramData%/WinCare/Models/`.
- Ensure cold-start model load time is under 1.5 seconds.

### Requirement 2: Intent-to-Command Pipeline Mapping
- Parse natural language queries into structured JSON action plans:
  ```json
  {
    "userQuery": "Clean up space on C drive",
    "detectedIntent": "STORAGE_CLEANUP",
    "recommendedCommands": [
      "cleaner.deep_temp_purge",
      "cleaner.empty_recycle_bin",
      "cleaner.deliver_optimization_cache"
    ],
    "explanation": "I found 4.2 GB of temporary cache files and delivery optimization logs that can be safely removed."
  }
  ```
- Present recommended maintenance steps with user confirmation buttons before execution.

### Requirement 3: Safety & Fail-Closed Guardrails
- AI Doctor cannot execute system modifications directly; all action plans must pass through `CommandDispatcher` safety admission rules.
- High-risk operations (e.g. registry tweaking, service disabling) require explicit UAC and user confirmation modals.

---

## 4. Success & Quality Criteria

- **Performance:** Query response generation under 200ms on DirectML-capable GPUs or modern NPU/CPUs.
- **Privacy:** 0 outbound network requests during AI Doctor interaction.
- **Accuracy:** >95% intent classification accuracy across standard Windows troubleshooting prompts.
