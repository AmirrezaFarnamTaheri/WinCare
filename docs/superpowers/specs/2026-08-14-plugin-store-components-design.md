# WinCare Plugin Store Upgrade — Technical Design Specification

- **Date:** 2026-08-14
- **Status:** Approved Technical Design (`status: approved`)
- **Origin:** `/brainstorming`
- **Approach:** Approach 1 — Integrated Multi-Tab Store Page (Remote Catalog + 1-Click Install + WinUI 3 Detail Dialog)

---

## 1. Overview & Goal

This design expands the **WinCare Plugin Store** into a full-featured, remote-capable extension marketplace. Users will be able to browse remote community plugins from an online catalog index, install or update plugins with a single click, filter extensions by category, and inspect detailed permissions before installation.

---

## 2. Component Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│                        WinCare.App (WinUI 3 UI)                        │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │                    PluginStorePage.xaml                          │  │
│  │  - SelectorBar Tabs (Featured & Online | Installed | Categories) │  │
│  │  - SearchBar & Category Filter Pills                             │  │
│  │  - PluginGrid (WinUI 3 ListView / ItemsRepeater)                 │  │
│  └──────────────────────────────────┬───────────────────────────────┘  │
│                                     │                                  │
│  ┌──────────────────────────────────▼───────────────────────────────┐  │
│  │                     PluginDetailDialog.xaml                      │  │
│  │  - Author, Version, Release Notes, Tool List, & Permissions      │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────┬──────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼──────────────────────────────────┐
│                       WinCare.Infrastructure                           │
│  ┌────────────────────────┐  ┌───────────────────┐  ┌───────────────┐ │
│  │  RemoteCatalogService  │  │ PluginInstaller   │  │ StateRepo     │ │
│  │  (GitHub Index Fetch)  │  │ (Download & ZIP)  │  │ (plugins.json)│ │
│  └────────────────────────┘  └───────────────────┘  └───────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Detailed Component Specifications

### 3.1 Remote Catalog Service (`RemoteCatalogService.cs`)
- **Location:** `src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs`
- **Function:** Fetches online plugin catalog JSON index from GitHub raw repository:
  `https://raw.githubusercontent.com/AmirrezaFarnamTaheri/WinCare/master/catalog.json`
- **Caching:** Caches index locally in `%LocalAppData%/WinCare/catalog_cache.json` for 24 hours to support offline browsing.

### 3.2 One-Click Installer Service (`PluginInstallerService.cs`)
- **Location:** `src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs`
- **Function:**
  1. Downloads `.wincare-plugin` ZIP archive from remote URL.
  2. Validates `wincare-plugin.json` schema and path traversal safety.
  3. Extracts into `%LocalAppData%/WinCare/Plugins/<plugin-id>/`.
  4. Triggers `IPluginRegistry.DiscoverAndInitializeAsync()`.

### 3.3 WinUI 3 Detail & Permission Dialog (`PluginDetailDialog.xaml`)
- **Location:** `src/WinCare.App/Views/Dialogs/PluginDetailDialog.xaml`
- **Content:**
  - Plugin title, author badge, SemVer tag, and rating stars.
  - Full description and markdown release notes.
  - List of contained tools/commands (`Area`, `Section`, `Risk`).
  - Permission Security Audit box (`"DiskRead"`, `"ProcessInfo"`, `"RegistryWrite"`).

### 3.4 Multi-Tab Store View (`PluginStorePage.xaml`)
- **Tabs (`SelectorBar`):**
  - `Featured & Online`: Browse remote catalog with 1-Click Install buttons.
  - `Installed`: Manage local plugins (Enable, Disable, Uninstall, Update).
  - `Categories`: Filter by category (`System Care`, `Security`, `Repair`, `Widgets`).
- **Design Tokens:** Cyber-Teal accents (`#00D2B4` / `#007A99`), Segoe UI Variable typography, card hover elevation.

---

## 4. Success & Quality Criteria

- **Parity:** All 259 frozen commands remain 100% functional.
- **Safety:** Path traversal linter blocks extraction outside `%LocalAppData%/WinCare/Plugins/`.
- **User Experience:** 1-Click install completes in under 2 seconds for standard script packs.
