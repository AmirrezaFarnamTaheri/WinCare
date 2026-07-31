# Windows Validation Evidence

This file records the most recent self-cleaning Windows validation run for the pull-request branch. The transient workflow and the previous evidence record were removed before the repository-owned harness ran.

- **Input commit:** `0deae34d3f26cfcfd3996760a0bc5783602ed8b9`
- **Workflow run:** https://github.com/AmirrezaFarnamTaheri/WinCare/actions/runs/30624168279
- **Status:** **failed**
- **Runner OS:** Windows
- **PowerShell:** 7.6.4
- **.NET SDK:** 8.0.416
- **Pester:** 5.9.0
- **PSScriptAnalyzer:** 1.25.0

## Gate results

| Gate | Status | Exit code | Duration (ms) |
|---|---:|---:|---:|
| 01-static | passed | 0 | 43765 |
| 02-pester | failed | 1 | 514 |
| 03-module-smoke | passed | 0 | 28561 |
| 04-native-build | passed | 0 | 17447 |
| 05-release | failed | 1 | 34984 |
| 06-clean-install | failed | 1 | 588 |

## Environment

- **Windows:** Microsoft Windows Server 2025 Datacenter 10.0.26100 (build 26100, 64-bit)
- **Administrator:** True
- **Culture:** en-US

## Failure summary

```text
Exception: D:\a\WinCare\WinCare\tools\Invoke-WindowsValidation.ps1:411
Line |
 411 |          throw "Windows validation failed in gate(s): $($failed.Name - …
     |          ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | Windows validation failed in gate(s): 02-pester, 05-release, 06-clean-install
```

## Promotion boundary

A passing run validates this source tree on a GitHub-hosted Windows runner. Release promotion still requires the repository-defined signing, provenance, and exact-byte publication controls.
