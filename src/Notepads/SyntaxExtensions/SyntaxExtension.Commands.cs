namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    internal sealed partial class SyntaxExtension
    {
        private static string ReadId(JsonElement entry)
        {
            var id = ReadString(entry, "id", 80);
            if (!IdPattern.IsMatch(id)) throw new FormatException("Invalid extension or contribution id.");
            return id;
        }

        private static string ReadLabel(JsonElement entry, string field)
        {
            var label = ReadString(entry, field, 100);
            if (label.Any(char.IsControl)) throw new FormatException("Invalid control character in " + field + ".");
            return label;
        }

        private static string ReadVersion(JsonElement root)
        {
            var value = ReadString(root, "version", 32);
            if (!System.Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0 ||
                version.ToString(3) != value)
                throw new FormatException("Expected a numeric major.minor.patch version.");
            return value;
        }

        private static IEnumerable<JsonElement> OptionalArray(JsonElement parent, string key, int limit)
        {
            if (!parent.TryGetProperty(key, out _)) yield break;
            foreach (var entry in ReadArray(parent, key, limit)) yield return entry;
        }

        private static Dictionary<string, ExtensionPanel> ReadPanels(JsonElement contributes)
        {
            var panels = new Dictionary<string, ExtensionPanel>(StringComparer.Ordinal);
            foreach (var entry in OptionalArray(contributes, "panels", 16))
            {
                var id = ReadId(entry);
                var handler = ReadString(entry, "handler", 80);
                if (handler != "log.filter" || panels.ContainsKey(id))
                    throw new FormatException("Unknown panel handler or duplicate panel id.");
                panels.Add(id, new ExtensionPanel(id, ReadLabel(entry, "title"), handler));
            }
            return panels;
        }

        private static List<ExtensionCommand> ReadCommands(JsonElement contributes)
        {
            var panels = ReadPanels(contributes);
            var commands = new List<ExtensionCommand>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in OptionalArray(contributes, "commands", 32))
            {
                var id = ReadId(entry);
                if (!ids.Add(id)) throw new FormatException("Duplicate command id: " + id);
                var handler = ReadString(entry, "handler", 80);
                ExtensionPanel panel = null;
                string prefix = null, suffix = null;
                switch (handler)
                {
                    case "panel.open":
                        if (!panels.TryGetValue(ReadString(entry, "panel", 80), out panel))
                            throw new FormatException("The command refers to an unknown panel.");
                        break;
                    case "text.wrap":
                        prefix = Required(entry, "prefix", JsonValueKind.String).GetString();
                        suffix = Required(entry, "suffix", JsonValueKind.String).GetString();
                        if (prefix.Length + suffix.Length == 0 || prefix.Length + suffix.Length > 512 ||
                            prefix.Any(char.IsControl) || suffix.Any(char.IsControl))
                            throw new FormatException("Use 1–512 printable characters for the prefix and suffix together.");
                        break;
                    case "json.format":
                    case "json.minify":
                        break;
                    default:
                        throw new FormatException("Unknown command handler: " + handler);
                }

                var suffixes = new List<string>();
                foreach (var element in OptionalArray(entry, "fileExtensions", 32))
                {
                    var value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
                    if (value == null || !SuffixPattern.IsMatch(value) || suffixes.Contains(value, StringComparer.OrdinalIgnoreCase))
                        throw new FormatException("Invalid or duplicate command file extension.");
                    suffixes.Add(value);
                }
                commands.Add(new ExtensionCommand(id, ReadLabel(entry, "title"), handler, suffixes, panel, prefix, suffix));
            }
            return commands;
        }
    }
}
