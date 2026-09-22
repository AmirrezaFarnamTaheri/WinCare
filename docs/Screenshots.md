# Interface screenshots

The runtime images below are **end-to-end captures**: each one is rendered by the app itself
when a built portable executable runs `--capture-screens` (the same in-app navigation loop the
packaged smoke test uses), then installed here by `tools/capture_screenshots.py --runtime`.
They are **runtime evidence for the exact build named in each section**, not a perpetual source
of truth for later source changes. The provenance manifest
[`images/runtime-captures.json`](images/runtime-captures.json) is the source that this page's
capture lines are rendered from, and CI re-checks that sync on every build.

## Home

### E2E runtime capture

![WinCare Home screen captured from the v3.0.0 portable build (x64, commit a7dcc77)](images/runtime-dashboard.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit a7dcc77, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. The Home is recommendation-led, derives evidence coverage from shared Activity records, and exposes one primary Checkup CTA.

### Original concept

![Conceptual WinCare dashboard showing system status, health cards, and recent activity](images/dashboard-preview.png)

## Checkup

### E2E runtime capture

![WinCare Checkup screen captured from the v3.0.0 portable build (x64, commit a7dcc77)](images/runtime-checkup.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit a7dcc77, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. Checkup reports checked-area evidence rather than a synthetic machine-health claim; fast read-only probes run concurrently while Windows Update readiness is checked in the background.

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

Capturing requires a Windows host with a built portable executable of the exact version being
recorded; the source tree alone cannot produce runtime evidence. (Installed-MSIX capture is
not yet implemented — only the portable build is supported.) Run:

```text
python tools/capture_screenshots.py --runtime --exe artifacts/portable/win-x64/WinCare.App.exe
```

The tool launches that build in `--capture-screens` mode, verifies every rendered PNG, rewrites `images/runtime-captures.json`, and re-renders this page from the manifest.

Every runtime image must record:

- the exact package/product version;
- architecture (`x64` or `ARM64`);
- the source commit SHA or release tag;
- the packaging source of the image (`portable`; installed MSIX is not yet implemented);
- the Windows appearance used when visually relevant.

Runtime captures must be taken from a known built artifact and kept free of machine names, account names, paths, license keys, tokens, or other personal data. The in-app capture path renders the XAML content surface only, so window chrome and shell titles are never included. Concept imagery must never be presented as a runtime capture.

A screenshot remains authoritative only for the exact build it names. Any UI-affecting change after that build automatically turns the screenshot into **historical runtime evidence** that **needs recapture** and a fresh visual check before it can be cited as current again.
