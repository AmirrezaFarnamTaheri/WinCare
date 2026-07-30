# WinCare release engineering

WinCare treats a release as an immutable, independently verified evidence bundle. Producing an archive or executable is not sufficient for production promotion.

## Release principles

A supported production release must be:

- built from one audited source state;
- clean unless a development-only dirty build is explicitly requested;
- validated by structural and Windows runtime gates;
- composed with source-built native artifacts;
- deterministic for the declared inputs and source epoch;
- accompanied by manifests, checksums, receipts, SBOM/provenance evidence, and standalone metadata;
- independently verified as hostile input;
- installed, repaired, and uninstalled through the lifecycle gate;
- promoted without changing the verified bytes.

## Requirements

Run production release construction on a supported Windows environment with:

- PowerShell 7.2+;
- Python 3;
- .NET 8 SDK;
- Pester 5.5.0;
- PSScriptAnalyzer when required by the validation profile;
- a clean source tree and sufficient temporary/output space.

The complete CI release workflow is the authoritative promotion path. Local builds are useful for reproduction and preflight but do not replace required CI evidence.

## Primary command

From the repository root:

```powershell
.\tools\Build-Release.ps1 -OutputDirectory .\artifacts\release
```

The output directory must be absent or empty, must not be the repository root or drive root, and must not traverse a reparse-point ancestor.

### Parameters

| Parameter | Purpose | Promotion note |
| --- | --- | --- |
| `-Root` | Repository root; normally inferred | Must resolve to the intended audited source tree |
| `-OutputDirectory` | Final release asset directory | Must be safe and empty |
| `-SkipTests` | Skip selected local test phases | Development/diagnostic only; not production evidence |
| `-AllowDirty` | Permit a dirty source receipt | Produces non-promotable development evidence unless promotion policy explicitly says otherwise |

Do not use `-SkipTests` or `-AllowDirty` for a production candidate.

## Build phases

`Build-Release.ps1` performs the following high-level phases.

### 1. Static checks

The release invokes the PowerShell/static validation layer and requires PSScriptAnalyzer when available/required. Structural validation covers closed source membership, public contracts, bounded authority, maintainability, and other repository invariants.

### 2. Pester

Unless explicitly skipped, every repository Pester suite runs. Failed tests, failed blocks, or failed containers stop the release.

### 3. Standalone adversarial tests

Python tests exercise malicious or malformed standalone payload/archive conditions and host/tooling behavior.

### 4. Native build

All required .NET/native projects are built from the current source state into an isolated work directory. Production receipts must identify native artifacts as source-built and verified.

### 5. Core production package

The Python release builder creates the core production archive and evidence with the production profile. The release version is read from `src/WinCare/WinCare.psd1`.

The core archive is named:

```text
WinCare-<version>.zip
```

### 6. Standalone payload

The verified core archive is transformed into the embedded standalone payload. The payload report must contain a passing status, payload SHA-256, manifest SHA-256, member count, and byte count.

### 7. Standalone publish

`tools/Build-Exe.ps1` publishes three self-contained single-file executables:

- `WinCare.exe`;
- `WinCare-GUI.exe`;
- `WinCare-TUI.exe`.

Each project receives the exact payload path and payload/manifest hashes as build properties. Published outputs are checked for expected PE architecture/subsystem, loose-file contamination, executable self-test, size, and independent hash identity.

### 8. Finalize release

The finalizer assembles the core archive, standalone executables, and release evidence into the output directory. Unexpected loose directories are not permitted in the final asset set.

### 9. Independent v3 verification

`tools/verify_release_v3.py` re-opens the final archive and asset directory independently. A passing result must include a valid archive SHA-256 and verified member count.

### 10. Installation lifecycle

`tools/Test-InstallationLifecycle.ps1` exercises the final immutable archive through the supported installation lifecycle, including clean installation, identity/integrity validation, repair behavior, shortcuts/launchers as applicable, and uninstall verification.

If any phase fails, the final output directory is cleared rather than leaving a partial candidate that could be mistaken for a release.

## Release evidence

Depending on profile/version, the release includes or references evidence such as:

- release archive and standalone executables;
- `RELEASE-MANIFEST.sha256` or equivalent closed file manifest;
- build receipt;
- release receipt;
- standalone build manifest;
- asset checksums;
- SPDX SBOM;
- in-toto provenance;
- source/native/payload hashes;
- verifier report;
- installation lifecycle evidence;
- validation logs and diagnostic indexes.

Consumers and installers should verify the evidence appropriate to the release schema rather than relying on the filename alone.

## Independent verification

Verification must be performed by code that treats the archive and companion assets as untrusted input. It should reject, as applicable:

- absolute, parent-traversal, reserved, or otherwise unsafe paths;
- duplicate or case-colliding members;
- links or reparse-like content;
- excessive entry count, member size, total size, or expansion ratio;
- unmanifested or missing files;
- hash, receipt, profile, version, payload, native, or provenance mismatches;
- dirty production receipts;
- development/production profile contamination;
- unexpected standalone or loose assets.

A verifier must fail closed on malformed evidence or unsupported schemas.

## Installation and repair

Install only from a verified production package:

```powershell
.\Install-WinCare.ps1
```

The installer verifies product, version, manifest, receipt, path, and source-built native evidence before managing the destination. It creates identity-bound installation metadata and transactional shortcuts.

Repair a recognized installation with:

```powershell
.\Install-WinCare.ps1 -Repair
```

Use a custom safe destination or shortcut root only when required:

```powershell
.\Install-WinCare.ps1 `
  -Destination "$env:LOCALAPPDATA\Programs\WinCare" `
  -ShortcutRoot "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
```

Installation refuses unsafe roots, reparse traversal, unverified packages, unrecognized managed trees, and incompatible receipts rather than silently overwriting them.

## Uninstallation

```powershell
.\Uninstall-WinCare.ps1
```

Optional data removal is separate from program removal:

```powershell
.\Uninstall-WinCare.ps1 -PurgeData
```

Corrupt-install removal is an exceptional recovery path and must still satisfy guarded identity/path rules:

```powershell
.\Uninstall-WinCare.ps1 -RemoveCorruptInstallation
```

The uninstaller validates the managed installation tree and product identity before destructive removal. It does not treat an arbitrary directory as WinCare merely because its path has a familiar name.

## Promotion procedure

A release engineer should:

1. confirm the intended version and changelog;
2. confirm the source tree and workflow inputs are the intended immutable state;
3. run or observe all mandatory source and Windows gates;
4. build source-native and standalone artifacts through the production release path;
5. independently verify the exact final assets;
6. review the SBOM, receipts, checksums, provenance, and validation summaries;
7. verify clean installation, repair, launch/self-test, and uninstall evidence;
8. record the exact asset hashes selected for promotion;
9. publish those exact bytes without rebuilding or repacking;
10. re-download and re-verify the published assets when the distribution path permits.

Never promote a newly rebuilt artifact merely because it has the same version or filename as the verified candidate.

## Reproducibility and source epoch

The tooling resolves a source date epoch from the repository/build context and uses deterministic construction rules. Reproducibility comparisons must hold inputs constant, including:

- source commit and dirty state;
- module version;
- toolchain versions;
- runtime identifier/configuration;
- native source closure;
- package profile;
- source date epoch;
- dependency lock state.

A byte difference must be investigated; it must not be waived solely because both artifacts appear functional.

## Failure handling

When a release fails:

- preserve diagnostics and the phase that failed;
- do not publish partial output;
- separate task-created regressions from pre-existing or environmental failures;
- remediate only failures related to the candidate change;
- rerun the failed gate and then the full mandatory chain before promotion;
- do not manually edit final archives, receipts, manifests, or checksums.

## Versioning and release notes

The module version in `src/WinCare/WinCare.psd1` is the runtime/release version source used by the builder. Update [CHANGELOG.md](../CHANGELOG.md) for the supported release and ensure version claims are consistent across module metadata, receipts, asset names, installer evidence, and release publication.

## Related documents

- [Validation](../VALIDATION.md)
- [Testing](Testing.md)
- [Architecture](ARCHITECTURE.md)
- [Security](../SECURITY.md)
- [Contributing](../CONTRIBUTING.md)
