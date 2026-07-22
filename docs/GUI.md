# WinCare V5 graphical console

## Product goal

The GUI makes WinCare's broad capability set discoverable without concealing authority, risk, recovery, or evidence. It is designed for power users, administrators, support engineers, and reviewers who need both speed and auditability.

## Launch and session model

`WinCare-GUI.ps1` is the canonical launcher. It relaunches into an STA PowerShell 7 process using an argument list rather than concatenated command text. A per-user mutex allows one graphical session. Each command runs in a child PowerShell process through `WinCare.ps1 -Command ... -Json`, keeping the UI responsive and preserving one execution authority.

## Main workspaces

- **Overview:** system identity, operating-system build, administrator context, Defender posture, storage, memory, restart state, recommended actions, policy state, and session activity.
- **Action catalog:** all 236 headless commands, searchable and filterable by domain and risk.
- **Profiles:** curated safe and unsafe multi-action workflows.
- **Health & storage:** diagnostics, cleanup, servicing, page file, and storage actions.
- **Security:** Defender, WDAC, hardening, policy, instrumentation, and security controls.
- **Maintenance:** updates, automation, reports, cleanup, and package lifecycle.
- **Network:** network inventory, Wi-Fi, TCP experiments, and related diagnostics.
- **Workspace:** windows, displays, power, personalization, context menus, downloads, notes, and productivity.
- **Evidence:** donor inventory, adoption decisions, surface accountability, and convergence status.
- **Settings:** workspace mode, reduced motion, screen-reader mode, and policy posture.

Domain entries reuse the action workbench with filters. They do not fork execution logic.

## Action lifecycle

1. Select a catalog action.
2. Review title, description, command, risk, provenance, and argument help.
3. Edit bounded JSON arguments.
4. Preview to collect evidence or generate a canonical plan.
5. Review structured output.
6. Apply only when allowed.
7. For Critical actions, enter the exact acknowledgement.
8. Follow the receipt, journal, restart, and recovery details returned by the engine.

## Failure behavior

Invalid JSON, unknown commands, policy denial, missing capability, stale identity, failed condition, and non-zero child-process exit are visible failures. The GUI does not reinterpret a failure as success. Raw output remains visible if JSON parsing fails.

## Accessibility

- Segoe UI Variable/Segoe UI and scalable WPF controls.
- Keyboard search/refresh/dismiss shortcuts.
- Tooltips and automation labels in the XAML.
- Risk text plus color, never color alone.
- Accessible text contrast.
- Reduced-motion, screen-reader, high-contrast, and monochrome settings.
- Minimum dimensions and scrolling for dense content.
- No automatic execution from navigation or search.

## Extension rule

A new GUI action must map to a registered public headless command. If the capability does not exist headlessly, first add a typed contract/provider/command/test/evidence slice, then add presentation metadata. GUI code must not directly mutate Windows state.
