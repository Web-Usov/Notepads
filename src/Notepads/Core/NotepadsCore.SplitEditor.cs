// Core-owned split editor groups. XAML supplies the surfaces; the core owns
// document identity, selected editor, tab transfer and persisted group state.
namespace Notepads.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Notepads.Controls;
    using Notepads.Controls.TextEditor;
    using Windows.ApplicationModel.DataTransfer;
    using Windows.Devices.Input;
    using Windows.Foundation;
    using Notepads.Services;
    using Windows.UI.Xaml;
    using Windows.UI.Xaml.Input;
    using Windows.UI.Xaml.Media;

    public partial class NotepadsCore
    {
        private SetsView _secondarySets;
        private SetsView _activeSets;
        private EditorSplitOrientation _splitOrientation;
        private bool _isMergingGroups;
        private int _tabDropTargetGroup = -1;
        private FrameworkElement _tabTransferSurface;
        private ITextEditor _pressedTabEditor;
        private uint _pressedTabPointerId;
        private Point _pressedTabPosition;
        private bool _tabPressExceededDragThreshold;

        // UI-only feedback. Document ownership and tab moves remain core-owned.
        public event EventHandler<int> TabDropTargetChanged;
        public event EventHandler<string> TabTransferDiagnosticChanged;

        private void TraceTabTransfer(string stage) => TabTransferDiagnosticChanged?.Invoke(this, stage);

        // Native ListView handles reordering and regular cross-window drags.
        // This pointer-based fallback also supports cross-group transfers when
        // UWP does not initiate ListView's DragItemsStarting in a split titlebar.
        public void AttachTabTransferSurface(FrameworkElement surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (_secondarySets == null) throw new InvalidOperationException("Attach the second tab group first.");
            if (_tabTransferSurface != null) throw new InvalidOperationException("Transfer surface already attached.");
            _tabTransferSurface = surface;

            surface.AddHandler(UIElement.PointerMovedEvent,
                new PointerEventHandler(TabTransferSurface_PointerMoved), true);
            surface.AddHandler(UIElement.PointerReleasedEvent,
                new PointerEventHandler(TabTransferSurface_PointerReleased), true);
            surface.AddHandler(UIElement.PointerCanceledEvent,
                new PointerEventHandler(TabTransferSurface_PointerCanceled), true);
            surface.AddHandler(UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(TabTransferSurface_PointerCanceled), true);
        }

        private void TabHeader_PointerPressed(ITextEditor editor, PointerRoutedEventArgs args)
        {
            if (_tabTransferSurface == null || !IsSplitEditorEnabled ||
                args.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
            {
                return;
            }

            var pointer = args.GetCurrentPoint(_tabTransferSurface);
            if (!pointer.Properties.IsLeftButtonPressed) return;

            // The X button should never arm a drag gesture.
            var current = args.OriginalSource as DependencyObject;
            while (current != null && !(current is SetsViewItem))
            {
                if (current is Windows.UI.Xaml.Controls.Primitives.ButtonBase) return;
                current = VisualTreeHelper.GetParent(current);
            }
            if (current == null || FindEditorGroup(editor) == null) return;

            _pressedTabEditor = editor;
            _pressedTabPointerId = args.Pointer.PointerId;
            _pressedTabPosition = pointer.Position;
            _tabPressExceededDragThreshold = false;
            TraceTabTransfer("pointer down group " + GetEditorGroupIndex(editor));
        }

        private int GetTransferTargetGroup(Point point)
        {
            if (!IsSplitEditorEnabled || _tabTransferSurface == null) return -1;
            if (IsPointInsideGroup(_sets, point)) return 0;
            if (IsPointInsideGroup(_secondarySets, point)) return 1;
            return -1;
        }

        private bool IsPointInsideGroup(SetsView group, Point position)
        {
            if (group == null || group.Visibility != Visibility.Visible ||
                group.ActualWidth <= 0 || group.ActualHeight <= 0)
            {
                return false;
            }
            var origin = group.TransformToVisual(_tabTransferSurface).TransformPoint(new Point(0, 0));
            return position.X >= origin.X && position.X < origin.X + group.ActualWidth &&
                   position.Y >= origin.Y && position.Y < origin.Y + group.ActualHeight;
        }

        private void TabTransferSurface_PointerMoved(object sender, PointerRoutedEventArgs args)
        {
            if (_pressedTabEditor == null || args.Pointer.PointerId != _pressedTabPointerId) return;
            var current = args.GetCurrentPoint(_tabTransferSurface);
            if (!current.Properties.IsLeftButtonPressed)
            {
                ClearTabPointerDrag();
                return;
            }

            var pos = current.Position;
            var dx = pos.X - _pressedTabPosition.X;
            var dy = pos.Y - _pressedTabPosition.Y;
            if (!_tabPressExceededDragThreshold && dx * dx + dy * dy >= 64)
            {
                _tabPressExceededDragThreshold = true;
                TraceTabTransfer("pointer threshold crossed");
            }
            if (!_tabPressExceededDragThreshold) return;

            var target = GetTransferTargetGroup(pos);
            SetTabDropTarget(target >= 0 && target != GetEditorGroupIndex(_pressedTabEditor)
                ? target : -1);
        }

        private void TabTransferSurface_PointerReleased(object sender, PointerRoutedEventArgs args)
        {
            if (_pressedTabEditor == null || args.Pointer.PointerId != _pressedTabPointerId) return;
            var editor = _pressedTabEditor;
            var target = _tabPressExceededDragThreshold
                ? GetTransferTargetGroup(args.GetCurrentPoint(_tabTransferSurface).Position) : -1;
            ClearTabPointerDrag();
            if (target < 0 || target == GetEditorGroupIndex(editor)) return;
            try
            {
                TraceTabTransfer("pointer transfer to group " + target);
                MoveEditorToGroup(editor, target);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
            }
        }

        private void TabTransferSurface_PointerCanceled(object sender, PointerRoutedEventArgs args)
        {
            if (_pressedTabEditor != null && args.Pointer.PointerId == _pressedTabPointerId)
            {
                ClearTabPointerDrag();
            }
        }

        private void ClearTabPointerDrag()
        {
            _pressedTabEditor = null;
            _tabPressExceededDragThreshold = false;
            SetTabDropTarget(-1);
        }

        private void SetTabDropTarget(int targetGroup)
        {
            if (_tabDropTargetGroup == targetGroup) return;
            _tabDropTargetGroup = targetGroup;
            if (targetGroup >= 0)
            {
                LoggingService.LogInfo($"[TabTransfer] Hover destination group {targetGroup}.");
                TraceTabTransfer($"hover group {targetGroup}");
            }
            TabDropTargetChanged?.Invoke(this, targetGroup);
        }

        private bool TryGetDraggedEditor(DataPackageView view, out ITextEditor editor)
        {
            editor = null;
            if (view?.Properties == null ||
                !view.Properties.TryGetValue(NotepadsInstanceId, out object instanceId) ||
                !(instanceId is string sourceId) ||
                !string.Equals(sourceId, App.InstanceId.ToString(), StringComparison.Ordinal) ||
                !view.Properties.TryGetValue(NotepadsTextEditorGuid, out object tabId) ||
                !(tabId is string editorId) || !Guid.TryParse(editorId, out Guid id))
            {
                return false;
            }

            editor = GetAllTextEditors().FirstOrDefault(item => item.Id == id);
            return editor != null;
        }

        public event EventHandler SplitLayoutChanged;
        public event EventHandler<ITextEditor> ActiveTextEditorChanged;

        public EditorSplitOrientation SplitOrientation => _splitOrientation;
        public bool IsSplitEditorEnabled => _splitOrientation != EditorSplitOrientation.None;

        public void AttachSecondarySetsView(SetsView secondarySets)
        {
            if (secondarySets == null) throw new ArgumentNullException(nameof(secondarySets));
            if (_secondarySets != null) throw new InvalidOperationException("Secondary editor group is already attached.");
            if (ReferenceEquals(_sets, secondarySets)) throw new ArgumentException("Editor groups must be distinct.");
            _secondarySets = secondarySets;
            BindSetsView(_secondarySets);
        }

        private void BindSetsView(SetsView sets)
        {
            sets.SelectionChanged += SetsView_OnSelectionChanged;
            sets.Items.VectorChanged += SetsView_OnItemsChanged;
            sets.SetClosing += SetsView_OnSetClosing;
            sets.SetTapped += (sender, args) =>
            {
                ActivateEditorGroup(ReferenceEquals(sets, _secondarySets) ? 1 : 0);
                FocusOnTextEditor(args.Item as ITextEditor);
            };
            sets.SetDraggedOutside += Sets_SetDraggedOutside;
            sets.DragOver += Sets_DragOver;
            sets.DragLeave += Sets_DragLeave;
            sets.Drop += Sets_Drop;
            sets.DragItemsStarting += Sets_DragItemsStarting;
            sets.DragItemsCompleted += Sets_DragItemsCompleted;
        }

        private IEnumerable<SetsView> EnumerateEditorGroups()
        {
            yield return _sets;
            if (_secondarySets != null) yield return _secondarySets;
        }

        private SetsView FindEditorGroup(ITextEditor editor)
        {
            if (editor == null) return null;
            foreach (var sets in EnumerateEditorGroups())
            {
                if (sets.Items == null) continue;
                foreach (SetsViewItem item in sets.Items)
                {
                    if (ReferenceEquals(item.Content, editor)) return sets;
                }
            }
            return null;
        }

        public void ActivateEditorGroup(int group)
        {
            var sets = group == 1 && IsSplitEditorEnabled ? _secondarySets : _sets;
            if (sets == null) return;
            var previous = _selectedTextEditor;
            _activeSets = sets;
            _selectedTextEditor = (sets.SelectedItem as SetsViewItem)?.Content as ITextEditor;
            if (_selectedTextEditor != null && !ReferenceEquals(previous, _selectedTextEditor))
            {
                ActiveTextEditorChanged?.Invoke(this, _selectedTextEditor);
            }
        }

        private void TextEditor_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ITextEditor editor)
            {
                var sets = FindEditorGroup(editor);
                if (sets != null && ReferenceEquals((sets.SelectedItem as SetsViewItem)?.Content, editor))
                {
                    ActivateEditorGroup(ReferenceEquals(sets, _secondarySets) ? 1 : 0);
                }
            }
        }

        public void SetSplitEditorOrientation(EditorSplitOrientation orientation)
        {
            if (orientation == EditorSplitOrientation.None)
            {
                CloseSplitEditor();
                return;
            }
            if (_secondarySets == null || (orientation != EditorSplitOrientation.Right &&
                                           orientation != EditorSplitOrientation.Down)) return;
            if (_splitOrientation == orientation) return;
            _splitOrientation = orientation;
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SplitSelectedEditor(EditorSplitOrientation orientation)
        {
            if (orientation != EditorSplitOrientation.Right && orientation != EditorSplitOrientation.Down) return;
            if (_secondarySets == null || GetSelectedTextEditor() == null) return;
            try
            {
                if (IsSplitEditorEnabled)
                {
                    SetSplitEditorOrientation(orientation);
                    return;
                }

                var editor = GetSelectedTextEditor();
                // Keep the primary pane usable when splitting the only document.
                if (_sets.Items.Count == 1)
                {
                    _activeSets = _sets;
                    OpenNewTextEditor(_resourceLoader.GetString("TextEditor_DefaultNewFileName"));
                }
                SetSplitEditorOrientation(orientation);
                MoveEditorToGroup(editor, _secondarySets);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[{nameof(NotepadsCore)}] SplitSelectedEditor({orientation}) failed: {ex}");
                throw;
            }
        }

        // Stable two-group API for tab context menus and keyboard users.
        public int GetEditorGroupIndex(ITextEditor editor)
        {
            var group = FindEditorGroup(editor);
            if (group == null) return -1;
            return ReferenceEquals(group, _secondarySets) ? 1 : 0;
        }

        public void MoveEditorToGroup(ITextEditor editor, int targetGroup)
        {
            if (editor == null || !IsSplitEditorEnabled || _secondarySets == null ||
                (targetGroup != 0 && targetGroup != 1)) return;
            var target = targetGroup == 0 ? _sets : _secondarySets;
            if (FindEditorGroup(editor) == null || ReferenceEquals(FindEditorGroup(editor), target)) return;
            MoveEditorToGroup(editor, target);
        }

        public void MoveEditorToOtherGroup(ITextEditor editor)
        {
            var currentGroup = GetEditorGroupIndex(editor);
            if (currentGroup < 0) return;
            if (!IsSplitEditorEnabled)
            {
                SetSplitEditorOrientation(EditorSplitOrientation.Right);
            }
            MoveEditorToGroup(editor, currentGroup == 0 ? 1 : 0);
        }

        // Transfer the same editor (and its undo history). A fresh tab container
        // avoids reparenting a SetsViewItem across different ListViews.
        private void MoveEditorToGroup(ITextEditor editor, SetsView target, int atIndex = -1)
        {
            var source = FindEditorGroup(editor);
            if (source == null || target == null) return;
            if (ReferenceEquals(source, target))
            {
                SwitchTo(editor);
                return;
            }

            if (ReferenceEquals(source, _sets) && ReferenceEquals(target, _secondarySets) &&
                source.Items.Count == 1)
            {
                // Also cover dragging the last primary tab into the second group.
                var previousActive = _activeSets;
                _activeSets = _sets;
                OpenNewTextEditor(_resourceLoader.GetString("TextEditor_DefaultNewFileName"));
                _activeSets = previousActive;
            }

            var oldItem = GetTextEditorSetsViewItem(editor);
            var originalIndex = source.Items.IndexOf(oldItem);
            source.Items.Remove(oldItem);

            // A SetsViewItem is a ContentControl. Removing it from ListView.Items
            // does not release its Content reference. Reusing the editor as the
            // Content of a *second* item would throw XAML 0x800F1000:
            // "Element is already the child of another element."
            // Detach the existing visual owner before assigning the same editor
            // to a new tab container. The document and Undo history stay intact.
            oldItem.Content = null;

            SetsViewItem newItem = null;
            try
            {
                newItem = CreateTextEditorSetsViewItem(editor);
                if (atIndex < 0 || atIndex > target.Items.Count)
                {
                    target.Items.Add(newItem);
                }
                else
                {
                    target.Items.Insert(atIndex, newItem);
                }
                target.SelectedItem = newItem;
            }
            catch
            {
                // A failed transfer must not orphan an unsaved document. Return
                // the same editor to its original group before rethrowing.
                if (newItem != null)
                {
                    target.Items.Remove(newItem);
                    newItem.Content = null;
                    if (newItem.ContextFlyout is TabContextFlyout newFlyout) newFlyout.Dispose();
                }
                oldItem.Content = editor;
                source.Items.Insert(Math.Min(originalIndex, source.Items.Count), oldItem);
                source.SelectedItem = oldItem;
                ActivateEditorGroup(ReferenceEquals(source, _secondarySets) ? 1 : 0);
                throw;
            }
            if (oldItem.ContextFlyout is TabContextFlyout oldFlyout) oldFlyout.Dispose();
            ActivateEditorGroup(ReferenceEquals(target, _secondarySets) ? 1 : 0);

            if (!_isMergingGroups && IsSplitEditorEnabled && _secondarySets.Items.Count == 0)
            {
                CloseSplitEditor();
            }
        }

        public void CloseSplitEditor()
        {
            if (_secondarySets == null || !IsSplitEditorEnabled) return;
            _isMergingGroups = true;
            try
            {
                var editors = _secondarySets.Items.Cast<SetsViewItem>()
                    .Select(item => item.Content as ITextEditor)
                    .Where(editor => editor != null).ToArray();
                foreach (var editor in editors) MoveEditorToGroup(editor, _sets);
            }
            finally
            {
                _isMergingGroups = false;
            }
            _splitOrientation = EditorSplitOrientation.None;
            ClearTabPointerDrag();
            ActivateEditorGroup(0);
            SplitLayoutChanged?.Invoke(this, EventArgs.Empty);
            FocusOnSelectedTextEditor();
        }

        public IReadOnlyList<ITextEditor> GetEditorsInSameGroup(ITextEditor editor)
        {
            var sets = FindEditorGroup(editor);
            if (sets == null) return Array.Empty<ITextEditor>();
            return sets.Items.Cast<SetsViewItem>()
                .Select(item => item.Content as ITextEditor)
                .Where(item => item != null).ToArray();
        }

        public EditorSplitLayout GetSplitEditorLayout()
        {
            var layout = new EditorSplitLayout
            {
                Orientation = _splitOrientation,
                SelectedPrimaryEditorId = ((_sets.SelectedItem as SetsViewItem)?.Content as ITextEditor)?.Id ?? Guid.Empty,
                ActiveGroup = ReferenceEquals(_activeSets, _secondarySets) ? 1 : 0
            };
            if (IsSplitEditorEnabled && _secondarySets != null)
            {
                layout.SecondaryEditorIds = _secondarySets.Items.Cast<SetsViewItem>()
                    .Select(item => item.Content as ITextEditor)
                    .Where(editor => editor != null).Select(editor => editor.Id).ToList();
                layout.SelectedSecondaryEditorId =
                    ((_secondarySets.SelectedItem as SetsViewItem)?.Content as ITextEditor)?.Id ?? Guid.Empty;
            }
            return layout;
        }

        public void RestoreSplitEditorLayout(EditorSplitLayout saved)
        {
            if (_secondarySets == null || saved == null) return;
            var layout = saved.Normalize(GetAllTextEditors().Select(editor => editor.Id));
            if (layout.Orientation == EditorSplitOrientation.None) return;

            SetSplitEditorOrientation(layout.Orientation);
            _isMergingGroups = true;
            try
            {
                foreach (var id in layout.SecondaryEditorIds)
                {
                    var editor = GetAllTextEditors().FirstOrDefault(item => item.Id == id);
                    if (editor != null) MoveEditorToGroup(editor, _secondarySets);
                }
            }
            finally
            {
                _isMergingGroups = false;
            }

            var primary = GetAllTextEditors().FirstOrDefault(editor => editor.Id == layout.SelectedPrimaryEditorId);
            var secondary = GetAllTextEditors().FirstOrDefault(editor => editor.Id == layout.SelectedSecondaryEditorId);
            var primaryItem = GetTextEditorSetsViewItem(primary);
            var secondaryItem = GetTextEditorSetsViewItem(secondary);
            if (primaryItem != null && ReferenceEquals(FindEditorGroup(primary), _sets))
                _sets.SelectedItem = primaryItem;
            if (secondaryItem != null && ReferenceEquals(FindEditorGroup(secondary), _secondarySets))
                _secondarySets.SelectedItem = secondaryItem;
            ActivateEditorGroup(layout.ActiveGroup);
        }
    }
}
