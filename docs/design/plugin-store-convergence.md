# Plugin Store convergence

## Scope

The Plugin Store extends the existing WinCare Precision Workspace. It reuses the established page title, semantic brushes, typography, card radii, quiet actions, and cyan primary action; it does not define or replace the product's visual identity.

## Implemented behavior

- **Available-width header compaction.** `PluginStorePage.Page_SizeChanged` passes the page's measured width to `LayoutVisibility.IsCompact`. In compact mode, the search field moves below the title, spans the header, and stretches to the available width; the category filter moves to the following row.
- **Native progress and announcements.** A WinUI `ProgressBar` and loading message bind to `IsLoading`. Catalog trust, errors, and empty results use native `InfoBar` controls with polite or assertive live settings. Retry remains disabled while a refresh is active.
- **Honest offline and retry behavior.** A catalog failure reports that the online catalog is unavailable, exposes **Retry connection**, and continues with entries returned by the installed-plugin registry. The failure path creates an empty remote catalog and does not fabricate offline samples. A normal read may use the saved catalog; the explicit retry requests a fresh remote catalog.
- **Generation-scoped publication.** Each refresh receives a monotonically increasing generation. Cards, trust status, trust verification, and the catalog error are staged locally, then published together only if that generation is still current. Disposal also invalidates in-flight work, so a late response cannot replace newer or departed-page state.
- **Trust remains runtime evidence.** `RemoteCatalogService` marks a catalog verified only when its exact bytes pass detached-signature verification against a configured WinCare-pinned key. Without that key, browsing remains available and remote installation remains disabled. A forced refresh fails closed instead of falling back to an old cache for installation.
- **Explicit runtime dependencies.** `PluginStorePage` constructs its view model with the runtime's plugin registry, catalog service, installer service, plugin host, and initialization delegate. `PluginStorePageViewModel` accepts those dependencies through its constructor and rejects missing required services. `RemoteCatalogService` also accepts its HTTP client, cache location, endpoints, cache duration, and trust key through its constructor.

## Evidence

| Concern | Implementation source |
|---|---|
| Header layout, progress, status, retry, and automation metadata | `src/WinCare.App/Views/Pages/PluginStorePage.xaml` and `.xaml.cs` |
| Refresh generations, offline composition, trust/card publication, and injected interfaces | `src/WinCare.App/ViewModels/Pages/PluginStorePageViewModel.cs` |
| Cache fallback, signature verification, browse-only state, and forced-refresh failure policy | `src/WinCare.Infrastructure/Plugins/RemoteCatalogService.cs` |
| Stale-response, retry, unrelated-error, and disposal regressions | `tests/WinCare.Application.Tests/PluginStoreRefreshTests.cs` |

## Validation boundary

This note records implemented source behavior, not production readiness. Final Plugin Store visual review at wide and compact widths, both themes, and High Contrast remains pending. Keyboard-only, Narrator, focus, live-announcement, and text-scaling checks also remain pending. Build and automated test results cannot replace those runtime visual and accessibility checks.
