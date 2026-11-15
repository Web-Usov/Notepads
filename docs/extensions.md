# Extensions

Open a file such as `package.json` to use the bundled JSON language extension. Keys, string values, numbers, literals and punctuation have separate colors. The same colors update as you type, including while a value is incomplete. Saving writes the original plain-text format.

## Manage extensions

Under **Settings → Advanced → Extensions**, check or uncheck an extension to enable or disable it. **Install from file…** imports a UTF-8 JSON manifest. Reinstalling an ID imported this way updates that copy. The importer validates the new manifest before replacing the old one.

**Open extensions folder** opens the app’s `LocalState/Extensions` folder. To uninstall a user extension, delete its manifest there and select **Reload extensions**. You can also copy files ending in `.notepads-extension.json` into this folder and reload. The bundled JSON extension can be disabled. Changes apply to open tabs in the current instance. In other running instances, select **Reload extensions**.

## Write a language extension

Each extension supplies a versioned manifest with an ID and a `contributes.grammars` array. The contribution-point pattern follows editors such as VS Code. Notepads v1 reads its own manifest format and runs declarative .NET regular expressions. VSIX packages, TextMate files and executable entry points have no loader in v1.

For example, save the following as `todos.notepads-extension.json`, import it and open a `.todo` file containing `TODO buy milk` or `# comment`.

```json
{
  "apiVersion": 1,
  "id": "example.todos",
  "displayName": "Todo lists",
  "contributes": {
    "grammars": [
      {
        "fileExtensions": [".todo"],
        "rules": [
          { "scope": "comment", "match": "#[^\\r\\n]*" },
          { "scope": "keyword", "match": "\\b(?:TODO|DONE)\\b" }
        ]
      }
    ]
  }
}
```

- The editor selects a grammar by the last suffix, ignoring case. Unsaved tabs can use a matching filename after Rename or Save As.
- A rule supplies a .NET regular expression and one of six scopes: `property`, `string`, `number`, `keyword`, `punctuation`, `comment`. The theme controls the colors. The earliest match wins. Ties go to the first rule. Matches consume text and cannot overlap. RichEdit uses one carriage return (`\r`) for each line break in its editing buffer.
- The loader reads user files first, then bundled files, each in filename order. The first enabled manifest for an ID wins. The first enabled grammar for each suffix wins. Removing a user override restores the bundled grammar.
- Manifests are UTF-8 and at most 64 KiB. Invalid manifests are skipped individually. The Extensions panel identifies files that failed to load.
- Tokenization runs off the UI thread. Very large documents (more than 1,048,576 UTF-16 code units), excessive matches (more than 24,000) and expensive expressions fall back to ordinary text. High Contrast, right-to-left mode and lines containing bidirectional text use the native text colors. Graphics devices without composition effects use colored backgrounds.

The [bundled JSON extension](../src/Notepads/Assets/Extensions/json.notepads-extension.json) uses exactly this API.

## Build and check

On Windows, open `src/Notepads.sln` in Visual Studio with the Universal Windows Platform tools and Windows SDK 10.0.22621.0 installed. Select **Notepads**, **Debug**, **x64**, **Local Machine** and press **F5**. The debug package uses the upstream development identity, `Notepads-Dev`.

The portable grammar and registry tests require the .NET 8 SDK. From the repository root run:

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release
```

For a Windows smoke test:

1. Open a JSON file with an escaped quote, emoji, a negative exponent and nested arrays. Key names and values should be visually distinguishable in light and dark themes.
2. Edit, undo, redo, copy, save and reopen. Verify that a single undo reverses the text edit and that saving a file with its original encoding and line endings preserves the contents.
3. Scroll vertically and horizontally, change the font size and enable word wrap. Verify alignment and normal selection colors. Turn on Windows High Contrast and check legibility.
4. Rename a JSON tab to `.txt` and back. Toggle JSON in Settings with the file open. Import the example above, then delete it from the extensions folder and reload.
