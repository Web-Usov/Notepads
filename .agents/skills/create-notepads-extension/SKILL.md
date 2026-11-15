---
name: create-notepads-extension
description: Create, update, test and package extensions for Web-Usov/Notepads. Use for syntax grammars, editor commands, read-only result panels or updates to a .notepads-extension.json manifest.
---

# Create a Notepads extension

Work from the repository root. Read the [extension authoring guide](../../../docs/extensions.md) for the current manifest contract and installation workflow.

1. **Choose the contribution.** Use v1 for a grammar compatible with older Notepads builds. Use v2 for commands, panels and versioned updates. Preserve the ID when extending a published manifest so its enabled setting survives. Give a v2 release a three-part numeric version.
2. **Build the manifest.** Start with the [JSON grammar and commands](../../../plugins/json/json.notepads-extension.json), the [selection wrapper](../../../plugins/examples/markdown-wrap/wrap.notepads-extension.json) or the [Log Lens panel](../../../plugins/log-lens/log-lens.notepads-extension.json). For a grammar, prioritize more specific consuming rules at the same offset and account for RichEdit's single `\r` line breaks. For a command, select an existing host handler, choose its parameters and declare the applicable suffixes. For a panel, give it an ID and reference it from a `panel.open` command in the same manifest. Supply Russian translations for fork-maintained user-facing labels using [the manifest translation fields](../../../docs/extensions.md#translate-extension-labels). Identify any behavior that would require implementing a new host handler.
3. **Verify the behavior.** Run the .NET 8 tests from the repository root:

   ```sh
   dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
   ```

   To preview a grammar, append `-- <manifest-path> <sample-path>` to that command. Compare the emitted scopes and UTF-16 spans against ordinary, overlapping and unfinished tokens. For a command or panel, exercise selection and caret-only input, disabled state, and document changes or tab switches. Check an edit and Undo as a pair. For navigation, check the original line with duplicate text and non-ASCII characters.
4. **Distribute and exercise the result.** Save the UTF-8 manifest with the suffix `.notepads-extension.json`. Put a bundled manifest at `plugins/<name>/<name>.notepads-extension.json` with a filename unique across bundled plugins. Keep optional examples at `plugins/examples/<name>/`. For a ZIP or a GitHub release, follow [Install, update and distribute](../../../docs/extensions.md#install-update-and-distribute). In Windows, import the package under **Settings → Advanced → Extensions** and try its commands from **Main menu → Extension commands**. Report the source, version, test results and whether the Windows steps were exercised.
