namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;

    internal static class LogLens
    {
        private static readonly char[] LineEndings = { '\r', '\n' };

        // Accept a level at the start, after a timestamp, in brackets or in a NestJS header.
        // A word such as "error" in ordinary message text is not a severity field.
        private static readonly Regex AnsiColor = new Regex(@"\x1B\[[0-9;]*m", RegexOptions.CultureInvariant);
        private static readonly Regex HeaderLevel = new Regex(
            @"(?:^\s*|\[\s*|^\d{4}-\d\d-\d\d[T ][\d:.+Z-]+\s+|^\[Nest\]\s+\d+\s+-\s+[^\r\n]{0,64}?\s+)" +
            @"(?<level>TRACE|VERBOSE|DEBUG|INFO|LOG|WARN(?:ING)?|ERROR|FATAL|CRITICAL)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IReadOnlyList<EditorPanelRow> Query(
            EditorSnapshot snapshot, string filter, string severity, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var results = new List<EditorPanelRow>();
            var text = snapshot.Text;
            filter = filter ?? string.Empty;
            string level = string.IsNullOrWhiteSpace(severity) || string.Equals(severity, "All", StringComparison.OrdinalIgnoreCase)
                ? null : CanonicalLevel(severity);
            for (int start = 0, number = 1; start < text.Length; number++)
            {
                token.ThrowIfCancellationRequested();
                int end = text.IndexOfAny(LineEndings, start);
                if (end < 0) end = text.Length;
                if (end > start)
                {
                    string line = text.Substring(start, end - start);
                    if (Matches(line, filter, level))
                        results.Add(new EditorPanelRow(number, start, line.Length, line));
                }
                start = end + 1;
                if (end < text.Length && text[end] == '\r' && start < text.Length && text[start] == '\n')
                    start++;
            }
            return results;
        }

        private static bool Matches(string line, string filter, string level)
        {
            if (filter.Length == 0 && level == null) return true;

            string requestId = null;
            string eventLevel = null;
            string payload = line;
            ReadEvent(line, ref requestId, ref eventLevel, out string nestedLog);
            if (nestedLog != null)
            {
                // Docker's json-file driver wraps stdout/stderr in a JSON "log" string.
                payload = nestedLog;
                ReadEvent(payload, ref requestId, ref eventLevel, out _);
            }

            if (filter.Length != 0 && (requestId != null
                ? !string.Equals(requestId, filter, StringComparison.Ordinal)
                : payload.IndexOf(filter, StringComparison.Ordinal) < 0))
                return false;

            if (level == null) return true;
            if (eventLevel == null)
            {
                // Structured text may mention another severity inside its message.
                // Match only header syntax and keep the work bounded for long lines.
                int headerLength = Math.Min(payload.Length, 160);
                var match = payload.IndexOf('\x1b', 0, headerLength) < 0
                    ? HeaderLevel.Match(payload, 0, headerLength)
                    : HeaderLevel.Match(AnsiColor.Replace(payload.Substring(0, headerLength), string.Empty));
                if (!match.Success) return false;
                eventLevel = match.Groups["level"].Value;
            }
            return string.Equals(level, CanonicalLevel(eventLevel), StringComparison.OrdinalIgnoreCase);
        }

        private static void ReadEvent(string source, ref string requestId, ref string level, out string nestedLog)
        {
            nestedLog = null;
            int start = 0;
            while (start < source.Length && char.IsWhiteSpace(source[start])) start++;
            if (start == source.Length || source[start] != '{') return;

            try
            {
                using (var document = JsonDocument.Parse(source))
                {
                    var record = document.RootElement;
                    string id = Field(record, "requestId");
                    string severity = Field(record, "level");
                    if (record.TryGetProperty("log", out var log) && log.ValueKind == JsonValueKind.String)
                        nestedLog = log.GetString();
                    requestId = id ?? requestId;
                    level = severity ?? level;
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidOperationException)
            {
                // GetString also rejects invalid Unicode escapes such as an unpaired surrogate.
                // Keep incomplete or malformed input searchable as the original text line.
            }
        }

        private static string Field(JsonElement record, string name)
        {
            if (!record.TryGetProperty(name, out var field)) return null;
            if (field.ValueKind == JsonValueKind.String) return field.GetString();
            return field.ValueKind == JsonValueKind.Number ? field.GetRawText() : null;
        }

        private static string CanonicalLevel(string level)
        {
            switch (level.ToUpperInvariant())
            {
                case "10": case "VERBOSE": return "TRACE";
                case "20": return "DEBUG";
                case "30": case "LOG": return "INFO";
                case "40": case "WARNING": return "WARN";
                case "50": return "ERROR";
                case "60": case "CRITICAL": return "FATAL";
                default: return level;
            }
        }
    }
}
