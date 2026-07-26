# GUI

WinCare provides a WPF interface over the same exported contracts used by terminal and headless modes. The GUI does not own policy, mutation state, or approval authority.

## Workspaces

- Dashboard and quick actions
- Applications, cleanup, storage, startup, and health
- Security, updates, network, internals, and advanced capabilities
- Maintenance, automation, recovery, reports, and knowledge
- Workspace, display, browser, notes, customization, and remote-support inspection
- Assurance: source manifests, exports, test inventory, native runtime, capability status, and packaged build receipt

Every mutation displays the generated plan, risk, elevation requirement, reversibility, and recovery limitations before execution. Cancellation and errors remain visible and are not converted to success.

The GUI must remain constructible in read-only mode without creating persistent directories, repairing state, or emitting disk logs.
