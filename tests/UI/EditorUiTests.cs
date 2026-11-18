using System.Threading;
using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace Plainix.UiTests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable] // All windows in one interactive desktop share keyboard, focus and the UWP profile.
public sealed class EditorUiTests
{
    private const string Original = "{\"requestId\":\"req-17\",\"level\":\"error\"}";
    private UiTestHost? _host;

    private UiTestHost App => _host ?? throw new InvalidOperationException("The test host was not initialized.");

    [SetUp]
    public void SetUp() => _host = new UiTestHost(Original);

    [TearDown]
    public void TearDown()
    {
        if (_host is null) return;
        try
        {
            var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
            _host.CaptureEvidence(TestContext.CurrentContext.Test.Name, failed);
        }
        finally
        {
            _host.Dispose();
            _host = null;
        }
    }

    [Test]
    public void Document_opens_edits_undoes_redoes_and_saves()
    {
        App.AssertDocument(Original);
        App.Editor.Focus();
        UiTestHost.Keys("^{END}");
        UiTestHost.Keys("x");
        App.AssertDocument(Original + "x");

        UiTestHost.Keys("^z");
        App.AssertDocument(Original);
        UiTestHost.Keys("^+z");
        App.AssertDocument(Original + "x");

        UiTestHost.Keys("^s");
        // The packaged editor may hold an exclusive file handle while saving.
        // Wait for the completed write, rather than treating a transient lock
        // as a failed save. Assert the exact bytes read on the successful poll.
        string? savedContents = null;
        UiTestHost.WaitUntil(() =>
        {
            try
            {
                savedContents = File.ReadAllText(App.SourceFile);
                return savedContents == Original + "x";
            }
            catch (IOException)
            {
                return false;
            }
        }, "saving the modified document", 30);
        Assert.That(savedContents, Is.EqualTo(Original + "x"));
    }

    [Test]
    public void Markdown_preview_can_be_opened_and_closed_without_editing()
    {
        // Markdown preview is a host-backed view even before its toggleable extension ships.
        // File-type association must be real: a .json fixture cannot open Markdown preview.
        const string markdown = "# Plainix preview";
        _host!.Dispose();
        _host = new UiTestHost(markdown, ".md");

        var originalWidth = App.Editor.BoundingRectangle.Width;
        Assert.That(originalWidth, Is.GreaterThan(300), "Expected a usable editor viewport.");
        App.Editor.Focus();
        UiTestHost.Keys("%p"); // Alt+P opens the native Markdown preview panel.
        UiTestHost.WaitUntil(() => App.Editor.BoundingRectangle.Width < originalWidth * 0.85,
            "Markdown preview to split the editor", 35);
        App.AssertDocument(markdown);

        App.Editor.Focus();
        UiTestHost.Keys("%p");
        UiTestHost.WaitUntil(() => App.Editor.BoundingRectangle.Width >= originalWidth * 0.93,
            "Markdown preview to close", 35);
        App.AssertDocument(markdown);
    }

    [Test]
    public void Diff_viewer_can_be_opened_and_closed_without_losing_edits()
    {
        App.Editor.Focus();
        UiTestHost.Keys("^{END}");
        UiTestHost.Keys("x");
        App.AssertDocument(Original + "x");

        var originalHeight = App.Editor.BoundingRectangle.Height;
        Assert.That(originalHeight, Is.GreaterThan(250), "Expected a usable editor viewport.");
        UiTestHost.Keys("%d"); // Alt+D opens the saved-versus-current diff.
        UiTestHost.WaitUntil(() => App.Editor.BoundingRectangle.Height < originalHeight * 0.85,
            "diff viewer to split the editor", 35);
        App.AssertDocument(Original + "x");

        UiTestHost.Keys("{ESC}"); // The diff viewer has an Escape accelerator.
        UiTestHost.WaitUntil(() => App.Editor.BoundingRectangle.Height >= originalHeight * 0.93,
            "diff viewer to close", 35);
        App.AssertDocument(Original + "x");
    }

    [Test]
    public void Find_bar_searches_open_document()
    {
        App.Editor.Focus();
        UiTestHost.Keys("^f");
        var search = App.RequireId("FindBar");
        search.Patterns.Value.Pattern.SetValue("requestId");
        UiTestHost.WaitUntil(() => App.RequireId("SearchForwardButton").IsEnabled,
            "the enabled Find Next action");
        Assert.That(App.RequireId("SearchForwardButton").IsEnabled, Is.True);
        App.AssertDocument(Original);
    }

    [Test]
    public void Advanced_settings_toggle_can_be_changed_and_restored()
    {
        App.OpenSettings("AdvancedSettingsItem");
        var toggle = App.RequireId("ShowStatusBarToggleSwitch");
        var initial = toggle.Patterns.Toggle.Pattern.ToggleState.Value.ToString();

        // Changing the status bar can dismiss the Settings pane. Always reopen it
        // before verifying the persisted preference, instead of reusing a stale peer.
        AutomationElement CurrentToggle()
        {
            var control = App.FindById("ShowStatusBarToggleSwitch");
            if (control is not null) return control;

            App.OpenSettings("AdvancedSettingsItem");
            return App.RequireId("ShowStatusBarToggleSwitch");
        }

        try
        {
            // Drive the actual UI, including its Click/Toggled handlers.
            toggle.Click();
            UiTestHost.WaitUntil(
                () => CurrentToggle().Patterns.Toggle.Pattern.ToggleState.Value.ToString() != initial,
                "status bar preference to change", 20);
        }
        finally
        {
            try
            {
                var current = CurrentToggle();
                if (current.Patterns.Toggle.Pattern.ToggleState.Value.ToString() != initial)
                {
                    current.Click();
                }
            }
            catch (Exception ex)
            {
                TestContext.Error.WriteLine("Could not restore the status bar preference: " + ex);
            }
        }

        UiTestHost.WaitUntil(
            () => CurrentToggle().Patterns.Toggle.Pattern.ToggleState.Value.ToString() == initial,
            "status bar preference to be restored", 20);
    }

    [Test]
    public void Theme_selection_works_in_personalization_settings()
    {
        App.OpenSettings("PersonalizationSettingsItem");
        var ids = new[] { "ThemeModeDefaultButton", "ThemeModeLightButton", "ThemeModeDarkButton" };
        var initial = ids.Single(id => App.RequireId(id).Patterns.SelectionItem.Pattern.IsSelected.Value);
        try
        {
            App.RequireId("ThemeModeLightButton").Click();
            UiTestHost.WaitUntil(() =>
                App.RequireId("ThemeModeLightButton").Patterns.SelectionItem.Pattern.IsSelected.Value,
                "light theme selection");
            App.RequireId("ThemeModeDarkButton").Click();
            UiTestHost.WaitUntil(() =>
                App.RequireId("ThemeModeDarkButton").Patterns.SelectionItem.Pattern.IsSelected.Value,
                "dark theme selection");
        }
        finally
        {
            App.RequireId(initial).Click();
        }

        UiTestHost.WaitUntil(() =>
            App.RequireId(initial).Patterns.SelectionItem.Pattern.IsSelected.Value,
            "original theme selection to be restored");
    }

    [Test]
    public void Json_extension_can_be_disabled_and_reenabled()
    {
        App.OpenSettings("AdvancedSettingsItem");
        var toggle = FindExtension("JSON");
        Assert.That(toggle.Patterns.Toggle.Pattern.ToggleState.Value.ToString(), Is.EqualTo("On"),
            "Bundled JSON commands must be enabled in a clean profile.");

        ToggleExtension("JSON", "Off");
        ToggleExtension("JSON", "On");
    }

    [Test]
    public void Bundled_feature_extensions_can_be_toggled_when_available()
    {
        App.OpenSettings("AdvancedSettingsItem");
        var featureManifests = Environment.GetEnvironmentVariable("PLAINIX_UI_FEATURE_EXTENSIONS") == "1";
        var names = new[] { "Markdown Preview", "Changes Diff" };
        if (featureManifests)
        {
            foreach (var name in names)
            {
                Assert.That(FindExtension(name).Patterns.Toggle.Pattern.ToggleState.Value.ToString(),
                    Is.EqualTo("On"), name + " must be enabled by default.");
                ToggleExtension(name, "Off");
                ToggleExtension(name, "On");
            }
        }
        else
        {
            // On the base branch these views are built-ins, before #13 is merged.
            // Do not quietly skip the test: assert that the extension toggles are
            // genuinely absent and that JSON extensions still register normally.
            foreach (var name in names)
            {
                Assert.That(TryFindExtension(name), Is.Null,
                    name + " should not appear as a feature extension without its manifest.");
            }

            Assert.That(FindExtension("JSON").Patterns.Toggle.Pattern.ToggleState.Value.ToString(),
                Is.EqualTo("On"));
        }
    }

    [Test]
    public void Visual_layout_has_visible_editor_and_menu_landmarks()
    {
        var frame = App.Window.BoundingRectangle;
        var editor = App.Editor.BoundingRectangle;
        var menu = App.RequireId("MainMenuButton").BoundingRectangle;
        Assert.That(frame.Width, Is.GreaterThanOrEqualTo(500));
        Assert.That(frame.Height, Is.GreaterThanOrEqualTo(350));
        Assert.That(editor.Width, Is.GreaterThan(250));
        Assert.That(editor.Height, Is.GreaterThan(150));
        Assert.That(editor.Left, Is.GreaterThanOrEqualTo(frame.Left - 2));
        Assert.That(editor.Top, Is.GreaterThanOrEqualTo(frame.Top));
        Assert.That(menu.Width, Is.GreaterThan(10));
        Assert.That(menu.Height, Is.GreaterThan(10));
        // This test also exports a screenshot, even on success, for nightly UI review.
        App.CaptureEvidence("Visual-layout-snapshot", failed: false, captureScreenshot: true);
    }

    [Test]
    public void Tab_context_menu_moves_existing_document_between_groups_and_keeps_undo()
    {
        App.Editor.Focus();
        UiTestHost.Keys("^{END}");
        UiTestHost.Keys("x");
        App.AssertDocument(Original + "x");

        PrepareSplitForTabMoves();

        MoveCurrentFileFromContextMenu("Group 1 — first pane");
        UiTestHost.WaitUntil(() => IsCurrentFileInGroup("PrimaryEditorGroup"),
            "context menu move into the first group");
        Assert.That(CountDocumentTabs(), Is.EqualTo(1), "Tab transfer must never duplicate the document.");
        App.Editor.Focus();
        UiTestHost.Keys("^z");
        App.AssertDocument(Original);

        MoveCurrentFileFromContextMenu("Group 2 — second pane");
        UiTestHost.WaitUntil(() => IsCurrentFileInGroup("SecondaryEditorGroup"),
            "context menu move into the second group");
        Assert.That(CountDocumentTabs(), Is.EqualTo(1));
    }

    [Test]
    public void Dragging_tab_into_other_group_moves_original_editor_without_duplication()
    {
        PrepareSplitForTabMoves();
        DragCurrentFileTo("PrimaryEditorGroup");
        DragCurrentFileTo("SecondaryEditorGroup");

        // Both transfers must preserve the actual source text, not merely the
        // tab label. Use both panes' text providers rather than the first one.
        var editorContents = App.Window.FindAllDescendants()
            .Where(element => (element.Properties.AutomationId.ValueOrDefault ?? string.Empty) == "TextEditorCore")
            .Select(element => element.Patterns.Text.Pattern.DocumentRange.GetText(-1).TrimEnd('\r', '\n'))
            .ToArray();
        Assert.That(editorContents, Does.Contain(Original));
    }

    private void DragCurrentFileTo(string targetGroup)
    {
        CurrentFileTab().Click();
        Thread.Sleep(150);
        var source = CurrentFileTab().BoundingRectangle;
        var divider = App.RequireId("VerticalSplitDivider").BoundingRectangle;
        var targetTab = UiTestHost.WaitFor(() => App.Window.FindAllDescendants()
            .FirstOrDefault(element =>
                element.ControlType == ControlType.ListItem &&
                element.Name == "Untitled.txt" &&
                (targetGroup == "PrimaryEditorGroup"
                    ? element.BoundingRectangle.Right <= divider.Left
                    : element.BoundingRectangle.Left >= divider.Right)),
            "the target tab in " + targetGroup);
        var target = targetTab.BoundingRectangle;
        var from = new Point((int)(source.Left + Math.Min(source.Width / 3, 35)),
            (int)(source.Top + source.Height / 2));
        var to = new Point((int)(target.Left + target.Width / 2),
            (int)(target.Top + target.Height / 2));

        Mouse.Position = from;
        Mouse.Down();
        try
        {
            Thread.Sleep(180);
            // FlaUI Mouse.MoveTo uses SetCursorPos. Send real Win32 move events
            // to exercise a native UWP drag instead of only relocating the cursor.
            for (var step = 1; step <= 24; step++)
            {
                var x = (int)Math.Round(from.X + (to.X - from.X) * step / 24.0);
                var y = (int)Math.Round(from.Y + (to.Y - from.Y) * step / 24.0);
                MoveMouseAsInput(x, y);
                Thread.Sleep(22);
            }
            Thread.Sleep(200);
        }
        finally
        {
            Mouse.Up();
        }

        UiTestHost.WaitUntil(() => IsCurrentFileInGroup(targetGroup),
            "dragged tab to appear in " + targetGroup, 30);
        Assert.That(CountDocumentTabs(), Is.EqualTo(1), "Drag cannot create a duplicate tab.");
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", EntryPoint = "mouse_event")]
    private static extern void SendMouseEvent(uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    private static void MoveMouseAsInput(int screenX, int screenY)
    {
        const uint mouseMove = 0x0001;
        const uint absolute = 0x8000;
        const uint virtualDesktop = 0x4000;
        var left = GetSystemMetrics(76);
        var top = GetSystemMetrics(77);
        var width = Math.Max(GetSystemMetrics(78) - 1, 1);
        var height = Math.Max(GetSystemMetrics(79) - 1, 1);

        var x = (uint)Math.Clamp((long)Math.Round((screenX - left) * 65535.0 / width), 0, 65535);
        var y = (uint)Math.Clamp((long)Math.Round((screenY - top) * 65535.0 / height), 0, 65535);
        SendMouseEvent(mouseMove | absolute | virtualDesktop, x, y, 0, UIntPtr.Zero);
    }

    private void PrepareSplitForTabMoves()
    {
        App.Invoke(App.RequireId("SplitEditorToolbarButton"));
        App.Invoke(App.RequireId("ToolbarSplitRightButton"));
        UiTestHost.WaitUntil(() =>
            App.RequireId("VerticalSplitDivider").BoundingRectangle.Width > 1 &&
            App.Window.FindAllDescendants()
                .Count(element => (element.Properties.AutomationId.ValueOrDefault ?? string.Empty) == "TextEditorCore") == 2,
            "two text editors after splitting", 35);

        // Keep both groups occupied when the file moves. Otherwise the normal
        // editor lifecycle closes an empty secondary group automatically.
        App.Invoke(App.RequireId("SecondaryNewSetButton"));
        UiTestHost.WaitUntil(() => IsCurrentFileInGroup("SecondaryEditorGroup"),
            "original document in the secondary group");
    }

    private int CountDocumentTabs()
    {
        var name = Path.GetFileName(App.SourceFile);
        return App.Window.FindAllDescendants().Count(element =>
            (element.Properties.AutomationId.ValueOrDefault ?? string.Empty).StartsWith("PlainixEditorTab-", StringComparison.Ordinal) &&
            string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private AutomationElement CurrentFileTab()
    {
        var name = Path.GetFileName(App.SourceFile);
        return UiTestHost.WaitFor(() => App.Window.FindAllDescendants().FirstOrDefault(element =>
            (element.Properties.AutomationId.ValueOrDefault ?? string.Empty).StartsWith("PlainixEditorTab-", StringComparison.Ordinal) &&
            string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase)),
            "document tab named " + name);
    }

    private bool IsCurrentFileInGroup(string groupAutomationId)
    {
        // The custom SetsView template does not expose its parent ListView as a
        // UIA element. Its *tabs* and the themed divider are accessible, so
        // assert destination group ownership by their screen coordinates.
        var name = Path.GetFileName(App.SourceFile);
        var tab = App.Window.FindAllDescendants().FirstOrDefault(element =>
            (element.Properties.AutomationId.ValueOrDefault ?? string.Empty).StartsWith("PlainixEditorTab-", StringComparison.Ordinal) &&
            string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase));
        var divider = App.FindById("VerticalSplitDivider");
        if (tab == null || divider == null) return false;
        var boundary = divider.BoundingRectangle;
        var tabRect = tab.BoundingRectangle;
        if (groupAutomationId == "PrimaryEditorGroup") return tabRect.Right <= boundary.Left;
        if (groupAutomationId == "SecondaryEditorGroup") return tabRect.Left >= boundary.Right;
        throw new ArgumentOutOfRangeException(nameof(groupAutomationId));
    }

    private static AutomationElement OwningMenuItem(AutomationElement element)
    {
        var current = element;
        for (var depth = 0; depth < 8; depth++)
        {
            if (current.ControlType == ControlType.MenuItem) return current;
            var parent = current.Parent;
            if (parent is null) break;
            current = parent;
        }
        return element;
    }

    private void MoveCurrentFileFromContextMenu(string destinationLabel)
    {
        CurrentFileTab().RightClick();

        var submenu = OwningMenuItem(App.RequireName("Move to group"));
        if (submenu.Patterns.ExpandCollapse.IsSupported)
        {
            submenu.Patterns.ExpandCollapse.Pattern.Expand();
        }
        else
        {
            submenu.Click();
        }
        var destination = OwningMenuItem(App.RequireName(destinationLabel));
        if (destination.Patterns.Invoke.IsSupported)
        {
            destination.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            // MenuFlyoutItem on some UWP builds only exposes its TextBlock to UIA.
            var bounds = destination.BoundingRectangle;
            Mouse.Position = new Point((int)(bounds.Left + bounds.Width / 2),
                (int)(bounds.Top + bounds.Height / 2));
            Thread.Sleep(180);
            Mouse.Click();
        }
    }

    private AutomationElement? TryFindExtension(string label)
    {
        // Version and contributed file-extension suffix can change; the stable
        // display-name prefix is the UI contract.
        return App.Window.FindAllDescendants()
            .FirstOrDefault(element =>
                element.ControlType == ControlType.CheckBox &&
                element.Name.StartsWith(label + "  ", StringComparison.Ordinal));
    }

    private AutomationElement FindExtension(string label) =>
        UiTestHost.WaitFor(() => TryFindExtension(label), label + " extension toggle");

    private void ToggleExtension(string label, string targetState)
    {
        var check = FindExtension(label);
        if (check.Patterns.Toggle.Pattern.ToggleState.Value.ToString() != targetState)
        {
            check.Patterns.Toggle.Pattern.Toggle();
        }
        // The app destroys and recreates CheckBox controls on each registry reload.
        UiTestHost.WaitUntil(() =>
            FindExtension(label).Patterns.Toggle.Pattern.ToggleState.Value.ToString() == targetState,
            label + " toggle to become " + targetState, 30);
    }
}
