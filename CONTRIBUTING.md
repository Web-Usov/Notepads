# Contributing to Plainix

Base your branch on `master` and open a [pull request against the fork](https://github.com/Web-Usov/Plainix/compare). Describe what changed and how you tested it. To report a problem with a proposed change, comment on its PR with the app version, Windows build and steps to reproduce it. Include a small sample when it helps reproduce the problem.

## Run the Windows app from source

1. Install Visual Studio 2022 on Windows with the C# Universal Windows Platform development tools and Windows SDK **10.0.22621.0**. The app project also references the Microsoft Store Services SDK and the Visual C++ UWP runtime libraries.
2. Open [src/Notepads.sln](src/Notepads.sln), restore its NuGet packages and set **Notepads** as the startup project.
3. Select **Debug**, **x64** and **Local Machine**. Press **F5** to deploy and run **Plainix Dev**. Enable Windows Developer Mode if Visual Studio prompts for it.

## Test extensions

Run the portable extension suite from the repository root with the .NET 8 SDK. It covers manifest loading, grammar selection, tokenization, editing commands, log filters and package installation.

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
```

For a new grammar, use the same command with `-- <manifest-file> <sample-file>` to inspect the tokens. Then open the sample file in the editor, edit a token and undo the change. The [extension authoring guide](docs/extensions.md) includes a working manifest and expected token output. The [repository instructions](AGENTS.md) describe the plugin layout and localization checks.

## Try a pull request build

Open the **Windows preview installer** run for the pull request. When its `installer` job succeeds, download the **Plainix-Windows-x64-ci-<run>-a<attempt>** artifact under **Artifacts**, extract the ZIP, open PowerShell in the directory containing `Add-AppDevPackage.ps1` and run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Add-AppDevPackage.ps1`. Open **Plainix** from Start. Sign in to GitHub to download a preview artifact. It is available for 30 days. Published installers are available on the [fork’s Releases page](https://github.com/Web-Usov/Plainix/releases).

### CI preview versioning and replacing an installed preview

For pull requests, manually dispatched runs and nightly checks, the Windows preview workflow assigns a **build-only** MSIX version from its run number and rerun attempt (four revision slots per run). For example, run 66 attempt 1 produces `1.7.0.265` when the committed manifest is `1.7.0.3`. The downloaded artifacts are clearly labeled **`Plainix-Windows-x64-ci-66-a1.zip`** and **`native-installer-ci-66-a1.zip`**, while the actual Windows package has a valid numeric four-part version (`Plainix_1.7.0.265_x64.msixbundle`). MSIX does **not** allow a textual suffix in `Identity.Version`.

This keeps the package family unchanged but makes successive CI artifacts upgradeable instead of failing with **"same identity, different content"** on unchanged version numbers. The checked-in manifest is never edited or committed by this mechanism. Pushed `release/v*` and `native-preview/v*` branches retain their explicitly versioned packages, artifact names and publication guards.

**Each CI artifact still has its own temporary self-signed certificate.** Import the `.cer` from **the same artifact** into Trusted People before installing or updating. A new version alone cannot make an untrusted signing certificate trusted. Do not share production signing secrets with pull request builds.

If an older equal-version package is already installed, first save documents and close Plainix, then back up its per-user package data (especially `LocalState` for extensions/session backups and `Settings`). Removing the existing `Notepads-Dev` MSIX may delete these folders. The commands below intentionally stop on a failed backup before removal:

```powershell
$pkg = Get-AppxPackage -Name 'Notepads-Dev'
if (!$pkg) { throw 'Notepads-Dev is not installed.' }
$profile = Join-Path $env:LOCALAPPDATA "Packages\$($pkg.PackageFamilyName)"
$backup = Join-Path $env:USERPROFILE ("Plainix-backup-" + (Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($folder in @('LocalState', 'Settings')) {
    $source = Join-Path $profile $folder
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $backup -Recurse -Force -ErrorAction Stop
    }
}
Remove-AppxPackage -Package $pkg.PackageFullName
```

Install the new matching `.msixbundle`/certificate and check settings and sessions before deleting the backup. Future stable versions must be **higher than every preview version users may have installed**; bump the appropriate leading version component when necessary.

## Automated UI tests

Windows UI tests run against the **installed** MSIX on an interactive Windows desktop using NUnit and FlaUI UIA3, in addition to the existing editor smoke and App Installer smoke. The [UI testing guide](docs/ui-testing.md) covers CI gates, local reproduction, TRX and screenshot artifacts, cleanup, and test authoring. The release workflow requires these UI jobs to pass before publication.

## Code signing and release security

The current signing instructions below cover **legacy self-signed releases only**. Plainix has prepared a [Code signing policy](docs/code-signing-policy.md) and [SignPath Foundation application checklist](docs/signpath-foundation-application.md) under [Issue #9](https://github.com/Web-Usov/Plainix/issues/9). The SignPath release workflow may replace these steps **only after approval**, verification of the actual certificate subject and a manual signing approval policy. PR artifacts must never gain production SignPath credentials.

Changing the MSIX publisher changes the package family. Maintain the [publisher-migration plan](docs/installer-publisher-migration.md) and preserve user profiles; an update across those identities cannot be tested as a normal in-place update.

## Publish an installer

1. Update `Identity.Version` in [Package.appxmanifest](src/Notepads/Package.appxmanifest) to a higher four-part version. The preview and stable packages share an identity, so choose a version above the last public preview for the next stable release.
2. For stable releases, provision the permanent signing certificate in the **notepads-release** GitHub environment as described in [release signing](tools/installer/README.md). Once the changes land on `master`, push a branch from that commit named `release/v<version>`. For a public preview, push `native-preview/v<version>` from the intended commit. Preview builds use a separate temporary signer. Regular PRs and workflow dispatches produce CI artifacts.
3. The workflow checks the signed bundle, generates the descriptor from its package metadata, and exercises installation and upgrade in Windows. It uploads the descriptor, bundle, frameworks, public certificate, offline ZIP and checksums to a draft before publishing. The public verification job downloads the released files and installs the descriptor on a clean runner.
4. Stable descriptors use `releases/latest/download/Plainix.appinstaller` to discover new releases. Continue publishing the compatible `Notepads.appinstaller` descriptor for earlier clients. Preview descriptors are pinned to their prerelease. Upload MSIX and framework assets with the correct MIME types and keep published version URLs available.
