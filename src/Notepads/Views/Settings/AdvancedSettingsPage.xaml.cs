// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2019-2024, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Notepads.Views.Settings
{
    using Notepads.Controls.Dialog;
    using Notepads.Services;
    using Notepads.SyntaxExtensions;
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Windows.ApplicationModel.Resources;
    using Windows.Storage.Pickers;
    using Windows.System;
    using Notepads.Utilities;
    using System.Collections.Generic;
    using System.Linq;
    using Windows.Globalization;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Controls;

    public sealed partial class AdvancedSettingsPage : Page
    {
        private CancellationTokenSource _pageLifetime;
        private string _extensionNotice;
        private readonly IReadOnlyCollection<LanguageItem> SupportedLanguages = LanguageUtility.GetSupportedLanguageItems();

        public AdvancedSettingsPage()
        {
            InitializeComponent();

            ShowStatusBarToggleSwitch.IsOn = AppSettingsService.ShowStatusBar;
            EnableSmartCopyToggleSwitch.IsOn = AppSettingsService.IsSmartCopyEnabled;

            // Disable session snapshot toggle for shadow windows
            if (!App.IsPrimaryInstance)
            {
                EnableSessionSnapshotToggleSwitch.IsOn = false;
                EnableSessionSnapshotToggleSwitch.IsEnabled = false;
            }
            else
            {
                EnableSessionSnapshotToggleSwitch.IsOn = AppSettingsService.IsSessionSnapshotEnabled;
            }

            ExitWhenLastTabClosedToggleSwitch.IsOn = AppSettingsService.ExitWhenLastTabClosed;
            AlwaysOpenNewWindowToggleSwitch.IsOn = AppSettingsService.AlwaysOpenNewWindow;

            if (App.IsGameBarWidget)
            {
                // these settings don't make sense for Game Bar, there can be only one
                SessionSnapshotSettingsTitle.Visibility = Visibility.Collapsed;
                SessionSnapshotSettingsControls.Visibility = Visibility.Collapsed;
                LaunchPreferenceSettingsTitle.Visibility = Visibility.Collapsed;
                LaunchPreferenceSettingsControls.Visibility = Visibility.Collapsed;
            }

            LanguagePicker.SelectedItem = SupportedLanguages.FirstOrDefault(language => language.ID == ApplicationLanguages.PrimaryLanguageOverride);
            RestartPrompt.Visibility = LanguageUtility.CurrentLanguageID == ApplicationLanguages.PrimaryLanguageOverride ? Visibility.Collapsed : Visibility.Visible;

            Loaded += AdvancedSettings_Loaded;
            Unloaded += AdvancedSettings_Unloaded;
        }

        private async void AdvancedSettings_Loaded(object sender, RoutedEventArgs e)
        {
            _pageLifetime = new CancellationTokenSource();
            ShowStatusBarToggleSwitch.Toggled += ShowStatusBarToggleSwitch_Toggled;
            EnableSmartCopyToggleSwitch.Toggled += EnableSmartCopyToggleSwitch_Toggled;
            EnableSessionSnapshotToggleSwitch.Toggled += EnableSessionBackupAndRestoreToggleSwitch_Toggled;
            ExitWhenLastTabClosedToggleSwitch.Toggled += ExitWhenLastTabClosedToggleSwitch_Toggled;
            AlwaysOpenNewWindowToggleSwitch.Toggled += AlwaysOpenNewWindowToggleSwitch_Toggled;
            LanguagePicker.SelectionChanged += LanguagePicker_SelectionChanged;
            await RunExtensionActionAsync(ExtensionCatalog.InitializeAsync);
        }

        private void AdvancedSettings_Unloaded(object sender, RoutedEventArgs e)
        {
            _pageLifetime?.Cancel();
            _pageLifetime?.Dispose();
            _pageLifetime = null;
            ShowStatusBarToggleSwitch.Toggled -= ShowStatusBarToggleSwitch_Toggled;
            EnableSmartCopyToggleSwitch.Toggled -= EnableSmartCopyToggleSwitch_Toggled;
            EnableSessionSnapshotToggleSwitch.Toggled -= EnableSessionBackupAndRestoreToggleSwitch_Toggled;
            ExitWhenLastTabClosedToggleSwitch.Toggled -= ExitWhenLastTabClosedToggleSwitch_Toggled;
            AlwaysOpenNewWindowToggleSwitch.Toggled -= AlwaysOpenNewWindowToggleSwitch_Toggled;
            LanguagePicker.SelectionChanged -= LanguagePicker_SelectionChanged;
        }

        private static string ExtensionLabel(string name)
        {
            return ResourceLoader.GetForCurrentView().GetString("/Settings/Extensions_" + name);
        }

        private async Task RunExtensionActionAsync(Func<Task> action)
        {
            ExtensionControls.IsEnabled = false;
            _extensionNotice = null;
            try
            {
                await action();
                ExtensionList.Children.Clear();
                foreach (var extension in ExtensionCatalog.Extensions)
                {
                    string suffixes = string.Join(", ", extension.Grammars.SelectMany(grammar => grammar.FileExtensions));
                    var toggle = new CheckBox
                    {
                        Content = extension.DisplayName + "  " + extension.Version +
                                  (suffixes.Length == 0 ? "" : " (" + suffixes + ")"),
                        IsChecked = ExtensionCatalog.IsEnabled(extension.Id),
                        Tag = extension.Id
                    };
                    toggle.Click += ToggleExtension_Click;
                    ExtensionList.Children.Add(toggle);
                    if (ExtensionCatalog.GetUpdateSource(extension.Id) != null)
                    {
                        var update = new Button { Content = ExtensionLabel("CheckUpdates"), Tag = extension };
                        AutomationProperties.SetName(update, ExtensionLabel("CheckUpdates") + " · " + extension.DisplayName);
                        update.Click += CheckExtensionUpdate_Click;
                        ExtensionList.Children.Add(update);
                    }
                }
                var errors = ExtensionCatalog.LoadErrors;
                ExtensionStatus.Text = errors.Count == 0 ? _extensionNotice ?? string.Empty
                    : string.Format(ExtensionLabel("LoadErrors"), string.Join(", ", errors));
            }
            catch (OperationCanceledException) when (_pageLifetime == null) { }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
                ExtensionStatus.Text = string.Format(ExtensionLabel("ActionError"), ex.Message);
            }
            finally
            {
                ExtensionControls.IsEnabled = true;
            }
        }

        private async void ToggleExtension_Click(object sender, RoutedEventArgs e)
        {
            var toggle = (CheckBox)sender;
            await RunExtensionActionAsync(() => ExtensionCatalog.SetEnabledAsync((string)toggle.Tag, toggle.IsChecked == true));
            toggle.IsChecked = ExtensionCatalog.IsEnabled((string)toggle.Tag);
        }

        private async void InstallExtension_Click(object sender, RoutedEventArgs e)
        {
            await RunExtensionActionAsync(async () =>
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".json");
                picker.FileTypeFilter.Add(".zip");
                var token = _pageLifetime.Token;
                var file = await picker.PickSingleFileAsync();
                if (file != null && !token.IsCancellationRequested)
                    await ConfirmExtensionAsync(await ExtensionCatalog.PreviewAsync(file), token);
            });
        }

        private async void InstallExtensionUrl_Click(object sender, RoutedEventArgs e)
        {
            await RunExtensionActionAsync(async () =>
            {
                var token = _pageLifetime.Token;
                var input = new TextBox { Header = ExtensionLabel("UrlLabel"), PlaceholderText = "https://", MaxLength = 4096 };
                var dialog = new NotepadsDialog
                {
                    Title = ExtensionLabel("InstallUrlTitle"),
                    Content = input,
                    PrimaryButtonText = ExtensionLabel("Preview"),
                    CloseButtonText = ExtensionLabel("Cancel"),
                    IsPrimaryButtonEnabled = false
                };
                input.TextChanged += (field, args) =>
                    dialog.IsPrimaryButtonEnabled = ExtensionDownload.TryGetHttps(input.Text.Trim(), out _);
                if (await DialogManager.OpenDialogAsync(dialog, true) == ContentDialogResult.Primary)
                {
                    token.ThrowIfCancellationRequested();
                    var package = await ExtensionDownload.LoadAsync(input.Text.Trim(), token);
                    await ConfirmExtensionAsync(package, token);
                }
            });
        }

        private async void CheckExtensionUpdate_Click(object sender, RoutedEventArgs e)
        {
            var installed = (SyntaxExtension)((Button)sender).Tag;
            await RunExtensionActionAsync(async () =>
            {
                var source = ExtensionCatalog.GetUpdateSource(installed.Id);
                if (source == null) return;
                var token = _pageLifetime.Token;
                var package = await ExtensionDownload.LoadAsync(source.AbsoluteUri, token);
                if (package.IsNewerThan(installed))
                    await ConfirmExtensionAsync(package, token, installed, source);
                else
                    _extensionNotice = string.Format(ExtensionLabel("NoUpdate"), installed.DisplayName, installed.Version);
            });
        }

        private async Task ConfirmExtensionAsync(ExtensionPackage package, CancellationToken token,
            SyntaxExtension previous = null, Uri previousSource = null)
        {
            token.ThrowIfCancellationRequested();
            string details = string.Format(ExtensionLabel("Details"), package.Extension.DisplayName,
                package.Extension.Id, package.Extension.Version, package.Source);
            if (package.Revision != null)
                details += Environment.NewLine + string.Format(ExtensionLabel("Revision"), package.Revision);
            var existing = ExtensionCatalog.Extensions.FirstOrDefault(e => e.Id == package.Extension.Id);
            if (existing != null)
                details += Environment.NewLine + string.Format(ExtensionLabel("Replaces"), existing.Version);

            var dialog = new NotepadsDialog
            {
                Title = ExtensionLabel("InstallTitle"),
                Content = new TextBlock { Text = details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                PrimaryButtonText = ExtensionLabel("Confirm"),
                CloseButtonText = ExtensionLabel("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await DialogManager.OpenDialogAsync(dialog, true) != ContentDialogResult.Primary) return;
            token.ThrowIfCancellationRequested();
            await ExtensionCatalog.InstallAsync(package, previous, previousSource);
            _extensionNotice = string.Format(ExtensionLabel("Installed"), package.Extension.DisplayName, package.Extension.Version);
        }

        private async void OpenExtensionsFolder_Click(object sender, RoutedEventArgs e)
        {
            await RunExtensionActionAsync(async () =>
            {
                if (!await Launcher.LaunchFolderAsync(await ExtensionCatalog.GetUserFolderAsync()))
                    throw new InvalidOperationException(ExtensionLabel("FolderError"));
            });
        }

        private async void ReloadExtensions_Click(object sender, RoutedEventArgs e)
        {
            await RunExtensionActionAsync(ExtensionCatalog.ReloadAsync);
        }

        private void EnableSmartCopyToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.IsSmartCopyEnabled = EnableSmartCopyToggleSwitch.IsOn;
        }

        private void EnableSessionBackupAndRestoreToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.IsSessionSnapshotEnabled = EnableSessionSnapshotToggleSwitch.IsOn;
        }

        private void ShowStatusBarToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.ShowStatusBar = ShowStatusBarToggleSwitch.IsOn;
        }

        private void ExitWhenLastTabClosedToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.ExitWhenLastTabClosed = ExitWhenLastTabClosedToggleSwitch.IsOn;
        }

        private void AlwaysOpenNewWindowToggleSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            AppSettingsService.AlwaysOpenNewWindow = AlwaysOpenNewWindowToggleSwitch.IsOn;
        }

        private void LanguagePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var languageId = ((LanguageItem)e.AddedItems.First()).ID;

            RestartPrompt.Visibility = languageId == LanguageUtility.CurrentLanguageID ? Visibility.Collapsed : Visibility.Visible;

            ApplicationLanguages.PrimaryLanguageOverride = languageId;
        }
    }
}
