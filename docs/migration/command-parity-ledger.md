# Native command parity ledger

The frozen oracle contains all stable command IDs from the historical implementation. The native catalog has exact 259-ID parity. Catalog preservation is not behavioral parity.

## Summary

| State | Count |
|---|---:|
| Total stable IDs | 259 |
| Unique native catalog IDs | 259 |
| Cataloged | 259 |
| Contract verified or later | 134 |
| Implemented or later | 134 |
| Behavior verified | 0 |
| Native implementation blockers | 125 |
| Production behavior-verification blockers | 259 |

Implemented native commands:

- `catalog`
- `presets`
- `system`
- `storage`
- `network`
- `experience-privacy-profiles`
- `cleaner-disk-pressure`
- `cleanup-targets`
- `startup`
- `security`
- `applications`
- `health`
- `pagefile`
- `tcp-global`
- `pagefile-recommendation`
- `security-controls`
- `network-measure`
- `process-modules`
- `security-maintenance`
- `network-experiments`
- `etw-sessions`
- `wua-history`
- `injection-surfaces`
- `remote-thread-events`
- `wua-search`
- `preset`
- `wdac-policies`
- `wdac-events`
- `internals-processes`
- `wua-hide`
- `internals-memory`
- `internals-cpu`
- `credential-providers`
- `appcontainer`
- `pagefile-set`
- `security-control-reduce`
- `security-control-restore`
- `network-experiment`
- `etw-capture`
- `injection-surface-quarantine`
- `wua-unhide`
- `wua-download`
- `wua-install`
- `wua-uninstall`
- `wdac-deploy`
- `desktop-controls`
- `wifi-profiles`
- `shell-extensions`
- `desktop-shortcuts`
- `boot`
- `bcd-export`
- `unattend-analyze`
- `provisioning-plan`
- `knowledge`
- `reports`
- `automation-profiles`
- `run-automation`
- `cancel-operation`
- `widgets`
- `widget-catalog`
- `widget-export`
- `bluetooth`
- `bluetooth-events`
- `maintenance`
- `maintenance-create`
- `maintenance-transition`
- `maintenance-metrics`
- `maintenance-export`
- `context-menu`
- `context-menu-set`
- `customization-hosts`
- `rainmeter-skins`
- `windhawk-mods`
- `explorerpatcher`
- `modern-context-validate`
- `playbooks`
- `playbook`
- `group-policy`
- `group-policy-import`
- `sysmon`
- `sysmon-configure`
- `sysmon-uninstall`
- `offline-images`
- `offline-drivers`
- `offline-packages`
- `offline-features`
- `offline-driver-add`
- `offline-driver-remove`
- `offline-package-add`
- `offline-feature-set`
- `power-sessions`
- `power-start`
- `power-stop`
- `windows`
- `monitors`
- `window-zones`
- `window-zone-set`
- `window-topmost`
- `window-activate`
- `input-release`
- `downloads`
- `downloads-due`
- `download-create`
- `download-start`
- `download-start-due`
- `download-suspend`
- `download-resume`
- `download-reconcile`
- `download-cancel`
- `download-remove`
- `telemetry-snapshot`
- `telemetry-history`
- `telemetry-capture`
- `telemetry-export`
- `launcher-search`
- `launcher-open`
- `calculator`
- `steam-games`
- `steam-users`
- `steam-cloud-files`
- `steam-backup`
- `steam-restore`
- `game-integrity`
- `offline-reduction-profiles`
- `offline-reduction-assess`
- `offline-reduction-apply`
- `workspace-layouts`
- `workspace-layout-save`
- `workspace-layout-apply`
- `workspace-layout-remove`
- `color-capture`
- `color-palette`
- `color-add`
- `color-remove`

None of these commands are marked `BehaviorVerified` because Windows oracle comparison has not run in the current environment.

## State definitions

| State | Required evidence |
|---|---|
| `Cataloged` | stable ID, title, summary, area, section, risk, privilege, restart, and keywords |
| `ContractVerified` | typed request/result contract and frozen parity fixture reviewed |
| `Implemented` | native handler exists and fails closed outside its admitted scope |
| `BehaviorVerified` | native and historical behavior compared on supported Windows environments with matching safety and result semantics |

## Promotion rule

The production finalizer requires exactly 259 `BehaviorVerified` records. An implemented handler without Windows behavior evidence is still a production blocker.

## Oracle provenance

Frozen fixtures live under `migration/oracle/`:

- `legacy-command-ids.json`
- `remediation-rules.json`
- `presets.json`
- `provenance.json`

The provenance file records the historical source commit and SHA-256 hash of each authoritative input. The native source archive uses these non-executable fixtures. The executable historical source is kept only in the separate oracle archive.
