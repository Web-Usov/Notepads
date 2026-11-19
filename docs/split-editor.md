# Split editor architecture

Plainix offers two built-in editor groups, side by side (**Split right**) or stacked (**Split down**). Drag a tab to the left/right/top/bottom editor edge to create a split and see a temporary drop preview, or use the tab context menu. There are no persistent split buttons in either tab strip. The feature uses native UWP/XAML controls styled with the app's theme resources. It is a core editor feature, not a plugin, and works on supported Windows 10 and 11 builds.

## Components

- `NotepadsCore` owns the document lifecycle, active tab, group selection, editor events, drag/drop and all group operations. `NotepadsCore.SplitEditor.cs` contains the second-group coordinator.
- `SetsView` remains the tab/content host. The existing primary `Sets` and an independent `SecondarySets` each have a selection and tab strip.
- `NotepadsMainPage.SplitEditor.cs` only positions the second view, configures Fluent toolbar/context commands, and manages the themed, keyboard-accessible `GridSplitter`.
- `EditorSplitLayout` stores orientation, secondary document IDs, selected tabs and the active group. It is independent of Windows UI dependencies and covered by the portable test suite.
- `SessionManager` persists the layout as an optional addition to the existing **version 1** session JSON; older session files restore into a single group.

## Invariants

1. Each `ITextEditor` instance is owned by exactly one group at a time. Tab transfers move the existing editor and its undo stack; they do not reopen or duplicate files.
2. `GetAllTextEditors`, unsaved-change checks, session backups and app-close confirmation include both groups.
3. Closing a split merges tabs into the primary group; it does **not** close or dispose editors.
4. Closing the last secondary tab collapses the split. Splitting the only primary document first creates a blank primary tab rather than leaving an unusable empty pane.
5. Switching groups updates the active document for global status-bar, keyboard and extension commands.
6. The source of a tab drag is the `SetsViewItem` container, so the core resolves its `Content` to get the existing editor before serializing the payload. Dragging across groups uses the editor ID from the same app instance and moves the original document without deserializing it. Dropping inside the same tab strip keeps the native ListView reorder behavior; cross-window drag/drop retains serialized-document transfer.
7. A transient accent border highlights only a valid *other-group* destination during drag-over; it never intercepts pointer input. In a single editor group with at least two tabs, dragging to an editor edge previews and creates a new group, keeping the original `ITextEditor` instance. Left/top creates the new group before the existing files, right/bottom after. Cancelling or completing the gesture clears the hint. For mouse/keyboard alternatives, right-click a tab and use **Move to group → Group 1 / Group 2**, with the current group disabled. These actions are queued after the context menu closes to avoid XAML reparenting while the menu still owns its anchor.
8. Missing or duplicate session IDs are ignored; the previous single-group session format remains readable.

## Verification

- Portable: `dotnet run --project tests/SyntaxExtensions/SyntaxExtensions.Tests.csproj -c Release` tests layout normalization and JSON roundtrip alongside extension tests.
- Windows UWP: build `src/Notepads.sln` (x64), run the preview package UI smoke and NUnit/FlaUI suite. Verify drag-to-create on all four edges, bidirectional tab movement, context menu, saving, undo, both locales and themes, and non-duplicated documents. The split divider uses deferred resize (only its indicator moves during drag; expensive RichEdit relayout runs after release).
- Check the app close flow with unsaved tabs in **both** groups and a restored session after application restart.

The initial implementation intentionally supports **two groups** and one document instance per file. Displaying the *same editable buffer twice* with independent carets would require a shared document/view abstraction and is outside this change.
