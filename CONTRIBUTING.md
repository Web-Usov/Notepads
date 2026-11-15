# Contributing to the Notepads fork

Base your branch on `master` and open a [pull request against the fork](https://github.com/Web-Usov/Notepads/compare). Describe what changed and how you tested it. To report a problem with a proposed change, comment on its PR with the app version, Windows build and steps to reproduce it. Include a small sample when it helps reproduce the problem.

## Run the Windows app from source

1. Install Visual Studio 2022 on Windows with the C# Universal Windows Platform development tools and Windows SDK **10.0.22621.0**. The app project also references the Microsoft Store Services SDK and the Visual C++ UWP runtime libraries.
2. Open [src/Notepads.sln](src/Notepads.sln), restore its NuGet packages and set **Notepads** as the startup project.
3. Select **Debug**, **x64** and **Local Machine**. Press **F5** to deploy and run **Notepads-Dev**. Enable Windows Developer Mode if Visual Studio prompts for it.

## Test language extensions

Run the manifest, tokenizer and grammar-selection tests from the repository root using the .NET 8 SDK. These tests run independently of the Windows UI.

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
```

For a new grammar, use the same command with `-- <manifest-file> <sample-file>` to inspect the tokens. Then open the sample file in the editor, edit a token and undo the change. The [extension authoring guide](docs/extensions.md) includes a working manifest and expected token output.

## Try a pull request build

Open the **Windows preview installer** run for the pull request. When its `installer` job succeeds, download **Notepads-Dev-Windows-x64** under **Artifacts**, extract the ZIP and follow the [Windows installation instructions](README.md#install-on-windows). Sign in to GitHub to download a preview artifact. It is available for 30 days. Published installers are available on the [fork’s Releases page](https://github.com/Web-Usov/Notepads/releases).

## Publish a release

1. Update `Identity.Version` in [Package.appxmanifest](src/Notepads/Package.appxmanifest) to a higher four-part version. Update the release notes in the [installer workflow](.github/workflows/preview-installer.yml) for the changes being shipped.
2. After the release changes land on `master`, push a new branch from that commit named `release/v<version>`. For example, manifest version `1.6.1.0` requires branch `release/v1.6.1.0`. Pushing that branch starts publication. Pull requests and manual runs produce preview artifacts.
3. The workflow tests the language extensions, signs and timestamps the x64 bundle, and verifies its contents. It publishes `v<version>` at the pushed commit with the Windows installer ZIP and `SHA256SUMS.txt`. Confirm both assets on the Releases page.
