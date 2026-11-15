namespace Notepads.SyntaxExtensions
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    internal static class ExtensionDownload
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false
        });

        internal static bool TryGetHttps(string value, out Uri uri)
        {
            if (value != null && value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out var candidate) &&
                candidate.Scheme == Uri.UriSchemeHttps && candidate.Host.Length != 0 &&
                candidate.UserInfo.Length == 0 && candidate.Fragment.Length == 0)
            {
                uri = candidate;
                return true;
            }
            uri = null;
            return false;
        }

        public static async Task<ExtensionPackage> LoadAsync(string address,
            CancellationToken token, HttpClient client = null)
        {
            if (!TryGetHttps(address, out var source))
                throw new FormatException("Enter an HTTPS URL without credentials or a fragment.");

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    return await ResolveAsync(client ?? Client, source, timeout.Token);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    throw new TimeoutException("The extension source took too long to respond.");
                }
            }
        }

        private static async Task<ExtensionPackage> ResolveAsync(HttpClient client, Uri source, CancellationToken token)
        {
            Uri download = source;
            string revision = null;
            var path = source.AbsolutePath.Trim('/').Split('/');
            bool repository = path.Length == 2 && path.All(p => p.Length != 0);
            bool latest = path.Length == 4 && path[2] == "releases" && path[3] == "latest";
            bool pinned = path.Length >= 5 && path[2] == "releases" && path[3] == "tag";
            if (source.IsDefaultPort && string.Equals(source.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase)
                && (repository || latest || pinned))
            {
                string repo = path[1];
                if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                    repo = repo.Substring(0, repo.Length - 4);
                string api = "https://api.github.com/repos/" + path[0] + "/" + repo;
                string tag = pinned ? Uri.UnescapeDataString(string.Join("/", path.Skip(4))) : null;
                string releasePath = pinned ? "/releases/tags/" + Uri.EscapeDataString(tag) : "/releases/latest";
                using (var release = await GetJsonAsync(client, new Uri(api + releasePath), !pinned, token))
                {
                    if (release != null)
                    {
                        var record = release.RootElement;
                        revision = record.GetProperty("tag_name").GetString();
                        var assets = record.GetProperty("assets").EnumerateArray().Where(asset =>
                        {
                            string name = asset.GetProperty("name").GetString() ?? "";
                            return name.EndsWith(ExtensionPackage.ManifestSuffix, StringComparison.OrdinalIgnoreCase) ||
                                   name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                        }).ToArray();
                        if (assets.Length != 1)
                            throw new FormatException("The GitHub release must attach one extension manifest or ZIP. Paste a direct asset URL to choose one.");
                        download = GetDownloadUri(assets[0].GetProperty("browser_download_url").GetString());
                    }
                    else
                    {
                        using (var contents = await GetJsonAsync(client, new Uri(api + "/contents"), false, token))
                        {
                            var files = contents.RootElement.EnumerateArray().Where(f =>
                                f.GetProperty("type").GetString() == "file" &&
                                f.GetProperty("name").GetString().EndsWith(ExtensionPackage.ManifestSuffix, StringComparison.OrdinalIgnoreCase)).ToArray();
                            if (files.Length != 1)
                                throw new FormatException("The repository has no published release and needs one root *.notepads-extension.json manifest.");
                            revision = files[0].GetProperty("sha").GetString();
                            // Fetch the blob by its immutable SHA, rather than a moving branch URL.
                            var blobUri = GetDownloadUri(files[0].GetProperty("git_url").GetString());
                            using (var blob = await GetJsonAsync(client, blobUri, false, token))
                            {
                                if (blob.RootElement.GetProperty("encoding").GetString() != "base64" ||
                                    !string.Equals(blob.RootElement.GetProperty("sha").GetString(), revision, StringComparison.Ordinal))
                                    throw new FormatException("GitHub returned an unexpected manifest blob.");
                                byte[] bytes = Convert.FromBase64String(blob.RootElement.GetProperty("content").GetString());
                                using (var input = new MemoryStream(bytes, false))
                                    return await ExtensionPackage.ReadAsync(input, source.AbsoluteUri, true, revision, token);
                            }
                        }
                    }
                }
            }
            byte[] packageBytes = await GetAsync(client, download, ExtensionPackage.MaxPackageBytes, false, token);
            using (var input = new MemoryStream(packageBytes, false))
                return await ExtensionPackage.ReadAsync(input, source.AbsoluteUri, true, revision, token);
        }

        private static Uri GetDownloadUri(string url)
        {
            if (!TryGetHttps(url, out var uri))
                throw new FormatException("The extension source returned a non-HTTPS download address.");
            return uri;
        }

        private static async Task<JsonDocument> GetJsonAsync(HttpClient client, Uri url, bool allowMissing, CancellationToken token)
        {
            var bytes = await GetAsync(client, url, 1024 * 1024, allowMissing, token);
            return bytes == null ? null : JsonDocument.Parse(bytes);
        }

        private static async Task<byte[]> GetAsync(HttpClient client, Uri url, int sizeLimit, bool allowMissing,
            CancellationToken token)
        {
            for (int hop = 0; hop <= 5; hop++)
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("Notepads-Extensions/2");
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound && allowMissing) return null;
                        int status = (int)response.StatusCode;
                        if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                        {
                            if (response.Headers.Location == null)
                                throw new FormatException("The extension source redirected without a destination.");
                            url = GetDownloadUri(new Uri(url, response.Headers.Location).AbsoluteUri);
                            continue;
                        }
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > sizeLimit)
                            throw new FormatException("The extension source exceeds the download size limit.");
                        using (var stream = await response.Content.ReadAsStreamAsync())
                            return await ExtensionPackage.ReadBytesAsync(stream, sizeLimit, token);
                    }
                }
            }
            throw new FormatException("Too many redirects while downloading the extension.");
        }
    }
}
