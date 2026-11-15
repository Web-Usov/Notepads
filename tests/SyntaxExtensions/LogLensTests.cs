using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Notepads.SyntaxExtensions;

internal static class LogLensTests
{
    public static void Run()
    {
        string sample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "interleaved.log.txt"));
        Expect(sample, "req-41", "", 1, 3, 7);
        Expect(sample, "req-41", "ERROR", 3);
        Expect(sample, "", "warn", 6);
        Expect(sample, "req-42", "warning", 6);
        Expect(sample, "", "info", 1, 2, 7);
        Expect(sample, "", "All", 1, 2, 3, 4, 5, 6, 7);
        Expect(sample, "Database.connect", null, 4);
        Expect(sample, "REQ-41", null);
        Expect(sample, "missing", null);
        Expect("", "", null);
        Expect("\r\n", "", null);
        Expect("😺\rINFO repeated\r\n  at trace\n\nINFO repeated", "repeated", "Info", 2, 5);
        Expect("plain .*\nINFO regular\n{\"requestId\":\".*\"}", ".*", null, 1, 3);
        Expect("{malformed req-1\n{\"requestId\":17,\"level\":40}\n{\"level\":\"debug\"}", "req-1", "", 1);
        Expect("{\"requestId\":17,\"level\":40}", "17", "warn", 1);
        Expect("{\"log\":\"[Nest] 2 - 2026-09-01 LOG [HTTP] req-1\\n\"}", "req-1", "info", 1);
        Expect("{\"requestId\":\"\\uD800\",\"msg\":\"literal\"}\nvalid literal", "literal", null, 1, 2);
        Expect("\x1b[32m[Nest] 33 \x1b[39m - 09/01/2026 09:00:00 AM  \x1b[31mERROR\x1b[39m req-41", "req-41", "error", 1);
        Expect("processing an error\n[ERROR] failed\nINFO got ERROR status", "", "error", 2);

        var manyLines = string.Concat(Enumerable.Repeat("{\"requestId\":\"other\",\"level\":50}\n", 28000));
        Expect(manyLines + "{\"requestId\":\"target\",\"level\":50}", "target", "error", 28001);
        try
        {
            LogLens.Query(Document(manyLines), "", "", new CancellationToken(true));
            throw new InvalidOperationException("Log Lens ignored cancellation.");
        }
        catch (OperationCanceledException) { }
    }

    private static EditorSnapshot Document(string text)
    {
        return new EditorSnapshot(Guid.NewGuid(), 1, text, 0, 0);
    }

    private static void Expect(string source, string filter, string severity, params int[] lines)
    {
        var snapshot = Document(source);
        var rows = LogLens.Query(snapshot, filter, severity, CancellationToken.None);
        if (!rows.Select(row => row.LineNumber).SequenceEqual(lines))
            throw new InvalidOperationException("Unexpected Log Lens results for " + filter + " / " + severity + ".");
        var offsets = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
            else if (source[i] != '\r' && source[i] != '\n') continue;
            offsets.Add(i + 1);
        }
        foreach (var row in rows)
        {
            if (row.Start != offsets[row.LineNumber - 1] || row.Text != source.Substring(row.Start, row.Length))
                throw new InvalidOperationException("Log Lens row lost its source range.");
        }
        if (snapshot.Text != source) throw new InvalidOperationException("Log Lens changed the document.");
    }
}
