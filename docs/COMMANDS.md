# WinCare command reference

WinCare exposes the same governed backend through an interactive terminal, a WPF GUI, and headless command routing. This document covers the public entry point and stable command patterns; the runtime capability/action registries remain authoritative for machine-specific availability.

## Public entry point

From the repository root:

```powershell
.\WinCare.ps1 [parameters]
```

Standalone releases expose equivalent supported parameters through `WinCare.exe`, `WinCare-GUI.exe`, and `WinCare-TUI.exe`.

## Parameters

| Parameter | Type | Purpose |
| --- | --- | --- |
| `-NoLogo` | switch | Suppress the terminal logo/banner where supported |
| `-Ascii` | switch | Prefer ASCII-compatible terminal rendering |
| `-ReadOnly` | switch | Start the interactive surface without persistent mutation authority |
| `-Theme` | `Normal`, `HighContrast`, or `Monochrome` | Select presentation semantics without changing operation authority |
| `-Command` | string | Invoke one headless route |
| `-ArgumentsJson` | JSON object string | Supply typed route arguments; defaults to `{}` |
| `-Apply` | switch | Authorize an admitted mutating plan to execute |
| `-Json` | switch | Emit machine-readable JSON and suppress non-result streams |
| `-Gui` | switch | Start the WPF interface |

Parameters are validated by the PowerShell entry point. Standalone executables additionally restrict forwarding to the supported parameter set and reject unexpected positional arguments.

## Operating modes

### Interactive terminal

```powershell
.\WinCare.ps1
```

Useful presentation variants:

```powershell
.\WinCare.ps1 -Ascii
.\WinCare.ps1 -Theme HighContrast
.\WinCare.ps1 -Theme Monochrome -NoLogo
```

### Read-only terminal

```powershell
.\WinCare.ps1 -ReadOnly
```

Read-only mode is an authority constraint, not merely a visual indicator. Providers must not persist state through a presentation-layer bypass.

### WPF GUI

```powershell
.\WinCare.ps1 -Gui
```

The launcher ensures an STA-capable process for the WPF surface. Read-only and theme selection can be forwarded:

```powershell
.\WinCare.ps1 -Gui -ReadOnly -Theme HighContrast
```

### Headless command

```powershell
.\WinCare.ps1 -Command system
```

For automation, request JSON:

```powershell
.\WinCare.ps1 -Command system -Json
```

Headless mode initializes state as read-only unless `-Apply` is supplied.

## JSON arguments

Pass route arguments as one JSON object:

```powershell
.\WinCare.ps1 `
  -Command binary-intelligence `
  -ArgumentsJson '{"LiteralPath":"C:\\Windows\\System32\\notepad.exe"}' `
  -Json
```

PowerShell parses `-ArgumentsJson` with a bounded object depth and passes the resulting hashtable to the command contract. Invalid JSON, missing required values, unsupported properties, or unsafe targets should fail explicitly rather than be guessed.

When invoking from another PowerShell process, prefer `ConvertTo-Json -Compress` over manually constructing complex JSON:

```powershell
$arguments = @{
    LiteralPath = 'C:\Windows\System32\notepad.exe'
} | ConvertTo-Json -Compress

.\WinCare.ps1 -Command binary-intelligence -ArgumentsJson $arguments -Json
```

## Preview and apply

Mutation-capable routes preview by default:

```powershell
.\WinCare.ps1 `
  -Command telemetry-retention `
  -ArgumentsJson '{"RetentionDays":30}' `
  -Json
```

After reviewing the returned plan, explicitly apply the same admitted request:

```powershell
.\WinCare.ps1 `
  -Command telemetry-retention `
  -ArgumentsJson '{"RetentionDays":30}' `
  -Apply `
  -Json
```

`-Apply` is not a blanket success override. The command may still be blocked, denied, unsupported, failed, partial, or inconclusive because of policy, elevation, dependency, identity, execution, or verification results.

## Common observation routes

The following routes are documented examples from the current source tree. Availability still depends on the active capability registry and local Windows providers.

| Route | Purpose | Typical authority |
| --- | --- | --- |
| `system` | Basic system observation and standalone self-test path | Read-only |
| `capabilities` | Current capability/dependency registry | Read-only |
| `fleet-inventory` | Configured bounded peer/fleet observations | Read-only, explicit peers |
| `forensics-timeline` | Bounded forensic event timeline | Read-only |
| `vbs-assurance` | VBS/HVCI/Credential Guard/DMA-related evidence | Read-only |
| `binary-intelligence` | Static bounded binary/PE/signature inspection | Read-only |
| `display-pipeline` | Display/driver/pipeline observations | Read-only |

Examples:

```powershell
.\WinCare.ps1 -Command capabilities -Json
.\WinCare.ps1 -Command forensics-timeline -Json
.\WinCare.ps1 -Command vbs-assurance -Json
.\WinCare.ps1 -Command display-pipeline -Json
```

## Common plan-producing routes

These examples produce a plan by default and require `-Apply` for execution when the underlying contract permits mutation.

| Route | Example purpose | Important boundary |
| --- | --- | --- |
| `vbs-harden` | Plan selected VBS/HVCI/Credential Guard configuration | Edition/hardware/policy dependent; configuration is not an enclave guarantee |
| `sandbox-config` | Generate a bounded Windows Sandbox configuration | Does not implicitly enable the Windows feature or auto-launch the sandbox |
| `telemetry-retention` | Apply local protected telemetry retention | Local state only; no hidden remote warehouse |
| `display-calibrate` | Plan supported DDC/CI calibration | Does not install a display driver |
| `ebpf-admit` | Create a digest-bound program admission record | Admission does not attach the program; runtime operation remains separately guarded |

Examples:

```powershell
.\WinCare.ps1 `
  -Command vbs-harden `
  -ArgumentsJson '{"IncludeCredentialGuard":true,"IncludeHvci":true}' `
  -Json

.\WinCare.ps1 `
  -Command sandbox-config `
  -ArgumentsJson '{"Id":"analysis-box","MappedFolder":"C:\\Cases"}' `
  -Json

.\WinCare.ps1 `
  -Command display-calibrate `
  -ArgumentsJson '{"DeviceName":"DISPLAY1","PhysicalIndex":0,"Brightness":45}' `
  -Json
```

## Capability discovery

Import the module to inspect the machine-specific capability registry:

```powershell
Import-Module .\src\WinCare\WinCare.psd1 -Force
Get-WinCareCapabilityRegistry
```

The registry distinguishes implementation presence from prerequisite availability. A capability is usable only when its implementation and required local mechanism are both present.

See [Capability Matrix](CAPABILITY-MATRIX.md) and [Compatibility](Compatibility.md).

## Result and exit behavior

Headless commands return structured objects. With `-Json`, the result is serialized to JSON and warning/information/progress streams are suppressed to keep stdout machine-readable.

When a returned result exposes `Success = $false`, the script entry point exits with code `1`. Successful execution exits with code `0`. Parse the structured result for the detailed status rather than treating the process exit code as the complete evidence record.

A robust caller should preserve:

- exit code;
- stdout JSON;
- stderr/error record;
- command and arguments;
- apply/read-only mode;
- timestamp and host context needed for diagnosis.

Example:

```powershell
$output = & .\WinCare.ps1 -Command capabilities -Json 2> .\wincare-error.log
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    throw "WinCare failed with exit code $exitCode. See wincare-error.log."
}

$result = $output | ConvertFrom-Json -Depth 50
```

## Standalone executable usage

```powershell
.\WinCare.exe -Command system -Json
.\WinCare-TUI.exe -ReadOnly
.\WinCare-GUI.exe -Theme HighContrast
```

Standalone help is side-effect-free:

```powershell
.\WinCare.exe --help
```

The standalone host accepts the supported WinCare parameters, verifies its embedded payload, and rejects arbitrary positional/script execution.

## Administrator elevation

Do not launch every WinCare session as administrator by default. Start with observation or preview, inspect the plan, and elevate only when the admitted contract requires it.

Elevation does not bypass policy, target validation, dependency checks, or postcondition verification.

## Troubleshooting

### `Blocked` or unavailable capability

Run:

```powershell
.\WinCare.ps1 -Command capabilities -Json
```

Then review [Compatibility](Compatibility.md) for the required Windows mechanism and expected degradation behavior.

### JSON output cannot be parsed

- ensure `-Json` is present;
- capture stderr separately;
- pass one valid JSON object to `-ArgumentsJson`;
- avoid adding formatting commands around WinCare stdout;
- check the exit code before parsing.

### GUI does not start

- confirm Windows 10/11 and PowerShell 7.2+;
- run the terminal surface to expose errors;
- validate the source tree or release package;
- run the Windows validation workflow for development builds.

### Mutation did not occur

- confirm the command is mutation-capable;
- inspect the preview result;
- supply `-Apply` only after review;
- check elevation/policy/dependency status;
- inspect the returned postcondition or failure evidence.

## Command documentation contract

When adding or changing a headless route:

1. update its action/dispatcher and parameter contracts;
2. add preview/apply and negative-path tests;
3. update the capability matrix and compatibility behavior;
4. add a focused example here when the route is operator-facing;
5. avoid copying the entire runtime registry into static documentation.
