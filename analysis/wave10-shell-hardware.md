# Wave 10: Shell & Hardware Studio convergence

## Scope

D144-D163 were validated before extraction. The wave accounts for 14,782 regular files, 15 repository symlinks, and approximately 390 MB of expanded evidence. D151, D155, D157, and D160 are exact archive repeats of D32, D57, D20, and D34; they preserve provenance without creating duplicate ownership.

## Adopted and hardened outcomes

- bounded CIM hardware inventory with sensitive identifiers hashed by default;
- DDC/CI VCP 0x10/0x12 inspection and mutation bound to logical device, physical index, description, current value, and maximum, with verified compensation;
- fuzzy top-level-window discovery and explicit Explorer local-directory session capture/restore;
- UI Automation snapshots bounded by process, depth, node count, and redacted text;
- AppX process/package identity and manifest-hash-bound activation through supported Windows APIs;
- no-extraction ZIP/AppX/MSIX/NuGet member, traversal, collision, symlink, expansion, and compression-ratio inspection;
- read-only DISM metadata for local WIM/ESD/SWM media;
- sanitized Windows Terminal and ConEmu profile metadata with command lines and directories represented only by hashes;
- XAML resource definition/reference auditing;
- assessment-only custom full-screen shell detection;
- `winhance-maximum` through the existing Critical Legacy Unsafe authorization plane.

## Rejected or superseded

Resident global hooks, AutoHotkey runtimes, shell injection, custom-shell replacement, AppX suspend/terminate/debug samples, remote script downloads, terminal injection, codec engines, shell extensions, drivers, browser-extension permissions, React Native toolchains, opaque binaries, destructive servicing scripts, and donor-specific authority paths are not bundled or executed.

## Validation boundary

The Linux build verifies source contracts, evidence graph consistency, packaging, hashes, XAML resources, and Python tests. PowerShell AST/Pester and Windows-native DDC/CI, CIM, COM, AppX, UI Automation, DISM, Explorer, WPF, UAC, Registry, service, reboot, and recovery behavior remain pending on a disposable Windows host.
