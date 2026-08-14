# WinCare Plugin Store Component & Catalog Upgrade — Implementation Plan

- **Date:** 2026-08-14
- **Status:** Completed (`artifact_readiness: completed`)
- **Contract Version:** `ce-unified-plan/v1`
- **Origin Specification:** `docs/superpowers/specs/2026-08-14-plugin-store-components-design.md`

---

## 1. Overview & Architecture Summary

This implementation plan details the technical implementation for the **Plugin Store Component & Catalog Upgrade**, introducing 1-Click remote installation from GitHub catalog indexes, category filtering, search, and WinUI 3 capability inspection dialogs.

---

## 2. Implementation Units & Task Breakdown

### Unit 1: Remote Catalog Service & Local Caching
**Goal:** Implement `RemoteCatalogService.cs` in `WinCare.Infrastructure` to fetch and cache GitHub online plugin index.

- **Files touched / created:**
  - `src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs`
  - `src/WinCare.Application/Plugins/RemotePluginCatalogModels.cs`
  - `tests/WinCare.Infrastructure.Tests/RemoteCatalogServiceTests.cs`

- **Acceptance Criteria:**
  - [x] `RemoteCatalogService` fetches online `catalog.json` index with local 24h caching in `%LocalAppData%/WinCare/catalog_cache.json` and offline fallbacks.
  - [x] Returns structured remote plugin catalog items with download URLs and author metadata.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Infrastructure.Tests --filter "RemoteCatalogServiceTests"`

---

### Unit 2: 1-Click Plugin Package Installer Service
**Goal:** Implement `PluginInstallerService.cs` downloading and extracting `.wincare-plugin` ZIP packages into user plugin directory.

- **Files touched / created:**
  - `src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`
  - `tests/WinCare.Infrastructure.Tests/PluginInstallerServiceTests.cs`

- **Acceptance Criteria:**
  - [x] Downloads `.wincare-plugin` archive over HTTPS.
  - [x] Enforces manifest validation, package ID binding, SHA-256 validation, and path canonicalization (rejecting `../` traversal).
  - [x] Extracts into `%LocalAppData%/WinCare/Plugins/<id>/` and triggers `IPluginRegistry.DiscoverAndInitializeAsync()`.

- **Verification Commands:**
  - `dotnet test tests/WinCare.Infrastructure.Tests --filter "PluginInstallerServiceTests"`

---

### Unit 3: WinUI 3 Plugin Detail & Permission Dialog
**Goal:** Build `PluginDetailDialog.xaml` displaying author links, tool lists, release notes, and security capabilities.

- **Files touched / created:**
  - `src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml`
  - `src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml.cs`

- **Acceptance Criteria:**
  - [x] Renders WinUI 3 `ContentDialog` with Cyber-Teal design tokens.
  - [x] Displays capability audit box (`"filesystem.read"`, `"process.spawn"`) with in-process execution security disclaimer.

- **Verification Commands:**
  - `python tools/verify_visual_tokens.py`

---

### Unit 4: Multi-Tab Store Page Upgrade (Search & Categories)
**Goal:** Upgrade `PluginStorePage.xaml` with category buttons (`All`, `System Care`, `Security`, `Utilities`, `Installed`), search bar, and 1-Click Install/Enable/Disable/Uninstall buttons.

- **Files touched / created:**
  - `src/WinCare.App/Views/Pages/PluginStorePage.xaml`
  - `src/WinCare.App/Views/Pages/PluginStorePage.xaml.cs`
  - `src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs`
  - `src/WinCare.App/ViewModels/Pages/PluginCardViewModel.cs`

- **Acceptance Criteria:**
  - [x] Category switching between Online Catalog and Installed Plugins renders instantly without visual shift.
  - [x] Search query filters plugins by title, category, and author.
  - [x] Action buttons (Install, Enable, Disable, Uninstall) update UI and tool registration state smoothly.

- **Verification Commands:**
  - `python tools/verify_native_foundation.py`
