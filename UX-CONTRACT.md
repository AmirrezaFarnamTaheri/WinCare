# WinCare interface behavior

The task-first workspace keeps the native safety/execution model while presenting capabilities as user jobs instead of implementation mechanics.

| Capability | Owner | Contract |
|---|---|---|
| Navigation | ShellPage, PageService, PageNavigation | All 12 routes remain reachable. Visible navigation is grouped around Home, Checkup, Care, Power tools, and Activity; Extensions/Troubleshoot/Settings/Help are secondary and About remains reachable without permanent rail space. |
| Appearance | AppPreferences, MainWindow, ThemeResources | System/Light/Dark remain persisted choices. High contrast follows Windows. Cached brushes refresh. |
| Forms | AllToolsPage, ToolExecutionViewModel | Typed fields lead; JSON stays Advanced. Preserve validation and approval invalidation. |
| Selection / dropdowns | Native WinUI | Windows owns keyboard, popup, focus and accessible selection. |
| Search | GlobalSearchBox, AllTools view model | Ctrl+K searches routes, native tools, installed extensions, and help topics. Ctrl+F focuses Power tools search. Area and Section are structured filters, not fuzzy category queries. |
| Scrolling | ScrollViewer / ListView | Native scrollbars and keyboard behavior; compact tables use records. |
| Feedback | InfoBars, command view models, Activity | Busy/disabled state during actions; visible failures; receipts describe actual outcomes. |
| Confirmation | Dispatcher / execution view model | Existing risk-tier policy: direct safe actions, moderate confirmation, destructive preview receipt plus approval. |
| Plugins | Trust and admission services | Remote installation disabled without approved trust root; widget failures stay visible. |

Home's checkup button opens Checkup, never a repair. Recommended tasks deep-link to the matching structured care section. Checkup is read-only and hands findings to care surfaces. Power tools keeps advanced execution details behind the inspector. Troubleshoot preserves catalog read-only/access metadata but never infers Undo; the canonical execution path owns review, approval, execution receipts and recovery claims.

The shell changes presentation without changing route identity. `LayoutVisibility.CompactBreakpointDip` remains the shared 920-DIP page boundary while measured components may use narrower fit breakpoints. Home's hero/evidence composition stacks below 820 DIP. Unknown, empty, pending, failed, partial and successful states remain distinct. A screenshot never substitutes for a live interface.
