# WinCare AI System Doctor — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Implementation-Ready (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/plans/2026-08-14-ai-system-doctor-requirements.md`

---

## 1. Overview & Architecture Summary

This plan details the technical tasks for implementing the **WinCare AI System Doctor**, an on-device natural language system diagnostic assistant powered by ONNX Runtime with DirectML GPU acceleration.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: ONNX Runtime DirectML Infrastructure & Model Loading
**Goal:** Integrate `Microsoft.ML.OnnxRuntime.DirectML` and build local model loading infrastructure in `WinCare.Application`.

- **Files touched / created:**
  - `src/WinCare.Application/Diagnostics/OnnxInferenceEngine.cs`
  - `src/WinCare.Application/Diagnostics/ModelManager.cs`
  - `src/WinCare.Application/WinCare.Application.csproj`
  - `tests/WinCare.Application.Tests/OnnxInferenceEngineTests.cs`

- **Acceptance Criteria:**
  - [ ] `Microsoft.ML.OnnxRuntime.DirectML` added to `WinCare.Application.csproj`.
  - [ ] `ModelManager` checks for quantized ONNX weights in `%ProgramData%/WinCare/Models/doctor.onnx` and downloads default weights on first launch.
  - [ ] `OnnxInferenceEngine` initializes DirectML session with cold-start latency <1.5s.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Application.Tests --filter "OnnxInferenceEngineTests"`

---

### Unit 2: Intent Classification & Command Action Translator
**Goal:** Implement intent parser mapping natural language text into `CommandDefinition` execution plans.

- **Files touched / created:**
  - `src/WinCare.Application/Diagnostics/IntentTranslator.cs`
  - `src/WinCare.Application/Diagnostics/DoctorActionPlan.cs`
  - `tests/WinCare.Application.Tests/IntentTranslatorTests.cs`

- **Acceptance Criteria:**
  - [ ] `IntentTranslator` maps prompt queries (e.g., *"Clean up C drive"*) into `DoctorActionPlan` objects.
  - [ ] All recommended action plan commands strictly match registered `CommandDefinition` IDs in `ToolCatalogService`.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Application.Tests --filter "IntentTranslatorTests"`

---

### Unit 3: WinUI 3 AI Doctor Conversational View
**Goal:** Create `AiDoctorPage.xaml` conversational chat interface in `WinCare.App`.

- **Files touched / created:**
  - `src/WinCare.App/Views/Pages/AiDoctorPage.xaml`
  - `src/WinCare.App/Views/Pages/AiDoctorPage.xaml.cs`
  - `src/WinCare.App/ViewModels/Pages/AiDoctorPageViewModel.cs`
  - `src/WinCare.App/Views/ShellPage.xaml`

- **Acceptance Criteria:**
  - [ ] `AiDoctorPage` added to Shell left navigation pane with Cyber-Teal design tokens.
  - [ ] Renders chat bubbles, system diagnosis summaries, and 1-click action execution buttons.
  - [ ] Action execution triggers `CommandDispatcher` fail-closed safety checks.

- **Verification Commands:**
  - `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64`
