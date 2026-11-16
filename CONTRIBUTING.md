# Contributing to the Notepads fork

Base your branch on `master` and open a [pull request against the fork](https://github.com/Web-Usov/Notepads/compare). Describe what changed and how you tested it. To report a problem with a proposed change, comment on its PR with the app version, Windows build and steps to reproduce it. Include a small sample when it helps reproduce the problem.

## Run the Windows app from source

1. Install Visual Studio 2022 on Windows with the C# Universal Windows Platform development tools and Windows SDK **10.0.22621.0**. The app project also references the Microsoft Store Services SDK and the Visual C++ UWP runtime libraries.
2. Open [src/Notepads.sln](src/Notepads.sln), restore its NuGet packages and set **Notepads** as the startup project.
3. Select **Debug**, **x64** and **Local Machine**. Press **F5** to deploy and run **Notepads-Dev**. Enable Windows Developer Mode if Visual Studio prompts for it.

## Test extensions

Run the portable extension suite from the repository root with the .NET 8 SDK. It covers manifest loading, grammar selection, tokenization, editing commands, log filters and package installation.

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
```

For a new grammar, use the same command with `-- <manifest-file> <sample-file>` to inspect the tokens. Then open the sample file in the editor, edit a token and undo the change. The [extension authoring guide](docs/extensions.md) includes a working manifest and expected token output. The [repository instructions](AGENTS.md) describe the plugin layout and localization checks.

## Try a pull request build

Open the **Windows preview installer** run for the pull request. When its `installer` job succeeds, download **Notepads-Dev-Windows-x64** under **Artifacts**, extract the ZIP and follow the [Windows installation instructions](README.md#install-on-windows). Sign in to GitHub to download a preview artifact. It is available for 30 days. Published installers are available on the [fork’s Releases page](https://github.com/Web-Usov/Notepads/releases).

## Publish an installer

1. Update `Identity.Version` in [Package.appxmanifest](src/Notepads/Package.appxmanifest) to a higher four-part version. The preview and stable packages share an identity, so choose a version above the last public preview for the next stable release.
2. For stable releases, provision the permanent signing certificate in the **notepads-release** GitHub environment as described in [release signing](tools/installer/README.md). Once the changes land on `master`, push a branch from that commit named `release/v<version>`. For a public preview, push `native-preview/v<version>` from the intended commit. Preview builds use a separate temporary signer. Regular PRs and workflow dispatches produce CI artifacts.
3. The workflow checks the signed bundle, generates the descriptor from its package metadata, and exercises installation and upgrade in Windows. It uploads the descriptor, bundle, frameworks, public certificate, offline ZIP and checksums to a draft before publishing. The public verification job downloads the released files and installs the descriptor on a clean runner.
4. Stable descriptors use `releases/latest/download/Notepads.appinstaller` to discover new releases. Preview descriptors are pinned to their prerelease. Upload MSIX and framework assets with the correct MIME types and keep published version URLs available.
