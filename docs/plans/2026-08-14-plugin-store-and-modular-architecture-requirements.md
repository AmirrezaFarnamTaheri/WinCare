# WinCare Plugin Store & Modular Extension Architecture — Product & Technical Design Specification

- **Date:** 2026-08-14
- **Status:** Requirements & Architectural Specification (`artifact_readiness: requirements-only`)
- **Contract Version:** `ce-unified-plan/v1`
- **Source:** `/ce-brainstorm`

---

## 1. Executive Summary & Intent

WinCare is evolving from a monolithic command host into a **modular, extensible platform** featuring a **Plugin Store & Extension Engine**.

### Core Goals
1. **Slim Core Host:** Reduce the main `WinCare` app to a lean host runner providing navigation, telemetry, Rust native FFI safety execution, and the plugin engine.
2. **Hybrid Plugin System:** Support both lightweight declarative JSON packages (`wincare-plugin.json` + scripts) for tools/catalogs and compiled C# assembly plugins (`.dll` via `AssemblyLoadContext`) for advanced UI widgets.
3. **Plugin Store (Marketplace):** Provide a dedicated WinUI 3 page for discovering, installing, enabling, disabling, and updating built-in and community plugins.
4. **Extensible UI Integration:** Allow plugins to inject custom tool cards into catalog views, dynamic widget cards onto the Home Dashboard, or dedicated application tabs.

---

## 2. Architecture & Layer Separation

```
┌────────────────────────────────────────────────────────────────────────┐
│                          WinCare.App (WinUI 3)                         │
│  ┌────────────────────┐ ┌────────────────────┐ ┌────────────────────┐  │
│  │   Plugin Store     │ │  Home Dashboard    │ │   All Tools /      │  │
│  │   Management Page  │ │  Widget Container  │ │   Category Views   │  │
│  └─────────┬──────────┘ └─────────┬──────────┘ └─────────┬──────────┘  │
└────────────┼──────────────────────┼──────────────────────┼─────────────┘
             │                      │                      │
┌────────────▼──────────────────────▼──────────────────────▼─────────────┐
│                       WinCare.Application (Core)                        │
│  ┌──────────────────────┐  ┌─────────────────────┐  ┌───────────────┐ │
│  │   IPluginRegistry    │  │   IPluginHost       │  │  PluginLoader │ │
│  │   (Local & Remote)   │  │   (Lifecycle/State) │  │  (JSON & DLL) │ │
│  └──────────┬───────────┘  └──────────┬──────────┘  └───────┬───────┘ │
└─────────────┼─────────────────────────┼─────────────────────┼─────────┘
              │                         │                     │
┌─────────────▼─────────────────────────▼─────────────────────▼─────────┐
│                     WinCare.Domain & Rust Native Engine                │
│  ┌─────────────────────────┐         ┌──────────────────────────────┐ │
│  │  CommandDispatcher      │         │  wincare-core (Rust 2024 FFI) │ │
│  │  (Fail-Closed Safety)   │<───────>│  (Safety & OS System API)   │ │
│  └─────────────────────────┘         └──────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 3. Hybrid Plugin Contract

### 3.1 Declarative Manifest (`wincare-plugin.json`)
For tool packs, scripts (PowerShell/CMD), and catalog additions:
```json
{
  "id": "com.wincare.plugins.disk-cleaner-pro",
  "name": "Advanced Disk Cleaner Pro",
  "version": "1.0.0",
  "author": "WinCare Community",
  "description": "Extended system disk cleanup scripts and telemetry widgets.",
  "category": "System Care",
  "entryType": "Manifest",
  "tools": [
    {
      "id": "cleaner.deep_temp_purge",
      "title": "Deep Temp Directory Purge",
      "summary": "Safely purges temporary cache files across system drives.",
      "area": "System care",
      "section": "Storage",
      "risk": "Low",
      "readOnly": false,
      "executorType": "PowerShell",
      "scriptPath": "scripts/purge_temp.ps1"
    }
  ]
}
```

### 3.2 Assembly Plugin Interface (`IWinCarePlugin`)
For compiled C# plugins requiring custom UI widgets or native integrations:
```csharp
namespace WinCare.Application.Plugins;

public interface IWinCarePlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    Task InitializeAsync(IPluginHost host, CancellationToken ct);
    IReadOnlyList<CommandDefinition> GetCommands();
    IReadOnlyList<IPluginWidget> GetWidgets();
}
```

---

## 4. Multi-Source Plugin Registry & Store

Plugins are discovered and loaded from 3 prioritized sources:
1. **Built-in Plugins:** Bundled category tool packs (`System Care`, `Security`, `Repair & Recovery`, `Checkup`) that ship with WinCare as standard plugins.
2. **Local User Directory (`%LocalAppData%/WinCare/Plugins`):** Local user-installed `.wincare-plugin` ZIP archives or extracted folders.
3. **Remote Online Catalog:** GitHub-hosted `catalog.json` index allowing 1-click download, installation, and updates directly inside the **Plugin Store** view.

---

## 5. Phased Implementation Plan

### Phase 1: Plugin Engine & Abstractions (`WinCare.Application`)
- Define `PluginManifest`, `IWinCarePlugin`, `IPluginRegistry`, `IPluginHost`.
- Implement `JsonPluginLoader` and `AssemblyPluginLoader` (`AssemblyLoadContext`).
- Implement `PluginRegistryService` managing local directory scanning (`%LocalAppData%/WinCare/Plugins`) and state persistence (`plugins.json`).

### Phase 2: Core App Decomposition & Built-in Tool Packs
- Extract current command catalog categories (`System Care`, `Security`, `Repair & Recovery`, `Checkup`) into modular built-in plugin manifests.
- Update `ToolCatalogService` to aggregate commands dynamically from `IPluginRegistry`.

### Phase 3: Plugin Store UI (`WinCare.App`)
- Create `PluginStorePage.xaml` and `PluginStorePageViewModel.cs`.
- Support browsing, searching, installing, enabling/disabling, and uninstalling plugins.
- Add remote catalog fetching from GitHub release/index repository.

### Phase 4: Extensible Dashboard Widgets
- Create dynamic `WidgetContainer` control on `HomePage.xaml`.
- Allow active plugins to register custom telemetry/status widgets on the Home Dashboard.

---

## 6. Success & Quality Criteria

- **Parity:** 100% of existing 259 commands remain functional through the plugin engine.
- **Isolation:** Disabling a plugin instantly removes its commands and widgets without app restart.
- **Safety:** All plugin command executions continue to route through `CommandDispatcher` fail-closed safety checks.
- **Testing:** 100% test coverage for plugin loading, manifest validation, and registry updates.
