# Release signing for Notepads

The fork uses the package identity `Notepads-Dev` with publisher `CN=Web-Usov Notepads Preview`. Keep that subject when provisioning a release signer so Windows can update existing installations in place. A PC that trusted an earlier preview certificate needs to trust the production certificate once.

## Provision the signing environment

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
   $repo = 'Web-Usov/Notepads'
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
