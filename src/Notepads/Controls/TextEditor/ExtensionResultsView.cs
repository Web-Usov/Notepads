namespace Notepads.Controls.TextEditor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Notepads.SyntaxExtensions;
    using Windows.System;
    using Windows.UI.Text;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Automation;
    using Windows.UI.Xaml.Automation.Peers;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media;

    internal sealed class ExtensionResultsView : UserControl
    {
        private readonly ExtensionPanel _panel;
        private readonly Action<EditorSnapshot, EditorPanelRow> _navigate;
        private readonly TextBox _filter;
        private readonly ComboBox _severity;
        private readonly TextBlock _status;
        private readonly ListView _results;

        private EditorSnapshot _snapshot;
        private IReadOnlyList<EditorPanelRow> _displayedRows;
        private EditorSnapshot _displayedSnapshot;
        private CancellationTokenSource _pending;
        private bool _stopped;

        public ExtensionResultsView(ExtensionCommand command, EditorSnapshot snapshot,
            Action<EditorSnapshot, EditorPanelRow> navigate, Action close)
        {
            _panel = command.Panel;
            _navigate = navigate;

            var layout = new Grid { Padding = new Thickness(8) };
            for (int row = 0; row < 4; row++)
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock
            {
                Text = _panel.Title,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var closeButton = new Button { Content = "Close" };
            AutomationProperties.SetName(closeButton, "Close " + _panel.Title);
            closeButton.Click += (sender, args) => close();
            Grid.SetColumn(closeButton, 1);
            header.Children.Add(title);
            header.Children.Add(closeButton);

            _filter = new TextBox
            {
                Header = "Filter",
                PlaceholderText = "Request ID or text",
                Text = snapshot.Text.Substring(snapshot.SelectionStart, snapshot.SelectionLength),
                Margin = new Thickness(0, 8, 0, 0)
            };
            AutomationProperties.SetName(_filter, "Filter results by request ID or literal text");
            _severity = new ComboBox
            {
                Header = "Severity",
                ItemsSource = new[] { "All", "Trace", "Debug", "Info", "Warn", "Error", "Fatal" },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = _panel.Handler == "log.filter" ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0)
            };
            AutomationProperties.SetName(_severity, "Filter results by severity");
            _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            _results = new ListView
            {
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.None,
                FontFamily = new FontFamily("Consolas"),
                IsTabStop = true
            };
            AutomationProperties.SetName(_results, _panel.Title + " results by source line");
            // The ListView virtualizes text rows and needs no reflection-based item template.
            _results.ItemClick += (sender, args) =>
            {
                var labels = _results.ItemsSource as string[];
                if (labels != null && args.ClickedItem is string label)
                    NavigateToRow(Array.IndexOf(labels, label));
            };
            _results.KeyDown += (sender, args) =>
            {
                if (args.Key != VirtualKey.Enter && args.Key != VirtualKey.Space) return;
                var item = FocusManager.GetFocusedElement() as ListViewItem;
                if (item == null) return;
                int index = _results.IndexFromContainer(item);
                if (index < 0) return;
                args.Handled = true;
                NavigateToRow(index);
            };
            _filter.TextChanged += (sender, args) => ScheduleQuery();
            _severity.SelectionChanged += (sender, args) => ScheduleQuery();
            KeyDown += (sender, args) =>
            {
                if (args.Key != VirtualKey.Escape || _severity.IsDropDownOpen) return;
                args.Handled = true;
                close();
            };

            Grid.SetRow(_filter, 1);
            Grid.SetRow(_severity, 2);
            Grid.SetRow(_status, 3);
            Grid.SetRow(_results, 4);
            layout.Children.Add(header);
            layout.Children.Add(_filter);
            layout.Children.Add(_severity);
            layout.Children.Add(_status);
            layout.Children.Add(_results);
            Content = layout;
            Refresh(snapshot);
        }

        private void NavigateToRow(int index)
        {
            var shown = _displayedSnapshot;
            if (shown != null && ReferenceEquals(shown, _snapshot) &&
                index >= 0 && index < _displayedRows.Count)
                _navigate(shown, _displayedRows[index]);
        }

        public void Refresh(EditorSnapshot snapshot)
        {
            if (_stopped) return;
            _snapshot = snapshot;
            ScheduleQuery();
        }

        public void Stop()
        {
            _stopped = true;
            _pending?.Cancel();
            _pending = null;
            _displayedSnapshot = null;
            _displayedRows = null;
            _snapshot = null;
            _results.ItemsSource = null;
        }

        private async void ScheduleQuery()
        {
            if (_stopped || _snapshot == null) return;
            _pending?.Cancel();
            var pending = new CancellationTokenSource();
            _pending = pending;
            var snapshot = _snapshot;
            string filter = _filter.Text;
            string severity = _severity.SelectedItem as string;
            _displayedSnapshot = null;
            _displayedRows = null;
            _results.ItemsSource = null;
            _status.Text = "Searching…";
            try
            {
                await Task.Delay(150, pending.Token);
                IReadOnlyList<EditorPanelRow> rows = null;
                var labels = await Task.Run(() =>
                {
                    rows = _panel.Query(snapshot, filter, severity, pending.Token);
                    var lines = new string[rows.Count];
                    for (int i = 0; i < lines.Length; i++)
                    {
                        pending.Token.ThrowIfCancellationRequested();
                        lines[i] = rows[i].Label;
                    }
                    return lines;
                }, pending.Token);
                if (pending.IsCancellationRequested) return;
                _displayedSnapshot = snapshot;
                _displayedRows = rows;
                _results.ItemsSource = labels;
                _status.Text = labels.Length == 0 ? "No matching lines." :
                    labels.Length == 1 ? "1 matching line." : labels.Length + " matching lines.";
            }
            catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (!pending.IsCancellationRequested)
                    _status.Text = "Could not load results. " + exception.Message;
            }
            finally
            {
                if (ReferenceEquals(_pending, pending)) _pending = null;
                pending.Dispose();
            }
        }
    }
}
