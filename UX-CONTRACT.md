# WinCare interface behavior

The task-first workspace keeps the native safety/execution model while presenting capabilities as user jobs instead of implementation mechanics.

| Capability | Owner | Contract |
|---|---|---|
| Navigation | ShellPage, PageService, PageNavigation | All 15 named routes remain reachable, including hidden About. Files & storage and Processes join Downloads under the primary tools area. Extensions, Settings, and Help remain in the footer. |
| Appearance | AppPreferences, MainWindow, ThemeResources | System/Light/Dark remain persisted choices. High contrast follows Windows. Cached brushes refresh. |
| Forms | AllToolsPage, ToolExecutionViewModel | Typed fields lead; JSON stays Advanced. Preserve validation and approval invalidation. |
| Selection / dropdowns | Native WinUI | Windows owns keyboard, popup, focus and accessible selection. |
| Search | GlobalSearchBox, AllTools view model | Ctrl+K searches routes, native tools, installed extensions, and help topics. Ctrl+F focuses Power tools search. Area and Section are structured filters, not fuzzy category queries. |
| Scrolling | ScrollViewer / ListView | Native scrollbars and keyboard behavior; compact tables use records. |
| Feedback | InfoBars, command view models, Activity | Busy/disabled state during actions; visible failures; receipts describe actual outcomes. |
| Confirmation | Dispatcher / execution view model | Existing risk-tier policy: direct safe actions, moderate confirmation, destructive preview receipt plus approval. |
| Plugins | Trust and admission services | Remote installation disabled without approved trust root; widget failures stay visible. |

Home's checkup button opens Checkup, never a repair. Common maintenance tasks open their care sections; file browsing and process monitoring open their native pages. Up to four saved Power tools appear as Home shortcuts, with their real availability displayed; selecting one opens the canonical review UI without executing it. The Favorites link opens the actual Favorites tab. Checkup is read-only and hands findings to care surfaces. Power tools keeps advanced execution details behind the inspector. Troubleshoot preserves catalog read-only/access metadata but never infers Undo; the canonical execution path owns review, approval, execution receipts and recovery claims.

The shell changes presentation without changing route identity. `LayoutVisibility.CompactBreakpointDip` remains the shared 920-DIP page boundary while measured components may use narrower fit breakpoints. Home's hero/evidence composition stacks below 820 DIP. Unknown, empty, pending, failed, partial and successful states remain distinct. A screenshot never substitutes for a live interface.

## Task-first flow refinement

- First run presents Home immediately; the tour is a voluntary Help action.
- Troubleshoot offers one-click common issue checks or a short free-text description. Its suggestions never bypass Power tools admission, and no fabricated assistant conversation appears on startup.
- Power tools defaults to available commands, but explicit category and command links expose unavailable entries with clear status. Reset clears all filters and restores the available-only view.
- Compact Power tools stacks search and taxonomy filters while keeping quick filters and reset accessible.

## Downloads

- Downloads can be filtered by All / Active / Finished and searched by filename or source host without exposing signed links.
- Home and primary navigation link to Downloads. The URL field is the only
  required input: WinCare selects the default Downloads folder and file name.
- The Download button creates and starts a transfer. Start/resume/pause/cancel
  are available when applicable. Remove record never deletes a completed file.
- Show folder opens the selected job's destination in the read-only Files & storage browser.
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


## Files & storage

- Read-only local drive and directory navigation. The listing is capped at 600 entries; the UI explicitly states when the displayed set is incomplete.
- The size scan reuses the existing bounded storage report (20,000 entries, 12 levels, hardlink accounting when available), skips reparse points, and identifies the largest directly scanned subfolders. Partial totals are labeled as such.
- Opening folders or copying paths requires no elevated access. Windows Explorer remains available for file operations.

## Processes

- The process list is sampled every five seconds while visible, with pause, filtering and sorting. CPU percentages require two comparable process readings; missing information is not shown as zero.
- Memory means resident working-set bytes, not uniquely owned or total system memory.
- End task is explicitly confirmed; system process names, WinCare itself, stale/reused IDs and unknown process identities are blocked. The action sends a single-process termination request, not a process-tree kill.
- Windows Task Manager remains available for operations requiring elevated rights or additional system context.
