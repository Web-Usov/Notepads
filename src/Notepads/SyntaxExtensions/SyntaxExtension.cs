namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;

    internal sealed partial class SyntaxExtension
    {
        public const int MaxManifestLength = 65536;
        private static readonly Regex IdPattern = new Regex(@"\A[a-z0-9][a-z0-9._-]{0,79}\z");
        private static readonly Regex SuffixPattern = new Regex(@"\A\.[a-zA-Z0-9][a-zA-Z0-9_+-]{0,23}\z");
        private static readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.Ordinal)
        {
            "property", "string", "number", "keyword", "punctuation", "comment"
        };

        private SyntaxExtension(string id, string displayName, List<SyntaxGrammar> grammars, string version, List<ExtensionCommand> commands)
        {
            Version = version;
            Commands = commands.AsReadOnly();
            Id = id;
            DisplayName = displayName;
            Grammars = grammars.AsReadOnly();
        }

        public string Id { get; }
        public string Version { get; }
        public IReadOnlyList<ExtensionCommand> Commands { get; }
        public string DisplayName { get; }
        public IReadOnlyList<SyntaxGrammar> Grammars { get; }

        public static SyntaxExtension Parse(string json)
        {
#if WINDOWS_UWP
            // Use the same language preference as the packaged UI, including its Settings override.
            var languages = Windows.Globalization.ApplicationLanguages.Languages;
            return Parse(json, languages.Count > 0 ? languages[0] : string.Empty);
#else
            return Parse(json, CultureInfo.CurrentUICulture.Name);
#endif
        }

        internal static SyntaxExtension Parse(string json, string language)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxManifestLength)
                throw new FormatException(ExtensionMessages.Get("ManifestEmpty"));

            try
            {
                using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 10 }))
                {
                    var root = document.RootElement;
                    var api = Required(root, "apiVersion", JsonValueKind.Number);
                    if (!api.TryGetInt32(out int version) || (version != 1 && version != 2))
                        throw new FormatException(ExtensionMessages.Get("ApiVersion"));

                    var id = ReadId(root);
                    var name = ReadLabel(root, "displayName", language);
                    var packageVersion = version == 2 ? ReadVersion(root) : "1.0.0";
                    var contributes = Required(root, "contributes", JsonValueKind.Object);
                    var grammars = new List<SyntaxGrammar>();
                    var claimedSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (version == 1) Required(contributes, "grammars", JsonValueKind.Array);
                    foreach (var item in OptionalArray(contributes, "grammars", 16))
                    {
                        var suffixes = new List<string>();
                        foreach (var suffix in ReadArray(item, "fileExtensions", 32))
                        {
                            var value = suffix.ValueKind == JsonValueKind.String ? suffix.GetString() : null;
                            if (value == null || !SuffixPattern.IsMatch(value) || !claimedSuffixes.Add(value))
                                throw new FormatException(ExtensionMessages.Get("GrammarSuffixes"));
                            suffixes.Add(value.ToLowerInvariant());
                        }

                        var rules = new List<SyntaxRule>();
                        foreach (var rule in ReadArray(item, "rules", 32))
                        {
                            string scope = ReadString(rule, "scope", 32);
                            if (!Scopes.Contains(scope))
                                throw new FormatException(ExtensionMessages.Get("TokenScope", scope));
                            rules.Add(new SyntaxRule(scope, ReadString(rule, "match", 1024)));
                        }
                        grammars.Add(new SyntaxGrammar(suffixes, rules));
                    }
                    var commands = version == 2 ? ReadCommands(contributes, language) : new List<ExtensionCommand>();
                    if (grammars.Count == 0 && commands.Count == 0)
                        throw new FormatException(ExtensionMessages.Get("MissingContributions"));
                    return new SyntaxExtension(id, name, grammars, packageVersion, commands);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new FormatException(ExtensionMessages.Get("InvalidJsonManifest"), ex);
            }
        }

        private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
                throw new FormatException(ExtensionMessages.Get("ManifestField", name));
            return value;
        }

        private static JsonElement.ArrayEnumerator ReadArray(JsonElement parent, string name, int limit)
        {
            var value = Required(parent, name, JsonValueKind.Array);
            if (value.GetArrayLength() == 0 || value.GetArrayLength() > limit)
                throw new FormatException(ExtensionMessages.Get("EntryCount", name));
            return value.EnumerateArray();
        }

        private static string ReadString(JsonElement parent, string name, int limit)
        {
            string value = Required(parent, name, JsonValueKind.String).GetString();
            if (string.IsNullOrWhiteSpace(value) || value.Length > limit)
                throw new FormatException(ExtensionMessages.Get("EmptyField", name));
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
                // \G anchors to the search start passed to Regex.Match, unlike ordinary matches.
                // A previous match (including a failed one) cannot be reused after the cursor moves.
                DependsOnSearchStart = pattern.IndexOf(@"\\G", StringComparison.Ordinal) >= 0;
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(40));
                if (Pattern.IsMatch(string.Empty))
                    throw new FormatException(ExtensionMessages.Get("EmptySyntaxRule"));
            }
            catch (ArgumentException ex)
            {
                throw new FormatException(ExtensionMessages.Get("InvalidPattern", scope), ex);
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new FormatException(ExtensionMessages.Get("PatternTimeout"), ex);
            }
        }

        public string Scope { get; }
        public Regex Pattern { get; }
        public bool DependsOnSearchStart { get; }
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
                        if (match == null || _rules[i].DependsOnSearchStart || (match.Success && match.Index < position))
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
