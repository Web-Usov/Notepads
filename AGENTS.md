# Developing the Web-Usov Notepads fork

Target the fork's `master` branch. Notepads is a C# UWP editor for Windows 10 and 11. Use the .NET 8 console project to exercise the portable extension code. The Windows app builds with Visual Studio and MSBuild.

## Where to make changes

| Change | Location |
| --- | --- |
| Bundled plugin | [plugins](plugins/). Each bundled plugin directory contains a uniquely named `*.notepads-extension.json` manifest and any compiled implementation specific to that plugin. JSON Tools and Log Lens are working examples. |
| Optional example | [plugins/examples](plugins/examples/). Put each example in its own subdirectory so it can be installed separately. |
| Extension host | [src/Notepads/SyntaxExtensions](src/Notepads/SyntaxExtensions/) handles manifest parsing, registration, package installation and dispatch to supported handlers. [TextEditor](src/Notepads/Controls/TextEditor/) applies edits and displays result panels. |
| App strings | [src/Notepads/Strings](src/Notepads/Strings/). Match resource keys and formatting placeholders in `en-US` and `ru-RU`. Use the manifest translation fields for labels owned by a plugin. |
| Tests | [tests/SyntaxExtensions](tests/SyntaxExtensions/) holds the portable suite, sample logs and the packaged Windows UI smoke test. |

## Changing a plugin

Start with the [authoring guide](docs/extensions.md) and the [extension workflow](.agents/skills/create-notepads-extension/SKILL.md). Retain the ID when upgrading a plugin so its enabled setting and update source continue to refer to it. Preserve support for API v1 grammar manifests when changing the host. Register a new handler in the host and include any new compiled plugin source in both the [app project](src/Notepads/Notepads.csproj) and the [portable project](tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj).

MSBuild links bundled manifests from `plugins/*/*.notepads-extension.json` into the flat `Assets/Extensions/` directory inside the app package. User-installed manifests live in `LocalState/Extensions/`. Keep these package and profile paths stable when reorganizing sources.

Editor offsets count UTF-16 code units with a single carriage return per line break. Bind edits and panel navigation to the source document and revision. Apply a text transformation as one Undo step. Use existing tests for selection offsets, stale results and source navigation when changing these paths.

## Verify the change

Run the portable suite from the repository root for changes to plugins or their host.

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
```

For desktop changes, open [src/Notepads.sln](src/Notepads.sln) on Windows, choose **Notepads / Debug / x64 / Local Machine** and press **F5**. For UI text, exercise the affected flow in English and Russian after restarting the app. For package changes, check the signed bundle in the **Windows preview installer** PR workflow and its installed-editor smoke test. Report which checks ran in the PR. [Build and installer instructions](CONTRIBUTING.md)
