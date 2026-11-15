using System;
using System.Linq;
using Notepads.SyntaxExtensions;

internal static class JsonToolsTests
{
    public static void Run()
    {
        const string number = "900719925474099312345678901234567890";
        const string escaped = "\"A\\u0042\\\"C\\\\D 🔧\"";
        string compact = "{\"same\":" + number + ",\"same\":[-0.0100e+70,true,false,null,{},[]]," + escaped + ":" + escaped + "}";
        string formatted = "{\r  \"same\": " + number + ",\r  \"same\": [\r    -0.0100e+70,\r    true,\r    false," +
            "\r    null,\r    {},\r    []\r  ],\r  " + escaped + ": " + escaped + "\r}";
        foreach (string ending in new[] { "\r", "\n", "\r\n" })
        {
            string document = compact + ending;
            string pretty = formatted.Replace("\r", ending);
            Check(Run(document, false).Text == pretty, "format using the existing newline convention");
            Check(Run(pretty, true).Text == compact, "minify without changing token spelling, order or duplicate keys");
            const string prefix = "notes first";
            string selected = prefix + ending + "[" + number + ", {}]" + ending + "notes last";
            string source = "[" + number + ", {}]";
            var edit = Run(selected, false, prefix.Length + ending.Length, source.Length);
            Check(edit.Start == prefix.Length + ending.Length && edit.Length == source.Length, "limit replacement to selection");
            Check(edit.Text == "[" + ending + "  " + number + "," + ending + "  {}" + ending + "]", "indent selected JSON");

            string before = "notes 🔧" + ending + "prefix ";
            string invalid = "{\"🔧\":[0,?]}";
            var error = Run(before + invalid + " tail", false, before.Length, invalid.Length);
            Check(error.Text == null && error.Start == before.Length + invalid.IndexOf('?') && error.Length == 1,
                "report the absolute UTF-16 error offset after a non-ASCII key");
            Check(error.Message.Contains("line 2, column " + (8 + invalid.IndexOf('?'))), "absolute error location in selected text");

            string badMultiline = "{\"a\":" + ending + "[0," + ending + "!]}";
            var multiline = Run(badMultiline, true);
            Check(multiline.Start == badMultiline.IndexOf('!') && multiline.Message.Contains("line 3, column 1"),
                "navigate invalid input on a later line");
        }
        Check(Run(" \t false \t", true).Text == "false", "boolean root");
        Check(Run("42", false).Text == "42", "unchanged root value");
        Check(Run("{}", false, 1).Text == "{}", "empty selection operates on whole document");
        Check(Run("[1,2]", false).Text == "[\r  1,\r  2\r]", "single-line selection defaults to native RichEdit CR");

        foreach (string invalid in new[] { "", "   ", "{", "[1,]", "true false", "{\"a\":01}", "\"unterminated", "{/*c*/}" })
        {
            var error = Run(invalid, true);
            Check(error.Text == null && !string.IsNullOrEmpty(error.Message), "do not edit invalid or empty input");
        }
        var eof = Run("notes[1", false, 5, 2);
        Check(eof.Start == 7 && eof.Length == 0, "place caret at the end of truncated input");
        var unicode = Run("[\"\uD800\"]", true);
        Check(unicode.Start == 2 && unicode.Text == null, "unpaired surrogate diagnosis");
        var largeOutput = new string('[', 95) + string.Join(",", Enumerable.Repeat("1", 22000)) + new string(']', 95);
        Check(Run(largeOutput, false).Text == null && Run(largeOutput, true).Text == largeOutput, "bound formatting expansion");
        AssertSingleUndoableEdit(compact);
        Console.WriteLine("JSON Tools tests passed.");
    }

    private static void AssertSingleUndoableEdit(string compact)
    {
        const string prefix = "prefix ";
        const string suffix = " suffix";
        string original = prefix + compact + suffix;
        var snapshot = new EditorSnapshot(Guid.NewGuid(), 5, original, prefix.Length, compact.Length);
        var result = JsonTools.Execute(snapshot, false);
        int writes = 0;
        string document = original;
        bool applied = result.Apply(snapshot, snapshot.DocumentId, snapshot.Revision, (start, length, replacement) =>
        {
            writes++;
            document = document.Remove(start, length).Insert(start, replacement);
        }, (start, length, message) => { throw new InvalidOperationException(message); });
        Check(applied && writes == 1 && document == prefix + result.Text + suffix,
            "command sends one edit and preserves the surrounding document");

        var invalid = new EditorSnapshot(snapshot.DocumentId, 6, "[?]", 0, 0);
        int navigations = 0;
        var error = JsonTools.Execute(invalid, false);
        Check(error.Apply(invalid, invalid.DocumentId, invalid.Revision,
            (start, length, text) => { throw new InvalidOperationException("Changed invalid JSON"); },
            (start, length, message) => { if (start == 1 && length == 1) navigations++; }) && navigations == 1,
            "invalid input only reports and navigates");
    }

    private static EditorCommandResult Run(string text, bool minify, int start = 0, int length = 0)
    {
        return JsonTools.Execute(new EditorSnapshot(Guid.Empty, 0, text, start, length), minify);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("JSON Tools: " + name);
    }
}
