namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Notepads.Services;
    using Notepads.Settings;
    using Windows.ApplicationModel;
    using Windows.Storage;

    internal static class ExtensionCatalog
    {
        private const string ManifestSuffix = ".notepads-extension.json";
        private static readonly SemaphoreSlim LoadLock = new SemaphoreSlim(1, 1);
        private static volatile GrammarRegistry _registry = new GrammarRegistry(Array.Empty<SyntaxExtension>());
        private static volatile bool _initialized;

        public static event EventHandler Changed;
        public static IReadOnlyList<SyntaxExtension> Extensions { get; private set; } = Array.Empty<SyntaxExtension>();
        public static bool IsEnabled(string id) => !(ApplicationSettingsStore.Read("ExtensionDisabled." + id) is bool disabled && disabled);

        public static async Task SetEnabledAsync(string id, bool enabled)
        {
            ApplicationSettingsStore.Write("ExtensionDisabled." + id, !enabled);
            await ReloadAsync();
        }
        public static IReadOnlyList<string> LoadErrors { get; private set; } = Array.Empty<string>();
        public static SyntaxGrammar FindGrammar(string extension) => _registry.FindGrammar(extension);

        public static Task InitializeAsync() => _initialized ? Task.CompletedTask : LoadAsync(false);
        public static Task ReloadAsync() => LoadAsync(true);

        public static async Task<StorageFolder> GetUserFolderAsync()
        {
            return await ApplicationData.Current.LocalFolder.CreateFolderAsync("Extensions", CreationCollisionOption.OpenIfExists);
        }

        public static async Task InstallAsync(StorageFile source)
        {
            // Persist the exact text that passed validation. A failed update leaves the previous file intact.
            var text = await ReadManifestAsync(source);
            var extension = await Task.Run(() => SyntaxExtension.Parse(text));
            var folder = await GetUserFolderAsync();
            var temp = await folder.CreateFileAsync(Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await FileIO.WriteBytesAsync(temp, Encoding.UTF8.GetBytes(text));
                await temp.RenameAsync("ext-" + extension.Id + ManifestSuffix, NameCollisionOption.ReplaceExisting);
            }
            catch
            {
                try { await temp.DeleteAsync(); }
                catch (Exception ex) { LoggingService.LogException(ex); }
                throw;
            }

            await ReloadAsync();
        }

        private static async Task LoadAsync(bool force)
        {
            await LoadLock.WaitAsync();
            try
            {
                if (_initialized && !force) return;
                var extensions = new List<SyntaxExtension>();
                var errors = new List<string>();
                // User grammars are first so removing an override restores the bundled grammar.
                await LoadFolderAsync(GetUserFolderAsync, "LocalState/Extensions", extensions, errors);
                await LoadFolderAsync(GetBundledFolderAsync, "Assets/Extensions", extensions, errors);
                Extensions = new GrammarRegistry(extensions).Extensions;
                _registry = new GrammarRegistry(extensions.Where(extension => IsEnabled(extension.Id)));
                LoadErrors = errors.AsReadOnly();
                _initialized = true;
            }
            finally
            {
                LoadLock.Release();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static async Task<StorageFolder> GetBundledFolderAsync()
        {
            return await Package.Current.InstalledLocation.GetFolderAsync(@"Assets\Extensions");
        }

        private static async Task LoadFolderAsync(Func<Task<StorageFolder>> getFolder, string label,
            List<SyntaxExtension> extensions, List<string> errors)
        {
            try
            {
                var folder = await getFolder();
                var files = await folder.GetFilesAsync();
                foreach (var file in files.Where(f => f.Name.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var text = await ReadManifestAsync(file);
                        extensions.Add(await Task.Run(() => SyntaxExtension.Parse(text)));
                    }
                    catch (Exception ex)
                    {
                        errors.Add(file.Name);
                        LoggingService.LogWarning($"Extension {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(label);
                LoggingService.LogWarning($"Extension folder {label}: {ex.Message}");
            }
        }

        private static async Task<string> ReadManifestAsync(StorageFile file)
        {
            using (var input = await file.OpenStreamForReadAsync())
            {
                if (input.Length > SyntaxExtension.MaxManifestLength)
                    throw new FormatException("Extension manifest exceeds 64 KiB.");

                // Restrict the read even if the source is modified after the length check.
                var bytes = new byte[SyntaxExtension.MaxManifestLength + 1];
                int count = 0;
                while (count < bytes.Length)
                {
                    int read = await input.ReadAsync(bytes, count, bytes.Length - count);
                    if (read == 0) break;
                    count += read;
                }
                if (count == bytes.Length)
                    throw new FormatException("Extension manifest exceeds 64 KiB.");

                int start = count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, start, count - start);
            }
        }
    }
}
