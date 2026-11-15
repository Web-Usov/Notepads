# Writing a Notepads language extension

A language extension is one UTF-8 JSON file. It lists the file suffixes it handles and the patterns to highlight. When you open or rename a file, Notepads selects a grammar by its last suffix, runs its rules on the text, and draws the resulting token colors over the editor. This keeps saving, selection, and Undo in the editor's plain-text model. The [bundled JSON extension](../src/Notepads/Assets/Extensions/json.notepads-extension.json) uses the same API as an extension you install yourself.

## Create your first extension

Save the following as `todos.notepads-extension.json`. It recognizes task markers and comments in `.todo` files.

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

Save these three lines as a UTF-8 file named `example.todo`.

```text
TODO buy milk
# DONE is an example
DONE send invoice
```

In **Settings → Advanced → Extensions**, choose **Install from file…** and select the manifest. Open `example.todo`. On line one, `TODO` gets the keyword color. The entire second line gets the comment color, including `DONE`. On line three, only `DONE` gets the keyword color. Toggle **Todo lists** off and on to check the change in the same tab.

To create an extension with a coding agent, use the repository's [create-notepads-extension skill](../.agents/skills/create-notepads-extension/SKILL.md). Give the agent the target suffixes and a short example showing what should receive each color.

## Manifest reference

The v1 loader accepts UTF-8 JSON up to 64 KiB, with an optional UTF-8 BOM. Use the field names and scope names exactly as shown. All fields in the table are required.

| Field | Accepted values |
| --- | --- |
| `apiVersion` | Integer `1`. |
| `id` | Stable identifier of 1–80 ASCII characters. Start with a lowercase letter or digit. Subsequent characters can also include `.`, `_`, and `-`. The ID identifies upgrades and the user's enabled setting. |
| `displayName` | Nonblank label shown in Settings, up to 100 UTF-16 code units. Control characters are rejected. |
| `contributes.grammars` | Array of 1–16 grammars. Group suffixes under one grammar when they share rules. |
| `fileExtensions` (per grammar) | Array of 1–32 suffixes, each starting with a dot followed by 1–24 ASCII characters. The first character after the dot must be a letter or digit. The rest may also include `_`, `+`, and `-`. Suffixes are unique across the manifest, ignoring case. For `app.config.json`, the suffix is `.json`. |
| `rules` (per grammar) | Ordered array of 1–32 objects, each with a `scope` and a `match`. |
| `scope` | One of `property`, `string`, `number`, `keyword`, `punctuation`, or `comment`. The editor supplies light and dark colors for each. |
| `match` | Nonblank .NET regular expression, up to 1,024 UTF-16 code units after JSON decoding. Each match must consume at least one character. |

API v1 exposes a single contribution point, `contributes.grammars`. Share the manifest file directly. Adding commands, callbacks, or a different grammar format would require changes to the host.

## How rules and grammars are selected

At each step the tokenizer picks the rule whose next match starts earliest, colors the entire match with its scope, then resumes at the end of that match. If multiple rules start at the same character, the first rule in the manifest wins. In the example, `#` starts the comment before the tokenizer reaches `DONE`. In the bundled JSON grammar, both a property and a string can start at the same quote. The property rule appears first and uses a lookahead for the colon.

Patterns use culture-invariant .NET regex semantics. JSON escaping applies before regex parsing, so write `"\\b"` to pass a word boundary to the regex engine. RichEdit represents each line break as one carriage return (`\r`). The example’s comment rule excludes both `\r` and `\n`. For a rule anchored to the start of each editor line, use `(?:\A|(?<=\r))` in the decoded regex. Token offsets and lengths count UTF-16 code units, so an emoji such as 🔧 occupies two positions.

The loader reads user manifests first, then bundled manifests. Within each folder it uses case-insensitive filename order. The first enabled manifest for an ID wins as a whole. When different enabled IDs claim the same suffix, the first grammar for that suffix wins. The panel's alphabetical display order does not affect selection. Disable a competing ID to try another extension for the same suffix. A user copy with the same ID as a bundled extension shares its enabled setting. To restore the bundled version, remove the user copy, reload and enable the ID.

## Check a grammar before installing it

With the .NET 8 SDK, run the following from the repository root. Put the two example files in that directory or substitute their paths.

```sh
dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release -- todos.notepads-extension.json example.todo
```

The preview prints a JSON array with `scope`, `text`, and zero-based `start` and `length` in UTF-16 code units (after normalizing line endings). The three tokens above start at 0, 14, and 35. Compare the reported scopes and spans with your expectations. For your own grammar, include a near miss such as `TODOLIST`, markers inside a comment or quoted string, and unfinished text as it could appear while someone is typing. Run the existing parser, tokenizer and registry regression suite by omitting the two file arguments.

If the panel reports a load error, check the file encoding, field names, suffix format and regex escaping. An invalid file is skipped individually. If a valid grammar produces no colors, check the active suffix, any competing extension, and whether the rules match the sample. Keep expressions inexpensive. The tokenizer falls back to plain text on a zero-length match, a regex timeout (40 ms per call), a scan exceeding its 300 ms budget, input over 1,048,576 UTF-16 code units, or more than 24,000 tokens. High Contrast, right-to-left mode and bidirectional lines use native text colors. Devices without supported composition effects use tinted backgrounds.

## Update, share or bundle

- **Update an imported extension.** Edit the source manifest and choose **Install from file…** again. The importer validates the new version before replacing its copy under `ext-<id>.notepads-extension.json`.
- **Iterate directly in the app folder.** Choose **Open extensions folder**, edit the installed copy in `LocalState/Extensions` and click **Reload extensions**. To remove a user extension, delete its file there and reload. Manifests copied directly into that folder must end in `.notepads-extension.json`. Changes refresh open tabs in this instance. Reload separately in other running instances.
- **Share or ship an extension.** Send the manifest to another user for import. To include it with every installation of this fork, add it directly under [src/Notepads/Assets/Extensions](../src/Notepads/Assets/Extensions/) using the same filename suffix. The app project packages matching files on its next build.

## Build and check

To run the app from source on Windows, open `src/Notepads.sln` in Visual Studio with the Universal Windows Platform tools and Windows SDK 10.0.22621.0 installed. Select **Notepads**, **Debug**, **x64**, **Local Machine** and press **F5**. This configuration runs as **Notepads-Dev**.

For an editor smoke test, open a sample, change a token and undo the edit. Rename or save the file under another suffix and then restore the original suffix. Check scrolling, word wrap, selection and both themes. Save and reopen the file with the original encoding and line endings to verify its contents. For the bundled JSON extension, include an escaped quote, an emoji in a key, a negative exponent and a nested array.
