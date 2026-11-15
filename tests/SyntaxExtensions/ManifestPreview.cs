using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Notepads.SyntaxExtensions;

internal static class ManifestPreview
{
    private static readonly UTF8Encoding Utf8 = new UTF8Encoding(true, true);

    internal static int Run(string manifestPath, string samplePath)
    {
        try
        {
            var extension = SyntaxExtension.Parse(ReadManifest(manifestPath));
            var registry = new GrammarRegistry(new[] { extension });
            var grammar = registry.FindGrammar(Path.GetExtension(samplePath));
            if (grammar == null)
                throw new FormatException($"Extension '{extension.Id}' has no grammar for '{Path.GetFileName(samplePath)}'.");

            // RichEdit represents CRLF and LF as a single CR in the text buffer.
            string sample;
            using (var reader = new StreamReader(samplePath, Utf8, detectEncodingFromByteOrderMarks: false))
                sample = reader.ReadToEnd().Replace("\r\n", "\r").Replace('\n', '\r');

            var tokens = grammar.Tokenize(sample, CancellationToken.None).Select(token => new
            {
                start = token.Start,
                length = token.Length,
                scope = token.Scope,
                text = sample.Substring(token.Start, token.Length)
            });
            Console.WriteLine(JsonSerializer.Serialize(tokens));
            return 0;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                   ex is ArgumentException || ex is FormatException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string ReadManifest(string path)
    {
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            var bytes = reader.ReadBytes(SyntaxExtension.MaxManifestLength + 1);
            if (bytes.Length > SyntaxExtension.MaxManifestLength)
                throw new FormatException("Extension manifest exceeds 64 KiB.");
            int start = bytes.AsSpan().StartsWith(Utf8.Preamble) ? Utf8.Preamble.Length : 0;
            return Utf8.GetString(bytes.AsSpan(start));
        }
    }
}
