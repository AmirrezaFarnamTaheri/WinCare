# AI System Doctor — Local ONNX Diagnostics Assistant

## Problem Statement
How might we enable Windows users to diagnose and fix complex PC performance and system issues using natural language without exposing sensitive system telemetry or user data to cloud APIs?

## Recommended Direction
Build an on-device, privacy-first natural language diagnostic assistant inside WinCare using ONNX Runtime for Windows with DirectML GPU/NPU acceleration. The assistant loads local 4-bit quantized Small Language Model (SLM) weights (`doctor.onnx`) stored in `%ProgramData%/WinCare/Models/`.

When users input plain English queries (*"Why is my C: drive filling up?"* or *"Optimize PC for gaming"*), the model classifies intent and translates it into structured WinCare command execution pipelines. All maintenance actions require explicit user confirmation before executing through `CommandDispatcher` safety rules.

## Key Assumptions to Validate
- [ ] **DirectML Execution Latency:** Validate that cold-start DirectML model loading takes <1.5s on mid-range Intel/AMD integrated GPUs and NVIDIA discrete GPUs.
- [ ] **Intent Classification Accuracy:** Validate >95% accuracy across 100 benchmark Windows maintenance prompts.
- [ ] **RAM Footprint:** Confirm model memory footprint remains under 450 MB RAM when idle or loaded in memory.

## MVP Scope
### What's In
- Chat interface in `WinCare.App` (`AiDoctorPage.xaml`).
- Intent parsing for top 3 maintenance categories: Storage Cleanup, Memory Optimization, and Network/Privacy Checkup.
- 1-click action plan preview and execution buttons.

### What's Out
- Online cloud AI fallback (100% offline local model execution only).
- Autonomous background maintenance execution without user click.

## Not Doing (and Why)
- **Cloud LLM API Integration:** Avoided to protect user privacy and eliminate monthly API key / subscription requirements.
- **Full System Registry Rewriting:** Excluded from initial release to prevent system stability risks.

## Open Questions
- What is the optimal quantized ONNX SLM model weight file size (e.g. 250MB vs 600MB) for balancing download speed and intent classification quality?
