# Writing Notepads extensions

A Notepads extension is a UTF-8 JSON manifest. It can associate file suffixes with highlighting rules, add commands to the editor, or open a read-only results panel. The host implements each operation. An author can publish a new combination of supported operations by shipping a manifest. New handler implementations require a change to the app.

## Try the bundled extensions

**JSON Tools.** Open a `.json` file and choose **Main menu → Extension commands → Format JSON** or **Minify JSON**. A selection is treated as a complete JSON value. With just a caret, the command processes the whole document. Formatting uses two spaces per level and preserves the document's line-ending setting. Both commands preserve key order, duplicate keys, string escapes and numeric spellings. **Ctrl+Z** undoes a transformation in one step. A parse error selects the offending position and reports the document line and column. The same **JSON** switch in Settings controls highlighting and both commands. [JSON manifest](../src/Notepads/Assets/Extensions/json.notepads-extension.json)

**Log Lens.** Open the [sample log](../tests/SyntaxExtensions/Fixtures/interleaved.log.txt), select `req-41`, and choose **Main menu → Extension commands → Open Log Lens**. Adjust or clear the request filter in the panel and choose a severity. For structured lines with a `requestId`, the filter matches the entire field. For plain text it searches for the literal text. The panel understands JSON lines, Docker's `log` envelope and common NestJS levels. Select a result to return to its original line and read the surrounding stack trace. Filtering and navigation leave the text and Undo history intact. [Log Lens manifest](../src/Notepads/Assets/Extensions/log-lens.notepads-extension.json)

## Create a highlighting extension

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

## API v1 grammar reference

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

Grammar-only v1 manifests keep working and have an implicit package version of `1.0.0`. To publish a newer version or add commands, set `apiVersion` to `2`, add a three-part `version` and keep the same `id`.

## Create an editor command

Install [wrap.notepads-extension.json](examples/wrap.notepads-extension.json). In a Markdown file select `important` and choose **Main menu → Extension commands → Wrap in bold**. The selected word becomes `**important**`. Press **Ctrl+Z** to restore it. With an empty selection the command inserts both delimiters at the caret. Change `prefix`, `suffix` and `title` to adapt it, and choose your own extension ID before sharing.

### API v2 reference

Keep `id`, `displayName` and `contributes` as in v1. Set `apiVersion` to `2` and supply a numeric `version` such as `1.2.0`. Each manifest needs at least one grammar or command. Omit unused arrays.

| Contribution | Fields and behavior |
| --- | --- |
| `grammars` | The v1 grammar format and limits apply. A grammar and commands can share a manifest and enabled setting. |
| `commands` | Up to 32 entries. Give each a unique `id`, a user-visible `title` and a `handler` from the table below. An optional `fileExtensions` list of 1–32 suffixes selects applicable documents. Omitting it makes the command available on all documents. |
| `panels` | Up to 16 entries. Each has a unique `id`, a `title` and a `handler`. Use `log.filter` for read-only log results. A command opens a panel by its ID in the same manifest. |

Command and panel IDs follow the extension ID rules. Titles accept up to 100 UTF-16 code units. The host recognizes these command handlers.

| Handler | Operation |
| --- | --- |
| `text.wrap` | Set `prefix` and `suffix` to strings of printable characters, with a combined length of 1–512 UTF-16 code units. Wrap the current selection, or insert the delimiters when the selection is empty. |
| `json.format`, `json.minify` | Rewrite whitespace in a complete JSON value. Invalid input returns a diagnostic and a source position. |
| `panel.open` | Set `panel` to the ID of a declared results panel. The host opens it for the active document and uses the current selection as its initial filter. |

When extending the host, use the [editor command contract](../src/Notepads/SyntaxExtensions/EditorCommands.cs). A handler reads an immutable snapshot containing a document ID, revision, text and selection. Its offsets are zero-based UTF-16 code units in editor text, with one carriage return per line break. Ranges use a start and length. An edit returns one replacement for one Undo step, a diagnostic returns a message and source range, and each panel result carries its original line and source range. Apply edits and navigation only while the snapshot still matches the active document and revision. Handle stale results by discarding or refreshing them.

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

## Install, update and distribute

Open **Settings → Advanced → Extensions**. Choose **Install from file…** for a local JSON manifest or ZIP, or **Install from URL…** for a remote source. Review the extension ID, version and source before confirming. Use the switch beside the extension to enable its grammars, commands and panels together.

| Source | What to publish or select |
| --- | --- |
| JSON file or HTTPS URL | Provide a UTF-8 manifest up to 64 KiB. Use a raw/download URL for the file on a web host. |
| ZIP file or HTTPS URL | Include exactly one `*.notepads-extension.json` manifest at the archive root or one directory below it. The ZIP can be up to 2 MiB with up to 256 entries. The installer reads the manifest and ignores other files. |
| Public GitHub repository or `/releases/latest` | Attach one `*.notepads-extension.json` or ZIP to the latest published stable release. If GitHub has no latest release, put a single matching manifest at the root of the default branch. For a release with multiple matching assets, give users the direct HTTPS asset URL. |
| GitHub `/releases/tag/<tag>` | Use the release page for a specific tag to pin the source. A direct asset URL also points to that asset. |

For a remote installation, choose **Check for updates** to check the recorded source. A repository URL follows the latest release, while a tag URL continues checking that release. When an update has the same ID and a higher package version, review and confirm the replacement. For a local installation, import the updated JSON or ZIP again.

Choose **Open extensions folder** to edit an installed `ext-<id>.notepads-extension.json` under `LocalState/Extensions`, then **Reload extensions**. To remove a user extension, delete its manifest and reload. Manifests copied directly into that folder must end in `.notepads-extension.json`. Other running instances have their own in-memory catalog; reload each instance where you want the change.

To bundle an extension with the app, put its manifest directly under [src/Notepads/Assets/Extensions](../src/Notepads/Assets/Extensions/) before building. Keep examples and test data outside that directory.

## Build and check

To run the app from source on Windows, open `src/Notepads.sln` in Visual Studio with the Universal Windows Platform tools and Windows SDK 10.0.22621.0 installed. Select **Notepads**, **Debug**, **x64**, **Local Machine** and press **F5**. This configuration runs as **Notepads-Dev**.

For an editor smoke test, open a sample, change a token and undo the edit. Rename or save the file under another suffix and then restore the original suffix. Check scrolling, word wrap, selection and both themes. Save and reopen the file with the original encoding and line endings to verify its contents. For the bundled JSON extension, include an escaped quote, an emoji in a key, a negative exponent and a nested array.
