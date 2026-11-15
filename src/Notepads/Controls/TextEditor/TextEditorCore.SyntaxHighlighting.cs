namespace Notepads.Controls.TextEditor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Notepads.Extensions;
    using Notepads.Services;
    using Notepads.SyntaxExtensions;
    using Windows.Foundation;
    using Windows.UI;
    using Windows.UI.Core;
    using Windows.UI.Text;
    using Windows.UI.ViewManagement;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Controls;
    using Windows.UI.Xaml.Media;
    using Windows.UI.Xaml.Shapes;

    public partial class TextEditorCore
    {
        private Canvas _syntaxViewport;
        private Canvas _syntaxLayer;
        private bool _syntaxScrollTracking;
        private readonly Dictionary<string, Path> _syntaxPaths = new Dictionary<string, Path>();
        private readonly AccessibilitySettings _syntaxAccessibility = new AccessibilitySettings();
        private bool _syntaxActive;
        private bool _syntaxPaintQueued;
        private string _syntaxExtension;
        private SyntaxGrammar _syntaxGrammar;
        private CancellationTokenSource _syntaxRequest;
        private IReadOnlyList<SyntaxToken> _syntaxTokens;
        private string _syntaxSnapshot;

        internal void SetSyntaxExtension(string extension)
        {
            _syntaxExtension = extension;
            if (_syntaxActive) RefreshSyntaxGrammar();
        }

        private void SetSyntaxLayer(Canvas viewport, Canvas layer)
        {
            ClearSyntaxLayer();
            _syntaxViewport = viewport;
            _syntaxLayer = layer;
            _syntaxScrollTracking = false;
            QueueSyntaxPaint();
        }

        private async void StartSyntaxHighlighting()
        {
            if (_syntaxActive) return;
            _syntaxActive = true;
            ExtensionCatalog.Changed += OnSyntaxCatalogChanged;
            ActualThemeChanged += OnSyntaxThemeChanged;
            _syntaxAccessibility.HighContrastChanged += OnSyntaxContrastChanged;
            try
            {
                await ExtensionCatalog.InitializeAsync();
                if (_syntaxActive) RefreshSyntaxGrammar();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning($"Syntax extensions could not be loaded ({ex.GetType().Name}).");
            }
        }

        private void OnSyntaxUnloaded(object sender, RoutedEventArgs args) => StopSyntaxHighlighting();

        private void StopSyntaxHighlighting()
        {
            if (!_syntaxActive) return;
            _syntaxActive = false;
            ExtensionCatalog.Changed -= OnSyntaxCatalogChanged;
            ActualThemeChanged -= OnSyntaxThemeChanged;
            _syntaxAccessibility.HighContrastChanged -= OnSyntaxContrastChanged;
            _syntaxRequest?.Cancel();
            _syntaxGrammar = null;
            _syntaxTokens = null;
            _syntaxSnapshot = null;
            ClearSyntaxLayer();
        }

        private void ClearSyntaxLayer()
        {
            _syntaxLayer?.Children.Clear();
            _syntaxPaths.Clear();
        }

        private void OnSyntaxThemeChanged(FrameworkElement sender, object args)
        {
            ClearSyntaxLayer();
            QueueSyntaxPaint();
        }

        private void OnSyntaxContrastChanged(AccessibilitySettings sender, object args) =>
            OnSyntaxCatalogChanged(sender, EventArgs.Empty);

        private async void OnSyntaxCatalogChanged(object sender, EventArgs args)
        {
            try
            {
                await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                {
                    if (_syntaxActive) RefreshSyntaxGrammar();
                });
            }
            catch (Exception ex) when (ex.HResult == unchecked((int)0x80010108) ||
                                       ex.HResult == unchecked((int)0x80000013))
            {
                // Another window can publish while this view's dispatcher is shutting down.
            }
        }

        private void RefreshSyntaxGrammar()
        {
            var grammar = _syntaxAccessibility.HighContrast || string.IsNullOrEmpty(_syntaxExtension)
                ? null : ExtensionCatalog.FindGrammar(_syntaxExtension);
            if (ReferenceEquals(grammar, _syntaxGrammar)) return;
            _syntaxGrammar = grammar;
            _syntaxTokens = null;
            _syntaxSnapshot = null;
            ClearSyntaxLayer();
            UpdateSyntaxTokens();
        }

        private async void UpdateSyntaxTokens()
        {
            _syntaxRequest?.Cancel();
            var grammar = _syntaxGrammar;
            var text = _document;
            if (grammar == null || text.Length == 0)
            {
                _syntaxTokens = null;
                _syntaxSnapshot = null;
                ClearSyntaxLayer();
                return;
            }

            using (var request = new CancellationTokenSource())
            {
                _syntaxRequest = request;
                try
                {
                    // Coalesce a burst of edits and allow the native text layout to settle.
                    await Task.Delay(text.Length < 65536 ? 25 : 90, request.Token);
                    var tokens = await Task.Run(() => grammar.Tokenize(text, request.Token), request.Token);
                    if (!request.IsCancellationRequested)
                    {
                        _syntaxTokens = tokens;
                        _syntaxSnapshot = text;
                        if (tokens.Count == 0) ClearSyntaxLayer();
                        else QueueSyntaxPaint();
                    }
                }
                catch (OperationCanceledException) when (request.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    if (!request.IsCancellationRequested)
                    {
                        _syntaxTokens = null;
                        _syntaxSnapshot = null;
                        ClearSyntaxLayer();
                    }
                    LoggingService.LogWarning($"Syntax highlighting failed ({ex.GetType().Name}).");
                }
                finally
                {
                    if (ReferenceEquals(request, _syntaxRequest)) _syntaxRequest = null;
                }
            }
        }

        private void QueueSyntaxPaint()
        {
            if (_syntaxPaintQueued || !_syntaxActive || _syntaxTokens == null || _syntaxTokens.Count == 0) return;
            _syntaxPaintQueued = true;
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                _syntaxPaintQueued = false;
                if (!_syntaxActive || _syntaxLayer == null || _syntaxTokens == null || _syntaxTokens.Count == 0) return;
                try
                {
                    PaintSyntaxViewport();
                }
                catch (Exception ex)
                {
                    // The native view can lose its layout while a tab or template is being replaced.
                    _syntaxLayer.Visibility = Visibility.Collapsed;
                    LoggingService.LogWarning($"Syntax viewport unavailable ({ex.GetType().Name}).");
                }
            });
        }

        private void PaintSyntaxViewport()
        {
            var viewer = _contentScrollViewer;
            if (viewer == null || _syntaxViewport == null || viewer.ViewportWidth <= 0 || viewer.ViewportHeight <= 0) return;
            if (!_syntaxScrollTracking)
            {
                // Keep native glyphs and their tint together during compositor-driven panning,
                // including intermediate frames before ScrollViewer.ViewChanged is delivered.
                viewer.StartExpressionAnimation(_syntaxLayer, Axis.X);
                viewer.StartExpressionAnimation(_syntaxLayer, Axis.Y);
                _syntaxScrollTracking = true;
            }

            var viewport = new Rect(viewer.HorizontalOffset, viewer.VerticalOffset,
                viewer.ViewportWidth, viewer.ViewportHeight);
            _syntaxViewport.Clip = new RectangleGeometry
            {
                Rect = new Rect(Padding.Left, Padding.Top, viewport.Width, viewport.Height)
            };
            var groups = new Dictionary<string, GeometryGroup>();
            GetTextSelectionPosition(out var selectionStart, out var selectionEnd);

            // Query the native layout a display line at a time. This also bounds work
            // on a horizontally scrolled, minified file and splits wrapped tokens.
            _syntaxLayer.Width = viewport.Right + Padding.Left;
            _syntaxLayer.Height = viewport.Bottom + Padding.Top;
            var y = viewport.Top;
            while (y < viewport.Bottom)
            {
                var left = Document.GetRangeFromPoint(new Point(viewport.Left, y), PointOptions.ClientCoordinates);
                var right = Document.GetRangeFromPoint(new Point(viewport.Right, y), PointOptions.ClientCoordinates);
                left.GetRect(PointOptions.ClientCoordinates, out var lineBounds, out _);
                if (lineBounds.IsEmpty || lineBounds.Height <= 0 || lineBounds.Bottom <= y) break;

                var first = Math.Min(left.StartPosition, right.StartPosition);
                var last = Math.Max(left.StartPosition, right.StartPosition);
                left.Expand(TextRangeUnit.Line);
                var lineStart = left.StartPosition;
                var lineEnd = Math.Min(left.EndPosition, _document.Length);
                first = Math.Max(lineStart, first - 1);
                last = Math.Min(lineEnd, last + 1);
                if (last > first && _document[last - 1] == RichEditBoxDefaultLineEnding) last--;

                // A fresh tokenizer may still be pending while the viewport is moved.
                // Only reuse old spans if this visible text still occupies the same offsets.
                bool unchanged = ReferenceEquals(_syntaxSnapshot, _document) ||
                    (last <= _syntaxSnapshot.Length && last >= first &&
                     string.CompareOrdinal(_syntaxSnapshot, first, _document, first, last - first) == 0);
                // GetRect returns a union bounding box. A mixed-direction visual line
                // can place unrelated glyphs between pieces of a logical token.
                // Preserve native ink when that single rectangle is ambiguous.
                if (unchanged && last > first && FlowDirection != FlowDirection.RightToLeft &&
                    !ContainsBidiText(_document, first, last))
                    AddLineInk(first, last);

                y = lineBounds.Bottom + 0.25;
                if (left.EndPosition >= _document.Length) break;
            }

            foreach (var entry in _syntaxPaths)
            {
                if (!groups.ContainsKey(entry.Key)) entry.Value.Data = null;
            }
            foreach (var entry in groups)
            {
                if (!_syntaxPaths.TryGetValue(entry.Key, out var path))
                {
                    path = new Path { Fill = CreateSyntaxBrush(entry.Key), Stretch = Stretch.None };
                    _syntaxLayer.Children.Add(path);
                    _syntaxPaths.Add(entry.Key, path);
                }
                path.Data = entry.Value;
            }

            _syntaxLayer.Visibility = Visibility.Visible;

            void AddLineInk(int first, int last)
            {
                for (var index = FirstSyntaxTokenEndingAfter(first); index < _syntaxTokens.Count; index++)
                {
                    var token = _syntaxTokens[index];
                    if (token.Start >= last) break;
                    var start = Math.Max(first, token.Start);
                    var end = Math.Min(last, token.Start + token.Length);
                    if (selectionStart == selectionEnd || selectionStart >= end || selectionEnd <= start)
                    {
                        AddInk(start, end, token.Scope);
                    }
                    else
                    {
                        AddInk(start, Math.Min(selectionStart, end), token.Scope);
                        AddInk(Math.Max(selectionEnd, start), end, token.Scope);
                    }
                }
            }

            void AddInk(int start, int end, string scope)
            {
                if (end <= start) return;
                Document.GetRange(start, end).GetRect(PointOptions.ClientCoordinates, out var rect, out _);
                rect.Intersect(viewport);
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return;
                rect.X += Padding.Left;
                rect.Y += Padding.Top;
                if (!groups.TryGetValue(scope, out var group))
                {
                    group = new GeometryGroup { FillRule = FillRule.Nonzero };
                    groups.Add(scope, group);
                }
                group.Children.Add(new RectangleGeometry { Rect = rect });
            }
        }

        private static bool ContainsBidiText(string text, int first, int last)
        {
            for (int i = first; i < last; i++)
            {
                char ch = text[i];
                if ((ch >= '\u0590' && ch <= '\u08ff') || (ch >= '\ufb1d' && ch <= '\ufefc') ||
                    ch == '\u200f' || (ch >= '\u202a' && ch <= '\u202e') ||
                    (ch >= '\u2066' && ch <= '\u2069'))
                    return true;
                if (ch >= '\ud800' && ch <= '\udbff' && i + 1 < last && char.IsLowSurrogate(text[i + 1]))
                {
                    int codePoint = char.ConvertToUtf32(ch, text[++i]);
                    if ((codePoint >= 0x10800 && codePoint <= 0x10fff) ||
                        (codePoint >= 0x1e800 && codePoint <= 0x1eeff))
                        return true;
                }
            }
            return false;
        }

        private int FirstSyntaxTokenEndingAfter(int position)
        {
            int lower = 0, upper = _syntaxTokens.Count;
            while (lower < upper)
            {
                var mid = lower + (upper - lower) / 2;
                var token = _syntaxTokens[mid];
                if (token.Start + token.Length <= position) lower = mid + 1;
                else upper = mid;
            }
            return lower;
        }

        private SyntaxInkBrush CreateSyntaxBrush(string scope)
        {
            var dark = ActualTheme == ElementTheme.Dark;
            Color color;
            switch (scope)
            {
                case "property": color = dark ? Color.FromArgb(255, 133, 201, 255) : Color.FromArgb(255, 11, 78, 139); break;
                case "string": color = dark ? Color.FromArgb(255, 159, 217, 157) : Color.FromArgb(255, 27, 103, 57); break;
                case "number": color = dark ? Color.FromArgb(255, 255, 192, 140) : Color.FromArgb(255, 145, 66, 22); break;
                case "keyword": color = dark ? Color.FromArgb(255, 219, 170, 255) : Color.FromArgb(255, 102, 49, 148); break;
                case "comment": color = dark ? Color.FromArgb(255, 166, 179, 172) : Color.FromArgb(255, 71, 96, 83); break;
                default: color = dark ? Color.FromArgb(255, 193, 196, 203) : Color.FromArgb(255, 84, 90, 98); break;
            }
            return new SyntaxInkBrush(color, dark);
        }
    }
}
