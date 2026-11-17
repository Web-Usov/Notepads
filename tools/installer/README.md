# Release signing for Plainix

> **Current state (18 November 2026):** Public releases use a self-signed certificate. This section documents the **legacy** signer only, and does **not** make the installer trusted by clean Windows installations. We are preparing a [SignPath Foundation application](../../docs/signpath-foundation-application.md) and [Code signing policy](../../docs/code-signing-policy.md). Do not create or upload additional PFX secrets once SignPath is activated. SignPath holds its private key; its manually approved release workflow must replace legacy self-signing.

The fork uses the package identity `Notepads-Dev` with publisher `CN=Web-Usov Notepads Preview`. That subject is used **only** for currently published self-signed builds and enables in-family updates while it remains unchanged. The planned SignPath certificate has a different publisher, so existing installations will require the [migration procedure](../../docs/installer-publisher-migration.md), not an in-place update. A PC that trusted an earlier self-signed preview certificate might need to trust a different self-signed certificate for *legacy* packages; this import step will not be necessary for a properly trusted SignPath release.

Stable releases publish both `Plainix.appinstaller` and `Notepads.appinstaller`. Both install the same signed bundle and frameworks. Each descriptor keeps its own stable update URL for Windows installations already subscribed to that address.

## Provision the legacy self-signed environment (do not use for SignPath)

After the first release run has created the `notepads-release` environment, sign in to GitHub CLI on your Windows PC with an account that manages the repository. From the repository root, run:

```powershell
.\tools\installer\Initialize-ReleaseSigning.ps1
```

Enter a password when prompted. The script creates an encrypted PFX at `%LOCALAPPDATA%\Plainix\Signing\Plainix.pfx` and configures the two environment secrets. Keep a backup of that file and its password. If you already have a signing key for this publisher, pass its full path with `-PfxPath`. The script only fills missing secrets. For a new environment it sets the deployment branch pattern to `release/v*`. Manage access to those branches in the repository settings.

Open the failed release run in **Actions** and choose **Re-run failed jobs** to resume publication. For manual provisioning, follow the steps below.

1. In the GitHub repository settings, create the Actions environment `notepads-release`. Under **Deployment branches and tags**, allow releases from the branch pattern `release/v*`. Restrict who can create or update those branches and the release workflow.
2. On a trusted Windows machine with GitHub CLI authenticated to the fork, create the signing certificate once. If you already hold a suitable PFX for this publisher, use its path and password for step 3. Keep the password and an encrypted backup of the PFX in storage accessible to release maintainers. The key created on earlier CI runners was ephemeral.

   ```powershell
   $pfxPath = Read-Host 'Full path for the new PFX backup outside the repository'
   if (Test-Path -LiteralPath $pfxPath) { throw 'Choose a new backup path.' }
   $password = Read-Host 'Release PFX password' -AsSecureString
   $cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=Web-Usov Notepads Preview' `
       -CertStoreLocation Cert:\CurrentUser\My -KeyUsage DigitalSignature -KeyExportPolicy Exportable `
       -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(5) `
       -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
   Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $password `
       -ChainOption EndEntityCertOnly -CryptoAlgorithmOption AES256_SHA256 | Out-Null
   ```

3. Upload the two **environment** secrets by piping their values into the CLI. PowerShell 7 preserves UTF-8 for non-ASCII passwords. In Windows PowerShell 5.1, set the output encoding as shown.

   ```powershell
   # When using an existing PFX, set $pfxPath and read $password with Read-Host -AsSecureString.
   $OutputEncoding = [Text.UTF8Encoding]::new($false)
   $repo = 'Web-Usov/Plainix'
   [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfxPath)) |
       gh secret set NOTEPADS_RELEASE_PFX_BASE64 --repo $repo --env notepads-release
   if ($LASTEXITCODE) { throw 'Could not upload the release PFX.' }
   [Net.NetworkCredential]::new('', $password).Password |
       gh secret set NOTEPADS_RELEASE_PFX_PASSWORD --repo $repo --env notepads-release
   if ($LASTEXITCODE) { throw 'Could not upload the PFX password.' }
   ```

The signing step imports the key into the runner's personal certificate store, signs the bundle and removes the key. Distribute the public `.cer` with the release. To obtain its SHA-256 fingerprint independently on the provisioning machine, export the leaf from the same PFX.

```powershell
$publicPath = [IO.Path]::ChangeExtension($pfxPath, '.cer')
$publicCert = (Get-PfxData -FilePath $pfxPath -Password $password).EndEntityCertificates
Export-Certificate -Cert $publicCert[0] -FilePath $publicPath | Out-Null
Get-FileHash -LiteralPath $publicPath -Algorithm SHA256
$password.Dispose()
```

Keep the same release key across versions. When rotating a self-signed certificate, distribute its public certificate to users before publishing the first bundle signed with the replacement. [Microsoft's package signing guide](https://learn.microsoft.com/windows/msix/package/create-certificate-package-signing) · [GitHub CLI environment secrets](https://cli.github.com/manual/gh_secret_set)
