using System.Threading;
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
        UiTestHost.WaitUntil(() => File.ReadAllText(App.SourceFile) == Original + "x",
            "saving the modified document", 30);
        Assert.That(File.ReadAllText(App.SourceFile), Is.EqualTo(Original + "x"));
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
        try
        {
            toggle.Patterns.Toggle.Pattern.Toggle();
            UiTestHost.WaitUntil(() =>
                App.RequireId("ShowStatusBarToggleSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ToString() != initial,
                "status bar preference to change");
        }
        finally
        {
            var fresh = App.RequireId("ShowStatusBarToggleSwitch");
            if (fresh.Patterns.Toggle.Pattern.ToggleState.Value.ToString() != initial)
            {
                fresh.Patterns.Toggle.Pattern.Toggle();
            }
        }

        UiTestHost.WaitUntil(() =>
            App.RequireId("ShowStatusBarToggleSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ToString() == initial,
            "status bar preference to be restored");
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
