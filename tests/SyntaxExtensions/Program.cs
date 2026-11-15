using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Notepads.SyntaxExtensions;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunTests();
            return 0;
        }
        if (args.Length == 2)
            return ManifestPreview.Run(args[0], args[1]);

        Console.Error.WriteLine("Usage: SyntaxExtensions.Tests [<manifest.json> <sample-file>]");
        return 2;
    }

    private static void RunTests()
    {
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "json.notepads-extension.json"));
        var extension = SyntaxExtension.Parse(manifest);
        Check(extension.Id == "notepads.json" && extension.DisplayName == "JSON", "identity");
        var grammar = extension.Grammars.Single();
        Check(grammar.FileExtensions.SequenceEqual(new[] { ".json" }), "file associations");

        ExpectTokens(grammar, "{\r\n \"🔧\\\"x\" : [true, false, null, -1.2e+3, 0, \"true: 12\"]\n}",
            "punctuation|{", "property|\"🔧\\\"x\"", "punctuation|:", "punctuation|[",
            "keyword|true", "punctuation|,", "keyword|false", "punctuation|,", "keyword|null",
            "punctuation|,", "number|-1.2e+3", "punctuation|,", "number|0", "punctuation|,",
            "string|\"true: 12\"", "punctuation|]", "punctuation|}");
        ExpectTokens(grammar, "\"partial true 123\\", "string|\"partial true 123\\");
        ExpectTokens(grammar, "❄️ \"bad\r\n null", "string|\"bad", "keyword|null");
        ExpectTokens(grammar, "\"abc\\\\\"\r : 0", "property|\"abc\\\\\"", "punctuation|:", "number|0");
        ExpectTokens(grammar, "trueish 2nd 012 tailfalse");
        ExpectTokens(grammar, string.Empty);

        var other = SyntaxExtension.Parse(Manifest(
            new[] { ".Example" }, Rule("comment", "#[^\r\n]*"), Rule("keyword", "key"), Rule("string", "[a-z]+")));
        Check(other.Grammars[0].FileExtensions[0] == ".example", "suffix normalization");
        ExpectTokens(other.Grammars[0], "abc key #key\nkey", "string|abc", "keyword|key", "comment|#key", "keyword|key");

        CheckRegistry(extension);
        EditorApiTests.Run();
        JsonToolsTests.Run();
        LogLensTests.Run();
        PackageTests.Run();

        Reject("{}", "incomplete manifest");
        Reject("{", "malformed manifest");
        Reject(manifest.Replace("notepads.json", @"\uD800"), "invalid UTF-16 escape");
        Reject(manifest.Replace("notepads.json", "\uD800"), "invalid UTF-16 input");
        Reject(manifest.Replace("\"apiVersion\": 2", "\"apiVersion\": 999"), "future apiVersion");
        Reject(manifest.Replace("notepads.json", "../plugin"), "invalid id");
        Reject(Manifest(new[] { "json" }, Rule("string", "a")), "invalid suffix");
        Reject(Manifest(new[] { ".Ab", ".aB" }, Rule("string", "a")), "duplicate suffix");
        Reject(Manifest(new[] { ".a" }, Rule("unknown", "a")), "unknown scope");
        Reject(Manifest(new[] { ".a" }, Rule("string", "a+(")), "invalid regex");
        Reject(Manifest(new[] { ".a" }, Rule("string", "a*")), "empty rule");
        Reject(Manifest(new[] { ".a" }, Rule("string", new string('x', 1025))), "oversized rule");
        Reject(new string('x', SyntaxExtension.MaxManifestLength + 1), "oversized manifest");
        var zeroWidth = SyntaxExtension.Parse(Manifest(new[] { ".a" }, Rule("string", "(?=a)"))).Grammars[0];
        ExpectTokens(zeroWidth, "a");
        Check(grammar.Tokenize(new string('0', 1024 * 1024 + 1), CancellationToken.None).Count == 0, "oversized file");
        Check(grammar.Tokenize(new string(',', 24001), CancellationToken.None).Count == 0, "token budget");
        bool cancelled = false;
        try
        {
            grammar.Tokenize("{}", new CancellationToken(true));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        Check(cancelled, "cancellation");
        Console.WriteLine("Syntax extension manifest and tokenizer tests passed.");
    }

    private static void CheckRegistry(SyntaxExtension bundled)
    {
        string customManifest = Manifest(new[] { ".JSON", ".test" }, Rule("keyword", "foo"));
        var user = SyntaxExtension.Parse(customManifest.Replace("Example", "Z custom"));
        var partial = SyntaxExtension.Parse(customManifest.Replace("tests.example", "another.plugin")
            .Replace("Example", "A custom").Replace(".test", ".extra"));
        var registry = new GrammarRegistry(new[] { user, partial, bundled });
        Check(registry.FindGrammar(".jSoN") == user.Grammars[0], "first suffix wins, ignoring case");
        Check(registry.FindGrammar(".test") == user.Grammars[0], "first plugin's second suffix");
        Check(registry.FindGrammar(".extra") == partial.Grammars[0], "unclaimed suffix still registered");
        Check(registry.FindGrammar(null) == null && registry.FindGrammar("") == null &&
            registry.FindGrammar(".txt") == null, "unknown or missing suffix");
        Check(registry.Extensions.SequenceEqual(new[] { partial, bundled, user }), "stable display order");

        var replacement = SyntaxExtension.Parse(Manifest(new[] { ".replacement" }, Rule("string", "x"))
            .Replace("tests.example", bundled.Id));
        var overridden = new GrammarRegistry(new[] { replacement, bundled });
        Check(overridden.Extensions.SequenceEqual(new[] { replacement }), "ignore duplicate extension id");
        Check(overridden.FindGrammar(".replacement") == replacement.Grammars[0] &&
            overridden.FindGrammar(".json") == null, "skip entire shadowed manifest");
        var restored = new GrammarRegistry(new[] { bundled });
        Check(restored.FindGrammar(".json") == bundled.Grammars[0] && restored.FindGrammar(".replacement") == null,
            "removing override restores bundled grammar");
    }

    private static object Rule(string scope, string match) => new { scope, match };

    private static string Manifest(string[] fileExtensions, params object[] rules)
    {
        return JsonSerializer.Serialize(new
        {
            apiVersion = 1,
            id = "tests.example",
            displayName = "Example",
            contributes = new { grammars = new[] { new { fileExtensions, rules } } }
        });
    }

    private static void Reject(string json, string testName)
    {
        try
        {
            SyntaxExtension.Parse(json);
        }
        catch (FormatException)
        {
            return;
        }
        throw new InvalidOperationException("Accepted " + testName);
    }

    private static void ExpectTokens(SyntaxGrammar grammar, string source, params string[] expected)
    {
        var actual = new List<string>();
        int lastEnd = 0;
        foreach (var token in grammar.Tokenize(source, CancellationToken.None))
        {
            Check(token.Start >= lastEnd && token.Length > 0, "overlapping or empty token");
            lastEnd = token.Start + token.Length;
            actual.Add(token.Scope + "|" + source.Substring(token.Start, token.Length));
        }
        Check(actual.SequenceEqual(expected), "tokens for " + source + ": " + string.Join(", ", actual));
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
    }
}
