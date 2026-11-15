using System;
using System.IO;
using System.Linq;
using Notepads.SyntaxExtensions;

internal static class EditorApiTests
{
    public static void Run()
    {
        var sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wrap.notepads-extension.json"));
        var extension = SyntaxExtension.Parse(sample);
        var wrap = extension.Commands.Single();
        Check(wrap.AppliesTo(".MD") && !wrap.AppliesTo(".cs") && !wrap.AppliesTo(null), "command suffix filter");
        var id = Guid.NewGuid();
        var snapshot = new EditorSnapshot(id, 7, "a\r🌲 z", 2, 2);
        var result = wrap.Execute(snapshot);
        Check(result.Start == 2 && result.Length == 2 && result.Text == "**🌲**", "external wrap handler");
        int edits = 0;
        Action<int, int, string> replace = (start, length, text) => edits++;
        Action<int, int, string> error = (start, length, text) => throw new Exception("unexpected diagnostic");
        Check(!result.Apply(snapshot, Guid.NewGuid(), 7, replace, error), "different tab");
        Check(!result.Apply(snapshot, id, 8, replace, error), "different revision");
        Check(!EditorCommandResult.Replace(5, 3, "bad").Apply(snapshot, id, 7, replace, error), "invalid result range");
        Check(EditorCommandResult.Replace(2, 2, "🌲").Apply(snapshot, id, 7, replace, error) && edits == 0, "no-op leaves undo alone");
        Check(result.Apply(snapshot, id, 7, replace, error) && edits == 1, "one transaction");

        var log = SyntaxExtension.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "log-lens.notepads-extension.json")));
        var panel = log.Commands.Single();
        Check(panel.AppliesTo(null) && panel.Panel != null && log.Grammars.Count == 0, "command-only extension");
        Check(panel.Panel.Query(snapshot, "🌲", "", default).Single().Start == 2, "panel source range");
        var json = SyntaxExtension.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "json.notepads-extension.json")));
        Check(json.Id == "notepads.json" && json.Commands.Count == 2 && json.Grammars.Count == 1, "bundled JSON upgrade");
        var disabled = new GrammarRegistry(new[] {json, log}.Where(e => e.Id != json.Id));
        Check(!disabled.Extensions.Contains(json) && disabled.FindGrammar(".json") == null, "disabled JSON removes both contributions");

        Reject(sample.Replace("text.wrap", "unknown"));
        Reject(sample.Replace("text.wrap", "panel.open"));
        Reject(sample.Replace("\"strong\"", "\"bad command\""));
        Reject(sample.Replace(extension.Version, "1.00.0"));
        Reject("{\"apiVersion\":2,\"id\":\"x\",\"displayName\":\"X\",\"version\":\"1.0.0\",\"contributes\":{}}");
        Console.WriteLine("Extension command, panel and stale-result tests passed.");
    }

    private static void Reject(string text)
    {
        try { SyntaxExtension.Parse(text); }
        catch (FormatException) { return; }
        throw new InvalidOperationException("Accepted invalid v2 manifest: " + text);
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }
}
