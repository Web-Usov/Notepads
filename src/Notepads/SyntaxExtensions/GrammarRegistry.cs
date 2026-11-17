namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal sealed class GrammarRegistry
    {
        private readonly Dictionary<string, SyntaxGrammar> _byExtension =
            new Dictionary<string, SyntaxGrammar>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _features = new HashSet<string>(StringComparer.Ordinal);

        // Input order determines priority. Display sorting must not change resolution.
        public GrammarRegistry(IEnumerable<SyntaxExtension> extensions)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var active = new List<SyntaxExtension>();
            foreach (var extension in extensions)
            {
                if (!ids.Add(extension.Id))
                    continue;

                active.Add(extension);
                foreach (var feature in extension.Features)
                    _features.Add(feature);
                foreach (var grammar in extension.Grammars)
                {
                    foreach (var suffix in grammar.FileExtensions)
                    {
                        if (!_byExtension.ContainsKey(suffix))
                            _byExtension.Add(suffix, grammar);
                    }
                }
            }
            Extensions = active.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList().AsReadOnly();
        }

        public IReadOnlyList<SyntaxExtension> Extensions { get; }

        public bool HasFeature(string feature) => feature != null && _features.Contains(feature);

        public SyntaxGrammar FindGrammar(string extension)
        {
            return extension != null && _byExtension.TryGetValue(extension, out var grammar) ? grammar : null;
        }
    }
}
