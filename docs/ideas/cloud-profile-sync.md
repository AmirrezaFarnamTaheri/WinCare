# Encrypted Cloud Profile & Plugin Sync

## Problem Statement
How might we enable users with multiple Windows PCs to seamlessly synchronize their installed plugins, custom maintenance schedules, and WinCare UI preferences without creating proprietary cloud server accounts?

## Recommended Direction
Build an encrypted cloud profile sync engine inside `WinCare.Infrastructure` that leverages existing user storage backends (GitHub Gists API or OneDrive Personal Vault API).

Before uploading, all configuration files, plugin state JSONs, and custom command settings are encrypted locally using AES-256-GCM with a user-supplied Master Passphrase or Windows Hello biometric key. The host automatically detects changes and syncs updates in the background.

## Key Assumptions to Validate
- [ ] **Encryption Security:** AES-256-GCM encryption verified zero plaintext leakage of paths, user names, or machine IDs.
- [ ] **Conflict Resolution:** Last-Write-Wins with manual merge fallback cleanly handles concurrent multi-PC sync updates.
- [ ] **GitHub Gist / OneDrive API:** Authenticates smoothly via OAuth 2.0 PKCE without requiring server credentials.

## MVP Scope
### What's In
- Local AES-256-GCM encryption module (`CryptoService.cs`).
- GitHub Gist Sync provider (`GitHubGistSyncProvider.cs`).
- UI sync toggle and passphrase modal in `SettingsPage.xaml`.

### What's Out
- Custom hosted WinCare server infrastructure.
- Automatic cloud binary DLL syncing (syncs plugin manifests & settings only).

## Not Doing (and Why)
- **Unencrypted Cloud Storage:** Strict policy against unencrypted cloud backups to protect user system paths.
- **Centralized WinCare User Database:** Avoided to maintain zero user data collection and zero server maintenance cost.

## Open Questions
- Should we support custom WebDAV / S3 bucket endpoints for advanced self-hosted users?
