namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;

    internal sealed class SyntaxExtension
    {
        public const int MaxManifestLength = 65536;
        private static readonly Regex IdPattern = new Regex(@"\A[a-z0-9][a-z0-9._-]{0,79}\z");
        private static readonly Regex SuffixPattern = new Regex(@"\A\.[a-zA-Z0-9][a-zA-Z0-9_+-]{0,23}\z");
        private static readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            "property", "string", "number", "keyword", "punctuation", "comment"
        };

        private SyntaxExtension(string id, string displayName, List<SyntaxGrammar> grammars)
        {
            Id = id;
            DisplayName = displayName;
            Grammars = grammars.AsReadOnly();
        }

        public string Id { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SyntaxGrammar> Grammars { get; }

        public static SyntaxExtension Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxManifestLength)
                throw new FormatException("Extension manifest is empty or too large.");

            try
            {
                using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10 }))
                {
                    var root = document.RootElement;
                    var api = Required(root, "apiVersion", JsonValueKind.Number);
                    if (!api.TryGetInt32(out int version) || version != 1)
                        throw new FormatException("Unsupported extension apiVersion. Expected 1.");

                    var id = ReadString(root, "id", 80);
                    if (!IdPattern.IsMatch(id))
                        throw new FormatException("Invalid extension id.");

                    var name = ReadString(root, "displayName", 100);
                    foreach (char character in name)
                    {
                        if (char.IsControl(character))
                            throw new FormatException("Extension displayName contains a control character.");
                    }

                    var contributes = Required(root, "contributes", JsonValueKind.Object);
                    var grammars = new List<SyntaxGrammar>();
                    var claimedSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in ReadArray(contributes, "grammars", 16))
                    {
                        var suffixes = new List<string>();
                        foreach (var suffix in ReadArray(item, "fileExtensions", 32))
                        {
                            var value = suffix.ValueKind == JsonValueKind.String ? suffix.GetString() : null;
                            if (value == null || !SuffixPattern.IsMatch(value) || !claimedSuffixes.Add(value))
                                throw new FormatException("File extensions must be unique suffixes such as .json.");
                            suffixes.Add(value.ToLowerInvariant());
                        }

                        var rules = new List<SyntaxRule>();
                        foreach (var rule in ReadArray(item, "rules", 32))
                        {
                            string scope = ReadString(rule, "scope", 32);
                            if (!Scopes.Contains(scope))
                                throw new FormatException("Unknown token scope: " + scope);
                            rules.Add(new SyntaxRule(scope, ReadString(rule, "match", 1024)));
                        }
                        grammars.Add(new SyntaxGrammar(suffixes, rules));
                    }
                    return new SyntaxExtension(id, name, grammars);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new FormatException("Invalid extension JSON.", ex);
            }
        }

        private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
                throw new FormatException("Missing or invalid manifest field: " + name);
            return value;
        }

        private static JsonElement.ArrayEnumerator ReadArray(JsonElement parent, string name, int limit)
        {
            var value = Required(parent, name, JsonValueKind.Array);
            if (value.GetArrayLength() == 0 || value.GetArrayLength() > limit)
                throw new FormatException("Invalid number of entries in " + name + ".");
            return value.EnumerateArray();
        }

        private static string ReadString(JsonElement parent, string name, int limit)
        {
            string value = Required(parent, name, JsonValueKind.String).GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > limit)
                throw new FormatException("Empty or oversized manifest field: " + name);
            return value;
        }
    }

    internal sealed class SyntaxRule
    {
        public SyntaxRule(string scope, string pattern)
        {
            Scope = scope;
            try
            {
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(40));
                if (Pattern.IsMatch(string.Empty))
                    throw new FormatException("A syntax rule must consume text.");
            }
            catch (ArgumentException ex)
            {
                throw new FormatException("Invalid regular expression for scope " + scope + ".", ex);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new FormatException("Regular expression timed out during validation.", ex);
            }
        }

        public string Scope { get; }
        public Regex Pattern { get; }
    }

    internal sealed class SyntaxGrammar
    {
        private readonly IReadOnlyList<SyntaxRule> _rules;

        internal SyntaxGrammar(List<string> fileExtensions, List<SyntaxRule> rules)
        {
            FileExtensions = fileExtensions.AsReadOnly();
            _rules = rules.AsReadOnly();
        }

        public IReadOnlyList<string> FileExtensions { get; }

        public IReadOnlyList<SyntaxToken> Tokenize(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(text) || text.Length > 1024 * 1024)
                return Array.Empty<SyntaxToken>();

            var elapsed = Stopwatch.StartNew();
            var tokens = new List<SyntaxToken>();
            var matches = new Match[_rules.Count];
            int position = 0;
            try
            {
                while (position < text.Length)
                {
                    int winner = -1;
                    for (int i = 0; i < _rules.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (elapsed.ElapsedMilliseconds > 300)
                            return Array.Empty<SyntaxToken>();

                        // A later match is still valid after another rule consumed an earlier token.
                        var match = matches[i];
                        if (match == null || (match.Success && match.Index < position))
                        {
                            match = _rules[i].Pattern.Match(text, position);
                            matches[i] = match;
                        }
                        if (match.Success && (winner == -1 || match.Index < matches[winner].Index))
                            winner = i;
                        // No later rule can beat a match at the cursor. Ties use manifest order.
                        if (winner >= 0 && matches[winner].Index == position)
                            break;
                    }
                    if (winner == -1)
                        break;

                    var chosen = matches[winner];
                    if (chosen.Length == 0 || tokens.Count >= 24000)
                        return Array.Empty<SyntaxToken>();
                    tokens.Add(new SyntaxToken(chosen.Index, chosen.Length, _rules[winner].Scope));
                    position = chosen.Index + chosen.Length;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return Array.Empty<SyntaxToken>();
            }
            return tokens;
        }
    }

    internal struct SyntaxToken
    {
        public SyntaxToken(int start, int length, string scope)
        {
            Start = start;
            Length = length;
            Scope = scope;
        }

        public int Start { get; }
        public int Length { get; }
        public string Scope { get; }
    }
}
