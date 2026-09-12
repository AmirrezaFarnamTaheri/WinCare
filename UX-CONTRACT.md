# WinCare interface behavior

The Precision Workspace preserves native behavior while changing presentation.

| Capability | Owner | Contract |
|---|---|---|
| Navigation | ShellPage, PageService, PageNavigation | All 12 routes remain reachable. Global search opens All Tools with the query. |
| Appearance | AppPreferences, MainWindow, ThemeResources | System/Light/Dark remain persisted choices. High contrast follows Windows. Cached brushes refresh. |
| Forms | AllToolsPage, ToolExecutionViewModel | Typed fields lead; JSON stays Advanced. Preserve validation and approval invalidation. |
| Selection / dropdowns | Native WinUI | Windows owns keyboard, popup, focus and accessible selection. |
| Search | GlobalSearchBox, AllTools view model | Preserve filtering; Ctrl+K global, Ctrl+F tool search. Native text controls provide clear affordances. |
| Scrolling | ScrollViewer / ListView | Native scrollbars and keyboard behavior; compact tables use records. |
| Feedback | InfoBars, command view models, Activity | Busy/disabled state during actions; visible failures; receipts describe actual outcomes. |
| Confirmation | Dispatcher / execution view model | Existing risk-tier policy: direct safe actions, moderate confirmation, destructive preview receipt plus approval. |
| Plugins | Trust and admission services | Remote installation disabled without approved trust root; widget failures stay visible. |

Home's checkup button opens Checkup, never a repair. Category controls open existing pages. Cleanup/startup/network preserve commands, risk metadata and results. The atlas contains no live controls or measured data.

The shell changes presentation without changing route identity. Pages retain the shared 920-DIP boundary. Home channels stack below 600 DIP. Unknown, empty, pending, failed, partial and successful states remain distinct. A screenshot never substitutes for a live interface.
