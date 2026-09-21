# WinCare User Guide

WinCare is a native Windows app for maintenance, diagnostics, and recovery.

> [!NOTE]
> The runtime screenshots in [Screenshots.md](Screenshots.md) are versioned historical captures unless their documented build matches the source being reviewed. Current XAML and runtime validation remain authoritative after UI changes.

## 1. System requirements

| Requirement | Supported configuration |
|---|---|
| Windows | Windows 10 (build 19041 or newer) or Windows 11 |
| Architecture | x64 or ARM64 |
| Privilege | Standard user for diagnostics; Administrator for actions requiring elevation |
| Distribution | MSIX, self-contained executable, or portable ZIP |

## 2. Installation and first launch

### MSIX

Download the package matching your architecture from GitHub Releases. Development-certificate releases also publish a matching `.cer` and `install_msix.py` helper:

```powershell
python install_msix.py `
  --package .\WinCare-v<version>-x64.msix `
  --certificate .\WinCare-v<version>-x64.cer
```

Run the helper from an elevated terminal only when the certificate is not already trusted.

### Standalone and portable builds

The self-contained `.exe` runs directly without installation. The portable ZIP can be extracted to any folder, which is useful for USB technician toolkits.

The first time WinCare opens, a short tour explains Checkup, the care pages, Power tools, and Ctrl+K search. You can reopen the tour later from **Help**.

WinCare can also restore the last usable window size and position. Turn this off under **Settings → Remember this window** if you prefer a fresh window each time.

## 3. Safety tiers

WinCare groups tools into three simple tiers:

```text
Safe: direct execution  │  Moderate: preview + confirmation  │  Destructive: preview + explicit approval
```

- **Safe:** Read-only checks and low-risk actions can run directly and are recorded in Activity.
- **Moderate:** WinCare previews what will change first. Applying the change requires confirmation of that reviewed preview.
- **Destructive:** High-impact changes require a successful preview and explicit approval before they can run.
- **Activity:** Actions and their outcomes are recorded locally so you can see what happened.
- **Honest status:** If an operation stops part-way through, WinCare reports where it stopped instead of pretending nothing changed. Undo is shown only when a real recovery path exists.

## 4. Navigation

The main navigation is task-oriented:

- **Home** — latest checkup results, recent activity, and common care shortcuts.
- **Checkup** — a read-only look at a few important Windows areas.
- **System care** — cleanup, performance, apps, startup, networking, updates, and maintenance.
- **Security** — protection, privacy, and hardening tasks.
- **Repair & recovery** — repair, restore, backup, reset, and recovery tasks.
- **Power tools** — the complete command catalog with search, filters, categories, favorites, Recent, and Care plans.
- **Troubleshoot** — describe a symptom and get local checks plus suggested next steps.
- **Extensions** — manage installed optional features and browse the online list when it is available.
- **Activity** — running work, items needing attention, finished work, and reports.
- **Settings** — theme, window placement, and local-data access.
- **Help** — getting started, the first-run tour, keyboard shortcuts, and common explanations.

Use **Ctrl+K** to search pages and tools. In Power tools, **Ctrl+F** focuses tool search.

## 5. Home

Home gives you a quick view of the latest Checkup results and recent WinCare activity. Before a check, it simply asks you to start with Checkup. Older results are shown as out of date rather than presented as current.

Extension widgets appear only when active. If a widget fails to load, WinCare shows a visible error instead of silently hiding it.

## 6. System Checkup

Checkup lists four areas — **Windows and hardware**, **Storage**, **Security**, and **Updates** — and the same rows become the results when a check runs.

The check looks at Windows/hardware basics, storage, security, and Windows Update without changing the PC. The fast system/storage/security probes run concurrently with bounded concurrency; Windows Update continues alongside them so a slow update search does not hold up the first results.

The summary only describes what these checks found: things look okay, something is worth a look, a check did not finish, or something needs attention. It is not a blanket health score. A finding can open the relevant care section; Checkup itself never applies maintenance.

Below the shared 920-DIP compact breakpoint, the hero and result table stack rather than squeezing desktop columns.

## 7. System care, Security, and Repair & recovery

These pages group the command catalog around common jobs. They all hand actual execution to the same reviewed Power tools path.

- Read-only rows can be opened without mutation approval.
- Changes use the same review and execution path as Power tools.
- Compact windows use stacked rows at the shared 920-DIP boundary.
- Repair & recovery does not pretend every historical operation has a generic Undo button.
- **Portable playbooks** contain catalog command IDs and typed parameters for review. Imported steps open individually and receive a fresh live preview; the file itself carries no execution approval.
- **Restore a reversible remediation** is available only for completed individual registry-value remediations with complete receipts. WinCare verifies the receipt digest and current registry value before each restore step, and records complete, partial, or conflict outcomes.

## 8. Power tools

Power tools exposes all 269 native command definitions without forcing the full catalog into the main navigation for everyday work.

### Search and filters

Search by task, title, or summary; filter by exact Area, Section, impact, and read-only status; and use Favorites or Recent for repeated work. Multi-word searches require every term to match somewhere in the tool metadata, keeping results focused and consistent with Ctrl+K. **Categories** is an Area/Section browser. At compact widths the filters stack instead of forcing a desktop-width toolbar.

### Typed parameters

Commands with declared parameters render native editors derived from `CommandParameterCatalog`:

- text fields;
- number fields with declared bounds;
- boolean toggles;
- supported-value choices;
- string-list inputs;
- date/time values;
- structured JSON values when a nested object or array is genuinely required.

Required fields are marked. Type, range, and choice errors are blocked before dispatch, and the executor validates again at the Windows boundary.

### Advanced JSON

**Advanced parameter editing** exposes the raw JSON object for power users and automation-compatible edge cases. It remains size-bounded and is not a safety bypass. Changing parameter values invalidates a previously reviewed mutation plan.

### Review and apply

Read-only commands run directly. Safe low-risk changes can also run directly under the declared risk policy. Moderate and Destructive changes first resolve the current targets and impact with a preview; applying requires explicit confirmation or approval of that exact reviewed plan.

## 9. Troubleshoot

Troubleshoot uses a local rule-based diagnostic engine. It is not a cloud chat model and it does not execute suggested changes itself.

1. Describe the problem in plain language.
2. WinCare maps it to a supported diagnostic area.
3. It runs relevant read-only checks.
4. It explains what it found and suggests supported next steps.
5. Open a suggested step in **Power tools** to review it.
6. Power tools applies the normal risk-tier flow—direct read/low-risk execution or preview plus confirmation/approval for higher-impact mutations—and records the outcome in Activity.

Troubleshoot cannot create its own execution approval. Errors shown in the conversation are kept readable instead of dumping raw exception text into the UI.

## 10. Extensions

Extensions run in process with the current user's WinCare permissions. Their declared capabilities tell you what they expect to use; they are not a sandbox.

### Current release behavior

This repository does **not** ship an approved production extension-catalog public key or a live official signed catalog. Remote installation therefore remains browse-only/disabled until a trusted production catalog is configured.

Trust and availability details are available under **Online catalog** on the Extensions page. Network or catalog failures are shown when they affect browsing or installation. Bundled/offline sample metadata is not treated as installable production content.

### Requirements before remote installation can be enabled

A configured catalog must pass all of these checks:

- the detached signature over the exact catalog bytes verifies against a WinCare-pinned key;
- installation performs a fresh security catalog fetch instead of relying on stale cache data;
- the reviewed entry is resolved again from that fresh catalog;
- package ID, SHA-256, publisher identity/signature, capability consent, and revocation state pass;
- discovery re-verifies the installed manifest against its admission record.

### Installed-extension lifecycle

Review and Add are separate actions. Uninstall requires confirmation. If an enabled extension must be disabled for removal but removal fails, WinCare attempts to restore its previous enabled state and tells you whether that worked.

## 11. Activity and reports

Activity shows what WinCare is doing and what it did recently.

- **Running** — work currently in progress.
- **Needs attention** — work that needs another look.
- **History** — finished, failed, and cancelled operations with their real state preserved.
- **Reports** — daily summaries rather than a duplicate of History.

Activity updates when the journal changes instead of polling on a timer. If the history file cannot be saved, the app shows a warning; the current in-memory activity may still be visible until WinCare closes.

## 12. Settings

Settings is intentionally small and only exposes behavior the app actually saves:

- **App theme** — System, Light, or Dark.
- **Remember this window** — restore the last usable size, position, and maximized state; turning it off clears the stored placement.
- **Local data** — open the WinCare data directory.
- A warning appears if settings cannot be loaded or saved.

## 13. WinCare Guard

`wincare-guard` is currently an **experimental local daemon boundary**.

The Windows named pipe is local and protected by an explicit DACL. The daemon can provide its current local health/IPC primitives, but this release does **not** claim a finished Windows Service Control Manager lifecycle or complete native/app toast-notification integration. Those remain production-promotion requirements.

## 14. Keyboard and accessibility

WinCare favors native WinUI controls and explicit automation names/IDs on important controls. The app provides Light, Dark, and Windows High Contrast resources, visible keyboard focus, wrapped text styles, and compact layouts for core task pages.

Important release checks still require a live Windows environment:

- Narrator reading/focus order;
- full keyboard traversal;
- 100%, 150%, 200%, and 225% Windows text scaling;
- display scaling and narrow-window resizing;
- High Contrast rendering;
- dialog focus and error announcements.

If a layout clips at a particular display or text scale, include the screen name and exact scale in the bug report.

## 15. Troubleshooting

### A command is Blocked or Not available

Read the returned message. WinCare blocks work when a required prerequisite, permission, trust check, supported state, or command implementation is missing.

### A change says the final state is unknown

Do not retry immediately. Open Activity, identify the task and affected resource, verify that Windows resource directly, and then decide whether a fresh preview is appropriate.

### Extensions is browse-only or offline

That is expected in the current repository build because no production catalog trust root is shipped. Installed extensions remain manageable. Do not treat an offline sample entry as an installable package.

### Settings or Activity is not saving

The app shows a warning when it cannot save those records. Open **Settings → Local data** and verify that the current account can access and write the WinCare data directory. Unsaved in-memory state may be lost when the app closes.

### The app closes after an unexpected UI fault

Unknown UI faults are not silently ignored. WinCare writes diagnostic context and exits rather than continuing with potentially invalid process state. Review the local diagnostics and restart.

### Report a security issue

Use GitHub Private Vulnerability Reporting rather than a public issue. See [SECURITY.md](../SECURITY.md).