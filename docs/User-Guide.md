# WinCare User Guide

WinCare is a native Windows diagnostics, maintenance, security, and recovery workspace. It is currently a **release candidate**. Start with read-only evidence, review every proposed change, and treat any outcome marked **final state unknown** as a reason to verify the affected Windows resource before retrying.

> [!NOTE]
> The images in [Screenshots.md](Screenshots.md) are build-specific historical captures. Current source has changed since the v2.5.0-rc5 images, so use their provenance notes rather than assuming every label/layout is current.

## 1. System requirements

| Requirement | Supported configuration |
|---|---|
| Windows | Windows 10 2004 / build 19041 or newer, or Windows 11 |
| Architecture | x64 or ARM64 |
| Privilege | Standard user for many read-only checks; Administrator for commands whose catalog policy requires elevation |
| Distribution | MSIX, self-contained executable, or portable ZIP as published by the release workflow |

## 2. Installation and first launch

### MSIX

Download the package matching your architecture from GitHub Releases. Development-certificate releases also publish a matching `.cer` and `install_msix.py` helper. The helper verifies the package signature, verifies that the supplied certificate matches the package signer, imports that certificate only to `LocalMachine\TrustedPeople`, verifies again, and then installs.

```powershell
python install_msix.py `
  --package .\WinCare-v<version>-x64.msix `
  --certificate .\WinCare-v<version>-x64.cer
```

Run the helper from an elevated terminal only when the certificate is not already trusted. Do not manually trust an unrelated certificate.

### Standalone and portable builds

The self-contained `.exe` runs directly. The portable ZIP should be extracted before launching `WinCare.App.exe`. These formats are useful for technician/recovery workflows but do not replace production signed-install testing.

On launch, WinCare restores the last usable window placement by default. Disable **Settings → Window continuity** if you prefer the default startup size each time.

## 3. Safety model: preview is not permission

A listed tool is not automatically authorized to change Windows.

```text
Choose tool
  → validate typed inputs
  → run read-only preview
  → inspect evidence and affected resources
  → dispatcher issues a short-lived, single-use review receipt
  → explicitly approve
  → apply
  → record outcome in Activity
```

For mutating commands, the receipt is issued by the command dispatcher only after a successful preview. It is bound to the exact command, canonical parameter values, correlation ID, and issuance time. Editing parameters, waiting past expiry, changing correlation, fabricating a receipt, or replaying an already-used receipt requires a new preview.

If a mutating handler faults after execution starts and WinCare cannot prove the final machine state, the result is **not** “nothing changed.” WinCare reports that the final state is unknown and tells you to verify the affected resource before retrying.

WinCare only advertises Undo when a concrete executable compensator exists. It does not manufacture a generic rollback promise for commands that cannot safely reverse themselves.

## 4. Navigation

The main navigation is task-oriented:

- **Home** — recent evidence and activity at a glance.
- **Checkup** — read-only evidence collection.
- **System Care** — maintenance-oriented command groups.
- **Security** — security/privacy command groups.
- **Repair & Recovery** — repair and recovery command groups.
- **All Tools** — complete 259-command catalog with search, filters, typed parameters, preview/apply, favorites, and recent commands.
- **System Doctor** — local rule-based symptom triage and evidence-guided recommendations.
- **Plugin Store** — installed-plugin lifecycle plus browse-only remote catalog metadata unless a production catalog trust root is configured.
- **Activity** — running work, items needing attention, completed operations, and aggregated daily reports.
- **Settings** — theme, window continuity, local-data access, persistence warnings, and the safety-policy summary.
- **Help** — in-app explanations for evidence, approval, plugins, Guard status, troubleshooting, and accessibility.

Use **Ctrl+K** for the global search palette. In All Tools, **Ctrl+F** focuses tool search.

## 5. Home

Home is an evidence/status dashboard, not a synthetic health-score generator. Before a check it clearly says evidence has not been collected. After operations/checks, it derives recent activity and status summaries from the shared Activity journal.

Plugin widgets appear only when active. A widget that fails to load reports a visible error state instead of silently disappearing.

## 6. System Checkup

Checkup currently has two sections: **Quick check** and **Results**.

The quick check runs four read-only evidence probes covering Windows/hardware basics, storage, security, and Windows Update search readiness. Measurement-sensitive probes run sequentially so one probe's CPU/disk activity does not contaminate the next probe's evidence.

The percentage shown after a run is **evidence-collection completion**, not a blanket machine-health score. Review each category result before deciding whether to open a related repair or maintenance tool.

Below the shared 920-DIP compact breakpoint, both the hero summary and evidence table become stacked layouts rather than squeezing desktop columns.

## 7. System Care, Security, and Repair & Recovery

These pages organize the native command catalog into task-focused groups. They do not bypass the catalog/dispatcher safety policy.

- Read-only rows can be opened/run without mutation approval.
- Mutating work is routed to the same preview → receipt → explicit approval path used by All Tools.
- Compact windows use stacked records at the shared 920-DIP boundary.
- Repair & Recovery shows recovery-oriented tools and Activity guidance; it does not imply that every historical operation has a generic Undo command.

## 8. All Tools

All Tools exposes all 259 native command definitions while keeping common input safer than hand-written JSON.

### Search and filters

Search by command/title/area, filter by area/risk/read-only characteristics, and use Favorites/Recent for repeated work. At compact widths the filters stack instead of forcing a desktop-width toolbar.

### Typed parameters

Commands with declared parameters render native editors derived from `CommandParameterCatalog`:

- text fields;
- number fields with declared bounds;
- boolean toggles;
- supported-value choices;
- string-list inputs;
- date/time inputs;
- structured JSON values when a nested object/array is genuinely required.

Required fields are marked. Type/range/choice problems are blocked before dispatch, and the executor validates again at the Windows boundary.

### Advanced JSON

**Advanced parameter editing** exposes the raw JSON object for power users and automation-compatible edge cases. It remains size-bounded and is not a safety bypass. Switching parameter values invalidates any previously reviewed mutation plan.

### Preview and Apply

For a read-only command, **Run tool** executes the admitted read operation. For a mutating command, the first action is **Review changes**. Only a successful dispatcher preview enables explicit approval; the next action becomes **Apply changes** using that issued receipt.

## 9. System Doctor

The shipped Doctor is a **local rule-based diagnostic assistant**, not a general-purpose AI agent and not a cloud model.

1. Describe a symptom in plain language.
2. The local rule engine maps it to supported diagnostic domains.
3. WinCare runs read-only evidence commands.
4. The Doctor explains the evidence and proposes an applicable next action when supported.
5. A proposed mutation must run the real dispatcher preview.
6. You review that preview and explicitly approve before Apply.

The Doctor cannot mint its own approval receipt. User-facing errors are sanitized rather than inserting raw exception text into the conversation.

## 10. Plugin Store

Plugins execute full-trust **in process** with the current user's WinCare privileges. Declared capabilities are informed-consent metadata; they are not a sandbox.

### Current release behavior

This repository does **not** ship an approved production plugin-catalog public key or a live official signed catalog. The current composition root therefore keeps remote installation **browse-only/disabled**. This is intentional fail-closed behavior.

The Plugin Store shows the current catalog trust state explicitly. If network/catalog access falls back to the bundled offline examples, those entries are labeled as samples and are not installable.

### Requirements before remote installation can ever be enabled

A future configured catalog must pass all of these boundaries:

- detached signature over the exact catalog bytes verifies against a WinCare-pinned key;
- installation performs a fresh security catalog fetch instead of stale-cache fallback;
- the reviewed card is re-resolved from that fresh catalog;
- package ID, SHA-256, trusted publisher identity/signature, capability consent, and revocation state pass;
- discovery re-verifies the installed manifest against an external admission record.

### Installed-plugin lifecycle

Details and Install are separate actions. Uninstall requires confirmation. If an enabled plugin must be disabled for removal but removal fails, WinCare attempts to restore its prior enabled state and reports whether restoration succeeded.

## 11. Activity and reports

Activity is the shared operation ledger.

- **Running** — operations currently executing.
- **Needs attention** — operations whose outcome requires verification/follow-up.
- **Completed** — terminal completed/failed/cancelled operations.
- **Reports** — daily aggregate summaries rather than a duplicate of Completed.

Activity refreshes from journal change events rather than a polling timer. Journal persistence occurs outside the in-memory state lock. If durable storage fails, the UI displays a persistence warning so in-memory success is not confused with a durable audit trail.

## 12. Settings

Current settings are intentionally limited to behavior the app actually persists:

- **App theme** — System, Light, or Dark.
- **Window continuity** — remember/restore the last usable size, position, and maximized state; turning it off clears the stored placement.
- **Local data** — open the WinCare data directory.
- **Persistence warning** — visible if preferences cannot be loaded or written durably.
- **Safety policy** — an explanation of preview receipts, outcomes, and truthful recovery claims.

Settings does not expose decorative toggles for product capabilities that are not actually wired.

## 13. Encrypted profiles

Commands that use encrypted profile storage create versioned AES-256-GCM envelopes. New writes derive keys with PBKDF2-HMAC-SHA256 using 600,000 iterations. Legacy 100,000-iteration envelopes remain decryptable for compatibility and migrate naturally when rewritten. Authentication detects a wrong passphrase or modified ciphertext.

This storage capability is separate from a promise that Settings provides automatic cloud synchronization; the current Settings surface does not make that claim.

## 14. WinCare Guard

`wincare-guard` is currently an **experimental local daemon boundary**.

The Windows named pipe is local and protected by an explicit DACL. The daemon can provide its current local health/IPC primitives, but this release does **not** claim a finished Windows Service Control Manager lifecycle or complete native/app toast-notification integration. Those remain production-promotion requirements.

## 15. Keyboard and accessibility

WinCare favors native WinUI controls and explicit automation names/IDs on important controls. The app provides Light, Dark, and Windows High Contrast resources, visible keyboard focus, wrapped text styles, and compact layouts for core task pages.

Important release checks still require a live Windows environment:

- Narrator reading/focus order;
- full keyboard traversal;
- 100%, 150%, 200%, and 225% Windows text scaling;
- display scaling and narrow-window resizing;
- High Contrast rendering;
- dialog focus and error announcements.

If a layout clips at a particular display/text scale, include the screen name and exact scale in the bug report.

## 16. Troubleshooting

### A command is Blocked or Not available

Read the returned code/message. WinCare fails closed for missing prerequisites, invalid input, unverified trust state, insufficient authority, unsupported state, or commands that are not admitted on the current machine.

### A mutation says final state unknown

Do not retry immediately. Open Activity, identify the command and affected resource, verify that Windows resource directly, and only then decide whether a new preview is appropriate.

### Plugin Store shows browse-only/offline status

That is expected in the current repository build because no production catalog trust root is shipped. Installed plugins remain manageable. Do not treat an offline sample entry as an installable package.

### Preferences or Activity are not durable

The app shows an InfoBar when it cannot persist those records. Open **Settings → Local data** and verify that the current account can access/write the WinCare data directory. In-memory state may be lost at exit while the warning remains active.

### The app terminates after an unexpected UI fault

Unknown UI faults are no longer universally swallowed. WinCare writes diagnostic context and exits rather than continuing with potentially invalid process state. Review the local diagnostics and restart.

### Report a security issue

Use GitHub Private Vulnerability Reporting rather than a public issue. See [SECURITY.md](../SECURITY.md).
