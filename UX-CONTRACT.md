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

## Task-first flow refinement

- First run presents Home immediately; the tour is a voluntary Help action.
- Troubleshoot offers one-click common issue checks or a short free-text description. Its suggestions never bypass Power tools admission, and no fabricated assistant conversation appears on startup.
- Power tools defaults to available commands, but explicit category and command links expose unavailable entries with clear status. Reset clears all filters and restores the available-only view.
- Compact Power tools stacks search and taxonomy filters while keeping quick filters and reset accessible.

## Downloads

- Home and primary navigation link to Downloads. The URL field is the only
  required input: WinCare selects the default Downloads folder and file name.
- Add creates a queued job, but does not start transfer. Start/resume/pause/cancel
  are explicit actions on that job. Remove record never deletes a completed file.
- The list shows file name, source host (never URL credentials/query), persisted
  state, transferred size, and real percentage only if the server declared total
  length. Unknown length is indeterminate, not 0%-done.
- Transfers belong to the process lifetime, not the page lifetime. Navigation
  must not silently abort work. Shutdown requests pause, subject to its budget.
- Native commands and the same Activity journal remain authoritative; no
  unreviewed duplicate downloader, browser download hijacking, or fake queue.

### Direct download action

Pressing Download creates a persisted queue record and starts it immediately.
The separate Start action remains for interrupted or explicitly queued jobs.
A stalled remote server fails after 90 seconds without arriving bytes and
keeps the verified partial file for retry; neither a truncated response nor
a resumed response with mismatched Content-Range is published as complete.
