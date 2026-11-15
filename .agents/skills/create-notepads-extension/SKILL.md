---
name: create-notepads-extension
description: Create, update and test installable syntax-highlighting extensions for the Web-Usov/Notepads fork. Use for a new language grammar, a file association, or a fix to token matching in a .notepads-extension.json manifest.
---

# Create a Notepads language extension

Work from the Notepads repository root. Read the [extension authoring guide](../../../docs/extensions.md) for the v1 contract and see the [bundled JSON manifest](../../../src/Notepads/Assets/Extensions/json.notepads-extension.json) for a working grammar.

1. **Choose the file and scope.** Identify the last filename suffix, the constructs to highlight and any existing manifest that claims the same suffix. Use representative input from the request when available. For a change to an existing plugin, preserve its ID so an import updates the installed copy.
2. **Write the installable manifest.** Save a UTF-8 `<language>.notepads-extension.json` at the requested destination. For an extension intended to ship with the app, place it directly under `src/Notepads/Assets/Extensions/`. The project already packages matching files. For a standalone extension, deliver the manifest for import through Settings. Keep samples and test fixtures outside the bundled extensions folder.
3. **Design consuming rules.** Map constructs to `property`, `string`, `number`, `keyword`, `punctuation` or `comment`. Use .NET regex syntax and JSON escaping. Give the more specific rule priority when two matches start at the same offset. Match a whole quoted value or comment when it should protect its contents from other rules. Account for the editor’s single `\r` between lines when writing line anchors.
4. **Exercise the grammar.** Save a small UTF-8 sample with a claimed suffix. Include an ordinary construct, a potential false match, and overlapping or unfinished input where relevant. Decide what should get a scope and what should remain plain. With the .NET 8 SDK, run the following commands from the repository root, substituting the two file paths:

   ```sh
   dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release -- <manifest-path> <sample-path>
   dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
   ```

   Compare the emitted `scope` and `text` with the expectations. Use `start` and `length` to catch misplaced or partial matches. If an expected token is missing, reduce the sample and check escaping, rule order and zero-width matches. An empty array can also indicate a tokenizer budget limit. For a bundled grammar change, add targeted coverage when the existing tests would miss a regression.
5. **Hand off the extension.** Give the manifest path, supported suffixes, tested sample and any deliberate omissions in language coverage. To try it in Windows, select **Settings → Advanced → Extensions → Install from file…**, choose the manifest, then open the sample. To test manual edits inside the extensions folder, select **Reload extensions**. Report the Windows result only if someone ran that check.
