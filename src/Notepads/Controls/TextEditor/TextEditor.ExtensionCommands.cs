namespace Notepads.Controls.TextEditor
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Notepads.Services;
    using Notepads.SyntaxExtensions;
    using Windows.UI.Core;
    using Windows.UI.Text;
    using Windows.UI.Xaml;

    public sealed partial class TextEditor
    {
        private long _extensionRevision;
        private ExtensionResultsView _extensionView;
        private ExtensionCommand _panelCommand;

        internal string CommandFileExtension => Path.GetExtension(EditingFileName ?? FileNamePlaceholder);

        private bool CommandIsAvailable(ExtensionCommand command)
        {
            return IsEditorEnabled() && command.AppliesTo(CommandFileExtension) &&
                ExtensionCatalog.Extensions.Any(e => ExtensionCatalog.IsEnabled(e.Id) && e.Commands.Contains(command));
        }

        private EditorSnapshot CaptureExtensionSnapshot()
        {
            string text = TextEditorCore.GetText();
            var selection = TextEditorCore.Document.Selection;
            int start = Math.Min(text.Length, Math.Max(0, selection.StartPosition));
            int end = Math.Min(text.Length, Math.Max(start, selection.EndPosition));
            return new EditorSnapshot(Id, _extensionRevision, text, start, end - start);
        }

        internal async Task RunExtensionCommandAsync(ExtensionCommand command, Func<bool> isActive)
        {
            if (!isActive() || !CommandIsAvailable(command)) return;
            try
            {
                var snapshot = CaptureExtensionSnapshot();
                if (command.Panel != null)
                {
                    OpenExtensionPanel(command, snapshot, isActive);
                    return;
                }

                var result = await Task.Run(() => command.Execute(snapshot));
                if (!isActive() || !CommandIsAvailable(command)) return;
                // A selection-only change does not increment the document revision.
                var current = CaptureExtensionSnapshot();
                if (snapshot.SelectionStart != current.SelectionStart || snapshot.SelectionLength != current.SelectionLength) return;
                result.Apply(snapshot, Id, _extensionRevision, ReplaceExtensionRange, ShowExtensionError);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
                if (isActive()) NotificationCenter.Instance.PostNotification(ExtensionMessages.Get("CommandFailed", command.Title), 4000);
            }
        }

        private void ReplaceExtensionRange(int start, int length, string replacement)
        {
            var document = TextEditorCore.Document;
            var range = document.GetRange(start, start + length);
            document.BeginUndoGroup();
            try
            {
                range.SetText(TextSetOptions.None, replacement);
            }
            finally
            {
                document.EndUndoGroup();
            }
        }

        private void RevealExtensionRange(int start, int length)
        {
            TextEditorCore.Focus(FocusState.Programmatic);
            var selection = TextEditorCore.Document.Selection;
            selection.SetRange(start, start + length);
            selection.ScrollIntoView(PointOptions.Start);
        }

        private void ShowExtensionError(int start, int length, string message)
        {
            RevealExtensionRange(start, length);
            NotificationCenter.Instance.PostNotification(message, 5000);
        }

        private void OpenExtensionPanel(ExtensionCommand command, EditorSnapshot snapshot, Func<bool> isActive)
        {
            if (_isContentPreviewPanelOpened) ShowHideContentPreview();
            CloseExtensionPanel();
            if (SplitPanel == null) LoadSplitView();
            _panelCommand = command;
            _extensionView = new ExtensionResultsView(command, snapshot, (source, row) =>
            {
                if (isActive() && ReferenceEquals(command, _panelCommand) && CommandIsAvailable(command) &&
                    source.Matches(Id, _extensionRevision) && source.Contains(row.Start, row.Length))
                    RevealExtensionRange(row.Start, row.Length);
            }, () =>
            {
                CloseExtensionPanel();
                if (isActive()) TextEditorCore.Focus(FocusState.Programmatic);
            });
            ExtensionCatalog.Changed += OnExtensionPanelCatalogChanged;
            SplitPanel.Content = _extensionView;
            SplitPanelColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            SplitPanelColumnDefinition.MinWidth = 100;
            SplitPanel.Visibility = Visibility.Visible;
            GridSplitter.Visibility = Visibility.Visible;
        }

        private async void OnExtensionPanelCatalogChanged(object sender, EventArgs args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_panelCommand != null && !CommandIsAvailable(_panelCommand)) CloseExtensionPanel();
                });
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80010108) || ex.HResult == unchecked((int)0x80000013))
            {
                // A different app window was closing when the catalog changed.
            }
        }

        private void CloseExtensionPanel()
        {
            if (_extensionView == null) return;
            ExtensionCatalog.Changed -= OnExtensionPanelCatalogChanged;
            _extensionView.Stop();
            _extensionView = null;
            _panelCommand = null;
            SplitPanel.Content = null;
            SplitPanelColumnDefinition.Width = new GridLength(0);
            SplitPanelColumnDefinition.MinWidth = 0;
            EditorColumnDefinition.Width = new GridLength(1, GridUnitType.Star);
            SplitPanel.Visibility = Visibility.Collapsed;
            GridSplitter.Visibility = Visibility.Collapsed;
        }
    }
}
