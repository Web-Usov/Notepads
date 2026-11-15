namespace Notepads.SyntaxExtensions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Notepads.Services;
    using Notepads.Settings;
    using Windows.ApplicationModel;
    using Windows.Storage;

    internal static class ExtensionCatalog
    {
        private static IReadOnlyDictionary<string, Uri> _updateSources = new Dictionary<string, Uri>();
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

        public static Uri GetUpdateSource(string id) => _updateSources.TryGetValue(id, out var source) ? source : null;

        public static async Task<ExtensionPackage> PreviewAsync(StorageFile source)
        {
            using (var input = await source.OpenStreamForReadAsync())
            {
                string label = string.IsNullOrEmpty(source.Path) ? source.Name : source.Path;
                return await ExtensionPackage.ReadAsync(input, label, false, null, CancellationToken.None);
            }
        }

        public static async Task InstallAsync(ExtensionPackage package, SyntaxExtension previous = null, Uri previousSource = null)
        {
            await LoadLock.WaitAsync();
            try
            {
                var folder = await GetUserFolderAsync();
                // The app supports multiple processes. Serialize the update check and final
                // rename across windows, while readers remain free to load either complete file.
                using (await AcquireInstallLockAsync(folder))
                {
                    string name = "ext-" + package.Extension.Id + ExtensionPackage.ManifestSuffix;
                    if (previous != null)
                    {
                        var file = await folder.TryGetItemAsync(name) as StorageFile;
                        if (file == null || previousSource == null)
                            throw new InvalidOperationException("The installed extension changed. Reload extensions and check again.");
                        var contents = ExtensionPackage.Unwrap(await ReadInstalledAsync(file), out var currentSource);
                        var current = SyntaxExtension.Parse(contents);
                        if (current.Version != previous.Version || currentSource != previousSource || !package.IsNewerThan(current))
                            throw new InvalidOperationException("The installed extension changed. Reload extensions and check again.");
                    }

                    // Commit the validated manifest and source receipt together. Reload ignores temporary files.
                    var temp = await folder.CreateFileAsync(Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        await FileIO.WriteBytesAsync(temp, package.InstalledBytes());
                        await temp.RenameAsync(name, NameCollisionOption.ReplaceExisting);
                    }
                    catch
                    {
                        try { await temp.DeleteAsync(); }
                        catch (Exception ex) { LoggingService.LogException(ex); }
                        throw;
                    }
                }
            }
            finally
            {
                LoadLock.Release();
            }

            await ReloadAsync();
        }

        private static async Task<FileStream> AcquireInstallLockAsync(StorageFolder folder)
        {
            string path = Path.Combine(folder.Path, ".install.lock");
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex) when ((ex.HResult & 0xffff) == 32 && attempt < 50)
                {
                    await Task.Delay(40);
                }
            }
        }

        private static async Task LoadAsync(bool force)
        {
            await LoadLock.WaitAsync();
            try
            {
                if (_initialized && !force) return;
                var extensions = new List<SyntaxExtension>();
                var errors = new List<string>();
                var sources = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
                // User grammars are first so removing an override restores the bundled grammar.
                await LoadFolderAsync(GetUserFolderAsync, "LocalState/Extensions", extensions, errors, sources);
                await LoadFolderAsync(GetBundledFolderAsync, "Assets/Extensions", extensions, errors);
                Extensions = new GrammarRegistry(extensions).Extensions;
                _updateSources = sources;
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
            List<SyntaxExtension> extensions, List<string> errors, Dictionary<string, Uri> sources = null)
        {
            try
            {
                var folder = await getFolder();
                var files = await folder.GetFilesAsync();
                foreach (var file in files.Where(f => f.Name.EndsWith(ExtensionPackage.ManifestSuffix, StringComparison.OrdinalIgnoreCase))
                                          .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var text = ExtensionPackage.Unwrap(await ReadInstalledAsync(file), out var source);
                        var extension = await Task.Run(() => SyntaxExtension.Parse(text));
                        // Only the first effective, importer-managed file may offer a remote update.
                        if (sources != null && source != null &&
                            file.Name.Equals("ext-" + extension.Id + ExtensionPackage.ManifestSuffix, StringComparison.OrdinalIgnoreCase) &&
                            !extensions.Any(e => e.Id == extension.Id))
                            sources[extension.Id] = source;
                        extensions.Add(extension);
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

        private static async Task<string> ReadInstalledAsync(StorageFile file)
        {
            using (var input = await file.OpenStreamForReadAsync())
                return ExtensionPackage.Decode(await ExtensionPackage.ReadBytesAsync(input,
                    ExtensionPackage.MaxInstalledBytes, CancellationToken.None));
        }
    }
}
