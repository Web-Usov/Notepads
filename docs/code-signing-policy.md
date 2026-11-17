# Code signing policy

## Current status

Plainix has **not yet been accepted by SignPath Foundation**, and no Plainix release is currently signed with a SignPath Foundation certificate. As of 18 November 2026, the latest stable release is [v1.7.0.3](https://github.com/Web-Usov/Plainix/releases/tag/v1.7.0.3), signed using a self-signed development/release certificate. Its certificate **is not trusted by a clean Windows installation**. Do not describe these downloads as verified by SignPath or as suitable for installation without an explicit trust decision.

**Proposed policy after acceptance:** Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org/).

Until acceptance, the existing certificate-import instructions continue to apply to previously published packages. We will not silently fall back to self-signing when publishing a release advertised as trusted.

## Source, builds and scope

- Source repository and issue tracker: [Web-Usov/Plainix](https://github.com/Web-Usov/Plainix).
- Upstream project: [0x7c13/Notepads](https://github.com/0x7c13/Notepads); Plainix is a visible GitHub fork with modifications released under the [MIT license](../LICENSE.txt).
- Public releases: [GitHub Releases](https://github.com/Web-Usov/Plainix/releases).
- Release CI: [Windows preview installer](../.github/workflows/preview-installer.yml). Before signing is enabled, it builds the x64 MSIX bundle and supplies the framework dependencies and App Installer descriptors.
- After acceptance, the *final* `.msixbundle` built from the reviewed release source will be submitted to SignPath through its GitHub trusted-build connector and signed with the Foundation certificate. The SignPath signing request must be linked to the GitHub-hosted build artifact and its origin.
- Third-party Microsoft framework dependency packages are redistributed as supplied by their upstream vendors. They are **not** submitted to SignPath for signature under the Plainix project certificate.
- The `.appinstaller` file is an HTTPS installation/update descriptor and is not itself an Authenticode-signed executable. The signed MSIX bundle is what Windows must verify.

The package `Publisher` must exactly match the subject of the certificate issued for the approved project; do not guess it or use `CN=SignPath Foundation` without checking the actual certificate. Package name and publisher together define the Windows **package family**. Changing publisher breaks in-place updates from the self-signed previews; see [publisher migration](installer-publisher-migration.md).

## Roles, review and release authorization

- **Maintainer, code author and release owner:** [Web-Usov](https://github.com/Web-Usov). The maintainer controls the repository and coordinates release branches.
- **Code reviewers:** repository collaborators authorized by the maintainer. Pull requests from outside trusted committers require an explicit review by a maintainer before merge. Any change to packaging, signing, dependencies or workflows requires security-sensitive review.
- **SignPath release approver:** the maintainer-designated approver(s), configured in the SignPath signing policy. **Every public signing request requires manual approval**, separately from ordinary PR checks.
- All team members with repository or SignPath access must have multi-factor authentication enabled. Release permissions and credentials must be restricted to the release environment, not exposed to PR jobs or untrusted forks.

Once SignPath access is granted, configure GitHub as a trusted build system in SignPath, install/authorize the SignPath GitHub App, use the dedicated `release-signing` policy with manual approval, and verify each returned signature on a clean Windows runner **without importing a certificate**. Do not publish if origin, identity, timestamp, trust-chain, dependency, or installer tests fail. Preserve source commit, signing request ID, and release artifact SHA-256 values in the release record.

## User privacy and uninstall

See [Plainix privacy policy](../PRIVACY.md). The editor opens local documents and can contact third-party endpoints only for user-initiated operations described there; the current legacy Store Services diagnostics integration is under review before submitting the SignPath application. The editor can be removed using Windows **Settings → Apps → Installed apps → Plainix → Uninstall**. Uninstalling may delete the installed package's application data; back up the data before migrating between package families.

## Verifying a trusted release (after acceptance)

On a clean Windows machine, download `Plainix.appinstaller` from the official GitHub release. The descriptor must point to the matching published bundle and framework files. Check the bundle with Windows **SignTool** (`signtool verify /pa /v Plainix-x64.msixbundle`) or with `Get-AuthenticodeSignature`, and compare the signer **certificate subject** with the expected publisher in the release notes. A valid certificate chain and timestamp are required. SmartScreen reputation is **separate** from valid package signing: report reputation warnings even if the signature verifies successfully.

Reference: [SignPath Foundation terms](https://signpath.org/terms) · [GitHub connector documentation](https://docs.signpath.io/trusted-build-systems/github) · [Microsoft MSIX code signing](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options).
