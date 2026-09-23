# Screenshots

This document records the visual state of WinCare. Screenshots are maintained alongside the source to give reviewers, contributors, and users a reliable visual reference.

A separate automated process captures these images directly from the built executable. The provenance manifest in `docs/images/runtime-captures.json` records the exact build, architecture, and commit each runtime capture was taken from. When a pull request modifies user-facing UI, the images are recaptured from that build, the manifest is updated, and this document is regenerated from the manifest so the images and their descriptions cannot drift apart.

The table below summarizes all current interface images in the repository.

| Preview | Image | Description | Source |
|---|---|---|---|
| <img src="images/runtime-dashboard.png" width="120" alt="Home runtime" /> | `runtime-dashboard.png` | Home screen with system status cards, quick maintenance action, and navigation rail | Built executable (`--capture-screens`, x64, commit c3dbca0) |
| <img src="images/runtime-checkup.png" width="120" alt="Checkup runtime" /> | `runtime-checkup.png` | Checkup diagnostic view showing disk health, system integrity, component status, and pending updates | Built executable (`--capture-screens`, x64, commit c3dbca0) |
| <img src="images/mockup-desktop.png" width="120" alt="Desktop design concept" /> | `mockup-desktop.png` | Original desktop workspace concept showing system vitals, threat map, and quick-action launcher | Design concept |
| <img src="images/mockup-mobile.png" width="120" alt="Mobile design concept" /> | `mockup-mobile.png` | Original mobile diagnostic companion concept with status overview and push notifications | Design concept |

---

## Home

The primary workspace view provides an immediate assessment of system state across storage, security, component servicing, and update status. A single primary Checkup action leads into diagnostic verification, and secondary care areas are accessible from the navigation rail.

### E2E runtime capture

![WinCare Home screen captured from the v3.0.0 portable build (x64, commit c3dbca0)](images/runtime-dashboard.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit c3dbca0, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. The Home is recommendation-led, derives evidence coverage from shared Activity records, and exposes one primary Checkup CTA.

### Original concept

![Desktop workspace concept showing system vitals, threat map, and quick-action launcher](images/mockup-desktop.png)

---

## Checkup

The diagnostic verification view runs read-only probes against the system to assess storage usage, security posture, component store integrity, and update availability. Fast read-only probes execute concurrently while Windows Update readiness continues in the background. Findings link directly to the corresponding care section for remediation.

### E2E runtime capture

![WinCare Checkup screen captured from the v3.0.0 portable build (x64, commit c3dbca0)](images/runtime-checkup.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit c3dbca0, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. Checkup reports checked-area evidence rather than a synthetic machine-health claim; fast read-only probes run concurrently while Windows Update readiness is checked in the background.

### Original concept

![Mobile companion concept with status overview and push notifications](images/mockup-mobile.png)

---

## Capture pipeline

The runtime screenshots above are produced by an automated capture pipeline:

1. `WinCare.App.exe --capture-screens <dir>` launches the application in an isolated mode with a fixed 1280x800 window size and Light theme. It renders each documented route, saves the resulting frame to PNG, and writes a provenance sidecar with build and environment details.
2. `tools/capture_screenshots.py --runtime --exe <path>` manages the capture process, verifies PNG content integrity, updates `docs/images/runtime-captures.json`, and regenerates this markdown file from the manifest.
3. CI verifies that every image referenced in this document exists, is non-empty, and matches the manifest. If a pull request modifies source code that affects visual output, CI requires recaptured screenshots so documentation never lags behind the implementation.

### Provenance requirements

Every runtime image must record:

- the exact package/product version;
- architecture (`x64` or `ARM64`);
- the source commit SHA or release tag;
- the display theme (Light or Dark);
- the window dimensions in device-independent pixels (DIPs);
- the executable name and capture tool version.

This metadata guarantees that every screenshot can be traced back to the exact binary and source commit that produced it.
