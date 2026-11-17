using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Notepads.Core;

internal static class EditorSplitLayoutTests
{
    public static void Run()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var stale = Guid.NewGuid();
        var layout = new EditorSplitLayout
        {
            Orientation = EditorSplitOrientation.Right,
            SecondaryEditorIds = new List<Guid> { second, second, stale },
            SelectedPrimaryEditorId = first,
            SelectedSecondaryEditorId = stale,
            ActiveGroup = 1
        };
        var clean = layout.Normalize(new[] { first, second, third });
        Check(clean.Orientation == EditorSplitOrientation.Right, "split orientation");
        Check(clean.SecondaryEditorIds.SequenceEqual(new[] { second }), "stale/duplicate tabs removed");
        Check(clean.SelectedPrimaryEditorId == first, "primary selection");
        Check(clean.SelectedSecondaryEditorId == second, "secondary selection fallback");
        Check(clean.ActiveGroup == 1, "active group restored");

        var restored = JsonSerializer.Deserialize<EditorSplitLayout>(JsonSerializer.Serialize(clean));
        Check(restored.Orientation == EditorSplitOrientation.Right &&
              restored.SecondaryEditorIds.SequenceEqual(new[] { second }), "split JSON roundtrip");

        var legacy = JsonSerializer.Deserialize<EditorSplitLayout>("{}").Normalize(new[] { first });
        Check(legacy.Orientation == EditorSplitOrientation.None && legacy.ActiveGroup == 0,
            "legacy session has one editor group");

        layout.Orientation = EditorSplitOrientation.Down;
        Check(layout.Normalize(new[] { first, second }).Orientation == EditorSplitOrientation.Down,
            "down split");

        layout.SecondaryEditorIds = new List<Guid> { first, second };
        Check(layout.Normalize(new[] { first, second }).Orientation == EditorSplitOrientation.None,
            "all tabs in one group cannot create an empty primary group");

        layout.Orientation = (EditorSplitOrientation)99;
        Check(layout.Normalize(new[] { first, second }).Orientation == EditorSplitOrientation.None,
            "invalid orientation ignored");
        Console.WriteLine("Editor split layout tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
