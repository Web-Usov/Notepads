using System.Diagnostics;
using System.Text;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;
using NUnit.Framework;

namespace Plainix.UiTests;

/// <summary>
/// Operates the real, installed UWP app on the current interactive desktop.
/// The installer and user-profile lifecycle are owned by Run-UiTests.ps1.
/// </summary>
internal sealed class UiTestHost : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private readonly string _sourceFile;
    private readonly int _editorProcessId;
    private bool _disposed;

    public UiTestHost(string contents)
    {
        var alias = Environment.GetEnvironmentVariable("PLAINIX_UI_ALIAS");
        var data = Environment.GetEnvironmentVariable("PLAINIX_UI_DATA");
        if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(data))
        {
            throw new InvalidOperationException(
                "Run the UI tests through tests/UI/Run-UiTests.ps1 after installing the MSIX package.");
        }

        Directory.CreateDirectory(data);
        _sourceFile = Path.Combine(data, "plainix-ui-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(_sourceFile, contents, new UTF8Encoding(false));

        var start = new ProcessStartInfo(alias)
        {
            Arguments = "\"" + _sourceFile + "\"",
            UseShellExecute = true
        };
        Process.Start(start)?.Dispose();

        var fileName = Path.GetFileName(_sourceFile);
        Window = WaitFor(
            () => _automation.GetDesktop().FindAllChildren()
                .FirstOrDefault(element => element.Name.Contains(fileName, StringComparison.OrdinalIgnoreCase)),
            "the document window for " + fileName, 45);
        var editor = WaitFor(
            () => Window.FindFirstDescendant(cf => cf.ByAutomationId("TextEditorCore")),
            "the RichEdit editor");
        _editorProcessId = editor.Properties.ProcessId.Value;
        WaitUntil(() => ReadDocument().TrimEnd('\r', '\n') == contents, "fixture file contents");
    }

    public AutomationElement Window { get; }

    public AutomationElement Editor =>
        FindById("TextEditorCore") ?? throw new AssertionException("TextEditorCore disappeared.");

    public string SourceFile => _sourceFile;

    public static void Keys(string sequence) => SendKeys.SendWait(sequence);

    public static T WaitFor<T>(Func<T?> probe, string description, int seconds = 25) where T : class
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        Exception? last = null;
        while (DateTime.UtcNow < until)
        {
            try
            {
                var result = probe();
                if (result is not null) return result;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                last = ex;
            }
            Thread.Sleep(150);
        }

        throw new AssertionException("Timed out waiting for " + description +
                                     (last is null ? "." : ": " + last.Message));
    }

    public static void WaitUntil(Func<bool> probe, string description, int seconds = 25)
    {
        WaitFor(() => probe() ? "ready" : null, description, seconds);
    }

    public AutomationElement? FindById(string id)
    {
        foreach (var root in AppRoots())
        {
            var control = root.FindFirstDescendant(cf => cf.ByAutomationId(id));
            if (control != null) return control;
        }
        return null;
    }

    public AutomationElement? FindByName(string name)
    {
        foreach (var root in AppRoots())
        {
            var control = root.FindFirstDescendant(cf => cf.ByName(name));
            if (control != null) return control;
        }
        return null;
    }

    public AutomationElement RequireId(string id) => WaitFor(() => FindById(id), id);
    public AutomationElement RequireName(string name) => WaitFor(() => FindByName(name), name);

    public string ReadDocument() => Editor.Patterns.Text.Pattern.DocumentRange.GetText(-1);

    public void AssertDocument(string expected)
    {
        WaitUntil(() => ReadDocument().TrimEnd('\r', '\n') == expected, "document to equal expected text");
        Assert.That(ReadDocument().TrimEnd('\r', '\n'), Is.EqualTo(expected));
    }

    public void Invoke(AutomationElement element) => element.Patterns.Invoke.Pattern.Invoke();

    public void OpenSettings(string itemId)
    {
        Invoke(RequireId("MainMenuButton"));
        Invoke(RequireId("MenuSettingsButton"));
        var item = RequireId(itemId);
        item.Focus();
        Keys("{ENTER}");
    }

    public void CaptureEvidence(string testName, bool failed)
    {
        var root = Environment.GetEnvironmentVariable("PLAINIX_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(root)) return;
        Directory.CreateDirectory(root);
        var safeName = string.Concat(testName.Select(c =>
            char.IsLetterOrDigit(c) || c == '-' ? c : '_'));
        try
        {
            if (failed)
            {
                Capture.Element(Window).ToFile(Path.Combine(root, safeName + ".png"));
            }

            var lines = new List<string>
            {
                "Test: " + testName,
                "Failed: " + failed,
                "Window: " + Window.Name,
                "Bounds: " + Window.BoundingRectangle,
                "Process ID: " + _editorProcessId
            };
            foreach (var element in Window.FindAllDescendants().Take(150))
            {
                try
                {
                    lines.Add(element.ControlType + " | " + element.AutomationId + " | " + element.Name +
                              " | " + element.BoundingRectangle);
                }
                catch (Exception ex)
                {
                    lines.Add("UIA element unavailable: " + ex.Message);
                }
            }
            File.WriteAllLines(Path.Combine(root, safeName + ".uia.txt"), lines);
        }
        catch (Exception ex)
        {
            TestContext.Error.WriteLine("Could not capture UI diagnostics: " + ex);
        }
    }

    private IEnumerable<AutomationElement> AppRoots()
    {
        yield return Window;
        foreach (var candidate in _automation.GetDesktop().FindAllChildren())
        {
            // UWP menu flyouts can live in their own top-level popup window.
            if (candidate.Properties.ProcessId.Value == _editorProcessId &&
                !ReferenceEquals(candidate, Window))
            {
                yield return candidate;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_editorProcessId > 0)
            {
                var app = Process.GetProcessById(_editorProcessId);
                if (!app.HasExited)
                {
                    app.Kill();
                    app.WaitForExit(10000);
                }
            }
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        finally
        {
            _automation.Dispose();
            try { File.Delete(_sourceFile); } catch (IOException) { }
        }
    }
}
