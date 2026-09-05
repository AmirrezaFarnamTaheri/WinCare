# WinCare AI System Doctor — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Completed — with the inference engine pivoted to a rule-based classifier (see note below)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/plans/2026-08-14-ai-system-doctor-requirements.md`

> [!NOTE]
> The shipped implementation replaced the originally planned ONNX Runtime / DirectML
> inference engine with a deterministic, on-device rule-based classifier
> (`RuleBasedIntentInferenceEngine`, formerly `OnnxInferenceEngine.cs`). There is no
> `Microsoft.ML.OnnxRuntime` / `Microsoft.ML.OnnxRuntime.DirectML` package, no model
> weights, and no `ModelManager` in the product. The ONNX acceptance criteria below are
> therefore superseded; all other Doctor units (intent translation, evidence collection,
> fail-closed action plans) shipped as described.

---

## 1. Overview & Architecture Summary

This plan details the technical implementation of the **WinCare AI System Doctor**, an on-device natural language system diagnostic assistant. The inference stage ships as a rule-based classifier rather than the ONNX/DirectML engine described in the original units.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: ONNX Runtime DirectML Infrastructure & Model Loading
**Goal:** Integrate `Microsoft.ML.OnnxRuntime.DirectML` and build local model loading infrastructure in `WinCare.Application`.

- **Files touched / created:**
  - `src/WinCare.Application/Diagnostics/OnnxInferenceEngine.cs`
  - `src/WinCare.Application/Diagnostics/ModelManager.cs`
  - `src/WinCare.Application/WinCare.Application.csproj`
  - `tests/WinCare.Application.Tests/IntentTranslatorTests.cs`

- **Acceptance Criteria:**
  - [~] `Microsoft.ML.OnnxRuntime.DirectML` reference configured for on-device inference. *(Superseded — no ONNX runtime is used.)*
  - [~] `ModelManager` checks for quantized ONNX weights in `%ProgramData%/WinCare/Models/doctor.onnx` and provides default weight loading. *(Superseded — `ModelManager` was removed.)*
  - [x] Inference engine initializes with cold-start latency <1.5s. *(Satisfied by `RuleBasedIntentInferenceEngine`, whose initialization is a near-zero-cost no-op.)*

- **Verification Commands:**
  - `dotnet test tests/WinCare.Application.Tests --filter "IntentTranslatorTests"`

---

### Unit 2: Intent Classification & Command Action Translator
**Goal:** Implement intent parser mapping natural language text into `CommandDefinition` execution plans.

- **Files touched / created:**
  - `src/WinCare.Application/Diagnostics/IntentTranslator.cs`
  - `src/WinCare.Application/Diagnostics/DoctorActionPlan.cs`
  - `tests/WinCare.Application.Tests/IntentTranslatorTests.cs`

- **Acceptance Criteria:**
  - [x] `IntentTranslator` maps prompt queries (e.g., *"Clean up C drive"*) into `DoctorActionPlan` objects.
  - [x] All recommended action plan commands strictly match registered `CommandDefinition` IDs in `ToolCatalogService`.

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
  - `src/WinCare.App/Services/PageService.cs`

- **Acceptance Criteria:**
  - [x] `AiDoctorPage` added to Shell left navigation pane with Cyber-Teal design tokens.
  - [x] Renders chat bubbles, system diagnosis summaries, and 1-click action execution buttons.
  - [x] Action execution triggers `CommandDispatcher` fail-closed safety checks.

- **Verification Commands:**
  - `python tools/verify_visual_tokens.py`
