# WinCare Plugin Store Component & Catalog Upgrade — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Implementation-Ready (`artifact_readiness: implementation-ready`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/superpowers/specs/2026-08-14-plugin-store-components-design.md`

---

## 1. Overview & Architecture Summary

This implementation plan details the step-by-step technical units for building the **Plugin Store Component & Catalog Upgrade**, introducing 1-Click remote installation from GitHub catalog indexes, category filtering, search, and WinUI 3 permission inspection dialogs.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: Remote Catalog Service & Local Caching
**Goal:** Implement `RemoteCatalogService.cs` in `WinCare.Infrastructure` to fetch and cache GitHub online plugin index.

- **Files touched / created:**
  - `src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs`
  - `src/WinCare.Infrastructure/Plugins/RemotePluginCatalogModels.cs`
  - `tests/WinCare.Infrastructure.Tests/RemoteCatalogServiceTests.cs`

- **Acceptance Criteria:**
  - [ ] `RemoteCatalogService` fetches online `catalog.json` index with local 24h caching in `%LocalAppData%/WinCare/catalog_cache.json`.
  - [ ] Returns structured remote plugin catalog items with download URLs and author metadata.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Infrastructure.Tests --filter "RemoteCatalogServiceTests"`

---

### Unit 2: 1-Click Plugin Package Installer Service
**Goal:** Implement `PluginInstallerService.cs` downloading and extracting `.wincare-plugin` ZIP packages into user plugin directory.

- **Files touched / created:**
  - `src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`
  - `tests/WinCare.Infrastructure.Tests/PluginInstallerServiceTests.cs`

- **Acceptance Criteria:**
  - [ ] Downloads `.wincare-plugin` archive over HTTPS.
  - [ ] Enforces manifest validation and path canonicalization (rejecting `../` traversal).
  - [ ] Extracts into `%LocalAppData%/WinCare/Plugins/<id>/` and triggers `IPluginRegistry.DiscoverAndInitializeAsync()`.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Infrastructure.Tests --filter "PluginInstallerServiceTests"`

---

### Unit 3: WinUI 3 Plugin Detail & Permission Dialog
**Goal:** Build `PluginDetailDialog.xaml` displaying author links, tool lists, release notes, and security permissions.

- **Files touched / created:**
  - `src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml`
  - `src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml.cs`

- **Acceptance Criteria:**
  - [ ] Renders WinUI 3 `ContentDialog` with Cyber-Teal design tokens.
  - [ ] Displays permission audit box (`"DiskRead"`, `"ProcessInfo"`, `"RegistryWrite"`).

- **Verification Commands:**
  - `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64`

---

### Unit 4: Multi-Tab Store Page Upgrade (Search & Categories)
**Goal:** Upgrade `PluginStorePage.xaml` with `SelectorBar` tabs (`Featured & Online`, `Installed`, `Categories`), search bar, and 1-Click Install buttons.

- **Files touched / created:**
  - `src/WinCare.App/Views/Pages/PluginStorePage.xaml`
  - `src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs`

- **Acceptance Criteria:**
  - [ ] Tab switching between Online Catalog and Installed Plugins renders instantly without visual shift.
  - [ ] Search query filters plugins by title, category, and author.
  - [ ] 1-Click Install button downloads, installs, and updates UI state smoothly.

- **Verification Commands:**
  - `dotnet build src/WinCare.App/WinCare.App.csproj -c Debug -p:Platform=x64`
  - `python tools/verify_native_foundation.py`
