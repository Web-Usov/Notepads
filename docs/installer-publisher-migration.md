# Migrating Plainix when the MSIX publisher changes

## Why this is required

Current public Plainix builds (through v1.7.0.3) use package name `Notepads-Dev` and self-signed publisher `CN=Web-Usov Notepads Preview`. A future SignPath Foundation build will use the certificate's **different publisher subject**. Windows computes a package family name from package name and publisher. **A different publisher is a different app identity, not an in-place update**. Existing `.appinstaller` subscriptions cannot silently update to that new identity.

The new installation can coexist with the old one, but it will start with a different app data directory. We must preserve settings and installed extensions before uninstalling the old build. Do not manually change or trust a certificate in Windows to simulate an update; that does not change package identity.

## Before installing the first SignPath-signed release

1. Close all Plainix/Notepads windows and save any unsaved documents.
2. Open Windows PowerShell as your normal Windows user (not as an unrelated administrator).
3. Run the following to create a **non-overwriting backup** of the old app's complete per-user package directory, including `LocalState/Extensions` and the Windows-managed `Settings` directory:

   ```powershell
   $old = Get-AppxPackage -Name 'Notepads-Dev' |
       Where-Object { $_.Publisher -eq 'CN=Web-Usov Notepads Preview' }
   if (@($old).Count -ne 1) { throw 'Expected exactly one older Plainix package for this user.' }
   $oldProfile = Join-Path $env:LOCALAPPDATA "Packages\$($old.PackageFamilyName)"
   if (!(Test-Path -LiteralPath $oldProfile)) { throw 'Old package data folder was not found.' }
   $backup = Join-Path $env:USERPROFILE 'Documents\Plainix-before-SignPath'
   if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $backup" }
   Copy-Item -LiteralPath $oldProfile -Destination $backup -Recurse
   Write-Host "Backed up package data to $backup"
   ```

4. Check the backup actually contains any user-installed extension manifests under `LocalState/Extensions`; preserve the backup outside the app package folder.

## Installing and restoring

1. Download the first **genuinely SignPath-signed** release from the official [Plainix releases](https://github.com/Web-Usov/Plainix/releases) after the release notes explicitly name its signer. Verify the package signature and install via `Plainix.appinstaller` **without** importing a certificate.
2. Close the newly installed editor before modifying its data. Find the new package using `Get-AppxPackage -Name Notepads-Dev` and select the entry whose `Publisher` matches the **exact verified SignPath certificate subject** announced in the release.
3. Restore documents and extension manifests from `Plainix-before-SignPath\LocalState` into the new package's `LocalState`. Avoid overwriting an existing newer extension with an older manifest without checking compatibility. If your installation uses a different package name, use the name declared by its signed manifest.
4. Windows UWP `Settings` files are system-managed. **Do not blindly overwrite the new package's `Settings` directory**: cross-family transfer of that database has not yet been verified. Preserve the backup and reconfigure preferences as needed until a tested settings migration tool is available.
5. Launch the editor and confirm that the plugins, recent-file workflow and settings work. **Only after verification** uninstall the older self-signed app through Windows Settings. Keep the backup until at least one subsequent trusted update has succeeded.

## Release engineering acceptance tests

- [ ] Test backup and restoration of user-installed JSON Tools / Log Lens extension manifests and any editor-specific LocalState data.
- [ ] Add a verified export/import mechanism for UWP `LocalSettings` when the new package cannot inherit it automatically.
- [ ] Install the trusted release on clean Windows x64 with no imported fork certificate.
- [ ] Publish a later **higher-version** build signed by the same Foundation publisher and use the same stable `Plainix.appinstaller` feed to verify an in-place update of the **new** package family, with its LocalState preserved.
- [ ] Confirm that the old package cannot auto-update across publisher change; document a manual upgrade path prominently in release notes.

Reference: [Microsoft MSIX update constraints](https://learn.microsoft.com/windows/msix/app-package-updates).
