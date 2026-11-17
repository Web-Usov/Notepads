using System;
using System.IO;
using System.Linq;
using Notepads.SyntaxExtensions;

internal static class EditorApiTests
{
    public static void Run()
    {
        var sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wrap.notepads-extension.json"));
        CheckTranslations(sample);
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
        CheckFeatureExtensions(sample);

        Reject(sample.Replace("text.wrap", "unknown"));
        Reject(sample.Replace("text.wrap", "panel.open"));
        Reject(sample.Replace("\"strong\"", "\"bad command\""));
        Reject(sample.Replace(extension.Version, "1.00.0"));
        Reject("{\"apiVersion\":2,\"id\":\"x\",\"displayName\":\"X\",\"version\":\"1.0.0\",\"contributes\":{}}");
        Console.WriteLine("Extension command, panel and stale-result tests passed.");
    }

    private static void CheckFeatureExtensions(string commandManifest)
    {
        var markdownManifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "markdown-preview.notepads-extension.json"));
        var diffManifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "changes-diff.notepads-extension.json"));
        var markdown = SyntaxExtension.Parse(markdownManifest, "en-US");
        var changes = SyntaxExtension.Parse(diffManifest, "en-US");

        Check(markdown.Id == EditorFeatures.MarkdownPreviewExtensionId &&
              markdown.Features.SequenceEqual(new[] { EditorFeatures.MarkdownPreview }) &&
              markdown.Commands.Count == 0 && markdown.Grammars.Count == 0, "Markdown feature-only extension");
        Check(changes.Id == EditorFeatures.ChangesDiffExtensionId &&
              changes.Features.SequenceEqual(new[] { EditorFeatures.ChangesDiff }) &&
              changes.Commands.Count == 0 && changes.Grammars.Count == 0, "Changes Diff feature-only extension");
        Check(SyntaxExtension.Parse(markdownManifest, "ru-RU").DisplayName == "Предпросмотр Markdown" &&
              SyntaxExtension.Parse(diffManifest, "ru-RU").DisplayName == "Сравнение изменений",
              "feature extension localization");

        var both = new GrammarRegistry(new[] { markdown, changes });
        Check(both.HasFeature(EditorFeatures.MarkdownPreview) &&
              both.HasFeature(EditorFeatures.ChangesDiff), "both features available by default");
        var withoutMarkdown = new GrammarRegistry(new[] { changes });
        var withoutChanges = new GrammarRegistry(new[] { markdown });
        Check(!withoutMarkdown.HasFeature(EditorFeatures.MarkdownPreview) &&
              withoutMarkdown.HasFeature(EditorFeatures.ChangesDiff), "independent Markdown disable");
        Check(!withoutChanges.HasFeature(EditorFeatures.ChangesDiff) &&
              withoutChanges.HasFeature(EditorFeatures.MarkdownPreview), "independent diff disable");
        Check(!new GrammarRegistry(Array.Empty<SyntaxExtension>()).HasFeature(EditorFeatures.MarkdownPreview),
              "missing bundled feature fails closed");

        // A local override wins by ID, so removing its contribution disables
        // only that feature, just as with existing grammar overrides.
        var shadow = SyntaxExtension.Parse(commandManifest.Replace("example.markdown-wrap",
            EditorFeatures.MarkdownPreviewExtensionId));
        var overridden = new GrammarRegistry(new[] { shadow, markdown, changes });
        Check(!overridden.HasFeature(EditorFeatures.MarkdownPreview) &&
              overridden.HasFeature(EditorFeatures.ChangesDiff), "feature override takes precedence");

        Reject(markdownManifest.Replace("plainix.markdown-preview", "third-party.preview"));
        Reject(markdownManifest.Replace("markdown.preview", "changes.diff"));
        Reject(markdownManifest.Replace("markdown.preview", "unknown.feature"));
        Reject(markdownManifest.Replace("\"features\": [",
            "\"features\": [{\"handler\":\"markdown.preview\"},"));
    }

    private static void CheckTranslations(string sample)
    {
        var neutral = SyntaxExtension.Parse(sample, "ru-BY");
        var english = SyntaxExtension.Parse(sample, "fr");
        Check(neutral.Id == english.Id && neutral.Version == english.Version, "locale-independent package identity");
        Check(neutral.DisplayName == "Выделение в Markdown" && neutral.Commands.Single().Title == "Выделить полужирным",
            "Russian parent-language fallback in a third-party manifest");
        Check(english.DisplayName == "Markdown emphasis" && english.Commands.Single().Title == "Wrap in bold",
            "default labels for untranslated languages");
        var specific = sample.Replace("\"ru\": \"Выделение в Markdown\"", "\"ru\": \"Generic\", \"ru-RU\": \"Regional\"");
        Check(SyntaxExtension.Parse(specific, "RU-ru").DisplayName == "Regional", "case-insensitive exact locale wins");
        var log = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "log-lens.notepads-extension.json"));
        var open = SyntaxExtension.Parse(log, "ru-RU").Commands.Single();
        Check(open.Title == "Открыть просмотр журналов" && open.Panel.Title == "Просмотр журналов", "localized panel title");
        Reject(sample.Replace("\"ru\": \"Выделение в Markdown\"", "\"ru\": \"One\", \"RU\": \"Two\""));
        Reject(sample.Replace("\"ru\": \"Выделение в Markdown\"", "\"ru\": 12"));
        Reject(sample.Replace("Выделить полужирным", "a\\nb"));
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
