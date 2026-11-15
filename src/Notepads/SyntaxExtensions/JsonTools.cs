namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Text;
    using System.Text.Json;

    internal static class JsonTools
    {
        private const int MaxInputLength = 1024 * 1024;
        private const int MaxOutputLength = 4 * MaxInputLength;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static EditorCommandResult Execute(EditorSnapshot snapshot, bool minify)
        {
            int start = snapshot.SelectionLength == 0 ? 0 : snapshot.SelectionStart;
            int length = snapshot.SelectionLength == 0 ? snapshot.Text.Length : snapshot.SelectionLength;
            if (length > MaxInputLength)
                return EditorCommandResult.Error(start, 0, "JSON input exceeds 1,048,576 characters.");

            string input = snapshot.Text.Substring(start, length);
            byte[] bytes;
            try
            {
                bytes = Utf8.GetBytes(input);
            }
            catch (EncoderFallbackException ex)
            {
                return ReportError(snapshot.Text, start, length, Math.Max(0, ex.Index), "Invalid Unicode character.");
            }

            // Validate without converting keys or numbers to CLR values. The formatter copies their original spelling.
            try
            {
                var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 128 });
                while (reader.Read()) { }
            }
            catch (JsonException ex)
            {
                int offset = GetErrorOffset(bytes, ex.LineNumber.GetValueOrDefault(), ex.BytePositionInLine.GetValueOrDefault());
                // System.Text.Json appends zero-based byte coordinates relative to the selection.
                int suffix = ex.Message.LastIndexOf(" LineNumber:", StringComparison.Ordinal);
                string reason = suffix < 0 ? ex.Message : ex.Message.Substring(0, suffix).TrimEnd('|', ' ');
                return ReportError(snapshot.Text, start, length, offset, reason);
            }

            string result = RewriteWhitespace(input, minify, GetLineEnding(snapshot.Text));
            if (result == null)
                return EditorCommandResult.Error(start, 0, "Formatted JSON exceeds 4,194,304 characters.");
            return EditorCommandResult.Replace(start, length, result);
        }

        private static int GetErrorOffset(byte[] bytes, long parserLine, long byteColumn)
        {
            // The reader counts each LF, including those in CRLF. Lone CR stays on its current parser line.
            int offset = 0;
            while (parserLine > 0 && offset < bytes.Length)
            {
                if (bytes[offset++] == 10)
                    parserLine--;
            }
            int position = offset + (int)Math.Min(byteColumn, bytes.Length - offset);
            while (position > 0 && position < bytes.Length && (bytes[position] & 0xc0) == 0x80)
                position--;
            return Utf8.GetCharCount(bytes, 0, position);
        }

        private static EditorCommandResult ReportError(string document, int start, int length, int relativeOffset, string reason)
        {
            int offset = start + Math.Min(length, relativeOffset);
            int line = 1, column = 1;
            for (int i = 0; i < offset; i++)
            {
                if (document[i] == '\r' || document[i] == '\n')
                {
                    if (document[i] == '\r' && i + 1 < offset && document[i + 1] == '\n')
                        i++;
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }
            int errorLength = 0;
            if (offset < start + length)
                errorLength = offset + 1 < start + length && char.IsSurrogatePair(document, offset) ? 2 : 1;
            return EditorCommandResult.Error(offset, errorLength, $"JSON error at line {line}, column {column}: {reason}");
        }

        private static string GetLineEnding(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                    return i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
                if (text[i] == '\n')
                    return "\n";
            }
            return "\r"; // RichEdit's native line break; the document's save convention is unchanged.
        }

        private static string RewriteWhitespace(string input, bool minify, string newline)
        {
            var output = new StringBuilder(input.Length);
            int depth = 0;
            char previous = '\0';
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    continue;

                bool closing = c == ']' || c == '}';
                bool afterOpen = previous == '[' || previous == '{';
                if (closing)
                    depth--;

                if (!minify)
                {
                    if ((closing && !afterOpen) || (!closing && (afterOpen || previous == ',')))
                        output.Append(newline).Append(' ', depth * 2);
                    else if (previous == ':')
                        output.Append(' ');
                }

                if (c == '"')
                {
                    int first = i;
                    while (input[++i] != '"')
                    {
                        if (input[i] == '\\') i++;
                    }
                    output.Append(input, first, i - first + 1);
                }
                else
                {
                    output.Append(c);
                }
                if (c == '[' || c == '{')
                    depth++;
                if (output.Length > MaxOutputLength)
                    return null;
                previous = c;
            }
            return output.ToString();
        }
    }
}
