# WinCare Plugin Store & Modular Extension Architecture — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Implementation-Ready (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/plans/2026-08-14-plugin-store-and-modular-architecture-requirements.md`

---

## 1. Overview & Architecture Summary

This plan details the implementation of the **WinCare Plugin Store & Modular Extension Architecture**, transforming WinCare into a modular platform where tool categories (`System Care`, `Security`, `Repair & Recovery`, `Checkup`) are decoupled into pluggable extension packages.

### System Architecture Layers
- **`WinCare.Application.Plugins`**: Interfaces (`IWinCarePlugin`, `IPluginHost`, `IPluginRegistry`), manifests (`PluginManifest`), and loaders (`JsonPluginLoader`, `AssemblyPluginLoader`).
- **`WinCare.Infrastructure.Plugins`**: Local disk scanner (`%LocalAppData%/WinCare/Plugins`), state persistence (`plugins.json`), and remote GitHub index fetcher (`RemoteCatalogService`).
- **`WinCare.App.Views.Pages.PluginStorePage`**: WinUI 3 marketplace UI for browsing, installing, enabling, disabling, and updating plugins.
- **`WinCare.App.Controls.WidgetContainer`**: Dynamic dashboard container rendering extensible plugin widgets on `HomePage.xaml`.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: Core Plugin Interfaces & Declarative Manifest Schema
**Goal:** Define the plugin domain abstractions, manifest JSON schema, and validation logic in `WinCare.Application`.

- **Files touched / created:**
  - `src/WinCare.Application/Plugins/PluginManifest.cs`
  - `src/WinCare.Application/Plugins/IWinCarePlugin.cs`
  - `src/WinCare.Application/Plugins/IPluginHost.cs`
  - `src/WinCare.Application/Plugins/IPluginWidget.cs`
  - `tests/WinCare.Application.Tests/PluginManifestTests.cs`

- **Acceptance Criteria:**
  - [ ] `PluginManifest` correctly deserializes `wincare-plugin.json` schema with `id`, `name`, `version`, `author`, `category`, and `tools`.
  - [ ] `PluginManifestValidator` validates required fields, version formats, and tool definition integrity.
  - [ ] `IWinCarePlugin` interface provides lifecycle hooks `InitializeAsync`, `GetCommands()`, and `GetWidgets()`.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Application.Tests --filter "PluginManifestTests"`

---

### Unit 2: Plugin Loaders (JSON & AssemblyLoadContext)
**Goal:** Build loaders for declarative JSON manifest packages and compiled C# `.dll` plugins with isolated dependency contexts.

- **Files touched / created:**
  - `src/WinCare.Application/Plugins/JsonPluginLoader.cs`
  - `src/WinCare.Application/Plugins/AssemblyPluginLoader.cs`
  - `src/WinCare.Application/Plugins/PluginLoadContext.cs`
  - `tests/WinCare.Application.Tests/PluginLoaderTests.cs`

- **Acceptance Criteria:**
  - [ ] `JsonPluginLoader` parses plugin directories containing `wincare-plugin.json` and returns executable command definitions.
  - [ ] `AssemblyPluginLoader` instantiates `IWinCarePlugin` via isolated `AssemblyLoadContext` without DLL locking or context leak.
  - [ ] Invalid plugin manifests or incompatible assembly versions return clear diagnostic error results without crashing host.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Application.Tests --filter "PluginLoaderTests"`

---

### Unit 3: Multi-Source Plugin Registry & Persistence
**Goal:** Implement `PluginRegistryService` to scan built-in plugins, local user directory (`%LocalAppData%/WinCare/Plugins`), and persist enabled state.

- **Files touched / created:**
  - `src/WinCare.Application/Plugins/IPluginRegistry.cs`
  - `src/WinCare.Application/Plugins/PluginRegistryService.cs`
  - `src/WinCare.Infrastructure/Plugins/PluginStateRepository.cs`
  - `tests/WinCare.Infrastructure.Tests/PluginRegistryServiceTests.cs`

- **Acceptance Criteria:**
  - [ ] `PluginRegistryService` discovers built-in category plugins and user plugins in `%LocalAppData%/WinCare/Plugins`.
  - [ ] Toggling plugin enabled/disabled state updates `plugins.json` and immediately updates active command catalog.
  - [ ] Dynamic catalog query merges core commands with all enabled plugin commands.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Infrastructure.Tests --filter "PluginRegistryServiceTests"`

---

### Unit 4: Modularize Core Categories into Built-in Tool Packs
**Goal:** Extract existing `commands.json` category definitions (`System Care`, `Security`, `Repair & Recovery`, `Checkup`) into modular built-in plugin manifests.

- **Files touched / created:**
  - `src/WinCare.CommandCatalog/Plugins/BuiltInSystemCarePlugin.json`
  - `src/WinCare.CommandCatalog/Plugins/BuiltInSecurityPlugin.json`
  - `src/WinCare.CommandCatalog/Plugins/BuiltInRepairPlugin.json`
  - `src/WinCare.Application/Tools/ToolCatalogService.cs`
  - `tools/verify_native_foundation.py`

- **Acceptance Criteria:**
  - [ ] 259/259 frozen commands remain 100% accessible through `ToolCatalogService` aggregated from plugin registry.
  - [ ] `tools/verify_native_foundation.py` passes clean with zero missing command IDs.

- **Verification Commands:**
  - `python tools/verify_native_foundation.py`
  - `dotnet test tests/WinCare.Application.Tests`

---

### Unit 5: WinUI 3 Plugin Store View & ViewModel
**Goal:** Build the **Plugin Store** marketplace page in `WinCare.App` for browsing, installing, toggling, and updating plugins.

- **Files touched / created:**
  - `src/WinCare.App/Views/Pages/PluginStorePage.xaml`
  - `src/WinCare.App/Views/Pages/PluginStorePage.xaml.cs`
  - `src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs`
  - `src/WinCare.App/ViewModels/Pages/PluginCardViewModel.cs`
  - `src/WinCare.App/Views/ShellPage.xaml`

- **Acceptance Criteria:**
  - [ ] `PluginStorePage` added to Shell left navigation pane with Cyber-Teal design tokens and WCAG AA contrast.
  - [ ] Displays grid of installed/available plugins with status pills (`[ INSTALLED ]`, `[ ENABLED ]`, `[ DISABLED ]`).
  - [ ] Clicking Install / Enable / Disable / Uninstall triggers `PluginRegistryService` and updates UI state smoothly without layout shift.

- **Verification Commands:**
  - `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64`
  - `dotnet test tests/WinCare.App.Tests`

---

### Unit 6: Extensible Dashboard Widget Container
**Goal:** Add dynamic `WidgetContainer` on `HomePage.xaml` allowing enabled plugins to render telemetry and health summary widgets.

- **Files touched / created:**
  - `src/WinCare.App/Controls/WidgetContainer.xaml`
  - `src/WinCare.App/Controls/WidgetContainer.xaml.cs`
  - `src/WinCare.App/Views/Pages/HomePage.xaml`
  - `src/WinCare.App/ViewModels/Pages/HomePageViewModel.cs`

- **Acceptance Criteria:**
  - [ ] `HomePage` dynamically populates active plugin widgets from `IWinCarePlugin.GetWidgets()`.
  - [ ] Disabling a plugin removes its widget cards from the dashboard instantly.

- **Verification Commands:**
  - `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64`

---

## 3. Dependency Graph & Sequencing

```
Unit 1 (Plugin Manifest & Interfaces)
    │
    ▼
Unit 2 (Plugin Loaders - JSON & AssemblyLoadContext)
    │
    ▼
Unit 3 (Multi-Source Plugin Registry & State Repository)
    │
    ├──► Unit 4 (Decompose Core Command Catalog into Built-in Plugins)
    │
    ├──► Unit 5 (WinUI 3 Plugin Store Page & Navigation)
    │
    └──► Unit 6 (Dynamic Dashboard Widget Container)
```

---

## 4. Verification Checkpoints

### Checkpoint 1 (After Units 1-3)
- [ ] Unit tests for `PluginManifest`, `JsonPluginLoader`, `AssemblyPluginLoader`, and `PluginRegistryService` pass 100%.
- [ ] `%LocalAppData%/WinCare/Plugins` directory scanning verified.

### Checkpoint 2 (After Unit 4)
- [ ] `python tools/verify_native_foundation.py` confirms 259/259 command parity.

### Checkpoint 3 (After Units 5-6)
- [ ] WinCare.App builds clean (`x64` & `ARM64`).
- [ ] Plugin Store page functions end-to-end (install, enable, disable, uninstall).
