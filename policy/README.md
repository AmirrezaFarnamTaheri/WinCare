# WinCare policy deployment

WinCare merges policy in this order, with later sources taking precedence:

1. built-in safe defaults;
2. `%LOCALAPPDATA%\WinCare\policy.json`;
3. the file named by `WINCARE_ORG_POLICY`;
4. `%ProgramData%\WinCare\policy.json`.

Each file is a complete schema-v1 policy object. Unknown fields, invalid signer thumbprints, malformed maintenance windows, invalid limits and unsupported schemas fail closed. Machine/organization policy should be deployed read-only to ordinary users through the organization’s existing configuration-management system.

The example is intentionally restrictive. Review every allowed/denied action and catalog rule against the organization’s change, recovery and maintenance-window policy before deployment.

## Expert-operation gates

The example policy keeps page-file disabling, security reduction, network experiments, experimental TCP settings, and interception-surface remediation disabled. Process instrumentation remains read-only/diagnostic unless a modifying ETW capture is explicitly approved. Organizations enabling an expert gate should also set strict duration limits, retain exact preview requirements, preserve recovery action types in the allowlist, and test automatic restoration in a disposable Windows environment before deployment.

`MaximumSecurityMaintenanceMinutes` is bounded to 5..1440 minutes. `MaximumNetworkExperimentMinutes` is bounded to 1..25 minutes and is evaluated against the declared target/sample/timeout worst case before mutation.

## Productivity and support gates

`AllowAwayModePowerSession`, `AllowIndefinitePowerSession`, and `MaximumPowerSessionMinutes` govern execution-state holds independently. `AllowRemoteSupportTermination` remains disabled in the example policy; enabling it permits only exact inventoried allowlisted process termination after normal preview and authorization. `MaximumRemoteConsentMinutes` bounds consent records but does not create remote-session authority. Workspace modes, command history, notes, palettes, and browser inventory cannot weaken policy or action risk floors.

## WinCare 4.3 operations gates

`AllowInsecureDownloads` remains false by default. `MaximumDownloadJobs` and `MaximumDownloadBytes` bound persistent queue size and the effective accepted BITS object size. Configuration can only lower the byte ceiling.

`AllowOfflineImageReduction` and `AllowGameStateRestore` remain false by default because they cross irreversible or conflict-prone boundaries. Enabling them does not permit wildcard package identities, `ResetBase`, a live Steam-client restore, hidden archive members, or cheat/trainer execution. Downloads, telemetry, launcher, and layout mutations still require exact preview receipts and normal typed-action authorization.

## WinCare 4.8 Experience Studio gates

Ordinary Experience Studio discovery is read-only. RDP/OpenSSH, location, power, and privacy changes require their existing typed actions to be allowed. `rytunex-maximum` and `privacy-sexy-maximum` additionally require Critical and Legacy Unsafe authorization plus any narrower Defender, SmartScreen, UAC, Update, firewall, hosts, AppX, service, task, cleanup, or privacy gate reached by the composed plan.
