# Release signing and installed acceptance

A GitHub Actions **pull request build** uses a disposable development signing identity and may
successfully create MSIX packages and pass portable startup smoke. That evidence is not production
release acceptance: each architecture's ephemeral certificate differs, and users cannot upgrade
or install a trusted public release from those artifacts without explicit trust.

To publish through `Native WinUI`, configure a persistent, access-controlled signing identity:

- GitHub secret `WINCARE_RELEASE_PFX_BASE64`: Base64 encoding of the code-signing PFX, including
  its private key. Configure only for trusted release workflows; do not commit a PFX or print it.
- GitHub secret `WINCARE_RELEASE_PFX_PASSWORD`: its PFX import password.
- GitHub repository variable `WINCARE_RELEASE_CERT_THUMBPRINT`: the SHA-1 certificate thumbprint
  of that persistent identity. The subject must match the package's current identity
  `CN=WinCare Development` until an intentional publisher-identity migration is undertaken.

Both x64 and ARM64 build jobs import this same signer when the run will publish a release, and
sign the MSIX and portable executable with it. Publication fails if credentials are missing,
the pinned thumbprint does not match, or the two architectures' exported certificates differ.
Each release also requires valid artifact digests, documented release notes and the release
behavioral gate. A certificate can be stable but still lack trust from users' machines; proper
public certificate-chain trust is a separate prerequisite.

**Installed acceptance checklist (requires real disposable Windows hosts):** test a clean
installation, launch, exit, upgrade from an older signed version, preservation of user data,
repair/reinstall, uninstall, reboot and recovery, both x64 and ARM64, UI automation at 100/150/200%
scaling, Narrator keyboard navigation, and fault-injected registry rollback. Save evidence with
build SHA, signer thumbprint and OS version. CI portable smoke alone cannot prove these steps.


## Opt-in disposable installed MSIX acceptance

The Native WinUI workflow exposes a manual **run_installed_acceptance** input.
Select this only for a workflow_dispatch run. It uses disposable Windows x64
and ARM64 runners, downloads the architecture-specific CI package and matching
development signer, verifies the signer, installs the MSIX, inspects the installed
publisher/version and uninstalls the package in a cleanup block. The script
refuses execution unless WINCARE_DISPOSABLE_TEST_HOST=1 and the host has no
pre-existing WinCare registration. An optional PreviousPackagePath supports a
same-signer upgrade scenario when manually invoked with genuine old/new MSIXs.

This test is not automatic in pull requests and has not established upgrade from
a real published release, reboot persistence, GUI installer usability, or store
certification. Store the run logs and package hashes with release acceptance.


## Final audit pull-request installed acceptance

The high-impact audit branch **PR #59** runs the x64 and ARM64 disposable
Windows MSIX install/uninstall tests automatically on pull-request builds,
in addition to managed tests and portable startup smoke. This is a
temporary, explicitly scoped pre-merge acceptance gate, not an installation
on contributor/user devices. It does not exercise public-signer upgrade,
reboot recovery, interactive WinUI accessibility, or store publication.
