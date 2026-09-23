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

![WinCare Home screen captured from the v3.0.0 portable build (x64, commit ad515dd)](images/runtime-dashboard.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit ad515dd, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. The Home is recommendation-led, derives evidence coverage from shared Activity records, and exposes one primary Checkup CTA.

### Original concept

![Desktop workspace concept showing system vitals, threat map, and quick-action launcher](images/mockup-desktop.png)

## Checkup

### E2E runtime capture

![WinCare Checkup screen captured from the v3.0.0 portable build (x64, commit ad515dd)](images/runtime-checkup.png)

**Capture status:** captured 2026-09-22 from the v3.0.0 portable build (x64, commit ad515dd, Light appearance, 1280x800 DIP window) by running `--capture-screens` against that artifact. Authoritative for that exact build; any later UI change makes it historical until recaptured. Checkup reports checked-area evidence rather than a synthetic machine-health claim; fast read-only probes run concurrently while Windows Update readiness is checked in the background.

### Original concept

![Mobile companion concept with status overview and push notifications](images/mockup-mobile.png)

## Capture policy

Capturing requires a Windows host with a built portable executable (or installed MSIX) of the exact version being recorded; the source tree alone cannot produce runtime evidence. Run:

```text
python tools/capture_screenshots.py --runtime --exe artifacts/portable/win-x64/WinCare.App.exe
```

The tool launches that build in `--capture-screens` mode, verifies every rendered PNG, rewrites `images/runtime-captures.json`, and re-renders this page from the manifest.

Every runtime image must record:

- the exact package/product version;
- architecture (`x64` or `ARM64`);
- the source commit SHA or release tag;
- whether the image came from an installed MSIX or portable build;
- the Windows appearance used when visually relevant.

Runtime captures must be taken from a known built artifact and kept free of machine names, account names, paths, license keys, tokens, or other personal data. The in-app capture path renders the XAML content surface only, so window chrome and shell titles are never included. Concept imagery must never be presented as a runtime capture.

A screenshot remains authoritative only for the exact build it names. Any UI-affecting change after that build automatically turns the screenshot into **historical runtime evidence** that **needs recapture** and a fresh visual check before it can be cited as current again.
