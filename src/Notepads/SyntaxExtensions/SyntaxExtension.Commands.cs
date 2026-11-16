namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    internal sealed partial class SyntaxExtension
    {
        private static string ReadId(JsonElement entry)
        {
            var id = ReadString(entry, "id", 80);
            if (!IdPattern.IsMatch(id)) throw new FormatException(ExtensionMessages.Get("InvalidId"));
            return id;
        }

        private static readonly Regex LanguageTag = new Regex(@"\A[a-zA-Z]{2,8}(?:-[a-zA-Z0-9]{1,8})*\z");

        private static string ReadLabel(JsonElement entry, string field, string language)
        {
            var label = ReadString(entry, field, 100);
            if (label.Any(char.IsControl)) throw new FormatException(ExtensionMessages.Get("LabelControl", field));
            if (!entry.TryGetProperty(field + "Translations", out var translations)) return label;
            if (translations.ValueKind != JsonValueKind.Object)
                throw new FormatException(ExtensionMessages.Get("InvalidTranslations", field));

            var localized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in translations.EnumerateObject())
            {
                var text = item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : null;
                if (localized.Count >= 64 || item.Name.Length > 85 || !LanguageTag.IsMatch(item.Name) ||
                    localized.ContainsKey(item.Name) || string.IsNullOrWhiteSpace(text) || text.Length > 100 || text.Any(char.IsControl))
                    throw new FormatException(ExtensionMessages.Get("InvalidTranslations", field));
                localized.Add(item.Name, text);
            }

            while (!string.IsNullOrEmpty(language))
            {
                if (localized.TryGetValue(language, out var text)) return text;
                int separator = language.LastIndexOf('-');
                if (separator < 0) break;
                language = language.Substring(0, separator);
            }
            return label;
        }

        private static string ReadVersion(JsonElement root)
        {
            var value = ReadString(root, "version", 32);
            if (!System.Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0 ||
                version.ToString(3) != value)
                throw new FormatException(ExtensionMessages.Get("VersionNumber"));
            return value;
        }

        private static IEnumerable<JsonElement> OptionalArray(JsonElement parent, string key, int limit)
        {
            if (!parent.TryGetProperty(key, out _)) yield break;
            foreach (var entry in ReadArray(parent, key, limit)) yield return entry;
        }

        private static Dictionary<string, ExtensionPanel> ReadPanels(JsonElement contributes, string language)
        {
            var panels = new Dictionary<string, ExtensionPanel>(StringComparer.Ordinal);
            foreach (var entry in OptionalArray(contributes, "panels", 16))
            {
                var id = ReadId(entry);
                var handler = ReadString(entry, "handler", 80);
                if (handler != "log.filter" || panels.ContainsKey(id))
                    throw new FormatException(ExtensionMessages.Get("PanelHandler"));
                panels.Add(id, new ExtensionPanel(id, ReadLabel(entry, "title", language), handler));
            }
            return panels;
        }

        private static List<ExtensionCommand> ReadCommands(JsonElement contributes, string language)
        {
            var panels = ReadPanels(contributes, language);
            var commands = new List<ExtensionCommand>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in OptionalArray(contributes, "commands", 32))
            {
                var id = ReadId(entry);
                if (!ids.Add(id)) throw new FormatException(ExtensionMessages.Get("DuplicateCommand", id));
                var handler = ReadString(entry, "handler", 80);
                ExtensionPanel panel = null;
                string prefix = null, suffix = null;
                switch (handler)
                {
                    case "panel.open":
                        if (!panels.TryGetValue(ReadString(entry, "panel", 80), out panel))
                            throw new FormatException(ExtensionMessages.Get("UnknownPanel"));
                        break;
                    case "text.wrap":
                        prefix = Required(entry, "prefix", JsonValueKind.String).GetString();
                        suffix = Required(entry, "suffix", JsonValueKind.String).GetString();
                        if (prefix.Length + suffix.Length == 0 || prefix.Length + suffix.Length > 512 ||
                            prefix.Any(char.IsControl) || suffix.Any(char.IsControl))
                            throw new FormatException(ExtensionMessages.Get("WrapDelimiters"));
                        break;
                    case "json.format":
                    case "json.minify":
                        break;
                    default:
                        throw new FormatException(ExtensionMessages.Get("UnknownCommand", handler));
                }

                var suffixes = new List<string>();
                foreach (var element in OptionalArray(entry, "fileExtensions", 32))
                {
                    var value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                    if (value == null || !SuffixPattern.IsMatch(value) || suffixes.Contains(value, StringComparer.OrdinalIgnoreCase))
                        throw new FormatException(ExtensionMessages.Get("CommandSuffix"));
                    suffixes.Add(value);
                }
                commands.Add(new ExtensionCommand(id, ReadLabel(entry, "title", language), handler, suffixes, panel, prefix, suffix));
            }
            return commands;
        }
    }
}
