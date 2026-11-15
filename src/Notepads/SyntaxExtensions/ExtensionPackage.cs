namespace Notepads.SyntaxExtensions
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>The same validated manifest is previewed and installed, regardless of its transport.</summary>
    internal sealed class ExtensionPackage
    {
        internal const string ManifestSuffix = ".notepads-extension.json";
        internal const int MaxPackageBytes = 2 * 1024 * 1024;
        internal const int MaxInstalledBytes = SyntaxExtension.MaxManifestLength + 32768;
        private const string ReceiptKey = "_notepadsInstall";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        private ExtensionPackage(string manifest, string source, bool remote, string revision)
        {
            Manifest = manifest;
            Extension = SyntaxExtension.Parse(manifest);
            Source = source;
            IsRemote = remote;
            Revision = revision;
        }

        public string Manifest { get; }
        public SyntaxExtension Extension { get; }
        public string Source { get; }
        public bool IsRemote { get; }
        public string Revision { get; }

        public static async Task<ExtensionPackage> ReadAsync(Stream input, string source, bool remote, string revision,
            CancellationToken cancellationToken)
        {
            byte[] bytes = await ReadBytesAsync(input, MaxPackageBytes, cancellationToken);
            string manifest;
            if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K')
            {
                using (var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read))
                {
                    var entries = archive.Entries;
                    if (entries.Count > 256)
                        throw new FormatException(ExtensionMessages.Get("ZipEntryCount"));
                    var manifests = entries.Where(e => e.FullName.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (manifests.Length != 1)
                        throw new FormatException(ExtensionMessages.Get("ZipManifestCount"));
                    var entry = manifests[0];
                    var path = entry.FullName.Split('/');
                    if (path.Length > 2 || path.Any(p => p.Length == 0 || p == "." || p == ".." || p.Contains("\\") || p.Contains(":")))
                        throw new FormatException(ExtensionMessages.Get("ZipManifestLocation"));
                    if (entry.Length > SyntaxExtension.MaxManifestLength)
                        throw new FormatException(ExtensionMessages.Get("ZipManifestSize"));
                    // Only the manifest stream is consumed; other entries never become installed files.
                    using (var stream = entry.Open())
                        manifest = Decode(await ReadBytesAsync(stream, SyntaxExtension.MaxManifestLength, cancellationToken));
                }
            }
            else
            {
                // A locally exported installed manifest can include a host-generated receipt.
                // Strip it and stamp the actual source of this import.
                if (bytes.Length > MaxInstalledBytes)
                    throw new FormatException(ExtensionMessages.Get("ManifestSize"));
                manifest = Decode(bytes);
            }
            return new ExtensionPackage(Unwrap(manifest, out _), source, remote, revision);
        }

        internal static string Decode(byte[] bytes)
        {
            int start = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return Utf8.GetString(bytes, start, bytes.Length - start);
        }

        internal static async Task<byte[]> ReadBytesAsync(Stream stream, int limit, CancellationToken token)
        {
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    if (output.Length + read > limit)
                        throw new FormatException(ExtensionMessages.Get("DownloadByteLimit", limit));
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        // The receipt and manifest share one atomic rename. Keep manifest formatting and editable rules.
        public byte[] InstalledBytes()
        {
            string receipt = JsonSerializer.Serialize(Source) + ",\"remote\":" + (IsRemote ? "true" : "false") +
                ",\"revision\":" + JsonSerializer.Serialize(Revision);
            int end = Manifest.LastIndexOf('}');
            var bytes = Utf8.GetBytes(Manifest.Insert(end, ",\n  \"" + ReceiptKey + "\": {\"source\":" + receipt + "}\n"));
            if (bytes.Length > MaxInstalledBytes)
                throw new FormatException(ExtensionMessages.Get("SourceLength"));
            return bytes;
        }

        public static string Unwrap(string text, out Uri updateSource)
        {
            updateSource = null;
            using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 }))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new FormatException(ExtensionMessages.Get("ManifestObject"));

                if (root.TryGetProperty(ReceiptKey, out var receipt))
                {
                    if (receipt.ValueKind == JsonValueKind.Object &&
                        receipt.TryGetProperty("remote", out var remote) && remote.ValueKind == JsonValueKind.True &&
                        receipt.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.String)
                        ExtensionDownload.TryGetHttps(source.GetString(), out updateSource);

                    // The receipt can appear anywhere after hand-editing. Preserve property values
                    // verbatim, including regex escapes. Never accept an incoming receipt as provenance.
                    text = "{" + string.Join(",",
                        root.EnumerateObject().Where(p => p.Name != ReceiptKey)
                            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + p.Value.GetRawText())) + "}";
                }
            }
            if (Utf8.GetByteCount(text) > SyntaxExtension.MaxManifestLength)
                throw new FormatException(ExtensionMessages.Get("ManifestSize"));
            return text;
        }

        public bool IsNewerThan(SyntaxExtension installed)
        {
            if (!string.Equals(Extension.Id, installed.Id, StringComparison.Ordinal))
                throw new FormatException(ExtensionMessages.Get("UpdateId"));
            return Version.Parse(Extension.Version).CompareTo(Version.Parse(installed.Version)) > 0;
        }
    }
}
