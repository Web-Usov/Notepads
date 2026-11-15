using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Notepads.SyntaxExtensions;

internal static class PackageTests
{
    private const string Filename = "example.notepads-extension.json";
    private const string Raw = "https://packages.example/example.json";
    private const string Repo = "https://github.com/example/grammar";
    private const string Api = "https://api.github.com/repos/example/grammar";

    private static string Manifest(string version = "1.0.0", string id = "example.grammar") =>
        "{\"apiVersion\":2,\"version\":" + JsonSerializer.Serialize(version) + ",\"id\":" + JsonSerializer.Serialize(id) +
        ",\"displayName\":\"Example\",\"contributes\":{\"grammars\":[{\"fileExtensions\":[\".eg\"],\"rules\":[{\"scope\":\"string\",\"match\":\"word\"}]}]}}";

    public static void Run()
    {
        CheckPackageSourcesAsync().GetAwaiter().GetResult();
        Console.WriteLine("Extension package and update-source tests passed.");
    }

    private static async Task CheckPackageSourcesAsync()
    {
        var token = CancellationToken.None;
        var bytes = Encoding.UTF8.GetBytes(Manifest());
        var installed = await ReadAsync(bytes, Raw);
        string manifest = ExtensionPackage.Unwrap(ExtensionPackage.Decode(installed.InstalledBytes()), out var source);
        Require(source.AbsoluteUri == Raw && SyntaxExtension.Parse(manifest).Id == "example.grammar", "receipt round trip");

        // A receipt supplied by someone else never replaces the source actually selected for this import.
        var imported = await ReadAsync(installed.InstalledBytes(), "C:\\test\\archive.zip", false);
        manifest = ExtensionPackage.Unwrap(ExtensionPackage.Decode(imported.InstalledBytes()), out source);
        Require(source == null && SyntaxExtension.Parse(manifest).Version == "1.0.0", "local reimport strips earlier remote provenance");

        var archiveBytes = Zip(("release/" + Filename, bytes), ("release/README.md", Encoding.UTF8.GetBytes("Notes")));
        var archived = await ReadAsync(archiveBytes, "grammar.zip", false);
        Require(archived.Extension.Id == installed.Extension.Id, "wrapper directory ZIP");
        await RejectAsync<FormatException>(() => ReadAsync(Zip(("docs/readme.md", bytes)), Raw));
        await RejectAsync<FormatException>(() => ReadAsync(Zip((Filename, bytes), ("other/" + Filename, bytes)), Raw));
        await RejectAsync<FormatException>(() => ReadAsync(Zip(("../" + Filename, bytes)), Raw));
        await RejectAsync<FormatException>(() => ReadAsync(Zip((Filename, new byte[65537])), Raw));
        await RejectAsync<FormatException>(() => ReadAsync(new byte[ExtensionPackage.MaxPackageBytes + 1], Raw));
        await RejectAsync<DecoderFallbackException>(() => ReadAsync(new byte[] { 0xff, 0xff }, Raw));
        await RejectAsync<OperationCanceledException>(() => ExtensionDownload.LoadAsync(Raw, new CancellationToken(true),
            new HttpClient(new TestHandler(_ => Ok(bytes)))));

        var release = "{\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"grammar.zip\",\"browser_download_url\":\"" + Repo + "/releases/download/v1.2.0/grammar.zip\"}]}";
        var requests = new List<string>();
        using (var client = new HttpClient(new TestHandler(url =>
        {
            requests.Add(url);
            if (url == Api + "/releases/latest" || url == Api + "/releases/tags/v1.2.0")
                return Json(release);
            if (url == Repo + "/releases/download/v1.2.0/grammar.zip")
                return Redirect("https://download.example/pinned.zip");
            if (url == "https://download.example/pinned.zip")
                return Ok(Zip((Filename, Encoding.UTF8.GetBytes(Manifest("1.2.0")))));
            throw new Exception("Unexpected URL: " + url);
        })))
        {
            var fromRepo = await ExtensionDownload.LoadAsync(Repo, token, client);
            Require(fromRepo.Revision == "v1.2.0" && fromRepo.Source == Repo && fromRepo.IsNewerThan(installed.Extension), "GitHub latest release with redirected ZIP");
            Require(!fromRepo.IsNewerThan(fromRepo.Extension), "equal versions do not offer installation");
            Require(!installed.IsNewerThan(fromRepo.Extension), "regressions do not offer installation");
            bool mismatched = false;
            try { fromRepo.IsNewerThan(SyntaxExtension.Parse(Manifest("1.0.0", "another.id"))); }
            catch (FormatException) { mismatched = true; }
            Require(mismatched, "reject ID changes on update");
            requests.Clear();
            string pinned = Repo + "/releases/tag/v1.2.0";
            var byTag = await ExtensionDownload.LoadAsync(pinned, token, client);
            Require(byTag.Source == pinned && requests[0] == Api + "/releases/tags/v1.2.0", "pin explicit release");
        }

        using (var client = new HttpClient(new TestHandler(url =>
        {
            if (url == Api + "/releases/latest" || url == Api + "/releases/tags/missing")
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (url == Api + "/contents")
                return Json("[{\"name\":\"" + Filename + "\",\"type\":\"file\",\"sha\":\"abc123\",\"git_url\":\"" + Api + "/git/blobs/abc123\"}]");
            if (url == Api + "/git/blobs/abc123")
                return Json("{\"sha\":\"abc123\",\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(bytes) + "\"}");
            throw new Exception("Unexpected URL: " + url);
        })))
        {
            var fallback = await ExtensionDownload.LoadAsync(Repo, token, client);
            Require(fallback.Revision == "abc123" && fallback.Source == Repo, "fallback pins exact repository blob");
            await RejectAsync<HttpRequestException>(() => ExtensionDownload.LoadAsync(Repo + "/releases/tag/missing", token, client));
        }

        using (var client = new HttpClient(new TestHandler(url => url == Raw ? Ok(bytes) :
            url == "https://packages.example/downgrade" ? Redirect("http://example.com/plain") :
            Json("{\"tag_name\":\"v1.2.0\",\"assets\":[]}"))))
        {
            Require((await ExtensionDownload.LoadAsync(Raw, token, client)).Source == Raw, "direct HTTPS manifest");
            await RejectAsync<FormatException>(() => ExtensionDownload.LoadAsync("https://packages.example/downgrade", token, client));
            await RejectAsync<FormatException>(() => ExtensionDownload.LoadAsync("http://example.com/", token, client));
            await RejectAsync<FormatException>(() => ExtensionDownload.LoadAsync("https://user:password@example.com/", token, client));
            await RejectAsync<FormatException>(() => ExtensionDownload.LoadAsync(Repo, token, client));
        }
    }

    private static async Task<ExtensionPackage> ReadAsync(byte[] bytes, string source, bool remote = true)
    {
        using (var input = new MemoryStream(bytes))
            return await ExtensionPackage.ReadAsync(input, source, remote, null, CancellationToken.None);
    }

    private static byte[] Zip(params (string path, byte[] bytes)[] entries)
    {
        using (var output = new MemoryStream())
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                foreach (var entry in entries)
                    using (var stream = zip.CreateEntry(entry.path).Open())
                        stream.Write(entry.bytes, 0, entry.bytes.Length);
            return output.ToArray();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Package test failed: " + description);
    }

    private static async Task RejectAsync<T>(Func<Task> run) where T : Exception
    {
        try { await run(); }
        catch (T) { return; }
        throw new Exception("Expected package failure of type " + typeof(T).Name);
    }

    private static HttpResponseMessage Ok(byte[] bytes) =>
        new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static HttpResponseMessage Json(string json) => Ok(Encoding.UTF8.GetBytes(json));

    private static HttpResponseMessage Redirect(string target) => new HttpResponseMessage(HttpStatusCode.Redirect)
    {
        Headers = { Location = new Uri(target) }
    };

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        public TestHandler(Func<string, HttpResponseMessage> respond) { _respond = respond; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request.RequestUri.AbsoluteUri));
        }
    }
}
