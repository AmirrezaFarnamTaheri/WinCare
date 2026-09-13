# Interface screenshots

The checked-in runtime images below were captured from the locally installed **v2.5.0.0 x64 MSIX** built from the `v2.5.0-rc5` source candidate. They are **historical runtime evidence for that exact package**, not a perpetual source of truth for later source changes.

The current source has since moved to a task-first Home, product-wide search, structured Power tools filters, exact care Area/Section routing, a canonical Power tools execution/review surface, visible extension trust state, and a Troubleshoot handoff into that same execution path. Until a new installed candidate is captured, current XAML/theme resources are authoritative for changed surfaces and these images remain baseline references only.

Concept images remain design references and are explicitly marked as concepts.

## Home

### Historical runtime — v2.5.0-rc5 candidate

![WinCare Home screen captured from the installed v2.5.0.0 candidate package](images/runtime-dashboard.png)

**Capture status:** needs recapture after the current PR is packaged. The current Home is recommendation-led, derives evidence coverage from shared Activity records, exposes one primary Checkup CTA, and no longer uses the older instrument-panel hierarchy.

### Original concept

![Conceptual WinCare dashboard showing system status, health cards, and recent activity](images/dashboard-preview.png)

## Checkup

### Historical runtime — v2.5.0-rc5 candidate

![WinCare Checkup screen captured from the installed v2.5.0.0 candidate package](images/runtime-checkup.png)

**Capture status:** needs recapture after the current PR is packaged. The current source reports checked-area evidence rather than a synthetic machine-health claim. Its fast read-only probes run concurrently with bounded concurrency, while Windows Update readiness is checked in the background; compact layouts stack below the shared 920-DIP breakpoint.

### Original concept

![Conceptual WinCare system checkup showing a health score and review-before-apply results](images/checkup-preview.png)

## Terminal REPL (Historical Concept)

### Headless Terminal Exploration Prototype

![Historical concept for a WinCare headless terminal REPL](images/tui-preview.png)

*Design concept only. WinCare ships exclusively as a native WinUI 3 desktop shell; the standalone terminal interface was an exploratory prototype and is not part of the active product distribution.*

## Platform Architecture

### C4 Interactive Architecture Model

![WinCare platform C4 interactive architecture diagram and governance pipeline](images/architecture-preview.png)

## Interactive Web Showcase

### Diagnostic Core Showcase

![WinCare interactive web showcase featuring holographic diagnostic topology, live telemetry dials, and tactile inspection](images/showcase-preview.png)

**Interactive experience:** Open [`docs/showcase.html`](showcase.html) in any modern browser for the live holographic diagnostic topology, command simulator, and responsive telemetry panels.

## Capture policy

Every runtime image must record:

- the exact package/product version;
- architecture (`x64` or `ARM64`);
- the source commit SHA or release tag;
- whether the image came from an installed MSIX or portable build;
- the Windows appearance used when visually relevant.

Runtime captures must be taken from a known built artifact and kept free of machine names, account names, paths, license keys, tokens, or other personal data. Concept imagery must never be presented as a runtime capture.

A screenshot remains authoritative only for the exact build it names. Any UI-affecting change after that build automatically makes the screenshot **historical** until recaptured and visually checked again.
