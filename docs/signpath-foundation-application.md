# SignPath Foundation application — Plainix

**Status:** application package prepared; **not submitted / not approved** as of 18 November 2026. Do not record an approval or certificate subject without official confirmation.

Use [SignPath Foundation's application page](https://signpath.org/apply.html). The maintainer must submit the application using an address they control and agree to the Foundation's conditions. Do not share API tokens, GitHub secrets, PFX files, or other credentials in the application.

## Project facts

| Field | Information |
| --- | --- |
| Project | Plainix |
| Repository | https://github.com/Web-Usov/Plainix |
| Maintainer | https://github.com/Web-Usov |
| Upstream | https://github.com/0x7c13/Notepads (GitHub fork) |
| License | MIT — https://github.com/Web-Usov/Plainix/blob/master/LICENSE.txt |
| Download / release | https://github.com/Web-Usov/Plainix/releases |
| Current published release | v1.7.0.3, Windows x64 (2026-11-17), currently self-signed |
| Project description | Lightweight, extensible Windows text editor with tabs, syntax highlighting, Markdown/diff tools, Russian localization and JSON/Log Lens extensions |
| Runtime | Windows 10/11 x64; C# UWP / XAML |
| Requested signing target | Final `Plainix-x64.msixbundle` built by GitHub Actions from this repository |
| Build workflow | `.github/workflows/preview-installer.yml` (to be split/gated for production SignPath signing) |
| Code signing policy | https://github.com/Web-Usov/Plainix/blob/master/docs/code-signing-policy.md (after PR merge) |
| Privacy policy | https://github.com/Web-Usov/Plainix/blob/master/PRIVACY.md |
| Uninstall | Windows Settings → Apps → Installed apps → Plainix → Uninstall |

### Suggested submission text

> Plainix is an actively developed MIT-licensed fork of Notepads, hosted publicly at https://github.com/Web-Usov/Plainix with the upstream fork relationship intact. Plainix extends the Windows C#/UWP text editor with plugin support, JSON and log analysis tools, localization, UX improvements, and a native MSIX/App Installer distribution path. The project has already shipped public Windows x64 releases, most recently v1.7.0.3. We request free SignPath Foundation code signing for our own final MSIX bundle, built from reviewed source by GitHub-hosted Actions and published through GitHub Releases. We will publish a code signing policy and privacy disclosure, configure manual approval for every release signing request, and perform Windows signature, origin, installer, and update verification before publishing. The current release is self-signed; we will not claim SignPath verification until approved.

## Eligibility and security review before submission

- [x] Public GitHub fork relationship and open-source MIT project license.
- [x] Publicly released Windows installer, documented features and source-controlled build workflow.
- [ ] Confirm the original Notepads project publishes signed releases and that the fork's release branches meet SignPath's *modified upstream* conditions. A visible fork alone is not sufficient; SignPath decides eligibility.
- [ ] Audit bundled components and redistributable licenses, including Microsoft Store Services SDK, against the Foundation's restrictions on proprietary code and upstream binary signing.
- [ ] Review or disable automatic Microsoft Store Services diagnostics/exception telemetry and confirm the resulting [privacy disclosure](../PRIVACY.md) meets the Foundation's requirements.
- [ ] Confirm all maintainers and signing approvers use MFA; assign and document code-review/approval rights.
- [ ] Ensure the applicable code signing policy is linked from the README and release notes.
- [ ] Submit the application and record its date, ticket or correspondence in [Issue #9](https://github.com/Web-Usov/Plainix/issues/9) **without posting private account data**.

## Setup after approval

1. Record organization ID, project slug, signing-policy slug, and the **exact X.509 certificate subject** from the SignPath console; do not guess the MSIX Publisher value.
2. Configure the GitHub.com trusted build system and install/authorize the SignPath GitHub App on `Web-Usov/Plainix`.
3. Create an artifact configuration that signs only the produced `.msixbundle` (not third-party upstream dependencies) and enforces Plainix product/version metadata where supported.
4. Create the dedicated `release-signing` policy with the Foundation-issued certificate, origin verification and **manual approval required for every request**. Restrict signing to reviewed release refs.
5. Create the release GitHub environment and a scoped `SIGNPATH_API_TOKEN` secret. Store the organization/project/policy configuration in GitHub Actions variables or a protected environment. Never grant release tokens to pull-request builds.
6. Configure the release pipeline to build with the **exact certificate subject** in the package identity, upload the unsigned GitHub-hosted artifact, use `signpath/github-action-submit-signing-request@v3`, verify the signed output and timestamp on a clean Windows runner, and publish only after verification succeeds.
7. Plan and test [migration from the old package family](installer-publisher-migration.md), then test two succeeding SignPath-signed versions through the stable `.appinstaller` feed, ensuring the second updates the first and preserves data.
8. Record published URL, signer DN/thumbprint, SHA-256, SignPath request ID and Windows trust/installation/SmartScreen results in the release notes and issue.

**Do not switch the public feed or silently publish an untrusted release while any of these conditions are missing.**

Official references: [Eligibility/terms](https://signpath.org/terms) · [SignPath application](https://signpath.org/apply.html) · [Trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github) · [MSIX signing](https://docs.signpath.io/artifact-configuration/reference).
