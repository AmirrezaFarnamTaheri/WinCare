# Compatibility and degradation model

## Supported target

- Windows 10 22H2 and supported Windows 11 editions
- PowerShell 7.2 or later, Core edition
- x64; ARM64 is supported where the invoked Windows provider/cmdlet exists

Windows Server is not a primary interactive target. Read-only/headless functions may work, but desktop, Store/AppX, brightness, consumer shell, and some Defender/WDAC features can be absent.

## Capability degradation

| Capability | Required mechanism | Behavior when absent |
|---|---|---|
| WinGet package install/upgrade/export | `winget.exe` | Menu/action unavailable; no alternate downloader |
| AppX/MSIX inventory/reset/remove | AppX cmdlets | Store-app functions unavailable |
| WUA lifecycle | `Microsoft.Update.Session` COM | Structured capability failure; Windows Settings remains available |
| WDAC deployment | CiTool plus policy conversion cmdlets | Read-only policy/event inventory where possible; no direct-file fallback |
| Defender scans/preferences | Defender cmdlets | Status indicates unavailable/passive; no third-party AV control |
| Audio controls | Core Audio COM | Volume workspace reports unavailable; no external utility |
| Brightness | WMI monitor brightness classes | Brightness action unavailable on unsupported displays |
| Secure Boot/TPM/BitLocker | Native cmdlets/firmware support | Unknown/unavailable state is displayed explicitly |
| Storage Sense/servicing | Windows cmdlets and build support | Rule is unavailable; no undocumented registry fallback where unsupported |
| Local broker | Policy and named-pipe support | Disabled by default and fails closed |
| LGPO import | Microsoft-signed `LGPO.exe` and valid backup | Import unavailable; current policy remains read-only |
| Sysmon | Microsoft-signed Sysmon executable and valid XML | State/events may be viewed when present; configuration unavailable |
| Offline servicing | DISM PowerShell cmdlets and healthy mounted read-write image | Read-only inventory or structured capability failure |
| Widgets | Allowlisted local providers | Individual widgets degrade independently |
| Bluetooth telemetry | PnP/CIM and Bluetooth event channels | Empty/unavailable observations; no fallback pairing tool |
| Page-file management | CIM `Win32_ComputerSystem`, `Win32_PageFileSetting`, and `Win32_PageFileUsage` | Read-only state unavailable; no Registry-only mutation fallback |
| Expiring security maintenance | Defender cmdlets, Registry, Services, and Scheduled Tasks | The affected control is unavailable and no reduction plan is created |
| Measured TCP experiments | `netsh interface tcp dump/set global`, DNS, ICMP, and TCP sockets | Read-only measurement may remain available; mutation is unavailable without exact token discovery |
| ETW/process instrumentation | WPR, Logman, process module access, Sysmon event channel | Each observation degrades independently; no injection or third-party profiler fallback |
| Power sessions | `SetThreadExecutionState`, PowerShell host process and local protected state | Capability unavailable; no undocumented power-plan mutation fallback |
| Window workspace | Win32 window/monitor APIs and interactive desktop | Read-only/modify actions unavailable in non-interactive or unsupported sessions |
| Screen color and image metadata | GDI pixel capture and System.Drawing | Individual observations fail explicitly; no screenshot or alternate renderer fallback |
| Local notes and palettes | Writable `%LOCALAPPDATA%\WinCare` state root | Read-only/degraded view where possible; mutation fails closed |
| Browser workspace | Local browser processes/profiles/manifests | Missing browsers return empty inventory; no debugging or extension-install fallback |
| Remote-support governance | Local application/process/service/network inventories | Each inventory degrades independently; no remote session is initiated |

## Terminal behavior

Wide terminals show denser rows; narrow terminals use truncated single-column layouts. Unicode is optional. ASCII, monochrome, high-contrast, screen-reader and reduced-motion modes are configuration-level capabilities and do not alter operation authority.

## Localization

The current operator text is English. Provider parsing avoids localized display tables where structured APIs exist. `netsh` text is used only for bounded human-facing summaries and Wi-Fi profile names; it is not an authority-bearing parser for mutating actions.

| Verified downloads | Windows BITS PowerShell module, writable protected state, approved destination root | Queue remains inspectable; mutation returns structured capability failure when BITS is absent |
| Operations telemetry | CIM, formatted performance classes, Get-NetAdapterStatistics, process APIs | Missing metrics return explicit zero/unavailable observations; no remote collector fallback |
| Unified launcher | Interactive Windows shell, Start menu, App Paths, Control Panel, Settings URIs | Returns an empty/partial bounded index; arbitrary command execution is never used as fallback |
| Steam game-state | Local Steam installation and userdata tree | Returns empty inventory; no online Steam credential/API fallback |
| Offline reduction | AppX/DISM cmdlets and healthy mounted read-write image | Assessment/mutation unavailable; no raw image-file or Registry-hive fallback |
| Workspace layouts | Interactive desktop and Win32 window/monitor APIs | Catalog remains available; apply returns structured capability failure |
